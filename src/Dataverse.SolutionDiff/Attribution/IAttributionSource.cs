using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Attribution;

/// <summary>
/// Provides attribution and state records for requested component types, fetched
/// in bulk from a live Dataverse environment. Implemented in the CLI (Web API client); the
/// engine only depends on this interface, keeping the core diff usable offline and
/// trivially fakeable in tests.
/// </summary>
public interface IAttributionSource
{
    Task<IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>>> GetRecordsAsync(
        IReadOnlyCollection<ComponentType> types,
        CancellationToken cancellationToken = default);
}

public sealed record AttributionRecord(string? Name, string? Id, AttributionInfo Info);
