using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppInterop.Runtime;

namespace OrbusBetterMirror;

/// <summary>
/// Optional CINEMATIC-ONLY rendering extension for the desktop camera.
/// This affects camera clipping/culling of objects that are ALREADY loaded.
/// It cannot request new network objects, spawn scene chunks, move the rig,
/// or extend what the server replicates to the client.
/// </summary>
public sealed partial class CameraDiagnostics
{
    private bool _cinematicWasApplied;
    private Camera? _cinematicCamera;
    private object? _savedLayerCullDistances;
    private PropertyInfo? _layerCullProperty;
    private bool _layerCullOverrideApplied;
    private bool _layerCullAttempted;
    private bool _renderErrorReported;

    internal static string CinematicRenderStatus { get; private set; } =
        "Extended camera rendering: inactive";

    private void RestoreCinematicRendering()
    {
        if (_cinematicCamera != null && _cinematicWasApplied)
        {
            try
            {
                if (_source != null && _source != _cinematicCamera)
                {
                    _cinematicCamera.farClipPlane = _source.farClipPlane;
                    _cinematicCamera.useOcclusionCulling =
                        _source.useOcclusionCulling;
                }
                if (_layerCullOverrideApplied &&
                    _layerCullProperty != null &&
                    _savedLayerCullDistances != null)
                    _layerCullProperty.SetValue(
                        _cinematicCamera, _savedLayerCullDistances);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[CinematicRender] Could not restore all camera defaults: " +
                    ex.GetType().Name);
            }
        }
        _cinematicWasApplied = false;
        _layerCullOverrideApplied = false;
        _layerCullAttempted = false;
        _layerCullProperty = null;
        _savedLayerCullDistances = null;
        _cinematicCamera = null;
        CinematicRenderStatus = "Extended camera rendering: inactive";
    }

    private void TryZeroCameraLayerCullDistances(Camera desktop)
    {
        _layerCullAttempted = true;
        try
        {
            // Unity 2018 IL2CPP interop differs: this property may be a
            // managed float[] or an Il2CppStructArray<float>. Discover the
            // property at runtime so old generated stubs still compile.
            PropertyInfo? prop = desktop.GetType().GetProperty(
                "layerCullDistances",
                BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanRead || !prop.CanWrite)
                return;

            object? existing = prop.GetValue(desktop);
            if (existing == null) return;
            object? emptyDistances = null;
            Type arrayType = prop.PropertyType;
            if (arrayType == typeof(float[]))
                emptyDistances = new float[32];
            else
            {
                // Generic interop arrays normally expose a length constructor
                // and initialize their 32 floats to zero.
                ConstructorInfo? ctor = arrayType.GetConstructor(
                    new[] { typeof(int) });
                if (ctor != null)
                    emptyDistances = ctor.Invoke(new object[] { 32 });
                else
                {
                    ctor = arrayType.GetConstructor(new[] { typeof(long) });
                    if (ctor != null)
                        emptyDistances = ctor.Invoke(new object[] { 32L });
                }
            }
            if (emptyDistances == null) return;
            prop.SetValue(desktop, emptyDistances);
            _savedLayerCullDistances = existing;
            _layerCullProperty = prop;
            _layerCullOverrideApplied = true;
            Plugin.ModLogger.LogInfo(
                "[CinematicRender] Desktop-only layer distance culling disabled.");
        }
        catch (Exception ex)
        {
            if (!_renderErrorReported)
            {
                _renderErrorReported = true;
                Plugin.ModLogger.LogWarning(
                    "[CinematicRender] LayerCullDistances override unavailable " +
                    "in current Unity interop (" + ex.GetType().Name + "). " +
                    "Other extended camera settings still work.");
            }
        }
    }

    private void ApplyCinematicRenderSettings()
    {
        if (_mode != CameraMode.Drone ||
            _desktop == null || !_desktop.enabled ||
            !Plugin.ExtendedRenderEnabled.Value)
        {
            if (_cinematicWasApplied)
                RestoreCinematicRendering();
            return;
        }

        if (_cinematicCamera != null &&
            _cinematicCamera != _desktop)
            RestoreCinematicRendering();

        _cinematicCamera = _desktop;
        _cinematicWasApplied = true;
        try
        {
            // IMPORTANT: never modify _source (VR headset camera), global
            // QualitySettings, renderer.enabled, shared meshes, scene objects
            // or the real player's transforms.
            float far = Mathf.Clamp(Plugin.ExtendedRenderFarClip.Value,
                150f, 5000f);
            _desktop.farClipPlane = Mathf.Max(far,
                _source != null ? _source.farClipPlane : 0f);
            _desktop.useOcclusionCulling = false;

            if (!_layerCullAttempted)
                TryZeroCameraLayerCullDistances(_desktop);

            CinematicRenderStatus =
                "Extended draw: " +
                _desktop.farClipPlane.ToString("0") + "m, occlusion OFF" +
                (_layerCullOverrideApplied
                    ? ", layer-range override ON"
                    : ", layer-range default");
        }
        catch (Exception ex)
        {
            CinematicRenderStatus =
                "Extended rendering unavailable: " + ex.GetType().Name;
            if (!_renderErrorReported)
            {
                _renderErrorReported = true;
                Plugin.ModLogger.LogWarning(
                    "[CinematicRender] Desktop draw extension failed: " + ex);
            }
        }
    }

    private void ToggleCinematicRenderBoost()
    {
        if (_mode != CameraMode.Drone)
        {
            Plugin.ModLogger.LogInfo(
                "[CinematicRender] Enter freecam before toggling the boost.");
            return;
        }
        Plugin.ExtendedRenderEnabled.Value =
            !Plugin.ExtendedRenderEnabled.Value;
        ApplyCinematicRenderSettings();
        Plugin.ModLogger.LogInfo(
            "[CinematicRender] Freecam rendering boost " +
            (Plugin.ExtendedRenderEnabled.Value ? "ON" : "OFF") +
            ". Only already-loaded geometry can reappear.");
    }

    /// <summary>
    /// List only native CLASS NAMES of possibly relevant local scenery
    /// loaders/cullers. Never call methods, read fields, list objects/players
    /// or change any scene state. Runs exclusively on the user hotkey.
    /// </summary>
    private static void AppendLoadingClassCandidates(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("POSSIBLE LOCAL SCENERY LOADING/CULLING COMPONENT CLASSES");
        sb.AppendLine("Metadata names only; no values or gameplay callbacks.");
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        int inspected = 0;
        try
        {
            foreach (Component component in
                Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null) continue;
                GameObject go = component.gameObject;
                if (go == null) continue;
                Scene scene = go.scene;
                if (!scene.IsValid() || !scene.isLoaded ||
                    scene.name == "DontDestroyOnLoad")
                    continue;
                if (++inspected > 30000) break;
                string layer = LayerMask.LayerToName(go.layer);
                if (layer == "Player" || layer == "Enemy" ||
                    layer == "Minicam")
                    continue;
                string full;
                try
                {
                    IntPtr klass =
                        IL2CPP.il2cpp_object_get_class(component.Pointer);
                    if (klass == IntPtr.Zero) continue;
                    string name = IL2CPP.il2cpp_class_get_name_(klass) ?? "";
                    string ns = IL2CPP.il2cpp_class_get_namespace_(klass) ?? "";
                    full = ns.Length == 0 ? name : ns + "." + name;
                }
                catch { continue; }
                string lower = full.ToLowerInvariant();
                if (!lower.Contains("load") &&
                    !lower.Contains("stream") &&
                    !lower.Contains("cull") &&
                    !lower.Contains("chunk") &&
                    !lower.Contains("terrain") &&
                    !lower.Contains("sector") &&
                    !lower.Contains("lod") &&
                    !lower.Contains("occlu") &&
                    !lower.Contains("pool") &&
                    !lower.Contains("scene") &&
                    !lower.Contains("zone"))
                    continue;
                names[full] = names.TryGetValue(full, out int count)
                    ? count + 1 : 1;
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("Metadata scan unavailable: " +
                ex.GetType().Name);
        }
        var sorted = new List<KeyValuePair<string, int>>(names);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        sb.AppendLine("Loaded-scene components inspected: " +
            inspected + " / 30000");
        sb.AppendLine("Potential class names: " + sorted.Count);
        for (int i = 0; i < Math.Min(sorted.Count, 55); i++)
            sb.AppendLine("  " + sorted[i].Value + " x " + sorted[i].Key);
        sb.AppendLine("These are candidates, NOT proof that a class owns streaming.");
    }

    private sealed class RenderBucket
    {
        public int Total;
        public int Active;
        public int Enabled;
        public int Both;
        public int StaticTotal;
        public int StaticDrawable;
        public int InvalidBounds;

        public void Record(Renderer renderer, GameObject go)
        {
            Total++;
            bool active = go.activeInHierarchy;
            bool enabled = renderer.enabled;
            if (active) Active++;
            if (enabled) Enabled++;
            if (active && enabled) Both++;
            if (go.isStatic)
            {
                StaticTotal++;
                if (active && enabled) StaticDrawable++;
            }
        }
        public string Format(string label) =>
            label + ": total=" + Total +
            " active=" + Active +
            " rendererEnabled=" + Enabled +
            " drawable=" + Both +
            " staticSceneMeshes=" + StaticTotal +
            " staticDrawable=" + StaticDrawable +
            " invalidBounds=" + InvalidBounds;
    }

    /// <summary>
    /// Manual, bounded read-only survey. Counts ONLY scene renderers and does
    /// not report world coordinates, remote-player positions or object names.
    /// Compares a snapshot near the avatar with the same snapshot far away
    /// to separate missing-scene content from camera culling.
    /// </summary>
    private void CaptureCinematicRendererSurvey()
    {
        if (_mode != CameraMode.Drone || _desktop == null)
            return;
        try
        {
            Camera? eye = FindVRCamera();
            Vector3 drone = _desktop.transform.position;
            float distance = eye == null ? -1f
                : Vector3.Distance(drone, eye.transform.position);
            var near = new RenderBucket();
            var medium = new RenderBucket();
            var far = new RenderBucket();
            var playerNear = new RenderBucket();
            var playerMedium = new RenderBucket();
            var playerFar = new RenderBucket();
            Vector3 playerEyePosition = eye != null
                ? eye.transform.position : Vector3.zero;
            int visited = 0, sceneRenderable = 0, beyond = 0, excluded = 0;
            foreach (Renderer renderer in
                Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null) continue;
                GameObject go = renderer.gameObject;
                if (go == null) continue;
                Scene scene = go.scene;
                if (!scene.IsValid() || !scene.isLoaded ||
                    scene.name == "DontDestroyOnLoad")
                    continue;
                // The source object collection includes unloaded prefab
                // assets. Count only ACTUAL loaded scene renderers so it
                // doesn't exhaust the scan budget before reaching the zone.
                if (++visited > 60000) break;
                // Avoid collecting local/remote player objects, enemies,
                // nametags and any server gameplay entity details.
                string layerName = LayerMask.LayerToName(go.layer);
                if (layerName == "Player" || layerName == "Enemy" ||
                    layerName == "Minicam" || layerName == "UI" ||
                    layerName == "UI-OnTop" ||
                    layerName == "NoInteract")
                {
                    excluded++;
                    continue;
                }
                sceneRenderable++;
                float d;
                try { d = Vector3.Distance(renderer.bounds.center, drone); }
                catch { continue; }
                if (d <= 75f) near.Record(renderer, go);
                else if (d <= 250f) medium.Record(renderer, go);
                else if (d <= 700f) far.Record(renderer, go);
                else beyond++;

                if (eye != null)
                {
                    float dPlayer = Vector3.Distance(
                        renderer.bounds.center, playerEyePosition);
                    if (dPlayer <= 75f) playerNear.Record(renderer, go);
                    else if (dPlayer <= 250f)
                        playerMedium.Record(renderer, go);
                    else if (dPlayer <= 700f)
                        playerFar.Record(renderer, go);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("REBORNCAM CINEMATIC SCENERY REPORT v0.9.8");
            sb.AppendLine("Read-only renderer presence/culling inventory.");
            sb.AppendLine("No player names, object paths or world coordinates exported.");
            sb.AppendLine("Loaded scene names are included to distinguish actual world transitions.");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Active Scene: " +
                SceneManager.GetActiveScene().name);
            sb.AppendLine("Loaded scenes:");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) sb.AppendLine("  " + scene.name);
            }
            sb.AppendLine("Drone distance from real player: " +
                (distance < 0 ? "eye unavailable"
                    : distance.ToString("0.0") + "m"));
            sb.AppendLine("Desktop farClipPlane=" +
                _desktop.farClipPlane.ToString("0") +
                " useOcclusionCulling=" +
                _desktop.useOcclusionCulling);
            sb.AppendLine("Desktop layer-range override=" +
                _layerCullOverrideApplied);
            sb.AppendLine("Extended rendering enabled=" +
                Plugin.ExtendedRenderEnabled.Value);
            if (_source != null)
                sb.AppendLine("Source VR camera farClipPlane=" +
                    _source.farClipPlane.ToString("0") +
                    " useOcclusionCulling=" +
                    _source.useOcclusionCulling);
            sb.AppendLine("Scanned renderers=" + visited +
                ", eligible zone renderers=" + sceneRenderable +
                ", non-scenery/excluded=" + excluded +
                ", beyond 700m=" + beyond);
            sb.AppendLine(near.Format("Camera 0-75m"));
            sb.AppendLine(medium.Format("Camera 75-250m"));
            sb.AppendLine(far.Format("Camera 250-700m"));
            if (eye != null)
            {
                sb.AppendLine("CONTROL: RENDERERS AROUND THE REAL PLAYER (SAME SNAPSHOT)");
                sb.AppendLine(playerNear.Format("Player 0-75m"));
                sb.AppendLine(playerMedium.Format("Player 75-250m"));
                sb.AppendLine(playerFar.Format("Player 250-700m"));
            }
            sb.AppendLine();
            AppendLoadingClassCandidates(sb);
            sb.AppendLine();
            sb.AppendLine("INTERPRETATION:");
            sb.AppendLine("- Many drawable objects but empty picture: investigate camera layers, fog, LOD or shaders.");
            sb.AppendLine("- Compare camera vs player counts in the SAME snapshot; fewer objects near the camera supports local player-centred scene loading.");
            sb.AppendLine("- Few/no nearby scene renderers far from avatar: geometry may be unloaded, disabled, or never spawned.");
            sb.AppendLine("- Camera settings only affect objects already present in memory.");
            sb.AppendLine("- This report never requests terrain, remote players or network streaming.");

            string folder = Path.Combine(BepInEx.Paths.ConfigPath,
                "RebornCam", "StreamingReports");
            Directory.CreateDirectory(folder);
            string filename = Path.Combine(folder, "Scenery_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
            File.WriteAllText(filename, sb.ToString(), Encoding.UTF8);
            Plugin.ModLogger.LogInfo(
                "[CinematicRender] Scenery report saved: " + filename);
            CinematicRenderStatus = "Scenery report saved: " +
                Path.GetFileName(filename);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[CinematicRender] Scenery report error: " + ex);
        }
    }
}
