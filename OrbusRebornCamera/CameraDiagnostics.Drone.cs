using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

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
    private float _droneRoll;
    private Quaternion _dronePlaybackRotation = Quaternion.identity;
    private bool _droneLooking;
    private float _droneNextInputWarning;
    private bool _droneCursorWasVisible;
    private CursorLockMode _dronePreviousCursorLock;

    // The game streams scenery and remote player objects around the REAL
    // local player, not this cinematic camera. Observations only: never
    // move the rig, request remote entities or spoof a streaming origin.
    internal static string StreamingStatus { get; private set; } =
        "Freecam distance: not active";

    private const float StreamingAdvisoryDistance = 35f; // Warning, not a measured loading radius.
    private const float LargePlayerRepositionDistance = 80f; // Conservative warp diagnostic.
    private string _droneWorldSignature = "";
    private float _nextDroneWorldCheck;
    private float _droneWorldChangedAt;
    private bool _droneRecenterPending;
    private bool _droneDistanceWarningLogged;
    private bool _droneHasObservedEye;
    private Vector3 _droneLastObservedEye;

    private static string LoadedWorldSignature()
    {
        var loaded = new List<string>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            var scene = SceneManager.GetSceneAt(index);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            if (scene.name == "BootstrapWeathermaker" ||
                scene.name == "DontDestroyOnLoad")
                continue;
            loaded.Add(scene.name);
        }
        loaded.Sort(StringComparer.Ordinal);
        return string.Join("|", loaded);
    }

    private void ScheduleDroneRecenter(string reason)
    {
        _droneRecenterPending = true;
        _droneWorldChangedAt = Time.unscaledTime;
        Plugin.ModLogger.LogInfo(
            "[Freecam] World/position transition observed (" + reason +
            "). Recenter will wait for the game camera and world to settle.");
    }

    private bool RecenterDroneToPlayer(string reason)
    {
        Camera? playerEye = FindVRCamera();
        if (playerEye == null || _desktop == null)
            return false;

        // The eye camera supplies the location of the local player. Its
        // transform is only READ. This affects _desktop, never the actual
        // player's position, avatar, gameplay movement or streamed entities.
        _source = playerEye;
        ConfigureDesktopCamera();
        StopKeyframePlayback();
        ReleaseDronePointer();
        _dronePosition = playerEye.transform.position;
        Vector3 euler = playerEye.transform.rotation.eulerAngles;
        _droneYaw = euler.y;
        _dronePitch = Mathf.Clamp(
            Mathf.DeltaAngle(0f, euler.x), -89f, 89f);
        _droneRoll = Mathf.DeltaAngle(0f, euler.z);
        _droneVelocity = Vector3.zero;
        _droneLastObservedEye = _dronePosition;
        _droneHasObservedEye = true;
        StreamingStatus = "Freecam: recentered to local player";
        Plugin.ModLogger.LogInfo("[Freecam] Camera recentered: " + reason +
            " | real player was NOT moved.");
        return true;
    }

    private void MonitorDroneWorld()
    {
        if (_mode != CameraMode.Drone)
        {
            StreamingStatus = "Freecam distance: not active";
            return;
        }
        if (Time.unscaledTime < _nextDroneWorldCheck)
            return;
        _nextDroneWorldCheck = Time.unscaledTime + 0.4f;

        string world = LoadedWorldSignature();
        if (world != _droneWorldSignature)
        {
            _droneWorldSignature = world;
            ScheduleDroneRecenter("loaded zone changed");
        }

        Camera? playerEye = FindVRCamera();
        if (playerEye != null)
        {
            Vector3 eye = playerEye.transform.position;
            if (_droneHasObservedEye &&
                Vector3.Distance(eye, _droneLastObservedEye) >
                    LargePlayerRepositionDistance)
                ScheduleDroneRecenter("large local-player reposition");
            _droneLastObservedEye = eye;
            _droneHasObservedEye = true;

            float distance = Vector3.Distance(_dronePosition, eye);
            StreamingStatus = "Freecam " + distance.ToString("0") +
                "m from player" +
                (distance > StreamingAdvisoryDistance
                    ? " - distant scenery/players may unload" : "");
            if (distance > StreamingAdvisoryDistance &&
                !_droneDistanceWarningLogged)
            {
                _droneDistanceWarningLogged = true;
                Plugin.ModLogger.LogWarning(
                    "[Freecam] Camera is " + distance.ToString("0") +
                    "m from the local player. Orbus streaming can unload scenery " +
                    "and other players. Ctrl+Shift+F11 recenters the CAMERA only.");
            }
            else if (distance < StreamingAdvisoryDistance * 0.7f)
                _droneDistanceWarningLogged = false;
        }
        else
            StreamingStatus = "Freecam: waiting for local game eye";

        // Require a loaded zone and stable scene for at least one second
        // before recentering. Never snap to stale headset/selection-menu
        // coordinates during a loading transition.
        if (_droneRecenterPending && world.Length > 0 &&
            Time.unscaledTime - _droneWorldChangedAt >= 1.0f &&
            RecenterDroneToPlayer("world switch / player warp"))
        {
            _droneRecenterPending = false;
            _droneDistanceWarningLogged = false;
        }
    }

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
        _droneRoll = Mathf.DeltaAngle(0f, euler.z);
        _droneVelocity = Vector3.zero;
        _mode = CameraMode.Drone;
        _snapNextFrame = false;
        _droneWorldSignature = LoadedWorldSignature();
        _droneRecenterPending = false;
        _droneHasObservedEye = false;
        _droneDistanceWarningLogged = false;
        _nextDroneWorldCheck = 0f;
        StreamingStatus = "Freecam: measuring player distance";
        UpdateDesktopCullingMask(true);

        Plugin.ModLogger.LogInfo(
            "[Freecam] Enabled. Ctrl+Shift+F11 resets camera to player. " +
            "The game streams objects around your avatar, not freecam.");
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
            _playingKeyframes)
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
        // Mouse and WASD can control freecam while the creator panel remains
        // open, but ONLY after the right button is pressed outside the panel.
        // Once flying, continuing over its bounds doesn't steal the mouse.
        if (rightHeld && !_droneLooking &&
            _panelVisible && IsPointerOverCreatorPanel())
        {
            ReleaseDronePointer();
            _droneVelocity = Vector3.zero;
            return;
        }

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

        // Z/C bank the camera; X levels it. These affect ONLY the desktop
        // camera and are saved into the existing quaternion keyframes.
        float rollSpeed = Mathf.Clamp(
            Plugin.DroneRollSpeed.Value, 5f, 180f);
        if (DroneKeyDown(0x5A)) _droneRoll += rollSpeed * dt; // Z
        if (DroneKeyDown(0x43)) _droneRoll -= rollSpeed * dt; // C
        if (DroneKeyDown(0x58)) _droneRoll = 0f;             // X
        _droneRoll = Mathf.DeltaAngle(0f, _droneRoll);

        Quaternion rotation = Quaternion.Euler(
            _dronePitch, _droneYaw, _droneRoll);

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
        _desktop.transform.rotation = _playingKeyframes
            ? _dronePlaybackRotation
            : Quaternion.Euler(_dronePitch, _droneYaw, _droneRoll);
    }

    private void LeaveDrone()
    {
        // The detached camera may be disabled before LateUpdate runs, so
        // restore cinema-only settings immediately whenever freecam exits.
        RestoreCinematicRendering();
        ReleaseDronePointer();
        _droneVelocity = Vector3.zero;
        _droneRecenterPending = false;
        StreamingStatus = "Freecam distance: not active";
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
        RestoreCinematicRendering();
    }

    private void OnDestroy()
    {
        LeaveDrone();
        RestoreCinematicRendering();
        DestroyPathPreviewMaterial();
    }
}
