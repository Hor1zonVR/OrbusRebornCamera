using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Read-only IL2CPP metadata inventory for Orbus's existing UI and input scripts.
/// Does not invoke ANY Orbus instance method or read game-instance field values.
/// Uses runtime metadata lookups from the installed Il2CppInterop.Runtime, so
/// missing API exports yield an explanatory line instead of a compile failure.
/// </summary>
internal static class OrbusUIMethodInventory
{
    private const int MaxObjects = 14000;
    private const int MaxMethodsPerClass = 48;
    private const int MaxFieldsPerClass = 30;
    private const int MaxMethodEnumerations = 400;
    private const int MaxFieldEnumerations = 180;

    private static readonly string[] Targets =
    {
        "Wacki.LaserPointerInputModule",
        "MagicalActual.VREventSystem",
        "Wacki.ViveUILaserPointer",
        "MagicalActual.LaserPointer",
        "CharacterSelect",
        "CanvasInit",
        "OrbusKeyboard",
        "MagicalActual.AppearanceOptionClicker",
        "MagicalActual.PlayerRig",
        "MagicalActual.JournalButton"
    };

    private sealed class MethodRecord
    {
        public int Priority;
        public string Text = "";
    }

    private static string Safe(string? value, int max = 130)
    {
        if (string.IsNullOrEmpty(value)) return "(unknown)";
        string result = value.Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\t', ' ').Trim();
        return result.Length > max ? result.Substring(0, max) + "..." : result;
    }

    private static string ClassFullName(IntPtr klass)
    {
        string n = IL2CPP.il2cpp_class_get_name_(klass) ?? "";
        string ns = IL2CPP.il2cpp_class_get_namespace_(klass) ?? "";
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }

    // Resolve known runtime METADATA calls by name to tolerate variation
    // between Il2CppInterop.Runtime versions and their generated exports.
    private static MethodInfo? FindMetadataCall(string symbol, int args)
    {
        foreach (MethodInfo method in typeof(IL2CPP).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if ((method.Name == symbol || method.Name == symbol + "_") &&
                method.GetParameters().Length == args)
                return method;
        }
        return null;
    }

    private static object? Call(string symbol, params object?[] args)
    {
        MethodInfo? method = FindMetadataCall(symbol, args.Length);
        if (method == null) return null;
        try
        {
            // Metadata API functions have no Unity/game-instance side effects.
            // Convert numeric index to the parameter's int/uint type if needed.
            ParameterInfo[] ps = method.GetParameters();
            for (int i = 0; i < args.Length; i++)
            {
                Type t = ps[i].ParameterType;
                if (t.IsByRef) t = t.GetElementType()!;
                if (args[i] is uint u && t == typeof(int)) args[i] = (int)u;
                else if (args[i] is int n && t == typeof(uint))
                    args[i] = (uint)n;
            }
            return method.Invoke(null, args);
        }
        catch { return null; }
    }

    private static IntPtr Pointer(object? obj) =>
        obj is IntPtr p ? p : IntPtr.Zero;

    private static string NativeName(object? obj)
    {
        if (obj is string s) return Safe(s);
        if (obj is IntPtr p && p != IntPtr.Zero)
        {
            try { return Safe(Marshal.PtrToStringAnsi(p)); }
            catch { return "(native name unavailable)"; }
        }
        return "(unknown)";
    }

    private static string TypeName(IntPtr type)
    {
        if (type == IntPtr.Zero) return "?";
        string result = NativeName(Call("il2cpp_type_get_name", type));
        return result == "(unknown)" ? "?" : result;
    }

    private static int RankMethod(string methodName)
    {
        string name = methodName.ToLowerInvariant();
        int rank = 0;
        foreach (string key in new[]
        {
            "pointer", "trigger", "click", "raycast", "ray", "laser",
            "process", "press", "release", "submit", "select",
            "interact", "input", "controller", "button", "hover",
            "touch", "connect", "menu", "activate", "disable",
            "enable", "init"
        })
        {
            if (name.Contains(key)) rank += 4;
        }
        if (name.Contains("onpointer") || name.Contains("process"))
            rank += 15;
        if (name.StartsWith("get_") || name.StartsWith("set_"))
            rank -= 4;
        return rank;
    }

    private static void MethodsForClass(StringBuilder output, IntPtr klass)
    {
        if (FindMetadataCall("il2cpp_class_get_methods", 2) == null)
        {
            output.AppendLine("  Native method enumeration unavailable in this Il2CppInterop build.");
            return;
        }
        var result = new List<MethodRecord>();
        IntPtr iterator = IntPtr.Zero;
        int total = 0;
        for (int i = 0; i < MaxMethodEnumerations; i++)
        {
            object?[] args = { klass, iterator };
            IntPtr methodPtr = Pointer(Call("il2cpp_class_get_methods", args));
            if (methodPtr == IntPtr.Zero) break;
            iterator = Pointer(args[1]);
            total++;

            string name = NativeName(Call("il2cpp_method_get_name", methodPtr));
            object? cnt = Call("il2cpp_method_get_param_count", methodPtr);
            int paramCount = cnt == null ? -1 : Convert.ToInt32(cnt);
            string returns = TypeName(Pointer(Call("il2cpp_method_get_return_type", methodPtr)));
            var sig = new StringBuilder();
            sig.Append("    ").Append(returns).Append(" ").Append(name).Append("(");
            if (paramCount >= 0 && paramCount <= 24)
            {
                for (int p = 0; p < paramCount; p++)
                {
                    if (p > 0) sig.Append(", ");
                    IntPtr param = Pointer(Call("il2cpp_method_get_param",
                        methodPtr, (uint)p));
                    sig.Append(TypeName(param));
                }
            }
            else sig.Append(paramCount == 0 ? "" : "?");
            sig.Append(")");
            result.Add(new MethodRecord
            {
                Priority = RankMethod(name),
                Text = sig.ToString()
            });
        }
        result.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        output.AppendLine("  Methods enumerated=" + total +
            " (bounded to " + MaxMethodEnumerations +
            "); top relevant signatures=" +
            Math.Min(result.Count, MaxMethodsPerClass));
        for (int i = 0; i < Math.Min(result.Count, MaxMethodsPerClass); i++)
            output.AppendLine(result[i].Text);
        if (total >= MaxMethodEnumerations)
            output.AppendLine("  TRUNCATED method enumeration.");
    }

    private static void FieldsForClass(StringBuilder output, IntPtr klass)
    {
        if (FindMetadataCall("il2cpp_class_get_fields", 2) == null)
        {
            output.AppendLine("  Native field enumeration unavailable.");
            return;
        }
        IntPtr iterator = IntPtr.Zero;
        int count = 0;
        for (int i = 0; i < MaxFieldEnumerations; i++)
        {
            object?[] args = { klass, iterator };
            IntPtr field = Pointer(Call("il2cpp_class_get_fields", args));
            if (field == IntPtr.Zero) break;
            iterator = Pointer(args[1]);
            count++;
            if (count <= MaxFieldsPerClass)
            {
                string name = NativeName(Call("il2cpp_field_get_name", field));
                string type = TypeName(Pointer(Call("il2cpp_field_get_type", field)));
                // Only metadata: field name + type. Never read its VALUE.
                output.AppendLine("    " + type + " " + name);
            }
        }
        output.AppendLine("  Fields enumerated=" + count +
            "; names shown=" + Math.Min(count, MaxFieldsPerClass) +
            " (no values read)");
    }

    public static void Append(StringBuilder output)
    {
        output.AppendLine();
        output.AppendLine("=== ORBUS NATIVE UI/INPUT CLASS METHODS AND FIELDS ===");
        output.AppendLine("Names and TYPE SIGNATURES only; NO game methods invoked.");
        output.AppendLine("Field names only, never values or credentials.");
        output.AppendLine("Use these signatures to decide whether desktop input is safe.");
        var found = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        int objects = 0;
        try
        {
            foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || ++objects > MaxObjects) break;
                GameObject go = transform.gameObject;
                if (go == null || !go.scene.IsValid() || !go.scene.isLoaded)
                    continue;
                foreach (Component component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    IntPtr klass;
                    try { klass = IL2CPP.il2cpp_object_get_class(component.Pointer); }
                    catch { continue; }
                    if (klass == IntPtr.Zero) continue;
                    string full = ClassFullName(klass);
                    if (Array.IndexOf(Targets, full) < 0 || found.ContainsKey(full))
                        continue;
                    found.Add(full, klass);
                }
                if (found.Count == Targets.Length) break;
            }
            output.AppendLine("Scanned transforms=" + objects +
                "; targeted classes located=" + found.Count +
                "/" + Targets.Length);
            foreach (string full in Targets)
            {
                output.AppendLine();
                output.AppendLine("Class: " + full +
                    (found.ContainsKey(full) ? " [found]" : " [not loaded]"));
                if (!found.TryGetValue(full, out IntPtr klass)) continue;
                try
                {
                    output.AppendLine("  Assembly: " +
                        Safe(IL2CPP.il2cpp_class_get_assemblyname_(klass)));
                    MethodsForClass(output, klass);
                    output.AppendLine("  Fields (metadata only):");
                    FieldsForClass(output, klass);
                }
                catch (Exception ex)
                {
                    output.AppendLine("  Metadata error: " + Safe(ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            output.AppendLine("Native metadata survey incomplete: " +
                Safe(ex.ToString(), 280));
        }
    }
}
