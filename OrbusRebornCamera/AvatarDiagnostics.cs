
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace OrbusBetterMirror;

public sealed class AvatarDiagnostics : MonoBehaviour
{
    public AvatarDiagnostics(IntPtr pointer) : base(pointer)
    {
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F4Key = 0x73;
    private const int ShiftKey = 0x10;

    private static readonly float[] AutomaticTimes =
    {
        8f, 25f, 60f
    };

    private static readonly string[] Keywords =
    {
        "mirror",
        "reflection",
        "reflect",
        "mannequin",
        "paperdoll",
        "characterselect",
        "character",
        "avatar",
        "player",
        "wardrobe",
        "preview",
        "portrait",
        "cosmetic",
        "equipment",
        "body",
        "head",
        "hand",
        "hair",
        "skin",
        "outfit",
        "skeleton",
        "dummy",
        "rig",
        "dressup"
    };

    private float _elapsed;
    private float _nextSceneCheck;
    private float _pendingSceneScan = -1f;
    private float _lastScanTime = -100f;

    private int _nextAutomatic;
    private int _scanNumber;

    private bool _f4WasDown;
    private bool _scanning;

    private string _lastSceneSignature = "";

    private sealed class Entry
    {
        public readonly int Score;
        public readonly string Description;

        public Entry(int score, string description)
        {
            Score = score;
            Description = description;
        }
    }

    private void Start()
    {
        _lastSceneSignature = GetSceneSignature();

        Plugin.ModLogger.LogInfo(
            "[AvatarInspector] v0.5 ready."
        );

        Plugin.ModLogger.LogInfo(
            "[AvatarInspector] Works without VR."
        );

        Plugin.ModLogger.LogInfo(
            "[AvatarInspector] F4 = full scan; " +
            "Shift+F4 = extended scan."
        );

        Plugin.ModLogger.LogInfo(
            "[AvatarInspector] Automatic scans at " +
            "8, 25 and 60 seconds, plus scene changes."
        );
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;

        // Manual diagnostics work even if XR is disabled.
        bool f4Down =
            Application.isFocused &&
            (GetAsyncKeyState(F4Key) & 0x8000) != 0;

        if (f4Down && !_f4WasDown)
        {
            bool extended =
                (GetAsyncKeyState(ShiftKey) & 0x8000) != 0;

            TakeSnapshot(
                extended ? "Shift+F4" : "F4",
                extended
            );
        }

        _f4WasDown = f4Down;

        // Detect scene changes without needing scene events.
        if (_elapsed >= _nextSceneCheck)
        {
            _nextSceneCheck = _elapsed + 1f;

            string signature = GetSceneSignature();

            if (signature != _lastSceneSignature)
            {
                Plugin.ModLogger.LogInfo(
                    "[AvatarInspector] Scene changed: " +
                    _lastSceneSignature + " -> " + signature
                );

                _lastSceneSignature = signature;

                // Give character objects time to appear.
                _pendingSceneScan = _elapsed + 4f;
            }
        }

        if (_nextAutomatic < AutomaticTimes.Length &&
            _elapsed >= AutomaticTimes[_nextAutomatic])
        {
            _nextAutomatic++;

            if (_elapsed - _lastScanTime >= 3f)
            {
                TakeSnapshot("automatic", false);
            }
        }

        if (_pendingSceneScan >= 0f &&
            _elapsed >= _pendingSceneScan)
        {
            _pendingSceneScan = -1f;

            if (_elapsed - _lastScanTime >= 3f)
            {
                TakeSnapshot("scene transition", false);
            }
        }
    }

    private static string GetSceneSignature()
    {
        try
        {
            return SceneManager.GetActiveScene().name +
                   " [" + SceneManager.sceneCount + " scenes]";
        }
        catch
        {
            return "Unknown Scene";
        }
    }

    private static bool IsSceneObject(Component component)
    {
        if (component == null)
            return false;

        GameObject go = component.gameObject;

        if (go == null)
            return false;

        try
        {
            var scene = go.scene;
            return scene.IsValid() && scene.isLoaded;
        }
        catch
        {
            return false;
        }
    }

    private static string Clean(string? value, int length = 150)
    {
        if (string.IsNullOrEmpty(value))
            return "(none)";

        string result = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        if (result.Length > length)
            result = result.Substring(0, length) + "...";

        return result;
    }

    private static string PathOf(Transform? transform)
    {
        if (transform == null)
            return "(null)";

        var names = new List<string>();
        Transform? current = transform;

        int depth = 0;

        while (current != null && depth < 9)
        {
            names.Add(Clean(current.name, 60));
            current = current.parent;
            depth++;
        }

        names.Reverse();

        string prefix = current == null ? "" : ".../";

        return prefix + string.Join("/", names);
    }

    private static int ScoreName(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return 0;

        string value = input.ToLowerInvariant();
        int score = 0;

        foreach (string keyword in Keywords)
        {
            if (value.Contains(keyword))
                score += 12;
        }

        if (value.Contains("mirror"))
            score += 35;

        if (value.Contains("mannequin"))
            score += 35;

        if (value.Contains("paperdoll"))
            score += 35;

        if (value.Contains("localplayer"))
            score += 30;

        if (value.Contains("characterselect"))
            score += 20;

        return score;
    }

    private static string LayerOf(GameObject go)
    {
        int layer = go.layer;
        string name = LayerMask.LayerToName(layer);

        return layer + ":" + Clean(name, 35);
    }

    private static string MaterialsOf(Renderer renderer, int limit = 3)
    {
        try
        {
            var materials = renderer.sharedMaterials;

            if (materials == null || materials.Length == 0)
                return "(none)";

            var result = new StringBuilder();

            int count = Math.Min(materials.Length, limit);

            for (int i = 0; i < count; i++)
            {
                var material = materials[i];

                if (i > 0)
                    result.Append(", ");

                if (material == null)
                {
                    result.Append("(null)");
                    continue;
                }

                result.Append(Clean(material.name, 45));

                if (material.shader != null)
                {
                    result.Append(" [");
                    result.Append(Clean(material.shader.name, 55));
                    result.Append("]");
                }
            }

            if (materials.Length > count)
                result.Append(", ...");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return "material read error: " + ex.Message;
        }
    }

    private static string MaskCheck(
        Renderer renderer,
        Camera? camera)
    {
        if (camera == null)
            return "n/a";

        try
        {
            bool included =
                (camera.cullingMask &
                (1 << renderer.gameObject.layer)) != 0;

            return included ? "YES" : "NO";
        }
        catch
        {
            return "error";
        }
    }

    private static string PositionOf(Vector3 position)
    {
        return "(" +
            position.x.ToString("0.00") + ", " +
            position.y.ToString("0.00") + ", " +
            position.z.ToString("0.00") + ")";
    }

    private static void PrintEntries(
        string heading,
        List<Entry> entries,
        int limit)
    {
        var log = Plugin.ModLogger;

        entries.Sort(
            (a, b) => b.Score.CompareTo(a.Score)
        );

        log.LogInfo(
            "[AvatarInspector] " + heading +
            ": " + entries.Count + " candidates"
        );

        int printed = Math.Min(limit, entries.Count);

        for (int i = 0; i < printed; i++)
        {
            log.LogInfo(
                "[AvatarInspector] " +
                heading + "[" + i + "] " +
                entries[i].Description
            );
        }

        if (entries.Count > printed)
        {
            log.LogInfo(
                "[AvatarInspector] " +
                (entries.Count - printed) +
                " additional entries omitted."
            );
        }
    }

    private void TakeSnapshot(string reason, bool extended)
    {
        if (_scanning)
            return;

        _scanning = true;
        _lastScanTime = _elapsed;
        _scanNumber++;

        var log = Plugin.ModLogger;

        try
        {
            log.LogInfo(
                "========== AVATAR SCAN #" +
                _scanNumber + " BEGIN =========="
            );

            log.LogInfo(
                "[AvatarInspector] Reason=" + reason +
                " Extended=" + extended +
                " Time=" + _elapsed.ToString("0.0") + "s"
            );

            ReportEnvironment();

            Camera? eyeCamera;
            Camera? desktopCamera;

            FindReferenceCameras(
                out eyeCamera,
                out desktopCamera
            );

            ReportCameras(extended);

            ReportSkinnedMeshes(
                eyeCamera,
                desktopCamera,
                extended
            );

            ReportNamedObjects(extended);

            if (extended)
            {
                ReportInterestingMaterials();
            }

            log.LogInfo(
                "========== AVATAR SCAN #" +
                _scanNumber + " END =========="
            );
        }
        catch (Exception ex)
        {
            log.LogError(
                "[AvatarInspector] Snapshot error: " + ex
            );
        }
        finally
        {
            _scanning = false;
        }
    }

    private static void ReportEnvironment()
    {
        var log = Plugin.ModLogger;

        log.LogInfo(
            "[AvatarInspector] Unity=" +
            Application.unityVersion +
            " Resolution=" +
            Screen.width + "x" + Screen.height
        );

        try
        {
            log.LogInfo(
                "[AvatarInspector] XR enabled=" +
                XRSettings.enabled +
                " active=" + XRSettings.isDeviceActive +
                " runtime=" + XRSettings.loadedDeviceName
            );
        }
        catch (Exception ex)
        {
            log.LogWarning(
                "[AvatarInspector] XR read failed: " +
                ex.Message
            );
        }

        try
        {
            log.LogInfo(
                "[AvatarInspector] Active scene=" +
                SceneManager.GetActiveScene().name +
                " Loaded scene count=" +
                SceneManager.sceneCount
            );

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                log.LogInfo(
                    "[AvatarInspector] Scene[" + i + "] " +
                    Clean(scene.name) +
                    " loaded=" + scene.isLoaded
                );
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(
                "[AvatarInspector] Scene list failed: " +
                ex.Message
            );
        }
    }

    private static void FindReferenceCameras(
        out Camera? eye,
        out Camera? desktop)
    {
        eye = null;
        desktop = null;

        try
        {
            var cameras = Camera.allCameras;

            foreach (var camera in cameras)
            {
                if (camera == null)
                    continue;

                if (camera.name == "Camera (eye)")
                    eye = camera;

                if (camera.name == "BetterMirror Creator Camera")
                    desktop = camera;
            }
        }
        catch
        {
            // No VR camera may exist in desktop-only mode.
        }
    }

    private static void ReportCameras(bool extended)
    {
        var log = Plugin.ModLogger;

        try
        {
            // Includes disabled loaded cameras.
            var cameras = Resources.FindObjectsOfTypeAll<Camera>();

            int sceneCount = 0;
            int printed = 0;
            int limit = extended ? 100 : 45;

            foreach (var camera in cameras)
            {
                if (camera == null || !IsSceneObject(camera))
                    continue;

                sceneCount++;

                if (printed >= limit)
                    continue;

                try
                {
                    string target =
                        camera.targetTexture == null
                        ? "Main Display / XR"
                        : Clean(camera.targetTexture.name);

                    var go = camera.gameObject;

                    log.LogInfo(
                        "[AvatarInspector] Camera[" +
                        printed + "] " +
                        PathOf(camera.transform) +
                        " enabled=" + camera.enabled +
                        " active=" + go.activeInHierarchy +
                        " FOV=" + camera.fieldOfView.ToString("0.0") +
                        " aspect=" + camera.aspect.ToString("0.000") +
                        " depth=" + camera.depth.ToString("0.0") +
                        " stereo=" + camera.stereoEnabled +
                        " eye=" + camera.stereoTargetEye +
                        " target=" + target +
                        " layerMask=0x" +
                        camera.cullingMask.ToString("X8") +
                        " pos=" +
                        PositionOf(camera.transform.position)
                    );

                    printed++;
                }
                catch (Exception ex)
                {
                    log.LogWarning(
                        "[AvatarInspector] Camera read failed: " +
                        ex.Message
                    );
                }
            }

            log.LogInfo(
                "[AvatarInspector] Scene cameras=" +
                sceneCount + " printed=" + printed
            );
        }
        catch (Exception ex)
        {
            log.LogError(
                "[AvatarInspector] Camera scan failed: " +
                ex
            );
        }
    }

    private static void ReportSkinnedMeshes(
        Camera? eye,
        Camera? desktop,
        bool extended)
    {
        var log = Plugin.ModLogger;

        try
        {
            // Includes inactive loaded skinned meshes.
            var renderers =
                Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();

            var entries = new List<Entry>();

            int sceneCount = 0;
            int enabledCount = 0;
            int disabledCount = 0;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !IsSceneObject(renderer))
                    continue;

                sceneCount++;

                try
                {
                    var go = renderer.gameObject;

                    if (renderer.enabled && go.activeInHierarchy)
                        enabledCount++;
                    else
                        disabledCount++;

                    string path = PathOf(renderer.transform);

                    var mesh = renderer.sharedMesh;

                    string meshName =
                        mesh == null
                        ? "(no mesh)"
                        : Clean(mesh.name, 65);

                    int vertexCount =
                        mesh == null ? 0 : mesh.vertexCount;

                    var bones = renderer.bones;

                    int boneCount =
                        bones == null ? 0 : bones.Length;

                    string rootBone =
                        renderer.rootBone == null
                        ? "(none)"
                        : Clean(renderer.rootBone.name, 65);

                    var bonePreview = new StringBuilder();

                    if (bones != null)
                    {
                        int sampleCount =
                            Math.Min(bones.Length, extended ? 8 : 4);

                        for (int i = 0; i < sampleCount; i++)
                        {
                            if (i > 0)
                                bonePreview.Append(",");

                            bonePreview.Append(
                                bones[i] == null
                                ? "(null)"
                                : Clean(bones[i].name, 35)
                            );
                        }
                    }

                    int score =
                        ScoreName(path) +
                        ScoreName(meshName) +
                        Math.Min(boneCount, 80);

                    string description =
                        "path=" + path +
                        " scene=" + Clean(go.scene.name, 45) +
                        " mesh=" + meshName +
                        " vertices=" + vertexCount +
                        " bones=" + boneCount +
                        " rootBone=" + rootBone +
                        " samples=[" + bonePreview + "]" +
                        " enabled=" + renderer.enabled +
                        " activeSelf=" + go.activeSelf +
                        " activeHierarchy=" + go.activeInHierarchy +
                        " layer=" + LayerOf(go) +
                        " eyeMask=" + MaskCheck(renderer, eye) +
                        " desktopMask=" + MaskCheck(renderer, desktop) +
                        " pos=" +
                        PositionOf(renderer.transform.position) +
                        " materials={" +
                        MaterialsOf(renderer) + "}";

                    entries.Add(new Entry(score, description));
                }
                catch (Exception ex)
                {
                    log.LogWarning(
                        "[AvatarInspector] Skinned mesh read error: " +
                        ex.Message
                    );
                }
            }

            log.LogInfo(
                "[AvatarInspector] Skinned meshes in scenes=" +
                sceneCount +
                " enabled/active=" + enabledCount +
                " hidden/inactive=" + disabledCount
            );

            PrintEntries(
                "SkinnedMesh",
                entries,
                extended ? 300 : 110
            );
        }
        catch (Exception ex)
        {
            log.LogError(
                "[AvatarInspector] Skinned mesh scan failed: " +
                ex
            );
        }
    }

    private static void ReportNamedObjects(bool extended)
    {
        var log = Plugin.ModLogger;

        try
        {
            var transforms =
                Resources.FindObjectsOfTypeAll<Transform>();

            var entries = new List<Entry>();

            int sceneCount = 0;

            foreach (var transform in transforms)
            {
                if (transform == null || !IsSceneObject(transform))
                    continue;

                sceneCount++;

                try
                {
                    var go = transform.gameObject;
                    string name = go.name;

                    // Check object name first to avoid formatting
                    // paths for every transform in the world.
                    int score = ScoreName(name);

                    if (score == 0)
                        continue;

                    string path = PathOf(transform);
                    score += ScoreName(path);

                    var renderer = go.GetComponent<Renderer>();
                    var camera = go.GetComponent<Camera>();

                    string rendererInfo = "";

                    if (renderer != null)
                    {
                        rendererInfo =
                            " rendererEnabled=" + renderer.enabled +
                            " materials={" +
                            MaterialsOf(renderer, extended ? 5 : 2) +
                            "}";
                    }

                    string cameraInfo =
                        camera == null
                        ? ""
                        : " hasCamera=True target=" +
                          (camera.targetTexture == null
                              ? "screen"
                              : Clean(camera.targetTexture.name));

                    entries.Add(
                        new Entry(
                            score,
                            "path=" + path +
                            " scene=" + Clean(go.scene.name, 45) +
                            " active=" + go.activeInHierarchy +
                            " layer=" + LayerOf(go) +
                            " pos=" +
                            PositionOf(transform.position) +
                            rendererInfo +
                            cameraInfo
                        )
                    );
                }
                catch (Exception ex)
                {
                    log.LogWarning(
                        "[AvatarInspector] Named object error: " +
                        ex.Message
                    );
                }
            }

            log.LogInfo(
                "[AvatarInspector] Scene transforms=" + sceneCount
            );

            PrintEntries(
                "NamedObject",
                entries,
                extended ? 250 : 85
            );
        }
        catch (Exception ex)
        {
            log.LogError(
                "[AvatarInspector] Object-name scan failed: " +
                ex
            );
        }
    }

    private static void ReportInterestingMaterials()
    {
        var log = Plugin.ModLogger;

        try
        {
            var renderers =
                Resources.FindObjectsOfTypeAll<Renderer>();

            var entries = new List<Entry>();

            int inspected = 0;
            const int MaximumInspected = 30000;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !IsSceneObject(renderer))
                    continue;

                inspected++;

                if (inspected > MaximumInspected)
                    break;

                try
                {
                    string materials = MaterialsOf(renderer, 5);
                    string lower = materials.ToLowerInvariant();

                    bool interesting =
                        lower.Contains("mirror") ||
                        lower.Contains("reflect") ||
                        lower.Contains("portal") ||
                        lower.Contains("glass") ||
                        lower.Contains("avatar") ||
                        lower.Contains("character") ||
                        lower.Contains("paperdoll");

                    if (!interesting)
                        continue;

                    var go = renderer.gameObject;

                    entries.Add(
                        new Entry(
                            100 + ScoreName(go.name),
                            "path=" + PathOf(renderer.transform) +
                            " scene=" + Clean(go.scene.name, 45) +
                            " enabled=" + renderer.enabled +
                            " active=" + go.activeInHierarchy +
                            " layer=" + LayerOf(go) +
                            " materials={" + materials + "}"
                        )
                    );
                }
                catch (Exception ex)
                {
                    log.LogWarning(
                        "[AvatarInspector] Material read error: " +
                        ex.Message
                    );
                }
            }

            log.LogInfo(
                "[AvatarInspector] Extended material scan " +
                "inspected=" + inspected
            );

            PrintEntries(
                "InterestingMaterial",
                entries,
                120
            );
        }
        catch (Exception ex)
        {
            log.LogError(
                "[AvatarInspector] Material scan failed: " + ex
            );
        }
    }
}
