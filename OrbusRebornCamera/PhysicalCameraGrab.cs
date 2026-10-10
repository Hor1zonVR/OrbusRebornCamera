
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

namespace OrbusBetterMirror;

public sealed class PhysicalCameraGrab : MonoBehaviour
{
    public PhysicalCameraGrab(IntPtr pointer) : base(pointer)
    {
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F3Key = 0x72;

    // Unity's legacy VR grip-button mappings.
    private const KeyCode LeftGripKey =
        KeyCode.JoystickButton4;

    private const KeyCode RightGripKey =
        KeyCode.JoystickButton5;

    private enum GrabHand
    {
        None,
        Left,
        Right
    }

    private struct HandState
    {
        public bool Valid;
        public bool Grip;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    // Matches the handle on our v0.7.0 placeholder.
    private static readonly Vector3 GripAnchorLocal =
        new Vector3(0.075f, -0.125f, -0.025f);

    // We can adjust this after testing in VR.
    private static readonly Vector3 SnapRotationEuler =
        Vector3.zero;

    private PhysicalCameraPrototype? _prototype;

    private GrabHand _heldBy = GrabHand.None;

    private bool _leftWasGripping;
    private bool _rightWasGripping;
    private bool _f3WasDown;

    private bool _trackingLogged;

    private float _nextWarningTime;

    private void Start()
    {
        _prototype =
            GetComponent<PhysicalCameraPrototype>();

        Plugin.ModLogger.LogInfo(
            "[CameraGrab] v0.7.1 legacy XR grabbing initialized."
        );

        Plugin.ModLogger.LogInfo(
            "[CameraGrab] Using InputTracking for controller poses."
        );

        Plugin.ModLogger.LogInfo(
            "[CameraGrab] Using legacy joystick buttons 4/5 for grip."
        );

        Plugin.ModLogger.LogInfo(
            "[CameraGrab] Squeeze grip near the handle to grab."
        );

        Plugin.ModLogger.LogInfo(
            "[CameraGrab] Release grip to float in place."
        );
    }

    private void Update()
    {
        try
        {
            UpdateGrabbing();
        }
        catch (Exception ex)
        {
            DropCamera("controller input error");

            if (Time.unscaledTime >= _nextWarningTime)
            {
                _nextWarningTime =
                    Time.unscaledTime + 10f;

                Plugin.ModLogger.LogWarning(
                    "[CameraGrab] Input error: " + ex
                );
            }
        }
    }

    private void UpdateGrabbing()
    {
        bool f3Down =
            Application.isFocused &&
            (GetAsyncKeyState(F3Key) & 0x8000) != 0;

        bool recalled =
            f3Down && !_f3WasDown;

        _f3WasDown = f3Down;

        if (recalled)
        {
            DropCamera("F3 recall");
            return;
        }

        if (_prototype == null)
        {
            _prototype =
                GetComponent<PhysicalCameraPrototype>();
        }

        Transform? cameraObject =
            _prototype?.CameraTransform;

        if (cameraObject == null ||
            !cameraObject.gameObject.activeInHierarchy)
        {
            DropCamera("camera hidden or unavailable");
            return;
        }

        Camera? eye = FindEyeCamera();

        if (eye == null)
        {
            DropCamera("VR eye camera unavailable");
            Warn("Waiting for VR eye camera.");
            return;
        }

        // The legacy XR system reports positions
        // relative to the VR tracking origin.
        //
        // Calculate the transformation between the
        // tracking origin and OrbusVR world space using
        // the known, working headset camera.
        Vector3 localHeadPosition =
            InputTracking.GetLocalPosition(XRNode.Head);

        Quaternion localHeadRotation =
            InputTracking.GetLocalRotation(XRNode.Head);

        Quaternion originRotation =
            eye.transform.rotation *
            Quaternion.Inverse(localHeadRotation);

        Vector3 originPosition =
            eye.transform.position -
            originRotation * localHeadPosition;

        HandState left = ReadHand(
            XRNode.LeftHand,
            LeftGripKey,
            originPosition,
            originRotation
        );

        HandState right = ReadHand(
            XRNode.RightHand,
            RightGripKey,
            originPosition,
            originRotation
        );

        bool leftGrip =
            left.Valid && left.Grip;

        bool rightGrip =
            right.Valid && right.Grip;

        bool leftPressed =
            leftGrip && !_leftWasGripping;

        bool rightPressed =
            rightGrip && !_rightWasGripping;

        _leftWasGripping = leftGrip;
        _rightWasGripping = rightGrip;

        if (!_trackingLogged &&
            (left.Valid || right.Valid))
        {
            _trackingLogged = true;

            Plugin.ModLogger.LogInfo(
                "[CameraGrab] Legacy XR controller tracking available."
            );
        }

        // Log grip presses regardless of proximity.
        // This helps diagnose controller mappings.
        if (leftPressed)
        {
            Plugin.ModLogger.LogInfo(
                "[CameraGrab] LEFT grip input detected."
            );
        }

        if (rightPressed)
        {
            Plugin.ModLogger.LogInfo(
                "[CameraGrab] RIGHT grip input detected."
            );
        }

        // Already holding the camera with left hand.
        if (_heldBy == GrabHand.Left)
        {
            if (!leftGrip)
            {
                DropCamera("left grip released");
            }
            else
            {
                FollowHand(cameraObject, left);
            }

            return;
        }

        // Already holding the camera with right hand.
        if (_heldBy == GrabHand.Right)
        {
            if (!rightGrip)
            {
                DropCamera("right grip released");
            }
            else
            {
                FollowHand(cameraObject, right);
            }

            return;
        }

        float grabRadius = Mathf.Clamp(
            Plugin.PrototypeGrabRadius.Value,
            0.10f,
            0.60f
        );

        Vector3 handlePosition =
            cameraObject.TransformPoint(GripAnchorLocal);

        // Start a new grab only on a new grip press.
        // The controller must be close to the handle.
        if (leftPressed)
        {
            float distance = Vector3.Distance(
                left.Position,
                handlePosition
            );

            Plugin.ModLogger.LogInfo(
                $"[CameraGrab] LEFT distance to handle: {distance:0.000}m"
            );

            if (distance <= grabRadius)
            {
                _heldBy = GrabHand.Left;

                Plugin.ModLogger.LogInfo(
                    "[CameraGrab] GRABBED with LEFT hand."
                );

                FollowHand(cameraObject, left);
                return;
            }
        }

        if (rightPressed)
        {
            float distance = Vector3.Distance(
                right.Position,
                handlePosition
            );

            Plugin.ModLogger.LogInfo(
                $"[CameraGrab] RIGHT distance to handle: {distance:0.000}m"
            );

            if (distance <= grabRadius)
            {
                _heldBy = GrabHand.Right;

                Plugin.ModLogger.LogInfo(
                    "[CameraGrab] GRABBED with RIGHT hand."
                );

                FollowHand(cameraObject, right);
            }
        }
    }

    private static Camera? FindEyeCamera()
    {
        if (!XRSettings.enabled ||
            !XRSettings.isDeviceActive)
        {
            return null;
        }

        foreach (Camera camera in Camera.allCameras)
        {
            if (camera == null)
                continue;

            if (camera.name == "Camera (eye)" &&
                camera.stereoEnabled)
            {
                return camera;
            }
        }

        return null;
    }

    private static HandState ReadHand(
        XRNode handNode,
        KeyCode gripKey,
        Vector3 originPosition,
        Quaternion originRotation)
    {
        HandState result = new HandState();

        // Legacy Unity VR tracking, rather than
        // InputDevices / CommonUsages.
        Vector3 localPosition =
            InputTracking.GetLocalPosition(handNode);

        Quaternion localRotation =
            InputTracking.GetLocalRotation(handNode);

        // Do not use an entirely empty tracking pose.
        // This generally means no controller pose
        // has been supplied by the XR runtime.
        if (localPosition.sqrMagnitude < 0.000001f &&
            Quaternion.Angle(
                localRotation,
                Quaternion.identity
            ) < 0.01f)
        {
            return result;
        }

        result.Valid = true;

        // Uses legacy joystick button mapping.
        result.Grip = Input.GetKey(gripKey);

        result.Position =
            originPosition +
            originRotation * localPosition;

        result.Rotation =
            originRotation * localRotation;

        return result;
    }

    private static void FollowHand(
        Transform cameraObject,
        HandState hand)
    {
        Quaternion rotation =
            hand.Rotation *
            Quaternion.Euler(SnapRotationEuler);

        // Place the model's actual handle at
        // the controller's tracked position.
        Vector3 position =
            hand.Position -
            rotation * GripAnchorLocal;

        cameraObject.position = position;
        cameraObject.rotation = rotation;
    }

    private void DropCamera(string reason)
    {
        if (_heldBy == GrabHand.None)
            return;

        Plugin.ModLogger.LogInfo(
            "[CameraGrab] RELEASED: " + reason
        );

        _heldBy = GrabHand.None;

        // No physics and no parent transform.
        // The camera stays at its last world position.
    }

    private void Warn(string message)
    {
        if (Time.unscaledTime < _nextWarningTime)
            return;

        _nextWarningTime =
            Time.unscaledTime + 10f;

        Plugin.ModLogger.LogWarning(
            "[CameraGrab] " + message
        );
    }

    private void OnDisable()
    {
        DropCamera("component disabled");
    }
}
