using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        protected MonoBehaviour(IntPtr pointer) { }
    }

    public static class Time
    {
        public static float unscaledDeltaTime = 30f;
    }

    public static class Application
    {
        public static string dataPath = "";
    }
}

namespace OrbusBetterMirror
{
    public sealed class BoolConfig
    {
        public bool Value = true;
    }

    public sealed class TestLogger
    {
        public void LogInfo(string message) => Console.WriteLine(message);
        public void LogWarning(string message) => Console.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }

    public static class Plugin
    {
        public static BoolConfig AutoScanTricksterFiles = new();
        public static TestLogger ModLogger = new();
    }
}

internal static class Program
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "reborn-trickster-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "vrclient_Data");
        string bepinex = Path.Combine(root, "BepInEx");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(bepinex);
        File.WriteAllText(Path.Combine(root, "GameAssembly.dll"), "dummy");
        File.WriteAllText(Path.Combine(data, "resources.assets"),
            "prefix TreasureGoblin then goblinstore vendor suffix");

        try
        {
            UnityEngine.Application.dataPath = data;
            var component = new OrbusBetterMirror.TricksterDiskDiagnostics(IntPtr.Zero);
            var update = typeof(OrbusBetterMirror.TricksterDiskDiagnostics)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("Missing Update()");
            update.Invoke(component, null);
            string report = Path.Combine(bepinex, "TricksterAssetReport.txt");
            for (int i = 0; i < 200 && !File.Exists(report); i++)
                Thread.Sleep(100);

            if (!File.Exists(report))
                throw new Exception("Expected report file was never created.");

            string text = File.ReadAllText(report);
            if (!text.Contains("treasuregoblin | ASCII", StringComparison.Ordinal))
                throw new Exception("Missing rare-monster match.");
            if (!text.Contains("goblinstore | ASCII", StringComparison.Ordinal))
                throw new Exception("Missing separate vendor match.");
            if (!text.Contains("No live NPC positions or coordinates were collected.",
                    StringComparison.Ordinal))
                throw new Exception("Missing scan limitations note.");

            Console.WriteLine("PASS: scanner compiles and finds expected identifiers.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}