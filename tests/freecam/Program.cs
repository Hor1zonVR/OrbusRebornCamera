using System;
using System.IO;
using UnityEngine;

namespace BepInEx
{
    public static class Paths
    {
        public static string ConfigPath = "";
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name;
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() =>
            new Scene { name = "UnitTestWorld" };
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z)
        {
            this.x = x; this.y = y; this.z = z;
        }
        public static Vector3 zero => new(0f, 0f, 0f);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
            new(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t),
                Mathf.Lerp(a.z, b.z, t));
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x; this.y = y; this.z = z; this.w = w;
        }
        public Vector3 eulerAngles => new(0f, 0f, 0f);
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) =>
            new(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t),
                Mathf.Lerp(a.z, b.z, t), Mathf.Lerp(a.w, b.w, t));
    }

    public sealed class Transform
    {
        public Vector3 position;
        public Quaternion rotation = new(0f, 0f, 0f, 1f);
    }

    public sealed class Camera
    {
        public Transform transform = new();
    }

    public static class Mathf
    {
        public static float Clamp(float n, float min, float max) =>
            Math.Clamp(n, min, max);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp01(float n) => Math.Clamp(n, 0f, 1f);
        public static int FloorToInt(float n) => (int)Math.Floor(n);
        public static float Lerp(float a, float b, float t) =>
            a + (b - a) * Clamp01(t);
        public static float SmoothStep(float a, float b, float t)
        {
            t = Clamp01(t);
            return Lerp(a, b, t * t * (3f - 2f * t));
        }
        public static float DeltaAngle(float a, float b) =>
            ((b - a + 180f) % 360f + 360f) % 360f - 180f;
    }
}

namespace OrbusBetterMirror
{
    public enum TestMode { POV, Drone }

    public class Setting<T>
    {
        public T Value;
        public Setting(T value) { Value = value; }
    }

    public class TestLogger
    {
        public void LogInfo(string s) => Console.WriteLine(s);
        public void LogWarning(string s) => Console.WriteLine(s);
    }

    public static class Plugin
    {
        public static Setting<float> KeyframeDuration = new(12f);
        public static TestLogger ModLogger = new();
    }

    public sealed partial class CameraDiagnostics
    {
        private enum CameraMode { POV, Drone }

        private CameraMode _mode = CameraMode.POV;
        private Camera? _desktop = new();
        private bool IsCameraActive => _desktop != null;
        private Vector3 _droneVelocity;
        private Vector3 _dronePosition;
        private float _droneYaw;
        private float _dronePitch;

        private bool EnterFreecam()
        {
            _mode = CameraMode.Drone;
            return true;
        }
        private void ReleaseDronePointer() { }
        private void ApplyFov() { }
        private float GetHorizontalFov() => 110f;

        public void Move(float x)
        {
            if (_desktop != null)
                _desktop.transform.position = new Vector3(x, 3f, 0f);
        }
        public void Add() => AddKeyframe();
        public int Count => _keyframes.Count;
        public void Play() => StartKeyframePlayback();
        public void Tick(float seconds) => UpdateKeyframePlayback(seconds);
        public float X => _dronePosition.x;
        public bool Playing => _playingKeyframes;
        public void Stop() => StopKeyframePlayback();
        public void Load() => LoadKeyframes();
        public void Undo() => RemoveLastKeyframe();
    }
}

internal static class Program
{
    public static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "reborn-keyframes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        BepInEx.Paths.ConfigPath = dir;

        try
        {
            var camera = new OrbusBetterMirror.CameraDiagnostics();
            camera.Move(2f);
            camera.Add();
            camera.Move(12f);
            camera.Add();

            if (camera.Count != 2)
                throw new Exception("Expected two saved camera shots.");

            string file = Path.Combine(dir, "RebornCamKeyframes.txt");
            if (!File.Exists(file) ||
                !File.ReadAllText(file).Contains("# scene=UnitTestWorld"))
                throw new Exception("Path was not persisted with its world.");

            camera.Play();
            camera.Tick(6f);
            if (!camera.Playing ||
                camera.X <= 2f || camera.X >= 12f)
                throw new Exception("Midpoint camera interpolation failed.");
            camera.Tick(6f);
            if (camera.Playing || Math.Abs(camera.X - 12f) > 0.01f)
                throw new Exception("Path did not finish at last shot.");

            var reloaded = new OrbusBetterMirror.CameraDiagnostics();
            reloaded.Load();
            if (reloaded.Count != 2)
                throw new Exception("Saved shots were not loaded.");
            reloaded.Undo();
            if (reloaded.Count != 1)
                throw new Exception("Undo did not remove last shot.");

            Console.WriteLine(
                "PASS: Freecam keyframes compiled, persisted, loaded, " +
                "interpolated, finished and supported undo.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
