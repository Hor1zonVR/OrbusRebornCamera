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

    private void ToggleFreecam()
    {
        if (_mode == CameraMode.Drone)
        {
            StopKeyframePlayback();
            LeaveDrone();
            _mode = _lastFollowMode;
            _snapNextFrame = true;
            UpdateDesktopCullingMask(true);
            Plugin.ModLogger.LogInfo(
                "[Freecam] Returned to " + _mode + ".");
            return;
        }

        EnterFreecam();
    }

    private bool EnterFreecam()
    {
        _autoStartHandled = true;

        if (!IsCameraActive && !TryEnableCamera(true))
        {
            Plugin.ModLogger.LogWarning(
                "[Freecam] Cannot start: Orbus has not provided a world " +
                "camera. A headset-free launch also requires the game " +
                "to support loading a playable world without XR.");
            TakeSnapshot("freecam camera unavailable");
            return false;
        }

        if (_desktop == null)
            return false;

        // The current recording camera pose is the starting position.
        // Never alter the local avatar, VR tracking, or server position.
        _dronePosition = _desktop.transform.position;
        Vector3 euler = _desktop.transform.rotation.eulerAngles;
        _droneYaw = euler.y;
        _dronePitch = Mathf.Clamp(
            Mathf.DeltaAngle(0f, euler.x), -89f, 89f);
        _droneVelocity = Vector3.zero;
        _mode = CameraMode.Drone;
        _snapNextFrame = false;
        UpdateDesktopCullingMask(true);

        Plugin.ModLogger.LogInfo(
            "[Freecam] Enabled. Hold right mouse + WASD to fly, " +
            "Q/E down/up, Shift faster, Ctrl slower. " +
            "Use F2 for keyframes and Ctrl+F11 to exit. " +
            "The camera never moves the player.");
        return true;
    }

    private void HandleDroneInput()
    {
        if (_mode != CameraMode.Drone || !IsCameraActive ||
            _playingKeyframes || _panelVisible)
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

        // The game camera may be recreated on world changes; do not permanently
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
