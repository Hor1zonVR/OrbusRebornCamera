
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

namespace OrbusBetterMirror;

public sealed partial class CameraDiagnostics : MonoBehaviour
{
    public CameraDiagnostics(IntPtr pointer) : base(pointer)
    {
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private enum CameraMode
    {
        POV,
        ThirdPerson,
        Selfie,
        Static,
        Drone
    }

    // F5 through F12.
    private const int FirstHotkey = 0x74;
    private const int CtrlKey = 0x11;

    private readonly bool[] _keyWasDown = new bool[8];

    private static readonly float[] FovPresets =
    {
        90f,
        100f,
        110f,
        120f,
        130f,
        140f
    };

    private static readonly float[] SnapshotTimes =
    {
        5f,
        20f,
        60f
    };

    private Camera? _source;
    private Camera? _desktop;
    private GameObject? _cameraRoot;

    private CameraMode _mode = CameraMode.POV;
    private CameraMode _lastFollowMode = CameraMode.POV;

    // Unity layer used by OrbusVR's local avatar.
    private int _avatarLayer = -1;
    private bool _avatarLayerWarningLogged;

    private bool _autoStartHandled;
    private bool _snapNextFrame = true;
    private bool _missingSourceLogged;

    private float _elapsed;
    private float _nextAutoAttempt = 5f;
    private int _nextSnapshot;

    private float _lastAspect;
    private float _lastFov;

    private bool _pendingMirrorCheck;
    private float _mirrorCheckTimer;
    private GameViewRenderMode _requestedMirrorMode;

    private bool IsCameraActive =>
        _desktop != null && _desktop.enabled;

    private void Start()
    {
        // Load the saved camera follow mode.
        if (Enum.TryParse(
            Plugin.StartMode.Value,
            true,
            out CameraMode savedMode))
        {
            if (savedMode != CameraMode.Static &&
                savedMode != CameraMode.Drone)
            {
                _mode = savedMode;
                _lastFollowMode = savedMode;
            }
        }

        ResolveAvatarLayer();

        Plugin.ModLogger.LogInfo(
            $"BetterMirror v0.6 ready. " +
            $"Mode={_mode}, " +
            $"FOV={GetHorizontalFov():0}, " +
            $"AutoStart={Plugin.StartEnabled.Value}"
        );

        Plugin.ModLogger.LogInfo(
            "Local avatar rendering is desktop-only."
        );

        Plugin.ModLogger.LogInfo(
            "F9 Toggle Camera | Ctrl+F9 Toggle Avatar"
        );

        Plugin.ModLogger.LogInfo(
            "F10 FOV | F11 Mode | F12 Freeze"
        );
    }

    private void ResolveAvatarLayer()
    {
        try
        {
            _avatarLayer =
                LayerMask.NameToLayer("Minicam");

            if (_avatarLayer >= 0 &&
                _avatarLayer < 32)
            {
                Plugin.ModLogger.LogInfo(
                    $"Found OrbusVR avatar layer: " +
                    $"Minicam ({_avatarLayer})."
                );

                if (_avatarLayer != 20)
                {
                    Plugin.ModLogger.LogWarning(
                        "Minicam layer index differs from " +
                        "the diagnostic logs. Using the " +
                        "runtime-resolved index."
                    );
                }
            }
            else
            {
                _avatarLayer = -1;
                WarnMissingAvatarLayer();
            }
        }
        catch (Exception ex)
        {
            _avatarLayer = -1;

            Plugin.ModLogger.LogWarning(
                $"Failed to resolve Minicam layer: {ex.Message}"
            );
        }
    }

    private void WarnMissingAvatarLayer()
    {
        if (_avatarLayerWarningLogged)
            return;

        _avatarLayerWarningLogged = true;

        Plugin.ModLogger.LogWarning(
            "Minicam layer was not found. " +
            "Avatar rendering will remain disabled. " +
            "Normal desktop camera modes still work."
        );
    }

    private void Update()
    {
        float delta = Time.unscaledDeltaTime;
        _elapsed += delta;

        // Automatic camera diagnostics.
        if (_nextSnapshot < SnapshotTimes.Length &&
            _elapsed >= SnapshotTimes[_nextSnapshot])
        {
            _nextSnapshot++;
            TakeSnapshot("automatic");
        }

        // Automatically start once VR is ready.
        if (!_autoStartHandled &&
            _elapsed >= _nextAutoAttempt)
        {
            _nextAutoAttempt = _elapsed + 1f;

            if (!Plugin.StartEnabled.Value)
            {
                _autoStartHandled = true;
            }
            else if (TryEnableCamera(false))
            {
                _autoStartHandled = true;
            }
        }

        // Verify built-in mirror changes.
        if (_pendingMirrorCheck)
        {
            _mirrorCheckTimer -= delta;

            if (_mirrorCheckTimer <= 0f)
            {
                _pendingMirrorCheck = false;
                VerifyMirrorMode();
            }
        }

        // Existing mirror controls.
        if (JustPressed(0))
            SetMirrorMode(GameViewRenderMode.LeftEye, "F5");

        if (JustPressed(1))
            SetMirrorMode(GameViewRenderMode.RightEye, "F6");

        if (JustPressed(2))
            SetMirrorMode(GameViewRenderMode.BothEyes, "F7");

        if (JustPressed(3))
            TakeSnapshot("F8");

        // F9 toggles the camera.
        // Ctrl+F9 toggles desktop avatar visibility.
        if (JustPressed(4))
        {
            if (IsCtrlDown())
                ToggleLocalAvatar();
            else
                ToggleCamera();
        }

        if (JustPressed(5))
            CycleFov();

        if (JustPressed(6))
        {
            if (IsCtrlDown()) ToggleExperimentalDrone();
            else CycleFollowMode();
        }

        if (JustPressed(7))
            ToggleStaticMode();

        HandleDroneInput();
    }

    private void LateUpdate()
    {
        if (!IsCameraActive)
            return;

        try
        {
            if (_desktop == null)
                return;

            // Refresh camera FOV if resolution or
            // configuration changes.
            float aspect = GetWindowAspect();
            float fov = GetHorizontalFov();

            if (Math.Abs(aspect - _lastAspect) > 0.001f ||
                Math.Abs(fov - _lastFov) > 0.01f)
            {
                ApplyFov();
            }

            // Drone movement is independent of the headset and never
            // changes the actual player rig or source eye camera.
            if (_mode == CameraMode.Drone)
            {
                UpdateDesktopCullingMask(false);
                ApplyDronePose();
                return;
            }

            // Static cameras keep their world pose.
            // Their visibility settings still update.
            if (_mode == CameraMode.Static)
            {
                UpdateDesktopCullingMask(false);
                return;
            }

            // The VR camera may be destroyed or replaced
            // when transitioning between game scenes.
            if (_source == null)
            {
                _source = FindVRCamera();

                if (_source == null)
                {
                    if (!_missingSourceLogged)
                    {
                        Plugin.ModLogger.LogWarning(
                            "Waiting for OrbusVR's eye camera."
                        );

                        _missingSourceLogged = true;
                    }

                    return;
                }

                _missingSourceLogged = false;

                ConfigureDesktopCamera();
                _snapNextFrame = true;

                Plugin.ModLogger.LogInfo(
                    "Reconnected BetterMirror to VR camera."
                );
            }

            // Sync desktop layer visibility without
            // modifying the headset camera.
            UpdateDesktopCullingMask(false);

            UpdateCameraPose();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Camera update failed: {ex}"
            );

            DisableCamera();
        }
    }

    private bool JustPressed(int index)
    {
        int virtualKey = FirstHotkey + index;

        bool isDown =
            Application.isFocused &&
            (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        bool pressed =
            isDown && !_keyWasDown[index];

        _keyWasDown[index] = isDown;

        return pressed;
    }

    private static bool IsCtrlDown()
    {
        return
            (GetAsyncKeyState(CtrlKey) & 0x8000) != 0;
    }

    private Camera? FindVRCamera()
    {
        var cameras = Camera.allCameras;

        foreach (var camera in cameras)
        {
            if (camera == null)
                continue;

            if (camera.name == "Camera (eye)" &&
                (camera.stereoEnabled ||
                 (!XRSettings.enabled || !XRSettings.isDeviceActive)))
            {
                return camera;
            }
        }

        return null;
    }

    private bool TryEnableCamera(bool manual, bool droneAttempt = false)
    {
        try
        {
            if ((!XRSettings.enabled || !XRSettings.isDeviceActive) &&
                !droneAttempt)
            {
                if (manual)
                {
                    Plugin.ModLogger.LogWarning(
                        "Cannot enable camera: VR is not active."
                    );
                }

                return false;
            }

            _source = FindVRCamera();

            if (_source == null)
            {
                if (manual)
                {
                    Plugin.ModLogger.LogWarning(
                        "Camera (eye) not found. " +
                        "Wait for OrbusVR to load."
                    );
                }

                return false;
            }

            if (_desktop == null)
                CreateDesktopCamera();

            if (_desktop == null)
                return false;

            _mode = _lastFollowMode;
            _snapNextFrame = true;

            ConfigureDesktopCamera();

            // Set the pose before the first frame.
            UpdateCameraPose();

            _desktop.enabled = true;

            Plugin.ModLogger.LogInfo(
                $"Creator camera ENABLED. " +
                $"Mode={_mode}, " +
                $"HorizontalFOV={GetHorizontalFov():0}"
            );

            UpdateDesktopCullingMask(true);

            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Camera enable failed: {ex}"
            );

            DisableCamera();
            return false;
        }
    }

    private void CreateDesktopCamera()
    {
        _cameraRoot = new GameObject(
            "BetterMirror Creator Camera"
        );

        UnityEngine.Object.DontDestroyOnLoad(
            _cameraRoot
        );

        _desktop =
            _cameraRoot.AddComponent<Camera>();

        _desktop.enabled = false;

        Plugin.ModLogger.LogInfo(
            "Created BetterMirror desktop camera."
        );
    }

    private void ConfigureDesktopCamera()
    {
        if (_source == null ||
            _desktop == null)
        {
            return;
        }

        Camera source = _source;
        Camera desktop = _desktop;

        // Desktop camera only.
        // No rendering to either VR headset eye.
        desktop.stereoTargetEye =
            StereoTargetEyeMask.None;

        desktop.targetDisplay = 0;
        desktop.targetTexture = null;

        desktop.depth = 1000f;
        desktop.rect =
            new Rect(0f, 0f, 1f, 1f);

        // Preserve the working game's clipping planes.
        desktop.nearClipPlane =
            source.nearClipPlane;

        desktop.farClipPlane =
            source.farClipPlane;

        desktop.clearFlags =
            source.clearFlags ==
                CameraClearFlags.SolidColor
                ? CameraClearFlags.SolidColor
                : CameraClearFlags.Skybox;

        desktop.backgroundColor =
            source.backgroundColor;

        // Copy the source mask first.
        // Then apply our own desktop-only layer changes.
        desktop.cullingMask =
            source.cullingMask;

        UpdateDesktopCullingMask(true);

        ApplyFov();
    }

    // -----------------------------------
    // DESKTOP-ONLY AVATAR VISIBILITY
    // -----------------------------------

    private bool ShouldShowAvatar()
    {
        if (!Plugin.ShowLocalAvatar.Value)
            return false;

        if (_avatarLayer < 0)
            return false;

        // Never show the local head/body in POV mode.
        if (_mode == CameraMode.POV)
            return false;

        // External views may render the local avatar.
        return
            _mode == CameraMode.ThirdPerson ||
            _mode == CameraMode.Selfie ||
            _mode == CameraMode.Static ||
            _mode == CameraMode.Drone;
    }

    private void UpdateDesktopCullingMask(bool logChange)
    {
        if (_desktop == null)
            return;

        // Read OrbusVR's actual eye-camera mask.
        // This does not modify that camera.
        int mask = _source != null
            ? _source.cullingMask
            : _desktop.cullingMask;

        bool showAvatar = ShouldShowAvatar();

        if (_avatarLayer >= 0)
        {
            int avatarFlag =
                1 << _avatarLayer;

            // First remove the layer to ensure
            // POV mode never renders the local head.
            mask &= ~avatarFlag;

            // Add it back only for external modes.
            if (showAvatar)
            {
                mask |= avatarFlag;
            }
        }

        bool changed =
            _desktop.cullingMask != mask;

        if (changed)
        {
            // Only our own desktop camera is modified.
            _desktop.cullingMask = mask;
        }

        if (logChange)
        {
            Plugin.ModLogger.LogInfo(
                "[Avatar] Visibility state: " +
                $"Mode={_mode}, " +
                $"ShowLocalAvatar={Plugin.ShowLocalAvatar.Value}, " +
                $"Included={showAvatar}"
            );

            Plugin.ModLogger.LogInfo(
                "[Avatar] Desktop culling mask: 0x" +
                mask.ToString("X8")
            );

            Plugin.ModLogger.LogInfo(
                "[Avatar] Minicam layer: " +
                _avatarLayer
            );
        }
    }

    private void ToggleLocalAvatar()
    {
        bool next =
            !Plugin.ShowLocalAvatar.Value;

        Plugin.ShowLocalAvatar.Value = next;

        Plugin.ModLogger.LogInfo(
            "[Ctrl+F9] Desktop avatar rendering: " +
            (next ? "ENABLED" : "DISABLED")
        );

        if (_avatarLayer < 0)
        {
            ResolveAvatarLayer();
        }

        UpdateDesktopCullingMask(true);

        if (_mode == CameraMode.POV)
        {
            Plugin.ModLogger.LogInfo(
                "[Avatar] POV mode always hides " +
                "the local head and body. " +
                "Switch to third-person or selfie " +
                "to see the avatar."
            );
        }

        Plugin.ModLogger.LogInfo(
            "[Avatar] Original VR camera unchanged."
        );
    }

    // -----------------------------------
    // FOV
    // -----------------------------------

    private static float GetWindowAspect()
    {
        int width =
            Math.Max(1, Screen.width);

        int height =
            Math.Max(1, Screen.height);

        return (float)width / height;
    }

    private static float GetHorizontalFov()
    {
        return Mathf.Clamp(
            Plugin.HorizontalFov.Value,
            50f,
            150f
        );
    }

    private void ApplyFov()
    {
        if (_desktop == null)
            return;

        float aspect =
            GetWindowAspect();

        float horizontalDegrees =
            GetHorizontalFov();

        // Unity Camera.fieldOfView is vertical FOV.
        // Convert horizontal FOV to vertical FOV.
        double horizontalRadians =
            horizontalDegrees *
            Math.PI / 180.0;

        double verticalRadians =
            2.0 * Math.Atan(
                Math.Tan(horizontalRadians / 2.0)
                / aspect
            );

        float verticalDegrees =
            (float)(
                verticalRadians * 180.0 / Math.PI
            );

        _desktop.aspect = aspect;
        _desktop.fieldOfView =
            verticalDegrees;

        _lastAspect = aspect;
        _lastFov = horizontalDegrees;
    }

    private void CycleFov()
    {
        float current = GetHorizontalFov();
        int next = 0;

        for (int i = 0; i < FovPresets.Length; i++)
        {
            if (FovPresets[i] > current + 0.1f)
            {
                next = i;
                break;
            }
        }

        Plugin.HorizontalFov.Value =
            FovPresets[next];

        ApplyFov();

        Plugin.ModLogger.LogInfo(
            "[F10] Horizontal FOV: " +
            $"{Plugin.HorizontalFov.Value:0} degrees."
        );
    }

    // -----------------------------------
    // CAMERA POSITIONING
    // -----------------------------------

    private void UpdateCameraPose()
    {
        if (_desktop == null ||
            _source == null)
        {
            return;
        }

        Transform head =
            _source.transform;

        Transform output =
            _desktop.transform;

        Vector3 targetPosition;
        Quaternion targetRotation;

        if (_mode == CameraMode.POV)
        {
            // Same working first-person view as v0.3.
            targetPosition =
                head.position;

            targetRotation =
                head.rotation;
        }
        else if (_mode == CameraMode.Static)
        {
            // Leave the camera where it was frozen.
            return;
        }
        else
        {
            // Horizontal direction of the player.
            Vector3 forward =
                head.forward;

            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            forward.Normalize();

            // Look towards the player's upper body.
            Vector3 lookTarget =
                head.position +
                Vector3.down * 0.45f;

            if (_mode == CameraMode.ThirdPerson)
            {
                float distance = Mathf.Clamp(
                    Plugin.ThirdDistance.Value,
                    0.75f,
                    8f
                );

                float height = Mathf.Clamp(
                    Plugin.ThirdHeight.Value,
                    -1f,
                    4f
                );

                targetPosition =
                    head.position -
                    forward * distance +
                    Vector3.up * height;
            }
            else
            {
                // Selfie: camera in front of the player.
                float distance = Mathf.Clamp(
                    Plugin.SelfieDistance.Value,
                    0.5f,
                    8f
                );

                targetPosition =
                    head.position +
                    forward * distance +
                    Vector3.up * 0.1f;
            }

            targetRotation = Quaternion.LookRotation(
                lookTarget - targetPosition,
                Vector3.up
            );
        }

        // First frame snaps to correct position.
        // POV always follows exactly.
        if (_snapNextFrame ||
            _mode == CameraMode.POV)
        {
            output.position =
                targetPosition;

            output.rotation =
                targetRotation;

            _snapNextFrame = false;
            return;
        }

        float speed = Mathf.Clamp(
            Plugin.Smoothing.Value,
            0f,
            30f
        );

        if (speed <= 0.01f)
        {
            output.position =
                targetPosition;

            output.rotation =
                targetRotation;

            return;
        }

        float blend =
            1f - Mathf.Exp(
                -speed * Time.unscaledDeltaTime
            );

        output.position = Vector3.Lerp(
            output.position,
            targetPosition,
            blend
        );

        output.rotation = Quaternion.Slerp(
            output.rotation,
            targetRotation,
            blend
        );
    }

    // -----------------------------------
    // CAMERA CONTROLS
    // -----------------------------------

    private void ToggleCamera()
    {
        _autoStartHandled = true;

        if (IsCameraActive)
        {
            DisableCamera();
        }
        else
        {
            TryEnableCamera(true);
        }
    }

    private void DisableCamera()
    {
        LeaveDrone();
        if (_desktop != null)
        {
            _desktop.enabled = false;
        }

        Plugin.ModLogger.LogInfo(
            "Creator camera DISABLED. " +
            "Original mirror restored."
        );
    }

    private void CycleFollowMode()
    {
        if (_mode == CameraMode.Drone)
        {
            LeaveDrone();
            _mode = _lastFollowMode;
        }

        if (_mode == CameraMode.Static)
        {
            _mode = _lastFollowMode;
        }

        CameraMode next;

        if (_mode == CameraMode.POV)
        {
            next = CameraMode.ThirdPerson;
        }
        else if (_mode == CameraMode.ThirdPerson)
        {
            next = CameraMode.Selfie;
        }
        else
        {
            next = CameraMode.POV;
        }

        _mode = next;
        _lastFollowMode = next;

        // Save for future launches.
        Plugin.StartMode.Value =
            next.ToString();

        _snapNextFrame = true;
        _autoStartHandled = true;

        if (!IsCameraActive)
        {
            TryEnableCamera(true);
        }
        else
        {
            UpdateDesktopCullingMask(true);
        }

        Plugin.ModLogger.LogInfo(
            $"[F11] Follow mode: {_mode}"
        );
    }

    private void ToggleStaticMode()
    {
        _autoStartHandled = true;

        // F12 freezes the current drone pose without returning to VR.
        // A second F12 restores the last follow mode.
        if (_mode == CameraMode.Drone)
        {
            LeaveDrone();
            _mode = CameraMode.Static;
            UpdateDesktopCullingMask(true);
            Plugin.ModLogger.LogInfo("[Drone] Camera frozen at drone position (F12).");
            return;
        }

        if (!IsCameraActive)
        {
            if (!TryEnableCamera(true))
                return;
        }

        if (_mode == CameraMode.Static)
        {
            _mode = _lastFollowMode;
            _snapNextFrame = true;

            Plugin.ModLogger.LogInfo(
                $"[F12] Camera reattached: {_mode}"
            );
        }
        else
        {
            // Freeze the camera's world transform.
            _lastFollowMode = _mode;
            _mode = CameraMode.Static;

            Plugin.ModLogger.LogInfo(
                "[F12] Camera detached and frozen."
            );
        }

        UpdateDesktopCullingMask(true);
    }

    // -----------------------------------
    // ORIGINAL UNITY MIRROR MODES
    // -----------------------------------

    private void SetMirrorMode(
        GameViewRenderMode mode,
        string key)
    {
        try
        {
            if (!XRSettings.enabled ||
                !XRSettings.isDeviceActive)
            {
                Plugin.ModLogger.LogWarning(
                    $"[{key}] No active VR device."
                );

                return;
            }

            _autoStartHandled = true;

            if (IsCameraActive)
                DisableCamera();

            var previous =
                XRSettings.gameViewRenderMode;

            XRSettings.gameViewRenderMode =
                mode;

            Plugin.ModLogger.LogInfo(
                $"[{key}] Mirror: {previous} -> " +
                $"{XRSettings.gameViewRenderMode}"
            );

            _requestedMirrorMode = mode;
            _mirrorCheckTimer = 0.5f;
            _pendingMirrorCheck = true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Mirror change failed: {ex}"
            );
        }
    }

    private void VerifyMirrorMode()
    {
        try
        {
            var current =
                XRSettings.gameViewRenderMode;

            Plugin.ModLogger.LogInfo(
                "Mirror verification: requested " +
                $"{_requestedMirrorMode}, current {current}"
            );
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Mirror verification failed: {ex.Message}"
            );
        }
    }

    // -----------------------------------
    // DIAGNOSTICS
    // -----------------------------------

    private void TakeSnapshot(string reason)
    {
        var log = Plugin.ModLogger;

        log.LogInfo(
            $"=== BetterMirror snapshot ({reason}) " +
            $"at {_elapsed:0.0}s ==="
        );

        try
        {
            log.LogInfo(
                $"Unity={Application.unityVersion} " +
                $"Screen={Screen.width}x{Screen.height}"
            );

            log.LogInfo(
                $"XR={XRSettings.enabled} " +
                $"Active={XRSettings.isDeviceActive} " +
                $"Runtime={XRSettings.loadedDeviceName}"
            );

            log.LogInfo(
                $"Mirror={XRSettings.gameViewRenderMode} " +
                $"Stereo={XRSettings.stereoRenderingMode}"
            );

            log.LogInfo(
                $"CreatorCamera={IsCameraActive} " +
                $"Mode={_mode} " +
                $"HorizontalFOV={GetHorizontalFov():0}"
            );

            log.LogInfo(
                $"AvatarToggle={Plugin.ShowLocalAvatar.Value} " +
                $"AvatarLayer={_avatarLayer} " +
                $"AvatarIncluded={ShouldShowAvatar()}"
            );

            if (_source != null)
            {
                log.LogInfo(
                    "Source camera mask=0x" +
                    _source.cullingMask.ToString("X8")
                );
            }

            if (_desktop != null)
            {
                Vector3 pos =
                    _desktop.transform.position;

                log.LogInfo(
                    "Desktop Camera: " +
                    $"FOV={_desktop.fieldOfView:0.0} " +
                    $"Aspect={_desktop.aspect:0.000} " +
                    $"Stereo={_desktop.stereoEnabled} " +
                    $"Eye={_desktop.stereoTargetEye} " +
                    $"Position=(" +
                    $"{pos.x:0.00}," +
                    $"{pos.y:0.00}," +
                    $"{pos.z:0.00})"
                );

                log.LogInfo(
                    "Desktop camera mask=0x" +
                    _desktop.cullingMask.ToString("X8")
                );
            }

            var cameras =
                Camera.allCameras;

            log.LogInfo(
                $"Active cameras: {cameras.Length}"
            );

            for (int i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];

                if (camera == null)
                    continue;

                log.LogInfo(
                    $"Camera[{i}] '{camera.name}' " +
                    $"FOV={camera.fieldOfView:0.0} " +
                    $"Stereo={camera.stereoEnabled} " +
                    $"Depth={camera.depth:0.0}"
                );
            }
        }
        catch (Exception ex)
        {
            log.LogError(
                $"Snapshot failed: {ex}"
            );
        }

        log.LogInfo(
            "=== End BetterMirror snapshot ==="
        );
    }
}
