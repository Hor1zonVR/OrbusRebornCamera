using System;
using UnityEngine;
using UnityEngine.XR;

namespace OrbusBetterMirror;

/// <summary>
/// Independent desktop fly-camera controls. This is an explicitly opt-in
/// experimental feature until the game's random collectibles can be identified
/// and safely excluded from cinematic output.
///
/// IMPORTANT: This never moves the player's avatar, controller transforms,
/// tracked headset, or gameplay camera. It only positions _desktop.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private const int RightMouseButton = 0x02;
    private const int ShiftVirtualKey = 0x10;
    private const int ControlVirtualKey = 0x11;

    private Vector3 _dronePosition;
    private Vector3 _droneVelocity;
    private float _droneYaw;
    private float _dronePitch;
    private bool _droneLooking;
    private float _droneNextInputWarning;
    private bool _droneCursorWasVisible;
    private CursorLockMode _dronePreviousCursorLock;

    private static bool DroneKeyDown(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    private void ToggleExperimentalDrone()
    {
        _autoStartHandled = true;

        if (_mode == CameraMode.Drone)
        {
            LeaveDrone();
            _mode = _lastFollowMode;
            _snapNextFrame = true;
            UpdateDesktopCullingMask(true);
            Plugin.ModLogger.LogInfo(
                "[Drone] Exited freecam. Restored " + _mode + ".");
            return;
        }

        if (!Plugin.EnableExperimentalDrone.Value)
        {
            Plugin.ModLogger.LogWarning(
                "[Drone] Freecam is LOCKED by default until randomly spawned " +
                "collectibles can be protected from camera scouting. " +
                "For private testing only, enable [ExperimentalDrone] " +
                "EnableExperimentalDrone in the BepInEx config, " +
                "then restart the game. Do not use the prototype " +
                "in public multiplayer.");
            return;
        }

        // Existing creator camera can already be running in follow mode.
        // Otherwise, also try to initialize in a non-XR world. This depends
        // on Orbus having loaded its world and Camera (eye) successfully.
        if (!IsCameraActive && !TryEnableCamera(true, droneAttempt: true))
        {
            Plugin.ModLogger.LogWarning(
                "[Drone] Could not start. The game must load a world and " +
                "provide Camera (eye). A disconnected VR headset may prevent " +
                "OrbusVR itself from reaching this state.");
            // Snapshot contains XR status and available camera names.
            // Useful for determining if headset-free sessions are possible
            // without guessing at game-specific startup flags.
            TakeSnapshot("drone activation unavailable");
            return;
        }

        if (_desktop == null)
            return;

        _dronePosition = _desktop.transform.position;
        Vector3 euler = _desktop.transform.rotation.eulerAngles;
        _droneYaw = euler.y;
        _dronePitch = Mathf.DeltaAngle(0f, euler.x);
        _dronePitch = Mathf.Clamp(_dronePitch, -89f, 89f);
        _droneVelocity = Vector3.zero;
        _mode = CameraMode.Drone;
        _snapNextFrame = false;
        UpdateDesktopCullingMask(true);

        Plugin.ModLogger.LogWarning(
            "[Drone] EXPERIMENTAL PRIVATE-TESTING MODE ACTIVE. " +
            "Random collectible filtering is NOT IMPLEMENTED. " +
            "Do not use this in public multiplayer.");
        Plugin.ModLogger.LogInfo(
            "[Drone] Hold right mouse to steer + move with WASD; " +
            "Q/E down/up, Shift boost, Ctrl slow, F12 freeze, " +
            "Ctrl+F11 return to player camera. " +
            "Only the desktop creator camera is moved.");
    }

    private void HandleDroneInput()
    {
        if (_mode != CameraMode.Drone || !IsCameraActive)
        {
            ReleaseDronePointer();
            return;
        }

        try
        {
            HandleDroneInputCore();
        }
        catch (Exception ex)
        {
            ReleaseDronePointer();
            _droneVelocity = Vector3.zero;
            if (Time.unscaledTime >= _droneNextInputWarning)
            {
                _droneNextInputWarning = Time.unscaledTime + 10f;
                Plugin.ModLogger.LogWarning(
                    "[Drone] Input unavailable: " + ex.Message);
            }
        }
    }

    private void HandleDroneInputCore()
    {
        if (!Application.isFocused || _desktop == null)
        {
            ReleaseDronePointer();
            _droneVelocity = Vector3.zero;
            return;
        }

        bool rightHeld = DroneKeyDown(RightMouseButton);

        if (!rightHeld)
        {
            // Input deliberately requires RMB to avoid intercepting ordinary
            // Orbus keyboard/menu interaction when freecam is idle.
            ReleaseDronePointer();
            _droneVelocity = Vector3.zero;
            return;
        }

        if (!_droneLooking)
        {
            _droneCursorWasVisible = Cursor.visible;
            _dronePreviousCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _droneLooking = true;
            // Discard the initial mouse-axis event after cursor capture.
            return;
        }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (dt <= 0f)
            return;

        // Unity's standard mouse axes work in the desktop window without
        // modifying tracking data or sending controller input.
        float mouseX = Input.GetAxisRaw("Mouse X");
        float mouseY = Input.GetAxisRaw("Mouse Y");
        float sensitivity = Mathf.Clamp(
            Plugin.DroneMouseSensitivity.Value, 0.1f, 8f);

        _droneYaw += mouseX * sensitivity;
        _dronePitch = Mathf.Clamp(
            _dronePitch - mouseY * sensitivity, -89f, 89f);

        Quaternion rotation = Quaternion.Euler(
            _dronePitch, _droneYaw, 0f);

        Vector3 direction = Vector3.zero;

        if (DroneKeyDown(0x57)) direction += rotation * Vector3.forward; // W
        if (DroneKeyDown(0x53)) direction -= rotation * Vector3.forward; // S
        if (DroneKeyDown(0x44)) direction += rotation * Vector3.right;   // D
        if (DroneKeyDown(0x41)) direction -= rotation * Vector3.right;   // A
        if (DroneKeyDown(0x45)) direction += Vector3.up;                // E
        if (DroneKeyDown(0x51)) direction -= Vector3.up;                // Q

        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        float speed = Mathf.Clamp(
            Plugin.DroneMoveSpeed.Value, 0.25f, 20f);

        if (DroneKeyDown(ShiftVirtualKey))
            speed *= Mathf.Clamp(Plugin.DroneBoostMultiplier.Value, 1f, 8f);
        else if (DroneKeyDown(ControlVirtualKey))
            speed *= 0.2f;

        Vector3 desiredVelocity = direction * speed;
        float smoothing = Mathf.Clamp(
            Plugin.DroneMovementSmoothing.Value, 0f, 30f);

        if (smoothing < 0.01f)
            _droneVelocity = desiredVelocity;
        else
        {
            float blend = 1f - Mathf.Exp(-smoothing * dt);
            _droneVelocity = Vector3.Lerp(
                _droneVelocity, desiredVelocity, blend);
        }

        _dronePosition += _droneVelocity * dt;
    }

    private void ApplyDronePose()
    {
        if (_desktop == null)
            return;

        // Camera (eye) may be recreated on world changes; do not permanently
        // point the recording camera at a destroyed or unrelated scene object.
        if (_source == null)
        {
            _source = FindVRCamera();
            if (_source != null)
                ConfigureDesktopCamera();
        }

        _desktop.transform.position = _dronePosition;
        _desktop.transform.rotation = Quaternion.Euler(
            _dronePitch, _droneYaw, 0f);
    }

    private void LeaveDrone()
    {
        ReleaseDronePointer();
        _droneVelocity = Vector3.zero;
    }

    private void ReleaseDronePointer()
    {
        if (!_droneLooking)
            return;

        try
        {
            Cursor.lockState = _dronePreviousCursorLock;
            Cursor.visible = _droneCursorWasVisible;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[Drone] Could not restore cursor: " + ex.Message);
        }

        _droneLooking = false;
    }

    private void OnDisable()
    {
        LeaveDrone();
    }

    private void OnDestroy()
    {
        LeaveDrone();
    }
}
