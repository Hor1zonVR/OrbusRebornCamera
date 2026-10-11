using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OrbusBetterMirror;

/// <summary>
/// Automatically scans the INSTALLED local game files for the rare Trickster
/// Goblin's asset identifier. Safe to run while playing: all disk reads are
/// on a background worker, and the game world/camera/network are untouched.
/// No Python, packages, hotkeys, or live goblin spawn required.
/// </summary>
public sealed class TricksterDiskDiagnostics : MonoBehaviour
{
    public TricksterDiskDiagnostics(IntPtr pointer) : base(pointer) { }

    private const int ReadSize = 1024 * 1024;
    private const int Overlap = 128;
    private const int MaxMatchesPerFile = 16;
    private const int MaxReportMatches = 300;
    private const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;
    private const long MaxSingleFileBytes = 2L * 1024 * 1024 * 1024;

    private static readonly string[] Terms =
    {
        "treasuregoblin",
        "treasure goblin",
        "trickstergoblin",
        "trickster goblin",
        "goblinstore",
        "trickster",
        "goblin"
    };

    private static readonly string[] IgnoreExtensions =
    {
        ".png", ".jpg", ".jpeg", ".dds", ".webp", ".mp3", ".ogg",
        ".wav", ".mp4", ".avi", ".webm", ".log", ".tmp"
    };

    private static readonly (string Term, string Encoding, byte[] Needle)[] Needles =
        Terms.SelectMany(term => new[]
        {
            (term, "ASCII", Encoding.ASCII.GetBytes(term)),
            (term, "UTF16LE", Encoding.Unicode.GetBytes(term))
        }).ToArray();

    private bool _started;
    private float _delay;
    private Task<ScanResult>? _worker;
    private string? _reportPath;

    private sealed class ScanResult
    {
        public int ScannedFiles;
        public long ScannedBytes;
        public int TotalMatches;
        public int ReportedMatches;
        public List<string> Matches = new();
        public List<string> Warnings = new();
    }

    private void Update()
    {
        if (!Plugin.AutoScanTricksterFiles.Value || _started)
            return;

        // Don't compete with scene loading: start a short while after launch.
        _delay += Time.unscaledDeltaTime;
        if (_delay < 25f)
            return;

        _started = true;
        try
        {
            string dataPath = Application.dataPath;
            if (!Directory.Exists(dataPath))
            {
                Plugin.ModLogger.LogWarning(
                    "[TricksterDisk] Unity data folder unavailable; scan skipped.");
                return;
            }

            string gameRoot = Path.GetDirectoryName(
                Path.GetFullPath(dataPath))!;
            if (!File.Exists(Path.Combine(gameRoot, "GameAssembly.dll")))
            {
                Plugin.ModLogger.LogWarning(
                    "[TricksterDisk] Not an IL2CPP Orbus client; scan skipped.");
                return;
            }

            string outputFolder = Path.Combine(gameRoot, "BepInEx");
            Directory.CreateDirectory(outputFolder);
            _reportPath = Path.Combine(outputFolder, "TricksterAssetReport.txt");
            if (File.Exists(_reportPath))
            {
                Plugin.ModLogger.LogInfo(
                    "[TricksterDisk] Previous report exists: " + _reportPath +
                    " (delete it to request a fresh scan).");
                return;
            }

            Plugin.ModLogger.LogInfo(
                "[TricksterDisk] Started background read-only asset scan. " +
                "Keep playing normally; report will appear in BepInEx.");
            string captureData = dataPath;
            string captureRoot = gameRoot;
            string captureOutput = _reportPath;
            _worker = Task.Run(() => Scan(captureRoot, captureData, captureOutput));
            _ = LogWhenDone(_worker, captureOutput);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "[TricksterDisk] Could not begin asset scan: " + ex.Message);
        }
    }

    private static async Task LogWhenDone(Task<ScanResult> task, string reportPath)
    {
        try
        {
            var result = await task;
            Plugin.ModLogger.LogInfo(
                "[TricksterDisk] Scan complete: " + result.ScannedFiles +
                " files, " + result.TotalMatches + " candidate matches. " +
                "Report: " + reportPath);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                "[TricksterDisk] Scan failed: " + ex.Message);
        }
    }

    private static ScanResult Scan(string gameRoot, string dataPath, string output)
    {
        var result = new ScanResult();
        try
        {
            IEnumerable<string> files = Directory.EnumerateFiles(
                dataPath, "*", SearchOption.AllDirectories);

            // The IL2CPP binary sometimes contains gameplay identifiers too.
            var allFiles = files.Concat(new[]
            {
                Path.Combine(gameRoot, "GameAssembly.dll")
            });

            foreach (string path in allFiles)
            {
                try
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (IgnoreExtensions.Contains(extension))
                        continue;

                    var file = new FileInfo(path);
                    if (file.Length > MaxSingleFileBytes ||
                        result.ScannedBytes + file.Length > MaxTotalBytes)
                    {
                        AddWarning(result, "Skipped oversized file: " +
                            Relative(gameRoot, path));
                        continue;
                    }

                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    ScanOneFile(path, gameRoot, result);
                    result.ScannedFiles++;
                    result.ScannedBytes += file.Length;
                }
                catch (Exception ex)
                {
                    AddWarning(result,
                        "Could not read " + Relative(gameRoot, path) +
                        ": " + ex.GetType().Name);
                }
            }
        }
        catch (Exception ex)
        {
            AddWarning(result,
                "File enumeration stopped: " + ex.GetType().Name +
                " - " + ex.Message);
        }

        WriteReport(result, output);
        return result;
    }

    private static void AddWarning(ScanResult result, string message)
    {
        if (result.Warnings.Count < 80)
            result.Warnings.Add(message);
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static byte AsciiLower(byte value) =>
        value >= (byte)'A' && value <= (byte)'Z'
            ? (byte)(value + 32)
            : value;

    private static bool Matches(byte[] buffer, int offset, int count, byte[] needle)
    {
        if (offset < 0 || offset + needle.Length > count)
            return false;

        for (int i = 0; i < needle.Length; i++)
        {
            if (AsciiLower(buffer[offset + i]) != needle[i])
                return false;
        }

        return true;
    }

    private static void ScanOneFile(
        string path, string gameRoot, ScanResult result)
    {
        string relative = Relative(gameRoot, path);
        var buffer = new byte[ReadSize + Overlap];
        int carry = 0;
        long position = 0;
        int fileMatches = 0;

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            ReadSize, FileOptions.SequentialScan);

        while (fileMatches < MaxMatchesPerFile &&
               result.ReportedMatches < MaxReportMatches)
        {
            int got = stream.Read(buffer, carry, ReadSize);
            if (got == 0)
                break;

            int valid = carry + got;
            long baseOffset = position - carry;

            foreach (var (term, encoding, needle) in Needles)
            {
                int index = 0;
                while (index + needle.Length <= valid)
                {
                    // Unity often capitalizes prefab identifiers:
                    // "TreasureGoblin" must match "treasuregoblin".
                    // Look up either ASCII case for the first character.
                    int lower = Array.IndexOf(
                        buffer, needle[0], index, valid - index);
                    byte upperByte = needle[0] >= (byte)'a' &&
                                     needle[0] <= (byte)'z'
                        ? (byte)(needle[0] - 32)
                        : needle[0];
                    int upper = Array.IndexOf(
                        buffer, upperByte, index, valid - index);
                    int found = lower < 0
                        ? upper
                        : upper < 0 ? lower : Math.Min(lower, upper);
                    if (found < 0 || found + needle.Length > valid)
                        break;

                    if (Matches(buffer, found, valid, needle) &&
                        // Do not report a match entirely in the carry from
                        // the previous chunk for a second time.
                        found + needle.Length > carry)
                    {
                        result.TotalMatches++;
                        fileMatches++;
                        if (result.ReportedMatches < MaxReportMatches)
                        {
                            result.Matches.Add(
                                relative + " | " + term + " | " + encoding +
                                " | offset " + (baseOffset + found));
                            result.ReportedMatches++;
                        }
                        if (fileMatches >= MaxMatchesPerFile)
                            break;
                    }
                    index = found + 1;
                }
                if (fileMatches >= MaxMatchesPerFile)
                    break;
            }

            position += got;
            carry = Math.Min(Overlap, valid);
            Buffer.BlockCopy(buffer, valid - carry, buffer, 0, carry);

            // Small cooperative pause so normal gameplay I/O has priority.
            Thread.Sleep(3);
        }
    }

    private static void WriteReport(ScanResult result, string output)
    {
        var text = new StringBuilder();
        text.AppendLine("ORBUSBORN CAMERA - TRICKSTER ASSET DIAGNOSTIC");
        text.AppendLine("Read-only local game-file scan (no Python required)");
        text.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
        text.AppendLine("Files scanned: " + result.ScannedFiles);
        text.AppendLine("Bytes scanned: " + result.ScannedBytes);
        text.AppendLine("Matches recorded: " + result.ReportedMatches);
        text.AppendLine();
        text.AppendLine("SERVER IDENTIFIERS (known):");
        text.AppendLine("treasuregoblin = rare Trickster Goblin enemy");
        text.AppendLine("goblinstore = separate Trickster Goblin Store vendor");
        text.AppendLine();
        text.AppendLine("POTENTIAL CLIENT ASSET IDENTIFIERS:");
        foreach (string match in result.Matches)
            text.AppendLine(match);
        if (result.Matches.Count == 0)
            text.AppendLine("(No uncompressed name matches found.)");
        text.AppendLine();
        text.AppendLine("WARNINGS:");
        foreach (string warning in result.Warnings)
            text.AppendLine(warning);
        if (result.Warnings.Count == 0)
            text.AppendLine("(none)");
        text.AppendLine();
        text.AppendLine("Limitations: This scan searches raw ASCII and UTF-16LE");
        text.AppendLine("strings in local files. Compressed Unity asset bundles");
        text.AppendLine("may not expose names to this scan. Matches are leads,");
        text.AppendLine("NOT verified prefab identities or a safe camera filter.");
        text.AppendLine("No live NPC positions or coordinates were collected.");

        File.WriteAllText(output, text.ToString(), Encoding.UTF8);
    }
}
