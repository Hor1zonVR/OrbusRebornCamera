using System;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Desktop-only creator controls drawn via Unity IMGUI.
/// This is intentionally independent of the optional VR camera prop and
/// intentionally does not enable the protected/experimental drone by default.
/// </summary>
public sealed partial class CameraDiagnostics
{
    // F2 is not used by the existing camera/mirror shortcuts (F3-F12).
    private const int PanelToggleVirtualKey = 0x71;

    private bool _panelVisible;
    private bool _panelToggleWasDown;
    private bool _panelInitialized;
    private bool _panelGuiErrorLogged;
    private bool _panelCursorManaged;
    private bool _panelCursorWasVisible;
    private CursorLockMode _panelPreviousCursorLock;

    private GUIStyle? _panelTitle;
    private GUIStyle? _panelSubtitle;
    private GUIStyle? _panelHeading;
    private GUIStyle? _panelText;
    private GUIStyle? _panelTiny;
    private GUIStyle? _panelValue;
    private GUIStyle? _panelButtonText;
    private GUIStyle? _panelButtonAccent;
    private GUIStyle? _panelBadge;

    private static readonly Color PanelBackground =
        new Color(0.078f, 0.086f, 0.111f, 0.98f);
    private static readonly Color PanelHeader =
        new Color(0.102f, 0.114f, 0.142f, 1f);
    private static readonly Color PanelCard =
        new Color(0.137f, 0.149f, 0.180f, 1f);
    private static readonly Color PanelHover =
        new Color(0.185f, 0.204f, 0.239f, 1f);
    private static readonly Color PanelStroke =
        new Color(0.244f, 0.266f, 0.300f, 1f);
    private static readonly Color PanelMint =
        new Color(0.341f, 0.851f, 0.675f, 1f);
    private static readonly Color PanelMintDark =
        new Color(0.128f, 0.300f, 0.255f, 1f);
    private static readonly Color PanelWhite =
        new Color(0.966f, 0.970f, 0.981f, 1f);
    private static readonly Color PanelMuted =
        new Color(0.65f, 0.68f, 0.73f, 1f);
    private static readonly Color PanelWarning =
        new Color(1f, 0.76f, 0.48f, 1f);

    // This extra Unity callback does not replace CameraDiagnostics.Update,
    // so all existing desktop-camera hotkeys and camera modes keep working.
    private void FixedUpdate() => PollPanelToggle();

    private void PollPanelToggle()
    {
        bool down = Application.isFocused &&
            (GetAsyncKeyState(PanelToggleVirtualKey) & 0x8000) != 0;

        if (down && !_panelToggleWasDown)
        {
            SetPanelVisibility(!_panelVisible);
            Plugin.ModLogger.LogInfo(
                "[CreatorPanel] " + (_panelVisible ? "Opened" : "Hidden") +
                " (F2)");
        }

        _panelToggleWasDown = down;
    }

    private void SetPanelVisibility(bool visible)
    {
        _panelVisible = visible;
        Plugin.CreatorPanelVisible.Value = visible;
        if (visible) AcquirePanelCursor();
        else ReleasePanelCursor();
    }

    private void AcquirePanelCursor()
    {
        if (_panelCursorManaged || !Application.isFocused)
            return;

        _panelPreviousCursorLock = Cursor.lockState;
        _panelCursorWasVisible = Cursor.visible;
        _panelCursorManaged = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReleasePanelCursor()
    {
        if (!_panelCursorManaged)
            return;

        _panelCursorManaged = false;
        Cursor.lockState = _panelPreviousCursorLock;
        Cursor.visible = _panelCursorWasVisible;
    }

    // A paused game can stop FixedUpdate, so OnGUI polls F2 as a fallback.
    private void OnGUI()
    {
        if (!_panelInitialized)
        {
            _panelInitialized = true;
            _panelVisible = Plugin.CreatorPanelVisible.Value;
        }

        PollPanelToggle();

        if (!_panelVisible)
            return;

        if (Application.isFocused)
        {
            AcquirePanelCursor();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        try
        {
            EnsureCreatorPanelStyles();

            // GUI.matrix is temporarily scaled to fit laptop-size game windows.
            // Always restore it so other mods' IMGUI is not affected.
            Matrix4x4 original = GUI.matrix;
            float scale = Mathf.Clamp(
                Mathf.Min(Screen.width / 520f, Screen.height / 700f),
                0.55f, 1f);

            try
            {
                GUI.matrix = Matrix4x4.Scale(
                    new Vector3(scale, scale, 1f));
                DrawCreatorPanel();
            }
            finally
            {
                GUI.matrix = original;
            }
        }
        catch (Exception ex)
        {
            if (!_panelGuiErrorLogged)
            {
                _panelGuiErrorLogged = true;
                Plugin.ModLogger.LogError(
                    "[CreatorPanel] UI rendering failed: " + ex);
            }

            // Never break the working creator camera due to a panel issue.
            _panelVisible = false;
            Plugin.CreatorPanelVisible.Value = false;
            ReleasePanelCursor();
        }
    }

    private void EnsureCreatorPanelStyles()
    {
        if (_panelTitle != null)
            return;

        _panelTitle = PanelStyle(19, PanelWhite, FontStyle.Bold);
        _panelSubtitle = PanelStyle(11, PanelMuted);
        _panelHeading = PanelStyle(12, PanelWhite, FontStyle.Bold);
        _panelText = PanelStyle(12, PanelWhite);
        _panelTiny = PanelStyle(10, PanelMuted);
        _panelValue = PanelStyle(12, PanelMint, FontStyle.Bold);
        _panelBadge = PanelStyle(10, PanelMint, FontStyle.Bold);
        _panelButtonText = PanelStyle(12, PanelWhite, FontStyle.Bold,
            TextAnchor.MiddleCenter);
        _panelButtonAccent = PanelStyle(12, PanelMint, FontStyle.Bold,
            TextAnchor.MiddleCenter);
    }

    private static GUIStyle PanelStyle(int size, Color color,
        FontStyle weight = FontStyle.Normal,
        TextAnchor align = TextAnchor.MiddleLeft)
    {
        // IL2CPP's generated GUIStyle wrapper does not expose the
        // GUIStyle(GUIStyle) copy constructor in this game.
        // Construct a fresh style and configure it explicitly.
        var result = new GUIStyle();
        result.fontSize = size;
        result.fontStyle = weight;
        result.normal.textColor = color;
        result.alignment = align;
        result.wordWrap = false;
        return result;
    }

    private static void FillPanelRect(Rect rect, Color color)
    {
        Color original = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = original;
    }

    private void DrawCreatorPanel()
    {
        const float x = 20f;
        const float y = 25f;
        const float w = 424f;
        const float height = 650f;

        FillPanelRect(new Rect(x - 1, y - 1, w + 2, height + 2),
            PanelStroke);
        FillPanelRect(new Rect(x, y, w, height), PanelBackground);
        FillPanelRect(new Rect(x, y, w, 67), PanelHeader);
        FillPanelRect(new Rect(x, y, 4, 67), PanelMint);

        GUI.Label(new Rect(x + 19, y + 11, 320, 29),
            "REBORNCAM", _panelTitle);
        GUI.Label(new Rect(x + 20, y + 39, 305, 18),
            "Desktop creator controls   |   F2 to hide",
            _panelSubtitle);

        if (PanelButton(new Rect(x + w - 45, y + 15, 29, 30),
            "X", false))
        {
            HideCreatorPanel();
        }

        float px = x + 18;
        float pw = w - 36;
        float pos = y + 81;

        // A status card that does not claim recording itself is active.
        FillPanelRect(new Rect(px, pos, pw, 49), PanelCard);
        FillPanelRect(new Rect(px, pos, 3, 49),
            IsCameraActive ? PanelMint : PanelWarning);
        GUI.Label(new Rect(px + 13, pos + 6, pw - 125, 18),
            IsCameraActive ? "CAMERA ACTIVE" : "CAMERA OFF / WAITING",
            _panelBadge);
        GUI.Label(new Rect(px + 13, pos + 26, pw - 125, 14),
            IsCameraActive ? "Desktop output enabled" :
            "Load OrbusVR, then enable camera",
            _panelTiny);
        if (PanelButton(new Rect(px + pw - 100, pos + 9, 88, 31),
            IsCameraActive ? "Disable" : "Enable", false))
        {
            ToggleCamera();
        }

        pos += 63;
        GUI.Label(new Rect(px, pos, pw, 19), "CAMERA MODE",
            _panelHeading);
        pos += 27;

        float half = (pw - 9) / 2;
        if (PanelButton(new Rect(px, pos, half, 36),
            "First person", _mode == CameraMode.POV))
            SetPanelMode(CameraMode.POV);
        if (PanelButton(new Rect(px + half + 9, pos, half, 36),
            "Third person", _mode == CameraMode.ThirdPerson))
            SetPanelMode(CameraMode.ThirdPerson);

        pos += 45;
        if (PanelButton(new Rect(px, pos, half, 36),
            "Selfie", _mode == CameraMode.Selfie))
            SetPanelMode(CameraMode.Selfie);
        if (PanelButton(new Rect(px + half + 9, pos, half, 36),
            "Static / detached", _mode == CameraMode.Static))
            SelectPanelStaticMode();

        pos += 56;
        float fov = DrawPanelSlider(px, pos, pw,
            "Horizontal field of view",
            Plugin.HorizontalFov.Value, 50f, 150f,
            Plugin.HorizontalFov.Value.ToString("0") + "°");
        fov = Mathf.Round(fov);
        if (Mathf.Abs(fov - Plugin.HorizontalFov.Value) > .01f)
        {
            Plugin.HorizontalFov.Value = fov;
            ApplyFov();
        }

        pos += 73;
        float smooth = DrawPanelSlider(px, pos, pw,
            "Follow smoothness",
            Plugin.Smoothing.Value, 0f, 20f,
            Plugin.Smoothing.Value.ToString("0.0"));
        smooth = Mathf.Round(smooth * 2f) / 2f;
        if (Mathf.Abs(smooth - Plugin.Smoothing.Value) > .1f)
            Plugin.Smoothing.Value = smooth;

        pos += 73;
        bool thirdMode = _mode == CameraMode.ThirdPerson ||
            _mode == CameraMode.Static &&
            _lastFollowMode == CameraMode.ThirdPerson;

        bool selfieMode = _mode == CameraMode.Selfie ||
            _mode == CameraMode.Static &&
            _lastFollowMode == CameraMode.Selfie;

        float distance = thirdMode
            ? Plugin.ThirdDistance.Value
            : Plugin.SelfieDistance.Value;
        float newDistance = DrawPanelSlider(px, pos, pw,
            thirdMode ? "Third-person distance" :
            selfieMode ? "Selfie distance" : "External camera distance",
            distance, 0.5f, 8f, distance.ToString("0.0") + " m");
        newDistance = Mathf.Round(newDistance * 10f) / 10f;

        if (thirdMode && Mathf.Abs(newDistance - distance) > .05f)
            Plugin.ThirdDistance.Value = newDistance;
        else if (selfieMode && Mathf.Abs(newDistance - distance) > .05f)
            Plugin.SelfieDistance.Value = newDistance;

        // Distance is intentionally disabled in POV/static-freeze/drone mode.
        // Do not change the settings accidentally when the camera is fixed.
        if (!thirdMode && !selfieMode)
        {
            FillPanelRect(new Rect(px, pos + 21, pw, 29),
                new Color(.09f, .10f, .12f, .47f));
            GUI.Label(new Rect(px + 11, pos + 26, pw - 20, 20),
                "Switch to Third Person or Selfie to adjust",
                _panelTiny);
        }

        pos += 68;

        FillPanelRect(new Rect(px, pos, pw, 1), PanelStroke);
        pos += 13;

        bool show = Plugin.ShowLocalAvatar.Value;
        GUI.Label(new Rect(px + 1, pos, pw - 120, 25),
            "Show local avatar (external views)",
            _panelText);
        if (PanelButton(new Rect(px + pw - 83, pos - 3, 83, 31),
            show ? "ON" : "OFF", show))
            ToggleLocalAvatar();

        pos += 44;
        if (PanelButton(new Rect(px, pos, pw, 37),
            _mode == CameraMode.Static ? "Reattach camera" :
            "Detach / freeze current shot",
            _mode == CameraMode.Static))
            ToggleStaticMode();

        pos += 48;
        GUI.Label(new Rect(px, pos, pw, 19),
            "Drone remains locked by default until coin filtering is safe.",
            _panelTiny);
        GUI.Label(new Rect(px, pos + 16, pw, 18),
            "F9 toggle   ·   F10 FOV   ·   F11 mode   ·   F12 freeze",
            _panelTiny);
    }

    private float DrawPanelSlider(float x, float y, float width,
        string title, float value, float min, float max, string display)
    {
        GUI.Label(new Rect(x, y, width - 90, 21), title, _panelText);
        GUI.Label(new Rect(x + width - 92, y, 92, 21),
            display, _panelValue);

        // Unity's standard draggable slider keeps interaction robust even
        // with the generated IL2CPP IMGUI interop wrappers.
        return GUI.HorizontalSlider(
            new Rect(x + 3, y + 29, width - 6, 19),
            value, min, max);
    }

    private bool PanelButton(Rect rect, string text, bool selected)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);
        FillPanelRect(rect, selected ? PanelMintDark :
            hovered ? PanelHover : PanelCard);

        if (selected)
            FillPanelRect(new Rect(rect.x, rect.y,
                3, rect.height), PanelMint);

        bool clicked = GUI.Button(rect, "", GUIStyle.none);

        GUI.Label(rect, text, selected ?
            _panelButtonAccent : _panelButtonText);

        return clicked;
    }

    private void HideCreatorPanel()
    {
        SetPanelVisibility(false);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) ReleasePanelCursor();
    }

    private void SetPanelMode(CameraMode next)
    {
        _autoStartHandled = true;

        if (next == CameraMode.Drone || next == CameraMode.Static)
            return;

        if (_mode == CameraMode.Drone)
            LeaveDrone();

        _mode = next;
        _lastFollowMode = next;
        _snapNextFrame = true;
        Plugin.StartMode.Value = next.ToString();

        if (!IsCameraActive)
            TryEnableCamera(true);
        else
            UpdateDesktopCullingMask(true);

        Plugin.ModLogger.LogInfo(
            "[CreatorPanel] Camera mode: " + next);
    }

    private void SelectPanelStaticMode()
    {
        if (_mode != CameraMode.Static)
            ToggleStaticMode();
    }
}
