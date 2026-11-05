using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Real materials/meshes displayed through newly-created render-only Unity
/// components. NEVER instantiate gameplay prefabs or copy their scripts.
/// The asset cache is valid only within the SAME running Orbus process.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private sealed class RigVisual
    {
        internal GameObject Root = null!;
        internal Transform[] Bones = Array.Empty<Transform>();
    }

    private sealed class LocalPartVisual
    {
        internal GameObject Object = null!;
        internal ReplayVisualAssets.AvatarPart Asset = null!;
    }

    private sealed class StaticWorldVisual
    {
        internal GameObject Object = null!;
        internal ReplayVisualAssets.WorldPart Asset = null!;
    }

    private GameObject? _offlineVisualRoot;
    private readonly Dictionary<string, RigVisual> _offlineRigVisuals =
        new(StringComparer.Ordinal);
    private readonly List<LocalPartVisual> _offlineLocalVisuals = new();
    private readonly List<StaticWorldVisual> _offlineWorldVisuals = new();
    private bool _showCachedWorld = true;
    private bool _showObservedModels;
    private bool _visualCacheUnavailableLogged;
    private string _builtVisualClipId = "";
    private int _visualRigCount;

    private void ClearOfflineVisuals()
    {
        try
        {
            if (_offlineVisualRoot != null)
                UnityEngine.Object.Destroy(_offlineVisualRoot);
        }
        catch { /* scene unload / shutdown */ }
        _offlineVisualRoot = null;
        _offlineRigVisuals.Clear();
        _offlineLocalVisuals.Clear();
        _offlineWorldVisuals.Clear();
        _builtVisualClipId = "";
        _visualRigCount = 0;
    }

    private void BuildOfflineVisuals()
    {
        ClearOfflineVisuals();
        if (!_offlineReplayActive || _replayClip == null) return;
        if (ReplayVisualAssets.SessionId != _replayClip.SessionId)
        {
            if (!_visualCacheUnavailableLogged)
            {
                _visualCacheUnavailableLogged = true;
                Plugin.ModLogger.LogInfo(
                    "[VisualCache] This clip has no same-process assets. " +
                    "Restarted-game recordings still show bones/axes. " +
                    "Disk asset loading is NOT implemented yet.");
            }
            return;
        }

        _offlineVisualRoot = new GameObject("RC Render-Only Offline Models");
        _offlineVisualRoot.layer = ReplayVisualAssets.ReplayLayer;
        UnityEngine.Object.DontDestroyOnLoad(_offlineVisualRoot);

        foreach (ReplayVisualAssets.AvatarPart part in
                 ReplayVisualAssets.GetLocalParts(_replayClip.SessionId))
        {
            try
            {
                GameObject obj = ReplayVisualAssets.CreateStaticPart(
                    "Avatar " + part.Name, part.Mesh, part.Materials);
                obj.transform.SetParent(_offlineVisualRoot.transform, false);
                _offlineLocalVisuals.Add(new LocalPartVisual
                {
                    Object = obj, Asset = part
                });
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[VisualCache] Skipped local avatar part: " +
                    ex.GetType().Name);
            }
        }

        // Only visual geometry from this very game process is available.
        // It consists of MeshFilter/Renderer COPIES, with no colliders,
        // scripts, NPC AI or network components attached.
        foreach (ReplayVisualAssets.WorldPart world in
                 ReplayVisualAssets.GetWorldParts(_replayClip.SessionId))
        {
            try
            {
                GameObject obj = ReplayVisualAssets.CreateStaticPart(
                    "World " + world.Name, world.Mesh, world.Materials);
                obj.transform.SetParent(_offlineVisualRoot.transform, false);
                obj.transform.position = world.Position;
                obj.transform.rotation = world.Rotation;
                obj.transform.localScale = world.Scale;
                _offlineWorldVisuals.Add(new StaticWorldVisual
                {
                    Object = obj, Asset = world
                });
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[WorldVisuals] Static mesh proxy failed: " +
                    ex.GetType().Name);
            }
        }

        // Each actor's renderable mesh is used ONLY if this clip has a
        // matching bone topology, in-game asset, and source session ID.
        if (_worldRigTimeline != null && _worldActorTimeline != null)
        {
            foreach (WorldActorObservation observed in
                     _worldActorTimeline.VisibleAt(_replayClip.FirstSeconds))
            {
                // Older/currently visible frames may not include all actors;
                // proxy creation is also supported lazily while replay runs.
                EnsureRigVisual(observed.ActorKey);
            }
        }
        _builtVisualClipId = _replayClip.ClipId;
        Plugin.ModLogger.LogInfo("[VisualCache] Offline models available: " +
            _offlineLocalVisuals.Count + " avatar pieces, " +
            _offlineRigVisuals.Count + " animated actor meshes, " +
            _offlineWorldVisuals.Count + " static world meshes. " +
            "Same-process cache, not portable geometry.");
        UpdateOfflineVisuals();
    }

    private void EnsureRigVisual(string actorKey)
    {
        if (_offlineVisualRoot == null || _replayClip == null ||
            _worldRigTimeline == null ||
            _offlineRigVisuals.ContainsKey(actorKey) ||
            _offlineRigVisuals.Count >= 12)
            return;
        if (!ReplayVisualAssets.TryGetRig(_replayClip.SessionId,
            actorKey, out ReplayVisualAssets.RigAsset? asset) ||
            asset == null) return;
        if (!_worldRigTimeline.TryAt(actorKey,
            _replayClip.FirstSeconds + _replayElapsed,
            out WorldRigDefinition? definition, out _, out _, out _) ||
            definition == null || definition.BoneCount != asset.BoneCount)
            return;

        GameObject? root = null;
        try
        {
            root = new GameObject("RC Recorded " + actorKey);
            root.layer = ReplayVisualAssets.ReplayLayer;
            root.transform.SetParent(_offlineVisualRoot.transform, false);
            var boneTransforms = new Transform[definition.BoneCount];
            for (int i = 0; i < boneTransforms.Length; i++)
            {
                GameObject joint = new GameObject("RC Bone " + i);
                joint.layer = ReplayVisualAssets.ReplayLayer;
                joint.transform.SetParent(root.transform, false);
                boneTransforms[i] = joint.transform;
            }
            ReplayVisualAssets.CreateSkinRenderer(root, asset, boneTransforms);
            _offlineRigVisuals.Add(actorKey, new RigVisual
            {
                Root = root, Bones = boneTransforms
            });
            _visualRigCount++;
            Plugin.ModLogger.LogInfo("[VisualCache] Created render-only " +
                asset.MeshName + " proxy for " + actorKey);
        }
        catch (Exception ex)
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            Plugin.ModLogger.LogWarning("[VisualCache] Could not make " +
                actorKey + " visual proxy: " + ex);
        }
    }

    private void UpdateOfflineVisuals()
    {
        if (!_offlineReplayActive || _replayClip == null) return;
        if (_builtVisualClipId != _replayClip.ClipId &&
            _offlineVisualRoot == null)
        {
            // Build only once per loaded clip; a missing session cache
            // cannot be conjured by repeatedly rebuilding every frame.
            if (ReplayVisualAssets.SessionId == _replayClip.SessionId)
                BuildOfflineVisuals();
        }

        ReplayPoseValue currentHead = EvaluatedPose(0);
        _replayClip.Evaluate(_replayElapsed,
            out ReplayClipFrame currentFrame, out _, out _);
        foreach (StaticWorldVisual world in _offlineWorldVisuals)
        {
            // Keep the cached tile in its ORIGINAL world coordinates;
            // never follow the camera or bridge unrelated zones.
            bool near = currentHead.Valid && Vector3.Distance(
                world.Asset.Position, ReplayPosition(currentHead)) < 85f;
            bool zoneMatches = string.Equals(world.Asset.Zone,
                currentFrame.Zone, StringComparison.Ordinal);
            world.Object.SetActive(_showCachedWorld && near && zoneMatches);
        }

        bool visible = _showObservedActors && _showObservedModels;
        foreach (LocalPartVisual part in _offlineLocalVisuals)
        {
            ReplayPoseValue pose = EvaluatedPose(part.Asset.PoseChannel);
            bool available = visible && pose.Valid;
            part.Object.SetActive(available);
            if (!available) continue;
            Vector3 position = ReplayPosition(pose);
            Quaternion rotation = ReplayRotation(pose);
            part.Object.transform.position =
                position + rotation * part.Asset.Offset;
            part.Object.transform.rotation = rotation * part.Asset.Rotation;
            part.Object.transform.localScale = part.Asset.Scale;
        }

        foreach (RigVisual v in _offlineRigVisuals.Values)
            v.Root.SetActive(false);
        if (!visible || _worldActorTimeline == null ||
            _worldRigTimeline == null || _offlineVisualRoot == null) return;

        double at = _replayClip.FirstSeconds + _replayElapsed;
        foreach (WorldActorObservation actor in
                 _worldActorTimeline.VisibleAt(at))
        {
            EnsureRigVisual(actor.ActorKey);
            if (!_offlineRigVisuals.TryGetValue(actor.ActorKey,
                out RigVisual? visual)) continue;
            if (!_worldRigTimeline.TryAt(actor.ActorKey, at,
                out _, out WorldRigSample? a,
                out WorldRigSample? b, out float blend) || a == null)
                continue;

            visual.Root.SetActive(true);
            visual.Root.transform.position =
                new Vector3(actor.X, actor.Y, actor.Z);
            visual.Root.transform.rotation =
                new Quaternion(actor.Qx, actor.Qy, actor.Qz, actor.Qw);
            for (int i = 0; i < visual.Bones.Length &&
                 i < a.Bones.Length; i++)
            {
                WorldRigBonePose first = a.Bones[i];
                Vector3 p = new Vector3(first.X, first.Y, first.Z);
                Quaternion q = new Quaternion(first.Qx, first.Qy,
                    first.Qz, first.Qw);
                if (b != null && i < b.Bones.Length)
                {
                    WorldRigBonePose second = b.Bones[i];
                    p = Vector3.Lerp(p, new Vector3(second.X,
                        second.Y, second.Z), blend);
                    q = Quaternion.Slerp(q, new Quaternion(
                        second.Qx, second.Qy, second.Qz,
                        second.Qw), blend);
                }
                visual.Bones[i].position = p;
                visual.Bones[i].rotation = q;
            }
        }
    }
}
