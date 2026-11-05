using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OrbusBetterMirror;

// No Unity dependency: the same reader is used by the player and CI tests.
// File format v1 (27 columns) and v2 (appended zone/transition) are supported.
internal enum ReplayTransitionKind { None, Gap, Teleport, ZoneChange }

internal struct ReplayPoseValue
{
    internal bool Valid;
    internal float X, Y, Z, Qx, Qy, Qz, Qw;
}

internal sealed class ReplayClipFrame
{
    internal double Seconds;
    internal long UtcTicks;
    internal string Scene = "";
    internal string Zone = "(not recorded)";
    internal ReplayTransitionKind Transition;
    internal ReplayPoseValue Head, Left, Right;
}

internal sealed class ReplayClipData
{
    internal readonly ReplayClipFrame[] Frames;
    internal readonly string ClipId;
    internal readonly string SessionId;
    internal readonly string FileName;
    internal int TeleportCount { get; }
    internal int GapCount { get; }
    internal int ZoneChangeCount { get; }
    internal double FirstSeconds => Frames[0].Seconds;
    internal double Duration => Frames[Frames.Length - 1].Seconds - FirstSeconds;
    internal int Count => Frames.Length;

    internal ReplayClipData(ReplayClipFrame[] frames, string clipId,
        string sessionId, string fileName)
    {
        if (frames.Length == 0)
            throw new InvalidDataException("Replay has no pose frames.");
        Frames = frames;
        ClipId = clipId;
        SessionId = sessionId;
        FileName = fileName;
        foreach (ReplayClipFrame frame in frames)
        {
            if (frame.Transition == ReplayTransitionKind.Teleport) TeleportCount++;
            else if (frame.Transition == ReplayTransitionKind.Gap) GapCount++;
            else if (frame.Transition == ReplayTransitionKind.ZoneChange) ZoneChangeCount++;
        }
    }

    // Replay position is relative to the clip start (0 to Duration).
    // On a gap, zone change or teleport, hold the previous pose until the
    // next recorded sample, then snap. Never interpolate an unknown path.
    internal void Evaluate(double elapsed, out ReplayClipFrame a,
        out ReplayClipFrame b, out float blend)
    {
        double absolute = FirstSeconds + Math.Max(0d, Math.Min(Duration, elapsed));
        int lo = 0, hi = Frames.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Frames[mid].Seconds <= absolute) lo = mid + 1;
            else hi = mid;
        }
        int index = Math.Max(0, lo - 1);
        a = Frames[index];
        b = Frames[Math.Min(index + 1, Frames.Length - 1)];
        double seconds = b.Seconds - a.Seconds;
        if (seconds <= 0 || b.Transition != ReplayTransitionKind.None)
        {
            blend = 0f;
            return;
        }
        blend = (float)Math.Max(0d, Math.Min(1d,
            (absolute - a.Seconds) / seconds));
    }
}

internal static class ReplayClipReader
{
    private const int MaxSamples = 30000; // up to 10 minutes at 30 fps + margin
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static ReplayClipData ReadFile(string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists) throw new FileNotFoundException("Clip missing.", filePath);
        if (file.Length > 25L * 1024L * 1024L)
            throw new InvalidDataException("Clip larger than 25 MB; refusing to load.");
        using var reader = new StreamReader(filePath);
        return Read(reader, file.Name);
    }

    internal static ReplayClipData Read(TextReader source, string fileName)
    {
        var frames = new List<ReplayClipFrame>(9000);
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string clipId = "", sessionId = "";
        int lineNumber = 0;
        while (true)
        {
            string? line = source.ReadLine();
            if (line == null) break;
            lineNumber++;
            if (line.Length == 0) continue;
            if (line[0] == '#')
            {
                string metadata = line.Substring(1).Trim();
                if (metadata.StartsWith("clip_id=", StringComparison.Ordinal))
                    clipId = metadata.Substring("clip_id=".Length);
                else if (metadata.StartsWith("session_id=", StringComparison.Ordinal))
                    sessionId = metadata.Substring("session_id=".Length);
                continue;
            }
            string[] fields = line.Split('\t');
            if (headers.Count == 0)
            {
                for (int i = 0; i < fields.Length; i++)
                    headers[fields[i]] = i;
                if (!headers.ContainsKey("seconds") ||
                    !headers.ContainsKey("head_valid") ||
                    !headers.ContainsKey("right_qw"))
                    throw new InvalidDataException("Not a RebornCamera pose TSV.");
                continue;
            }
            if (frames.Count >= MaxSamples)
                throw new InvalidDataException("Clip exceeds 30,000-frame safety limit.");

            var frame = new ReplayClipFrame
            {
                Seconds = Number(fields, headers, "seconds"),
                UtcTicks = Long(fields, headers, "utc_ticks"),
                Scene = Text(fields, headers, "scene", ""),
                Zone = Text(fields, headers, "zone", "(not recorded)"),
                Head = Pose(fields, headers, "head"),
                Left = Pose(fields, headers, "left"),
                Right = Pose(fields, headers, "right")
            };
            if (double.IsInfinity(frame.Seconds) ||
                double.IsNaN(frame.Seconds) ||
                (frames.Count > 0 &&
                 frame.Seconds < frames[frames.Count - 1].Seconds))
                throw new InvalidDataException(
                    "Invalid or backwards timestamp at row " + lineNumber);

            string recordedKind = Text(fields, headers, "transition", "none");
            if (!Enum.TryParse(recordedKind, true,
                out ReplayTransitionKind declaredKind))
                declaredKind = ReplayTransitionKind.None;
            if (frames.Count > 0)
            {
                ReplayClipFrame prior = frames[frames.Count - 1];
                // Derive transitions for v1 clips as well as v2 exports.
                ReplayTransitionKind derived = DetectTransition(prior, frame);
                frame.Transition = derived != ReplayTransitionKind.None
                    ? derived : declaredKind;
            }
            frames.Add(frame);
        }
        return new ReplayClipData(frames.ToArray(), clipId, sessionId, fileName);
    }

    internal static ReplayTransitionKind DetectTransition(
        ReplayClipFrame previous, ReplayClipFrame current)
    {
        double dt = current.Seconds - previous.Seconds;
        if (dt <= 0) return ReplayTransitionKind.Gap;

        // Large, almost instantaneous relocations. Require both distance
        // and speed so normal fast locomotion is not tagged as a teleport.
        if (previous.Head.Valid && current.Head.Valid)
        {
            double dx = current.Head.X - previous.Head.X;
            double dy = current.Head.Y - previous.Head.Y;
            double dz = current.Head.Z - previous.Head.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > 12d && distance / dt > 35d)
                return ReplayTransitionKind.Teleport;
        }

        if (dt > .25d) return ReplayTransitionKind.Gap;
        if (!string.Equals(previous.Zone, current.Zone,
                StringComparison.Ordinal) &&
            previous.Zone != "(not recorded)" &&
            current.Zone != "(not recorded)")
            return ReplayTransitionKind.ZoneChange;
        return ReplayTransitionKind.None;
    }

    private static string Text(string[] data, Dictionary<string,int> columns,
        string name, string fallback)
    {
        return columns.TryGetValue(name, out int at) && at < data.Length
            ? data[at] : fallback;
    }

    private static double Number(string[] data, Dictionary<string,int> columns,
        string name)
    {
        if (!double.TryParse(Text(data, columns, name, ""),
            NumberStyles.Float, Invariant, out double value) ||
            double.IsNaN(value) || double.IsInfinity(value))
            throw new InvalidDataException("Invalid numeric column: " + name);
        return value;
    }

    private static long Long(string[] data, Dictionary<string,int> columns,
        string name)
    {
        if (!long.TryParse(Text(data, columns, name, "0"),
            NumberStyles.Integer, Invariant, out long value))
            throw new InvalidDataException("Invalid timestamp field: " + name);
        return value;
    }

    private static ReplayPoseValue Pose(string[] data,
        Dictionary<string,int> columns, string stem)
    {
        string value = Text(data, columns, stem + "_valid", "0");
        if (value != "1" && value != "0")
            throw new InvalidDataException("Invalid validity flag for " + stem);
        if (value == "0") return default;
        return new ReplayPoseValue
        {
            Valid = true,
            X = (float)Number(data, columns, stem + "_x"),
            Y = (float)Number(data, columns, stem + "_y"),
            Z = (float)Number(data, columns, stem + "_z"),
            Qx = (float)Number(data, columns, stem + "_qx"),
            Qy = (float)Number(data, columns, stem + "_qy"),
            Qz = (float)Number(data, columns, stem + "_qz"),
            Qw = (float)Number(data, columns, stem + "_qw")
        };
    }
}
