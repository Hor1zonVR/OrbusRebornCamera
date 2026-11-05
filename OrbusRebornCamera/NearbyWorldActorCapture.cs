using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Low-frequency physics-neighborhood discovery of already instantiated
/// characters and animated objects; no global scene scans or server APIs.
/// The recorded data is client OBSERVATION, not a physics/server simulation.
/// </summary>
internal sealed class NearbyWorldActorCapture
{
    private const float SurveyInterval = 1.35f;
    private const float SampleInterval = .10f;
    private const int MaxColliderChecks = 256;
    private const int MaxTrackedActors = 24;
    private const int MaxAncestry = 12;
    private readonly Dictionary<int, LiveActor> _actors = new();
    private readonly WorldRigCapture _rigs = new();
    private ReplayRingBuffer<WorldActorObservation>? _observations;
    private double _nextSurvey, _nextSample, _lastWarning, _nextWorldCopy;
    private int _sequence, _window = 300;
    private bool _hadCandidates;

    private sealed class LiveActor
    {
        internal int InstanceId;
        internal string Key = "";
        internal string Category = "";
        internal string Prefab = "";
        internal Transform? Root;
        internal Animator? Animator;
        internal double LastSeen;
        internal bool InRange;
        internal bool WasPresent;
    }

    internal int CurrentCandidates
    {
        get
        {
            int total = 0;
            foreach (LiveActor a in _actors.Values)
                if (a.InRange) total++;
            return total;
        }
    }

    internal void Clear()
    {
        _observations?.Clear();
        _actors.Clear();
        _rigs.Clear();
        _sequence = 0;
        _nextSurvey = _nextSample = _nextWorldCopy = 0d;
        _hadCandidates = false;
    }

    internal void Tick(Vector3 localPosition, double seconds, long utcTicks,
        string zone, int windowSeconds, float radiusMeters)
    {
        windowSeconds = Math.Clamp(windowSeconds, 30, 600);
        if (_observations == null || windowSeconds != _window)
        {
            // Preserve no unbounded history when buffer duration changes.
            WorldActorObservation[] retained =
                _observations?.Snapshot(seconds) ?? Array.Empty<WorldActorObservation>();
            _window = windowSeconds;
            _observations = new ReplayRingBuffer<WorldActorObservation>(
                windowSeconds * 10 * MaxTrackedActors + 8,
                windowSeconds, item => item.Seconds);
            foreach (WorldActorObservation item in retained)
                _observations.Add(item);
        }

        if (seconds >= _nextSurvey)
        {
            _nextSurvey = seconds + SurveyInterval;
            TrySurvey(localPosition, seconds, Math.Max(10f,
                Math.Min(50f, radiusMeters)), zone);
        }
        if (seconds < _nextSample) return;
        _nextSample = seconds + SampleInterval;

        foreach (LiveActor actor in _actors.Values)
        {
            if (!actor.InRange && !actor.WasPresent) continue;
            string eventKind = actor.InRange ?
                (actor.WasPresent ? "pose" : "appear") : "leave";
            if (!actor.InRange)
            {
                actor.WasPresent = false;
                continueWithLeave(actor, seconds, utcTicks, zone);
                continue;
            }
            if (actor.Root == null)
            {
                actor.InRange = false;
                actor.WasPresent = false;
                continue;
            }
            try
            {
                Vector3 p = actor.Root.position;
                Quaternion q = actor.Root.rotation;
                var observation = new WorldActorObservation
                {
                    Seconds = seconds, UtcTicks = utcTicks,
                    ActorKey = actor.Key, Category = actor.Category,
                    Prefab = actor.Prefab, Zone = zone, Event = eventKind,
                    X = p.x, Y = p.y, Z = p.z,
                    Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w
                };
                if (actor.Animator != null && actor.Animator.isActiveAndEnabled)
                {
                    // Animator states provide a first approximation, NOT
                    // complete NPC/hand/weapon animations.
                    try
                    {
                        var state = actor.Animator.GetCurrentAnimatorStateInfo(0);
                        observation.AnimationHash = state.fullPathHash;
                        observation.AnimationTime = state.normalizedTime;
                    }
                    catch (Exception) { /* animator can be uninitialized */ }
                }
                _observations!.Add(observation);
                actor.WasPresent = true;
            }
            catch (Exception)
            {
                actor.InRange = false;
                actor.WasPresent = false;
            }
        }

        // Run a second, lower-frequency pass for true skin-bone animation.
        // A maximum of four *nearby* visually animated actors are sampled
        // per pass; the existing 10Hz root/Animator timeline is unchanged.
        if (Plugin.RecordNearbyRigBones != null &&
            Plugin.RecordNearbyRigBones.Value)
        {
            if (_rigs.BeginTick(seconds, windowSeconds))
            {
                var near = new List<(LiveActor actor, float distance)>(24);
                foreach (LiveActor actor in _actors.Values)
                {
                    if (!actor.InRange || actor.Root == null) continue;
                    try
                    {
                        near.Add((actor,
                            Vector3.Distance(actor.Root.position, localPosition)));
                    }
                    catch (Exception) { /* possible zone/despawn */ }
                }
                near.Sort((a, b) => a.distance.CompareTo(b.distance));
                int recorded = 0;
                foreach (var item in near)
                {
                    if (_rigs.TryCapture(item.actor.Key, item.actor.Category,
                        item.actor.Root!, seconds))
                        recorded++;
                    if (recorded >= WorldRigCapture.MaxSimultaneousRigs)
                        break;
                }
            }
        }
        else
        {
            _rigs.Clear();
        }
    }

    private void continueWithLeave(LiveActor actor, double seconds,
        long utcTicks, string zone)
    {
        // Mark missing actors explicitly: editors must not interpolate an
        // actor straight through an unloaded zone or long disappearance.
        _observations!.Add(new WorldActorObservation
        {
            Seconds = seconds, UtcTicks = utcTicks,
            ActorKey = actor.Key, Category = actor.Category,
            Prefab = actor.Prefab, Zone = zone, Event = "leave"
        });
    }

    private void TrySurvey(Vector3 origin, double now, float radius,
        string zone)
    {
        try
        {
            // Use the game's own nearby colliders. This cannot see remote
            // areas or NPCs without nearby colliders; record that limitation.
            var colliders = Physics.OverlapSphere(origin, radius);
            foreach (LiveActor actor in _actors.Values)
                actor.InRange = false;
            int inspected = Math.Min(MaxColliderChecks,
                colliders == null ? 0 : colliders.Length);

            for (int i = 0; i < inspected; i++)
            {
                var hit = colliders[i];
                if (hit == null) continue;
                if (!FindActor(hit.transform, out Transform? root,
                    out string category, out Animator? animator))
                    continue;
                if (root == null || root.gameObject == null ||
                    !root.gameObject.activeInHierarchy ||
                    Vector3.Distance(root.position, origin) > radius + 3f)
                    continue;
                int id = root.GetInstanceID();
                if (!_actors.TryGetValue(id, out LiveActor? actor) ||
                    actor.Root != root)
                {
                    if (_actors.Count >= MaxTrackedActors)
                    {
                        PruneExpired(now);
                        if (_actors.Count >= MaxTrackedActors) continue;
                    }
                    actor = new LiveActor
                    {
                        InstanceId = id, Key = "actor-" + (++_sequence),
                        Category = category,
                        Prefab = WorldActorRecords.Safe(root.name),
                        Root = root, Animator = animator
                    };
                    _actors[id] = actor;
                }
                actor.InRange = true;
                actor.LastSeen = now;
                if (actor.Animator == null) actor.Animator = animator;
            }

            // Opt-in: clone a few nearby static scene MESHES, never an
            // entire scene, physics body, renderer script or NPC AI.
            if (Plugin.CacheNearbyWorldGeometry != null &&
                Plugin.CacheNearbyWorldGeometry.Value &&
                now >= _nextWorldCopy)
            {
                _nextWorldCopy = now + 3.5d;
                // First inspect known Highsteppe static scene hierarchies
                // without requiring a nearby collider on every wall/floor.
                // Then add small collision-adjacent props as before.
                ReplayVisualAssets.CacheHighsteppeStructure(
                    ReplayBufferRecorder.CurrentSessionId,
                    origin, Math.Min(35f, radius), zone);
                ReplayVisualAssets.CacheNearbyWorld(
                    ReplayBufferRecorder.CurrentSessionId,
                    colliders, origin, Math.Min(25f, radius), zone);
            }

            if (!_hadCandidates && CurrentCandidates > 0)
            {
                _hadCandidates = true;
                Plugin.ModLogger.LogInfo(
                    "[WorldActors] First nearby actor candidates detected (" +
                    CurrentCandidates + "). Identity is session-local.");
            }
        }
        catch (Exception ex)
        {
            // A failed physics query must NOT mark every existing actor gone.
            if (now - _lastWarning > 10d)
            {
                _lastWarning = now;
                Plugin.ModLogger.LogWarning(
                    "[WorldActors] Nearby query unavailable: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    private void PruneExpired(double now)
    {
        var removable = new List<int>();
        foreach (var pair in _actors)
            if (!pair.Value.WasPresent && !pair.Value.InRange &&
                now - pair.Value.LastSeen > SurveyInterval * 1.5f)
                removable.Add(pair.Key);
        foreach (int id in removable) _actors.Remove(id);
    }

    private static bool FindActor(Transform? start,
        out Transform? root, out string category, out Animator? animator)
    {
        root = null;
        category = "";
        animator = null;
        Transform? candidate = null;
        Animator? candidateAnimator = null;
        Transform? walk = start;
        for (int depth = 0; walk != null && depth < MaxAncestry; depth++)
        {
            string name = walk.name ?? "";
            if (name == "PlayerRigv4" || name.StartsWith(
                "PlayerRigv4(", StringComparison.Ordinal))
                return false; // Already captured as the local character.
            if (name.StartsWith("OtherPlayerv2(", StringComparison.Ordinal) ||
                name == "OtherPlayerv2")
            {
                root = walk; category = "remote_player_candidate";
                // Some player animators are children; it's safe to omit
                // state hash rather than search the entire rig per frame.
                animator = candidateAnimator;
                return true;
            }

            if (candidateAnimator == null)
            {
                // One component lookup per ancestor of a nearby collider.
                var found = walk.GetComponent<Animator>();
                if (found != null)
                {
                    candidate = walk;
                    candidateAnimator = found;
                }
            }
            walk = walk.parent;
        }

        if (candidate == null) return false;
        root = candidate;
        animator = candidateAnimator;
        category = "animated_world_candidate";
        // We deliberately do not call this an NPC until object
        // responsibilities are confirmed against the IL2CPP dumps.
        return true;
    }

    // Targeted, bounded diagnostic at the moment Save Clip is pressed.
    // Only actor roots already discovered near this local player are visited.
    // A manifest identifies potential meshes; it does NOT persist mesh bytes,
    // clothing appearance, enemy AI state, or exact animation poses.
    internal string CaptureManifest()
    {
        var sb = new System.Text.StringBuilder(8000);
        sb.AppendLine("# RebornCamera nearby world actor visual manifest v1");
        sb.AppendLine("# actor identities are local to this game process");
        sb.AppendLine("# only actors currently near player are inspected");
        sb.AppendLine("# player names, mesh bytes, animations, voice NOT captured");
        foreach (LiveActor actor in _actors.Values)
        {
            if (!actor.InRange || actor.Root == null) continue;
            try
            {
                sb.AppendLine("actor_key=" + WorldActorRecords.Safe(actor.Key));
                sb.AppendLine("actor_category=" +
                    WorldActorRecords.Safe(actor.Category));
                sb.AppendLine("actor_prefab=" +
                    WorldActorRecords.Safe(actor.Prefab));
                var skins = actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int count = skins == null ? 0 : Math.Min(8, skins.Length);
                sb.AppendLine("skinned_renderers=" +
                    (skins == null ? 0 : skins.Length));
                for (int i = 0; i < count; i++)
                {
                    var skin = skins[i];
                    if (skin == null) continue;
                    var mesh = skin.sharedMesh;
                    sb.AppendLine("mesh_" + i + "=" +
                        WorldActorRecords.Safe(mesh == null ? "(none)" : mesh.name));
                    sb.AppendLine("mesh_" + i + "_vertex_count=" +
                        (mesh == null ? 0 : mesh.vertexCount));
                    var bones = skin.bones;
                    sb.AppendLine("mesh_" + i + "_bones=" +
                        (bones == null ? 0 : bones.Length));
                    var materials = skin.sharedMaterials;
                    int m = materials == null ? 0 : Math.Min(4, materials.Length);
                    for (int j = 0; j < m; j++)
                        sb.AppendLine("mesh_" + i + "_material_" + j + "=" +
                            WorldActorRecords.Safe(materials[j] == null ?
                                "(none)" : materials[j].name));
                }
                if (skins != null && skins.Length > count)
                    sb.AppendLine("additional_meshes_omitted=true");
                sb.AppendLine("end_actor=" + actor.Key);
            }
            catch (Exception ex)
            {
                sb.AppendLine("inspection_error=" +
                    WorldActorRecords.Safe(ex.GetType().Name));
            }
        }
        return sb.ToString();
    }

    internal void SnapshotRigs(double now, double firstSeconds,
        double lastSeconds, out WorldRigDefinition[] definitions,
        out WorldRigSample[] samples) =>
        _rigs.Snapshot(now, firstSeconds, lastSeconds,
            out definitions, out samples);

    internal void Snapshot(double now, double firstSeconds,
        double lastSeconds, out WorldActorIdentity[] identities,
        out WorldActorObservation[] samples)
    {
        var selected = new List<WorldActorObservation>();
        foreach (WorldActorObservation observation in
                 _observations?.Snapshot(now) ?? Array.Empty<WorldActorObservation>())
        {
            if (observation.Seconds < firstSeconds ||
                observation.Seconds > lastSeconds) continue;
            selected.Add(observation);
        }

        // Derive metadata from the saved samples, not just currently
        // loaded actors. A disconnected/despawned actor can be evicted
        // from the live dictionary without losing its older footage.
        var actors = new List<WorldActorIdentity>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorldActorObservation item in selected)
        {
            if (!included.Add(item.ActorKey)) continue;
            actors.Add(new WorldActorIdentity
            {
                ActorKey = item.ActorKey, Category = item.Category,
                Prefab = item.Prefab,
                IdentitySource = "session-local actor ID",
                DisplayName = "" // Nameplate API still unverified.
            });
        }
        identities = actors.ToArray();
        samples = selected.ToArray();
    }
}
