using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace OrbusBetterMirror;

/// <summary>
/// Opt-in, bounded VR pose capture diagnostic. This is a recorder probe,
/// not a ReplayMod player: it does not touch the VR rig or simulate entities.
/// </summary>
public sealed class ReplayCaptureProbe : MonoBehaviour
{
    public ReplayCaptureProbe(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private const int CtrlKey = 0x11;
    private const int ShiftKey = 0x10;
    private const int RKey = 0x52;
    private const float SampleInterval = 1f / 30f;
    private const float MaxDurationSeconds = 60f;

    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;
    private StreamWriter? _writer;
    private Camera? _eye;
    private bool _hotkeyWasDown;
    private float _elapsed;
    private float _untilSample;
    private float _flushElapsed;
    private int _sampleCount;
    private long _startUtcTicks;
    private string _outputPath = string.Empty;

    private struct PoseState
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void Start()
    {
        Plugin.ModLogger.LogInfo(
            "[ReplayProbe] Ready (opt-in). Ctrl+Shift+R starts or stops a " +
            "60-second headset-and-hands capture. No gameplay modifications.");
    }

    private void Update()
    {
        bool chord = Application.isFocused &&
                     IsKeyDown(CtrlKey) &&
                     IsKeyDown(ShiftKey) &&
                     IsKeyDown(RKey);

        if (chord && !_hotkeyWasDown)
        {
            if (_writer == null) StartCapture();
            else StopCapture("manual stop");
        }

        _hotkeyWasDown = chord;
    }

    private void LateUpdate()
    {
        if (_writer == null) return;

        float delta = Mathf.Max(0f, Time.unscaledDeltaTime);
        _elapsed += delta;
        _untilSample -= delta;
        _flushElapsed += delta;

        if (_untilSample <= 0f)
        {
            _untilSample += SampleInterval;
            // Never attempt a large catch-up burst after a frame hitch.
            if (_untilSample < -SampleInterval) _untilSample = 0f;

            try
            {
                WriteSample();
                if (_flushElapsed >= 1f)
                {
                    _writer.Flush();
                    _flushElapsed = 0f;
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError("[ReplayProbe] Capture failed: " + ex);
                StopCapture("write error");
                return;
            }
        }

        if (_elapsed >= MaxDurationSeconds)
            StopCapture("60-second probe limit");
    }

    private void StartCapture()
    {
        if (!XRSettings.enabled || !XRSettings.isDeviceActive)
        {
            Plugin.ModLogger.LogWarning(
                "[ReplayProbe] VR tracking is not active; capture not started.");
            return;
        }

        _eye = FindEyeCamera();
        if (_eye == null)
        {
            Plugin.ModLogger.LogWarning(
                "[ReplayProbe] Camera (eye) not ready; enter the game world and retry.");
            return;
        }

        try
        {
            string folder = Path.Combine(
                Paths.ConfigPath, "OrbusRebornCamera", "ReplayProbes");
            Directory.CreateDirectory(folder);

            string sessionId = Guid.NewGuid().ToString("N");
            string filename = "RC_PoseProbe_" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", _culture) +
                "_" + sessionId.Substring(0, 8) + ".tsv";
            _outputPath = Path.Combine(folder, filename);
            _startUtcTicks = DateTime.UtcNow.Ticks;

            _writer = new StreamWriter(
                new FileStream(_outputPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));

            _writer.WriteLine("# OrbusRebornCamera pose probe v1");
            _writer.WriteLine("# session_id=" + sessionId);
            _writer.WriteLine("# start_utc_ticks=" +
                              _startUtcTicks.ToString(_culture));
            _writer.WriteLine("# sample_target_hz=30");
            _writer.WriteLine("# units=Unity world metres; quaternion xyzw");
            _writer.WriteLine("# hands can be invalid when tracking is missing");
            _writer.WriteLine(
                "seconds\tutc_ticks\tscene" +
                "\thead_valid\thead_x\thead_y\thead_z\thead_qx\thead_qy\thead_qz\thead_qw" +
                "\tleft_valid\tleft_x\tleft_y\tleft_z\tleft_qx\tleft_qy\tleft_qz\tleft_qw" +
                "\tright_valid\tright_x\tright_y\tright_z\tright_qx\tright_qy\tright_qz\tright_qw");
            _writer.Flush();

            _elapsed = 0f;
            _untilSample = 0f;
            _flushElapsed = 0f;
            _sampleCount = 0;

            Plugin.ModLogger.LogInfo(
                "[ReplayProbe] Recording started: " + _outputPath);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                "[ReplayProbe] Could not create capture file: " + ex);
            StopCapture("start error");
        }
    }

    private void WriteSample()
    {
        if (_eye == null || !_eye.isActiveAndEnabled)
            _eye = FindEyeCamera();

        if (_eye == null) return; // Scene loading; missing samples are explicit.

        // Use the same local-XR-to-world conversion as PhysicalCameraGrab.
        Vector3 localHeadPosition =
            InputTracking.GetLocalPosition(XRNode.Head);
        Quaternion localHeadRotation =
            InputTracking.GetLocalRotation(XRNode.Head);

        Quaternion originRotation =
            _eye.transform.rotation * Quaternion.Inverse(localHeadRotation);
        Vector3 originPosition =
            _eye.transform.position - originRotation * localHeadPosition;

        PoseState head = new PoseState
        {
            Valid = true,
            Position = _eye.transform.position,
            Rotation = _eye.transform.rotation
        };
        PoseState left = ReadHand(
            XRNode.LeftHand, originPosition, originRotation);
        PoseState right = ReadHand(
            XRNode.RightHand, originPosition, originRotation);

        string scene = SceneManager.GetActiveScene().name
            .Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        long sampleTicks = _startUtcTicks +
            (long)(_elapsed * TimeSpan.TicksPerSecond);

        StringBuilder line = new StringBuilder(320);
        line.Append(_elapsed.ToString("F4", _culture));
        line.Append('\t').Append(sampleTicks.ToString(_culture));
        line.Append('\t').Append(scene);
        AppendPose(line, head);
        AppendPose(line, left);
        AppendPose(line, right);
        _writer!.WriteLine(line.ToString());
        _sampleCount++;
    }

    private static PoseState ReadHand(
        XRNode node, Vector3 originPosition, Quaternion originRotation)
    {
        Vector3 localPosition = InputTracking.GetLocalPosition(node);
        Quaternion localRotation = InputTracking.GetLocalRotation(node);
        if (localPosition.sqrMagnitude < 0.000001f &&
            Quaternion.Angle(localRotation, Quaternion.identity) < 0.01f)
            return new PoseState
            {
                Valid = false,
                Position = Vector3.zero,
                Rotation = Quaternion.identity
            };

        return new PoseState
        {
            Valid = true,
            Position = originPosition + originRotation * localPosition,
            Rotation = originRotation * localRotation
        };
    }

    private void AppendPose(StringBuilder line, PoseState pose)
    {
        line.Append('\t').Append(pose.Valid ? '1' : '0');
        AppendNumber(line, pose.Position.x);
        AppendNumber(line, pose.Position.y);
        AppendNumber(line, pose.Position.z);
        AppendNumber(line, pose.Rotation.x);
        AppendNumber(line, pose.Rotation.y);
        AppendNumber(line, pose.Rotation.z);
        AppendNumber(line, pose.Rotation.w);
    }

    private void AppendNumber(StringBuilder line, float value)
    {
        line.Append('\t').Append(value.ToString("R", _culture));
    }

    private static Camera? FindEyeCamera()
    {
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera != null &&
                camera.name == "Camera (eye)" &&
                camera.stereoEnabled)
                return camera;
        }
        return null;
    }

    private void StopCapture(string reason)
    {
        StreamWriter? writer = _writer;
        _writer = null;
        if (writer == null) return;

        try
        {
            writer.WriteLine("# stop_reason=" + reason);
            writer.WriteLine("# samples=" +
                             _sampleCount.ToString(_culture));
            writer.Flush();
            writer.Dispose();
            Plugin.ModLogger.LogInfo(
                "[ReplayProbe] Saved " + _sampleCount + " samples over " +
                _elapsed.ToString("F2", _culture) + "s: " + _outputPath);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                "[ReplayProbe] Error closing capture: " + ex);
            try { writer.Dispose(); } catch { /* already closing */ }
        }
    }

    private void OnDestroy() => StopCapture("component destroyed");
    private void OnApplicationQuit() => StopCapture("game shutdown");
}
