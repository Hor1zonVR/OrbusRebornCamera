using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbusBetterMirror;

/// <summary>
/// A camera-only rendering override plus an on-demand local-scene
/// visibility diagnostic. This DOES NOT request chunks or remote players
/// from Orbus or change the avatar, tracking rig, scene GameObjects or
/// any network visibility/relevancy rules.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private bool _cinematicSettingsSaved;
    private float _preCinematicFarClip;
    private bool? _preCinematicOcclusion;
    private float _nextCinematicSettingsCheck;
    private bool _cinematicOcclusionWarned;

    internal static string CinematicRenderStatus { get; private set; } =
        "Cinematic rendering: inactive";
    internal static string LocalSceneProbeStatus { get; private set; } =
        "Ctrl+Shift+F8: capture local streaming report";

    private static readonly PropertyInfo? OcclusionProperty =
        typeof(Camera).GetProperty("useOcclusionCulling",
            BindingFlags.Public | BindingFlags.Instance);

    private static bool? ReadOcclusion(Camera camera)
    {
        if (OcclusionProperty == null ||
            OcclusionProperty.PropertyType != typeof(bool))
            return null;
        try { return OcclusionProperty.GetValue(camera) is bool b ? b : null; }
        catch { return null; }
    }

    private static bool WriteOcclusion(Camera camera, bool value)
    {
        if (OcclusionProperty == null || !OcclusionProperty.CanWrite)
            return false;
        try
        {
            OcclusionProperty.SetValue(camera, value);
            return true;
        }
        catch { return false; }
    }

    private void BeginCinematicRendering()
    {
        if (_desktop == null) return;
        _preCinematicFarClip = _desktop.farClipPlane;
        _preCinematicOcclusion = ReadOcclusion(_desktop);
        _cinematicSettingsSaved = true;
        _nextCinematicSettingsCheck = 0f;
        _cinematicOcclusionWarned = false;
        ApplyCinematicRendering(true);
    }

    private void ApplyCinematicRendering(bool force = false)
    {
        if (_mode != CameraMode.Drone || _desktop == null ||
            !_desktop.enabled)
            return;

        if (!force && Time.unscaledTime < _nextCinematicSettingsCheck)
            return;
        _nextCinematicSettingsCheck = Time.unscaledTime + 1f;

        if (Plugin.CinematicRenderBoost.Value)
        {
            // A bigger far clipping plane only makes ALREADY-LOADED local
            // geometry eligible for rendering; it does not load new areas.
            float requested = Mathf.Clamp(
                Plugin.CinematicFarClip.Value, 300f, 5000f);
            float gameDistance = _source != null
                ? _source.farClipPlane : _preCinematicFarClip;
            float desired = Mathf.Max(requested, gameDistance);
            if (Mathf.Abs(_desktop.farClipPlane - desired) > 1f)
                _desktop.farClipPlane = desired;

            bool? occlusion = ReadOcclusion(_desktop);
            if (Plugin.CinematicDisableOcclusion.Value &&
                occlusion == true && !WriteOcclusion(_desktop, false) &&
                !_cinematicOcclusionWarned)
            {
                _cinematicOcclusionWarned = true;
                Plugin.ModLogger.LogWarning(
                    "[CinematicRender] Cannot disable camera occlusion " +
                    "on this game's Unity interop.");
            }
        }
        else if (_cinematicSettingsSaved)
        {
            // Settings may be changed through BepInEx config while flying.
            _desktop.farClipPlane = _preCinematicFarClip;
            if (_preCinematicOcclusion.HasValue)
                WriteOcclusion(_desktop, _preCinematicOcclusion.Value);
        }

        CinematicRenderStatus =
            "Freecam clip " + _desktop.farClipPlane.ToString("0") +
            "m | occlusion " +
            (ReadOcclusion(_desktop)?.ToString() ?? "unknown") +
            " | boost " + (Plugin.CinematicRenderBoost.Value ? "ON" : "OFF");
    }

    private void EndCinematicRendering()
    {
        if (_desktop != null && _cinematicSettingsSaved)
        {
            try
            {
                _desktop.farClipPlane = _preCinematicFarClip;
                if (_preCinematicOcclusion.HasValue)
                    WriteOcclusion(_desktop, _preCinematicOcclusion.Value);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[CinematicRender] Restore failed: " + ex.Message);
            }
        }
        _cinematicSettingsSaved = false;
        CinematicRenderStatus = "Cinematic rendering: inactive";
    }

    private sealed class NearbyObjects
    {
        public int Enabled25, Enabled75, Enabled150, Enabled300;
        public int Disabled25, Disabled75, Disabled150, Disabled300;

        public void Add(float distance, bool enabled)
        {
            if (distance > 300f) return;
            if (enabled)
            {
                if (distance <= 25f) Enabled25++;
                if (distance <= 75f) Enabled75++;
                if (distance <= 150f) Enabled150++;
                Enabled300++;
            }
            else
            {
                if (distance <= 25f) Disabled25++;
                if (distance <= 75f) Disabled75++;
                if (distance <= 150f) Disabled150++;
                Disabled300++;
            }
        }

        public string Lines(string title) =>
            title + "\n" +
            "  Distance from origin | enabled renderers | inactive/disabled renderers\n" +
            "  <=25m: " + Enabled25 + " / " + Disabled25 + "\n" +
            "  <=75m: " + Enabled75 + " / " + Disabled75 + "\n" +
            "  <=150m: " + Enabled150 + " / " + Disabled150 + "\n" +
            "  <=300m: " + Enabled300 + " / " + Disabled300;
    }

    private static string NativeTypeName(Component component)
    {
        try
        {
            IntPtr klass = IL2CPP.il2cpp_object_get_class(component.Pointer);
            if (klass != IntPtr.Zero)
            {
                string name = IL2CPP.il2cpp_class_get_name_(klass) ?? "";
                string nameSpace =
                    IL2CPP.il2cpp_class_get_namespace_(klass) ?? "";
                if (name.Length > 0)
                    return nameSpace.Length > 0
                        ? nameSpace + "." + name : name;
            }
        }
        catch { /* Best-effort metadata only. */ }
        return component.GetType().FullName ??
            component.GetType().Name;
    }

    private static bool IsSceneObject(GameObject go)
    {
        try
        {
            var scene = go.scene;
            return scene.IsValid() && scene.isLoaded;
        }
        catch { return false; }
    }

    private static bool IsLocalScenery(GameObject go)
    {
        // Exclude player/enemy/avatar layers from the aggregate counts.
        // No remote player coordinates, identities or target lists.
        string layer = LayerMask.LayerToName(go.layer);
        string lower = layer.ToLowerInvariant();
        return !lower.Contains("player") &&
            !lower.Contains("enemy") &&
            !lower.Contains("avatar") &&
            !lower.Contains("npc") &&
            !lower.Contains("monster");
    }

    private void SaveLocalSceneReport()
    {
        if (_desktop == null || !IsCameraActive)
        {
            LocalSceneProbeStatus = "No active recording camera";
            return;
        }
        try
        {
            var report = new StringBuilder();
            report.AppendLine("REBORNCAM LOCAL SCENE STREAMING PROBE v0.9.7");
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Diagnostic only: never adjusts world objects, network visibility or avatars.");
            report.AppendLine("No player names, account IDs, remote positions or actual world coordinates.");
            report.AppendLine("Important: a camera can only draw scene objects currently present on this client.");
            report.AppendLine("Absent geometry could be streamed out OR could be an empty area.");
            report.AppendLine("Unity loaded scenes: " + SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                    report.AppendLine("  " + scene.name);
            }
            report.AppendLine("Camera mode: " + _mode);
            report.AppendLine("Source farClip: " +
                (_source != null ? _source.farClipPlane.ToString("0") : "unknown"));
            report.AppendLine("Desktop farClip: " + _desktop.farClipPlane.ToString("0"));
            report.AppendLine("Source occlusion: " +
                (_source != null
                    ? (ReadOcclusion(_source)?.ToString() ?? "unknown")
                    : "unknown"));
            report.AppendLine("Desktop occlusion: " +
                (ReadOcclusion(_desktop)?.ToString() ?? "unknown"));
            report.AppendLine("Freecam cinematic boost: " +
                Plugin.CinematicRenderBoost.Value);

            Camera? eye = FindVRCamera();
            Vector3 cameraPosition = _desktop.transform.position;
            bool hasEye = eye != null;
            Vector3 eyePosition = hasEye ? eye!.transform.position : Vector3.zero;
            if (hasEye)
                report.AppendLine("Distance camera-to-player-eye: " +
                    Vector3.Distance(cameraPosition, eyePosition).ToString("0.0") + "m");

            var cameraCounts = new NearbyObjects();
            var playerCounts = new NearbyObjects();
            int loaded = 0, objectsInspected = 0, skippedActors = 0;
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null) continue;
                if (++objectsInspected > 45000) break;
                try
                {
                    GameObject go = renderer.gameObject;
                    if (go == null || !IsSceneObject(go)) continue;
                    if (!IsLocalScenery(go))
                    {
                        skippedActors++;
                        continue;
                    }
                    loaded++;
                    Vector3 center = renderer.bounds.center;
                    bool enabled = renderer.enabled && go.activeInHierarchy;
                    cameraCounts.Add(Vector3.Distance(center, cameraPosition), enabled);
                    if (hasEye)
                        playerCounts.Add(Vector3.Distance(center, eyePosition), enabled);
                }
                catch { /* Destroyed renderer or no world bounds. */ }
            }
            report.AppendLine();
            report.AppendLine("Renderers enumerated (including inactive loaded assets): " +
                objectsInspected + " / 45000");
            report.AppendLine("Loaded-scene local-scenery renderers: " + loaded);
            report.AppendLine("Ignored player/creature layer renderers: " +
                skippedActors);
            report.AppendLine(cameraCounts.Lines("Near the recording camera:"));
            if (hasEye)
                report.AppendLine(playerCounts.Lines("Near the local player eye:"));
            else report.AppendLine("Local player eye unavailable; comparison not possible.");

            report.AppendLine();
            report.AppendLine("=== POSSIBLE LOCAL LOADING/CULLING COMPONENT TYPES ===");
            report.AppendLine("Names/types only, no field values, player data, or method calls.");
            var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
            int componentsInspected = 0;
            foreach (Component component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null) continue;
                if (++componentsInspected > 75000) break;
                try
                {
                    if (!IsSceneObject(component.gameObject) ||
                        !IsLocalScenery(component.gameObject))
                        continue;
                    string type = NativeTypeName(component);
                    string lower = type.ToLowerInvariant();
                    if (!lower.Contains("load") && !lower.Contains("stream") &&
                        !lower.Contains("cull") && !lower.Contains("chunk") &&
                        !lower.Contains("terrain") && !lower.Contains("sector") &&
                        !lower.Contains("level") && !lower.Contains("lod") &&
                        !lower.Contains("visibility") && !lower.Contains("pool"))
                        continue;
                    histogram[type] = histogram.TryGetValue(type,
                        out int count) ? count + 1 : 1;
                }
                catch { /* Stale native wrapper. */ }
            }
            var sorted = new List<KeyValuePair<string, int>>(histogram);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            report.AppendLine("Component wrappers inspected: " +
                componentsInspected + " / 75000");
            report.AppendLine("Candidate component TYPES: " + sorted.Count);
            for (int i = 0; i < Math.Min(70, sorted.Count); i++)
                report.AppendLine("  " + sorted[i].Value + " x " + sorted[i].Key);
            report.AppendLine();
            report.AppendLine("Interpretation: if geometry is already active near the freecam, " +
                "look into camera culling, LODs and occlusion.");
            report.AppendLine("If active and inactive geometry both disappear from " +
                "the loaded scene near freecam, the game probably unloads those objects.");
            report.AppendLine("A detached client camera cannot load remote player entities " +
                "that the server has not replicated.");
            report.AppendLine("Do not change server-relevancy or local player position.");

            string dir = Path.Combine(Paths.ConfigPath,
                "RebornCam", "StreamingReports");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "OrbusStreaming_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
            File.WriteAllText(file, report.ToString(), Encoding.UTF8);
            LocalSceneProbeStatus =
                "Streaming report saved: " + Path.GetFileName(file);
            Plugin.ModLogger.LogInfo("[StreamingProbe] " +
                LocalSceneProbeStatus + " | path=" + file);
        }
        catch (Exception ex)
        {
            LocalSceneProbeStatus = "Streaming report failed; see BepInEx log";
            Plugin.ModLogger.LogWarning("[StreamingProbe] " + ex);
        }
    }
}
