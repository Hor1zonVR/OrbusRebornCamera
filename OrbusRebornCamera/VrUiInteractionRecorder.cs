using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace OrbusBetterMirror;

/// <summary>
/// Captures *observations* of real VR UI interactions for reverse engineering.
/// Never sends pointer events, invokes game input methods, or changes a
/// controller, avatar, tracked object, camera, or raycaster.
/// Ctrl+Shift+F1 starts/stops a bounded VR recording when XR is active.
/// </summary>
public sealed class VrUiInteractionRecorder : MonoBehaviour
{
    public VrUiInteractionRecorder(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F1Key = 0x70;
    private const int CtrlKey = 0x11;
    private const int ShiftKey = 0x10;
    private const float CaptureSeconds = 60f;
    private const float SampleInterval = 0.10f;
    private const float MaxDistance = 30f;
    private const int MaxEvents = 650;

    private sealed class Hand
    {
        public string Name = "";
        public XRNode Node;
        public Transform? Controller;
        public Transform? Pointer;
        public string LastTarget = "";
        public bool WasPressed;
        public bool LastAvailable;
        public float NextHeartbeat;
    }

    private readonly Hand _left = new Hand { Name = "left", Node = XRNode.LeftHand };
    private readonly Hand _right = new Hand { Name = "right", Node = XRNode.RightHand };
    private readonly List<string> _events = new List<string>();
    private bool _chordWasDown;
    private bool _recording;
    private float _startedAt;
    private float _nextSample;
    private int _samples;
    private int _eventLimitWarnings;
    private string _startScene = "";
    private string _startXrInput = "";
    private int _uiLayer = -1;

    internal static string Status { get; private set; } =
        "VR interaction recorder: idle";

    private static bool Held(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Start()
    {
        Plugin.ModLogger.LogInfo(
            "[VrUiRecorder] Ctrl+Shift+F1 starts a 60-second VR UI observation " +
            "while XR is active. Repeat the shortcut to stop early.");
        Plugin.ModLogger.LogInfo(
            "[VrUiRecorder] Read-only: observes rays and XR trigger state only.");
    }

    private void Update()
    {
        try
        {
            bool chord = Application.isFocused &&
                Held(F1Key) && Held(CtrlKey) && Held(ShiftKey);

            if (chord && !_chordWasDown && XRSettings.isDeviceActive)
            {
                if (_recording) Finish("stopped by user");
                else Begin();
            }
            _chordWasDown = chord;

            if (!_recording) return;
            if (!XRSettings.isDeviceActive)
            {
                Finish("XR disconnected");
                return;
            }
            if (Time.unscaledTime - _startedAt >= CaptureSeconds)
            {
                Finish("60-second limit reached");
                return;
            }
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + SampleInterval;
            _samples++;

            Observe(_left);
            Observe(_right);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[VrUiRecorder] Recorder update error: " + ex);
            if (_recording) Finish("exception");
        }
    }

    private static string Clean(string? text, int max = 150)
    {
        if (string.IsNullOrEmpty(text)) return "(none)";
        string s = text.Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\t', ' ').Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    private static string PathOf(Transform? item)
    {
        var parts = new List<string>();
        Transform? cur = item;
        for (int depth = 0; cur != null && depth < 12;
            depth++, cur = cur.parent)
            parts.Add(Clean(cur.name, 60));
        parts.Reverse();
        return (cur != null ? ".../" : "") + string.Join("/", parts);
    }

    private static bool UnderController(Transform transform, string hand)
    {
        Transform? node = transform;
        bool controllerFound = false;
        for (int i = 0; i < 12 && node != null; i++, node = node.parent)
        {
            if (node.name == "Controller (" + hand + ")")
                controllerFound = true;
            if (node.name == "PlayerRigv4")
                return controllerFound;
        }
        return false;
    }

    private static void ResolveHand(Hand hand)
    {
        hand.Controller = null;
        hand.Pointer = null;
        // The objects can be inactive until a VR laser is shown, so use
        // FindObjectsOfTypeAll instead of only searching active transforms.
        int inspected = 0;
        foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (transform == null || ++inspected > 15000) break;
            if (transform.name == "Controller (" + hand.Name + ")" &&
                transform.parent != null &&
                transform.parent.name == "PlayerRigv4")
                hand.Controller = transform;
            else if (transform.name == "UIPointer" &&
                UnderController(transform, hand.Name))
                hand.Pointer = transform;
            if (hand.Controller != null && hand.Pointer != null) break;
        }
    }

    private void Begin()
    {
        _recording = true;
        _startedAt = Time.unscaledTime;
        _nextSample = 0f;
        _samples = 0;
        _eventLimitWarnings = 0;
        _uiLayer = LayerMask.NameToLayer("UI-Interactable");
        _events.Clear();
        _startScene = Clean(SceneManager.GetActiveScene().name);
        ProbeXrTriggerApi();
        _startXrInput = _xrTriggerApiStatus;

        ResolveHand(_left);
        ResolveHand(_right);
        ResetHand(_left);
        ResetHand(_right);

        Add("START scene=" + _startScene +
            " XRactive=" + XRSettings.isDeviceActive +
            " UI-Interactable layer=" + _uiLayer);
        Add("Input polling=" + _startXrInput);
        Add("Left controller=" + PathOf(_left.Controller) +
            " | laser pointer=" + PathOf(_left.Pointer));
        Add("Right controller=" + PathOf(_right.Controller) +
            " | laser pointer=" + PathOf(_right.Pointer));
        Add("Aim at a menu item with the VR controller and press/release trigger.");
        Add("Then try the other hand or another menu item. No interactions are generated by this mod.");
        Status = "VR UI CAPTURE: recording (up to 60 seconds)";
        Plugin.ModLogger.LogInfo(
            "[VrUiRecorder] Recording started. Interact with Orbus menus in VR. " +
            "Press Ctrl+Shift+F1 to end early.");
    }

    private void ResetHand(Hand hand)
    {
        hand.LastTarget = "";
        hand.WasPressed = false;
        hand.LastAvailable = false;
        hand.NextHeartbeat = 0f;
    }

    private void Add(string line)
    {
        if (_events.Count < MaxEvents)
            _events.Add((Time.unscaledTime - _startedAt)
                .ToString("000.00") + "s " + line);
        else if (_eventLimitWarnings++ == 0)
            Plugin.ModLogger.LogWarning(
                "[VrUiRecorder] Event limit reached; remaining observations omitted.");
    }

    // Unity 2018 installations do not necessarily expose InputDevices or
    // CommonUsages in their generated interop. Discover those APIs at runtime
    // rather than referencing missing C# types and breaking the entire mod.
    private static bool _xrTriggerApiChecked;
    private static string _xrTriggerApiStatus = "not inspected";
    private static System.Reflection.MethodInfo? _getDeviceAtNode;
    private static System.Reflection.MethodInfo? _readDigital;
    private static System.Reflection.MethodInfo? _readAnalog;
    private static System.Reflection.PropertyInfo? _isValid;
    private static object? _digitalUsage;
    private static object? _analogUsage;

    private static System.Type? FindManagedType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                System.Type? type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            catch { /* Optional Unity assembly may not load. */ }
        }
        return null;
    }

    private static object? ReadStaticUsage(System.Type type, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static;
        try
        {
            var field = type.GetField(name, flags);
            if (field != null) return field.GetValue(null);
            var property = type.GetProperty(name, flags);
            return property != null ? property.GetValue(null) : null;
        }
        catch { return null; }
    }

    private static System.Reflection.MethodInfo? FeatureMethod(
        System.Type deviceType, object? usage, System.Type outputType)
    {
        if (usage == null) return null;
        foreach (var method in deviceType.GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance))
        {
            if (method.Name != "TryGetFeatureValue") continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 2 &&
                parameters[0].ParameterType == usage.GetType() &&
                parameters[1].ParameterType ==
                    outputType.MakeByRefType())
                return method;
        }
        return null;
    }

    private static void ProbeXrTriggerApi()
    {
        if (_xrTriggerApiChecked) return;
        _xrTriggerApiChecked = true;
        try
        {
            System.Type? devices = FindManagedType("UnityEngine.XR.InputDevices");
            System.Type? usages = FindManagedType("UnityEngine.XR.CommonUsages");
            if (devices == null || usages == null)
            {
                _xrTriggerApiStatus =
                    "Legacy XR API: InputDevices/CommonUsages not exposed";
                return;
            }
            _getDeviceAtNode = devices.GetMethod("GetDeviceAtXRNode",
                new[] { typeof(XRNode) });
            if (_getDeviceAtNode == null)
            {
                _xrTriggerApiStatus =
                    "InputDevices found; GetDeviceAtXRNode unavailable";
                return;
            }
            System.Type deviceType = _getDeviceAtNode.ReturnType;
            _isValid = deviceType.GetProperty("isValid");
            _digitalUsage = ReadStaticUsage(usages, "triggerButton");
            _analogUsage = ReadStaticUsage(usages, "trigger");
            _readDigital = FeatureMethod(
                deviceType, _digitalUsage, typeof(bool));
            _readAnalog = FeatureMethod(
                deviceType, _analogUsage, typeof(float));

            _xrTriggerApiStatus = _readDigital != null || _readAnalog != null
                ? "Runtime Unity XR trigger polling available"
                : "Unity XR types found; trigger features unavailable";
        }
        catch (Exception ex)
        {
            _xrTriggerApiStatus =
                "XR API probe failed: " + ex.GetType().Name;
        }
    }

    private static bool TryTrigger(XRNode node, out bool pressed,
        out string kind)
    {
        pressed = false;
        ProbeXrTriggerApi();
        kind = _xrTriggerApiStatus;
        if (_getDeviceAtNode == null) return false;

        try
        {
            object? device = _getDeviceAtNode.Invoke(null,
                new object[] { node });
            if (device == null)
            {
                kind = "XR input device not available";
                return false;
            }
            if (_isValid != null &&
                _isValid.GetValue(device) is bool valid && !valid)
            {
                kind = "XR device not valid";
                return false;
            }
            if (_readDigital != null && _digitalUsage != null)
            {
                object?[] args = { _digitalUsage, false };
                object? ok = _readDigital.Invoke(device, args);
                if (ok is bool received && received &&
                    args[1] is bool digital)
                {
                    pressed = digital;
                    kind = "XR triggerButton";
                    return true;
                }
            }
            if (_readAnalog != null && _analogUsage != null)
            {
                object?[] args = { _analogUsage, 0f };
                object? ok = _readAnalog.Invoke(device, args);
                if (ok is bool received && received &&
                    args[1] is float analog)
                {
                    pressed = analog >= 0.70f;
                    kind = "XR analog trigger >=0.70";
                    return true;
                }
            }
            kind = "XR trigger feature not returned by device";
        }
        catch (Exception ex)
        {
            kind = "XR trigger poll error: " + ex.GetType().Name;
        }
        return false;
    }

    private static string RaySummary(Transform source, Vector3 direction,
        int uiLayer, out string uiTarget)
    {
        uiTarget = "(none)";
        float nearestHit = float.MaxValue;
        float nearestUi = float.MaxValue;
        string firstObject = "(none)";
        try
        {
            Ray ray = new Ray(source.position, direction);
            foreach (RaycastHit hit in Physics.RaycastAll(ray, MaxDistance))
            {
                if (hit.collider == null ||
                    !hit.collider.gameObject.activeInHierarchy)
                    continue;
                if (hit.distance < nearestHit)
                {
                    nearestHit = hit.distance;
                    firstObject = PathOf(hit.collider.transform) +
                        "@" + hit.distance.ToString("0.00") + "m";
                }
                if (hit.collider.gameObject.layer == uiLayer &&
                    hit.distance < nearestUi)
                {
                    nearestUi = hit.distance;
                    uiTarget = PathOf(hit.collider.transform) +
                        "@" + hit.distance.ToString("0.00") + "m";
                }
            }
        }
        catch (Exception ex)
        {
            return "raycastError=" + ex.GetType().Name;
        }
        return "first=" + firstObject + " UI=" + uiTarget;
    }

    private void Observe(Hand hand)
    {
        bool available = TryTrigger(hand.Node, out bool pressed,
            out string inputType);
        if (hand.Pointer == null && hand.Controller == null)
            ResolveHand(hand);
        Transform? laser = hand.Pointer != null &&
            hand.Pointer.gameObject.activeInHierarchy
                ? hand.Pointer : hand.Controller ?? hand.Pointer;

        string forward = "(no controller transform)";
        string reverse = "(no controller transform)";
        string uiForward = "(none)";
        string uiReverse = "(none)";
        if (laser != null)
        {
            forward = RaySummary(laser, laser.forward, _uiLayer, out uiForward);
            reverse = RaySummary(laser, -laser.forward, _uiLayer, out uiReverse);
        }

        string target = "forward{" + uiForward + "} reverse{" + uiReverse + "}";
        bool changedTarget = target != hand.LastTarget;
        bool triggerEdge = available &&
            (!hand.LastAvailable || pressed != hand.WasPressed);
        float elapsed = Time.unscaledTime - _startedAt;
        bool heartbeat = elapsed >= hand.NextHeartbeat;

        if (triggerEdge || changedTarget || heartbeat)
        {
            string reason = triggerEdge ? (pressed ? "TRIGGER DOWN" : "TRIGGER UP")
                : changedTarget ? "target changed" : "heartbeat";
            Add(hand.Name + " " + reason +
                " input=" + inputType +
                " trigger=" + (available ? pressed.ToString() : "unknown") +
                " laser=" + (laser != null ? PathOf(laser) : "(missing)") +
                " active=" + (laser != null && laser.gameObject.activeInHierarchy) +
                " forward: " + forward + " | reverse: " + reverse);
            hand.NextHeartbeat = elapsed + 1.5f;
            if (triggerEdge)
            {
                Plugin.ModLogger.LogInfo("[VrUiRecorder] " + hand.Name +
                    " " + reason + " UI=" + (uiForward != "(none)"
                        ? uiForward : uiReverse));
            }
        }
        hand.WasPressed = pressed;
        hand.LastAvailable = available;
        hand.LastTarget = target;
    }

    private void Finish(string reason)
    {
        _recording = false;
        Add("STOP reason=" + reason + " samples=" + _samples);
        try
        {
            string folder = Path.Combine(
                Paths.ConfigPath, "RebornCam", "UIReports");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder,
                "OrbusVRInput_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
            var report = new StringBuilder();
            report.AppendLine("REBORNCAM VR UI INTERACTION OBSERVER v0.9.5");
            report.AppendLine("Read-only samples; no UI event injection.");
            report.AppendLine("Captured in scene: " + _startScene);
            report.AppendLine("Records native VR controller pointer/world ray observations.");
            report.AppendLine("Not a trace of Orbus callbacks; use with method metadata from UI Inspector.");
            report.AppendLine("Does not read text field contents or credentials.");
            report.AppendLine("Controller transform origins/directions are NOT exported as coordinates.");
            report.AppendLine("NOTICE: GameObject paths may contain player names. Review before sharing.");
            report.AppendLine("Reason ended: " + reason);
            report.AppendLine("Samples=" + _samples +
                " EventsWritten=" + _events.Count + " EventLimit=" + MaxEvents);
            report.AppendLine();
            foreach (string line in _events) report.AppendLine(line);
            File.WriteAllText(file, report.ToString(), Encoding.UTF8);
            Status = "VR UI report saved: " + Path.GetFileName(file);
            Plugin.ModLogger.LogInfo(
                "[VrUiRecorder] " + Status + " | path=" + file);
        }
        catch (Exception ex)
        {
            Status = "VR recorder save failed; see BepInEx log";
            Plugin.ModLogger.LogError(
                "[VrUiRecorder] Could not save report: " + ex);
        }
    }

    private void OnDisable()
    {
        if (_recording) Finish("component disabled");
    }

    private void OnDestroy()
    {
        if (_recording) Finish("component destroyed");
    }
}
