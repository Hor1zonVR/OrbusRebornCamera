using System;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Optional cached local-avatar anchor sampling. Reuses the Minicam layer RC
/// already uses for rendering the local character in the desktop camera.
/// Never clones objects, writes transforms, invokes game/network methods, or
/// enumerates player accounts. A cached renderer is read at 30Hz; a bounded
/// lookup is attempted at most once every 12 seconds.
/// </summary>
internal sealed class LocalAvatarCapture
{
    internal struct Sample
    {
        internal bool Valid;
        internal Vector3 Position;
        internal Quaternion Rotation;
    }

    private const double RescanSeconds = 12d;
    private const float MaximumEyeDistance = 2.8f;
    private const int MaxRendererCandidates = 900;

    private SkinnedMeshRenderer? _renderer;
    private Transform? _anchor;
    private double _nextScan;
    private bool _reportedFound;
    private bool _reportedNoLayer;

    internal string MeshHint { get; private set; } = "(not resolved)";
    internal int BoneCount { get; private set; }
    internal bool HasAnchor => _anchor != null && _renderer != null;

    internal Sample Read(Camera eye, double clockSeconds)
    {
        if (eye == null) return default;
        int minicamLayer = LayerMask.NameToLayer("Minicam");
        if (minicamLayer < 0)
        {
            if (!_reportedNoLayer)
            {
                _reportedNoLayer = true;
                Plugin.ModLogger.LogWarning(
                    "[AvatarCapture] Minicam layer unavailable. " +
                    "Recording head/hands; model anchor marked missing.");
            }
            return default;
        }

        if (_renderer != null && _anchor != null)
        {
            try
            {
                if (_renderer.gameObject.layer == minicamLayer &&
                    Vector3.Distance(_anchor.position,
                        eye.transform.position) <= MaximumEyeDistance)
                {
                    return new Sample
                    {
                        Valid = true,
                        Position = _anchor.position,
                        Rotation = _anchor.rotation
                    };
                }
            }
            catch (Exception)
            {
                // Scene transitions can destroy these cached objects.
            }
            _renderer = null;
            _anchor = null;
        }

        if (clockSeconds < _nextScan) return default;
        _nextScan = clockSeconds + RescanSeconds;
        try { FindLocalAvatar(eye, minicamLayer); }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[AvatarCapture] Avatar lookup skipped: " + ex.Message);
        }

        if (_anchor == null) return default;
        return new Sample
        {
            Valid = true,
            Position = _anchor.position,
            Rotation = _anchor.rotation
        };
    }

    private void FindLocalAvatar(Camera eye, int localLayer)
    {
        // This renderer enumeration is the same kind of information already
        // inspected by AvatarDiagnostics, but runs at most once per 12s.
        // The Minicam layer and a small eye-distance bound exclude scenery,
        // NPCs and distant people. Identity is still a heuristic; if RC
        // cannot find a convincing local mesh, it exports no avatar pose.
        SkinnedMeshRenderer[] renderers =
            Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
        float bestScore = float.MaxValue;
        SkinnedMeshRenderer? best = null;
        Transform? bestAnchor = null;
        int examined = 0;

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            if (++examined > MaxRendererCandidates) break;
            if (renderer == null || renderer.gameObject == null ||
                renderer.gameObject.layer != localLayer)
                continue;

            Transform anchor = renderer.rootBone != null
                ? renderer.rootBone : renderer.transform;
            if (anchor == null) continue;
            Vector3 pos = anchor.position;
            float distance = Vector3.Distance(pos, eye.transform.position);
            if (distance > MaximumEyeDistance) continue;

            // Prefer a rig close to the local eye. Reject ambiguous
            // equally-close candidates rather than claiming player identity.
            float score = distance;
            if (score < bestScore)
            {
                bestScore = score;
                best = renderer;
                bestAnchor = anchor;
            }
        }

        if (best == null || bestAnchor == null) return;
        _renderer = best;
        _anchor = bestAnchor;

        try
        {
            Mesh mesh = best.sharedMesh;
            MeshHint = Clean(mesh != null ? mesh.name : best.name);
            Transform[] bones = best.bones;
            BoneCount = bones == null ? 0 : bones.Length;
        }
        catch (Exception)
        {
            MeshHint = "(mesh unavailable)";
            BoneCount = 0;
        }

        if (!_reportedFound)
        {
            _reportedFound = true;
            Plugin.ModLogger.LogInfo(
                "[AvatarCapture] Local Minicam rig candidate: " +
                MeshHint + ", bones=" + BoneCount +
                ". Recording its anchor pose; mesh geometry/animations " +
                "are NOT yet serialized.");
        }
    }

    private static string Clean(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "(unnamed)";
        input = input.Replace('\t', ' ').Replace('\r', ' ')
            .Replace('\n', ' ').Replace('=', '-');
        return input.Length > 100 ? input.Substring(0, 100) : input;
    }
}
