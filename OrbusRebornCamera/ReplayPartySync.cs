using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OrbusBetterMirror;

/// <summary>
/// Optional transport for recorder files. No Unity objects, game networking,
/// login tokens, nor scene state on worker threads. All callbacks to the
/// recorder are invoked in Tick on the Unity main thread.
/// A separate consent-based HTTPS relay hosts the private room.
/// </summary>
internal sealed class ReplayPartySync : IDisposable
{
    private const long MaxClipBytes = 100L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _room;
    private readonly string _participant;
    private readonly string _imports;
    private readonly HashSet<string> _uploaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _uploading = new(StringComparer.Ordinal);
    private readonly List<(string path, Task<bool> task)> _uploads = new();
    private readonly List<Task<bool>> _saveCommands = new();
    private readonly long _joinedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private Task<SyncPoll>? _poll;
    private DateTime _nextPollUtc = DateTime.MinValue;
    private DateTime _nextRetryUtc = DateTime.MinValue;
    private DateTime _lastRemoteSaveUtc = DateTime.MinValue;
    private long _eventCursor;
    private bool _disposed;

    internal string Status { get; private set; } = "Connecting to private party relay...";
    internal int ReceivedCount { get; private set; }
    internal bool IsEnabled => !_disposed;
    internal Action? RemoteSaveRequested;
    internal Action<string>? ReceivedClip;

    private sealed class SyncPoll
    {
        internal long Cursor;
        internal bool ShouldSave;
        internal readonly List<string> Downloaded = new();
        internal int PartnerCount;
    }

    private ReplayPartySync(string endpoint, string room,
        string secret, string participant, string imports)
    {
        if (room.Length != 32 || !room.All(Uri.IsHexDigit) ||
            secret.Length != 64 || !secret.All(Uri.IsHexDigit) ||
            participant.Length != 32 || !participant.All(Uri.IsHexDigit))
            throw new InvalidDataException(
                "Party room, secret or local recorder ID is invalid.");

        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute,
            out Uri? address) || (address.Scheme != Uri.UriSchemeHttps &&
            !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)))
            throw new InvalidDataException(
                "Party relay requires HTTPS, except for localhost testing.");

        _room = new Uri(address, "v1/rooms/" + room + "/");
        _participant = participant.ToLowerInvariant();
        _imports = imports;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", secret.ToLowerInvariant());
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RebornCamera-PartySync/1");
    }

    internal static ReplayPartySync? Create(string endpoint,
        string room, string secret, string participant, string imports,
        Action<string> warn)
    {
        try { return new ReplayPartySync(endpoint, room, secret, participant, imports); }
        catch (Exception ex)
        {
            // Never print room tokens, Authorization headers or full URL.
            warn("Party Sync disabled: " + ex.Message);
            return null;
        }
    }

    private Uri Api(string relative) => new(_room, relative);

    private static string SafeClipId(string file)
    {
        string name = Path.GetFileName(file);
        if (!name.EndsWith(".party.zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Not a party archive.");
        string id = name.Substring(0, name.Length - ".party.zip".Length);
        if (!id.StartsWith("RC_Clip_", StringComparison.Ordinal) ||
            id.Length > 64 || id.Length < 16 ||
            !id.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new InvalidDataException("Invalid clip identity.");
        return id;
    }

    internal void QueueUpload(string packagePath)
    {
        if (_disposed || !File.Exists(packagePath)) return;
        string id;
        try { id = SafeClipId(packagePath); }
        catch { return; }
        if (_uploaded.Contains(id) || !_uploading.Add(id)) return;
        // Network file streaming runs on a worker. This never blocks VR.
        Task<bool> upload = Task.Run(() => UploadAsync(packagePath, id));
        _uploads.Add((packagePath, upload));
    }

    internal void QueueRecentClips(string clipDir)
    {
        if (!Directory.Exists(clipDir)) return;
        // Resend unsent saved clips on startup/reconnection. Never delete
        // local source files; server uploads are idempotent by clip ID.
        foreach (string path in Directory.GetFiles(clipDir, "RC_Clip_*.party.zip")
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(12))
            QueueUpload(path);
    }

    private async Task<bool> UploadAsync(string archive, string clipId)
    {
        try
        {
            var file = new FileInfo(archive);
            if (file.Length < 1 || file.Length > MaxClipBytes) return false;
            await using FileStream stream = new(archive, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            using var request = new HttpRequestMessage(HttpMethod.Put,
                Api("clips/" + _participant + "/" + clipId));
            request.Content = new StreamContent(stream, 65536);
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/zip");
            using HttpResponseMessage response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) { return false; }
    }

    internal void RequestPartnerSave()
    {
        if (_disposed) return;
        string payload = JsonSerializer.Serialize(new
        {
            from = _participant,
            request_id = Guid.NewGuid().ToString("N"),
            action = "save-last"
        });
        _saveCommands.Add(Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Api("events"));
                request.Content = new StringContent(payload,
                    Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }));
    }

    private async Task<SyncPoll> PollAsync(long cursor)
    {
        var result = new SyncPoll { Cursor = cursor };
        try
        {
            using var ev = await _http.GetAsync(Api("events?after=" + cursor));
            ev.EnsureSuccessStatusCode();
            using JsonDocument events = JsonDocument.Parse(
                await ev.Content.ReadAsStringAsync());
            foreach (JsonElement item in events.RootElement.GetProperty("events")
                .EnumerateArray())
            {
                long seq = item.GetProperty("seq").GetInt64();
                result.Cursor = Math.Max(result.Cursor, seq);
                if (item.GetProperty("from").GetString() == _participant) continue;
                if (item.GetProperty("action").GetString() != "save-last") continue;
                long created = item.GetProperty("created_ms").GetInt64();
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                // An old event should not trigger an unexpected clip export
                // after a player reconnects hours later.
                if (created >= _joinedMs - 10000 &&
                    created >= now - 60000 && created <= now + 10000)
                    result.ShouldSave = true;
            }

            using var index = await _http.GetAsync(Api("clips"));
            index.EnsureSuccessStatusCode();
            using JsonDocument clips = JsonDocument.Parse(
                await index.Content.ReadAsStringAsync());
            foreach (JsonElement clip in clips.RootElement.GetProperty("clips")
                .EnumerateArray())
            {
                string sender = clip.GetProperty("participant").GetString() ?? "";
                string id = clip.GetProperty("clip_id").GetString() ?? "";
                if (sender == _participant || sender.Length != 32 ||
                    !sender.All(Uri.IsHexDigit)) continue;
                if (!id.StartsWith("RC_Clip_", StringComparison.Ordinal) ||
                    id.Length > 64 || !id.All(c =>
                        char.IsLetterOrDigit(c) || c == '_')) continue;
                result.PartnerCount++;
                string folder = Path.Combine(_imports, sender);
                string destination = Path.Combine(folder, id + ".party.zip");
                if (File.Exists(destination)) continue;
                // Only download up to 3 on each polling pass; a huge backlog
                // cannot monopolize the connection or consume unbounded disk.
                if (result.Downloaded.Count >= 3) break;
                Directory.CreateDirectory(folder);
                string partial = destination + ".partial";
                try
                {
                    using var response = await _http.GetAsync(
                        Api("clips/" + sender + "/" + id),
                        HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > MaxClipBytes)
                        throw new InvalidDataException("Partner clip too large.");
                    await using (Stream source = await response.Content.ReadAsStreamAsync())
                    await using (var output = new FileStream(partial,
                        FileMode.Create, FileAccess.Write, FileShare.None,
                        65536, useAsync: true))
                    {
                        byte[] buffer = new byte[65536];
                        long total = 0;
                        int count;
                        while ((count = await source.ReadAsync(buffer,
                            0, buffer.Length)) > 0)
                        {
                            total += count;
                            if (total > MaxClipBytes)
                                throw new InvalidDataException("Partner clip too large.");
                            await output.WriteAsync(buffer, 0, count);
                        }
                        if (total == 0)
                            throw new InvalidDataException("Empty partner clip.");
                    }
                    File.Move(partial, destination);
                    result.Downloaded.Add(destination);
                }
                catch (Exception)
                {
                    try { if (File.Exists(partial)) File.Delete(partial); }
                    catch { }
                }
            }
            return result;
        }
        catch (Exception)
        {
            throw new IOException("Party relay unavailable.");
        }
    }

    /// <summary>Main-thread-only drain of completed work; no waiting.</summary>
    internal void Tick()
    {
        if (_disposed) return;
        for (int i = _saveCommands.Count - 1; i >= 0; i--)
        {
            if (!_saveCommands[i].IsCompleted) continue;
            try
            {
                if (!_saveCommands[i].GetAwaiter().GetResult())
                    Status = "Partner save request not delivered (will need a retry)";
            }
            catch { Status = "Partner save request failed"; }
            _saveCommands.RemoveAt(i);
        }

        for (int i = _uploads.Count - 1; i >= 0; i--)
        {
            var job = _uploads[i];
            if (!job.task.IsCompleted) continue;
            _uploading.Remove(SafeClipId(job.path));
            bool ok = false;
            try { ok = job.task.GetAwaiter().GetResult(); }
            catch { }
            if (ok)
            {
                _uploaded.Add(SafeClipId(job.path));
                Status = "Saved replay automatically synced";
            }
            else Status = "Relay upload interrupted; retrying later";
            _uploads.RemoveAt(i);
        }

        if (_poll != null && _poll.IsCompleted)
        {
            try
            {
                SyncPoll result = _poll.GetAwaiter().GetResult();
                _eventCursor = result.Cursor;
                foreach (string file in result.Downloaded)
                {
                    ReceivedCount++;
                    ReceivedClip?.Invoke(file);
                }
                if (result.ShouldSave &&
                    DateTime.UtcNow - _lastRemoteSaveUtc > TimeSpan.FromSeconds(15))
                {
                    _lastRemoteSaveUtc = DateTime.UtcNow;
                    RemoteSaveRequested?.Invoke();
                }
                if (result.Downloaded.Count > 0)
                    Status = "Partner recording downloaded automatically";
                else if (_uploads.Count == 0)
                    Status = "Connected to private party relay";
            }
            catch { Status = "Relay temporarily unavailable; retrying"; }
            _poll = null;
        }

        DateTime nowUtc = DateTime.UtcNow;
        if (nowUtc >= _nextRetryUtc)
        {
            _nextRetryUtc = nowUtc.AddSeconds(45);
            string folder = Path.Combine(BepInEx.Paths.ConfigPath,
                "OrbusRebornCamera", "Clips");
            QueueRecentClips(folder);
        }
        if (_poll == null && nowUtc >= _nextPollUtc)
        {
            _nextPollUtc = nowUtc.AddSeconds(5);
            _poll = Task.Run(() => PollAsync(_eventCursor));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        // Do not terminate in-progress workers: they may be finishing an
        // already saved replay upload during application shutdown.
        // Client disposal after worker completion is deferred to process end.
    }
}
