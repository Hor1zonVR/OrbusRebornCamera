using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Suppresses the game's own top level/XP HUD renderers for the independent
/// desktop creator camera only. The original renderers are restored straight
/// after that camera finishes; VR eye cameras retain their normal HUD.
/// 
/// Based on confirmed QuestHelper mappings for both Orbus Community and
/// Renewed/Spades:
/// PlayerCharacter.Instance -> myPlayerAvatar -> topHud -> levelLabel
/// ceh.blfr -> betm -> bebt -> bhch
/// 
/// Never disables a native GameObject, rewrites its layer or camera culling
/// mask, changes game state, or touches QuestHelper's detached tracker.
/// </summary>
public sealed class DesktopLevelHudFilter : MonoBehaviour
{
    public DesktopLevelHudFilter(IntPtr pointer) : base(pointer) { }

    private const BindingFlags Flags =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    private const int MaxHudRenderers = 64;
    private readonly List<Renderer> _suppressed = new();
    private Renderer[]? _cachedRenderers;
    private Component? _hud;
    private Component? _label;
    private float _nextResolve;
    private bool _loggedFound;
    private bool _loggedMissing;

    // Set by CameraDiagnostics when attaching this component to its
    // separate, desktop-only Camera GameObject.
    internal CameraDiagnostics? Owner;

    private static Type? FindManagedType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }

    private static object? GetMember(object? owner, string name)
    {
        if (owner == null) return null;
        bool isStatic = owner is Type;
        Type type = isStatic ? (Type)owner : owner.GetType();
        object? instance = isStatic ? null : owner;
        try
        {
            PropertyInfo? property = type.GetProperty(name, Flags);
            if (property != null) return property.GetValue(instance);
            FieldInfo? field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(instance);
            MethodInfo? getter = type.GetMethod(
                "get_" + name, Flags, null, Type.EmptyTypes, null);
            return getter?.Invoke(instance, null);
        }
        catch { return null; }
    }

    private static object? GetEither(object? owner,
        string communityName, string renewedName) =>
        GetMember(owner, communityName) ??
        GetMember(owner, renewedName);

    private void ResolveNativeHud()
    {
        _cachedRenderers = null;
        _hud = null;
        _label = null;

        try
        {
            Type? type = FindManagedType("MagicalActual.PlayerCharacter");
            object? character = GetMember(type, "Instance");
            if (character == null)
            {
                type = FindManagedType("ceh");
                character = GetMember(type, "blfr");
            }
            object? avatar = GetEither(character, "myPlayerAvatar", "betm");
            object? hud = GetEither(avatar, "topHud", "bebt");
            object? label = GetEither(hud, "levelLabel", "bhch");

            _hud = hud as Component;
            _label = label as Component;

            if (_hud == null || _label == null ||
                _hud.gameObject == null || _label.gameObject == null ||
                !_hud.gameObject.activeInHierarchy ||
                !_label.gameObject.activeInHierarchy)
                return;

            // Only the native TopHud subtree. The custom QuestHelper tracker
            // is detached from this hierarchy and is deliberately untouched.
            Renderer[] renderers =
                _hud.gameObject.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length > MaxHudRenderers)
            {
                Plugin.ModLogger.LogWarning(
                    "[CleanHUD] Native TopHud too large; no renderers changed.");
                return;
            }

            if (renderers.Length == 0)
            {
                Renderer? fallback = _label.gameObject.GetComponent<Renderer>();
                if (fallback != null) renderers = new[] { fallback };
            }

            _cachedRenderers = renderers;
            if (!_loggedFound)
            {
                _loggedFound = true;
                Plugin.ModLogger.LogInfo(
                    "[CleanHUD] Located native TopHud with " +
                    renderers.Length + " renderers. Desktop-only suppression ready.");
            }
            _loggedMissing = false;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[CleanHUD] HUD detection failed: " + ex.Message);
        }
    }

    private void OnPreCull()
    {
        // Always undo a previous incomplete render pass first.
        Restore();

        if (Owner == null || !Owner.ShouldSuppressDesktopLevelHud)
            return;

        try
        {
            if (_cachedRenderers == null || _hud == null ||
                _label == null || Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + 2f;
                ResolveNativeHud();
            }

            if (_cachedRenderers == null)
            {
                if (!_loggedMissing)
                {
                    _loggedMissing = true;
                    Plugin.ModLogger.LogInfo(
                        "[CleanHUD] Waiting for native gameplay level HUD.");
                }
                return;
            }

            // All original enabled values are restored after our camera
            // render. Do not change any other cameras, objects or layers.
            foreach (Renderer renderer in _cachedRenderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                _suppressed.Add(renderer);
                renderer.enabled = false;
            }
        }
        catch (Exception ex)
        {
            Restore();
            Plugin.ModLogger.LogWarning(
                "[CleanHUD] Render filter failed: " + ex.Message);
        }
    }

    private void OnPostRender() => Restore();
    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    internal void Restore()
    {
        for (int i = _suppressed.Count - 1; i >= 0; i--)
        {
            try
            {
                Renderer? renderer = _suppressed[i];
                if (renderer != null) renderer.enabled = true;
            }
            catch { /* Destroyed during a zone or loading transition. */ }
        }
        _suppressed.Clear();
    }
}
