using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace OrbusBetterMirror;

/// <summary>
/// Read-only feasibility probe for monitor-only Orbus startup.
/// F1 shows a desktop status panel and enables a non-invasive mouse ray test.
/// This does not fake XR tracking, send UI events, join servers or move an avatar.
/// </summary>
public sealed class DesktopStartupProbe : MonoBehaviour
{
    public DesktopStartupProbe(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F1 = 0x70;
    private const int LeftMouse = 0x01;
    private const float RayDistance = 300f;

    private bool _panelOpen;
    private bool _f1WasDown;
    private bool _leftWasDown;
    private bool _cursorOwned;
    private bool _cursorWasVisible;
    private CursorLockMode _previousCursorLock;
    private bool _warned;
    private float _elapsed;
    private int _snapshotIndex;
    private float _lastStatusTime;
    private string _lastResult = "Move the cursor over a game object.";
    private string _lastClicked = "Nothing clicked yet.";
    private string _lastCamera = "No camera detected";
    private string _lastScene = "(waiting)";
    private bool _lastXR;
    private int _lastCameraCount;

    private static readonly float[] SnapshotSeconds = { 2f, 8f, 20f, 45f };

    private void Start()
    {
        Plugin.ModLogger.LogInfo("[DesktopProbe] v0.1 initialized.");
        Plugin.ModLogger.LogInfo(
            "[DesktopProbe] F1 = diagnostic overlay; click = physics ray test.");
        Plugin.ModLogger.LogInfo(
            "[DesktopProbe] Read-only: no simulated XR hands, UI clicks, movement or server access.");
        Snapshot("Start");
    }

    private void Update()
    {
        try
        {
            UpdateCore();
        }
        catch (Exception ex)
        {
            if (_warned) return;
            _warned = true;
            Plugin.ModLogger.LogWarning("[DesktopProbe] Update failed: " + ex);
        }
    }

    private void UpdateCore()
    {
        _elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
        while (_snapshotIndex < SnapshotSeconds.Length &&
               _elapsed >= SnapshotSeconds[_snapshotIndex])
        {
            _snapshotIndex++;
            Snapshot("Startup checkpoint");
        }

        bool focus = Application.isFocused;
        bool f1Down = focus && IsHeld(F1);
        if (f1Down && !_f1WasDown)
        {
            _panelOpen = !_panelOpen;
            if (_panelOpen) TakeCursor();
            else RestoreCursor();
            Snapshot(_panelOpen ? "F1 opened" : "F1 closed");
        }
        _f1WasDown = f1Down;

        if (!_panelOpen || !focus)
        {
            _leftWasDown = false;
            if (!focus) RestoreCursor();
            return;
        }

        if (!_cursorOwned) TakeCursor();
        Camera? camera = FindGameCamera();
        if (camera == null)
        {
            _lastResult = "No usable world camera found yet.";
            return;
        }

        // Raycast is observation only. It never invokes UI events or physics.
        // World-space Unity UI may have no physics collider; a miss is not
        // proof that the menu cannot be operated via a virtual controller.
        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, RayDistance))
        {
            string name = hit.collider != null
                ? hit.collider.gameObject.name : "(unknown collider)";
            _lastResult = "Physics ray hit: " + name;
        }
        else _lastResult = "No physics collider under mouse (UI may use GraphicRaycaster).";

        bool leftDown = IsHeld(LeftMouse);
        if (leftDown && !_leftWasDown)
        {
            _lastClicked = _lastResult;
            Plugin.ModLogger.LogInfo("[DesktopProbe] Mouse pressed: " +
                                     _lastClicked);
        }
        _leftWasDown = leftDown;

        if (_elapsed - _lastStatusTime > 0.5f)
        {
            _lastStatusTime = _elapsed;
            RefreshStatus();
        }
    }

    private static bool IsHeld(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    private static Camera? FindGameCamera()
    {
        Camera[] all = Camera.allCameras;
        foreach (Camera camera in all)
            if (camera != null && camera.name == "Camera (eye)" &&
                camera.enabled)
                return camera;

        Camera main = Camera.main;
        if (main != null && main.enabled &&
            main.name != "BetterMirror Creator Camera")
            return main;

        foreach (Camera camera in all)
            if (camera != null && camera.enabled &&
                camera.name != "BetterMirror Creator Camera" &&
                camera.targetTexture == null)
                return camera;
        return null;
    }

    private void RefreshStatus()
    {
        try
        {
            _lastXR = XRSettings.isDeviceActive;
            _lastCameraCount = Camera.allCameras.Length;
            _lastScene = SceneManager.GetActiveScene().name ?? "(unnamed)";
            Camera? camera = FindGameCamera();
            _lastCamera = camera == null ? "No usable game camera" :
                camera.name + " (enabled)";
        }
        catch (Exception ex)
        {
            _lastCamera = "Status error: " + ex.Message;
        }
    }

    private void Snapshot(string reason)
    {
        RefreshStatus();
        Plugin.ModLogger.LogInfo("[DesktopProbe] " + reason +
            " | elapsed=" + _elapsed.ToString("0.0") + "s" +
            " | Scene=" + _lastScene +
            " | XR enabled=" + XRSettings.enabled +
            " | XR active=" + _lastXR +
            " | Cameras=" + _lastCameraCount +
            " | GameCamera=" + _lastCamera +
            " | Focused=" + Application.isFocused);
        try
        {
            int i = 0;
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null) continue;
                Plugin.ModLogger.LogInfo("[DesktopProbe] Camera[" + i +
                    "] name=" + camera.name +
                    " enabled=" + camera.enabled +
                    " stereo=" + camera.stereoEnabled);
                if (++i >= 12) break;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[DesktopProbe] Camera enumeration error: " + ex.Message);
        }
    }

    private void TakeCursor()
    {
        if (_cursorOwned || !Application.isFocused) return;
        _previousCursorLock = Cursor.lockState;
        _cursorWasVisible = Cursor.visible;
        _cursorOwned = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreCursor()
    {
        if (!_cursorOwned) return;
        _cursorOwned = false;
        try
        {
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _cursorWasVisible;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[DesktopProbe] Cursor restore failed: " + ex.Message);
        }
    }

    private void OnGUI()
    {
        if (!_panelOpen) return;
        try
        {
            const float x = 16f;
            const float y = 16f;
            const float w = 565f;
            GUI.Box(new Rect(x, y, w, 204f), "RC DESKTOP STARTUP PROBE v0.1");
            GUI.Label(new Rect(x + 15f, y + 28f, w - 30f, 20f),
                "Scene: " + _lastScene +
                "     XR active: " + _lastXR +
                "     cameras: " + _lastCameraCount);
            GUI.Label(new Rect(x + 15f, y + 51f, w - 30f, 20f),
                "Game camera: " + _lastCamera);
            GUI.Label(new Rect(x + 15f, y + 78f, w - 30f, 42f),
                _lastResult);
            GUI.Label(new Rect(x + 15f, y + 120f, w - 30f, 42f),
                "Last click: " + _lastClicked);
            GUI.Label(new Rect(x + 15f, y + 169f, w - 30f, 20f),
                "F1 hide | Observation only - no menu selection or XR simulation");
        }
        catch (Exception ex)
        {
            _panelOpen = false;
            RestoreCursor();
            Plugin.ModLogger.LogWarning(
                "[DesktopProbe] Overlay failed: " + ex.Message);
        }
    }

    private void OnDisable() => RestoreCursor();
    private void OnDestroy() => RestoreCursor();
}
