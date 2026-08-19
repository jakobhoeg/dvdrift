using System.Text;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Attribution;

/// <summary>
/// Joins attribution/state records onto the diff. Matching is id-first; the name
/// fallback normalizes away non-alphanumerics because export file names mangle
/// display names ("Contoso - Nightly Contact Sync" becomes
/// "Contoso-NightlyContactSync-&lt;guid&gt;").
/// Missing records degrade to "unavailable" in the report — never a failure.
/// </summary>
public static class ReportBuilder
{
    public static DiffReport Build(
        IReadOnlyList<ComponentChange> changes,
        IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>>? attribution)
    {
        var index = attribution is null ? null : AttributionIndex.Create(attribution);
        var enriched = changes
            .Select(c => c with { Attribution = FindAttribution(index, c.Type, c.Name, c.NewId ?? c.OldId) })
            .ToList();

        return new DiffReport(
            enriched.Count(c => c.Kind == ChangeKind.Added),
            enriched.Count(c => c.Kind == ChangeKind.Modified),
            enriched.Count(c => c.Kind == ChangeKind.Deleted),
            attribution is not null,
            enriched);
    }

    private static AttributionInfo? FindAttribution(
        AttributionIndex? index,
        ComponentType type,
        string name,
        string? id)
    {
        if (index is null || !index.ByType.TryGetValue(type, out var records))
        {
            return null;
        }

        if (id is not null && records.ById.TryGetValue(id, out var byId))
        {
            return byId;
        }

        // Component names may carry a solution scope ("ContosoFlows / MySyncFlow")
        // and/or an entity qualifier ("Account.Active Accounts"); API records have neither.
        var shortName = name;
        var scopeIdx = shortName.LastIndexOf(" / ", StringComparison.Ordinal);
        if (scopeIdx >= 0)
        {
            shortName = shortName[(scopeIdx + 3)..];
        }

        var dotIdx = shortName.LastIndexOf('.');
        if (dotIdx >= 0)
        {
            shortName = shortName[(dotIdx + 1)..];
        }

        return records.ByName.GetValueOrDefault(Normalize(shortName));
    }

    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private sealed record AttributionTypeIndex(
        IReadOnlyDictionary<string, AttributionInfo> ById,
        IReadOnlyDictionary<string, AttributionInfo> ByName);

    private sealed record AttributionIndex(IReadOnlyDictionary<ComponentType, AttributionTypeIndex> ByType)
    {
        public static AttributionIndex Create(
            IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>> attribution) =>
            new(attribution.ToDictionary(
                pair => pair.Key,
                pair => new AttributionTypeIndex(
                    pair.Value
                        .Where(record => record.Id is not null)
                        .GroupBy(record => record.Id!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First().Info, StringComparer.OrdinalIgnoreCase),
                    pair.Value
                        .Where(record => record.Name is not null)
                        .GroupBy(record => Normalize(record.Name!), StringComparer.Ordinal)
                        .Where(group => group.Count() == 1)
                        .ToDictionary(group => group.Key, group => group.Single().Info, StringComparer.Ordinal))));
    }
}
