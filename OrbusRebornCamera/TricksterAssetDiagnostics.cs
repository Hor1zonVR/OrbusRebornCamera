using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Read-only, manually triggered Unity asset-name inspection.
/// Searches loaded objects, including prefabs which may exist without a
/// spawned Trickster Goblin. Does not enumerate coordinates or modify
/// objects, cameras, renderer state, player state, or networking.
/// </summary>
public sealed class TricksterAssetDiagnostics : MonoBehaviour
{
    public TricksterAssetDiagnostics(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int F4VirtualKey = 0x73;
    private const int CtrlVirtualKey = 0x11;
    private const int MaxResults = 100;

    // Exact words from the community server's monster and dialogue data.
    // Broad "goblin" matching here is for DIAGNOSTICS ONLY; never use a
    // substring match on "goblin" to exclude models from camera output.
    private static readonly string[] SearchWords =
    {
        "treasuregoblin",
        "treasure goblin",
        "trickstergoblin",
        "trickster goblin",
        "trickster",
        "goblinstore",
        "goblin"
    };

    private bool _hotkeyWasDown;
    private bool _scanning;

    private void Start()
    {
        Plugin.ModLogger.LogInfo(
            "[TricksterAssets] Inspector available (OFF by default). " +
            "For private development only: enable " +
            "[Diagnostics] EnableTricksterAssetScan = true " +
            "and press Ctrl+F4 to inspect loaded Unity asset names.");
    }

    private static bool KeyDown(int code) =>
        (GetAsyncKeyState(code) & 0x8000) != 0;

    private void Update()
    {
        bool chord = Application.isFocused &&
                     KeyDown(CtrlVirtualKey) && KeyDown(F4VirtualKey);

        if (chord && !_hotkeyWasDown)
        {
            if (!Plugin.EnableTricksterAssetScan.Value)
            {
                Plugin.ModLogger.LogInfo(
                    "[TricksterAssets] Read-only scan not enabled. " +
                    "Set [Diagnostics] EnableTricksterAssetScan=true " +
                    "in the BepInEx config for private tests.");
            }
            else if (!_scanning)
            {
                ScanLoadedAssets();
            }
        }

        _hotkeyWasDown = chord;
    }

    private static bool Candidate(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (string word in SearchWords)
        {
            if (name.IndexOf(word,
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string Safe(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "(unnamed)";
        return name.Replace("\r", " ").Replace("\n", " ")
            .Replace("\t", " ").Trim();
    }

    private static string ParentsOf(Transform? node)
    {
        var parents = new List<string>();
        int depth = 0;
        while (node != null && depth++ < 6)
        {
            parents.Add(Safe(node.gameObject.name));
            node = node.parent;
        }
        return string.Join(" < ", parents);
    }

    private static void ReportObjects<T>(
        string kind, T[] objects, ref int matching, ref int printed)
        where T : UnityEngine.Object
    {
        foreach (T obj in objects)
        {
            if (obj == null)
                continue;

            try
            {
                string name = Safe(obj.name);
                if (!Candidate(name))
                    continue;

                matching++;
                if (printed >= MaxResults)
                    continue;

                printed++;
                string info = "[TricksterAssets] " + kind +
                              " name=" + name;

                if (obj is GameObject go)
                {
                    // A valid scene means this is a live object; an invalid
                    // scene usually indicates an asset/prefab. Never output
                    // its world location or an NPC spawn coordinate.
                    info += " sceneObject=" + go.scene.IsValid() +
                            " hierarchy=" + ParentsOf(go.transform);
                }

                Plugin.ModLogger.LogInfo(info);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    "[TricksterAssets] Could not inspect one " +
                    kind + ": " + ex.Message);
            }
        }
    }

    private void ScanLoadedAssets()
    {
        _scanning = true;
        try
        {
            Plugin.ModLogger.LogInfo(
                "[TricksterAssets] ===== READ-ONLY ASSET SCAN START =====");
            Plugin.ModLogger.LogInfo(
                "[TricksterAssets] Keywords: TreasureGoblin, " +
                "TricksterGoblin, GoblinStore, Goblin. " +
                "No objects or renderers will be modified.");

            int matching = 0;
            int printed = 0;

            ReportObjects("GameObject",
                Resources.FindObjectsOfTypeAll<GameObject>(),
                ref matching, ref printed);
            ReportObjects("Mesh",
                Resources.FindObjectsOfTypeAll<Mesh>(),
                ref matching, ref printed);
            ReportObjects("Material",
                Resources.FindObjectsOfTypeAll<Material>(),
                ref matching, ref printed);
            ReportObjects("Texture2D",
                Resources.FindObjectsOfTypeAll<Texture2D>(),
                ref matching, ref printed);

            Plugin.ModLogger.LogInfo(
                "[TricksterAssets] Matches=" + matching +
                " Printed=" + printed +
                " Limit=" + MaxResults + ".");
            if (matching == 0)
            {
                Plugin.ModLogger.LogInfo(
                    "[TricksterAssets] No matching LOADED assets. " +
                    "This does not prove the prefab is absent: it may " +
                    "still be stored in an unloaded or packed asset bundle.");
            }
            Plugin.ModLogger.LogInfo(
                "[TricksterAssets] ===== READ-ONLY ASSET SCAN END =====");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                "[TricksterAssets] Scan failed: " + ex);
        }
        finally
        {
            _scanning = false;
        }
    }
}
