using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Desktop-only clean HUD. Target JUST the native level text and XP slider,
/// not TopHud.GetComponentsInChildren: TopHud's hierarchy can include avatar
/// renderers and hiding it made the player disappear in v0.9.29.
/// Community dump: PlayerCharacter.myPlayerAvatar.topHud has fields
/// levelLabel (SuperTextMesh) and xpBar (UnityEngine.UI.Slider).
/// QuestHelper verified obfuscated chain: ceh.blfr/betm/bebt/bhch.
/// </summary>
public sealed class DesktopLevelHudFilter : MonoBehaviour
{
    public DesktopLevelHudFilter(IntPtr pointer) : base(pointer) { }

    private const BindingFlags Flags = BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // Only renderer components attached to the two specific visual objects.
    // Never crawl TopHud as a whole or disable its GameObject.
    private readonly List<Renderer> _renderers = new();
    private readonly List<Renderer> _hidden = new();
    private readonly List<(Component canvas, float alpha)> _hiddenCanvas = new();
    private Component? _label;
    private Component? _xp;
    private float _nextResolve;
    private bool _reported;

    internal CameraDiagnostics? Owner;

    private static Type? FindType(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = a.GetType(name, false);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static object? Read(object? obj, string name)
    {
        if (obj == null) return null;
        bool isStatic = obj is Type;
        Type t = isStatic ? (Type)obj : obj.GetType();
        object? target = isStatic ? null : obj;
        try
        {
            PropertyInfo? p = t.GetProperty(name, Flags);
            if (p != null) return p.GetValue(target);
            FieldInfo? f = t.GetField(name, Flags);
            if (f != null) return f.GetValue(target);
        }
        catch { }
        return null;
    }

    private static object? Either(object? obj, string one, string two) =>
        Read(obj, one) ?? Read(obj, two);

    private static bool IsSliderType(Type? t)
    {
        for (; t != null; t = t.BaseType)
            if (t.FullName == "UnityEngine.UI.Slider") return true;
        return false;
    }

    private static Component? FindSlider(object? hud)
    {
        Component? slider = Read(hud, "xpBar") as Component;
        if (slider != null || hud == null) return slider;
        try
        {
            foreach (FieldInfo field in hud.GetType().GetFields(Flags))
                if (IsSliderType(field.FieldType))
                    return field.GetValue(hud) as Component;
        }
        catch { }
        return null;
    }

    private static bool IsInside(Transform candidate, Transform container)
    {
        Transform? t = candidate;
        for (int i = 0; t != null && i < 12; i++, t = t.parent)
            if (t == container) return true;
        return false;
    }

    private static void CollectRenderers(Component? root, List<Renderer> dest,
        bool searchChildren)
    {
        if (root == null || root.gameObject == null ||
            !root.gameObject.activeInHierarchy) return;
        Renderer? own = root.gameObject.GetComponent<Renderer>();
        if (own != null && !dest.Contains(own)) dest.Add(own);

        if (!searchChildren) return;
        Renderer[] children = root.gameObject.GetComponentsInChildren<Renderer>(true);
        if (children == null || children.Length > 12) return;
        foreach (Renderer r in children)
        {
            if (r != null && r.transform != null &&
                IsInside(r.transform, root.transform) && !dest.Contains(r))
                dest.Add(r);
        }
    }

    private void ResolveHud()
    {
        _label = null;
        _xp = null;
        _renderers.Clear();
        try
        {
            Type? type = FindType("MagicalActual.PlayerCharacter");
            object? player = Read(type, "Instance");
            if (player == null)
                player = Read(FindType("ceh"), "blfr");
            object? avatar = Either(player, "myPlayerAvatar", "betm");
            object? hud = Either(avatar, "topHud", "bebt");
            _label = Either(hud, "levelLabel", "bhch") as Component;
            _xp = FindSlider(hud);

            // LevelLabel is a SuperTextMesh renderer. The XP Slider is a
            // canvas UI element and often has no MeshRenderer at all.
            CollectRenderers(_label, _renderers, false);
            CollectRenderers(_xp, _renderers, false);

            if (!_reported && _label != null)
            {
                _reported = true;
                Plugin.ModLogger.LogInfo(
                    "[CleanHUD] Exact HUD targets found: level=" +
                    (_label != null) + ",xpBar=" + (_xp != null) +
                    ",renderers=" + _renderers.Count +
                    ". Never hiding TopHud/player root.");
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[CleanHUD] Exact HUD lookup failed: " + ex.Message);
        }
    }

    private void SetCanvasAlpha(Component? root, bool hide)
    {
        if (root == null || root.gameObject == null ||
            !root.gameObject.activeInHierarchy) return;
        // Slider descendants are CanvasRenderer-based uGUI Graphics.
        // Unlike altering the entire TopHud, only the XP subtree is touched.
        Component[] components = root.gameObject.GetComponentsInChildren<Component>(true);
        if (components == null || components.Length > 48) return;
        foreach (Component node in components)
        {
            if (node == null || node.GetType().FullName != "UnityEngine.CanvasRenderer")
                continue;
            try
            {
                Type t = node.GetType();
                MethodInfo? get = t.GetMethod("GetAlpha", Flags);
                MethodInfo? set = t.GetMethod("SetAlpha", Flags);
                if (get == null || set == null) continue;
                object? value = get.Invoke(node, null);
                if (value == null) continue;
                float original = Convert.ToSingle(value);
                _hiddenCanvas.Add((node, original));
                set.Invoke(node, new object[] { 0f });
            }
            catch { }
        }
    }

    private void OnPreCull()
    {
        Restore();
        if (Owner == null || !Owner.ShouldSuppressDesktopLevelHud)
            return;
        try
        {
            if (_label == null || Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + 1.5f;
                ResolveHud();
            }
            foreach (Renderer r in _renderers)
            {
                if (r == null || !r.enabled) continue;
                _hidden.Add(r);
                r.enabled = false;
            }
            SetCanvasAlpha(_xp, true);
        }
        catch (Exception ex)
        {
            Restore();
            Plugin.ModLogger.LogWarning(
                "[CleanHUD] Failed to hide exact HUD targets: " + ex.Message);
        }
    }

    private void OnPostRender() => Restore();
    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    internal void Restore()
    {
        for (int i = _hidden.Count - 1; i >= 0; i--)
        {
            try
            {
                Renderer r = _hidden[i];
                if (r != null) r.enabled = true;
            }
            catch { }
        }
        _hidden.Clear();
        for (int i = _hiddenCanvas.Count - 1; i >= 0; i--)
        {
            try
            {
                var item = _hiddenCanvas[i];
                if (item.canvas == null) continue;
                MethodInfo? set = item.canvas.GetType().GetMethod("SetAlpha", Flags);
                set?.Invoke(item.canvas, new object[] { item.alpha });
            }
            catch { }
        }
        _hiddenCanvas.Clear();
    }
}
