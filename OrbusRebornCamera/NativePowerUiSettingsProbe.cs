using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Temporary read-only observer for native PowerUI Settings navigation.
/// No UI creation, DOM mutations, menu calls or input injection.
/// Ctrl+Shift+P starts a capture with the menu CLOSED; press it again
/// with Settings OPEN to write one concise report.
/// </summary>
public sealed class NativePowerUiSettingsProbe : MonoBehaviour
{
    public NativePowerUiSettingsProbe(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private const int Ctrl = 0x11;
    private const int Shift = 0x10;
    private const int P = 0x50;
    private const int MaxRelevantEvents = 72;
    private const int MaxAllSetterCalls = 1500;
    private const int MaxHtmlInspect = 24000;
    private const int MaxRuntimeSeconds = 60;
    private const string PatchId = "com.horizon.orbus.reborncam.nativepowerui.settingsprobe";

    private static readonly BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;

    // Match ONLY identifier-like class/id values; never retain raw HTML or text.
    private static readonly Regex HtmlIdentifiers = new(
        @"\b(?<kind>id|class)\s*=\s*['""](?<value>[a-zA-Z0-9 _:\-]{1,100})['""]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed class Observation
    {
        public string Target = "";
        public string OwnerType = "";
        public string ElementType = "";
        public string Identifiers = "";
        public int Characters;
        public bool NavigationContent;
    }

    private bool _wasPressed;
    private bool _armed;
    private float _expires;
    private Harmony? _harmony;
    private MethodInfo? _setter;
    private readonly List<Observation> _observations = new();
    private string _metadata = "";
    private int _totalCalls;
    private int _relevantCalls;
    private int _skippedCalls;
    private int _capturedTypes;
    private object? _observedDocument;
    private static NativePowerUiSettingsProbe? _current;

    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Start()
    {
        Plugin.ModLogger.LogInfo(
            "[PowerUiSettings] Ready. Close Orbus's menu; Ctrl+Shift+P START, " +
            "open normal Settings, Ctrl+Shift+P STOP. No UI is modified.");
    }

    private void Update()
    {
        bool down = Application.isFocused && Held(Ctrl) && Held(Shift) && Held(P);
        if (down && !_wasPressed)
        {
            try
            {
                if (_armed) Finish("manual stop");
                else Begin();
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError("[PowerUiSettings] Capture error: " + ex);
                StopHook();
            }
        }
        _wasPressed = down;

        if (_armed && Time.realtimeSinceStartup >= _expires)
        {
            try { Finish("automatic timeout (60 seconds)"); }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning("[PowerUiSettings] Timeout save failed: " + ex);
                StopHook();
            }
        }
    }

    private static bool Has(string? text, string part) =>
        text != null &&
        text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool RelevantIdentifier(string text) =>
        Has(text, "setting") || Has(text, "tab") ||
        Has(text, "menu") || Has(text, "inventory") ||
        Has(text, "mission") || Has(text, "achievement") ||
        Has(text, "talent") || Has(text, "equipment");

    private static string SafeType(object? value) =>
        value?.GetType().FullName ?? "(not available)";

    private static object? ReadMember(object? owner, string name)
    {
        if (owner == null) return null;
        try
        {
            Type type = owner is Type staticType ? staticType : owner.GetType();
            object? instance = owner is Type ? null : owner;
            // Only an explicitly named getter, never arbitrary method calls.
            PropertyInfo? prop = type.GetProperty(name, Members);
            if (prop != null && prop.GetIndexParameters().Length == 0)
                return prop.GetValue(instance);
            FieldInfo? field = type.GetField(name, Members);
            if (field != null) return field.GetValue(instance);
            MethodInfo? getter = type.GetMethod(
                "get_" + name, Members, null, Type.EmptyTypes, null);
            return getter?.Invoke(instance, null);
        }
        catch { return null; }
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? found = assembly.GetType(fullName, false);
                if (found != null) return found;
            }
            catch { }
        }
        return null;
    }

    private static string SafeId(object? element)
    {
        string? id = ReadMember(element, "id")?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return "(no id)";
        if (id!.Length > 90 || !Regex.IsMatch(id, @"^[A-Za-z0-9_\-:.]+$"))
            return "(nonstandard id hidden)";
        return RelevantIdentifier(id) ? id : "(other element)";
    }

    private static string IdentifierSummary(string html)
    {
        var selected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string sample = html.Length <= MaxHtmlInspect
            ? html : html.Substring(0, MaxHtmlInspect);
        foreach (Match match in HtmlIdentifiers.Matches(sample))
        {
            string kind = match.Groups["kind"].Value.ToLowerInvariant();
            string raw = match.Groups["value"].Value;
            foreach (string token in raw.Split(new[] { ' ' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (!RelevantIdentifier(token) || token.Length > 60) continue;
                selected.Add(kind + ":" + token);
                if (selected.Count >= 24) break;
            }
            if (selected.Count >= 24) break;
        }
        return selected.Count == 0 ? "(no recognised tab/menu ids or classes)" :
            string.Join(", ", selected);
    }

    private static bool ContainsMenuKeywords(string html)
    {
        // Extremely cheap gate before regex. This is a *transient observer*,
        // not an HTML parser and never stores the original markup.
        return Has(html, "settings") || Has(html, "setting") ||
               Has(html, "inventory") || Has(html, "equipment") ||
               Has(html, "talents") || Has(html, "achievements") ||
               Has(html, "missions-tab") || Has(html, "player-menu");
    }

    private static void ObserveInnerHtml(object __instance, string __0)
    {
        NativePowerUiSettingsProbe? current = _current;
        if (current == null || !current._armed || __0 == null) return;
        try { current.HandleInnerHtml(__instance, __0); }
        catch { current._skippedCalls++; } // no exception may escape into game UI
    }

    private void HandleInnerHtml(object element, string html)
    {
        _totalCalls++;
        if (_totalCalls > MaxAllSetterCalls || _observations.Count >= MaxRelevantEvents)
            return;

        string id = SafeId(element);
        bool matching = RelevantIdentifier(id) || ContainsMenuKeywords(html);
        if (!matching) return;
        _relevantCalls++;

        object? owner = ReadMember(element, "ownerDocument") ??
                        ReadMember(element, "document");
        _observedDocument ??= owner;

        string ids = IdentifierSummary(html);
        // De-duplicate identical observations, but keep changed tab state.
        string elementType = SafeType(element);
        string ownerType = SafeType(owner);
        if (_observations.Count > 0)
        {
            Observation prev = _observations[_observations.Count - 1];
            if (prev.Target == id && prev.Characters == html.Length &&
                prev.Identifiers == ids)
                return;
        }
        _observations.Add(new Observation
        {
            Target = id,
            OwnerType = ownerType,
            ElementType = elementType,
            Characters = html.Length,
            Identifiers = ids,
            NavigationContent = ContainsMenuKeywords(html)
        });
    }

    private static void AppendType(StringBuilder report, string fullName)
    {
        Type? type = FindType(fullName);
        report.AppendLine("Type " + fullName + " found=" + (type != null));
        if (type == null) return;
        try
        {
            report.AppendLine("  Assembly: " + type.Assembly.GetName().Name);
            var relevantMethods = type.GetMethods(Members)
                .Where(m => RelevantIdentifier(m.Name) ||
                    Has(m.Name, "html") || Has(m.Name, "document") ||
                    Has(m.Name, "element") || Has(m.Name, "click") ||
                    Has(m.Name, "event") || Has(m.Name, "mouse"))
                .Select(m => m.Name + "(" + string.Join(",",
                    m.GetParameters().Select(p => p.ParameterType.Name)) +
                    ") -> " + m.ReturnType.Name)
                .Distinct()
                .OrderBy(s => s)
                .Take(28).ToArray();
            foreach (string name in relevantMethods)
                report.AppendLine("  Method " + name);
            foreach (FieldInfo field in type.GetFields(Members)
                .Where(f => RelevantIdentifier(f.Name) ||
                    Has(f.Name, "doc") || Has(f.Name, "page"))
                .Take(18))
                report.AppendLine("  Field " + field.FieldType.Name + " " +
                                  field.Name + " (value not read)");
            foreach (PropertyInfo prop in type.GetProperties(Members)
                .Where(p => RelevantIdentifier(p.Name) ||
                    Has(p.Name, "doc") || Has(p.Name, "page"))
                .Take(18))
                report.AppendLine("  Property " + prop.PropertyType.Name + " " +
                                  prop.Name + " (value not read)");
        }
        catch (Exception ex)
        {
            report.AppendLine("  Metadata unavailable: " + ex.GetType().Name);
        }
    }

    private void Begin()
    {
        _observations.Clear();
        _observedDocument = null;
        _totalCalls = 0;
        _relevantCalls = 0;
        _skippedCalls = 0;

        var metadata = new StringBuilder(8500);
        metadata.AppendLine("=== LIVE CLIENT POWERUI TYPE INVENTORY (METADATA ONLY) ===");
        string[] targets =
        {
            "PlayerMenu", "PowerUI.UI", "PowerUI.Document", "PowerUI.Element",
            "PowerUI.HtmlDocument", "PowerUI.HtmlElement",
            "Dom.Document", "Dom.Element", "Dom.EventTarget"
        };
        foreach (string target in targets) AppendType(metadata, target);

        // QH saw PlayerMenu.document only on generic client.
        // Record the TYPE if exposed; never dump document content.
        Type? playerMenu = FindType("PlayerMenu");
        object? legacyDocument = playerMenu == null ? null :
            ReadMember(playerMenu, "document");
        metadata.AppendLine("PlayerMenu.document TYPE=" + SafeType(legacyDocument));
        _observedDocument = legacyDocument;
        _metadata = metadata.ToString();

        Type? legacy = FindType("PowerUI.Element");
        Type? modern = FindType("Dom.Element");
        Type? modernDoc = FindType("PowerUI.HtmlDocument");
        Type? selected = legacy != null ? legacy :
            modernDoc != null ? modern : null;

        _armed = true;
        _expires = Time.realtimeSinceStartup + MaxRuntimeSeconds;
        _current = this;
        _setter = null;
        if (selected != null)
        {
            _setter = selected.GetMethods(Members)
                .FirstOrDefault(m => m.Name == "set_innerHTML" &&
                    !m.IsStatic && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(string));
        }
        if (_setter != null)
        {
            try
            {
                MethodInfo prefix = typeof(NativePowerUiSettingsProbe).GetMethod(
                    nameof(ObserveInnerHtml), BindingFlags.NonPublic |
                    BindingFlags.Static)!;
                _harmony = new Harmony(PatchId);
                _harmony.Patch(_setter, prefix: new HarmonyMethod(prefix));
                Plugin.ModLogger.LogInfo("[PowerUiSettings] STARTED read-only " +
                    selected!.FullName + ".set_innerHTML observation. " +
                    "Open the original Orbus Settings menu now.");
            }
            catch (Exception ex)
            {
                _harmony = null;
                Plugin.ModLogger.LogWarning("[PowerUiSettings] Could not observe " +
                    "HTML setter: " + ex.GetType().Name + " " + ex.Message +
                    " (metadata-only report will still be saved).");
            }
        }
        else
        {
            Plugin.ModLogger.LogWarning("[PowerUiSettings] No compatible HTML setter " +
                "available; recording safe type inventory only.");
        }
    }

    private void StopHook()
    {
        _armed = false;
        _current = null;
        try { _harmony?.UnpatchSelf(); }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[PowerUiSettings] Patch removal warning: " + ex.GetType().Name);
        }
        _harmony = null;
        _setter = null;
        _observedDocument = null;
    }

    private void Finish(string reason)
    {
        if (!_armed) return;
        StopHook();
        var report = new StringBuilder(18500);
        report.AppendLine("REBORNCAM POWERUI SETTINGS INVESTIGATION v0.9.13");
        report.AppendLine("Time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                          " reason=" + reason);
        report.AppendLine("READ ONLY: observed native HTML setter without changing arguments.");
        report.AppendLine("NO source HTML, text content, character names or config values saved.");
        report.AppendLine("No Unity PlayerRig, native UI buttons or events modified.");
        report.AppendLine();
        report.Append(_metadata);
        report.AppendLine();
        report.AppendLine("=== SETTINGS MENU HTML UPDATE OBSERVATIONS ===");
        report.AppendLine("Observed total setter calls=" + _totalCalls +
            " relevant=" + _relevantCalls +
            " uniqueReported=" + _observations.Count +
            " skipped=" + _skippedCalls +
            " cap=" + MaxAllSetterCalls);
        if (_observations.Count == 0)
            report.AppendLine("No matching setter events. The menu may have been " +
                "built before recording or updated without set_innerHTML. " +
                "This does NOT prove native tabs are inaccessible.");
        int index = 0;
        foreach (Observation eventInfo in _observations)
        {
            report.AppendLine(" #" + (++index) +
                " target=" + eventInfo.Target +
                " length=" + eventInfo.Characters +
                " ownerDocument=" + eventInfo.OwnerType +
                " element=" + eventInfo.ElementType +
                " navigationTerms=" + eventInfo.NavigationContent);
            report.AppendLine("    ids/classes=" + eventInfo.Identifiers);
        }
        report.AppendLine();
        report.AppendLine("=== NEXT ENGINEERING DECISION ===");
        report.AppendLine("If specific Settings tab classes/IDs are observed, " +
            "probe the owning document and click dispatch without mutation.");
        report.AppendLine("If no Settings-related setter is observed, inspect " +
            "PlayerMenu navigation methods and the exact document owner next.");
        report.AppendLine("DO NOT insert DOM nodes into the live game yet.");

        string folder = Path.Combine(Paths.ConfigPath, "RebornCam", "UIReports");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "OrbusPowerUI_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        Plugin.ModLogger.LogInfo("[PowerUiSettings] SAVED " + path +
            " | relevant events=" + _observations.Count +
            " | setter calls=" + _totalCalls);
    }

    private void OnDestroy()
    {
        StopHook();
    }
}
