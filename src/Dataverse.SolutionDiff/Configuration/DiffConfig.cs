using System.Text.Json;

namespace Dataverse.SolutionDiff.Configuration;

/// <summary>
/// Controls which elements/attributes are treated as re-serialization noise and
/// stripped before comparison. Defaults are validated against real solution exports;
/// repos can add/remove entries via dvdrift.json.
/// </summary>
public sealed record DiffConfig
{
    public static readonly string[] DefaultStripElements =
    [
        "IntroducedVersion",
        "modifiedon",
        "createdon",
        "overriddencreatedon",
        "importsequencenumber",
        "versionnumber",
        // Component ids are recorded separately by the extractor (used for the
        // attribution join and "id changed" notes) and stripped from content so
        // delete+recreate shows as Modified rather than Delete+Add.
        "formid",
        "savedqueryid",
    ];

    public static readonly string[] DefaultStripAttributes =
    [
        "OrganizationVersion",
        "CRMServerServiceabilityVersion",
        "modifiedon",
        "createdon",
        // Volatile identity attributes that regenerate on export/sync. They are read
        // by the extractor before canonicalization (for matching and the attribution
        // join), then stripped from content so a re-synced step/role with a fresh id
        // does not look changed.
        "SdkMessageProcessingStepId",
        "SdkMessageProcessingStepImageId",
        "SdkMessageProcessingStepSecureConfigId",
        "RoleId",
        "AppModuleIdUnique",
    ];

    public IReadOnlyList<string> StripElements { get; init; } = DefaultStripElements;

    public IReadOnlyList<string> StripAttributes { get; init; } = DefaultStripAttributes;

    public bool ShouldStripElement(string localName) =>
        StripElements.Contains(localName, StringComparer.OrdinalIgnoreCase);

    public bool ShouldStripAttribute(string localName) =>
        StripAttributes.Contains(localName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads config from <paramref name="configPath"/>, or from dvdrift.json in the
    /// current working directory when no path is given. Returns defaults when no
    /// config file exists.
    /// </summary>
    public static DiffConfig Load(string? configPath)
    {
        var path = configPath;
        if (path is null)
        {
            var candidate = Path.Combine(Directory.GetCurrentDirectory(), "dvdrift.json");
            path = File.Exists(candidate) ? candidate : null;
        }

        if (path is null)
        {
            return new DiffConfig();
        }

        if (!File.Exists(path))
        {
            throw new DiffException($"Config file '{path}' not found.");
        }

        ConfigFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ConfigFile>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        }
        catch (JsonException ex)
        {
            throw new DiffException($"Config file '{path}' is not valid JSON: {ex.Message}");
        }

        return new DiffConfig
        {
            StripElements = Merge(DefaultStripElements, parsed?.StripElements),
            StripAttributes = Merge(DefaultStripAttributes, parsed?.StripAttributes),
        };
    }

    private static string[] Merge(string[] defaults, StripRule? rule)
    {
        if (rule is null)
        {
            return defaults;
        }

        var remove = new HashSet<string>(rule.Remove ?? [], StringComparer.OrdinalIgnoreCase);
        var result = defaults.Where(d => !remove.Contains(d)).ToList();
        foreach (var add in rule.Add ?? [])
        {
            if (!result.Contains(add, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(add);
            }
        }

        return [.. result];
    }

    private sealed class ConfigFile
    {
        public StripRule? StripElements { get; set; }

        public StripRule? StripAttributes { get; set; }
    }

    private sealed class StripRule
    {
        public string[]? Add { get; set; }

        public string[]? Remove { get; set; }
    }
}
