using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbusBetterMirror;

/// <summary>
/// Explicit, no-network local pose preview, independent of streamed world
/// scenery. It works from Orbus's menu if Unity is running and F2 is usable.
/// World geometry is intentionally not loaded: the generated grid is a
/// diagnostic placeholder, never presented as the recorded zone.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private bool _offlineReplayActive;
    private string _offlineStatus =
        "Offline preview not running. No world assets loaded.";
    private Task<string>? _offlineInventoryTask;
    private bool _offlineGridErrorLogged;

    private void StartOfflineReplay()
    {
        if (_offlineReplayActive)
        {
            FocusReplayHead();
            return;
        }

        // Loading a clip offline must NOT call EnterFreecam(), which relies
        // on the current server/Orbus game view camera.
        if (_replayClip == null) LoadLatestReplay(false);
        if (_replayClip == null)
        {
            _offlineStatus = "Save or copy an RC_Clip_*.tsv first.";
            return;
        }

        try
        {
            StopKeyframePlayback();
            ReleaseDronePointer();
            _autoStartHandled = true;
            if (_desktop == null) CreateDesktopCamera();
            if (_desktop == null)
                throw new InvalidOperationException(
                    "Could not allocate the detached desktop camera.");

            // This camera draws only a generated grid + pose guides.
            // Never touch game world cameras, XR rig, scene loader or network.
            _offlineReplayActive = true;
            _mode = CameraMode.Drone;
            _droneRecenterPending = false;
            _droneHasObservedEye = false;
            _droneVelocity = Vector3.zero;
            _desktop.enabled = false;
            _desktop.stereoTargetEye = StereoTargetEyeMask.None;
            _desktop.targetDisplay = 0;
            _desktop.targetTexture = null;
            _desktop.depth = 1000f;
            _desktop.rect = new Rect(0f, 0f, 1f, 1f);
            // Preview renders ONLY RC-created render-only mesh objects.
            // Live Orbus scene objects and menus remain culled.
            _desktop.cullingMask = 1 << ReplayVisualAssets.ReplayLayer;
            _desktop.clearFlags = CameraClearFlags.SolidColor;
            _desktop.backgroundColor = new Color(.025f, .036f, .051f, 1f);
            _desktop.nearClipPlane = .035f;
            _desktop.farClipPlane = 1500f;
            _desktop.fieldOfView = 75f;
            _desktop.aspect = Mathf.Max(1f, Screen.width) /
                Mathf.Max(1f, Screen.height);
            _desktop.enabled = true;

            _offlineStatus = "Offline stage ACTIVE. Cached models work only in same process.";
            // If this recording already has a same-process visual cache,
            // show the actual mesh experiment by default instead of
            // leaving the previous Bones diagnostic mode selected.
            if (_replayClip != null &&
                ReplayVisualAssets.SessionId == _replayClip.SessionId &&
                (ReplayVisualAssets.RigCount > 0 ||
                 ReplayVisualAssets.AvatarPartCount > 0))
            {
                _showObservedActors = true;
                _showObservedBones = false;
                _showObservedModels = true;
                Plugin.ModLogger.LogInfo(
                    "[ReplayVisuals] MODEL preview selected for this " +
                    "same-process clip. Switch to BONES for diagnostics.");
            }
            BuildOfflineVisuals();
            FocusReplayHead();
            Plugin.ModLogger.LogInfo(
                "[OfflineReplay] Detached replay stage started. " +
                "No game session, world loading or server requests performed.");
        }
        catch (Exception ex)
        {
            _offlineStatus = "Offline preview failed: see BepInEx log.";
            Plugin.ModLogger.LogError(
                "[OfflineReplay] Could not create safe preview: " + ex);
            StopOfflineReplay();
        }
    }

    private void StopOfflineReplay()
    {
        if (!_offlineReplayActive) return;
        _offlineReplayActive = false;
        _replayPlaying = false;
        ClearOfflineVisuals();
        ReleaseDronePointer();
        RestoreCinematicRendering();
        if (_desktop != null)
            _desktop.enabled = false;
        _mode = _lastFollowMode;
        _source = null;
        _droneVelocity = Vector3.zero;
        DesktopFreecamActive = false;
        _offlineStatus = "Offline preview closed. Live camera not changed.";
        Plugin.ModLogger.LogInfo(
            "[OfflineReplay] Preview closed. Press F9 to enable regular camera.");
    }

    private void CaptureOfflineInventory()
    {
        if (_offlineInventoryTask != null && !_offlineInventoryTask.IsCompleted)
        {
            _offlineStatus = "Asset inventory already running.";
            return;
        }

        try
        {
            var loaded = new StringBuilder();
            // Unity API access stays on the main thread.
            int count = Math.Min(80, SceneManager.sceneCount);
            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                    loaded.AppendLine(scene.name);
            }

            string dataPath = Application.dataPath;
            string streamingPath = Application.streamingAssetsPath;
            string scenes = loaded.ToString();
            string folder = Path.Combine(BepInEx.Paths.ConfigPath,
                "OrbusRebornCamera", "OfflineReports");
            string dest = Path.Combine(folder,
                "RC_OfflineAssets_" + DateTime.UtcNow.ToString(
                    "yyyyMMdd_HHmmss") + ".txt");

            _offlineStatus = "Scanning filenames (read-only, background)...";
            _offlineInventoryTask = Task.Run(() =>
            {
                string report = OfflineWorldInventory.BuildReport(
                    dataPath, streamingPath, scenes);
                Directory.CreateDirectory(folder);
                File.WriteAllText(dest, report, new UTF8Encoding(false));
                return dest;
            });
        }
        catch (Exception ex)
        {
            _offlineStatus = "Inventory failed - check BepInEx log.";
            Plugin.ModLogger.LogError("[OfflineReplay] Inventory setup: " + ex);
        }
    }

    private void UpdateOfflineInventory()
    {
        Task<string>? task = _offlineInventoryTask;
        if (task == null || !task.IsCompleted) return;
        _offlineInventoryTask = null;
        try
        {
            string path = task.GetAwaiter().GetResult();
            _offlineStatus = "Saved: " + Path.GetFileName(path);
            Plugin.ModLogger.LogInfo("[OfflineReplay] World inventory: " + path);
        }
        catch (Exception ex)
        {
            _offlineStatus = "Inventory failed - see BepInEx log.";
            Plugin.ModLogger.LogError("[OfflineReplay] Inventory: " + ex);
        }
    }

    private void DrawOfflineStageGrid()
    {
        if (!_offlineReplayActive || _replayClip == null) return;
        ReplayPoseValue head = EvaluatedPose(0);
        bool partnerOnly = !head.Valid && _partyTimeline != null &&
            PartyPartnerVisible();
        if (partnerOnly) head = PartyPose(1, 0);
        if (!head.Valid) return;

        // A previous version permanently placed the 20m grid under the
        // *first frame*. Clips that start in CharacterSelectHybrid and
        // teleport ~1 km into Highsteppe left the grid in the wrong world.
        //
        // Use the current region's recorded first pose for ground height,
        // and draw a world-aligned, tiled 60m window around the moving
        // playback marker. Grid lines stay on *absolute metre boundaries*:
        // they do NOT slide under the avatar as it walks. We only reveal
        // more tiles when the replay crosses a 10m window boundary.
        ReplayPoseValue region = partnerOnly && _partyTimeline != null
            ? _partyTimeline.Partner.GridReferenceAt(
                _partyTimeline.ClipElapsed(1, _replayElapsed))
            : _replayClip.GridReferenceAt(LocalReplayElapsed);
        if (!region.Valid) region = head;
        float y = region.Y - 1.65f;
        float cx = (float)(Math.Floor(head.X / 10d) * 10d);
        float cz = (float)(Math.Floor(head.Z / 10d) * 10d);
        const int halfSpan = 30;
        Color grid = new Color(.18f, .31f, .37f, .42f);
        Color major = new Color(.33f, .61f, .7f, .72f);
        for (int i = -halfSpan; i <= halfSpan; i++)
        {
            float x = cx + i;
            float z = cz + i;
            Color xTint = ((int)Math.Round(x)) % 10 == 0 ? major : grid;
            Color zTint = ((int)Math.Round(z)) % 10 == 0 ? major : grid;
            DrawGuideLine(new Vector3(x, y, cz - halfSpan),
                new Vector3(x, y, cz + halfSpan), xTint);
            DrawGuideLine(new Vector3(cx - halfSpan, y, z),
                new Vector3(cx + halfSpan, y, z), zTint);
        }

        Color link = new Color(.62f, .8f, .91f, .78f);
        ReplayPoseValue left = partnerOnly ? PartyPose(1, 1) : EvaluatedPose(1);
        ReplayPoseValue right = partnerOnly ? PartyPose(1, 2) : EvaluatedPose(2);
        Vector3 at = ReplayPosition(head);
        if (left.Valid)
            DrawGuideLine(at + Vector3.down * .22f,
                ReplayPosition(left), link);
        if (right.Valid)
            DrawGuideLine(at + Vector3.down * .22f,
                ReplayPosition(right), link);
    }

    private void DrawOfflineTab(float x, float y, float w)
    {
        StudioSection(x, y, w, "OFFLINE REPLAY LAB");
        StudioFill(new Rect(x, y + 30f, w, 84f), StudioSurface);
        StudioBorder(new Rect(x, y + 30f, w, 84f), StudioStroke);
        StudioText(new Rect(x + 12f, y + 36f, w - 24f, 24f),
            _offlineReplayActive ? "OFFLINE POSE STAGE ACTIVE" :
                "PREVIEW WITHOUT JOINING A SERVER", _studioHeading);
        StudioText(new Rect(x + 12f, y + 62f, w - 24f, 19f),
            "Replay meshes: session cache only (no disk geometry).", _studioMuted);
        StudioText(new Rect(x + 12f, y + 82f, w - 24f, 19f),
            "Old clips after restart: bone/axis fallback.", _studioMuted);

        if (StudioButton(new Rect(x, y + 125f, w, 44f),
            _offlineReplayActive ? "END OFFLINE PREVIEW" :
                "START OFFLINE PREVIEW", _offlineReplayActive, true))
        {
            if (_offlineReplayActive) StopOfflineReplay();
            else StartOfflineReplay();
        }

        float half = (w - 10f) / 2f;
        if (StudioButton(new Rect(x, y + 178f, half, 36f),
            "Load latest clip", false))
        {
            LoadLatestReplay(false);
        }
        if (StudioButton(new Rect(x + half + 10f, y + 178f,
            half, 36f), "Focus recorded head", false))
            FocusReplayHead();

        StudioSection(x, y + 226f, w, "INSTALLED WORLD ASSET RESEARCH");
        if (StudioButton(new Rect(x, y + 265f, half, 40f),
            "EXPORT ASSET INVENTORY", false))
            CaptureOfflineInventory();
        if (StudioButton(new Rect(x + half + 10f, y + 265f,
            half, 40f), "CACHED WORLD: " +
            (_showCachedWorld ? "ON" : "OFF"), _showCachedWorld))
        {
            _showCachedWorld = !_showCachedWorld;
            UpdateOfflineVisuals();
        }

        StudioFill(new Rect(x, y + 314f, w, 85f), StudioSurface);
        StudioBorder(new Rect(x, y + 314f, w, 85f), StudioStroke);
        StudioText(new Rect(x + 11f, y + 319f, w - 22f, 25f),
            _offlineStatus, _studioMuted);
        StudioText(new Rect(x + 11f, y + 351f, w - 22f, 19f),
            "Clip play / seek controls: REPLAY tab.", _studioMuted);
        StudioText(new Rect(x + 11f, y + 373f, w - 22f, 19f),
            "Cached: " + ReplayVisualAssets.WorldPartCount +
            " world / " + ReplayVisualAssets.AvatarPartCount +
            " avatar / " + ReplayVisualAssets.RigCount +
            " actor meshes (memory only)", _studioMuted);
    }
}
