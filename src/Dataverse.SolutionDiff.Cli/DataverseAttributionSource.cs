using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Dataverse.SolutionDiff.Attribution;
using Dataverse.SolutionDiff.Model;
using Microsoft.Identity.Client;

namespace Dataverse.SolutionDiff.Cli;

/// <summary>
/// Bulk-fetches attribution and state records from the Dataverse Web API.
/// Covers the v1 attributable types: forms (systemforms), views (savedqueries) and
/// flows/workflows (workflow entity, including statecode/statuscode).
/// Entity/attribute metadata attribution is a known v1 gap (the metadata endpoints
/// don't expose modifiedby uniformly) — those rows report "unavailable".
/// Auth: a pre-acquired token passthrough, or MSAL client credentials.
/// </summary>
public sealed class DataverseAttributionSource : IAttributionSource, IDisposable
{
    private readonly HttpClient _http;

    // One Dataverse query definition per component type. Multiple types can share
    // a definition (Flow and Workflow both live in the workflow entity); the fetch
    // is performed once per unique definition and its result assigned to each type.
    private sealed record QueryDefinition(string EntitySet, string IdField, string NameField, bool IncludeState);

    private static readonly QueryDefinition WorkflowQuery = new("workflows", "workflowid", "name", IncludeState: true);

    private static readonly IReadOnlyDictionary<ComponentType, QueryDefinition> QueryByType =
        new Dictionary<ComponentType, QueryDefinition>
        {
            [ComponentType.Form] = new("systemforms", "formid", "name", IncludeState: false),
            [ComponentType.View] = new("savedqueries", "savedqueryid", "name", IncludeState: false),
            [ComponentType.Flow] = WorkflowQuery,
            [ComponentType.Workflow] = WorkflowQuery,
        };

    private const string ApiVersion = "v9.2";

    private DataverseAttributionSource(HttpClient http) => _http = http;

    public static async Task<DataverseAttributionSource> CreateAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        var baseUrl = options.Url!.TrimEnd('/') + $"/api/data/{ApiVersion}/";
        var token = options.AccessToken ?? await AcquireTokenAsync(options, cancellationToken).ConfigureAwait(false);

        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new DataverseAttributionSource(http);
    }

    public async Task<IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>>> GetRecordsAsync(
        IReadOnlyCollection<ComponentType> types,
        CancellationToken cancellationToken = default)
    {
        var requested = types
            .Where(QueryByType.ContainsKey)
            .Distinct()
            .ToArray();

        // One fetch per unique query definition, run concurrently.
        var fetchTasks = requested
            .Select(type => QueryByType[type])
            .Distinct()
            .ToDictionary(
                definition => definition,
                definition => FetchRecordsAsync(
                    definition.EntitySet, definition.IdField, definition.NameField, definition.IncludeState, cancellationToken));

        await Task.WhenAll(fetchTasks.Values).ConfigureAwait(false);

        var records = new Dictionary<ComponentType, IReadOnlyList<AttributionRecord>>();
        foreach (var type in requested)
        {
            records[type] = await fetchTasks[QueryByType[type]].ConfigureAwait(false);
        }

        return records;
    }

    private async Task<IReadOnlyList<AttributionRecord>> FetchRecordsAsync(
        string entitySet,
        string idField,
        string nameField,
        bool includeState,
        CancellationToken cancellationToken)
    {
        var select = includeState
            ? $"{idField},{nameField},modifiedon,statecode,statuscode"
            : $"{idField},{nameField},modifiedon";

        string? url = $"{entitySet}?$select={select}&$expand=modifiedby($select=fullname)";
        var records = new List<AttributionRecord>();

        while (url is not null)
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var modifiedBy = item.TryGetProperty("modifiedby", out var mb) &&
                    mb.ValueKind == JsonValueKind.Object &&
                    mb.TryGetProperty("fullname", out var fn)
                        ? fn.GetString()
                        : null;

                var modifiedOn = item.TryGetProperty("modifiedon", out var mo) &&
                    mo.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(mo.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                        ? dto
                        : (DateTimeOffset?)null;

                int? state = includeState && item.TryGetProperty("statecode", out var sc) && sc.ValueKind == JsonValueKind.Number
                    ? sc.GetInt32()
                    : null;
                int? status = includeState && item.TryGetProperty("statuscode", out var st) && st.ValueKind == JsonValueKind.Number
                    ? st.GetInt32()
                    : null;

                var id = item.TryGetProperty(idField!, out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : null;
                var name = item.TryGetProperty(nameField!, out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString()
                    : null;

                records.Add(new AttributionRecord(name, id, new AttributionInfo(modifiedBy, modifiedOn, null, state, StateLabel(state), status)));
            }

            url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }

        return records;
    }

    public void Dispose() => _http.Dispose();

    internal static string? StateLabel(int? state) => state switch
    {
        0 => "Draft",
        1 => "Activated",
        2 => "Suspended",
        null => null,
        _ => $"Unknown ({state.Value.ToString(CultureInfo.InvariantCulture)})",
    };

    private static async Task<string> AcquireTokenAsync(CliOptions options, CancellationToken cancellationToken)
    {
        if (options.TenantId is null || options.ClientId is null || options.ClientSecret is null)
        {
            throw new DiffException(
                "Attribution requires --access-token or --tenant-id/--client-id/--client-secret " +
                "(or the DATAVERSE_* environment variables). Use --offline to run without attribution.");
        }

        var app = ConfidentialClientApplicationBuilder
            .Create(options.ClientId)
            .WithTenantId(options.TenantId)
            .WithClientSecret(options.ClientSecret)
            .Build();

        var scope = options.Url!.TrimEnd('/') + "/.default";
        var result = await app.AcquireTokenForClient([scope]).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return result.AccessToken;
    }
}
