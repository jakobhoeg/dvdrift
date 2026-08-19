using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Diffing;

/// <summary>
/// Matches components by key (logical-name first) and classifies Added/Modified/Deleted.
/// A matched pair whose GUID changed is Modified with <see cref="ComponentChange.IdChanged"/>
/// set — delete+recreate is reported as a modification, not a remove/add pair.
/// </summary>
public static class SnapshotDiffer
{
    public static IReadOnlyList<ComponentChange> Diff(
        IReadOnlyList<SolutionComponent> baseline,
        IReadOnlyList<SolutionComponent> current)
    {
        var baseMap = IndexByKey(baseline, "baseline", FallbackScope(baseline, current));
        var currentMap = IndexByKey(current, "current", FallbackScope(current, baseline));
        var changes = new List<ComponentChange>();

        foreach (var (key, cur) in currentMap.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!baseMap.TryGetValue(key, out var old))
            {
                changes.Add(new ComponentChange(cur.Type, cur.Name, ChangeKind.Added, false, null, cur.Id, null));
            }
            else if (!string.Equals(old.CanonicalContent, cur.CanonicalContent, StringComparison.Ordinal))
            {
                var idChanged = !string.Equals(old.Id, cur.Id, StringComparison.OrdinalIgnoreCase);
                changes.Add(new ComponentChange(cur.Type, cur.Name, ChangeKind.Modified, idChanged, old.Id, cur.Id, null));
            }
        }

        foreach (var (key, old) in baseMap.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentMap.ContainsKey(key))
            {
                changes.Add(new ComponentChange(old.Type, old.Name, ChangeKind.Deleted, false, old.Id, null, null));
            }
        }

        return changes
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Type)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyDictionary<string, SolutionComponent> IndexByKey(
        IReadOnlyList<SolutionComponent> components,
        string snapshotName,
        string? fallbackScope)
    {
        var duplicateKey = components
            .GroupBy(component => MatchKey(component, fallbackScope), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any())
            ?.Key;
        if (duplicateKey is not null)
        {
            throw new DiffException(
                $"The {snapshotName} snapshot contains duplicate component key '{duplicateKey}'.");
        }

        return components.ToDictionary(
            component => MatchKey(component, fallbackScope),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? FallbackScope(
        IReadOnlyList<SolutionComponent> components,
        IReadOnlyList<SolutionComponent> other)
    {
        if (components.Any(component => component.ScopeIdentity is not null))
        {
            return null;
        }

        var otherScopes = other
            .Select(component => component.ScopeIdentity)
            .Where(identity => identity is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return otherScopes.Count == 1 ? otherScopes[0] : null;
    }

    private static string MatchKey(SolutionComponent component, string? fallbackScope)
    {
        var scope = component.ScopeIdentity ?? fallbackScope;
        return scope is null
            ? "unscoped\u0001" + component.Key
            : "scoped\u0001" + scope.ToLowerInvariant() + "\u0001" + component.Key;
    }
}
