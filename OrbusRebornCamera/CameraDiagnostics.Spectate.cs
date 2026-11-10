using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// A read-only, desktop-only camera attachment to an already instantiated
/// OtherPlayerv2 rig. Based on the Community Edition IL2CPP / Ghidra research:
/// OrbusNetManager -> OtherPlayerEntity -> smoothed remote avatar transforms.
/// Never registers with PlayerCharacter.SetNewExternalCamera (game-owned
/// camera cleanup), moves the local rig, or requests network entities.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private sealed class LoadedSpectatePlayer
    {
        internal Transform Root = null!;
        internal int InstanceId;
        internal float Distance;
    }

    // Discovery is deliberately explicit (the REFRESH button), not a
    // whole-scene query in Update/LateUpdate. Unity 2018 VR can hitch when
    // thousands of transforms are enumerated every frame.
    private readonly List<LoadedSpectatePlayer> _loadedSpectatePlayers = new();
    private Transform? _spectateRoot;
    private Transform? _spectateHead;
    private int _spectateInstanceId;
    private int _spectatePage;
    private bool _spectateDiscovered;
    private float _spectateNextHeadLookup;

    private const int SpectatePageSize = 4;
    private const int SpectateMaxCandidates = 128;
    private const int SpectateMaxHeadSearchNodes = 256;

    private static bool IsLoadedRemotePlayer(Transform? root)
    {
        if (root == null || root.gameObject == null ||
            !root.gameObject.activeInHierarchy)
            return false;

        // Confirmed by NearbyWorldActorCapture and the local scene probes.
        // These are only client-instantiated remote players, not hidden or
        // server-side characters in another area.
        string name = root.name ?? "";
        return name == "OtherPlayerv2" ||
            name.StartsWith("OtherPlayerv2(", StringComparison.Ordinal);
    }

    private void RefreshSpectateTargets()
    {
        _spectateDiscovered = true;
        _loadedSpectatePlayers.Clear();
        try
        {
            Vector3 from = _source != null
                ? _source.transform.position
                : (_desktop != null ? _desktop.transform.position : Vector3.zero);

            var seen = new HashSet<int>();
            // A single explicit, read-only scene enumeration. Do not poll.
            foreach (Transform root in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (!IsLoadedRemotePlayer(root)) continue;

                int id = root.GetInstanceID();
                if (!seen.Add(id)) continue;
                _loadedSpectatePlayers.Add(new LoadedSpectatePlayer
                {
                    Root = root,
                    InstanceId = id,
                    Distance = Vector3.Distance(root.position, from)
                });
            }

            _loadedSpectatePlayers.Sort((a, b) =>
                a.Distance.CompareTo(b.Distance));
            if (_loadedSpectatePlayers.Count > SpectateMaxCandidates)
                _loadedSpectatePlayers.RemoveRange(
                    SpectateMaxCandidates,
                    _loadedSpectatePlayers.Count - SpectateMaxCandidates);

            int lastPage = Math.Max(0,
                (_loadedSpectatePlayers.Count - 1) / SpectatePageSize);
            _spectatePage = Mathf.Clamp(_spectatePage, 0, lastPage);
            Plugin.ModLogger.LogInfo(
                "[Spectate] Loaded OtherPlayerv2 candidates: " +
                _loadedSpectatePlayers.Count + " (manual refresh).");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[Spectate] Player refresh failed: " + ex.Message);
        }
    }

    private static int HeadNodePriority(string rawName)
    {
        string n = (rawName ?? "").ToLowerInvariant();
        if (n.Contains("headset") || n.Contains("headphone") ||
            n.Contains("headmesh") || n.Contains("helmet"))
            return 0; // An accessory is not a tracked view transform.
        if (n == "headanchor" || n == "headtarget" ||
            n == "headtransform" || n == "hmd")
            return 12;
        if (n == "camera (eye)" || n == "eyeanchor")
            return 11;
        if (n == "head" || n == "headobj")
            return 10;
        if (n.StartsWith("head", StringComparison.Ordinal))
            return 8;
        if (n.Contains("head"))
            return 5;
        return 0;
    }

    private static Transform? FindRemoteHead(Transform root)
    {
        // Bounded hierarchy inspection. Exact head/eye paths can vary
        // between versions and rigs; fall back to root + approximate height.
        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        Transform? best = null;
        int bestPriority = 0;
        int visited = 0;

        while (queue.Count > 0 && visited++ < SpectateMaxHeadSearchNodes)
        {
            Transform node = queue.Dequeue();
            if (node == null || node.gameObject == null) continue;
            int priority = HeadNodePriority(node.name);
            if (priority > bestPriority)
            {
                best = node;
                bestPriority = priority;
                if (priority == 12) break;
            }

            int children = node.childCount;
            for (int i = 0; i < children; i++)
            {
                Transform child = node.GetChild(i);
                if (child != null) queue.Enqueue(child);
            }
        }

        return best;
    }

    private void ReturnSpectateToSelf(string reason)
    {
        if (_spectateRoot != null)
            Plugin.ModLogger.LogInfo("[Spectate] " + reason);
        _spectateRoot = null;
        _spectateHead = null;
        _spectateInstanceId = 0;
        _snapNextFrame = true;
    }

    private void SelectSpectateTarget(Transform? root)
    {
        if (root != null && !IsLoadedRemotePlayer(root))
        {
            Plugin.ModLogger.LogWarning(
                "[Spectate] That player is no longer loaded. Refresh targets.");
            return;
        }

        if (_offlineReplayActive)
            StopOfflineReplay();
        if (_mode == CameraMode.Drone)
        {
            StopKeyframePlayback();
            LeaveDrone();
            _mode = _lastFollowMode;
        }
        if (_mode == CameraMode.Static)
            _mode = _lastFollowMode;

        if (root == null)
        {
            ReturnSpectateToSelf("Returned to your player.");
        }
        else
        {
            _spectateRoot = root;
            _spectateInstanceId = root.GetInstanceID();
            _spectateHead = FindRemoteHead(root);
            _spectateNextHeadLookup = Time.unscaledTime + 3f;
            _snapNextFrame = true;
            Plugin.ModLogger.LogInfo(
                "[Spectate] Attached to loaded OtherPlayerv2 instance " +
                _spectateInstanceId + ". View head: " +
                (_spectateHead == null ? "approximate root + height" :
                    _spectateHead.name) +
                ". Desktop camera only; network/local avatar unchanged.");
        }

        _autoStartHandled = true;
        if (!IsCameraActive)
            TryEnableCamera(true);
        UpdateDesktopCullingMask(false);
    }

    // Read only the remote visual rig *after* the game has interpolated it.
    // This is a reconstruction, not the remote headset's exact eye texture.
    private bool TryGetSpectatePose(out Vector3 eyePosition,
        out Quaternion eyeRotation, out Vector3 bodyForward)
    {
        eyePosition = Vector3.zero;
        eyeRotation = Quaternion.identity;
        bodyForward = Vector3.forward;
        Transform? root = _spectateRoot;
        if (root == null) return false;

        if (!IsLoadedRemotePlayer(root) ||
            root.GetInstanceID() != _spectateInstanceId)
        {
            ReturnSpectateToSelf("Target despawned or changed scene.");
            return false;
        }

        bodyForward = root.forward;
        Transform? head = _spectateHead;
        if (head == null || head.gameObject == null ||
            !head.gameObject.activeInHierarchy)
        {
            if (Time.unscaledTime >= _spectateNextHeadLookup)
            {
                _spectateHead = FindRemoteHead(root);
                _spectateNextHeadLookup = Time.unscaledTime + 3f;
            }
            head = _spectateHead;
        }

        // PlayerRoot is commonly at floor/body origin in Orbus, not HMD.
        // If the head pose isn't exposed, the user still gets a usable
        // third-person attachment and approximate first-person view.
        eyePosition = root.position + Vector3.up * 1.55f;
        eyeRotation = root.rotation;
        if (head != null)
        {
            eyePosition = head.position;
            eyeRotation = head.rotation;
        }
        return true;
    }

    private void DrawTargetsTab(float x, float y, float w)
    {
        StudioSection(x, y, w, "SPECTATE LOADED PLAYERS");

        string selection = "MY PLAYER";
        if (_spectateRoot != null)
        {
            selection = "REMOTE PLAYER #" + _spectateInstanceId;
            if (!IsLoadedRemotePlayer(_spectateRoot))
                selection = "PLAYER LEFT - SELECT ANOTHER";
        }
        StudioFill(new Rect(x, y + 30f, w, 39f), StudioSurface);
        StudioBorder(new Rect(x, y + 30f, w, 39f), StudioStroke);
        StudioText(new Rect(x + 12f, y + 39f, w - 24f, 25f),
            selection, _studioValue);

        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 78f, half, 38f),
            "My character", _spectateRoot == null))
            SelectSpectateTarget(null);
        if (StudioButton(new Rect(x + half + 9f, y + 78f, half, 38f),
            "Refresh loaded players", false))
            RefreshSpectateTargets();

        float third = (w - 18f) / 3f;
        if (StudioButton(new Rect(x, y + 125f, third, 37f),
            "First person", _mode == CameraMode.POV))
            SetPanelMode(CameraMode.POV);
        if (StudioButton(new Rect(x + third + 9f, y + 125f,
            third, 37f), "Third person",
            _mode == CameraMode.ThirdPerson))
            SetPanelMode(CameraMode.ThirdPerson);
        if (StudioButton(new Rect(x + (third + 9f) * 2f,
            y + 125f, third, 37f), "Selfie",
            _mode == CameraMode.Selfie))
            SetPanelMode(CameraMode.Selfie);

        // Additional camera draw distance applies to ALREADY LOADED
        // scenery. It never increases server/player interest replication.
        bool extendDraw = Plugin.SpectateExtendLoadedDrawDistance.Value;
        if (StudioButton(new Rect(x, y + 165f, half, 29f),
            extendDraw ? "Extended draw: ON" : "Extended draw: OFF",
            extendDraw))
        {
            Plugin.SpectateExtendLoadedDrawDistance.Value = !extendDraw;
            Plugin.ModLogger.LogInfo(
                "[Spectate] Loaded-object render extension: " +
                (Plugin.SpectateExtendLoadedDrawDistance.Value ?
                    "ON" : "OFF") +
                ". Does not change server interest radius.");
        }
        string rangeText = "Loaded players only";
        if (_spectateRoot != null && _source != null &&
            IsLoadedRemotePlayer(_spectateRoot))
        {
            float playerDistance = Vector3.Distance(
                _spectateRoot.position, _source.transform.position);
            rangeText = playerDistance.ToString("0") +
                "m from you - loaded";
        }
        StudioText(new Rect(x + half + 11f, y + 169f,
            half - 12f, 24f), rangeText, _studioMuted);

        if (!_spectateDiscovered)
        {
            StudioText(new Rect(x + 8f, y + 218f, w - 16f, 42f),
                "Select REFRESH to discover loaded characters.",
                _studioBody);
        }
        else if (_loadedSpectatePlayers.Count == 0)
        {
            StudioText(new Rect(x + 8f, y + 218f, w - 16f, 50f),
                "No remote players found. Stand near another player " +
                "and refresh.", _studioBody);
        }

        int start = _spectatePage * SpectatePageSize;
        int end = Math.Min(start + SpectatePageSize,
            _loadedSpectatePlayers.Count);
        for (int i = start; i < end; i++)
        {
            LoadedSpectatePlayer player = _loadedSpectatePlayers[i];
            int row = i - start;
            bool valid = IsLoadedRemotePlayer(player.Root);
            bool selected = valid && _spectateRoot != null &&
                _spectateInstanceId == player.InstanceId;
            string label = valid ?
                "PLAYER " + (i + 1).ToString("00") + "   |   " +
                player.Distance.ToString("0.0") + "m   |   " +
                player.InstanceId :
                "PLAYER " + (i + 1).ToString("00") + "   |   unloaded";
            if (StudioButton(new Rect(x, y + 195f + row * 40f,
                w, 35f), label, selected) && valid)
                SelectSpectateTarget(player.Root);
        }

        int totalPages = Math.Max(1,
            (_loadedSpectatePlayers.Count + SpectatePageSize - 1)
                / SpectatePageSize);
        if (StudioButton(new Rect(x, y + 360f, 98f, 37f),
            "Previous", false))
            _spectatePage = Math.Max(0, _spectatePage - 1);
        StudioText(new Rect(x + 108f, y + 368f, w - 216f, 24f),
            "Page " + (_spectatePage + 1) + " / " + totalPages,
            _studioMuted);
        if (StudioButton(new Rect(x + w - 98f, y + 360f,
            98f, 37f), "Next", false))
            _spectatePage = Math.Min(totalPages - 1, _spectatePage + 1);
    }
}
