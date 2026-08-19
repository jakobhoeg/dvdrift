using Dataverse.SolutionDiff.Attribution;
using Dataverse.SolutionDiff.Classification;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Diffing;
using Dataverse.SolutionDiff.Loading;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff;

/// <summary>
/// Orchestrates load → extract → diff → (optional) attribution join → report model.
/// Pure with respect to its inputs: same snapshot bytes and same attribution records
/// always produce the same report.
/// </summary>
public static class DiffEngine
{
    /// <summary>Component types the attribution join knows how to query (v1).</summary>
    public static readonly ComponentType[] AttributionTypes =
    [
        ComponentType.Form,
        ComponentType.View,
        ComponentType.Flow,
        ComponentType.Workflow,
    ];

    public static async Task<DiffReport> RunAsync(
        string baselinePath,
        string currentPath,
        DiffConfig config,
        IAttributionSource? attributionSource,
        CancellationToken cancellationToken = default)
    {
        var baseline = ComponentExtractor.Extract(SnapshotLoader.Load(baselinePath), config);
        var current = ComponentExtractor.Extract(SnapshotLoader.Load(currentPath), config);
        var changes = SnapshotDiffer.Diff(baseline, current);

        IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>>? attribution = null;
        if (attributionSource is not null)
        {
            var requiredTypes = AttributionTypes
                .Where(type => changes.Any(change => change.Type == type))
                .ToArray();

            attribution = await attributionSource.GetRecordsAsync(requiredTypes, cancellationToken).ConfigureAwait(false);
        }

        return ReportBuilder.Build(changes, attribution);
    }
}
