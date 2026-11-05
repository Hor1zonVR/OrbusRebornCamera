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
            _desktop.cullingMask = 0;
            _desktop.clearFlags = CameraClearFlags.SolidColor;
            _desktop.backgroundColor = new Color(.025f, .036f, .051f, 1f);
            _desktop.nearClipPlane = .035f;
            _desktop.farClipPlane = 1500f;
            _desktop.fieldOfView = 75f;
            _desktop.aspect = Mathf.Max(1f, Screen.width) /
                Mathf.Max(1f, Screen.height);
            _desktop.enabled = true;

            _offlineStatus = "Offline pose preview ACTIVE. No world geometry.";
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
        if (!head.Valid) return;

        float cx = Mathf.Round(head.X);
        float cz = Mathf.Round(head.Z);
        float y = head.Y - 1.65f;
        Color grid = new Color(.18f, .31f, .37f, .42f);
        Color main = new Color(.33f, .61f, .7f, .72f);
        for (int i = -10; i <= 10; i++)
        {
            DrawGuideLine(new Vector3(cx + i, y, cz - 10f),
                new Vector3(cx + i, y, cz + 10f), i == 0 ? main : grid);
            DrawGuideLine(new Vector3(cx - 10f, y, cz + i),
                new Vector3(cx + 10f, y, cz + i), i == 0 ? main : grid);
        }

        Color link = new Color(.62f, .8f, .91f, .78f);
        ReplayPoseValue left = EvaluatedPose(1);
        ReplayPoseValue right = EvaluatedPose(2);
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
            "Uses recorded poses + generated grid only.", _studioMuted);
        StudioText(new Rect(x + 12f, y + 82f, w - 24f, 19f),
            "Real zone geometry needs a separate loader.", _studioMuted);

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
            LoadLatestReplay(!_offlineReplayActive);
        }
        if (StudioButton(new Rect(x + half + 10f, y + 178f,
            half, 36f), "Focus recorded head", false))
            FocusReplayHead();

        StudioSection(x, y + 226f, w, "INSTALLED WORLD ASSET RESEARCH");
        if (StudioButton(new Rect(x, y + 265f, w, 40f),
            "EXPORT READ-ONLY WORLD INVENTORY", false))
            CaptureOfflineInventory();

        StudioFill(new Rect(x, y + 314f, w, 85f), StudioSurface);
        StudioBorder(new Rect(x, y + 314f, w, 85f), StudioStroke);
        StudioText(new Rect(x + 11f, y + 319f, w - 22f, 25f),
            _offlineStatus, _studioMuted);
        StudioText(new Rect(x + 11f, y + 351f, w - 22f, 19f),
            "Clip play / seek controls: REPLAY tab.", _studioMuted);
        StudioText(new Rect(x + 11f, y + 373f, w - 22f, 19f),
            "Never switches worlds or connects to a server.", _studioMuted);
    }
}
