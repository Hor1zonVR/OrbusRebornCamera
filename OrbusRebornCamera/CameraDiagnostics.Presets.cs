using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Portable cinematic camera presets. Saved movements use local coordinates,
/// never global scene coordinates. Subject anchors are manually picked world
/// points; live-player tracking and replay reconstruction are separate work.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private const string PresetExtension = ".rcpreset";
    private readonly List<string> _presetFiles = new();
    private bool _presetTab;
    private bool _presetsLoaded;
    private bool _presetConfirmOverwrite;
    private bool _presetMirror;
    private bool _presetReverse;
    private bool _presetSaveSubject;
    private int _presetIndex;
    private string _presetName = "My Camera Move";
    private float _presetSpeed = 1f;
    private float _presetAimDistance = 3f;
    private bool _presetHasSubject;
    private string _presetSubjectScene = "";
    private Vector3 _presetSubjectPoint;
    private Quaternion _presetSubjectOrientation = Quaternion.identity;
    private string _presetStatus = "Save a path or choose an existing preset.";

    private sealed class CameraPreset
    {
        public string Name = "";
        public bool SubjectRelative;
        public float Duration;
        public float StartFov;
        public readonly List<CameraKeyframe> Frames = new();
    }

    private static string PresetDirectory => Path.Combine(
        BepInEx.Paths.ConfigPath, "RebornCamPresets");

    private static string SafePresetName(string name)
    {
        name = name.Trim();
        if (name.Length > 50) name = name.Substring(0, 50);
        var chars = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            chars[i] = char.IsLetterOrDigit(c) || c == ' ' ||
                       c == '-' || c == '_' ? c : '_';
        }
        string safe = new string(chars).Trim(' ', '.', '_');
        return safe.Length == 0 ? "Camera Move" : safe;
    }

    private void RefreshPresets()
    {
        _presetsLoaded = true;
        _presetFiles.Clear();
        try
        {
            Directory.CreateDirectory(PresetDirectory);
            _presetFiles.AddRange(Directory.GetFiles(
                PresetDirectory, "*" + PresetExtension));
            _presetFiles.Sort(StringComparer.OrdinalIgnoreCase);
            _presetIndex = Math.Min(Math.Max(0, _presetIndex),
                Math.Max(0, _presetFiles.Count - 1));
        }
        catch (Exception ex)
        {
            _presetStatus = "Cannot read preset directory.";
            Plugin.ModLogger.LogWarning("[Presets] " + ex.Message);
        }
    }

    private void CaptureSubjectAnchor()
    {
        if (!IsCameraActive || _desktop == null)
        {
            _presetStatus = "Enable the creator camera first.";
            return;
        }

        Transform t = _desktop.transform;
        _presetSubjectPoint = t.position +
            t.forward * Mathf.Clamp(_presetAimDistance, 0.5f, 20f);
        _presetSubjectOrientation = t.rotation;
        _presetSubjectScene = ActiveSceneName();
        _presetHasSubject = true;
        _presetStatus = "Subject anchor captured. This is a fixed point, not live tracking.";
        Plugin.ModLogger.LogInfo("[Presets] Captured manual subject anchor.");
    }

    private void SaveCameraPreset()
    {
        if (_keyframes.Count < 2)
        {
            _presetStatus = "Add at least 2 camera shots first.";
            return;
        }

        if (_keyframeScene != ActiveSceneName())
        {
            _presetStatus = "The camera path belongs to another scene.";
            return;
        }

        if (_presetSaveSubject &&
            (!_presetHasSubject || _presetSubjectScene != ActiveSceneName()))
        {
            _presetStatus = "Capture a subject anchor in this scene first.";
            return;
        }

        string name = SafePresetName(_presetName);
        string path = Path.Combine(PresetDirectory, name + PresetExtension);
        if (File.Exists(path) && !_presetConfirmOverwrite)
        {
            _presetConfirmOverwrite = true;
            _presetStatus = "Click SAVE again to overwrite " + name + ".";
            return;
        }

        try
        {
            Directory.CreateDirectory(PresetDirectory);
            CameraKeyframe first = _keyframes[0];
            Quaternion basis = first.Rotation;
            Quaternion inverse = Quaternion.Inverse(basis);
            Vector3 origin = _presetSaveSubject
                ? _presetSubjectPoint : first.Position;
            var lines = new List<string>
            {
                "# RebornCam portable preset v1",
                "# name=" + name,
                "# mode=" + (_presetSaveSubject ? "subject" : "camera"),
                "# duration=" + N(Mathf.Clamp(
                    Plugin.KeyframeDuration.Value, 0.2f, 120f)),
                "# start_fov=" + N(first.HorizontalFov),
                "# local_x|local_y|local_z|qx|qy|qz|qw|fov_delta"
            };

            foreach (CameraKeyframe k in _keyframes)
            {
                Vector3 localPos = inverse * (k.Position - origin);
                Quaternion localRot = inverse * k.Rotation;
                lines.Add(string.Join("|", new[]
                {
                    N(localPos.x), N(localPos.y), N(localPos.z),
                    N(localRot.x), N(localRot.y),
                    N(localRot.z), N(localRot.w),
                    N(k.HorizontalFov - first.HorizontalFov)
                }));
            }

            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            _presetName = name;
            _presetConfirmOverwrite = false;
            RefreshPresets();
            _presetIndex = _presetFiles.FindIndex(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (_presetIndex < 0) _presetIndex = 0;
            _presetStatus = "Saved " + name + " (portable preset).";
            Plugin.ModLogger.LogInfo("[Presets] Saved " + name + ".");
        }
        catch (Exception ex)
        {
            _presetStatus = "Failed to save preset.";
            Plugin.ModLogger.LogWarning("[Presets] Save error: " + ex.Message);
        }
    }

    private CameraPreset? ReadSelectedPreset()
    {
        if (!_presetsLoaded) RefreshPresets();
        if (_presetFiles.Count == 0 || _presetIndex < 0 ||
            _presetIndex >= _presetFiles.Count)
            return null;

        try
        {
            CameraPreset preset = new();
            foreach (string line in File.ReadAllLines(_presetFiles[_presetIndex]))
            {
                if (line.StartsWith("# name=", StringComparison.Ordinal))
                    preset.Name = line.Substring(7);
                else if (line == "# mode=subject")
                    preset.SubjectRelative = true;
                else if (line.StartsWith("# duration=", StringComparison.Ordinal) &&
                         F(line.Substring(11), out float duration))
                    preset.Duration = Mathf.Clamp(duration, 0.2f, 120f);
                else if (line.StartsWith("# start_fov=", StringComparison.Ordinal) &&
                         F(line.Substring(12), out float startFov))
                    preset.StartFov = startFov;
                else if (!line.StartsWith("#", StringComparison.Ordinal) &&
                         !string.IsNullOrWhiteSpace(line))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 8 ||
                        !F(parts[0], out float x) ||
                        !F(parts[1], out float y) ||
                        !F(parts[2], out float z) ||
                        !F(parts[3], out float qx) ||
                        !F(parts[4], out float qy) ||
                        !F(parts[5], out float qz) ||
                        !F(parts[6], out float qw) ||
                        !F(parts[7], out float fovDelta))
                        throw new FormatException("Invalid frame data.");
                    if (Math.Abs(qx) + Math.Abs(qy) +
                        Math.Abs(qz) + Math.Abs(qw) < 0.001f)
                        throw new FormatException("Invalid rotation.");
                    preset.Frames.Add(new CameraKeyframe
                    {
                        Position = new Vector3(x, y, z),
                        Rotation = new Quaternion(qx, qy, qz, qw),
                        HorizontalFov = fovDelta
                    });
                    if (preset.Frames.Count > MaxKeyframes)
                        throw new FormatException("Too many frames.");
                }
            }

            if (preset.Frames.Count < 2 || preset.Duration <= 0f ||
                preset.StartFov < 50f || preset.StartFov > 150f)
                throw new FormatException("Incomplete preset.");
            return preset;
        }
        catch (Exception ex)
        {
            _presetStatus = "Could not load selected preset.";
            Plugin.ModLogger.LogWarning("[Presets] Read error: " + ex.Message);
            return null;
        }
    }

    private void ApplySelectedPreset(bool play)
    {
        CameraPreset? preset = ReadSelectedPreset();
        if (preset == null)
        {
            _presetStatus = "Select a valid saved preset first.";
            return;
        }

        if (!IsCameraActive && !TryEnableCamera(true))
        {
            _presetStatus = "Camera unavailable; load a world first.";
            return;
        }
        if (_desktop == null) return;

        if (preset.SubjectRelative &&
            (!_presetHasSubject || _presetSubjectScene != ActiveSceneName()))
        {
            _presetStatus = "Aim at your new subject, then CAPTURE ANCHOR.";
            return;
        }

        StopKeyframePlayback();
        // The camera is moved by the existing drone/keyframe system only.
        // Neither the XR rig nor any live gameplay object is modified.
        if (_mode != CameraMode.Drone && !EnterFreecam())
            return;

        Quaternion basis = preset.SubjectRelative
            ? _presetSubjectOrientation : _desktop.transform.rotation;
        Vector3 origin = preset.SubjectRelative
            ? _presetSubjectPoint : _desktop.transform.position;
        float startFov = preset.SubjectRelative
            ? preset.StartFov : GetHorizontalFov();
        int last = preset.Frames.Count - 1;

        // Reversed camera-relative moves start exactly at the *current*
        // camera pose, rather than teleporting to their old final frame.
        CameraKeyframe end = preset.Frames[last];
        Quaternion endInverse = Quaternion.Inverse(end.Rotation);
        float endFov = end.HorizontalFov;
        var transformed = new List<CameraKeyframe>();

        for (int i = 0; i < preset.Frames.Count; i++)
        {
            CameraKeyframe source = preset.Frames[_presetReverse ? last - i : i];
            Vector3 localPosition = source.Position;
            Quaternion localRotation = source.Rotation;
            float fovDelta = source.HorizontalFov;

            if (_presetReverse && !preset.SubjectRelative)
            {
                localPosition = endInverse * (localPosition - end.Position);
                localRotation = endInverse * localRotation;
                fovDelta -= endFov;
            }

            // Reflection in the local right/left axis. This mirrors the
            // camera's motion and orientation, NOT the rendered image.
            if (_presetMirror)
            {
                localPosition.x = -localPosition.x;
                localRotation = new Quaternion(
                    localRotation.x, -localRotation.y,
                    -localRotation.z, localRotation.w);
            }

            transformed.Add(new CameraKeyframe
            {
                Position = origin + basis * localPosition,
                Rotation = basis * localRotation,
                HorizontalFov = Mathf.Clamp(
                    startFov + fovDelta, 50f, 150f)
            });
        }

        try
        {
            // Keep the preceding scene path recoverable, including across
            // restarts, before replacing it with the newly applied preset.
            if (File.Exists(PathFile))
                File.Copy(PathFile, PathFile + ".before-preset", true);
        }
        catch (Exception ex)
        {
            _presetStatus = "Cannot back up your current path; not applying.";
            Plugin.ModLogger.LogWarning("[Presets] Backup failed: " + ex.Message);
            return;
        }

        _keyframes.Clear();
        _keyframes.AddRange(transformed);
        _keyframeScene = ActiveSceneName();
        Plugin.KeyframeDuration.Value = Mathf.Clamp(
            preset.Duration / Mathf.Clamp(_presetSpeed, 0.25f, 4f),
            0.2f, 120f);
        SaveKeyframes();
        _presetStatus = "Applied " + preset.Name + " (" +
            (_presetReverse ? "reversed " : "") +
            (_presetMirror ? "mirrored" : "normal") + ").";

        if (play)
        {
            StartKeyframePlayback();
            if (_playingKeyframes) SetPanelVisibility(false);
        }
        Plugin.ModLogger.LogInfo("[Presets] " + _presetStatus);
    }

    private void DrawPresetPanel(float px, float pw, float pos)
    {
        if (!_presetsLoaded) RefreshPresets();
        GUI.Label(new Rect(px, pos, pw, 21),
            "PORTABLE CAMERA PRESETS", _panelHeading);
        pos += 26;
        GUI.Label(new Rect(px, pos, pw, 36),
            "Save a movement once and replay it in any world.\n" +
            "The original camera coordinates are never reused.",
            _panelTiny);
        pos += 49;

        GUI.Label(new Rect(px, pos, pw, 20),
            "PRESET NAME (when saving)", _panelHeading);
        pos += 25;
        string editedName = GUI.TextField(
            new Rect(px, pos, pw, 30), _presetName, 50);
        if (editedName != _presetName)
        {
            _presetName = editedName;
            _presetConfirmOverwrite = false;
        }
        pos += 43;

        if (PanelButton(new Rect(px, pos, pw, 35),
            _presetSaveSubject ? "SAVE MODE: SUBJECT RELATIVE" :
            "SAVE MODE: CAMERA RELATIVE", _presetSaveSubject))
        {
            _presetSaveSubject = !_presetSaveSubject;
            _presetConfirmOverwrite = false;
        }
        pos += 43;
        if (PanelButton(new Rect(px, pos, pw, 36),
            _presetConfirmOverwrite ? "CONFIRM OVERWRITE" :
            "Save current keyframes as preset", _presetConfirmOverwrite))
            SaveCameraPreset();

        pos += 51;
        FillPanelRect(new Rect(px, pos, pw, 1), PanelStroke);
        pos += 12;
        GUI.Label(new Rect(px, pos, pw, 20), "PRESET LIBRARY", _panelHeading);
        pos += 27;
        float arrow = 45;
        if (PanelButton(new Rect(px, pos, arrow, 36), "<", false) &&
            _presetFiles.Count > 0)
            _presetIndex = (_presetIndex - 1 + _presetFiles.Count) %
                _presetFiles.Count;
        if (PanelButton(new Rect(px + pw - arrow, pos, arrow, 36),
            ">", false) && _presetFiles.Count > 0)
            _presetIndex = (_presetIndex + 1) % _presetFiles.Count;

        string chosen = _presetFiles.Count == 0
            ? "No presets saved"
            : Path.GetFileNameWithoutExtension(_presetFiles[_presetIndex]);
        GUI.Label(new Rect(px + arrow + 7, pos + 2,
            pw - arrow * 2 - 14, 31), chosen, _panelText);
        pos += 47;

        float half = (pw - 10) / 2f;
        if (PanelButton(new Rect(px, pos, half, 35),
            _presetReverse ? "REVERSE: ON" : "Reverse: OFF",
            _presetReverse)) _presetReverse = !_presetReverse;
        if (PanelButton(new Rect(px + half + 10, pos, half, 35),
            _presetMirror ? "MIRROR: ON" : "Mirror: OFF",
            _presetMirror)) _presetMirror = !_presetMirror;
        pos += 46;

        _presetSpeed = Mathf.Round(
            DrawPanelSlider(px, pos, pw, "Playback speed",
                _presetSpeed, 0.25f, 4f,
                _presetSpeed.ToString("0.00") + "x") * 4f) / 4f;
        pos += 73;

        FillPanelRect(new Rect(px, pos, pw, 1), PanelStroke);
        pos += 12;
        GUI.Label(new Rect(px, pos, pw, 20),
            "MATCH SHOT — MANUAL SUBJECT ANCHOR", _panelHeading);
        pos += 26;
        GUI.Label(new Rect(px, pos, pw, 34),
            "Aim the camera at a subject, set its distance, then\n" +
            "capture an anchor. Static anchor, not player tracking.",
            _panelTiny);
        pos += 43;

        _presetAimDistance = Mathf.Round(
            DrawPanelSlider(px, pos, pw, "Subject distance",
                _presetAimDistance, 0.5f, 20f,
                _presetAimDistance.ToString("0.0") + " m") * 2f) / 2f;
        pos += 65;
        if (PanelButton(new Rect(px, pos, pw, 35),
            "Capture subject anchor at view centre", false))
            CaptureSubjectAnchor();
        pos += 43;
        GUI.Label(new Rect(px, pos, pw, 18),
            _presetHasSubject && _presetSubjectScene == ActiveSceneName()
                ? "Anchor READY in this scene"
                : "Anchor not set for this scene",
            _panelTiny);
        pos += 32;

        if (PanelButton(new Rect(px, pos, half, 36),
            "Apply preset", false))
            ApplySelectedPreset(false);
        if (PanelButton(new Rect(px + half + 10, pos, half, 36),
            "Apply + Play", true))
            ApplySelectedPreset(true);
        pos += 45;
        GUI.Label(new Rect(px, pos, pw, 35),
            _presetStatus, _panelTiny);
        pos += 38;
        GUI.Label(new Rect(px, pos, pw, 20),
            "F2 hide panel  |  Original path backed up on apply",
            _panelTiny);
    }
}
