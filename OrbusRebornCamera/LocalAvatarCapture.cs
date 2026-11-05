using System;
using System.Text;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Read-only, hierarchy-targeted local avatar capture. The local PlayerRigv4
/// structure is grounded in prior AvatarInspector logs from Renewed/Spades:
///   PlayerRigv4/Avatar/Torso/BodyParent (Minicam layer)
///   PlayerRigv4/Camera (eye)/Head
///   PlayerRigv4/Controller (left|right)/Grabber/Hand/model
/// It is deliberately independent of obfuscated C# class names. No global
/// SkinnedMeshRenderer scan, network access, cloning, or pose manipulation.
/// </summary>
internal sealed class LocalAvatarCapture
{
    internal struct TrackedPose
    {
        internal bool Valid;
        internal Vector3 Position;
        internal Quaternion Rotation;
    }

    internal struct Sample
    {
        // Kept compatible with the existing v3 avatar anchor writer.
        internal bool Valid;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal TrackedPose Root;
        internal TrackedPose ModelHead;
        internal TrackedPose ModelLeft;
        internal TrackedPose ModelRight;
    }

    private const double RetrySeconds = 3.0;
    private const int MaxManifestRenderersPerPart = 48;
    private const int MaxManifestBones = 72;
    private const int MaxManifestMaterials = 8;

    private Transform? _playerRig;
    private Transform? _body;
    private Transform? _head;
    private Transform? _leftHand;
    private Transform? _rightHand;
    private double _nextResolve;
    private bool _reportedFound;
    private bool _reportedMissing;
    private string _lastReason = "not inspected";

    internal string MeshHint { get; private set; } = "(not resolved)";
    internal int BoneCount { get; private set; }
    internal bool HasAnchor => _body != null;

    private static bool IsLocalRootName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        (name == "PlayerRigv4" ||
         name.StartsWith("PlayerRigv4(", StringComparison.Ordinal));

    private static Transform? ParentRigOf(Transform? child)
    {
        Transform? at = child;
        for (int depth = 0; at != null && depth < 14; depth++)
        {
            if (IsLocalRootName(at.name)) return at;
            at = at.parent;
        }
        return null;
    }

    private static TrackedPose ReadPose(Transform? transform)
    {
        if (transform == null) return default;
        return new TrackedPose
        {
            Valid = true,
            Position = transform.position,
            Rotation = transform.rotation
        };
    }

    private void ResetRig()
    {
        _playerRig = null;
        _body = null;
        _head = null;
        _leftHand = null;
        _rightHand = null;
        MeshHint = "(not resolved)";
        BoneCount = 0;
    }

    internal Sample Read(Camera eye, double clockSeconds)
    {
        if (eye == null) return default;
        try
        {
            // Only consider the local player owning the known source eye.
            // Do not guess which of hundreds of remote avatar renderers
            // belongs to us, and don't match by player account or location.
            Transform? rig = ParentRigOf(eye.transform);
            if (rig == null || _playerRig == null ||
                rig != _playerRig || _body == null)
            {
                ResetRig();
                if (clockSeconds < _nextResolve) return default;
                _nextResolve = clockSeconds + RetrySeconds;
                if (rig == null)
                {
                    _lastReason = "eye camera is not under PlayerRigv4";
                    LogUnresolved();
                    return default;
                }
                ResolveRig(rig);
                if (_body == null)
                {
                    LogUnresolved();
                    return default;
                }
            }

            TrackedPose body = ReadPose(_body);
            return new Sample
            {
                Valid = body.Valid,
                Position = body.Position,
                Rotation = body.Rotation,
                Root = ReadPose(_playerRig),
                ModelHead = ReadPose(_head),
                ModelLeft = ReadPose(_leftHand),
                ModelRight = ReadPose(_rightHand)
            };
        }
        catch (Exception ex)
        {
            ResetRig();
            _lastReason = "source eye or avatar hierarchy invalid: " +
                ex.GetType().Name;
            LogUnresolved();
            return default;
        }
    }

    private void ResolveRig(Transform rig)
    {
        _playerRig = rig;
        _body = rig.Find("Avatar/Torso/BodyParent");
        _head = rig.Find("Camera (eye)/Head");
        _leftHand = rig.Find("Controller (left)/Grabber/Hand/model");
        _rightHand = rig.Find("Controller (right)/Grabber/Hand/model");

        if (_body == null)
        {
            _lastReason = "PlayerRigv4 found, BodyParent path missing";
            return;
        }

        _lastReason = "PlayerRigv4/Avatar/Torso/BodyParent resolved";
        MeshHint = "BodyParent hierarchy (mesh unresolved)";
        BoneCount = 0;
        ResolveBodyVisualHint();

        if (!_reportedFound)
        {
            _reportedFound = true;
            Plugin.ModLogger.LogInfo(
                "[AvatarCapture] Verified local PlayerRigv4/BODY ANCHOR. " +
                "Head=" + (_head != null) +
                ", left hand=" + (_leftHand != null) +
                ", right hand=" + (_rightHand != null) +
                ". Saving actual transform tracks; NO mesh/animation playback yet.");
        }
    }

    private void ResolveBodyVisualHint()
    {
        if (_body == null) return;
        try
        {
            // A single, bounded lookup under BodyParent. The renderer can
            // live on Default even when its parent is on Minicam.
            var skins = _body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int count = skins == null ? 0 : Math.Min(24, skins.Length);
            for (int i = 0; i < count; i++)
            {
                var skin = skins[i];
                if (skin == null || skin.sharedMesh == null) continue;
                MeshHint = Safe(skin.sharedMesh.name);
                var bones = skin.bones;
                BoneCount = bones == null ? 0 : bones.Length;
                return;
            }

            var filters = _body.GetComponentsInChildren<MeshFilter>(true);
            int n = filters == null ? 0 : Math.Min(24, filters.Length);
            for (int i = 0; i < n; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null) continue;
                MeshHint = Safe(filter.sharedMesh.name);
                BoneCount = 0; // MeshFilter geometry has no skin rig.
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[AvatarCapture] Body mesh hint unavailable: " +
                ex.GetType().Name);
        }
    }

    private void LogUnresolved()
    {
        if (_reportedMissing) return;
        _reportedMissing = true;
        Plugin.ModLogger.LogWarning(
            "[AvatarCapture] Targeted rig not resolved: " + _lastReason);
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(none)";
        string clean = value.Replace('\t', ' ').Replace('\r', ' ')
            .Replace('\n', ' ').Replace('=', '-');
        return clean.Length > 100 ? clean.Substring(0, 100) : clean;
    }

    private static string PathWithin(Transform? node, Transform? boundary)
    {
        if (node == null) return "(missing)";
        var parts = new System.Collections.Generic.List<string>();
        Transform? at = node;
        for (int depth = 0; at != null && depth < 20; depth++)
        {
            parts.Add(Safe(at.name));
            if (boundary != null && at == boundary) break;
            at = at.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void AppendPose(StringBuilder sb, string prefix,
        Transform? transform)
    {
        sb.AppendLine(prefix + "_path=" + PathWithin(transform, null));
        if (transform == null) return;
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        sb.AppendLine(prefix + "_position=" + pos.x + "," + pos.y +
            "," + pos.z);
        sb.AppendLine(prefix + "_rotation=" + rot.x + "," + rot.y +
            "," + rot.z + "," + rot.w);
    }

    private static void AppendPart(StringBuilder sb, string part,
        Transform? node, Transform? rig)
    {
        sb.AppendLine("part=" + part + " path=" + PathWithin(node, rig));
        if (node == null) return;

        // Targeted only: local avatar, model head and two hand objects.
        // No global scan. This is called only when Save Clip is pressed.
        try
        {
            var renderers = node.GetComponentsInChildren<Renderer>(true);
            int total = renderers == null ? 0 : renderers.Length;
            sb.AppendLine("renderer_count=" + total);
            int count = Math.Min(total, MaxManifestRenderersPerPart);
            for (int i = 0; i < count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                sb.AppendLine("renderer_" + i + "_path=" +
                    PathWithin(renderer.transform, rig));
                sb.AppendLine("renderer_" + i + "_type=" +
                    Safe(renderer.GetType().Name));
                sb.AppendLine("renderer_" + i + "_enabled=" + renderer.enabled);
                sb.AppendLine("renderer_" + i + "_layer=" +
                    renderer.gameObject.layer);

                SkinnedMeshRenderer? skin = renderer as SkinnedMeshRenderer;
                if (skin != null)
                {
                    var mesh = skin.sharedMesh;
                    sb.AppendLine("renderer_" + i + "_mesh=" +
                        (mesh == null ? "(none)" : Safe(mesh.name)));
                    sb.AppendLine("renderer_" + i + "_vertices=" +
                        (mesh == null ? 0 : mesh.vertexCount));
                    var bones = skin.bones;
                    int n = bones == null ? 0 : Math.Min(
                        MaxManifestBones, bones.Length);
                    sb.AppendLine("renderer_" + i + "_bone_count=" +
                        (bones == null ? 0 : bones.Length));
                    sb.AppendLine("renderer_" + i + "_root_bone=" +
                        PathWithin(skin.rootBone, rig));
                    for (int b = 0; b < n; b++)
                        sb.AppendLine("renderer_" + i + "_bone_" + b + "=" +
                            PathWithin(bones[b], rig));
                    if (bones != null && bones.Length > n)
                        sb.AppendLine("renderer_" + i +
                            "_bone_names_truncated=true");
                }
                else
                {
                    var meshFilter = renderer.gameObject.GetComponent<MeshFilter>();
                    var mesh = meshFilter == null ? null : meshFilter.sharedMesh;
                    if (mesh != null)
                    {
                        sb.AppendLine("renderer_" + i + "_mesh=" +
                            Safe(mesh.name));
                        sb.AppendLine("renderer_" + i + "_vertices=" +
                            mesh.vertexCount);
                    }
                }

                var materials = renderer.sharedMaterials;
                int mCount = materials == null ? 0 : Math.Min(
                    MaxManifestMaterials, materials.Length);
                for (int m = 0; m < mCount; m++)
                    sb.AppendLine("renderer_" + i + "_material_" + m +
                        "=" + (materials[m] == null ? "(none)" :
                        Safe(materials[m].name)));
            }
            if (total > count) sb.AppendLine("renderers_truncated=true");
        }
        catch (Exception ex)
        {
            sb.AppendLine("part_error=" + Safe(ex.GetType().Name));
        }
    }

    private static void AppendSkinnedRigDetails(StringBuilder sb,
        string part, Transform? node, Transform? rig)
    {
        if (node == null) return;
        try
        {
            // Query the derived type directly: Unity IL2CPP base Renderer
            // wrappers do not always preserve their managed subclass.
            var skins = node.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int count = skins == null ? 0 : Math.Min(24, skins.Length);
            sb.AppendLine("skin_part=" + part + " count=" +
                (skins == null ? 0 : skins.Length));
            for (int i = 0; i < count; i++)
            {
                var skin = skins[i];
                if (skin == null) continue;
                var mesh = skin.sharedMesh;
                var bones = skin.bones;
                string key = "skin_" + part + "_" + i;
                sb.AppendLine(key + "_path=" +
                    PathWithin(skin.transform, rig));
                sb.AppendLine(key + "_mesh=" +
                    (mesh == null ? "(none)" : Safe(mesh.name)));
                sb.AppendLine(key + "_vertices=" +
                    (mesh == null ? 0 : mesh.vertexCount));
                sb.AppendLine(key + "_root_bone=" +
                    PathWithin(skin.rootBone, rig));
                int n = bones == null ? 0 : Math.Min(
                    MaxManifestBones, bones.Length);
                sb.AppendLine(key + "_bone_count=" +
                    (bones == null ? 0 : bones.Length));
                for (int j = 0; j < n; j++)
                    sb.AppendLine(key + "_bone_" + j + "=" +
                        PathWithin(bones[j], rig));
                if (bones != null && bones.Length > n)
                    sb.AppendLine(key + "_bone_names_truncated=true");
            }
            if (skins != null && skins.Length > count)
                sb.AppendLine("skinned_renderers_truncated=true");
        }
        catch (Exception ex)
        {
            sb.AppendLine("skin_part_error=" + part + " " +
                Safe(ex.GetType().Name));
        }
    }

    internal string CaptureRigManifest()
    {
        var sb = new StringBuilder(12000);
        sb.AppendLine("# RebornCamera targeted local avatar report v2");
        sb.AppendLine("# hierarchy rooted at the SOURCE EYE'S PlayerRigv4");
        sb.AppendLine("# READ-ONLY: no mesh export, animation or network calls");
        sb.AppendLine("capture_status=" + (_body != null ?
            "body_anchor_resolved" : "not_resolved"));
        sb.AppendLine("last_reason=" + Safe(_lastReason));
        sb.AppendLine("mesh_hint=" + MeshHint);
        sb.AppendLine("bone_count_hint=" + BoneCount);
        sb.AppendLine("player_rig=" + PathWithin(_playerRig, null));
        AppendPose(sb, "body", _body);
        AppendPose(sb, "head", _head);
        AppendPose(sb, "hand_left", _leftHand);
        AppendPose(sb, "hand_right", _rightHand);
        if (_playerRig != null)
        {
            Transform? avatar = _playerRig.Find("Avatar");
            Transform? torso = _playerRig.Find("Avatar/Torso");
            AppendPart(sb, "Torso", torso, _playerRig);
            AppendPart(sb, "BodyParent", _body, _playerRig);
            AppendPart(sb, "Avatar", avatar, _playerRig);
            AppendPart(sb, "HeadVisual", _head, _playerRig);
            AppendPart(sb, "LeftGrabber",
                _playerRig.Find("Controller (left)/Grabber"), _playerRig);
            AppendPart(sb, "RightGrabber",
                _playerRig.Find("Controller (right)/Grabber"), _playerRig);
            AppendSkinnedRigDetails(sb, "Avatar", avatar, _playerRig);
            AppendSkinnedRigDetails(sb, "BodyParent", _body, _playerRig);
            AppendSkinnedRigDetails(sb, "HeadVisual", _head, _playerRig);
            AppendSkinnedRigDetails(sb, "LeftHand", _leftHand, _playerRig);
            AppendSkinnedRigDetails(sb, "RightHand", _rightHand, _playerRig);
        }
        return sb.ToString();
    }
}
