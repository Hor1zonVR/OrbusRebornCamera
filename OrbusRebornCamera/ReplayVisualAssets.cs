using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    internal const int MaxWorldMeshes = 128;
    internal const int MaxTotalWorldVertices = 750000;
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
    internal sealed class WorldPart
    {
        internal string Name = "";
        internal string SourcePath = "";
        internal int VertexCount;
        internal string Zone = "";
        internal Mesh Mesh = null!;
        internal Material[] Materials = Array.Empty<Material>();
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 Scale;
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
    private static readonly HashSet<int> AvatarRendererIds = new();
    private static readonly List<WorldPart> WorldParts = new();
    private static readonly HashSet<int> WorldObjectIds = new();
    private static int _worldVertices;
    private static int _largeGroundMeshes;
    private static int _foliageMeshes;
    private static int _structuralMeshes;
    private static int _lodSkipped;
    private static readonly Queue<(Transform at, int depth)> StructuralQueue = new();
    private static readonly HashSet<int> StructuralVisited = new();
    private static string _structuralZone = "";
    private static bool _rootsSeeded;
    private static float _lastRootSurveyAt;
    private static Vector3 _lastRootSurveyOrigin;
    private static int _sceneRootCandidates;
    private static string _sessionId = "";
    internal static string SessionId => _sessionId;
    internal static int RigCount => RigAssets.Count;
    internal static int AvatarPartCount => AvatarParts.Count;
    internal static int WorldPartCount => WorldParts.Count;

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
        foreach (WorldPart part in WorldParts)
        {
            DestroyCopy(part.Mesh);
            foreach (Material mat in part.Materials) DestroyCopy(mat);
        }
        RigAssets.Clear();
        AvatarParts.Clear();
        AvatarRendererIds.Clear();
        WorldParts.Clear();
        WorldObjectIds.Clear();
        _worldVertices = 0;
        _largeGroundMeshes = 0;
        _foliageMeshes = 0;
        _structuralMeshes = 0;
        _lodSkipped = 0;
        StructuralQueue.Clear();
        StructuralVisited.Clear();
        _structuralZone = "";
        _rootsSeeded = false;
        _sceneRootCandidates = 0;
        _lastRootSurveyAt = 0f;
        _lastRootSurveyOrigin = default;
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
        if (AvatarParts.Count >= MaxLocalParts) return;
        Transform? rig = FindLocalRig(eye.transform);
        if (rig == null) return;

        // Read only precisely known paths, not whole-scene components.
        var target = new (string path, string posePath, int channel)[]
        {
            ("Avatar/Torso/BodyParent", "Avatar/Torso/BodyParent", 3),
            ("Avatar/Torso/ShoulderParent", "Avatar/Torso/BodyParent", 3),
            ("Camera (eye)/Head/headPivot", "Camera (eye)/Head", 5),
            ("Controller (left)/Grabber/Hand/model",
                "Controller (left)/Grabber/Hand/model", 6),
            ("Controller (right)/Grabber/Hand/model",
                "Controller (right)/Grabber/Hand/model", 7)
        };
        foreach (var item in target)
        {
            Transform? anchor = rig.Find(item.path);
            if (anchor == null) continue;
            // The recorded channels follow BodyParent/Head/model, NOT
            // the shoulder/headPivot search ancestors. Offsets must
            // always be stored relative to the RECORDED transform.
            Transform poseAnchor = rig.Find(item.posePath) ?? anchor;
            try
            {
                var skins = anchor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int n = skins == null ? 0 : Math.Min(12, skins.Length);
                for (int i = 0; i < n && AvatarParts.Count < MaxLocalParts; i++)
                    CacheAvatarPart(item.channel, poseAnchor, skins[i], null);
                var filters = anchor.GetComponentsInChildren<MeshFilter>(true);
                n = filters == null ? 0 : Math.Min(12, filters.Length);
                for (int i = 0; i < n && AvatarParts.Count < MaxLocalParts; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter == null) continue;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        CacheAvatarPart(item.channel, poseAnchor, renderer, filter.sharedMesh);
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
            !renderer.gameObject.activeInHierarchy ||
            AvatarParts.Count >= MaxLocalParts) return;
        int rendererId = renderer.GetInstanceID();
        if (AvatarRendererIds.Contains(rendererId)) return;
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
            AvatarRendererIds.Add(rendererId);
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

    // Captures only static MeshFilter + MeshRenderer geometry that already
    // has a nearby collider in the live client. No scene loading or
    // copying behaviours, NPC scripts, lighting components or terrain AI.
    // Survey only a few collider-parent subtrees. In Orbus, a building's
    // physical collider can sit on a parent whose renderable walls, doors
    // and props are CHILD objects. v0.9.26 missed those descendants.
    // All traversal is bounded and runs only every ~3.5 sec when opted in.
    internal static void CacheNearbyWorld(string sessionId,
        Collider[] colliders, Vector3 origin, float radius, string zone)
    {
        if (string.IsNullOrEmpty(sessionId) || colliders == null) return;
        // Never fill the limited cache with menu/character-select scenery
        // before reaching the real recorded destination.
        if (zone == "(unknown)" ||
            zone.Contains("CharacterSelect", StringComparison.OrdinalIgnoreCase))
            return;
        StartSession(sessionId);
        if (WorldParts.Count >= MaxWorldMeshes) return;

        int newThisPass = 0;
        int nodesExamined = 0;
        int inspected = Math.Min(colliders.Length, 256);
        var visited = new HashSet<int>();
        const int maxNodesPerSurvey = 220;
        const int maxCopiedPerSurvey = 8;

        for (int i = 0; i < inspected &&
             newThisPass < maxCopiedPerSurvey &&
             WorldParts.Count < MaxWorldMeshes &&
             nodesExamined < maxNodesPerSurvey; i++)
        {
            Collider hit = colliders[i];
            if (hit == null) continue;
            try
            {
                Transform collided = hit.transform;
                if (collided == null ||
                    Vector3.Distance(collided.position, origin) > radius + 22f)
                    continue;

                // A nearby building's mesh may be a sibling of its collider.
                // Start one parent up where possible; never traverse more
                // than 3 child levels or 220 total nodes across the survey.
                Transform? parent = collided.parent;
                Transform root = parent != null && parent.childCount <= 60
                    ? parent : collided;
                if (IsAnimatedOrPlayer(root)) continue;

                var queue = new Queue<(Transform at, int depth)>();
                queue.Enqueue((root, 0));
                while (queue.Count > 0 &&
                       nodesExamined < maxNodesPerSurvey &&
                       newThisPass < maxCopiedPerSurvey &&
                       WorldParts.Count < MaxWorldMeshes)
                {
                    var next = queue.Dequeue();
                    Transform at = next.at;
                    if (at == null) continue;
                    int transformId = at.GetInstanceID();
                    if (!visited.Add(transformId)) continue;
                    nodesExamined++;

                    if (IsAnimatedOrPlayer(at)) continue;
                    if (TryCopyStaticRenderer(at, origin, radius, zone))
                        newThisPass++;

                    if (next.depth >= 3) continue;
                    int childCount = at.childCount;
                    // Avoid iterating huge scene roots which might contain
                    // thousands of unrelated/distant world objects.
                    if (childCount > 60) continue;
                    for (int child = 0; child < childCount; child++)
                        queue.Enqueue((at.GetChild(child), next.depth + 1));
                }
            }
            catch (Exception)
            {
                // A despawned or streamed scene collider is not an error.
                // Do not abort the working pose/actor recording.
            }
        }

        if (newThisPass > 0)
            Plugin.ModLogger.LogInfo("[WorldVisuals] Cached " +
                WorldParts.Count + " static meshes (" +
                _worldVertices + " vertices); +" + newThisPass +
                " on this pass, " + nodesExamined +
                " nearby hierarchy nodes visited.");
    }

    private static bool IsAnimatedOrPlayer(Transform? node)
    {
        for (int i = 0; node != null && i < 8; i++, node = node.parent)
        {
            string name = node.name ?? "";
            if (name.StartsWith("PlayerRigv4", StringComparison.Ordinal) ||
                name.StartsWith("OtherPlayerv2", StringComparison.Ordinal))
                return true;
            if (node.GetComponent<Animator>() != null)
                return true;
        }
        return false;
    }

    private static string WorldHierarchyPath(Transform from)
    {
        var path = new List<string>(10);
        Transform? at = from;
        for (int i = 0; at != null && i < 10; i++, at = at.parent)
            path.Add(WorldActorRecords.Safe(at.name));
        path.Reverse();
        return WorldActorRecords.Safe(string.Join("/", path));
    }

    // Unity's scene hierarchy has colliders and renderers on different
    // branches. This second pass bypasses the collider requirement for
    // known Highsteppe static roots, but is deliberately incremental.
    // Names from the real .visuals.txt report are listed first;
    // additional root names are only probes, NOT assumed to exist.
    private static readonly string[] StructuralRootNames =
    {
        "CenterCourt", "FellowshipCourt", "UndergroundStatic", "Nate_HS",
        "HighsteppeStatic", "Highsteppe", "ZoneHighsteppe", "TownStatic"
    };

    internal static void CacheHighsteppeStructure(string sessionId,
        Vector3 origin, float radius, string zone)
    {
        if (string.IsNullOrEmpty(sessionId) ||
            !zone.Contains("Highsteppe", StringComparison.OrdinalIgnoreCase))
            return;
        StartSession(sessionId);
        bool movedFar = StructuralQueue.Count == 0 && _rootsSeeded &&
            Vector3.Distance(origin, _lastRootSurveyOrigin) > 25f;
        bool refresh = StructuralQueue.Count == 0 && _rootsSeeded &&
            Time.unscaledTime - _lastRootSurveyAt > 35f;
        if (zone != _structuralZone || movedFar || refresh)
        {
            StructuralQueue.Clear();
            StructuralVisited.Clear();
            _structuralZone = zone;
            _rootsSeeded = false;
        }
        if (!_rootsSeeded)
        {
            _rootsSeeded = true;
            _lastRootSurveyAt = Time.unscaledTime;
            _lastRootSurveyOrigin = origin;
            int found = 0;
            foreach (string name in StructuralRootNames)
            {
                try
                {
                    GameObject root = GameObject.Find(name);
                    if (root == null) continue;
                    StructuralQueue.Enqueue((root.transform, 0));
                    found++;
                }
                catch (Exception) { /* scene may be switching */ }
            }
            _sceneRootCandidates = TryAddRelevantSceneRoots();
            Plugin.ModLogger.LogInfo("[WorldStructure] Named Highsteppe " +
                "roots=" + found + ", matched loaded scene roots=" +
                _sceneRootCandidates + ", queue=" + StructuralQueue.Count +
                ". No full-scene child scan.");
        }

        int scanned = 0, copied = 0;
        const int MaxNodesPerPass = 160;
        const int MaxNewMeshesPerPass = 12;
        const int MaxQueuedNodes = 9000;
        while (StructuralQueue.Count > 0 && scanned < MaxNodesPerPass &&
               copied < MaxNewMeshesPerPass &&
               WorldParts.Count < MaxWorldMeshes)
        {
            var item = StructuralQueue.Dequeue();
            try
            {
                Transform at = item.at;
                if (at == null) continue;
                if (!StructuralVisited.Add(at.GetInstanceID())) continue;
                scanned++;
                if (IsAnimatedOrPlayer(at)) continue;

                if (TryCopyStaticRenderer(at, origin, radius, zone))
                    copied++;
                if (item.depth >= 11) continue;

                // Avoid a single huge root expanding into thousands of
                // potentially unrelated children in one frame.
                int children = at.childCount;
                if (children > 250 || StructuralQueue.Count + children >
                    MaxQueuedNodes) continue;
                for (int j = 0; j < children; j++)
                    StructuralQueue.Enqueue((at.GetChild(j), item.depth + 1));
            }
            catch (Exception) { /* world stream/despawn */ }
        }

        if (copied > 0)
            Plugin.ModLogger.LogInfo("[WorldStructure] Copied " + copied +
                " static meshes this pass, visited " + scanned +
                " nodes, queue=" + StructuralQueue.Count +
                ", total=" + WorldParts.Count + ". No gameplay objects copied.");
    }

    private static int TryAddRelevantSceneRoots()
    {
        // Scan only the top-level GameObjects of loaded scenes, not every
        // renderer/transform. Select likely architecture root containers
        // by their names. Their children are visited incrementally later.
        int selected = 0, checkedRoots = 0;
        try
        {
            int sceneCount = Math.Min(12, SceneManager.sceneCount);
            for (int i = 0; i < sceneCount && checkedRoots < 1200; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                int count = roots == null ? 0 :
                    Math.Min(roots.Length, 1200 - checkedRoots);
                for (int j = 0; j < count; j++)
                {
                    GameObject root = roots[j];
                    if (root == null) continue;
                    checkedRoots++;
                    string name = root.name ?? "";
                    if (name.Contains("Highsteppe", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("CenterCourt", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("FellowshipCourt", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Terrain", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Building", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Ground", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Wall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("TownStatic", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Nate_HS", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("OverworldStatic", StringComparison.OrdinalIgnoreCase))
                    {
                        if (StructuralQueue.Count < 96)
                        {
                            StructuralQueue.Enqueue((root.transform, 0));
                            selected++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("[WorldStructure] Loaded-scene " +
                "root inventory unavailable: " + ex.GetType().Name);
        }
        return selected;
    }

    private static bool IsFoliageName(string name)
    {
        return name.Contains("tree", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("grass", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bush", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("leaf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStructuralName(string name)
    {
        return name.Contains("floor", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ground", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("wall", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("building", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("roof", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("terrain", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("road", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("house", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bridge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCopyStaticRenderer(Transform at,
        Vector3 origin, float radius, string zone)
    {
        MeshFilter? filter = at.GetComponent<MeshFilter>();
        MeshRenderer? renderer = at.GetComponent<MeshRenderer>();
        if (filter == null || renderer == null || !renderer.enabled ||
            !renderer.gameObject.activeInHierarchy ||
            renderer.gameObject.layer == 20)
            return false;

        Mesh? source = filter.sharedMesh;
        if (source == null) return false;
        string meshName = source.name ?? "";
        // Higher-detail LODs are a waste of scarce mesh slots when the
        // corresponding LOD0 is already in the live scene. Choose LOD0.
        if (meshName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
            meshName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
            meshName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase))
        {
            _lodSkipped++;
            return false;
        }
        bool foliage = IsFoliageName(meshName);
        bool structure = IsStructuralName(meshName);
        int vertices = source.vertexCount;
        // Large floors/walls were previously thrown out at 20k vertices.
        // Allow a small number of genuine structures to be larger.
        int meshLimit = structure ? 120000 : 20000;
        if (vertices < 24 || vertices > meshLimit ||
            (foliage && _foliageMeshes >= 12) ||
            (vertices > 14000 && _largeGroundMeshes >= 7) ||
            _worldVertices + vertices > MaxTotalWorldVertices)
            return false;
        // A building's pivot may be far outside its visible walls.
        // Bounds.SqrDistance checks the actual visible geometry extent.
        float maxDist = radius + 20f;
        if (renderer.bounds.SqrDistance(origin) > maxDist * maxDist)
            return false;

        int id = filter.GetInstanceID();
        if (WorldObjectIds.Contains(id)) return false;
        Mesh? mesh = null;
        Material[] materials = Array.Empty<Material>();
        try
        {
            mesh = UnityEngine.Object.Instantiate(source);
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            materials = CopyMaterials(renderer);
            WorldParts.Add(new WorldPart
            {
                Name = WorldActorRecords.Safe(source.name),
                SourcePath = WorldHierarchyPath(at),
                VertexCount = source.vertexCount,
                Zone = WorldActorRecords.Safe(zone),
                Mesh = mesh, Materials = materials,
                Position = renderer.transform.position,
                Rotation = renderer.transform.rotation,
                Scale = renderer.transform.lossyScale
            });
            WorldObjectIds.Add(id);
            _worldVertices += source.vertexCount;
            if (source.vertexCount > 14000) _largeGroundMeshes++;
            if (foliage) _foliageMeshes++;
            if (structure) _structuralMeshes++;
            return true;
        }
        catch (Exception)
        {
            DestroyCopy(mesh);
            foreach (Material mat in materials) DestroyCopy(mat);
            return false;
        }
    }

    internal static string CaptureDiagnosticReport(string sessionId)
    {
        var sb = new System.Text.StringBuilder(9000);
        sb.AppendLine("# RebornCamera same-process visual cache report v1");
        sb.AppendLine("# Diagnostic names/positions only, not mesh geometry.");
        sb.AppendLine("session_matches=" + (_sessionId == sessionId));
        sb.AppendLine("cached_session_id=" + _sessionId);
        sb.AppendLine("rig_mesh_copies=" + RigAssets.Count);
        sb.AppendLine("local_avatar_mesh_parts=" + AvatarParts.Count);
        sb.AppendLine("world_mesh_copies=" + WorldParts.Count);
        sb.AppendLine("world_total_vertices=" + _worldVertices);
        sb.AppendLine("large_ground_meshes=" + _largeGroundMeshes);
        sb.AppendLine("foliage_meshes=" + _foliageMeshes);
        sb.AppendLine("structural_meshes=" + _structuralMeshes);
        sb.AppendLine("lower_lods_skipped=" + _lodSkipped);
        sb.AppendLine("structural_scene_nodes_seen=" + StructuralVisited.Count);
        sb.AppendLine("matched_loaded_scene_roots=" + _sceneRootCandidates);
        sb.AppendLine("structural_scene_nodes_remaining=" + StructuralQueue.Count);
        sb.AppendLine("structural_scene_zone=" + _structuralZone);
        sb.AppendLine("world_copy_cap=" + MaxWorldMeshes);
        sb.AppendLine("world_vertex_cap=" + MaxTotalWorldVertices);
        sb.AppendLine("world_copy_is_portable=false");
        foreach (RigAsset actor in RigAssets.Values)
            sb.AppendLine("rig=" + actor.ActorKey +
                " mesh=" + WorldActorRecords.Safe(actor.MeshName) +
                " bones=" + actor.BoneCount);
        foreach (AvatarPart part in AvatarParts)
            sb.AppendLine("avatar_part=" + part.PoseChannel +
                " mesh=" + WorldActorRecords.Safe(part.Name));
        for (int i = 0; i < WorldParts.Count; i++)
        {
            WorldPart part = WorldParts[i];
            sb.AppendLine("world_" + i + "_mesh=" + part.Name);
            sb.AppendLine("world_" + i + "_path=" + part.SourcePath);
            sb.AppendLine("world_" + i + "_zone=" + part.Zone);
            sb.AppendLine("world_" + i + "_vertices=" + part.VertexCount);
            sb.AppendLine("world_" + i + "_position=" +
                part.Position.x + "," + part.Position.y + "," +
                part.Position.z);
        }
        return sb.ToString();
    }

    internal static WorldPart[] GetWorldParts(string sessionId) =>
        _sessionId == sessionId ? WorldParts.ToArray() :
            Array.Empty<WorldPart>();

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
