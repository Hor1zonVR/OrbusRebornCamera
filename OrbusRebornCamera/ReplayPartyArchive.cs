using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OrbusBetterMirror;

/// <summary>
/// Portable, bounded and offline-only clip transfer. Each recorder shares
/// their OWN saved clip, never network tokens or live server traffic.
/// No game/Unity dependencies. The archive contains transform data and
/// descriptive manifests, NOT game meshes, textures, audio or world assets.
/// </summary>
internal static class ReplayPartyArchive
{
    private const long MaxSourceBytes = 100L * 1024L * 1024L;
    private const long MaxArchiveBytes = 100L * 1024L * 1024L;
    private static readonly string[] Suffixes =
    {
        ".tsv", ".actors.tsv", ".rigs.bin", ".avatar.txt",
        ".actors.manifest.txt", ".visuals.txt"
    };

    internal static string PackagePath(string posePath) =>
        Path.Combine(Path.GetDirectoryName(posePath) ?? "",
            Path.GetFileNameWithoutExtension(posePath) + ".party.zip");

    internal static string Create(string posePath)
    {
        string basePath = Path.Combine(
            Path.GetDirectoryName(posePath) ?? "",
            Path.GetFileNameWithoutExtension(posePath));
        if (!Path.GetFileName(basePath).StartsWith(
            "RC_Clip_", StringComparison.Ordinal))
            throw new InvalidDataException("Not a RebornCamera clip filename.");
        if (!File.Exists(basePath + ".tsv"))
            throw new FileNotFoundException("Pose clip not saved.", basePath + ".tsv");

        long sum = 0;
        foreach (string suffix in Suffixes)
        {
            string path = basePath + suffix;
            if (File.Exists(path))
            {
                sum += new FileInfo(path).Length;
                if (sum > MaxSourceBytes)
                    throw new InvalidDataException("Clip package exceeds 100 MB.");
            }
        }

        string zipPath = PackagePath(posePath);
        string partial = zipPath + ".partial";
        try
        {
            using (var stream = new FileStream(partial, FileMode.Create,
                FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string suffix in Suffixes)
                {
                    string path = basePath + suffix;
                    if (!File.Exists(path)) continue;
                    // No absolute paths or folders are stored in the ZIP.
                    ZipArchiveEntry entry = zip.CreateEntry(
                        Path.GetFileName(path), CompressionLevel.Fastest);
                    using Stream source = File.OpenRead(path);
                    using Stream target = entry.Open();
                    source.CopyTo(target);
                }
            }
            // Existing packages are replaceable; never overwrite pose data.
            File.Move(partial, zipPath, true);
            return zipPath;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch { }
            throw;
        }
    }

    internal static string Import(string archive, string destinationRoot)
    {
        if (!File.Exists(archive))
            throw new FileNotFoundException("Partner package missing.", archive);
        if (new FileInfo(archive).Length > MaxArchiveBytes)
            throw new InvalidDataException("Partner package exceeds 100 MB.");

        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count < 1 || zip.Entries.Count > Suffixes.Length)
            throw new InvalidDataException("Unexpected partner ZIP content.");

        string baseName = "";
        long total = 0;
        int poses = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string entryName = entry.FullName;
            if (string.IsNullOrEmpty(entryName) ||
                entryName != Path.GetFileName(entryName) ||
                entryName.Contains('/') || entryName.Contains('\\') ||
                entryName.Contains("..", StringComparison.Ordinal) ||
                entry.Length < 0)
                throw new InvalidDataException("ZIP contains an unsafe path.");

            string matching = Suffixes.Where(entryName.EndsWith)
                .OrderByDescending(x => x.Length).FirstOrDefault() ?? "";
            if (matching.Length == 0)
                throw new InvalidDataException("Unknown clip package entry.");

            string candidateBase = entryName.Substring(
                0, entryName.Length - matching.Length);
            if (candidateBase.Length > 128 ||
                candidateBase.Length < 10 ||
                !candidateBase.StartsWith("RC_Clip_", StringComparison.Ordinal) ||
                candidateBase.Any(c => !(char.IsLetterOrDigit(c) ||
                    c == '_' || c == '-')))
                throw new InvalidDataException("Invalid partner clip filename.");
            if (baseName.Length != 0 && baseName != candidateBase)
                throw new InvalidDataException("Package contains multiple clips.");
            baseName = candidateBase;
            if (matching == ".tsv") poses++;
            total += entry.Length;
            if (total > MaxSourceBytes)
                throw new InvalidDataException("Uncompressed partner ZIP exceeds 100 MB.");
        }
        if (poses != 1)
            throw new InvalidDataException("Package needs exactly one main pose TSV.");

        string folder = Path.Combine(destinationRoot, baseName);
        Directory.CreateDirectory(folder);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string destination = Path.Combine(folder, entry.FullName);
            // Explicitly stays inside a dedicated import folder.
            string full = Path.GetFullPath(destination);
            string prefix = Path.GetFullPath(folder) +
                Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid ZIP extraction path.");
            // Refuse overwriting a previously imported clip.
            // It might be referenced by an ongoing replay viewer.
            if (!File.Exists(destination))
                entry.ExtractToFile(destination, false);
        }
        return Path.Combine(folder, baseName + ".tsv");
    }
}
