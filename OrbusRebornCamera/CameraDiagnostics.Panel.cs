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

    private static readonly Color StudioBack =
        new Color(.065f, .078f, .108f, .98f);
    private static readonly Color StudioTop =
        new Color(.09f, .111f, .149f, 1f);
    private static readonly Color StudioSurface =
        new Color(.112f, .135f, .174f, 1f);
    private static readonly Color StudioSurfaceHover =
        new Color(.15f, .181f, .224f, 1f);
    private static readonly Color StudioSelected =
        new Color(.115f, .298f, .265f, 1f);
    private static readonly Color StudioTrack =
        new Color(.065f, .083f, .114f, 1f);
    private static readonly Color StudioStroke =
        new Color(.205f, .25f, .302f, 1f);
    private static readonly Color StudioAccent =
        new Color(.32f, .89f, .75f, 1f);
    private static readonly Color StudioBright =
        new Color(.96f, .97f, .99f, 1f);
    private static readonly Color StudioSecondary =
        new Color(.68f, .73f, .79f, 1f);
    private static readonly Color StudioAmber =
        new Color(1f, .74f, .42f, 1f);

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
        _studioValue = StudioStyle(13, StudioAccent, FontStyle.Bold,
            TextAnchor.MiddleRight);
        _studioButton = StudioStyle(13, StudioBright, FontStyle.Bold,
            TextAnchor.MiddleCenter);
        _studioActiveButton = StudioStyle(13, StudioAccent,
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
        if (selected || accent)
            StudioFill(new Rect(rect.x, rect.y,
                selected ? 3f : 2f, rect.height), StudioAccent);
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
        StudioFill(new Rect(tx, ty, tw, 5f), StudioTrack);
        float t = Mathf.Clamp01((value - min) / (max - min));
        StudioFill(new Rect(tx, ty, tw * t, 5f), StudioAccent);
        StudioFill(new Rect(tx + tw * t - 5f, ty - 5f, 10f, 15f),
            StudioBright);

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

        // The header, status, tabs, editor canvas and footer occupy
        // non-overlapping vertical bands. Content never draws below +610.
        StudioFill(new Rect(x - 1f, y - 1f,
            StudioWidth + 2f, StudioHeight + 2f), StudioStroke);
        StudioFill(new Rect(x, y, StudioWidth, StudioHeight), StudioBack);
        StudioFill(new Rect(x, y, StudioWidth, StudioHeaderHeight),
            StudioTop);
        StudioFill(new Rect(x, y, 3f, StudioHeaderHeight),
            StudioAccent);

        StudioText(new Rect(px, y + 11f, pw - 60f, 30f),
            "REBORNCAM", _studioTitle);
        StudioText(new Rect(px, y + 43f, pw - 60f, 20f),
            "Creator Studio   •   Drag header to move",
            _studioCaption);

        if (StudioButton(new Rect(
                x + StudioWidth - 43f, y + 19f, 28f, 29f),
            "X", false))
        {
            SetPanelVisibility(false);
            return;
        }

        // One status strip instead of cramped floating text above tabs.
        StudioFill(new Rect(px, y + StudioStatusY,
            pw, 37f), StudioSurface);
        StudioFill(new Rect(px, y + StudioStatusY, 3f, 37f),
            IsCameraActive ? StudioAccent : StudioAmber);
        StudioText(new Rect(px + 13f, y + StudioStatusY + 6f,
                195f, 25f),
            IsCameraActive ? "CAMERA ONLINE" : "CAMERA OFF",
            IsCameraActive ? _studioHeading : _studioMuted);
        StudioText(new Rect(px + pw - 188f, y + StudioStatusY + 6f,
                174f, 25f),
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
            if (StudioButton(new Rect(tx, y + StudioTabsY,
                    tabW, 40f), labels[i], _panelTab == i))
            {
                _panelTab = i;
                _activeSlider = 0;
                _confirmClearKeyframes = false;
            }
        }

        StudioFill(new Rect(px, y + StudioDividerY,
            pw, 1f), StudioStroke);

        // No flexible footer overlays: this content band and footer are
        // intentionally disjoint even after uniform window scaling.
        if (_panelTab == 0)
            DrawCameraTab(px, y + StudioContentY, pw);
        else if (_panelTab == 1)
            DrawFlightTab(px, y + StudioContentY, pw);
        else
            DrawKeyframesTab(px, y + StudioContentY, pw);

        StudioFill(new Rect(px, y + StudioFooterY,
            pw, 1f), StudioStroke);
        StudioText(new Rect(px + 2f, y + StudioFooterY + 13f,
                pw - 4f, 20f),
            "RMB outside panel + WASD to fly   •   F2 to hide",
            _studioMuted);
        StudioText(new Rect(px + 2f, y + StudioFooterY + 37f,
                pw - 4f, 20f),
            "Desktop camera only  •  VR headset / avatar unchanged",
            _studioMuted);
    }

    private void DrawCameraTab(float x, float y, float w)
    {
        StudioText(new Rect(x, y, w, 24f), "Camera perspective",
            _studioHeading);
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
        StudioFill(new Rect(x, y, w, 66f), StudioSurface);
        StudioFill(new Rect(x, y, 3f, 66f), StudioAccent);
        StudioText(new Rect(x + 15f, y + 7f, w - 30f, 25f),
            _keyframes.Count + " / 80  SAVED SHOTS", _studioHeading);
        StudioText(new Rect(x + 15f, y + 34f, w - 30f, 21f),
            _keyframes.Count > 1
                ? "Curved path • " + KeyframePathLength().ToString("0.0") +
                    "m • " + (_playingKeyframes ? "PLAYING" : "READY")
                : "Add at least two camera shots to make a path",
            _studioMuted);

        float half = (w - 9f) * .5f;
        if (StudioButton(new Rect(x, y + 80f, half, 38f),
            "+ Add shot", false, true))
        {
            _confirmClearKeyframes = false;
            AddKeyframe();
        }
        if (StudioButton(new Rect(x + half + 9f, y + 80f,
            half, 38f), "Undo last", false))
        {
            _confirmClearKeyframes = false;
            RemoveLastKeyframe();
        }
        if (StudioButton(new Rect(x, y + 127f, half, 38f),
            _playingKeyframes ? "Stop playback" : "Play path",
            _playingKeyframes))
        {
            _confirmClearKeyframes = false;
            if (_playingKeyframes) StopKeyframePlayback();
            else StartKeyframePlayback();
        }
        if (StudioButton(new Rect(x + half + 9f, y + 127f,
            half, 38f), _confirmClearKeyframes ?
                "Confirm clear?" : "Clear path", _confirmClearKeyframes))
        {
            if (_confirmClearKeyframes)
            {
                ClearKeyframes();
                _confirmClearKeyframes = false;
            }
            else _confirmClearKeyframes = true;
        }

        float duration = StudioSlider(7, x, y + 181f, w,
            "Playback duration", Plugin.KeyframeDuration.Value,
            2f, 120f,
            Plugin.KeyframeDuration.Value.ToString("0") + " sec");
        duration = Mathf.Round(duration);
        if (Mathf.Abs(duration - Plugin.KeyframeDuration.Value) > .5f)
            Plugin.KeyframeDuration.Value = duration;

        if (StudioButton(new Rect(x, y + 258f, w, 41f),
            "Show camera path & markers  " +
                (Plugin.ShowKeyframePath.Value ? "ON" : "OFF"),
            Plugin.ShowKeyframePath.Value))
        {
            Plugin.ShowKeyframePath.Value =
                !Plugin.ShowKeyframePath.Value;
        }

        // A separate help card, with consistent 27px line spacing.
        // Its last line ends at +405; the pinned footer begins at +632.
        StudioFill(new Rect(x, y + 313f, w, 87f), StudioSurface);
        StudioFill(new Rect(x, y + 313f, 3f, 87f), StudioStroke);
        StudioText(new Rect(x + 13f, y + 322f, w - 26f, 20f),
            "Mint line = trajectory   •   Crosses = saved shots",
            _studioMuted);
        StudioText(new Rect(x + 13f, y + 349f, w - 26f, 20f),
            "Guides disappear during playback and when F2 closes.",
            _studioMuted);
        StudioText(new Rect(x + 13f, y + 376f, w - 26f, 20f),
            "Curved movement includes your saved camera roll.",
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
