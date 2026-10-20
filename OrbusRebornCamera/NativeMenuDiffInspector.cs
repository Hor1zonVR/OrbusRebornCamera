using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

#if ORBUS_UI_AVAILABLE
using UnityEngine.UI;
#endif

namespace OrbusBetterMirror;

/// <summary>
/// Two snapshots of the actual in-game menu, on demand.
/// Ctrl+Shift+F2 when menu is CLOSED: baseline.
/// Ctrl+Shift+F2 when game Settings is OPEN: difference report.
/// Never modifies game objects, UI, VR pointers or input.
/// </summary>
public sealed class NativeMenuDiffInspector : MonoBehaviour
{
    public NativeMenuDiffInspector(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F2Key = 0x71;
    private const int CtrlKey = 0x11;
    private const int ShiftKey = 0x10;
    private const int MaxTransforms = 45000;
    private const int MaxChanges = 160;
    private const int MaxMenuCandidates = 100;
    private bool _wasDown;
    private Dictionary<int, Entry>? _baseline;
    private string _baselineScene = "";
    private string _baselineTime = "";

    private sealed class Entry
    {
        public int Id;
        public string Path = "";
        public string Name = "";
        public string Scene = "";
        public bool Active;
        public bool SelfActive;
        public int Layer;
        public int Score;
    }

    private sealed class Snapshot
    {
        public Dictionary<int, Entry> Items = new Dictionary<int, Entry>();
        public int Seen;
        public int Skipped;
        public bool Truncated;
    }

    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Start()
    {
        Plugin.ModLogger.LogInfo(
            "[NativeMenuDiff] Ctrl+Shift+F2 CLOSED then OPEN Settings -> one compact menu report.");
    }

    private void Update()
    {
        bool down = Application.isFocused &&
            Held(F2Key) && Held(CtrlKey) && Held(ShiftKey);
        if (down && !_wasDown)
        {
            try
            {
                if (_baseline == null)
                {
                    Snapshot initial = Capture();
                    _baseline = initial.Items;
                    _baselineScene = Clean(SceneManager.GetActiveScene().name);
                    _baselineTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Plugin.ModLogger.LogInfo(
                        "[NativeMenuDiff] BASELINE captured (" + initial.Items.Count +
                        " transforms). Now OPEN Orbus Settings and press Ctrl+Shift+F2 again.");
                }
                else
                {
                    Snapshot current = Capture();
                    SaveReport(current);
                    _baseline = null; // next press starts a NEW comparison
                }
            }
            catch (Exception ex)
            {
                _baseline = null;
                Plugin.ModLogger.LogError("[NativeMenuDiff] Snapshot failed: " + ex);
            }
        }
        _wasDown = down;
    }

    private static string Clean(string? s, int length = 120)
    {
        if (string.IsNullOrEmpty(s)) return "(unnamed)";
        string trimmed = s.Replace('\r', ' ').Replace('\n', ' ')
                          .Replace('\t', ' ').Trim();
        return trimmed.Length > length ? trimmed.Substring(0, length) + "..." : trimmed;
    }

    private static string FullPath(Transform transform)
    {
        var parts = new List<string>();
        Transform? cursor = transform;
        for (int depth = 0; cursor != null && depth < 23; depth++)
        {
            parts.Add(Clean(cursor.name, 65));
            cursor = cursor.parent;
        }
        parts.Reverse();
        return (cursor != null ? ".../" : "") + string.Join("/", parts);
    }

    private static bool Contains(string path, string word) =>
        path.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool LooksLikePlayerUi(string path)
    {
        if (Contains(path, "PlayerRigv4") || Contains(path, "PlayerMenu"))
            return true;

        if (Contains(path, "Transmog") || Contains(path, "Wardrobe") ||
            Contains(path, "Tinkerer") || Contains(path, "Auction") ||
            Contains(path, "Elevator") || Contains(path, "Blueprint"))
            return false;

        return Contains(path, "Settings") || Contains(path, "Inventory") ||
               Contains(path, "Journal") || Contains(path, "PlayerUI") ||
               Contains(path, "PowerUI") || Contains(path, "MainMenu") ||
               Contains(path, "MenuRoot");
    }

    private static int Score(string path, bool active)
    {
        if (!LooksLikePlayerUi(path)) return 0;
        int score = active ? 10 : 0;
        if (Contains(path, "PlayerRigv4")) score += 12;
        if (Contains(path, "Settings")) score += 65;
        if (Contains(path, "PlayerMenu")) score += 60;
        if (Contains(path, "Journal")) score += 45;
        if (Contains(path, "Inventory")) score += 40;
        if (Contains(path, "PowerUI")) score += 45;
        if (Contains(path, "Tab")) score += 20;
        if (Contains(path, "Canvas")) score += 10;
        if (Contains(path, "Button")) score += 10;
        if (Contains(path, "UIPointer")) score += 25;
        if (Contains(path, "PlayerMap")) score -= 25;
        if (Contains(path, "Controller")) score -= 6;
        return score;
    }

    private static Snapshot Capture()
    {
        var snapshot = new Snapshot();
        // Include inactive player menus: FindObjectsOfType excludes them.
        foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (transform == null) continue;
            if (++snapshot.Seen > MaxTransforms)
            {
                snapshot.Truncated = true;
                break;
            }
            try
            {
                GameObject go = transform.gameObject;
                var scene = go.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue;
                string path = FullPath(transform);
                var entry = new Entry
                {
                    Id = go.GetInstanceID(),
                    Path = path,
                    Name = Clean(go.name),
                    Scene = Clean(scene.name, 70),
                    Active = go.activeInHierarchy,
                    SelfActive = go.activeSelf,
                    Layer = go.layer,
                    Score = Score(path, go.activeInHierarchy)
                };
                snapshot.Items[entry.Id] = entry;
            }
            catch { snapshot.Skipped++; }
        }
        return snapshot;
    }

    private static string Describe(Entry e) =>
        (e.Active ? "ON " : "off") +
        " self=" + e.SelfActive +
        " layer=" + e.Layer + ":" + Clean(LayerMask.LayerToName(e.Layer), 26) +
        " scene=" + e.Scene + " path=" + e.Path;

    private static string ActualType(Component component)
    {
        try
        {
            IntPtr clazz = IL2CPP.il2cpp_object_get_class(component.Pointer);
            if (clazz != IntPtr.Zero)
            {
                string n = IL2CPP.il2cpp_class_get_name_(clazz) ?? "";
                string ns = IL2CPP.il2cpp_class_get_namespace_(clazz) ?? "";
                string assembly = IL2CPP.il2cpp_class_get_assemblyname_(clazz) ?? "";
                if (!string.IsNullOrEmpty(n))
                    return Clean((ns.Length == 0 ? "" : ns + ".") + n +
                                 (assembly.Length == 0 ? "" : " [" + assembly + "]"), 140);
            }
        }
        catch { }
        return Clean(component.GetType().FullName, 140);
    }

    private static string ComponentsOf(int id)
    {
        // Access components only on a SMALL number of selected UI candidates,
        // never all the thousands of world scene transforms.
        try
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.GetInstanceID() != id) continue;
                var parts = new List<string>();
                foreach (Component comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    if (parts.Count == 15) { parts.Add("..."); break; }
                    parts.Add(ActualType(comp));
                }
                return string.Join(", ", parts);
            }
        }
        catch (Exception ex) { return "components-error=" + Clean(ex.Message); }
        return "(destroyed or unavailable)";
    }

    private static void AppendPointerSummary(StringBuilder output)
    {
        output.AppendLine();
        output.AppendLine("=== GAME VR POINTER OBSERVATIONS (READ ONLY) ===");
        int layer = LayerMask.NameToLayer("UI-Interactable");
        output.AppendLine("UI-Interactable layer=" + layer);
        var candidates = new List<Transform>();
        foreach (Transform tr in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (tr == null) continue;
            string name = tr.name;
            if (!(Contains(name, "UIPointer") || Contains(name, "LaserPointer") ||
                  Contains(name, "ViveUILaserPointer"))) continue;
            if (!tr.gameObject.activeInHierarchy) continue;
            candidates.Add(tr);
            if (candidates.Count >= 10) break;
        }
        output.AppendLine("Active native pointer transforms=" + candidates.Count);
        foreach (Transform tr in candidates)
        {
            output.AppendLine("  " + FullPath(tr));
            if (layer < 0) continue;
            try
            {
                // Observe both directions because Unity model transform
                // conventions differ across Orbus editions.
                foreach (Vector3 direction in new[] { tr.forward, -tr.forward })
                {
                    RaycastHit[] hits = Physics.RaycastAll(
                        new Ray(tr.position, direction), 10f, 1 << layer);
                    Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    if (hits.Length == 0) continue;
                    if (hits[0].collider != null)
                        output.AppendLine("    " +
                            (Vector3.Dot(direction, tr.forward) > 0f ? "forward" : "reverse") +
                            " UI target=" + FullPath(hits[0].collider.transform) +
                            " distance=" + hits[0].distance.ToString("0.00"));
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("    raycast-error=" + Clean(ex.Message));
            }
        }
        if (candidates.Count == 0)
            output.AppendLine("No active UIPointer objects found by name; this does not prove the VR laser is unavailable.");
    }

    private void SaveReport(Snapshot after)
    {
        if (_baseline == null) return;
        var before = _baseline;
        var changed = new List<Entry>();
        int becameActive = 0, becameInactive = 0, created = 0, renamed = 0;
        foreach (Entry entry in after.Items.Values)
        {
            if (!before.TryGetValue(entry.Id, out Entry? old))
            {
                created++;
                if (entry.Score > 0) changed.Add(entry);
            }
            else if (old.Active != entry.Active ||
                     old.SelfActive != entry.SelfActive ||
                     old.Path != entry.Path)
            {
                if (!old.Active && entry.Active) becameActive++;
                if (old.Active && !entry.Active) becameInactive++;
                if (old.Path != entry.Path) renamed++;
                if (entry.Score > 0 || old.Score > 0) changed.Add(entry);
            }
        }

        int destroyed = before.Keys.Count(id => !after.Items.ContainsKey(id));
        var output = new StringBuilder(45 * 1024);
        output.AppendLine("REBORNCAM NATIVE MENU DIFF v0.9.12");
        output.AppendLine("Baseline (menu CLOSED): " + _baselineTime + " scene=" + _baselineScene);
        output.AppendLine("After (Settings OPEN): " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                          " scene=" + Clean(SceneManager.GetActiveScene().name));
        output.AppendLine("Read-only. No UI methods called, no controller input injected.");
        output.AppendLine("NO text field contents, saved credentials or network details captured.");
        output.AppendLine("Object paths can include names; review before sharing.");
        output.AppendLine("Captured objects: before=" + before.Count + " after=" + after.Items.Count +
                          " created=" + created + " destroyed=" + destroyed +
                          " newlyActive=" + becameActive + " newlyInactive=" + becameInactive +
                          " renamedOrReparented=" + renamed);
        output.AppendLine("XR active=" + XRSettings.isDeviceActive +
                          " truncated=" + after.Truncated +
                          " errors=" + after.Skipped);
        output.AppendLine();
        output.AppendLine("=== CHANGED OR NEW PLAYER-MENU OBJECTS ===");
        output.AppendLine("Highest-ranked relevant objects first. World props and wardrobe items excluded.");
        var ordered = changed.OrderByDescending(x => x.Score).ThenBy(x => x.Path).ToList();
        output.AppendLine("Relevant changed=" + ordered.Count +
                          " shown=" + Math.Min(ordered.Count, MaxChanges));
        foreach (Entry entry in ordered.Take(MaxChanges))
            output.AppendLine("  " + Describe(entry));
        if (ordered.Count == 0)
            output.AppendLine("NO relevant active state changes detected. Menu may use PowerUI/materials rather than scene activation.");

        output.AppendLine();
        output.AppendLine("=== PLAYER-MENU CANDIDATES AFTER OPENING SETTINGS ===");
        var selection = after.Items.Values
            .Where(x => x.Score >= 25 && x.Active)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Path)
            .Take(MaxMenuCandidates).ToList();
        output.AppendLine("Active candidates shown=" + selection.Count +
                          " (sorted by Settings/PlayerMenu/Journal priority)");
        foreach (Entry entry in selection)
            output.AppendLine("  score=" + entry.Score + " " + Describe(entry));

        output.AppendLine();
        output.AppendLine("=== COMPONENT TYPES OF CHANGED MENU ELEMENTS ===");
        // Only a handful of high-value objects to avoid a massive scan.
        foreach (Entry entry in ordered.Take(12))
            output.AppendLine("  " + entry.Path + "\n    " + ComponentsOf(entry.Id));

        AppendPointerSummary(output);
        output.AppendLine();
        output.AppendLine("=== NEXT STEP ===");
        output.AppendLine("If this identifies native Settings buttons, build a separate read-only adapter probe.");
        output.AppendLine("Do NOT patch inventory events or manipulate native UI before testing click ownership.");
        output.AppendLine("If there are NO Settings objects, inspect PowerUI render documents next.");

        string folder = Path.Combine(Paths.ConfigPath, "RebornCam", "UIReports");
        Directory.CreateDirectory(folder);
        string filename = "OrbusMenu_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt";
        string file = Path.Combine(folder, filename);
        File.WriteAllText(file, output.ToString(), Encoding.UTF8);
        Plugin.ModLogger.LogInfo("[NativeMenuDiff] SUCCESS: " + file +
            " | menu changes=" + ordered.Count + " | activeCandidates=" + selection.Count);
    }
}
