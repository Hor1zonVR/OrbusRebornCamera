using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace OrbusBetterMirror;

/// <summary>
/// Passive, bounded, five-minute pose buffer for future ReplayMod-style clips.
/// Samples the existing XR camera and two hands. Never moves the VR rig,
/// changes gameplay objects, or records undiscovered remote entities.
/// File exports are pose-only until the world recorder is implemented.
/// </summary>
public sealed class ReplayBufferRecorder : MonoBehaviour
{
    public ReplayBufferRecorder(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int CtrlKey = 0x11;
    private const int ShiftKey = 0x10;
    private const int CKey = 0x43;
    private const int SamplesPerSecond = 30;
    private const float SampleInterval = 1f / SamplesPerSecond;
    private const int MaxQueuedSaves = 2;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static ReplayBufferRecorder? _instance;

    private struct Pose
    {
        public bool Valid;
        public float X, Y, Z, Qx, Qy, Qz, Qw;

        public static Pose From(Vector3 position, Quaternion rotation)
        {
            return new Pose
            {
                Valid = true, X = position.x, Y = position.y, Z = position.z,
                Qx = rotation.x, Qy = rotation.y, Qz = rotation.z,
                Qw = rotation.w
            };
        }
    }

    private struct Frame
    {
        public double Seconds;
        public long UtcTicks;
        public string Scene;
        public string Zone;
        public string Transition;
        public Pose Head;
        public Pose Left;
        public Pose Right;
        public Pose Avatar;
        public Pose AvatarRigRoot;
        public Pose AvatarModelHead;
        public Pose AvatarModelLeft;
        public Pose AvatarModelRight;
    }

    private ReplayRingBuffer<Frame>? _buffer;
    private readonly List<Task<string>> _pending = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly LocalAvatarCapture _localAvatar = new();
    private readonly NearbyWorldActorCapture _nearbyActors = new();

    private Camera? _eye;
    private bool _hotkeyWasDown;
    private bool _previousEnabled;
    private double _clockSeconds;
    private float _sampleAccumulator;
    private float _nextErrorLog;
    private int _configuredSeconds;
    private string _lastStatus = "Waiting for VR tracking";
    private string _zone = "(unknown)";
    private double _nextZoneRefresh;
    private double _nextLocalVisualCapture;
    private Frame _previousFrame;
    private bool _hasPreviousFrame;

    internal static bool Enabled =>
        Plugin.ReplayBufferEnabled != null && Plugin.ReplayBufferEnabled.Value;

    internal static int DurationSeconds =>
        Plugin.ReplayBufferSeconds == null ? 300 :
        Math.Clamp(Plugin.ReplayBufferSeconds.Value, 30, 600);

    internal static int FrameCount => _instance?._buffer?.Count ?? 0;
    internal static int NearbyActors => _instance?._nearbyActors.CurrentCandidates ?? 0;
    internal static string CurrentSessionId => _instance?._sessionId ?? "";

    internal static float AvailableSeconds
    {
        get
        {
            ReplayBufferRecorder? current = _instance;
            double? oldest = current?._buffer?.OldestTime;
            if (current == null || !oldest.HasValue) return 0f;
            return (float)Math.Min(DurationSeconds,
                Math.Max(0d, current._clockSeconds - oldest.Value));
        }
    }

    internal static string State
    {
        get
        {
            if (!Enabled) return "DISABLED";
            if (_instance == null) return "WAITING FOR RECORDER";
            if (_instance._buffer?.Count > 0) return "BUFFERING POSES";
            return "WAITING FOR VR / WORLD";
        }
    }

    internal static string LastSaveStatus =>
        _instance?._lastStatus ?? "No clip saved";

    internal static void SetEnabled(bool enabled)
    {
        Plugin.ReplayBufferEnabled.Value = enabled;
        ReplayBufferRecorder? current = _instance;
        if (current == null) return;
        current._previousEnabled = enabled;
        if (!enabled)
        {
            current._buffer?.Clear();
            current._hasPreviousFrame = false;
            current._lastStatus = "Buffer stopped and cleared";
        }
        else
        {
            current._lastStatus = "Buffer enabled; waiting for samples";
        }
        Plugin.ModLogger.LogInfo("[ReplayBuffer] " +
            (enabled ? "Enabled" : "Disabled; memory cleared"));
    }

    internal static void SetDurationSeconds(int seconds)
    {
        seconds = Math.Clamp(seconds, 30, 600);
        Plugin.ReplayBufferSeconds.Value = seconds;
        _instance?.ResizeBuffer(seconds);
        Plugin.ModLogger.LogInfo(
            "[ReplayBuffer] Window set to " + seconds + " seconds.");
    }

    internal static void SaveLastClip() => _instance?.QueueClip();

    private static bool Held(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Start()
    {
        _instance = this;
        _configuredSeconds = DurationSeconds;
        _previousEnabled = Enabled;
        _buffer = NewBuffer(_configuredSeconds);
        Plugin.ModLogger.LogInfo(
            "[ReplayBuffer] Pose-only rolling buffer ready. " +
            "Ctrl+Shift+C saves the last " + _configuredSeconds +
            " seconds. Default 5 minutes; VR eye and controller tracking only.");
    }

    private static ReplayRingBuffer<Frame> NewBuffer(int seconds)
    {
        int capacity = seconds * SamplesPerSecond + 2;
        return new ReplayRingBuffer<Frame>(
            capacity, seconds, frame => frame.Seconds);
    }

    private void ResizeBuffer(int seconds)
    {
        if (seconds == _configuredSeconds && _buffer != null) return;
        Frame[] retained = _buffer?.Snapshot(_clockSeconds) ?? Array.Empty<Frame>();
        ReplayRingBuffer<Frame> replacement = NewBuffer(seconds);
        for (int i = 0; i < retained.Length; i++)
            replacement.Add(retained[i]);
        replacement.Trim(_clockSeconds);
        _buffer = replacement;
        _configuredSeconds = seconds;
    }

    private void Update()
    {
        DrainCompletedSaves();

        bool hotkey = Application.isFocused && Held(CtrlKey) &&
                      Held(ShiftKey) && Held(CKey);
        if (hotkey && !_hotkeyWasDown)
            QueueClip();
        _hotkeyWasDown = hotkey;
    }

    private void LateUpdate()
    {
        float delta = Mathf.Max(0f, Time.unscaledDeltaTime);
        _clockSeconds += delta;
        _buffer?.Trim(_clockSeconds);

        int seconds = DurationSeconds;
        if (seconds != _configuredSeconds)
            ResizeBuffer(seconds);

        if (!Enabled)
        {
            if (_previousEnabled)
            {
                _buffer?.Clear();
                _nearbyActors.Clear();
                _hasPreviousFrame = false;
            }
            _previousEnabled = false;
            _sampleAccumulator = 0f;
            return;
        }
        _previousEnabled = true;

        // Sample at most once per frame and never try to recover an entire
        // backlog after a hitch, load screen or suspended game.
        _sampleAccumulator += delta;
        if (_sampleAccumulator < SampleInterval) return;
        _sampleAccumulator = Math.Min(
            _sampleAccumulator - SampleInterval, SampleInterval);

        if (!XRSettings.enabled || !XRSettings.isDeviceActive)
        {
            _eye = null;
            return;
        }

        if (_eye == null || !_eye.isActiveAndEnabled)
            _eye = FindEyeCamera();
        if (_eye == null) return;

        try
        {
            CaptureFrame(_eye);
        }
        catch (Exception ex)
        {
            _eye = null;
            if (Time.unscaledTime >= _nextErrorLog)
            {
                _nextErrorLog = Time.unscaledTime + 10f;
                Plugin.ModLogger.LogWarning(
                    "[ReplayBuffer] Skipping sample after tracking error: " +
                    ex.Message);
            }
        }
    }

    private void CaptureFrame(Camera eye)
    {
        // Identical tracking-space conversion used by PhysicalCameraGrab.
        Vector3 localHead = InputTracking.GetLocalPosition(XRNode.Head);
        Quaternion localRotation = InputTracking.GetLocalRotation(XRNode.Head);
        Quaternion originRotation = eye.transform.rotation *
                                    Quaternion.Inverse(localRotation);
        Vector3 originPosition =
            eye.transform.position - originRotation * localHead;

        string scene = SceneManager.GetActiveScene().name ?? "";
        scene = scene.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        if (_clockSeconds >= _nextZoneRefresh)
        {
            _zone = LoadedZoneSignature();
            _nextZoneRefresh = _clockSeconds + 1d;
        }
        Frame frame = new Frame
        {
            Seconds = _clockSeconds,
            UtcTicks = DateTime.UtcNow.Ticks,
            Scene = scene,
            Zone = _zone,
            Head = Pose.From(eye.transform.position, eye.transform.rotation),
            Left = ReadHand(XRNode.LeftHand, originPosition, originRotation),
            Right = ReadHand(XRNode.RightHand, originPosition, originRotation)
        };
        LocalAvatarCapture.Sample avatar = _localAvatar.Read(eye, _clockSeconds);
        if (_clockSeconds >= _nextLocalVisualCapture)
        {
            _nextLocalVisualCapture = _clockSeconds + 7d;
            if (avatar.Valid)
                ReplayVisualAssets.TryCacheLocalAvatar(_sessionId, eye);
        }
        if (avatar.Valid)
            frame.Avatar = Pose.From(avatar.Position, avatar.Rotation);
        if (avatar.Root.Valid)
            frame.AvatarRigRoot = Pose.From(
                avatar.Root.Position, avatar.Root.Rotation);
        if (avatar.ModelHead.Valid)
            frame.AvatarModelHead = Pose.From(
                avatar.ModelHead.Position, avatar.ModelHead.Rotation);
        if (avatar.ModelLeft.Valid)
            frame.AvatarModelLeft = Pose.From(
                avatar.ModelLeft.Position, avatar.ModelLeft.Rotation);
        if (avatar.ModelRight.Valid)
            frame.AvatarModelRight = Pose.From(
                avatar.ModelRight.Position, avatar.ModelRight.Rotation);
        frame.Transition = _hasPreviousFrame
            ? TransitionBetween(_previousFrame, frame) : "none";
        _previousFrame = frame;
        _hasPreviousFrame = true;
        _buffer!.Add(frame);

        if (Plugin.RecordNearbyActors != null &&
            Plugin.RecordNearbyActors.Value)
        {
            try
            {
                _nearbyActors.Tick(eye.transform.position, _clockSeconds,
                    frame.UtcTicks, _zone, _configuredSeconds,
                    Plugin.NearbyActorRadius == null ? 30f :
                        Plugin.NearbyActorRadius.Value);
            }
            catch (Exception ex)
            {
                // Actor observations must never interrupt the proven
                // local player pose recorder.
                if (Time.unscaledTime >= _nextErrorLog)
                {
                    _nextErrorLog = Time.unscaledTime + 10f;
                    Plugin.ModLogger.LogWarning(
                        "[WorldActors] Skipped observation: " + ex.Message);
                }
            }
        }
        else
        {
            // Disabling actor capture in RebornMenu also removes its
            // buffered observations; it doesn't silently export old data.
            _nearbyActors.Clear();
        }
    }

    private static string LoadedZoneSignature()
    {
        // The active scene commonly remains BootstrapWeathermaker. Read
        // loaded additive zone scenes instead, without inspecting server data.
        var zones = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            string name = scene.name ?? "";
            if (name.Length == 0 ||
                name == "BootstrapWeathermaker" ||
                name == "DontDestroyOnLoad")
                continue;
            zones.Add(name.Replace('\t', ' ').Replace('\r', ' ')
                .Replace('\n', ' '));
        }
        zones.Sort(StringComparer.Ordinal);
        return zones.Count == 0 ? "(unknown)" : string.Join("|", zones);
    }

    private static string TransitionBetween(Frame previous, Frame current)
    {
        double dt = current.Seconds - previous.Seconds;
        if (dt <= 0d) return "Gap";
        if (previous.Head.Valid && current.Head.Valid)
        {
            double dx = current.Head.X - previous.Head.X;
            double dy = current.Head.Y - previous.Head.Y;
            double dz = current.Head.Z - previous.Head.Z;
            double moved = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (moved > 12d && moved / dt > 35d)
                return "Teleport";
        }
        if (dt > .25d) return "Gap";
        if (previous.Zone != current.Zone &&
            previous.Zone != "(unknown)" &&
            current.Zone != "(unknown)")
            return "ZoneChange";
        return "None";
    }

    private static Pose ReadHand(XRNode node, Vector3 origin,
        Quaternion originRotation)
    {
        Vector3 position = InputTracking.GetLocalPosition(node);
        Quaternion rotation = InputTracking.GetLocalRotation(node);
        if (position.sqrMagnitude < 0.000001f &&
            Quaternion.Angle(rotation, Quaternion.identity) < 0.01f)
            return default;

        return Pose.From(origin + originRotation * position,
                         originRotation * rotation);
    }

    private static Camera? FindEyeCamera()
    {
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera != null && camera.name == "Camera (eye)" &&
                camera.stereoEnabled && camera.isActiveAndEnabled)
                return camera;
        }
        return null;
    }

    private void QueueClip()
    {
        if (!Enabled)
        {
            _lastStatus = "Buffer is disabled";
            Plugin.ModLogger.LogWarning("[ReplayBuffer] Enable the buffer first.");
            return;
        }

        if (_pending.Count >= MaxQueuedSaves)
        {
            _lastStatus = "Two clips still saving; retry in a moment";
            return;
        }

        Frame[] snapshot = _buffer?.Snapshot(_clockSeconds) ?? Array.Empty<Frame>();
        if (snapshot.Length == 0)
        {
            _lastStatus = "No VR poses buffered yet";
            Plugin.ModLogger.LogWarning("[ReplayBuffer] " + _lastStatus);
            return;
        }

        string folder = Path.Combine(Paths.ConfigPath,
            "OrbusRebornCamera", "Clips");
        string clipId = Guid.NewGuid().ToString("N");
        string name = "RC_Clip_" +
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Invariant) +
            "_" + clipId.Substring(0, 8) + ".tsv";
        string destination = Path.Combine(folder, name);
        string sessionId = _sessionId;
        int duration = _configuredSeconds;
        string avatarMeshHint = _localAvatar.MeshHint;
        int avatarBoneCount = _localAvatar.BoneCount;
        string avatarRigManifest = _localAvatar.CaptureRigManifest();
        _nearbyActors.Snapshot(_clockSeconds,
            snapshot[0].Seconds, snapshot[snapshot.Length - 1].Seconds,
            out WorldActorIdentity[] actorIdentities,
            out WorldActorObservation[] actorSamples);
        string actorVisualManifest = _nearbyActors.CaptureManifest();
        _nearbyActors.SnapshotRigs(_clockSeconds,
            snapshot[0].Seconds, snapshot[snapshot.Length - 1].Seconds,
            out WorldRigDefinition[] rigDefinitions,
            out WorldRigSample[] rigSamples);
        Plugin.ModLogger.LogInfo("[WorldRigs] Save snapshot: " +
            rigDefinitions.Length + " actual skin rigs, " +
            rigSamples.Length + " animated bone frames; " +
            actorSamples.Length + " nearby actor observations.");
        _lastStatus = "Saving " + snapshot.Length + " pose samples...";
        Plugin.ModLogger.LogInfo(
            "[ReplayBuffer] Queued " + snapshot.Length +
            " samples for background export: " + destination);

        // Unity objects and game APIs are NEVER touched on the worker.
        // Taking a copy leaves the live rolling buffer uninterrupted.
        _pending.Add(Task.Run(() =>
            WriteClip(destination, clipId, sessionId, duration, snapshot,
                avatarMeshHint, avatarBoneCount, avatarRigManifest,
                actorIdentities, actorSamples, actorVisualManifest,
                rigDefinitions, rigSamples)));
    }

    private void DrainCompletedSaves()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Task<string> task = _pending[i];
            if (!task.IsCompleted) continue;
            _pending.RemoveAt(i);
            try
            {
                string result = task.GetAwaiter().GetResult();
                _lastStatus = "Saved: " + Path.GetFileName(result);
                Plugin.ModLogger.LogInfo("[ReplayBuffer] " + _lastStatus +
                    " | " + result);
            }
            catch (Exception ex)
            {
                _lastStatus = "Clip save failed (see BepInEx log)";
                Plugin.ModLogger.LogError("[ReplayBuffer] Save failed: " + ex);
            }
        }
    }

    private static string WriteClip(string destination, string clipId,
        string sessionId, int duration, Frame[] frames,
        string avatarMeshHint, int avatarBoneCount,
        string avatarRigManifest,
        WorldActorIdentity[] actorIdentities,
        WorldActorObservation[] actorSamples,
        string actorVisualManifest,
        WorldRigDefinition[] rigDefinitions, WorldRigSample[] rigSamples)
    {
        string temporary = destination + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            using (var writer = new StreamWriter(new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536), new UTF8Encoding(false)))
            {
                writer.WriteLine("# RebornCamera rolling pose clip v4");
                writer.WriteLine("# content=LOCAL_PLAYER_RIG_TRANSFORMS_NO_MESH");
                writer.WriteLine("# rig_source=PlayerRigv4/Avatar/Torso/BodyParent");
                writer.WriteLine("# appearance_or_equipment_state_recorded=false");
                writer.WriteLine("# avatar_mesh_hint=" + avatarMeshHint);
                writer.WriteLine("# avatar_bone_count_hint=" +
                    avatarBoneCount.ToString(Invariant));
                writer.WriteLine("# avatar_mesh_geometry_recorded=false");
                writer.WriteLine("# avatar_bone_animations_recorded=false");
                writer.WriteLine("# clip_id=" + clipId);
                writer.WriteLine("# session_id=" + sessionId);
                writer.WriteLine("# requested_window_seconds=" +
                    duration.ToString(Invariant));
                writer.WriteLine("# samples=" + frames.Length.ToString(Invariant));
                writer.WriteLine("# first_utc_ticks=" +
                    frames[0].UtcTicks.ToString(Invariant));
                writer.WriteLine("# last_utc_ticks=" +
                    frames[frames.Length - 1].UtcTicks.ToString(Invariant));
                writer.WriteLine("# sample_target_hz=30");
                writer.WriteLine("# units=Unity world metres; quaternion xyzw");
                writer.WriteLine("seconds\tutc_ticks\tscene" +
                    "\thead_valid\thead_x\thead_y\thead_z\thead_qx\thead_qy\thead_qz\thead_qw" +
                    "\tleft_valid\tleft_x\tleft_y\tleft_z\tleft_qx\tleft_qy\tleft_qz\tleft_qw" +
                    "\tright_valid\tright_x\tright_y\tright_z\tright_qx\tright_qy\tright_qz\tright_qw" +
                    "\tzone\ttransition" +
                    "\tavatar_valid\tavatar_x\tavatar_y\tavatar_z" +
                    "\tavatar_qx\tavatar_qy\tavatar_qz\tavatar_qw" +
                    "\tavatar_root_valid\tavatar_root_x\tavatar_root_y\tavatar_root_z" +
                    "\tavatar_root_qx\tavatar_root_qy\tavatar_root_qz\tavatar_root_qw" +
                    "\tavatar_head_valid\tavatar_head_x\tavatar_head_y\tavatar_head_z" +
                    "\tavatar_head_qx\tavatar_head_qy\tavatar_head_qz\tavatar_head_qw" +
                    "\tavatar_left_valid\tavatar_left_x\tavatar_left_y\tavatar_left_z" +
                    "\tavatar_left_qx\tavatar_left_qy\tavatar_left_qz\tavatar_left_qw" +
                    "\tavatar_right_valid\tavatar_right_x\tavatar_right_y\tavatar_right_z" +
                    "\tavatar_right_qx\tavatar_right_qy\tavatar_right_qz\tavatar_right_qw");

                var line = new StringBuilder(420);
                foreach (Frame frame in frames)
                {
                    line.Clear();
                    line.Append(frame.Seconds.ToString("F4", Invariant));
                    line.Append('\t').Append(frame.UtcTicks.ToString(Invariant));
                    line.Append('\t').Append(frame.Scene);
                    AppendPose(line, frame.Head);
                    AppendPose(line, frame.Left);
                    AppendPose(line, frame.Right);
                    line.Append('\t').Append(frame.Zone);
                    line.Append('\t').Append(frame.Transition);
                    AppendPose(line, frame.Avatar);
                    AppendPose(line, frame.AvatarRigRoot);
                    AppendPose(line, frame.AvatarModelHead);
                    AppendPose(line, frame.AvatarModelLeft);
                    AppendPose(line, frame.AvatarModelRight);
                    writer.WriteLine(line.ToString());
                }
            }
            File.Move(temporary, destination);
            // Optional descriptive sidecar; failures cannot invalidate an
            // otherwise successfully saved five-minute pose clip.
            try
            {
                string manifestPath = Path.ChangeExtension(
                    destination, ".avatar.txt");
                File.WriteAllText(manifestPath, avatarRigManifest,
                    new UTF8Encoding(false));
            }
            catch (IOException) { /* main clip is still valid */ }
            catch (UnauthorizedAccessException) { /* main clip is still valid */ }

            // Actor timeline is a separate bounded sidecar, preserving the
            // v4 local-pose TSV reader and old saved clips unchanged.
            try
            {
                string actorPath = Path.ChangeExtension(destination, ".actors.tsv");
                string actorPartial = actorPath + ".partial";
                using (var actorFile = new StreamWriter(actorPartial,
                    false, new UTF8Encoding(false)))
                    WorldActorRecords.Write(actorFile, sessionId, clipId,
                        actorIdentities, actorSamples);
                File.Move(actorPartial, actorPath);
            }
            catch (Exception ex) when (ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidDataException)
            {
                // An optional actor export cannot damage the primary clip.
                Plugin.ModLogger.LogWarning(
                    "[WorldActors] Actor sidecar could not be saved: " +
                    ex.Message);
            }
            try
            {
                string manifestPath = Path.ChangeExtension(
                    destination, ".actors.manifest.txt");
                File.WriteAllText(manifestPath, actorVisualManifest,
                    new UTF8Encoding(false));
            }
            catch (IOException) { /* primary clip remains valid */ }
            catch (UnauthorizedAccessException) { /* primary clip remains valid */ }

            // Compact real skin-bone animation is optional; its failures
            // must never destroy a saved local-player pose recording.
            if (rigSamples.Length > 0 && rigDefinitions.Length > 0)
            {
                string rigPath = Path.ChangeExtension(destination, ".rigs.bin");
                string rigPartial = rigPath + ".partial";
                try
                {
                    using (var rigFile = new FileStream(rigPartial,
                        FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        WorldRigCodec.Write(rigFile, sessionId, clipId,
                            rigDefinitions, rigSamples);
                    File.Move(rigPartial, rigPath);
                }
                catch (Exception ex) when (ex is IOException ||
                    ex is UnauthorizedAccessException ||
                    ex is InvalidDataException)
                {
                    Plugin.ModLogger.LogWarning(
                        "[WorldRigs] Rig sidecar unavailable: " + ex.Message);
                    try { if (File.Exists(rigPartial)) File.Delete(rigPartial); }
                    catch { /* best effort */ }
                }
            }
            return destination;
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { /* partial file may survive a fatal disk error */ }
            throw;
        }
    }

    private static void AppendPose(StringBuilder line, Pose pose)
    {
        line.Append('\t').Append(pose.Valid ? '1' : '0');
        AppendNumber(line, pose.X);
        AppendNumber(line, pose.Y);
        AppendNumber(line, pose.Z);
        AppendNumber(line, pose.Qx);
        AppendNumber(line, pose.Qy);
        AppendNumber(line, pose.Qz);
        AppendNumber(line, pose.Qw);
    }

    private static void AppendNumber(StringBuilder line, float number) =>
        line.Append('\t').Append(number.ToString("R", Invariant));

    private void OnApplicationQuit()
    {
        // Give in-flight writes a chance to finish on normal shutdown.
        foreach (Task<string> task in _pending)
        {
            try { task.Wait(1500); }
            catch { /* failures are reported if normal update resumes */ }
        }
        DrainCompletedSaves();
        ReplayVisualAssets.Clear();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
