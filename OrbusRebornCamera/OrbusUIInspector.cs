using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

#if ORBUS_UI_AVAILABLE
using UnityEngine.UI;
#endif

namespace OrbusBetterMirror;

/// <summary>
/// Read-only, user-triggered inspection of loaded UI-like scene objects.
/// Ctrl+Shift+F1 writes a bounded text report that can inform future mods.
/// Does not click UI, move a camera/player, read field values, or change objects.
/// </summary>
public sealed class OrbusUIInspector : MonoBehaviour
{
    public OrbusUIInspector(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F1 = 0x70;
    private const int Ctrl = 0x11;
    private const int Shift = 0x10;
    private const int MaxObjects = 35000;
    private const int MaxCandidates = 900;
    private const int MaxComponentNames = 16;
    private const int MaxComponentKinds = 120;

    private static readonly string[] UiTerms =
    {
        "ui", "menu", "canvas", "panel", "button", "click", "hover",
        "pointer", "laser", "raycast", "interact", "select", "eventsystem",
        "inputfield", "dropdown", "scroll", "toggle", "slider", "label",
        "textmesh", "tmp_", "keyboard", "login", "connect", "server",
        "character", "create", "start", "play", "lobby", "confirm",
        "settings", "address", "tooltip", "window", "interface"
    };

    private sealed class Candidate
    {
        public int Score;
        public string Line = "";
    }

    private bool _hotkeyWasDown;
    private bool _working;
    internal static string LastStatus { get; private set; } =
        "Ctrl+Shift+F1: save UI report";

    private static bool Held(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Start()
    {
        Plugin.ModLogger.LogInfo(
            "[UIInspector] Ctrl+Shift+F1 saves a read-only UI hierarchy report.");
        Plugin.ModLogger.LogInfo(
            "[UIInspector] No UI values, password contents or server credentials are collected.");
    }

    private void Update()
    {
        bool chord = Application.isFocused &&
            Held(F1) && Held(Ctrl) && Held(Shift);

        if (chord && !_hotkeyWasDown && !_working)
            WriteReport();

        _hotkeyWasDown = chord;
    }

    private static string Clean(string? value, int max = 100)
    {
        if (string.IsNullOrEmpty(value)) return "(unnamed)";
        string s = value.Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\t', ' ').Trim();
        if (s.Length > max) s = s.Substring(0, max) + "...";
        return s;
    }

    private static string PathOf(Transform? transform, int limit = 18)
    {
        if (transform == null) return "(null)";
        var parts = new List<string>();
        Transform? cursor = transform;
        while (cursor != null && parts.Count < limit)
        {
            parts.Add(Clean(cursor.gameObject.name, 64));
            cursor = cursor.parent;
        }
        parts.Reverse();
        return (cursor != null ? ".../" : "") + string.Join("/", parts);
    }

    private static bool LooksLikeUi(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = name.ToLowerInvariant();
        foreach (string term in UiTerms)
        {
            // Avoid treating ordinary names like "build", "player"
            // and "equipment" as menu UI because they contain "ui".
            if (term == "ui")
            {
                if (lower == "ui" || lower.StartsWith("ui_") ||
                    lower.StartsWith("ui-") || lower.EndsWith("_ui") ||
                    lower.Contains("userinterface"))
                    return true;
            }
            else if (lower.Contains(term)) return true;
        }
        return false;
    }

    private static int ScoreName(string name)
    {
        if (!LooksLikeUi(name)) return 0;
        string lower = name.ToLowerInvariant();
        int score = 10;
        foreach (string term in UiTerms)
        {
            if (term == "ui")
            {
                if (lower == "ui" || lower.StartsWith("ui_") ||
                    lower.EndsWith("_ui") || lower.Contains("userinterface"))
                    score += 4;
            }
            else if (lower.Contains(term)) score += 4;
        }
        if (lower.Contains("connect") || lower.Contains("server") ||
            lower.Contains("menu") || lower.Contains("button"))
            score += 18;
        return score;
    }

    private static string ComponentNames(GameObject go,
        Dictionary<string, int> histogram, out int score)
    {
        var parts = new List<string>();
        score = 0;
        try
        {
            foreach (Component component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                string type = Clean(component.GetType().FullName, 130);
                if (histogram.TryGetValue(type, out int count))
                    histogram[type] = count + 1;
                else histogram[type] = 1;

                if (parts.Count < MaxComponentNames) parts.Add(type);
                if (LooksLikeUi(type)) score += 24;

                if (component is RectTransform) score += 12;
                if (component is Collider) score += 1;
#if ORBUS_UI_AVAILABLE
                if (component is Canvas) score += 80;
                if (component is Selectable) score += 80;
                if (component is GraphicRaycaster) score += 65;
#endif
            }
        }
        catch (Exception ex)
        {
            parts.Add("COMPONENT_ENUM_ERROR:" + Clean(ex.Message, 80));
        }
        return parts.Count == 0 ? "(no readable components)" :
            string.Join(", ", parts);
    }

    private static bool IsLiveScene(Transform transform)
    {
        try
        {
            var scene = transform.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }
        catch { return false; }
    }

    private static string TypeDetails(GameObject go)
    {
        var details = new StringBuilder();
        try
        {
            details.Append(" layer=").Append(go.layer).Append(":")
                .Append(Clean(LayerMask.LayerToName(go.layer), 32));

            RectTransform? rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                details.Append(" rectSize=(")
                    .Append(rect.rect.width.ToString("0.0"))
                    .Append(",")
                    .Append(rect.rect.height.ToString("0.0"))
                    .Append(")");
            }
            Collider? collider = go.GetComponent<Collider>();
            if (collider != null)
                details.Append(" collider=").Append(collider.GetType().Name)
                    .Append(" trigger=").Append(collider.isTrigger);

            Renderer? renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                details.Append(" renderer=").Append(renderer.GetType().Name);

#if ORBUS_UI_AVAILABLE
            Canvas? canvas = go.GetComponent<Canvas>();
            if (canvas != null)
                details.Append(" canvasMode=").Append(canvas.renderMode)
                    .Append(" sortOrder=").Append(canvas.sortingOrder);
            Selectable? selectable = go.GetComponent<Selectable>();
            if (selectable != null)
                details.Append(" selectable=").Append(selectable.GetType().Name)
                    .Append(" interactable=").Append(selectable.IsInteractable());
#endif
        }
        catch (Exception ex)
        {
            details.Append(" detailsError=").Append(Clean(ex.Message, 60));
        }
        return details.ToString();
    }

    private static Camera? FindDesktopCamera()
    {
        Camera? best = null;
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera == null || !camera.enabled ||
                camera.targetTexture != null || camera.targetDisplay != 0)
                continue;
            if (camera.name == "BetterMirror Creator Camera")
                return camera;
            if (best == null || camera.depth > best.depth) best = camera;
        }
        return best;
    }

    private static void InspectCursor(StringBuilder output)
    {
        output.AppendLine();
        output.AppendLine("=== MOUSE TARGET / RAYCAST (READ ONLY) ===");
        try
        {
            Camera? camera = FindDesktopCamera();
            if (camera == null)
            {
                output.AppendLine("No enabled monitor camera detected.");
                return;
            }
            output.AppendLine("Camera: " + Clean(camera.name) +
                " FOV=" + camera.fieldOfView.ToString("0.0") +
                " Depth=" + camera.depth.ToString("0.0"));
            Vector3 mouse = Input.mousePosition;
            output.AppendLine("Mouse screen position: " + mouse.x.ToString("0.0") +
                "," + mouse.y.ToString("0.0") +
                " (position only, never text input)");
            Ray ray = camera.ScreenPointToRay(mouse);
            var hits = new List<RaycastHit>();
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 300f))
                hits.Add(hit);
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (hits.Count == 0)
                output.AppendLine("Physics hits: 0 (UI may not have colliders).");
            int count = 0;
            foreach (RaycastHit hit in hits)
            {
                if (count++ >= 12) break;
                if (hit.collider == null) continue;
                GameObject go = hit.collider.gameObject;
                output.AppendLine(
                    "Hit " + count + " distance=" + hit.distance.ToString("0.00") +
                    " path=" + PathOf(go.transform) +
                    " collider=" + hit.collider.GetType().Name);
                Transform? parent = go.transform;
                for (int depth = 0; parent != null && depth < 4;
                     depth++, parent = parent.parent)
                {
                    var hist = new Dictionary<string, int>();
                    int score;
                    output.AppendLine("  ancestor[" + depth + "] " +
                        Clean(parent.name) + " components: " +
                        ComponentNames(parent.gameObject, hist, out score));
                }
            }

            // Rendered menus sometimes have renderer bounds but no colliders.
            var closest = new List<(float Distance, Renderer Renderer)>();
            foreach (Renderer renderer in
                UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy) continue;
                if (!renderer.bounds.IntersectRay(ray, out float distance) ||
                    distance < 0 || distance > 300f) continue;
                if (closest.Count < 10 ||
                    distance < closest[closest.Count - 1].Distance)
                {
                    closest.Add((distance, renderer));
                    closest.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                    if (closest.Count > 10) closest.RemoveAt(10);
                }
            }
            output.AppendLine("Renderer bounds candidates: " + closest.Count);
            foreach (var hit in closest)
                output.AppendLine("  distance=" +
                    hit.Distance.ToString("0.00") + " path=" +
                    PathOf(hit.Renderer.transform) + " type=" +
                    hit.Renderer.GetType().Name);
        }
        catch (Exception ex)
        {
            output.AppendLine("Mouse inspection failed: " +
                Clean(ex.Message, 180));
        }
    }

    private static void AppendUiSurvey(StringBuilder output)
    {
        output.AppendLine();
        output.AppendLine("=== KNOWN UNITY UI TYPES ===");
#if ORBUS_UI_AVAILABLE
        try
        {
            int canvases = 0, selectables = 0, raycasters = 0;
            foreach (var item in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (item != null) canvases++;
            foreach (var item in UnityEngine.Object.FindObjectsOfType<Selectable>())
                if (item != null) selectables++;
            foreach (var item in UnityEngine.Object.FindObjectsOfType<GraphicRaycaster>())
                if (item != null) raycasters++;
            output.AppendLine("Legacy UnityEngine.UI interop: available");
            output.AppendLine("Active Canvases=" + canvases +
                ", Selectables=" + selectables +
                ", GraphicRaycasters=" + raycasters);
            output.AppendLine("Counts exclude inactive objects; hierarchy scan below includes inactive scene transforms.");
        }
        catch (Exception ex)
        {
            output.AppendLine("Unity UI survey error: " + Clean(ex.Message, 160));
        }
#else
        output.AppendLine("Legacy UnityEngine.UI interop: unavailable at compile time.");
        output.AppendLine("This does not prove Orbus has no UI; it may use mesh/custom VR scripts.");
#endif
    }

    private void WriteReport()
    {
        _working = true;
        LastStatus = "Scanning scene UI...";
        try
        {
            var output = new StringBuilder(64 * 1024);
            output.AppendLine("REBORNCAM ORBUS UI INSPECTOR v0.9.3");
            output.AppendLine("Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            output.AppendLine("Scope: loaded scene objects, including inactive transforms.");
            output.AppendLine("Read-only. NO text field values, saved passwords, server addresses,");
            output.AppendLine("authentication tokens, network requests, or UI callbacks are read.");
            output.AppendLine("Names and hierarchy paths may contain identifying labels; review before sharing.");
            output.AppendLine("Active scene: " + Clean(SceneManager.GetActiveScene().name));
            output.AppendLine("Loaded scenes: " + SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                output.AppendLine("  [" + i + "] " + Clean(scene.name) +
                    " loaded=" + scene.isLoaded);
            }
            output.AppendLine("XR device active: " + XRSettings.isDeviceActive);
            output.AppendLine("Screen: " + Screen.width + "x" + Screen.height);

            AppendUiSurvey(output);
            InspectCursor(output);

            output.AppendLine();
            output.AppendLine("=== UI-LIKE SCENE OBJECTS, INCLUDING INACTIVE ===");
            output.AppendLine("Scores prioritize UI names, custom pointer scripts,");
            output.AppendLine("Unity UI components and menu-like ancestors.");
            var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
            var candidates = new List<Candidate>();
            int visited = 0, matched = 0, live = 0;
            int unreadable = 0;
            // All loaded transforms includes inactive game objects. Exclude
            // assets and prefabs, which have no loaded scene and do not
            // represent clickable controls in the current world.
            foreach (Transform transform in
                Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || !IsLiveScene(transform)) continue;
                live++;
                if (visited >= MaxObjects) continue;
                visited++;
                try
                {
                    GameObject go = transform.gameObject;
                    int score = ScoreName(go.name);
                    Transform? parent = transform.parent;
                    for (int depth = 0; depth < 4 && parent != null;
                        depth++, parent = parent.parent)
                    {
                        if (LooksLikeUi(parent.name)) score += 5;
                    }
                    int componentScore;
                    string types = ComponentNames(go, histogram,
                        out componentScore);
                    score += componentScore;
                    if (score <= 0) continue;
                    matched++;

                    // Limit report size but keep the highest-ranked objects.
                    string line = "score=" + score +
                        " active=" + go.activeInHierarchy +
                        " scene=" + Clean(go.scene.name, 45) +
                        " path=" + PathOf(transform) + TypeDetails(go) +
                        "\n    components: " + types;
                    var candidate = new Candidate { Score = score, Line = line };
                    if (candidates.Count < MaxCandidates)
                        candidates.Add(candidate);
                    else
                    {
                        int low = 0;
                        for (int n = 1; n < candidates.Count; n++)
                            if (candidates[n].Score < candidates[low].Score)
                                low = n;
                        if (score > candidates[low].Score)
                            candidates[low] = candidate;
                    }
                }
                catch (Exception ex)
                {
                    unreadable++;
                    if (unreadable <= 8)
                        output.AppendLine("WARN: object inspection skipped: " +
                            Clean(ex.Message, 120));
                }
            }

            candidates.Sort((a, b) =>
            {
                int score = b.Score.CompareTo(a.Score);
                return score == 0
                    ? string.Compare(a.Line, b.Line,
                        StringComparison.OrdinalIgnoreCase)
                    : score;
            });
            output.AppendLine("Transforms in loaded scenes: " + live);
            output.AppendLine("Inspected: " + visited + " / " + MaxObjects +
                ", matching: " + matched +
                ", written: " + candidates.Count +
                ", errors: " + unreadable);
            if (live > MaxObjects)
                output.AppendLine("TRUNCATED: scene exceeds safety scan limit.");
            foreach (Candidate entry in candidates)
                output.AppendLine(entry.Line);

            output.AppendLine();
            output.AppendLine("=== COMPONENT TYPE HISTOGRAM (INSPECTED SCENE) ===");
            var counts = new List<KeyValuePair<string, int>>(histogram);
            counts.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < Math.Min(counts.Count, MaxComponentKinds); i++)
                output.AppendLine(counts[i].Value + "  " + counts[i].Key);

            output.AppendLine();
            output.AppendLine("=== INTERPRETATION NOTES ===");
            output.AppendLine("- Canvas + GraphicRaycaster + Selectable -> likely standard Unity UI.");
            output.AppendLine("- RectTransform/text meshes without Selectable -> custom UI or display-only controls.");
            output.AppendLine("- MeshRenderer/Collider/laser scripts -> likely 3D interaction.");
            output.AppendLine("- Actual Orbus class types may be unavailable through managed wrappers.");
            output.AppendLine("- This is a discovery snapshot, not proof that clicks can be injected.");
            output.AppendLine("- To diagnose one button, aim at it before pressing Ctrl+Shift+F1.");
            output.AppendLine("- No game objects were modified or button handlers invoked.");

            string directory = Path.Combine(
                BepInEx.Paths.ConfigPath, "RebornCam", "UIReports");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory,
                "OrbusUI_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                ".txt");
            File.WriteAllText(file, output.ToString(), Encoding.UTF8);
            LastStatus = "Report saved: " + Path.GetFileName(file);
            Plugin.ModLogger.LogInfo(
                "[UIInspector] " + LastStatus +
                " | objects=" + visited +
                " candidates=" + matched + " file=" + file);
        }
        catch (Exception ex)
        {
            LastStatus = "UI scan failed - see BepInEx log.";
            Plugin.ModLogger.LogError("[UIInspector] Scan error: " + ex);
        }
        finally { _working = false; }
    }
}
