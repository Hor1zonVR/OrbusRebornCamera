using System;
using System.IO;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// First replay viewer: read a saved local pose clip, scrub/pause/play its
/// tracked headset and hands, and inspect them with the existing freecam.
/// Visuals are drawn via GL only when RC's DESKTOP camera renders. Nothing
/// is spawned into the game world, and VR tracking/avatars stay unchanged.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private ReplayClipData? _replayClip;
    private double _replayElapsed;
    private bool _replayPlaying;
    private string _replayMessage = "No clip loaded";
    private bool _replayRenderErrorLogged;
    private const float ReplayHeadRadius = .25f;
    private const float ReplayHandRadius = .14f;

    private static string ReplayTime(double seconds)
    {
        int t = Math.Max(0, (int)seconds);
        return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
    }

    private void LoadLatestReplay(bool useLiveWorld = true)
    {
        try
        {
            string folder = Path.Combine(BepInEx.Paths.ConfigPath,
                "OrbusRebornCamera", "Clips");
            if (!Directory.Exists(folder))
            {
                _replayMessage = "No saved clips directory yet";
                Plugin.ModLogger.LogWarning("[ReplayViewer] " + _replayMessage);
                return;
            }

            string[] files = Directory.GetFiles(folder, "RC_Clip_*.tsv");
            if (files.Length == 0)
            {
                _replayMessage = "No RC_Clip_*.tsv files found";
                return;
            }

            // Normal saved files are timestamped. Sort by modified time so
            // users can copy an older v1 clip in and view that one as well.
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b)
                .CompareTo(File.GetLastWriteTimeUtc(a)));
            ReplayClipData loaded = ReplayClipReader.ReadFile(files[0]);
            StopKeyframePlayback();
            _replayClip = loaded;
            _replayElapsed = 0d;
            _replayPlaying = false;
            _replayMessage = loaded.Count + " frames | " +
                loaded.TeleportCount + " teleports | " +
                loaded.GapCount + " gaps";
            Plugin.ModLogger.LogInfo("[ReplayViewer] Loaded " +
                loaded.FileName + " " + _replayMessage +
                " duration=" + ReplayTime(loaded.Duration) +
                " zones=" + loaded.Frames[0].Zone);
            if (_offlineReplayActive)
                FocusReplayHead();
            else if (useLiveWorld)
            {
                if (_mode != CameraMode.Drone && !EnterFreecam())
                    Plugin.ModLogger.LogWarning(
                        "[ReplayViewer] Loaded clip; freecam unavailable. " +
                        "Use OFFLINE tab for no-server pose preview.");
                else
                    FocusReplayHead();
            }
        }
        catch (Exception ex)
        {
            _replayMessage = "Load failed - check BepInEx log";
            Plugin.ModLogger.LogError(
                "[ReplayViewer] Could not load local pose clip: " + ex);
        }
    }

    private void ClearReplay()
    {
        _replayPlaying = false;
        _replayClip = null;
        _replayElapsed = 0d;
        _replayMessage = "Viewer closed (buffer unaffected)";
        Plugin.ModLogger.LogInfo("[ReplayViewer] Viewer closed.");
    }

    private void ToggleReplayPlayback()
    {
        if (_replayClip == null) return;
        if (_replayElapsed >= _replayClip.Duration)
            _replayElapsed = 0d;
        _replayPlaying = !_replayPlaying;
    }

    private void SeekReplay(float seconds)
    {
        if (_replayClip == null) return;
        _replayElapsed = Math.Max(0d, Math.Min(_replayClip.Duration, seconds));
        _replayPlaying = false;
    }

    private void UpdateReplayPlayback(float delta)
    {
        if (!_replayPlaying || _replayClip == null) return;
        _replayElapsed = Math.Min(_replayClip.Duration,
            _replayElapsed + Math.Max(0f, delta));
        if (_replayElapsed >= _replayClip.Duration)
            _replayPlaying = false;
    }

    private ReplayPoseValue EvaluatedPose(int which)
    {
        if (_replayClip == null) return default;
        _replayClip.Evaluate(_replayElapsed, out ReplayClipFrame a,
            out ReplayClipFrame b, out float blend);
        ReplayPoseValue left = which == 0 ? a.Head :
            which == 1 ? a.Left : a.Right;
        ReplayPoseValue right = which == 0 ? b.Head :
            which == 1 ? b.Left : b.Right;
        if (!left.Valid || blend <= 0f || !right.Valid) return left;

        Vector3 pos = Vector3.Lerp(ReplayPosition(left),
            ReplayPosition(right), blend);
        Quaternion rot = Quaternion.Slerp(ReplayRotation(left),
            ReplayRotation(right), blend);
        return new ReplayPoseValue
        {
            Valid = true,
            X = pos.x, Y = pos.y, Z = pos.z,
            Qx = rot.x, Qy = rot.y, Qz = rot.z, Qw = rot.w
        };
    }

    private static Vector3 ReplayPosition(ReplayPoseValue pose) =>
        new Vector3(pose.X, pose.Y, pose.Z);

    private static Quaternion ReplayRotation(ReplayPoseValue pose) =>
        new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw);

    private void FocusReplayHead()
    {
        if (_replayClip == null) return;
        if (_mode != CameraMode.Drone && !EnterFreecam()) return;
        ReplayPoseValue head = EvaluatedPose(0);
        if (!head.Valid) return;

        Vector3 position = ReplayPosition(head);
        Vector3 backward = ReplayRotation(head) * Vector3.back;
        _dronePosition = position + backward * 3f + Vector3.up * 1.1f;
        Quaternion look = Quaternion.LookRotation(
            (position - _dronePosition).normalized, Vector3.up);
        Vector3 euler = look.eulerAngles;
        _droneYaw = euler.y;
        _dronePitch = Mathf.DeltaAngle(0f, euler.x);
        _droneRoll = 0f;
        _droneVelocity = Vector3.zero;
        Plugin.ModLogger.LogInfo(
            "[ReplayViewer] Framed recorded headset with desktop freecam.");
    }

    private static void DrawReplayMarker(ReplayPoseValue pose,
        float radius, Color tint)
    {
        if (!pose.Valid) return;
        Vector3 at = ReplayPosition(pose);
        DrawGuideLine(at - Vector3.right * radius,
            at + Vector3.right * radius, tint);
        DrawGuideLine(at - Vector3.up * radius,
            at + Vector3.up * radius, tint);
        DrawGuideLine(at - Vector3.forward * radius,
            at + Vector3.forward * radius, tint);
        Vector3 forward = ReplayRotation(pose) * Vector3.forward;
        DrawGuideLine(at, at + forward * radius * 2.2f, tint);
    }

    private void RenderReplayMarkers()
    {
        if (_replayClip == null || _mode != CameraMode.Drone ||
            !IsCameraActive || _desktop == null ||
            Camera.current != _desktop)
            return;

        try
        {
            if (!EnsurePathPreviewMaterial() ||
                _pathPreviewMaterial == null ||
                !_pathPreviewMaterial.SetPass(0))
                return;
            GL.PushMatrix();
            try
            {
                GL.MultMatrix(Matrix4x4.identity);
                GL.Begin(GL.LINES);
                try
                {
                    DrawOfflineStageGrid();
                    DrawReplayMarker(EvaluatedPose(0),
                        ReplayHeadRadius, new Color(.32f, .87f, 1f, 1f));
                    DrawReplayMarker(EvaluatedPose(1),
                        ReplayHandRadius, new Color(1f, .65f, .32f, 1f));
                    DrawReplayMarker(EvaluatedPose(2),
                        ReplayHandRadius, new Color(.39f, 1f, .62f, 1f));
                }
                finally { GL.End(); }
            }
            finally { GL.PopMatrix(); }
        }
        catch (Exception ex)
        {
            if (!_replayRenderErrorLogged)
            {
                _replayRenderErrorLogged = true;
                Plugin.ModLogger.LogWarning(
                    "[ReplayViewer] Marker rendering unavailable: " + ex);
            }
            // The live game/camera must remain unaffected by preview errors.
        }
    }
}
