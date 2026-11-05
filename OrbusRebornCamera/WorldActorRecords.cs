using System;
using System.Globalization;
using System.IO;

namespace OrbusBetterMirror;

// An observation from the local Unity client, NOT authoritative server state.
// Keys are stable only for one recording process: Party Sync will later need
// a separate, verified identity and clock-alignment layer.
internal struct WorldActorObservation
{
    internal double Seconds;
    internal long UtcTicks;
    internal string ActorKey;
    internal string Category;
    internal string Prefab;
    internal string Zone;
    internal string Event; // appear, pose, leave
    internal float X, Y, Z, Qx, Qy, Qz, Qw;
    internal int AnimationHash;
    internal float AnimationTime;
}
internal sealed class WorldActorIdentity
{
    internal string ActorKey = "";
    internal string Category = "";
    internal string Prefab = "";
    internal string IdentitySource = "local Unity instance";
    // Display name deliberately unknown until a verified Orbus field is found.
    internal string DisplayName = "";
}

internal static class WorldActorRecords
{
    internal const int MaxActorIdentities = 128;
    internal const int MaxObservations = 180000;
    private static readonly CultureInfo Number = CultureInfo.InvariantCulture;

    internal static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string cleaned = value.Replace('\t', ' ').Replace('\r', ' ')
            .Replace('\n', ' ');
        return cleaned.Length > 100 ? cleaned.Substring(0, 100) : cleaned;
    }

    internal static void Write(TextWriter output, string sessionId,
        string clipId, WorldActorIdentity[] actors, WorldActorObservation[] data)
    {
        if (actors.Length > MaxActorIdentities ||
            data.Length > MaxObservations)
            throw new InvalidDataException("Actor capture exceeds safety limits.");

        output.WriteLine("# RebornCamera observed actor timeline v1");
        output.WriteLine("# session_id=" + Safe(sessionId));
        output.WriteLine("# clip_id=" + Safe(clipId));
        output.WriteLine("# timestamps=same clock as matching pose TSV");
        output.WriteLine("# identities=local instance only; NOT stable across game sessions");
        output.WriteLine("# player_names=not verified; blank by design");
        output.WriteLine("# npc_category=unverified visual/animation candidates");
        output.WriteLine("# events=appear pose leave; leave is no longer observed locally");
        output.WriteLine("# missing=world geometry, HP, damage, spells, audio, server actions");
        output.WriteLine("record\tactor_key\tcategory\tprefab\tidentity_source\tdisplay_name" +
            "\tseconds\tutc_ticks\tzone\tevent\tx\ty\tz\tqx\tqy\tqz\tqw" +
            "\tanim_state_hash\tanim_normalized_time");
        foreach (WorldActorIdentity actor in actors)
        {
            output.Write("actor\t");
            output.Write(Safe(actor.ActorKey)); output.Write('\t');
            output.Write(Safe(actor.Category)); output.Write('\t');
            output.Write(Safe(actor.Prefab)); output.Write('\t');
            output.Write(Safe(actor.IdentitySource)); output.Write('\t');
            output.Write(Safe(actor.DisplayName));
            output.WriteLine("\t\t\t\t\t\t\t\t\t\t\t\t");
        }
        foreach (WorldActorObservation item in data)
        {
            output.Write("sample\t"); output.Write(Safe(item.ActorKey));
            output.Write('\t'); output.Write(Safe(item.Category));
            output.Write('\t'); output.Write(Safe(item.Prefab));
            output.Write("\t\t\t");
            output.Write(item.Seconds.ToString("F4", Number)); output.Write('\t');
            output.Write(item.UtcTicks.ToString(Number)); output.Write('\t');
            output.Write(Safe(item.Zone)); output.Write('\t');
            output.Write(Safe(item.Event)); output.Write('\t');
            Append(output, item.X); Append(output, item.Y);
            Append(output, item.Z); Append(output, item.Qx);
            Append(output, item.Qy); Append(output, item.Qz);
            output.Write(item.Qw.ToString("R", Number)); output.Write('\t');
            output.Write(item.AnimationHash.ToString(Number)); output.Write('\t');
            output.WriteLine(item.AnimationTime.ToString("F5", Number));
        }
    }

    private static void Append(TextWriter writer, float value)
    {
        writer.Write(value.ToString("R", Number));
        writer.Write('\t');
    }
}

internal sealed class WorldActorTimeline
{
    private readonly Dictionary<string, WorldActorObservation[]> _tracks;
    internal int ActorCount => _tracks.Count;
    internal int ObservationCount { get; }

    private WorldActorTimeline(Dictionary<string, WorldActorObservation[]> tracks,
        int count)
    {
        _tracks = tracks;
        ObservationCount = count;
    }

    internal static WorldActorTimeline ReadFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Actor sidecar missing.", path);
        if (file.Length > 40L * 1024L * 1024L)
            throw new InvalidDataException("Actor sidecar too large.");
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    internal static WorldActorTimeline Read(TextReader reader)
    {
        var tracks = new Dictionary<string, List<WorldActorObservation>>(
            StringComparer.Ordinal);
        string? line;
        int total = 0;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] == '#' ||
                line.StartsWith("record\t", StringComparison.Ordinal))
                continue;
            string[] fields = line.Split('\t');
            if (fields[0] != "sample") continue;
            if (fields.Length < 19)
                throw new InvalidDataException("Incomplete actor observation.");
            if (++total > WorldActorRecords.MaxObservations)
                throw new InvalidDataException("Too many actor samples.");
            string key = fields[1];
            if (key.Length == 0 || key.Length > 96)
                throw new InvalidDataException("Bad actor key.");
            var item = new WorldActorObservation
            {
                ActorKey = key, Category = fields[2], Prefab = fields[3],
                Seconds = double.Parse(fields[6], CultureInfo.InvariantCulture),
                UtcTicks = long.Parse(fields[7], CultureInfo.InvariantCulture),
                Zone = fields[8], Event = fields[9],
                X = float.Parse(fields[10], CultureInfo.InvariantCulture),
                Y = float.Parse(fields[11], CultureInfo.InvariantCulture),
                Z = float.Parse(fields[12], CultureInfo.InvariantCulture),
                Qx = float.Parse(fields[13], CultureInfo.InvariantCulture),
                Qy = float.Parse(fields[14], CultureInfo.InvariantCulture),
                Qz = float.Parse(fields[15], CultureInfo.InvariantCulture),
                Qw = float.Parse(fields[16], CultureInfo.InvariantCulture),
                AnimationHash = int.Parse(fields[17], CultureInfo.InvariantCulture),
                AnimationTime = float.Parse(fields[18], CultureInfo.InvariantCulture)
            };
            if (double.IsNaN(item.Seconds) || double.IsInfinity(item.Seconds))
                throw new InvalidDataException("Bad actor sample timestamp.");
            if (!tracks.TryGetValue(key, out List<WorldActorObservation>? samples))
            {
                if (tracks.Count >= WorldActorRecords.MaxActorIdentities)
                    throw new InvalidDataException("Too many actor tracks.");
                samples = new List<WorldActorObservation>();
                tracks.Add(key, samples);
            }
            if (samples.Count > 0 && samples[samples.Count - 1].Seconds > item.Seconds)
                throw new InvalidDataException("Actor timestamps went backwards.");
            samples.Add(item);
        }
        var indexed = new Dictionary<string, WorldActorObservation[]>(StringComparer.Ordinal);
        foreach (var pair in tracks) indexed.Add(pair.Key, pair.Value.ToArray());
        return new WorldActorTimeline(indexed, total);
    }

    // Only show current, observed actors. Do not glide across a zone gap
    // or keep a frozen NPC around for the remainder of the dungeon.
    internal WorldActorObservation[] VisibleAt(double seconds)
    {
        var visible = new List<WorldActorObservation>(Math.Min(24, ActorCount));
        foreach (var track in _tracks.Values)
        {
            int lo = 0, hi = track.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (track[mid].Seconds <= seconds) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0) continue;
            WorldActorObservation prior = track[lo - 1];
            if (prior.Event == "leave" || seconds - prior.Seconds > .4d)
                continue;
            if (lo < track.Length)
            {
                WorldActorObservation next = track[lo];
                double dt = next.Seconds - prior.Seconds;
                double dx = next.X - prior.X;
                double dy = next.Y - prior.Y;
                double dz = next.Z - prior.Z;
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                bool suddenJump = dt > 0d && distance > 12d &&
                    distance / dt > 35d;
                bool zoneSwap = prior.Zone != next.Zone &&
                    prior.Zone != "(unknown)" &&
                    next.Zone != "(unknown)";
                if (dt > 0d && dt <= .3d && next.Event == "pose" &&
                    prior.Event != "leave" && !suddenJump && !zoneSwap)
                {
                    float t = (float)((seconds - prior.Seconds) / dt);
                    prior.X += (next.X - prior.X) * t;
                    prior.Y += (next.Y - prior.Y) * t;
                    prior.Z += (next.Z - prior.Z) * t;
                    // Orientation is held for this early marker-only viewer.
                }
            }
            visible.Add(prior);
        }
        return visible.ToArray();
    }
}
