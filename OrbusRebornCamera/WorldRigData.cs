using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrbusBetterMirror;

/// <summary>
/// Versioned binary sidecar containing *observed* world-space skin-bone poses.
/// Mesh assets, textures, AI behaviour and audio are NOT serialized here.
/// No Unity dependency: read/write and timeline safety are CI tested.
/// </summary>
internal struct WorldRigBonePose
{
    internal float X, Y, Z, Qx, Qy, Qz, Qw;
}
internal sealed class WorldRigDefinition
{
    internal string ActorKey = "";
    internal string Category = "";
    internal string MeshName = "";
    internal string[] BonePaths = Array.Empty<string>();
    internal int[] ParentIndices = Array.Empty<int>();
    internal int BoneCount => BonePaths.Length;
}
internal sealed class WorldRigSample
{
    internal string ActorKey = "";
    internal double Seconds;
    internal WorldRigBonePose[] Bones = Array.Empty<WorldRigBonePose>();
}
internal static class WorldRigCodec
{
    internal const int MaxRigs = 128;
    internal const int MaxBones = 72;
    internal const int MaxSamples = 24000; // 4 nearby rigs @5Hz for 20 minutes
    internal const int MaxFileBytes = 50 * 1024 * 1024;
    private const string Magic = "OrbusRebornCamera_ObservedRigs_v1";
    private const int Version = 1;

    internal static void Write(Stream destination, string sessionId,
        string clipId, WorldRigDefinition[] definitions,
        WorldRigSample[] samples)
    {
        if (definitions.Length > MaxRigs || samples.Length > MaxSamples)
            throw new InvalidDataException("Rig capture is above safety limits.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorldRigDefinition def in definitions)
        {
            ValidateDefinition(def);
            if (!names.Add(def.ActorKey))
                throw new InvalidDataException("Duplicate rig actor key.");
        }
        foreach (WorldRigSample sample in samples)
        {
            if (!names.Contains(sample.ActorKey))
                throw new InvalidDataException("Rig sample without definition.");
            if (sample.Bones.Length > MaxBones ||
                double.IsNaN(sample.Seconds) ||
                double.IsInfinity(sample.Seconds))
                throw new InvalidDataException("Invalid rig sample.");
        }

        using var writer = new BinaryWriter(destination,
            new UTF8Encoding(false), leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(Clean(sessionId));
        writer.Write(Clean(clipId));
        writer.Write(definitions.Length);
        foreach (WorldRigDefinition def in definitions)
        {
            writer.Write(Clean(def.ActorKey));
            writer.Write(Clean(def.Category));
            writer.Write(Clean(def.MeshName));
            writer.Write(def.BoneCount);
            for (int j = 0; j < def.BoneCount; j++)
            {
                writer.Write(Clean(def.BonePaths[j]));
                writer.Write(def.ParentIndices[j]);
            }
        }
        writer.Write(samples.Length);
        foreach (WorldRigSample sample in samples)
        {
            writer.Write(Clean(sample.ActorKey));
            writer.Write(sample.Seconds);
            writer.Write(sample.Bones.Length);
            foreach (WorldRigBonePose b in sample.Bones)
            {
                writer.Write(b.X); writer.Write(b.Y); writer.Write(b.Z);
                writer.Write(b.Qx); writer.Write(b.Qy);
                writer.Write(b.Qz); writer.Write(b.Qw);
            }
        }
        writer.Flush();
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string safe = value.Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\t', ' ');
        return safe.Length > 140 ? safe.Substring(0, 140) : safe;
    }

    private static void ValidateDefinition(WorldRigDefinition def)
    {
        if (string.IsNullOrEmpty(def.ActorKey) ||
            def.ActorKey.Length > 96 ||
            def.BoneCount < 1 || def.BoneCount > MaxBones ||
            def.ParentIndices.Length != def.BoneCount)
            throw new InvalidDataException("Invalid rig definition.");
        for (int i = 0; i < def.BoneCount; i++)
            if (def.ParentIndices[i] >= def.BoneCount ||
                def.ParentIndices[i] < -1 ||
                def.ParentIndices[i] == i)
                throw new InvalidDataException("Invalid bone parent index.");
    }

    internal static WorldRigTimeline Read(Stream source)
    {
        if (!source.CanRead) throw new InvalidDataException("Not a readable rig file.");
        if (source.CanSeek && source.Length > MaxFileBytes)
            throw new InvalidDataException("Rig file over 50 MiB limit.");
        try
        {
            using var reader = new BinaryReader(source, Encoding.UTF8,
                leaveOpen: true);
            if (reader.ReadString() != Magic ||
                reader.ReadInt32() != Version)
                throw new InvalidDataException("Wrong RC rig file/version.");
            string sessionId = reader.ReadString();
            string clipId = reader.ReadString();
            int nDefs = reader.ReadInt32();
            if (nDefs < 0 || nDefs > MaxRigs)
                throw new InvalidDataException("Invalid rig count.");
            var defs = new Dictionary<string, WorldRigDefinition>(
                StringComparer.Ordinal);
            for (int i = 0; i < nDefs; i++)
            {
                var def = new WorldRigDefinition
                {
                    ActorKey = reader.ReadString(),
                    Category = reader.ReadString(),
                    MeshName = reader.ReadString()
                };
                int n = reader.ReadInt32();
                if (n < 1 || n > MaxBones)
                    throw new InvalidDataException("Invalid bone count.");
                def.BonePaths = new string[n];
                def.ParentIndices = new int[n];
                for (int j = 0; j < n; j++)
                {
                    def.BonePaths[j] = reader.ReadString();
                    def.ParentIndices[j] = reader.ReadInt32();
                }
                ValidateDefinition(def);
                if (!defs.TryAdd(def.ActorKey, def))
                    throw new InvalidDataException("Duplicate rig definition.");
            }
            int nSamples = reader.ReadInt32();
            if (nSamples < 0 || nSamples > MaxSamples)
                throw new InvalidDataException("Invalid sample count.");
            var tracks = new Dictionary<string, List<WorldRigSample>>(
                StringComparer.Ordinal);
            for (int i = 0; i < nSamples; i++)
            {
                var item = new WorldRigSample
                {
                    ActorKey = reader.ReadString(),
                    Seconds = reader.ReadDouble()
                };
                if (!defs.TryGetValue(item.ActorKey,
                        out WorldRigDefinition? def) ||
                    !double.IsFinite(item.Seconds))
                    throw new InvalidDataException("Bad rig sample identity/time.");
                int n = reader.ReadInt32();
                if (n != def.BoneCount)
                    throw new InvalidDataException("Bone topology mismatch.");
                item.Bones = new WorldRigBonePose[n];
                for (int j = 0; j < n; j++)
                {
                    var bone = new WorldRigBonePose
                    {
                        X = reader.ReadSingle(), Y = reader.ReadSingle(),
                        Z = reader.ReadSingle(), Qx = reader.ReadSingle(),
                        Qy = reader.ReadSingle(), Qz = reader.ReadSingle(),
                        Qw = reader.ReadSingle()
                    };
                    if (!float.IsFinite(bone.X) || !float.IsFinite(bone.Y) ||
                        !float.IsFinite(bone.Z) || !float.IsFinite(bone.Qw))
                        throw new InvalidDataException("Bad rig bone data.");
                    item.Bones[j] = bone;
                }
                if (!tracks.TryGetValue(item.ActorKey,
                    out List<WorldRigSample>? list))
                {
                    list = new List<WorldRigSample>();
                    tracks.Add(item.ActorKey, list);
                }
                if (list.Count > 0 &&
                    item.Seconds < list[list.Count - 1].Seconds)
                    throw new InvalidDataException("Backwards rig timestamps.");
                list.Add(item);
            }
            var index = new Dictionary<string, WorldRigSample[]>(
                StringComparer.Ordinal);
            foreach (var p in tracks) index.Add(p.Key, p.Value.ToArray());
            return new WorldRigTimeline(sessionId, clipId, defs, index,
                nSamples);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Truncated rig sidecar.", ex);
        }
    }
}

internal sealed class WorldRigTimeline
{
    internal readonly string SessionId, ClipId;
    private readonly Dictionary<string, WorldRigDefinition> _definitions;
    private readonly Dictionary<string, WorldRigSample[]> _tracks;
    internal int RigCount => _definitions.Count;
    internal int SampleCount { get; }

    internal WorldRigTimeline(string sessionId, string clipId,
        Dictionary<string, WorldRigDefinition> defs,
        Dictionary<string, WorldRigSample[]> tracks, int sampleCount)
    {
        SessionId = sessionId;
        ClipId = clipId;
        _definitions = defs;
        _tracks = tracks;
        SampleCount = sampleCount;
    }

    internal bool TryAt(string key, double seconds,
        out WorldRigDefinition? definition, out WorldRigSample? before,
        out WorldRigSample? after, out float blend)
    {
        definition = null; before = null; after = null; blend = 0f;
        if (!_definitions.TryGetValue(key, out definition) ||
            !_tracks.TryGetValue(key, out WorldRigSample[]? frames) ||
            frames.Length == 0) return false;
        int lo = 0, hi = frames.Length;
        while (lo < hi)
        {
            int middle = lo + (hi - lo) / 2;
            if (frames[middle].Seconds <= seconds) lo = middle + 1;
            else hi = middle;
        }
        if (lo == 0) return false;
        WorldRigSample prev = frames[lo - 1];
        if (seconds - prev.Seconds > .36d) return false; // never freeze a skeleton
        before = prev;
        if (lo < frames.Length)
        {
            WorldRigSample next = frames[lo];
            double dt = next.Seconds - prev.Seconds;
            if (dt > 0 && dt <= .36d)
            {
                // Prevent interpolating a whole character across teleports.
                float dx = next.Bones[0].X - prev.Bones[0].X;
                float dy = next.Bones[0].Y - prev.Bones[0].Y;
                float dz = next.Bones[0].Z - prev.Bones[0].Z;
                float distance2 = dx * dx + dy * dy + dz * dz;
                if (distance2 < 144f || distance2 / (dt * dt) < 1225f)
                {
                    after = next;
                    blend = (float)((seconds - prev.Seconds) / dt);
                }
            }
        }
        return true;
    }
}
