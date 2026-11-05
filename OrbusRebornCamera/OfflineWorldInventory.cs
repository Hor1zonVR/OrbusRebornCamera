using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OrbusBetterMirror;

/// <summary>
/// Read-only, bounded inventory of potential local Unity world data.
/// No Unity APIs, asset deserialization, network requests, or game mutations.
/// Strings of loaded scenes are captured by the caller on the main thread.
/// </summary>
internal static class OfflineWorldInventory
{
    private const int MaxFiles = 1800;
    private const int MaxListedFiles = 320;
    private const int MaxDirectories = 240;
    private const int MaxDepth = 4;

    private struct PendingDirectory
    {
        internal string Path;
        internal int Depth;
        internal PendingDirectory(string path, int depth)
        { Path = path; Depth = depth; }
    }

    internal static string BuildReport(string dataPath,
        string streamingAssetsPath, string loadedScenes)
    {
        var result = new StringBuilder(48000);
        result.AppendLine("RebornCamera - LOCAL WORLD ASSET INVENTORY");
        result.AppendLine("Generated UTC: " +
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        result.AppendLine("MODE: READ ONLY - NO ASSET LOADING / SERVER CONNECTIONS");
        result.AppendLine("The inventory cannot prove that a scene can load offline.");
        result.AppendLine();
        result.AppendLine("Loaded Unity scenes when requested:");
        result.AppendLine(string.IsNullOrWhiteSpace(loadedScenes)
            ? "(none observed)" : loadedScenes);
        result.AppendLine();

        // Paths and filenames stay local to the user's machine and are
        // never submitted by the mod. No file bytes are opened.
        result.AppendLine("Data folder: " + dataPath);
        result.AppendLine("StreamingAssets folder: " + streamingAssetsPath);
        result.AppendLine();
        int total = 0, listed = 0, directories = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<PendingDirectory>();
        if (!string.IsNullOrWhiteSpace(dataPath))
            queue.Enqueue(new PendingDirectory(dataPath, 0));
        if (!string.IsNullOrWhiteSpace(streamingAssetsPath))
            queue.Enqueue(new PendingDirectory(streamingAssetsPath, 0));

        while (queue.Count > 0 && directories < MaxDirectories &&
               total < MaxFiles)
        {
            PendingDirectory item = queue.Dequeue();
            if (!seen.Add(item.Path) || !Directory.Exists(item.Path)) continue;
            directories++;
            string[] files, folders;
            try
            {
                files = Directory.GetFiles(item.Path, "*",
                    SearchOption.TopDirectoryOnly);
                folders = Directory.GetDirectories(item.Path, "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException)
            {
                result.AppendLine("SKIPPED INACCESSIBLE: " + item.Path);
                continue;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (total >= MaxFiles) break;
                total++;
                if (listed >= MaxListedFiles || !LooksLikeAsset(file))
                    continue;

                try
                {
                    var info = new FileInfo(file);
                    result.AppendLine(info.Length.ToString(
                        CultureInfo.InvariantCulture) + " bytes | " + file);
                    listed++;
                }
                catch (Exception ex) when (
                    ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.AppendLine("SKIPPED FILE: " + file);
                }
            }

            if (item.Depth >= MaxDepth) continue;
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                if (seen.Contains(folder)) continue;
                // No need to crawl the managed code / interop / cache.
                string name = Path.GetFileName(folder);
                if (name.Equals("Managed", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("il2cpp_data", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Cache", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (queue.Count < MaxDirectories)
                    queue.Enqueue(new PendingDirectory(folder, item.Depth + 1));
            }
        }

        result.AppendLine();
        result.AppendLine("Scanned " + directories + " directories, " +
            total + " files; listed " + listed + " possible asset files.");
        if (total >= MaxFiles || directories >= MaxDirectories)
            result.AppendLine("Safety limits reached; inventory may be partial.");
        result.AppendLine("NEXT: determine asset bundle / scene dependencies " +
            "from authorized local inspection before attempting to load a zone.");
        return result.ToString();
    }

    private static bool LooksLikeAsset(string path)
    {
        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.Equals("globalgamemanagers", StringComparison.OrdinalIgnoreCase)
            || name.Equals("level0", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("level", StringComparison.OrdinalIgnoreCase)
            || name.Equals("resources.assets", StringComparison.OrdinalIgnoreCase))
            return true;
        return extension == ".assets" || extension == ".ress" ||
               extension == ".resource" || extension == ".bundle" ||
               extension == ".unity3d" || extension == ".manifest" ||
               extension == ".ab" || extension == ".assetbundle";
    }
}
