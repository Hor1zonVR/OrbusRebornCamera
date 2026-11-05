using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Opt-in, capped sampling of the *real currently observed* skin bones
/// for up to four nearby animated actors, at five samples per second.
/// These are world-space render-bone observations, not NPC AI, material
/// snapshots, or offline reconstructed prefab objects.
/// Unity objects are only read on the main thread.
/// </summary>
internal sealed class WorldRigCapture
{
    internal const int MaxSimultaneousRigs = 4;
    private const double SamplePeriodSeconds = .2d;
    private const double FailedLookupRetrySeconds = 12d;
    private const int MaxSkinCandidates = 16;

    private sealed class RigSource
    {
        internal WorldRigDefinition Definition = new();
        internal Transform[] Bones = Array.Empty<Transform>();
    }

    private readonly Dictionary<string, RigSource> _sources =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _retryAfter =
        new(StringComparer.Ordinal);
    private ReplayRingBuffer<WorldRigSample>? _ring;
    private int _windowSeconds = 300;
    private double _nextSample;
    private int _processedThisTick;
    private int _rigsEverCaptured;
    internal int CapturedRigCount => _rigsEverCaptured;

    internal void Clear()
    {
        _ring?.Clear();
        _sources.Clear();
        _retryAfter.Clear();
        _nextSample = 0d;
        _processedThisTick = 0;
        _rigsEverCaptured = 0;
    }

    internal bool BeginTick(double seconds, int requestedWindow)
    {
        int window = Math.Max(30, Math.Min(600, requestedWindow));
        if (_ring == null || window != _windowSeconds)
        {
            WorldRigSample[] previous = _ring?.Snapshot(seconds) ??
                Array.Empty<WorldRigSample>();
            _windowSeconds = window;
            _ring = new ReplayRingBuffer<WorldRigSample>(
                window * 5 * MaxSimultaneousRigs + 8,
                window, item => item.Seconds);
            foreach (WorldRigSample old in previous)
                _ring.Add(old);
        }
        _ring.Trim(seconds);
        if (seconds < _nextSample) return false;
        _nextSample = seconds + SamplePeriodSeconds;
        _processedThisTick = 0;
        return true;
    }

    internal bool TryCapture(string key, string category,
        Transform actorRoot, double seconds)
    {
        if (_ring == null ||
            _processedThisTick >= MaxSimultaneousRigs ||
            actorRoot == null)
            return false;
        try
        {
            if (!_sources.TryGetValue(key, out RigSource? source))
            {
                if (_sources.Count >= WorldRigCodec.MaxRigs ||
                    (_retryAfter.TryGetValue(key, out double retry) &&
                     seconds < retry))
                    return false;
                source = FindRigSource(key, category, actorRoot);
                if (source == null)
                {
                    _retryAfter[key] = seconds + FailedLookupRetrySeconds;
                    return false;
                }
                _sources.Add(key, source);
                _retryAfter.Remove(key);
                _rigsEverCaptured++;
                Plugin.ModLogger.LogInfo("[WorldRigs] Found " + key +
                    " (" + source.Definition.MeshName + "), " +
                    source.Bones.Length + " real skin bones, category=" +
                    source.Definition.Category);
            }

            var bones = new WorldRigBonePose[source.Bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Transform joint = source.Bones[i];
                if (joint == null) return false;
                Vector3 p = joint.position;
                Quaternion q = joint.rotation;
                bones[i] = new WorldRigBonePose
                {
                    X = p.x, Y = p.y, Z = p.z,
                    Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w
                };
            }
            _ring.Add(new WorldRigSample
            {
                ActorKey = key, Seconds = seconds, Bones = bones
            });
            _processedThisTick++;
            return true;
        }
        catch (Exception ex)
        {
            // Destroyed Unity wrappers and teleports may invalidate a
            // cached skin; clear just this actor and retry at low frequency.
            _sources.Remove(key);
            _retryAfter[key] = seconds + FailedLookupRetrySeconds;
            Plugin.ModLogger.LogWarning("[WorldRigs] Skipped " + key +
                " rig sample: " + ex.GetType().Name);
            return false;
        }
    }

    private static RigSource? FindRigSource(string key, string category,
        Transform actorRoot)
    {
        // Search only beneath this actor's already-discovered root.
        var skins = actorRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int total = skins == null ? 0 :
            Math.Min(MaxSkinCandidates, skins.Length);
        SkinnedMeshRenderer? best = null;
        int bestBones = 0, bestVertices = 0;
        for (int i = 0; i < total; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (skin == null || skin.sharedMesh == null) continue;
            var bones = skin.bones;
            if (bones == null || bones.Length < 4 ||
                bones.Length > WorldRigCodec.MaxBones) continue;
            int verts = skin.sharedMesh.vertexCount;
            // Reuse the richest skin rig (e.g. 34-bone pet, 52-bone NPC).
            if (bones.Length > bestBones ||
                (bones.Length == bestBones && verts > bestVertices))
            {
                best = skin;
                bestBones = bones.Length;
                bestVertices = verts;
            }
        }
        if (best == null) return null;

        var source = new RigSource();
        var transforms = best.bones;
        if (transforms == null || transforms.Length != bestBones)
            return null;
        source.Bones = new Transform[bestBones];
        var names = new string[bestBones];
        var parents = new int[bestBones];
        var index = new Dictionary<int, int>();
        for (int i = 0; i < bestBones; i++)
        {
            Transform bone = transforms[i];
            if (bone == null) return null;
            source.Bones[i] = bone;
            names[i] = LimitedPath(bone, actorRoot);
            index[bone.GetInstanceID()] = i;
        }
        for (int i = 0; i < bestBones; i++)
        {
            Transform? parent = source.Bones[i].parent;
            int found = -1;
            for (int depth = 0; parent != null && depth < 20; depth++)
            {
                if (index.TryGetValue(parent.GetInstanceID(), out found))
                    break;
                parent = parent.parent;
                found = -1;
            }
            parents[i] = found;
        }
        source.Definition = new WorldRigDefinition
        {
            ActorKey = key,
            Category = WorldActorRecords.Safe(category),
            MeshName = WorldActorRecords.Safe(best.sharedMesh.name),
            BonePaths = names,
            ParentIndices = parents
        };
        return source;
    }

    private static string LimitedPath(Transform bone, Transform root)
    {
        var names = new List<string>(12);
        Transform? at = bone;
        for (int depth = 0; at != null && depth < 18; depth++)
        {
            names.Add(WorldActorRecords.Safe(at.name));
            if (at == root) break;
            at = at.parent;
        }
        names.Reverse();
        return WorldActorRecords.Safe(string.Join("/", names));
    }

    internal void Snapshot(double now, double first, double last,
        out WorldRigDefinition[] definitions, out WorldRigSample[] samples)
    {
        var kept = new List<WorldRigSample>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorldRigSample item in _ring?.Snapshot(now) ??
                 Array.Empty<WorldRigSample>())
        {
            if (item.Seconds < first || item.Seconds > last) continue;
            kept.Add(item);
            keys.Add(item.ActorKey);
        }
        var defs = new List<WorldRigDefinition>();
        foreach (var entry in _sources)
            if (keys.Contains(entry.Key))
                defs.Add(entry.Value.Definition);
        definitions = defs.ToArray();
        samples = kept.ToArray();
    }
}
