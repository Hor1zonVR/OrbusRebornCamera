using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

#if ORBUS_UI_AVAILABLE
using UnityEngine.UI;
#endif

namespace OrbusBetterMirror;

/// <summary>
/// Experimental flatscreen Unity-UI input adapter. It ONLY operates when no
/// real XR device is active. Press TAB to opt in. Never moves the player,
/// touches tracking transforms or synthesizes network/gameplay actions.
/// It invokes existing Unity UI control handlers where available.
/// </summary>
public sealed class DesktopMenuInput : MonoBehaviour
{
    public DesktopMenuInput(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int TabKey = 0x09;
    private const int LeftMouseKey = 0x01;
    private const int RightMouseKey = 0x02;

    private bool _tabWasDown;
    private bool _leftWasDown;
    private bool _pointerEnabled;
    private bool _cursorOwned;
    private bool _originalCursorVisible;
    private CursorLockMode _originalCursorLock;
    private float _nextScan;
    private float _nextErrorLog;
    private string _hoverName = "(none)";
    private string _action = "TAB enables the desktop menu pointer.";
    private int _lastSelectableCount;
    private int _lastCanvasCount;

#if ORBUS_UI_AVAILABLE
    private Selectable? _hover;
    private InputField? _editingField;
    private string _editingText = "";
    private bool _focusEditor;
#endif

    internal static string PointerStatus { get; private set; } = "TAB: pointer off";
    internal static string HoverStatus { get; private set; } = "(none)";
    internal static string ActionStatus { get; private set; } = "Not used";
    internal static string UiStatus { get; private set; } = "Not scanned";

    private void Start()
    {
#if ORBUS_UI_AVAILABLE
        Plugin.ModLogger.LogInfo(
            "[DesktopUI] v0.9.2 Unity UI adapter loaded. TAB toggles pointer; " +
            "left click activates supported Unity UI controls.");
#else
        Plugin.ModLogger.LogWarning(
            "[DesktopUI] UnityEngine.UI.dll was not found at build time. " +
            "Only existing F1 physics diagnostics are available. " +
            "Send the BepInEx interop DLL filenames to identify the menu system.");
#endif
        Plugin.ModLogger.LogInfo(
            "[DesktopUI] Desktop-only and opt-in; never touches VR tracking or player movement.");
    }

    private static bool Held(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Update()
    {
        try { UpdateCore(); }
        catch (Exception ex)
        {
            ReleaseCursor();
            if (Time.unscaledTime >= _nextErrorLog)
            {
                _nextErrorLog = Time.unscaledTime + 5f;
                Plugin.ModLogger.LogWarning("[DesktopUI] Error: " + ex);
            }
        }
    }

    private void UpdateCore()
    {
        if (!Application.isFocused)
        {
            _leftWasDown = false;
            ReleaseCursor();
            return;
        }

        bool tabDown = Held(TabKey);
        if (tabDown && !_tabWasDown)
        {
            if (XRSettings.isDeviceActive)
                SetAction("Desktop pointer is disabled with an active VR headset.");
            else
            {
                _pointerEnabled = !_pointerEnabled;
                if (!_pointerEnabled)
                {
                    CancelTextEditor();
                    ReleaseCursor();
                }
                Plugin.ModLogger.LogInfo(
                    "[DesktopUI] Pointer " + (_pointerEnabled ? "enabled" : "disabled") +
                    ". XR device active: " + XRSettings.isDeviceActive);
#if ORBUS_UI_AVAILABLE
                if (_pointerEnabled) SnapshotUi();
#endif
            }
        }
        _tabWasDown = tabDown;

        bool blocked = XRSettings.isDeviceActive ||
                       CameraDiagnostics.DesktopFreecamActive ||
                       Plugin.CreatorPanelVisible.Value;

        if (!_pointerEnabled || blocked)
        {
            ReleaseCursor();
            _leftWasDown = Held(LeftMouseKey);
            PointerStatus = blocked
                ? "Pointer paused (VR / freecam / F2 panel)"
                : "TAB: pointer off";
            HoverStatus = "(inactive)";
            return;
        }

        AcquireCursor();
        PointerStatus = "TAB: pointer on | Left click: select | F1: diagnostics";
        if (Held(RightMouseKey))
        {
            _leftWasDown = Held(LeftMouseKey);
            return;
        }

#if ORBUS_UI_AVAILABLE
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.08f;
            FindHoveredControl();
        }
        bool leftDown = Held(LeftMouseKey);
        if (leftDown && !_leftWasDown && !_editingField &&
            !MouseOverOurOverlay())
        {
            FindHoveredControl();
            ClickHoveredControl();
        }
        _leftWasDown = leftDown;
#else
        PointerStatus = "UI interop unavailable: F1 diagnostics only";
#endif
        HoverStatus = _hoverName;
        ActionStatus = _action;
    }

    private void SetAction(string value)
    {
        _action = value;
        ActionStatus = value;
        Plugin.ModLogger.LogInfo("[DesktopUI] " + value);
    }

    private void AcquireCursor()
    {
        if (_cursorOwned) return;
        _originalCursorLock = Cursor.lockState;
        _originalCursorVisible = Cursor.visible;
        _cursorOwned = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReleaseCursor()
    {
        if (!_cursorOwned) return;
        _cursorOwned = false;
        try
        {
            Cursor.lockState = _originalCursorLock;
            Cursor.visible = _originalCursorVisible;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[DesktopUI] Could not restore cursor: " + ex.Message);
        }
    }

    private static Camera? GetDisplayCamera()
    {
        Camera? eye = null;
        Camera? primary = null;
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera == null || !camera.enabled ||
                camera.targetTexture != null || camera.targetDisplay != 0)
                continue;
            if (camera.name == "BetterMirror Creator Camera")
                return camera;
            if (camera.name == "Camera (eye)") eye = camera;
            if (primary == null || camera.depth > primary.depth)
                primary = camera;
        }
        return eye ?? primary ?? Camera.main;
    }

    private static bool MouseOverOurOverlay()
    {
        Vector3 m = Input.mousePosition;
        float topY = Screen.height - m.y;
        // Existing F1 desktop diagnostic overlay is at top left.
        // This also prevents selecting hidden UI behind our text editor.
        bool overTop = m.x < 585f && topY >= 0f && topY < 225f;
        bool overEditor = m.x < 650f && topY > Screen.height - 180f;
        return overEditor || overTop;
    }

#if ORBUS_UI_AVAILABLE
    private static bool TryScreenHit(Selectable selectable, Camera renderCamera,
        Vector2 screenPoint, out float rank)
    {
        rank = float.MaxValue;
        if (selectable == null || !selectable.isActiveAndEnabled ||
            !selectable.IsInteractable() || !selectable.gameObject.activeInHierarchy)
            return false;
        RectTransform? rect = selectable.GetComponent<RectTransform>();
        if (rect == null) return false;
        Canvas? canvas = selectable.GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled) return false;

        Camera? eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : renderCamera;

        if (canvas.renderMode == RenderMode.WorldSpace)
        {
            Vector3 delta = rect.position - renderCamera.transform.position;
            if (Vector3.Dot(renderCamera.transform.forward, delta) <= 0f)
                return false;
        }

        if (!RectTransformUtility.RectangleContainsScreenPoint(
            rect, screenPoint, eventCamera))
            return false;

        float distance = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? 0f
            : Vector3.Distance(renderCamera.transform.position, rect.position);
        // Prefer closer controls, with screen-space UI above world-space UI.
        rank = distance - canvas.sortingOrder * 0.01f;
        return true;
    }

    private void FindHoveredControl()
    {
        _hover = null;
        _hoverName = "(no Unity UI control under cursor)";
        if (MouseOverOurOverlay()) { _hoverName = "(RC overlay)"; return; }

        Camera? camera = GetDisplayCamera();
        if (camera == null)
        {
            _hoverName = "(no game camera)";
            return;
        }

        Selectable[] controls = UnityEngine.Object.FindObjectsOfType<Selectable>();
        _lastSelectableCount = controls.Length;
        Vector2 point = Input.mousePosition;
        float best = float.MaxValue;
        foreach (Selectable item in controls)
        {
            try
            {
                if (!TryScreenHit(item, camera, point, out float rank) ||
                    rank >= best) continue;
                best = rank;
                _hover = item;
                _hoverName = item.gameObject.name + " [" +
                    item.GetType().Name + "]";
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= _nextErrorLog)
                {
                    _nextErrorLog = Time.unscaledTime + 5f;
                    Plugin.ModLogger.LogWarning(
                        "[DesktopUI] Hit-test failed for a UI control: " + ex.Message);
                }
            }
        }
    }

    private void ClickHoveredControl()
    {
        if (_hover == null)
        {
            SetAction("No Unity Selectable hit. F1 reports physics colliders.");
            return;
        }

        string controlName = _hover.gameObject.name;
        try
        {
            Button? button = _hover.TryCast<Button>();
            if (button != null)
            {
                button.onClick.Invoke();
                SetAction("Button click sent: " + controlName);
                return;
            }

            InputField? input = _hover.TryCast<InputField>();
            if (input != null)
            {
                _editingField = input;
                _editingText = input.text ?? "";
                _focusEditor = true;
                SetAction("Editing text field: " + controlName);
                return;
            }

            Toggle? toggle = _hover.TryCast<Toggle>();
            if (toggle != null)
            {
                toggle.isOn = !toggle.isOn;
                SetAction("Toggle changed: " + controlName);
                return;
            }

            Dropdown? dropdown = _hover.TryCast<Dropdown>();
            if (dropdown != null)
            {
                dropdown.Show();
                SetAction("Dropdown opened: " + controlName);
                return;
            }

            SetAction("Unsupported UI control: " + controlName +
                      " (" + _hover.GetType().Name + ")");
        }
        catch (Exception ex)
        {
            SetAction("UI action failed: " + controlName);
            Plugin.ModLogger.LogWarning(
                "[DesktopUI] Could not activate " + controlName + ": " + ex);
        }
    }

    private void SnapshotUi()
    {
        try
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            Selectable[] controls = UnityEngine.Object.FindObjectsOfType<Selectable>();
            _lastCanvasCount = canvases.Length;
            _lastSelectableCount = controls.Length;
            UiStatus = "Canvases=" + _lastCanvasCount +
                       " | Controls=" + _lastSelectableCount;
            Plugin.ModLogger.LogInfo("[DesktopUI] " + UiStatus +
                " | Scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            int logged = 0;
            foreach (Selectable control in controls)
            {
                if (logged >= 25) break;
                Canvas? canvas = control.GetComponentInParent<Canvas>();
                Plugin.ModLogger.LogInfo(
                    "[DesktopUI] Control " + control.gameObject.name +
                    " type=" + control.GetType().Name +
                    " canvas=" + (canvas != null ? canvas.name : "(none)") +
                    " enabled=" + control.isActiveAndEnabled);
                logged++;
            }
            if (controls.Length == 0)
                Plugin.ModLogger.LogWarning(
                    "[DesktopUI] No standard Unity UI controls found. " +
                    "Orbus may use a custom VR interaction system; " +
                    "we need component diagnostics before sending VR-style input.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("[DesktopUI] UI survey failed: " + ex);
        }
    }

    private void CommitText()
    {
        if (_editingField == null) return;
        try
        {
            _editingField.text = _editingText;
            _editingField.onEndEdit.Invoke(_editingText);
            SetAction("Text applied to " + _editingField.gameObject.name +
                      " (value not logged)");
        }
        catch (Exception ex)
        {
            SetAction("Could not apply text.");
            Plugin.ModLogger.LogWarning("[DesktopUI] Text apply failed: " + ex);
        }
        _editingField = null;
    }

    private void CancelTextEditor()
    {
        _editingField = null;
        _focusEditor = false;
    }
#else
    private void CancelTextEditor() { }
#endif

    private void OnGUI()
    {
        if (!_pointerEnabled || XRSettings.isDeviceActive ||
            CameraDiagnostics.DesktopFreecamActive ||
            Plugin.CreatorPanelVisible.Value)
            return;

        try
        {
#if ORBUS_UI_AVAILABLE
            if (_editingField != null)
            {
                float y = Screen.height - 160f;
                GUI.Box(new Rect(12f, y, 620f, 146f),
                    "RC DESKTOP TEXT INPUT");
                GUI.Label(new Rect(25f, y + 28f, 580f, 24f),
                    "Edit selected menu field. Applies to game UI, not clipboard.");
                GUI.SetNextControlName("RCDesktopText");
                _editingText = GUI.TextField(
                    new Rect(24f, y + 57f, 575f, 32f), _editingText, 180);
                if (_focusEditor)
                {
                    GUI.FocusControl("RCDesktopText");
                    _focusEditor = false;
                }
                if (GUI.Button(new Rect(24f, y + 99f, 272f, 30f),
                               "Apply (then click Connect)"))
                    CommitText();
                if (GUI.Button(new Rect(305f, y + 99f, 294f, 30f),
                               "Cancel")) CancelTextEditor();
                return;
            }
#endif
            Vector3 m = Input.mousePosition;
            float x = m.x;
            float yMarker = Screen.height - m.y;
            Color original = GUI.color;
            GUI.color = _hoverName.StartsWith("(")
                ? new Color(1f, .75f, .33f, .85f)
                : new Color(.32f, .95f, .78f, .95f);
            GUI.DrawTexture(new Rect(x - 3f, yMarker - 3f, 6f, 6f),
                Texture2D.whiteTexture);
            GUI.color = original;
            float y = Screen.height - 33f;
            GUI.Box(new Rect(12f, y, Mathf.Min(730f, Screen.width - 24f), 28f),
                "RC Pointer | " + _hoverName + " | TAB exit");
        }
        catch (Exception ex)
        {
            ReleaseCursor();
            _pointerEnabled = false;
            Plugin.ModLogger.LogWarning("[DesktopUI] Overlay failed: " + ex);
        }
    }

    private void OnDisable()
    {
        _pointerEnabled = false;
        CancelTextEditor();
        ReleaseCursor();
    }

    private void OnDestroy()
    {
        _pointerEnabled = false;
        CancelTextEditor();
        ReleaseCursor();
    }
}
