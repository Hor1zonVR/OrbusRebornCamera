// Deliberately minimal build-time stand-ins for required BepInEx/Unity types.
// These do not test game interop, tracking, rendering or in-game behaviour.
// They let public CI compile the new recording source without game binaries.
using System;

namespace BepInEx
{
    public static class Paths { public static string ConfigPath => "/tmp/rc-test"; }
}

namespace UnityEngine
{
    public class MonoBehaviour { public MonoBehaviour(IntPtr pointer) { } }
    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
    }
    public class GUIStyle { }
    public struct Color { }
    public struct Vector3
    {
        public float x, y, z;
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => default;
        public static Quaternion Inverse(Quaternion q) => default;
        public static float Angle(Quaternion a, Quaternion b) => 0f;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
        public static Vector3 operator *(Quaternion a, Vector3 b) => default;
    }
    public class Transform
    {
        public Vector3 position => default;
        public Quaternion rotation => default;
    }
    public class Camera
    {
        public static Camera[] allCameras => Array.Empty<Camera>();
        public bool isActiveAndEnabled => true;
        public bool stereoEnabled => true;
        public string name => "Camera (eye)";
        public Transform transform => new Transform();
    }
    public static class Application { public static bool isFocused => true; }
    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float unscaledTime => 0f;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a,b);
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name => "stub-scene"; }
    public static class SceneManager
    {
        public static Scene GetActiveScene() => new Scene();
    }
}

namespace UnityEngine.XR
{
    public enum XRNode { Head, LeftHand, RightHand }
    public static class XRSettings
    {
        public static bool enabled => false;
        public static bool isDeviceActive => false;
    }
    public static class InputTracking
    {
        public static UnityEngine.Vector3 GetLocalPosition(XRNode node) => default;
        public static UnityEngine.Quaternion GetLocalRotation(XRNode node) => default;
    }
}

namespace OrbusBetterMirror
{
    internal sealed class TestConfig<T>
    {
        internal T Value { get; set; }
        public TestConfig(T value) { Value = value; }
    }

    internal sealed class TestLogger
    {
        internal void LogInfo(object value) { }
        internal void LogWarning(object value) { }
        internal void LogError(object value) { }
    }

    internal static class Plugin
    {
        internal static TestConfig<bool> ReplayBufferEnabled = new(true);
        internal static TestConfig<int> ReplayBufferSeconds = new(300);
        internal static TestLogger ModLogger = new();
    }

    public sealed partial class CameraDiagnostics
    {
        private readonly UnityEngine.Color StudioSurface = default;
        private readonly UnityEngine.Color StudioStroke = default;
        private readonly UnityEngine.GUIStyle _studioHeading = new();
        private readonly UnityEngine.GUIStyle _studioMuted = new();
        private void StudioSection(float x, float y, float w, string title) { }
        private void StudioFill(UnityEngine.Rect rect, UnityEngine.Color color) { }
        private void StudioBorder(UnityEngine.Rect rect, UnityEngine.Color color) { }
        private void StudioText(UnityEngine.Rect rect, string value, UnityEngine.GUIStyle? style) { }
        private bool StudioButton(UnityEngine.Rect rect, string text,
                                  bool selected, bool accent = false) => false;
    }
}

internal static class IntegrationCompileMain
{
    public static void Main() => Console.WriteLine(
        "Replay recorder + Recording-tab sources compile against minimal stubs.");
}
