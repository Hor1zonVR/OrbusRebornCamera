using System;
using System.IO;
using OrbusBetterMirror;

internal static class OfflineInventorySmokeTest
{
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAILED: " + name);
    }

    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "rc-world-test-" + Guid.NewGuid().ToString("N"));
        string streaming = Path.Combine(root, "StreamingAssets");
        string dungeon = Path.Combine(streaming, "Zones", "Dungeon");
        try
        {
            Directory.CreateDirectory(dungeon);
            File.WriteAllText(Path.Combine(root, "globalgamemanagers"), "fake");
            File.WriteAllText(Path.Combine(root, "sharedassets0.assets"), "fake");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "ignore");
            File.WriteAllText(Path.Combine(dungeon, "Highsteppe.bundle"), "fake");
            File.WriteAllText(Path.Combine(dungeon, "npc.dat"), "ignore");

            string report = OfflineWorldInventory.BuildReport(
                root, streaming, "BootstrapWeathermaker\nZoneHighsteppe");
            Check(report.Contains("ZoneHighsteppe"), "observed scenes included");
            Check(report.Contains("globalgamemanagers"), "Unity main assets included");
            Check(report.Contains("sharedassets0.assets"), "shared asset included");
            Check(report.Contains("Highsteppe.bundle"), "nested streaming asset included");
            Check(!report.Contains("notes.txt"), "ordinary text ignored");
            Check(!report.Contains("npc.dat"), "arbitrary binary ignored");
            Check(report.Contains("READ ONLY"), "audit mode labelled");
            Check(File.Exists(Path.Combine(dungeon, "Highsteppe.bundle")),
                "original assets unchanged");
            Console.WriteLine("Offline inventory tests passed.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
