using System;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Desktop creator studio with a measured layout: the content region
/// always ends before the pinned footer, including in small windows.
/// RMB outside the editor still flies freecam.
/// The Quest display and actual player are never touched.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private const int PanelToggleVirtualKey = 0x71; // F2
    // Design-space pixels; the entire panel scales uniformly in small
    // windows. All sections below are measured from these shared anchors.
    private const float StudioWidth = 472f;
    private const float StudioHeight = 694f;
    private const float StudioPadding = 22f;
    private const float StudioHeaderHeight = 76f;
    private const float StudioStatusY = 90f;
    private const float StudioTabsY = 141f;
    private const float StudioDividerY = 193f;
    private const float StudioContentY = 204f;
    private const float StudioFooterY = 632f;

    // Tallest editor tab ends at ContentY + 406 = 610, leaving a clear
    // 22px gap before the footer at 632. This fixes 0.9.9's overlap.
    private const float StudioContentBottom =
        StudioContentY + 406f;

    private bool _panelVisible;
    private bool _panelToggleWasDown;
    private bool _panelInitialized;
    private bool _panelGuiErrorLogged;
    private bool _panelCursorManaged;
    private bool _panelCursorWasVisible;
    private CursorLockMode _panelPreviousCursorLock;
    private bool _confirmClearKeyframes;
    private bool _draggingPanel;
    private Vector2 _dragOffset;
    private bool _hasPanelPosition;
    private float _panelX, _panelY;
    private int _panelTab; // 0 Camera, 1 Freecam, 2 Keyframes
    private int _activeSlider;
    private float _panelScale = 1f;

    private GUIStyle? _studioTitle;
    private GUIStyle? _studioCaption;
    private GUIStyle? _studioHeading;
    private GUIStyle? _studioBody;
    private GUIStyle? _studioMuted;
    private GUIStyle? _studioValue;
    private GUIStyle? _studioButton;
    private GUIStyle? _studioActiveButton;
    private GUIStyle? _studioTabActive;
    private GUIStyle? _studioTabInactive;

    // Restrained Orbus-inspired palette: ink/stone, parchment, jade and
    // aged brass. Jade is reserved for a meaningful enabled/action state.
    private static readonly Color StudioBack =
        new Color(.074f, .077f, .084f, .985f);
    private static readonly Color StudioTop =
        new Color(.109f, .116f, .123f, 1f);
    private static readonly Color StudioStone =
        new Color(.094f, .101f, .105f, 1f);
    private static readonly Color StudioShadow =
        new Color(.018f, .025f, .029f, .93f);
    private static readonly Color StudioSurface =
        new Color(.139f, .149f, .151f, 1f);
    private static readonly Color StudioSurfaceHover =
        new Color(.199f, .211f, .212f, 1f);
    private static readonly Color StudioSelected =
        new Color(.168f, .207f, .196f, 1f);
    private static readonly Color StudioTabSurface =
        new Color(.161f, .176f, .170f, 1f);
    private static readonly Color StudioTrack =
        new Color(.091f, .109f, .111f, 1f);
    private static readonly Color StudioStroke =
        new Color(.259f, .257f, .222f, 1f);
    private static readonly Color StudioBrassDim =
        new Color(.377f, .333f, .230f, 1f);
    private static readonly Color StudioGold =
        new Color(.870f, .702f, .418f, 1f);
    private static readonly Color StudioJade =
        new Color(.395f, .790f, .697f, 1f);
    private static readonly Color StudioAccent = StudioJade;
    private static readonly Color StudioBright =
        new Color(.949f, .932f, .874f, 1f);
    private static readonly Color StudioSecondary =
        new Color(.696f, .719f, .704f, 1f);
    private static readonly Color StudioAmber = StudioGold;

    // A paused game might skip FixedUpdate, so F2 is also polled in OnGUI.
    private void FixedUpdate() => PollPanelToggle();

    private void PollPanelToggle()
    {
        bool down = Application.isFocused &&
            (GetAsyncKeyState(PanelToggleVirtualKey) & 0x8000) != 0;
        if (down && !_panelToggleWasDown)
        {
            SetPanelVisibility(!_panelVisible);
            Plugin.ModLogger.LogInfo("[CreatorStudio] " +
                (_panelVisible ? "Open" : "Closed") + " (F2)");
        }
        _panelToggleWasDown = down;
    }

    private void SetPanelVisibility(bool visible)
    {
        _panelVisible = visible;
        Plugin.CreatorPanelVisible.Value = visible;
        _draggingPanel = false;
        _activeSlider = 0;
        if (visible)
        {
            ReleaseDronePointer();
            AcquirePanelCursor();
        }
        else ReleasePanelCursor();
    }

    private void AcquirePanelCursor()
    {
        if (_panelCursorManaged || !Application.isFocused ||
            _droneLooking)
            return;
        _panelPreviousCursorLock = Cursor.lockState;
        _panelCursorWasVisible = Cursor.visible;
        _panelCursorManaged = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReleasePanelCursor()
    {
        if (!_panelCursorManaged) return;
        _panelCursorManaged = false;
        if (_droneLooking) return;
        Cursor.lockState = _panelPreviousCursorLock;
        Cursor.visible = _panelCursorWasVisible;
    }

    private void StudioGeometry(out float scale, out float x, out float y)
    {
        // Fit both dimensions; never force a minimum scale that makes the
        // panel taller than the actual game window.
        scale = Mathf.Min(1f, Mathf.Min(
            Screen.width / (StudioWidth + 24f),
            Screen.height / (StudioHeight + 24f)));
        scale = Mathf.Max(.20f, scale);
        float maxX = Mathf.Max(12f, Screen.width / scale -
            StudioWidth - 12f);
        float maxY = Mathf.Max(12f, Screen.height / scale -
            StudioHeight - 12f);
        if (!_hasPanelPosition)
        {
            _panelX = maxX;
            _panelY = 16f;
            _hasPanelPosition = true;
        }
        _panelX = Mathf.Clamp(_panelX, 10f, maxX);
        _panelY = Mathf.Clamp(_panelY, 10f, maxY);
        x = _panelX;
        y = _panelY;
    }

    private Vector2 StudioMouse(float scale)
    {
        Vector3 pointer = Input.mousePosition;
        return new Vector2(pointer.x / scale,
            (Screen.height - pointer.y) / scale);
    }

    // Called by Drone.Update to decide whether RMB starts a flight or a
    // panel interaction. Once flight starts, the locked cursor can pass
    // across the panel without interrupting flight.
    private bool IsPointerOverCreatorPanel()
    {
        if (!_panelVisible) return false;
        StudioGeometry(out float scale, out float x, out float y);
        return new Rect(x, y, StudioWidth, StudioHeight)
            .Contains(StudioMouse(scale));
    }

    private void HandlePanelDragging(float scale, float x, float y)
    {
        Event evt = Event.current;
        Vector2 mouse = StudioMouse(scale);
        if (evt.type == EventType.MouseDown && evt.button == 0 &&
            new Rect(x, y, StudioWidth - 48f, 62f).Contains(mouse))
        {
            _dragOffset = mouse - new Vector2(x, y);
            _draggingPanel = true;
            evt.Use();
        }
        else if (_draggingPanel && evt.type == EventType.MouseDrag &&
            evt.button == 0)
        {
            _panelX = mouse.x - _dragOffset.x;
            _panelY = mouse.y - _dragOffset.y;
            evt.Use();
        }
        else if (_draggingPanel && evt.type == EventType.MouseUp &&
            evt.button == 0)
        {
            _draggingPanel = false;
            evt.Use();
        }
    }

    private void OnGUI()
    {
        if (!_panelInitialized)
        {
            _panelInitialized = true;
            _panelVisible = Plugin.CreatorPanelVisible.Value;
        }
        PollPanelToggle();
        if (!_panelVisible) return;

        // Don't fight cursor locking while holding RMB to fly outside the
        // panel. Otherwise keep the pointer free for GUI buttons/sliders.
        if (Application.isFocused && !_droneLooking)
            AcquirePanelCursor();

        try
        {
            EnsureStudioStyles();
            ProbeOrbusUiFont();
            StudioGeometry(out float scale, out float x, out float y);
            _panelScale = scale;
            HandlePanelDragging(scale, x, y);

            Matrix4x4 original = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.Scale(
                    new Vector3(scale, scale, 1f));
                DrawStudio(x, y);
            }
            finally { GUI.matrix = original; }
        }
        catch (Exception ex)
        {
            if (!_panelGuiErrorLogged)
            {
                _panelGuiErrorLogged = true;
                Plugin.ModLogger.LogError("[CreatorStudio] Panel error: " + ex);
            }
            SetPanelVisibility(false);
        }
    }

    private static GUIStyle StudioStyle(int size, Color color,
        FontStyle font = FontStyle.Normal,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var style = new GUIStyle();
        style.fontSize = size;
        style.fontStyle = font;
        style.normal.textColor = color;
        style.alignment = alignment;
        style.wordWrap = false;
        return style;
    }

    private void EnsureStudioStyles()
    {
        if (_studioTitle != null) return;
        _studioTitle = StudioStyle(22, StudioBright, FontStyle.Bold);
        _studioCaption = StudioStyle(12, StudioSecondary);
        _studioHeading = StudioStyle(14, StudioBright, FontStyle.Bold);
        _studioBody = StudioStyle(13, StudioBright);
        _studioMuted = StudioStyle(12, StudioSecondary);
        _studioValue = StudioStyle(13, StudioGold, FontStyle.Bold,
            TextAnchor.MiddleRight);
        _studioButton = StudioStyle(13, StudioBright, FontStyle.Bold,
            TextAnchor.MiddleCenter);
        _studioActiveButton = StudioStyle(13, StudioJade,
            FontStyle.Bold, TextAnchor.MiddleCenter);
        _studioTabActive = StudioStyle(14, StudioBright,
            FontStyle.Bold, TextAnchor.MiddleCenter);
        _studioTabInactive = StudioStyle(14, StudioSecondary,
            FontStyle.Bold, TextAnchor.MiddleCenter);
    }

    private static void StudioFill(Rect r, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void StudioText(Rect rect, string text, GUIStyle? style)
    {
        if (style != null) GUI.Label(rect, text, style);
    }

    private bool StudioButton(Rect rect, string name, bool selected,
        bool accent = false)
    {
        bool hover = rect.Contains(StudioMouse(_panelScale));
        StudioFill(rect, selected ? StudioSelected :
            hover ? StudioSurfaceHover : StudioSurface);
        StudioBorder(rect,
            selected ? StudioGold : accent ? StudioBrassDim : StudioStroke);
        StudioFill(new Rect(rect.x + 2f, rect.y + 2f,
            rect.width - 4f, 1f),
            selected || hover ? StudioBrassDim : StudioStroke);

        if (selected)
            StudioFill(new Rect(rect.x + 8f,
                rect.yMax - 3f, rect.width - 16f, 2f), StudioGold);
        else if (accent)
            StudioFill(new Rect(rect.x + 1f,
                rect.y + 1f, 3f, rect.height - 2f), StudioJade);

        bool clicked = GUI.Button(rect, "", GUIStyle.none);
        StudioText(rect, name, selected ?
            _studioActiveButton : _studioButton);
        return clicked;
    }

    private float StudioSlider(int id, float x, float y, float w,
        string title, float value, float min, float max, string display)
    {
        StudioText(new Rect(x, y, w - 110, 24f), title, _studioBody);
        StudioText(new Rect(x + w - 112f, y, 112f, 24f),
            display, _studioValue);
        float tx = x + 4f, ty = y + 38f, tw = w - 8f;
        // Carved stone track with a low-key jade inlay and brass handle.
        StudioFill(new Rect(tx, ty - 2f, tw, 8f), StudioStroke);
        StudioFill(new Rect(tx + 1f, ty - 1f, tw - 2f, 6f),
            StudioTrack);
        float t = Mathf.Clamp01((value - min) / (max - min));
        StudioFill(new Rect(tx + 2f, ty, (tw - 4f) * t, 4f),
            StudioJade);
        float handle = tx + 2f + (tw - 4f) * t;
        StudioFill(new Rect(handle - 5f, ty - 7f, 10f, 17f),
            StudioBrassDim);
        StudioFill(new Rect(handle - 3f, ty - 5f, 6f, 13f),
            StudioGold);

        // Custom slider keeps styling coherent and avoids the old Unity
        // skin's grey track/handle. Event is consumed only over the track.
        Event evt = Event.current;
        Vector2 mouse = StudioMouse(_panelScale);
        Rect hit = new Rect(tx - 9f, ty - 12f, tw + 18f, 29f);
        if (evt.type == EventType.MouseDown && evt.button == 0 &&
            hit.Contains(mouse))
        {
            _activeSlider = id;
            evt.Use();
        }
        if (_activeSlider == id &&
            (evt.type == EventType.MouseDown ||
             evt.type == EventType.MouseDrag))
        {
            value = Mathf.Lerp(min, max,
                Mathf.Clamp01((mouse.x - tx) / tw));
            if (evt.type == EventType.MouseDrag) evt.Use();
        }
        if (_activeSlider == id && evt.type == EventType.MouseUp &&
            evt.button == 0)
        {
            _activeSlider = 0;
            evt.Use();
        }
        return value;
    }

    private void DrawStudio(float x, float y)
    {
        float px = x + StudioPadding;
        float pw = StudioWidth - StudioPadding * 2f;

        StudioFrame(x, y);
        StudioFill(new Rect(x + 4f, y + 4f,
            StudioWidth - 8f, StudioHeaderHeight - 4f), StudioTop);
        StudioFill(new Rect(x + 17f,
            y + StudioHeaderHeight - 2f, StudioWidth - 34f, 1f),
            StudioBrassDim);

        StudioSeal(px + 1f, y + 21f);
        StudioText(new Rect(px + 46f, y + 10f,
                pw - 111f, 33f),
            "REBORN CAM", _studioTitle);
        StudioText(new Rect(px + 48f, y + 43f,
                pw - 113f, 22f),
            "CREATOR STUDIO  /  DRAG TITLE TO MOVE",
            _studioCaption);
        if (StudioButton(new Rect(
                x + StudioWidth - 44f, y + 21f, 27f, 28f),
            "X", false))
        {
            SetPanelVisibility(false);
            return;
        }

        // The carved status row uses jade only for genuine active output.
        StudioFill(new Rect(px, y + StudioStatusY,
            pw, 37f), StudioSurface);
        StudioBorder(new Rect(px, y + StudioStatusY,
            pw, 37f), StudioStroke);
        StudioFill(new Rect(px + 11f, y + StudioStatusY + 13f,
            9f, 9f), IsCameraActive ? StudioJade : StudioAmber);
        StudioText(new Rect(px + 29f, y + StudioStatusY + 6f,
                186f, 25f),
            IsCameraActive ? "CAMERA ONLINE" : "CAMERA OFF",
            IsCameraActive ? _studioHeading : _studioMuted);
        StudioText(new Rect(px + pw - 191f,
                y + StudioStatusY + 6f, 177f, 25f),
            _mode == CameraMode.Drone ? "FREECAM ACTIVE" :
                _mode == CameraMode.POV ? "FIRST PERSON" :
                _mode == CameraMode.ThirdPerson ? "THIRD PERSON" :
                _mode == CameraMode.Selfie ? "SELFIE" : "STATIC",
            _studioValue);

        float tabW = (pw - 16f) / 3f;
        string[] labels = { "CAMERA", "FREECAM", "KEYFRAMES" };
        for (int i = 0; i < 3; i++)
        {
            float tx = px + i * (tabW + 8f);
            if (StudioTab(new Rect(tx, y + StudioTabsY,
                    tabW, 40f), labels[i], _panelTab == i))
            {
                _panelTab = i;
                _activeSlider = 0;
                _confirmClearKeyframes = false;
            }
        }

        StudioDivider(px, y + StudioDividerY, pw);
        if (_panelTab == 0)
            DrawCameraTab(px, y + StudioContentY, pw);
        else if (_panelTab == 1)
            DrawFlightTab(px, y + StudioContentY, pw);
        else
            DrawKeyframesTab(px, y + StudioContentY, pw);

        StudioDivider(px, y + StudioFooterY, pw);
        StudioText(new Rect(px + 2f, y + StudioFooterY + 13f,
                pw - 4f, 20f),
            "RMB outside panel + WASD to fly   /   F2 to hide",
            _studioMuted);
        StudioText(new Rect(px + 2f, y + StudioFooterY + 37f,
                pw - 4f, 20f),
            "Desktop camera only  /  headset & avatar unchanged",
            _studioMuted);
    }

    private void DrawCameraTab(float x, float y, float w)
    {
        StudioSection(x, y, w, "CAMERA PERSPECTIVE");
        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 31f, half, 37f),
            "First person", _mode == CameraMode.POV))
            SetPanelMode(CameraMode.POV);
        if (StudioButton(new Rect(x + half + 9f, y + 31f, half, 37f),
            "Third person", _mode == CameraMode.ThirdPerson))
            SetPanelMode(CameraMode.ThirdPerson);
        if (StudioButton(new Rect(x, y + 77f, half, 37f),
            "Selfie", _mode == CameraMode.Selfie))
            SetPanelMode(CameraMode.Selfie);
        if (StudioButton(new Rect(x + half + 9f, y + 77f, half, 37f),
            "Static / detached", _mode == CameraMode.Static))
            SelectPanelStaticMode();

        float fov = StudioSlider(1, x, y + 123f, w,
            "Horizontal FOV", Plugin.HorizontalFov.Value,
            50f, 150f,
            Plugin.HorizontalFov.Value.ToString("0") + "°");
        fov = Mathf.Round(fov);
        if (Mathf.Abs(fov - Plugin.HorizontalFov.Value) > .01f)
        {
            Plugin.HorizontalFov.Value = fov;
            ApplyFov();
        }

        bool third = _mode == CameraMode.ThirdPerson ||
            _mode == CameraMode.Static &&
            _lastFollowMode == CameraMode.ThirdPerson;
        bool selfie = _mode == CameraMode.Selfie ||
            _mode == CameraMode.Static &&
            _lastFollowMode == CameraMode.Selfie;

        float smooth = StudioSlider(2, x, y + 186f, w,
            "Follow smoothing", Plugin.Smoothing.Value, 0f, 20f,
            Plugin.Smoothing.Value.ToString("0.0"));
        smooth = Mathf.Round(smooth * 2f) / 2f;
        if (Mathf.Abs(smooth - Plugin.Smoothing.Value) > .1f)
            Plugin.Smoothing.Value = smooth;

        if (third || selfie)
        {
            float distance = third ? Plugin.ThirdDistance.Value :
                Plugin.SelfieDistance.Value;
            float changed = StudioSlider(3, x, y + 248f, w,
                third ? "Third-person distance" : "Selfie distance",
                distance, .5f, 8f, distance.ToString("0.0") + "m");
            changed = Mathf.Round(changed * 10f) / 10f;
            if (Mathf.Abs(changed - distance) > .05f)
            {
                if (third) Plugin.ThirdDistance.Value = changed;
                else Plugin.SelfieDistance.Value = changed;
            }
        }
        else
        {
            StudioFill(new Rect(x, y + 251f, w, 39f), StudioSurface);
            StudioText(new Rect(x + 12f, y + 258f, w - 24f, 22f),
                "Distance is available in Third Person / Selfie",
                _studioMuted);
        }

        bool show = Plugin.ShowLocalAvatar.Value;
        StudioText(new Rect(x, y + 315f, w - 118f, 30f),
            "Show my avatar in external views", _studioBody);
        if (StudioButton(new Rect(x + w - 84f, y + 313f, 84f, 34f),
            show ? "ON" : "OFF", show))
            ToggleLocalAvatar();

        if (StudioButton(new Rect(x, y + 365f, w, 39f),
            IsCameraActive ? "Disable creator camera" :
                "Enable creator camera", IsCameraActive))
            ToggleCamera();
    }

    private void DrawFlightTab(float x, float y, float w)
    {
        if (StudioButton(new Rect(x, y + 2f, w, 45f),
            _mode == CameraMode.Drone ?
            "EXIT FREECAM    Ctrl+F11" :
            "ENTER FREECAM    Ctrl+F11",
            _mode == CameraMode.Drone, true))
        {
            _confirmClearKeyframes = false;
            ToggleFreecam();
        }

        float speed = StudioSlider(4, x, y + 62f, w,
            "Flight speed", Plugin.DroneMoveSpeed.Value,
            .25f, 20f,
            Plugin.DroneMoveSpeed.Value.ToString("0.0") + " m/s");
        speed = Mathf.Round(speed * 4f) / 4f;
        if (Mathf.Abs(speed - Plugin.DroneMoveSpeed.Value) > .05f)
            Plugin.DroneMoveSpeed.Value = speed;

        float mouse = StudioSlider(5, x, y + 124f, w,
            "Mouse sensitivity", Plugin.DroneMouseSensitivity.Value,
            .1f, 8f, Plugin.DroneMouseSensitivity.Value.ToString("0.0"));
        mouse = Mathf.Round(mouse * 10f) / 10f;
        if (Mathf.Abs(mouse - Plugin.DroneMouseSensitivity.Value) > .04f)
            Plugin.DroneMouseSensitivity.Value = mouse;

        float rollSpeed = StudioSlider(6, x, y + 186f, w,
            "Roll speed  [Z / C]", Plugin.DroneRollSpeed.Value,
            5f, 180f, Plugin.DroneRollSpeed.Value.ToString("0") + " °/s");
        rollSpeed = Mathf.Round(rollSpeed);
        if (Mathf.Abs(rollSpeed - Plugin.DroneRollSpeed.Value) > .5f)
            Plugin.DroneRollSpeed.Value = rollSpeed;

        StudioText(new Rect(x, y + 251f, w - 100f, 32f),
            "Camera bank", _studioBody);
        StudioText(new Rect(x + w - 96f, y + 251f, 95f, 30f),
            _droneRoll.ToString("+0;-0;0") + "°", _studioValue);

        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 288f, half, 36f),
            "Level roll  [X]", false))
            _droneRoll = 0f;
        if (StudioButton(new Rect(x + half + 9f, y + 288f, half, 36f),
            "Recenter camera", false))
        {
            if (_mode == CameraMode.Drone)
                RecenterDroneToPlayer("Creator Studio recenter");
        }
        if (StudioButton(new Rect(x, y + 342f, w, 37f),
            "Extended scenery rendering: " +
                (Plugin.ExtendedRenderEnabled.Value ? "ON" : "OFF"),
            Plugin.ExtendedRenderEnabled.Value))
        {
            if (_mode == CameraMode.Drone)
                ToggleCinematicRenderBoost();
            else Plugin.ExtendedRenderEnabled.Value =
                !Plugin.ExtendedRenderEnabled.Value;
        }
        StudioText(new Rect(x + 2f, y + 387f, w - 4f, 19f),
            "RMB+WASD move   Q/E height   Z/C roll   X level",
            _studioMuted);
    }

    private void DrawKeyframesTab(float x, float y, float w)
    {
        StudioFill(new Rect(x, y, w, 59f), StudioSurface);
        StudioBorder(new Rect(x, y, w, 59f), StudioStroke);
        StudioFill(new Rect(x, y, 3f, 59f), StudioBrassDim);
        StudioText(new Rect(x + 14f, y + 5f, w - 28f, 25f),
            _keyframes.Count + " SAVED CAMERA SHOTS",
            _studioHeading);
        StudioText(new Rect(x + 14f, y + 30f,
                w - 28f, 21f),
            _keyframes.Count > 1 ?
                KeyframePathLength().ToString("0.0") + "m path  /  " +
                (_playingKeyframes ? "PLAYING" : "READY TO PLAY") :
                "Capture at least two viewpoints to create a replay",
            _studioMuted);

        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 68f, half, 36f),
            "Add camera shot", false, true))
        {
            _confirmClearKeyframes = false;
            AddKeyframe();
        }
        if (StudioButton(new Rect(x + half + 9f, y + 68f,
            half, 36f), "Undo last", false))
        {
            _confirmClearKeyframes = false;
            RemoveLastKeyframe();
        }
        if (StudioButton(new Rect(x, y + 113f, half, 36f),
            _playingKeyframes ? "Stop playback" : "Play camera path",
            _playingKeyframes))
        {
            _confirmClearKeyframes = false;
            if (_playingKeyframes) StopKeyframePlayback();
            else StartKeyframePlayback();
        }
        if (StudioButton(new Rect(x + half + 9f, y + 113f,
            half, 36f),
            _confirmClearKeyframes ? "Confirm clear?" : "Clear path",
            _confirmClearKeyframes))
        {
            if (_confirmClearKeyframes)
            {
                ClearKeyframes();
                _confirmClearKeyframes = false;
            }
            else _confirmClearKeyframes = true;
        }

        StudioSequence(x, y + 164f, w);

        float duration = StudioSlider(7, x, y + 233f, w,
            "Playback duration", Plugin.KeyframeDuration.Value,
            2f, 120f,
            Plugin.KeyframeDuration.Value.ToString("0") + " sec");
        duration = Mathf.Round(duration);
        if (Mathf.Abs(duration - Plugin.KeyframeDuration.Value) > .5f)
            Plugin.KeyframeDuration.Value = duration;

        if (StudioButton(new Rect(x, y + 294f, w, 41f),
            "Show world-space camera guides: " +
                (Plugin.ShowKeyframePath.Value ? "ON" : "OFF"),
            Plugin.ShowKeyframePath.Value))
        {
            Plugin.ShowKeyframePath.Value =
                !Plugin.ShowKeyframePath.Value;
        }

        StudioFill(new Rect(x, y + 350f, w, 51f), StudioSurface);
        StudioBorder(new Rect(x, y + 350f, w, 51f), StudioStroke);
        StudioText(new Rect(x + 13f, y + 354f,
                w - 26f, 20f),
            "Brass markers & jade trail appear while editing.",
            _studioMuted);
        StudioText(new Rect(x + 13f, y + 377f,
                w - 26f, 20f),
            "They disappear during playback. Roll is preserved.",
            _studioMuted);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            _draggingPanel = false;
            _activeSlider = 0;
            ReleasePanelCursor();
        }
    }

    private void SetPanelMode(CameraMode next)
    {
        if (next == CameraMode.Drone || next == CameraMode.Static) return;
        _autoStartHandled = true;
        if (_mode == CameraMode.Drone) LeaveDrone();
        _mode = next;
        _lastFollowMode = next;
        _snapNextFrame = true;
        Plugin.StartMode.Value = next.ToString();
        if (!IsCameraActive) TryEnableCamera(true);
        else UpdateDesktopCullingMask(true);
        Plugin.ModLogger.LogInfo("[CreatorStudio] Camera mode: " + next);
    }

    private void SelectPanelStaticMode()
    {
        if (_mode != CameraMode.Static) ToggleStaticMode();
    }
}
