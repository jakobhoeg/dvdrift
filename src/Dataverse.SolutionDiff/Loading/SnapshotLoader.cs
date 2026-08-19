using System.IO.Compression;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Loading;

/// <summary>
/// Loads a snapshot into an ordered list of raw files. Accepts a folder (raw
/// zip-extract or pac-unpacked layout — the extractor handles both) or a solution
/// .zip, which is read directly without an unpack step. Container zips/folders that
/// hold several solution zips (e.g. a "one zip per solution" export bundle) are
/// expanded recursively: each nested solution zip becomes a path prefix.
/// </summary>
public static class SnapshotLoader
{
    public static IReadOnlyList<RawFile> Load(string path)
    {
        List<RawFile> files;
        if (Directory.Exists(path))
        {
            files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(f => new RawFile(Normalize(Path.GetRelativePath(path, f)), File.ReadAllBytes(f)))
                .ToList();
        }
        else if (File.Exists(path))
        {
            try
            {
                using var archive = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
                files = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .Select(e => new RawFile(Normalize(e.FullName), ReadAll(e)))
                    .ToList();
            }
            catch (InvalidDataException)
            {
                throw new DiffException($"'{path}' is not a valid solution zip.");
            }
        }
        else
        {
            throw new DiffException($"Input '{path}' does not exist or is neither a folder nor a .zip file.");
        }

        return ExpandNestedSolutions(files, prefix: string.Empty)
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Replaces every .zip entry that itself looks like a solution (contains
    /// solution.xml or customizations.xml) with its contents, prefixed by the zip's
    /// path. Non-solution zips (e.g. plugin packages) are kept as binary files.
    /// </summary>
    private static IEnumerable<RawFile> ExpandNestedSolutions(List<RawFile> files, string prefix)
    {
        foreach (var file in files)
        {
            if (!file.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                yield return new RawFile(prefix + file.Path, file.Content);
                continue;
            }

            var inner = TryReadSolutionZip(file.Content);
            if (inner is null)
            {
                yield return new RawFile(prefix + file.Path, file.Content);
                continue;
            }

            var nestedPrefix = prefix + file.Path[..^4] + "/";
            foreach (var expanded in ExpandNestedSolutions(inner, nestedPrefix))
            {
                yield return expanded;
            }
        }
    }

    private static List<RawFile>? TryReadSolutionZip(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            var looksLikeSolution = archive.Entries.Any(e =>
                string.Equals(e.Name, "solution.xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Name, "customizations.xml", StringComparison.OrdinalIgnoreCase));
            if (!looksLikeSolution)
            {
                return null;
            }

            return archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => new RawFile(Normalize(e.FullName), ReadAll(e)))
                .ToList();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
