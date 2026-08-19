using System.Xml;
using System.Xml.Linq;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Classification;

internal sealed record ScopedSnapshotFile(RawFile File, string RelativePath);

internal sealed record SnapshotScope(string PackagingPath, string Identity, IReadOnlyList<ScopedSnapshotFile> Files)
{
    public string DisplayName => Identity.Length > 0
        ? Identity
        : PackagingPath.Length == 0 ? string.Empty : PackagingPath.Split('/')[^1];
}

internal static class SnapshotScopeDetector
{
    public static IReadOnlyList<SnapshotScope> Detect(IReadOnlyList<RawFile> files)
    {
        var markerPaths = files
            .Where(file => IsScopeMarker(file.Name))
            .Select(file => ParentPath(file.Path))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (markerPaths.Count == 0)
        {
            markerPaths.Add(string.Empty);
        }

        var scopes = files
            .Select(file => (File: file, PackagingPath: FindScope(file.Path, markerPaths)))
            .GroupBy(item => item.PackagingPath, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateScope(
                group.Key,
                group.Select(item => new ScopedSnapshotFile(item.File, RelativePath(item.File.Path, group.Key))).ToList()))
            .ToList();

        var duplicateIdentity = scopes
            .Where(scope => scope.Identity.Length > 0)
            .GroupBy(scope => scope.Identity, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any())
            ?.Key;
        if (duplicateIdentity is not null)
        {
            throw new DiffException($"Snapshot contains multiple solution scopes with UniqueName '{duplicateIdentity}'.");
        }

        return scopes;
    }

    private static SnapshotScope CreateScope(string packagingPath, IReadOnlyList<ScopedSnapshotFile> files)
    {
        var manifest = files.FirstOrDefault(file =>
            string.Equals(file.RelativePath, "solution.xml", StringComparison.OrdinalIgnoreCase));
        if (manifest is null)
        {
            return new SnapshotScope(packagingPath, string.Empty, files);
        }

        try
        {
            var uniqueName = XDocument.Parse(manifest.File.Text())
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "UniqueName", StringComparison.OrdinalIgnoreCase))
                ?.Value.Trim();
            if (string.IsNullOrEmpty(uniqueName))
            {
                throw new DiffException($"Solution manifest '{manifest.File.Path}' has no UniqueName.");
            }

            return new SnapshotScope(packagingPath, uniqueName, files);
        }
        catch (XmlException exception)
        {
            throw new DiffException($"Solution manifest '{manifest.File.Path}' is not valid XML: {exception.Message}");
        }
    }

    private static bool IsScopeMarker(string fileName) =>
        string.Equals(fileName, "solution.xml", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "customizations.xml", StringComparison.OrdinalIgnoreCase);

    private static string ParentPath(string path) =>
        path.LastIndexOf('/') is var separator && separator >= 0 ? path[..separator] : string.Empty;

    private static string FindScope(string path, IReadOnlyList<string> markerPaths) =>
        markerPaths
            .Where(scope => scope.Length == 0 ||
                path.StartsWith(scope + "/", StringComparison.Ordinal))
            .OrderByDescending(scope => scope.Length)
            .FirstOrDefault() ?? string.Empty;

    private static string RelativePath(string path, string scope) =>
        scope.Length == 0 ? path : path[(scope.Length + 1)..];
}