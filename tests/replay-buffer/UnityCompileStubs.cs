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
    public struct Color
    {
        public Color(float r, float g, float b, float a) { }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float px, float py, float pz)
        { x = px; y = py; z = pz; }
        public float sqrMagnitude => x*x + y*y + z*z;
        public Vector3 normalized => default;
        public static Vector3 up => default;
        public static Vector3 forward => default;
        public static Vector3 back => default;
        public static Vector3 right => default;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
        public static Vector3 operator *(Vector3 a, float b) => default;
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float a, float b, float c, float d)
        { x = a; y = b; z = c; w = d; }
        public Vector3 eulerAngles => default;
        public static Quaternion identity => default;
        public static Quaternion Inverse(Quaternion q) => default;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => default;
        public static Quaternion LookRotation(Vector3 a, Vector3 b) => default;
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
        public static Camera current => new Camera();
        public bool isActiveAndEnabled => true;
        public bool stereoEnabled => true;
        public string name => "Camera (eye)";
        public Transform transform => new Transform();
    }
    public class Material
    {
        public bool SetPass(int i) => true;
    }
    public struct Matrix4x4
    {
        public static Matrix4x4 identity => default;
    }
    public static class GL
    {
        public const int LINES = 1;
        public static void PushMatrix() { }
        public static void PopMatrix() { }
        public static void MultMatrix(Matrix4x4 m) { }
        public static void Begin(int mode) { }
        public static void End() { }
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
        public static float Abs(float a) => Math.Abs(a);
        public static float DeltaAngle(float a, float b) => 0f;
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name => "stub-scene";
        public bool isLoaded => true;
        public bool IsValid() => true;
    }
    public static class SceneManager
    {
        public static Scene GetActiveScene() => new Scene();
        public static int sceneCount => 1;
        public static Scene GetSceneAt(int i) => new Scene();
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
        private enum CameraMode { POV, Drone }
        private CameraMode _mode = CameraMode.Drone;
        private UnityEngine.Camera? _desktop = new();
        private bool IsCameraActive => true;
        private UnityEngine.Material? _pathPreviewMaterial = new();
        private UnityEngine.Vector3 _dronePosition;
        private UnityEngine.Vector3 _droneVelocity;
        private float _droneYaw, _dronePitch, _droneRoll;
        private void StopKeyframePlayback() { }
        private bool EnterFreecam() => true;
        private bool EnsurePathPreviewMaterial() => true;
        private static void DrawGuideLine(UnityEngine.Vector3 from,
            UnityEngine.Vector3 to, UnityEngine.Color color) { }
        private float StudioSlider(int id, float x, float y, float w,
            string title, float value, float min, float max,
            string display) => value;
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
