using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Session-local COPY of rendered assets already observed by the client.
/// Live gameplay objects are never reused as replay objects. No world
/// loading, network use, GameObject cloning or component copying happens.
/// This cache is *not* disk-persistent and cannot hydrate old sessions.
/// </summary>
internal static class ReplayVisualAssets
{
    internal const int ReplayLayer = 29;
    internal const int MaxRigAssets = 12;
    internal const int MaxLocalParts = 24;
    private const int MaxMeshVertices = 30000;
    internal sealed class RigAsset
    {
        internal string ActorKey = "";
        internal string MeshName = "";
        internal Mesh Mesh = null!;
        internal Material[] Materials = Array.Empty<Material>();
        internal Vector3 RendererOffset;
        internal Quaternion RendererRotation;
        internal Vector3 RendererScale;
        internal Bounds LocalBounds;
        internal int RootBoneIndex;
        internal int BoneCount;
    }
    internal sealed class AvatarPart
    {
        internal int PoseChannel;
        internal string Name = "";
        internal Mesh Mesh = null!;
        internal Material[] Materials = Array.Empty<Material>();
        internal Vector3 Offset;
        internal Quaternion Rotation;
        internal Vector3 Scale;
    }

    private static readonly Dictionary<string, RigAsset> RigAssets =
        new(StringComparer.Ordinal);
    private static readonly List<AvatarPart> AvatarParts = new();
    private static string _sessionId = "";
    internal static string SessionId => _sessionId;
    internal static int RigCount => RigAssets.Count;
    internal static int AvatarPartCount => AvatarParts.Count;

    private static void StartSession(string id)
    {
        if (_sessionId == id) return;
        // Never delete game-owned mesh/materials. Only remove our cloned
        // cache assets when a genuinely new recording session begins.
        Clear();
        _sessionId = id;
    }

    internal static void Clear()
    {
        foreach (RigAsset asset in RigAssets.Values)
        {
            DestroyCopy(asset.Mesh);
            foreach (Material mat in asset.Materials) DestroyCopy(mat);
        }
        foreach (AvatarPart part in AvatarParts)
        {
            DestroyCopy(part.Mesh);
            foreach (Material mat in part.Materials) DestroyCopy(mat);
        }
        RigAssets.Clear();
        AvatarParts.Clear();
        _sessionId = "";
    }

    private static void DestroyCopy(UnityEngine.Object? value)
    {
        try { if (value != null) UnityEngine.Object.Destroy(value); }
        catch { /* Unity may be shutting down */ }
    }

    private static Material[] CopyMaterials(Renderer source)
    {
        var originals = source.sharedMaterials;
        int n = originals == null ? 0 : Math.Min(8, originals.Length);
        var copies = new Material[n];
        for (int i = 0; i < n; i++)
        {
            var original = originals[i];
            if (original == null) continue;
            // Copy, not reference, the original game material. Textures are
            // still referenced as game assets (not saved to our clip).
            copies[i] = new Material(original);
            copies[i].hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        return copies;
    }

    private static Il2CppReferenceArray<Material> AsMaterials(Material[] mats)
    {
        var a = new Il2CppReferenceArray<Material>(mats.Length);
        for (int i = 0; i < mats.Length; i++) a[i] = mats[i];
        return a;
    }

    internal static bool TryCacheRig(string sessionId, string key,
        SkinnedMeshRenderer source, Transform actorRoot, int boneCount)
    {
        if (string.IsNullOrEmpty(sessionId) || source == null ||
            actorRoot == null || boneCount < 4) return false;
        StartSession(sessionId);
        if (RigAssets.ContainsKey(key)) return true;
        if (RigAssets.Count >= MaxRigAssets) return false;
        Mesh? mesh = null;
        Material[] materials = Array.Empty<Material>();
        try
        {
            Mesh original = source.sharedMesh;
            if (original == null || original.vertexCount < 1 ||
                original.vertexCount > MaxMeshVertices) return false;
            mesh = UnityEngine.Object.Instantiate(original);
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            materials = CopyMaterials(source);

            var rootBone = source.rootBone;
            var bones = source.bones;
            int rootIndex = 0;
            if (rootBone != null && bones != null)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] == rootBone)
                    {
                        rootIndex = i; break;
                    }
            Quaternion inverse = Quaternion.Inverse(actorRoot.rotation);
            RigAssets.Add(key, new RigAsset
            {
                ActorKey = key, MeshName = original.name,
                Mesh = mesh, Materials = materials,
                RendererOffset = inverse *
                    (source.transform.position - actorRoot.position),
                RendererRotation = inverse * source.transform.rotation,
                RendererScale = source.transform.lossyScale,
                LocalBounds = source.localBounds,
                RootBoneIndex = rootIndex, BoneCount = boneCount
            });
            Plugin.ModLogger.LogInfo("[VisualCache] Copied " + key +
                " mesh=" + original.name + " bones=" + boneCount +
                " (same-process offline replay only)");
            return true;
        }
        catch (Exception ex)
        {
            DestroyCopy(mesh);
            foreach (Material m in materials) DestroyCopy(m);
            Plugin.ModLogger.LogWarning("[VisualCache] Rig copy failed: " +
                ex.GetType().Name);
            return false;
        }
    }

    internal static bool TryGetRig(string sessionId, string actorKey,
        out RigAsset? asset)
    {
        asset = null;
        return _sessionId == sessionId &&
            RigAssets.TryGetValue(actorKey, out asset);
    }

    private static Transform? FindLocalRig(Transform? from)
    {
        for (int i = 0; from != null && i < 15; i++, from = from.parent)
            if (from.name == "PlayerRigv4" ||
                from.name.StartsWith("PlayerRigv4(", StringComparison.Ordinal))
                return from;
        return null;
    }

    internal static void TryCacheLocalAvatar(string sessionId, Camera eye)
    {
        if (string.IsNullOrEmpty(sessionId) || eye == null) return;
        StartSession(sessionId);
        if (AvatarParts.Count > 0) return;
        Transform? rig = FindLocalRig(eye.transform);
        if (rig == null) return;

        // Read only precisely known paths, not whole-scene components.
        var target = new (string path, int channel)[]
        {
            ("Avatar/Torso/BodyParent", 3),
            ("Avatar/Torso/ShoulderParent", 3),
            ("Camera (eye)/Head/headPivot", 5),
            ("Controller (left)/Grabber/Hand/model", 6),
            ("Controller (right)/Grabber/Hand/model", 7)
        };
        foreach (var item in target)
        {
            Transform? anchor = rig.Find(item.path);
            if (anchor == null) continue;
            try
            {
                var skins = anchor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int n = skins == null ? 0 : Math.Min(12, skins.Length);
                for (int i = 0; i < n && AvatarParts.Count < MaxLocalParts; i++)
                    CacheAvatarPart(item.channel, anchor, skins[i], null);
                var filters = anchor.GetComponentsInChildren<MeshFilter>(true);
                n = filters == null ? 0 : Math.Min(12, filters.Length);
                for (int i = 0; i < n && AvatarParts.Count < MaxLocalParts; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter == null) continue;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        CacheAvatarPart(item.channel, anchor, renderer, filter.sharedMesh);
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[VisualCache] Local model part lookup: " +
                    ex.GetType().Name);
            }
        }
        if (AvatarParts.Count > 0)
            Plugin.ModLogger.LogInfo("[VisualCache] Copied " +
                AvatarParts.Count + " actual local avatar mesh parts.");
    }

    private static void CacheAvatarPart(int channel, Transform anchor,
        Renderer renderer, Mesh? provided)
    {
        if (renderer == null || !renderer.enabled ||
            !renderer.gameObject.activeInHierarchy) return;
        Mesh? mesh = null;
        Material[] materials = Array.Empty<Material>();
        try
        {
            SkinnedMeshRenderer? skin = renderer as SkinnedMeshRenderer;
            Mesh? original = skin == null ? provided : skin.sharedMesh;
            if (original == null || original.vertexCount < 1 ||
                original.vertexCount > MaxMeshVertices) return;
            if (skin != null && skin.bones != null && skin.bones.Length > 0)
            {
                // Captures the current skinned appearance for fingers etc.
                // Finger animation is NOT in the v4 pose clip yet.
                mesh = new Mesh();
                skin.BakeMesh(mesh);
                if (mesh.vertexCount < 1)
                {
                    DestroyCopy(mesh);
                    mesh = UnityEngine.Object.Instantiate(original);
                }
            }
            else mesh = UnityEngine.Object.Instantiate(original);
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            materials = CopyMaterials(renderer);
            Quaternion inverse = Quaternion.Inverse(anchor.rotation);
            AvatarParts.Add(new AvatarPart
            {
                PoseChannel = channel,
                Name = original.name, Mesh = mesh, Materials = materials,
                Offset = inverse *
                    (renderer.transform.position - anchor.position),
                Rotation = inverse * renderer.transform.rotation,
                Scale = renderer.transform.lossyScale
            });
        }
        catch
        {
            DestroyCopy(mesh);
            foreach (Material m in materials) DestroyCopy(m);
        }
    }

    internal static AvatarPart[] GetLocalParts(string sessionId) =>
        _sessionId == sessionId ? AvatarParts.ToArray() :
            Array.Empty<AvatarPart>();

    internal static GameObject CreateStaticPart(string name, Mesh mesh,
        Material[] mats)
    {
        GameObject obj = new GameObject("RC Offline " + name);
        obj.layer = ReplayLayer;
        var filter = obj.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = AsMaterials(mats);
        return obj;
    }

    internal static SkinnedMeshRenderer CreateSkinRenderer(GameObject root,
        RigAsset asset, Transform[] bones)
    {
        GameObject model = new GameObject("RC " + asset.MeshName);
        model.layer = ReplayLayer;
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = asset.RendererOffset;
        model.transform.localRotation = asset.RendererRotation;
        model.transform.localScale = asset.RendererScale;
        var renderer = model.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = asset.Mesh;
        renderer.sharedMaterials = AsMaterials(asset.Materials);
        var refs = new Il2CppReferenceArray<Transform>(bones.Length);
        for (int i = 0; i < bones.Length; i++) refs[i] = bones[i];
        renderer.bones = refs;
        renderer.rootBone = bones[Math.Max(0,
            Math.Min(bones.Length - 1, asset.RootBoneIndex))];
        renderer.localBounds = asset.LocalBounds;
        renderer.updateWhenOffscreen = true;
        return renderer;
    }
}
