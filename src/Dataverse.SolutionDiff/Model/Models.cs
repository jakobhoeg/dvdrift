namespace Dataverse.SolutionDiff.Model;

/// <summary>Friendly component classification for a changed (or flagged) item.</summary>
public enum ComponentType
{
    Entity,
    Attribute,
    Form,
    View,
    Flow,
    Workflow,
    WebResource,
    EnvironmentVariableDefinition,
    AppAction,
    PluginAssembly,
    CanvasApp,
    PcfControl,
    CustomApi,
    Formula,
    SecurityRole,
    AppModule,
    OptionSet,
    SiteMap,
    EntityRelationship,
    Dashboard,
    PluginStep,
    ServiceEndpoint,
    CustomConnector,
    Bot,
    BotComponent,
    SettingDefinition,
    ConnectionReference,
    Report,
    Template,
    Other,
}

public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
}

/// <summary>A single file inside a snapshot, with a normalized '/'-separated path.</summary>
public sealed record RawFile(string Path, byte[] Content)
{
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    public string Text() => System.Text.Encoding.UTF8.GetString(Content);
}

/// <summary>
/// One logical component of a solution snapshot. <see cref="CanonicalContent"/> is
/// the noise-stripped, deterministically serialized representation used for equality.
/// <see cref="Key"/> is the scope-local match key (logical-name first);
/// <see cref="ScopeIdentity"/> keeps solutions isolated when a manifest identity is
/// available. <see cref="Id"/> is the component GUID when one exists, kept for
/// reporting and attribution joins.
/// </summary>
public sealed record SolutionComponent(
    ComponentType Type,
    string Name,
    string? Id,
    string Key,
    string CanonicalContent)
{
    public string? ScopeIdentity { get; init; }

    public static string MakeKey(ComponentType type, string name) =>
        type + "|" + name.ToLowerInvariant();
}

/// <summary>Point-in-time data joined from the Dataverse Web API. Never part of the diff itself.</summary>
public sealed record AttributionInfo(
    string? ModifiedBy,
    DateTimeOffset? ModifiedOn,
    string? CreatedBy,
    int? StateCode,
    string? StateLabel,
    int? StatusCode);

public sealed record ComponentChange(
    ComponentType Type,
    string Name,
    ChangeKind Kind,
    bool IdChanged,
    string? OldId,
    string? NewId,
    AttributionInfo? Attribution);

public sealed record DiffReport(
    int Added,
    int Modified,
    int Deleted,
    bool AttributionIncluded,
    IReadOnlyList<ComponentChange> Changes);
