
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

namespace OrbusBetterMirror;

public sealed class PhysicalCameraPrototype : MonoBehaviour
{
    public PhysicalCameraPrototype(IntPtr pointer) : base(pointer)
    {
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F3Key = 0x72;
    private const int ShiftKey = 0x10;

    private GameObject? _root;

    private bool _visible;
    private bool _f3WasDown;

    private float _elapsed;
    private float _nextSpawnAttempt = 7f;

    // Intended to be reusable when real grabbing is added.
    public Transform? CameraTransform =>
        _root != null ? _root.transform : null;

    private void Start()
    {
        _visible = Plugin.PrototypeVisible.Value;

        Plugin.ModLogger.LogInfo(
            "[PhysicalCamera] Prototype initialized."
        );

        Plugin.ModLogger.LogInfo(
            "[PhysicalCamera] Waiting for an active VR headset."
        );

        Plugin.ModLogger.LogInfo(
            "[PhysicalCamera] F3 recall, Shift+F3 visibility."
        );
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;

        HandleTemporaryKeyboardControls();

        // Spawn the model when the VR eye camera exists.
        // This does not require the desktop recording camera.
        if (_root == null &&
            _visible &&
            _elapsed >= _nextSpawnAttempt)
        {
            _nextSpawnAttempt = _elapsed + 2f;
            TryPlaceInFrontOfPlayer(false);
        }
    }

    private void HandleTemporaryKeyboardControls()
    {
        bool f3Down =
            Application.isFocused &&
            (GetAsyncKeyState(F3Key) & 0x8000) != 0;

        if (f3Down && !_f3WasDown)
        {
            bool shiftDown =
                (GetAsyncKeyState(ShiftKey) & 0x8000) != 0;

            if (shiftDown)
            {
                ToggleVisibility();
            }
            else
            {
                RecallCamera();
            }
        }

        _f3WasDown = f3Down;
    }

    private void RecallCamera()
    {
        // Recalling also makes a hidden camera visible.
        _visible = true;
        Plugin.PrototypeVisible.Value = true;

        TryPlaceInFrontOfPlayer(true);
    }

    private void ToggleVisibility()
    {
        _visible = !_visible;

        Plugin.PrototypeVisible.Value = _visible;

        if (_root != null)
        {
            _root.SetActive(_visible);
        }

        Plugin.ModLogger.LogInfo(
            "[PhysicalCamera] Visibility: " +
            (_visible ? "ON" : "OFF")
        );

        // If the object hasn't been created yet,
        // enabling visibility allows automatic spawning.
        if (_visible && _root == null)
        {
            _nextSpawnAttempt = _elapsed;
        }
    }

    private Camera? FindEyeCamera()
    {
        if (!XRSettings.enabled ||
            !XRSettings.isDeviceActive)
        {
            return null;
        }

        // Use the same known eye-camera identification
        // as the existing BetterMirror controller.
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

    private bool TryPlaceInFrontOfPlayer(bool manual)
    {
        Camera? eye = FindEyeCamera();

        if (eye == null)
        {
            if (manual)
            {
                Plugin.ModLogger.LogWarning(
                    "[PhysicalCamera] Cannot place camera yet. " +
                    "VR eye camera not found."
                );
            }

            return false;
        }

        try
        {
            if (_root == null)
            {
                CreatePlaceholder();
            }

            if (_root == null)
                return false;

            Transform head = eye.transform;

            // Keep the placement horizontal so looking
            // straight down doesn't spawn the prop below
            // the floor.
            Vector3 forward = head.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            float distance = Mathf.Clamp(
                Plugin.PrototypeSpawnDistance.Value,
                0.4f,
                2f
            );

            Vector3 position =
                head.position +
                forward * distance -
                Vector3.up * 0.15f;

            Quaternion rotation = Quaternion.LookRotation(
                forward,
                Vector3.up
            );

            // No parent, Rigidbody, gravity or follow
            // component. This pose remains fixed.
            _root.transform.position = position;
            _root.transform.rotation = rotation;

            _root.SetActive(_visible);

            Plugin.ModLogger.LogInfo(
                "[PhysicalCamera] Camera placed at " +
                $"({position.x:0.00}, " +
                $"{position.y:0.00}, " +
                $"{position.z:0.00})."
            );

            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                "[PhysicalCamera] Failed to place prototype: " +
                ex
            );

            return false;
        }
    }

    private void CreatePlaceholder()
    {
        // This root will eventually be the physical camera
        // rig used by the VR grabbing and recording systems.
        GameObject root = new GameObject(
            "OrbusRebornCamera_PhysicalPrototype"
        );

        _root = root;

        UnityEngine.Object.DontDestroyOnLoad(root);

        try
        {
            Transform parent = root.transform;

            Color body = new Color(
                0.17f, 0.20f, 0.24f, 1f
            );

            Color darker = new Color(
                0.07f, 0.09f, 0.12f, 1f
            );

            Color metal = new Color(
                0.42f, 0.46f, 0.50f, 1f
            );

            Color blue = new Color(
                0.12f, 0.62f, 0.85f, 1f
            );

            Color red = new Color(
                0.95f, 0.16f, 0.15f, 1f
            );

            // Main camera body.
            CreatePart(
                parent,
                PrimitiveType.Cube,
                "CameraBody",
                new Vector3(0f, 0f, 0f),
                new Vector3(0.23f, 0.15f, 0.24f),
                Quaternion.identity,
                body
            );

            // Front lens housing.
            CreatePart(
                parent,
                PrimitiveType.Cylinder,
                "LensBarrel",
                new Vector3(0f, 0.005f, 0.155f),
                new Vector3(0.065f, 0.065f, 0.065f),
                Quaternion.Euler(90f, 0f, 0f),
                metal
            );

            // Dark glass at the front of the lens.
            CreatePart(
                parent,
                PrimitiveType.Sphere,
                "LensGlass",
                new Vector3(0f, 0.005f, 0.222f),
                new Vector3(0.096f, 0.096f, 0.024f),
                Quaternion.identity,
                darker
            );

            // A blue lens ring for easy identification.
            CreatePart(
                parent,
                PrimitiveType.Cylinder,
                "LensRing",
                new Vector3(0f, 0.005f, 0.192f),
                new Vector3(0.07f, 0.009f, 0.07f),
                Quaternion.Euler(90f, 0f, 0f),
                blue
            );

            // Small top handle.
            CreatePart(
                parent,
                PrimitiveType.Cube,
                "TopHandle",
                new Vector3(0f, 0.116f, -0.02f),
                new Vector3(0.15f, 0.034f, 0.035f),
                Quaternion.identity,
                darker
            );

            // Two supports for the top handle.
            CreatePart(
                parent,
                PrimitiveType.Cube,
                "HandleSupportFront",
                new Vector3(0f, 0.09f, 0.035f),
                new Vector3(0.035f, 0.065f, 0.035f),
                Quaternion.identity,
                metal
            );

            CreatePart(
                parent,
                PrimitiveType.Cube,
                "HandleSupportBack",
                new Vector3(0f, 0.09f, -0.075f),
                new Vector3(0.035f, 0.065f, 0.035f),
                Quaternion.identity,
                metal
            );

            // Small grip underneath the body.
            // Later this will be our snap-to-hand point.
            CreatePart(
                parent,
                PrimitiveType.Cube,
                "GripHandle",
                new Vector3(0.075f, -0.125f, -0.025f),
                new Vector3(0.07f, 0.13f, 0.09f),
                Quaternion.identity,
                darker
            );

            // Flip-out monitor housing.
            CreatePart(
                parent,
                PrimitiveType.Cube,
                "PreviewScreenHousing",
                new Vector3(-0.18f, 0.018f, -0.03f),
                new Vector3(0.14f, 0.11f, 0.022f),
                Quaternion.identity,
                darker
            );

            // Blue placeholder for a future live preview.
            // It is NOT a functional screen yet.
            CreatePart(
                parent,
                PrimitiveType.Cube,
                "PreviewScreenPlaceholder",
                new Vector3(-0.18f, 0.018f, -0.045f),
                new Vector3(0.115f, 0.086f, 0.006f),
                Quaternion.identity,
                blue
            );

            // Red recording-indicator placeholder.
            CreatePart(
                parent,
                PrimitiveType.Sphere,
                "RecordingIndicator",
                new Vector3(0.075f, 0.084f, 0.055f),
                new Vector3(0.025f, 0.025f, 0.025f),
                Quaternion.identity,
                red
            );

            Plugin.ModLogger.LogInfo(
                "[PhysicalCamera] Placeholder model created."
            );
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            _root = null;
            throw;
        }
    }

    private static GameObject CreatePart(
        Transform parent,
        PrimitiveType primitiveType,
        string name,
        Vector3 localPosition,
        Vector3 localScale,
        Quaternion localRotation,
        Color color)
    {
        GameObject part = GameObject.CreatePrimitive(
            primitiveType
        );

        part.name = name;

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localRotation = localRotation;

        // This is a visual test, so the placeholders
        // must not interfere with gameplay collisions.
        Collider collider = part.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
        }

        Renderer renderer = part.GetComponent<Renderer>();

        if (renderer != null)
        {
            renderer.material.color = color;
        }

        return part;
    }

    private void OnDestroy()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
