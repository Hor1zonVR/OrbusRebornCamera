using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbusBetterMirror;

/// <summary>
/// Cinematic paths for the same independent desktop camera used in both VR
/// and desktop launches. A path never moves the player or XR rig.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private const int MaxKeyframes = 80;
    private const string PathFileName = "RebornCamKeyframes.txt";

    private sealed class CameraKeyframe
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float HorizontalFov;
    }

    private readonly List<CameraKeyframe> _keyframes = new();
    private bool _playingKeyframes;
    private float _pathElapsed;
    private float _playbackHorizontalFov = 110f;
    private string _keyframeScene = "";
    private string _playbackScene = "";

    private string PathFile => Path.Combine(
        BepInEx.Paths.ConfigPath, PathFileName);

    private static string ActiveSceneName()
    {
        try { return SceneManager.GetActiveScene().name ?? ""; }
        catch { return ""; }
    }

    private static string N(float n) =>
        n.ToString("R", CultureInfo.InvariantCulture);

    private static bool F(string s, out float n) =>
        float.TryParse(s, NumberStyles.Float,
            CultureInfo.InvariantCulture, out n) &&
        !float.IsNaN(n) && !float.IsInfinity(n);

    private void AddKeyframe()
    {
        if (!IsCameraActive || _desktop == null)
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] Enable the creator camera before adding a shot.");
            return;
        }

        if (_playingKeyframes)
            StopKeyframePlayback();

        if (_keyframes.Count >= MaxKeyframes)
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] Maximum of " + MaxKeyframes + " shots reached.");
            return;
        }

        string scene = ActiveSceneName();
        if (_keyframes.Count > 0 && _keyframeScene != scene)
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] This is a different world/scene. " +
                "Clear the old path before recording a new one.");
            return;
        }

        if (_keyframes.Count == 0)
            _keyframeScene = scene;

        _keyframes.Add(new CameraKeyframe
        {
            Position = _desktop.transform.position,
            Rotation = _desktop.transform.rotation,
            HorizontalFov = GetHorizontalFov()
        });

        SaveKeyframes();
        Plugin.ModLogger.LogInfo(
            "[Keyframes] Added shot " + _keyframes.Count +
            " of " + MaxKeyframes + ".");
    }

    private void RemoveLastKeyframe()
    {
        StopKeyframePlayback();
        if (_keyframes.Count == 0)
            return;

        _keyframes.RemoveAt(_keyframes.Count - 1);
        if (_keyframes.Count == 0)
            _keyframeScene = "";
        SaveKeyframes();
        Plugin.ModLogger.LogInfo(
            "[Keyframes] Removed last shot. Remaining: " +
            _keyframes.Count + ".");
    }

    private void ClearKeyframes()
    {
        StopKeyframePlayback();
        _keyframes.Clear();
        _keyframeScene = "";
        SaveKeyframes();
        Plugin.ModLogger.LogInfo("[Keyframes] Path cleared.");
    }

    private void StartKeyframePlayback()
    {
        if (_keyframes.Count < 2)
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] Add at least two shots before playing.");
            return;
        }

        if (_keyframeScene != ActiveSceneName())
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] Path belongs to a different scene. " +
                "Record a new path after changing zones.");
            return;
        }

        if (_playingKeyframes)
        {
            StopKeyframePlayback();
            return;
        }

        if (_mode != CameraMode.Drone && !EnterFreecam())
            return;

        if (!IsCameraActive)
            return;

        ReleaseDronePointer();
        _droneVelocity = Vector3.zero;
        _pathElapsed = 0f;
        _playbackScene = ActiveSceneName();
        _playbackHorizontalFov = _keyframes[0].HorizontalFov;
        _playingKeyframes = true;

        Plugin.ModLogger.LogInfo(
            "[Keyframes] Playing " + _keyframes.Count +
            " shots over " +
            Plugin.KeyframeDuration.Value.ToString("0.0") + "s.");
    }

    private void UpdateKeyframePlayback(float delta)
    {
        if (!_playingKeyframes)
            return;

        if (_mode != CameraMode.Drone || !IsCameraActive ||
            _keyframes.Count < 2 || ActiveSceneName() != _playbackScene)
        {
            StopKeyframePlayback();
            return;
        }

        float seconds = Mathf.Clamp(
            Plugin.KeyframeDuration.Value, 2f, 120f);
        _pathElapsed = Mathf.Min(seconds,
            _pathElapsed + Mathf.Max(0f, delta));

        float progress = Mathf.Clamp01(_pathElapsed / seconds);
        float sections = _keyframes.Count - 1;
        float segmentProgress = progress * sections;
        int index = Math.Min(_keyframes.Count - 2,
            Mathf.FloorToInt(segmentProgress));
        float fraction = Mathf.Clamp01(segmentProgress - index);
        // Smoothly ease between shots. Rotation uses quaternion interpolation
        // so turns never jump across the 0/360-degree boundary.
        float smooth = Mathf.SmoothStep(0f, 1f, fraction);
        CameraKeyframe from = _keyframes[index];
        CameraKeyframe to = _keyframes[index + 1];

        _dronePosition = EvaluateKeyframePosition(index, smooth);
        Quaternion rotation = Quaternion.Slerp(
            from.Rotation, to.Rotation, smooth);
        // Preserve the FULL 3-axis quaternion while replaying (including
        // camera roll), without round-tripping through Euler angles.
        _dronePlaybackRotation = rotation;
        Vector3 euler = rotation.eulerAngles;
        _droneYaw = euler.y;
        _dronePitch = Mathf.Clamp(
            Mathf.DeltaAngle(0f, euler.x), -89f, 89f);
        _droneRoll = Mathf.DeltaAngle(0f, euler.z);
        _playbackHorizontalFov = Mathf.Lerp(
            from.HorizontalFov, to.HorizontalFov, smooth);

        if (progress >= 1f)
        {
            // The final camera orientation is already copied into roll,
            // yaw and pitch above, so ending playback does not snap to 0°.
            StopKeyframePlayback();
            Plugin.ModLogger.LogInfo(
                "[Keyframes] Playback finished at final shot.");
        }
    }

    // Shared by playback and the desktop-only preview. Use the exact same
    // cubic path geometry, so the line shows where the camera will fly.
    private Vector3 EvaluateKeyframePosition(int segment, float t)
    {
        int last = _keyframes.Count - 1;
        if (last < 1) return _dronePosition;
        segment = Mathf.Clamp(segment, 0, last - 1);
        t = Mathf.Clamp01(t);
        Vector3 p1 = _keyframes[segment].Position;
        Vector3 p2 = _keyframes[segment + 1].Position;
        if (last == 1) return Vector3.Lerp(p1, p2, t);
        Vector3 p0 = _keyframes[Math.Max(0, segment - 1)].Position;
        Vector3 p3 = _keyframes[Math.Min(last, segment + 2)].Position;
        // Catmull-Rom spline, continuous through each keyframe. Start/end
        // tangents use clamped endpoint positions to avoid extrapolation.
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private float KeyframePathLength()
    {
        float total = 0f;
        if (_keyframes.Count < 2) return 0f;
        for (int i = 0; i < _keyframes.Count - 1; i++)
        {
            Vector3 prev = _keyframes[i].Position;
            for (int step = 1; step <= 12; step++)
            {
                Vector3 now = EvaluateKeyframePosition(
                    i, step / 12f);
                total += Vector3.Distance(prev, now);
                prev = now;
            }
        }
        return total;
    }

    private void StopKeyframePlayback()
    {
        if (!_playingKeyframes)
            return;

        _playingKeyframes = false;
        _droneVelocity = Vector3.zero;
        ApplyFov();
    }

    private void SaveKeyframes()
    {
        try
        {
            Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
            // Invariant numbers + human-readable plain text, one path per
            // installation. Paths contain world coordinates but no player or
            // other entity positions. Never include these in diagnostic logs.
            var lines = new List<string>
            {
                "# RebornCam creator camera path v1",
                "# scene=" + _keyframeScene,
                "# x|y|z|qx|qy|qz|qw|horizontal_fov"
            };
            foreach (CameraKeyframe k in _keyframes)
            {
                lines.Add(string.Join("|", new[]
                {
                    N(k.Position.x), N(k.Position.y), N(k.Position.z),
                    N(k.Rotation.x), N(k.Rotation.y),
                    N(k.Rotation.z), N(k.Rotation.w),
                    N(k.HorizontalFov)
                }));
            }

            // Safe update without truncating the existing file on failure.
            string tmp = PathFile + ".tmp";
            File.WriteAllLines(tmp, lines);
            if (File.Exists(PathFile))
                File.Delete(PathFile);
            File.Move(tmp, PathFile);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] Could not save path: " + ex.Message);
        }
    }

    private void LoadKeyframes()
    {
        if (!File.Exists(PathFile))
            return;

        try
        {
            var loaded = new List<CameraKeyframe>();
            string scene = "";
            foreach (string line in File.ReadAllLines(PathFile))
            {
                if (line.StartsWith("# scene=", StringComparison.Ordinal))
                {
                    scene = line.Substring(8);
                    continue;
                }
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] pieces = line.Split('|');
                if (pieces.Length != 8 ||
                    !F(pieces[0], out float x) ||
                    !F(pieces[1], out float y) ||
                    !F(pieces[2], out float z) ||
                    !F(pieces[3], out float qx) ||
                    !F(pieces[4], out float qy) ||
                    !F(pieces[5], out float qz) ||
                    !F(pieces[6], out float qw) ||
                    !F(pieces[7], out float fov))
                    continue;

                if (fov < 50f || fov > 150f ||
                    Math.Abs(qx) + Math.Abs(qy) +
                    Math.Abs(qz) + Math.Abs(qw) < 0.001f)
                    continue;

                loaded.Add(new CameraKeyframe
                {
                    Position = new Vector3(x, y, z),
                    Rotation = new Quaternion(qx, qy, qz, qw),
                    HorizontalFov = fov
                });
                if (loaded.Count == MaxKeyframes)
                    break;
            }

            _keyframes.Clear();
            _keyframes.AddRange(loaded);
            _keyframeScene = scene;
            Plugin.ModLogger.LogInfo(
                "[Keyframes] Loaded " + _keyframes.Count +
                " shots from " + PathFile + ".");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[Keyframes] Could not load camera path: " + ex.Message);
        }
    }
}
