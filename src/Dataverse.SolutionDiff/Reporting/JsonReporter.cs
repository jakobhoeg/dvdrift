using System.Text.Json;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Reporting;

/// <summary>
/// Renders the machine-readable report. Property order follows declaration order,
/// indentation and line endings are fixed — identical inputs produce byte-identical JSON.
/// </summary>
public static class JsonReporter
{
    public static string Render(DiffReport report)
    {
        var payload = new
        {
            summary = new
            {
                added = report.Added,
                modified = report.Modified,
                deleted = report.Deleted,
                attributionIncluded = report.AttributionIncluded,
            },
            changes = report.Changes.Select(c => new
            {
                type = c.Type.ToString(),
                name = c.Name,
                kind = c.Kind.ToString(),
                idChanged = c.IdChanged,
                oldId = c.OldId,
                newId = c.NewId,
                attribution = c.Attribution is null
                    ? null
                    : new
                    {
                        modifiedBy = c.Attribution.ModifiedBy,
                        modifiedOn = c.Attribution.ModifiedOn,
                        state = c.Attribution.StateLabel,
                    },
            }),
        };

        return Serialize(payload);
    }

    /// <summary>
    /// Counts only, hoisted to the root so notification and gating scripts can read
    /// <c>.added</c> without walking into <c>.summary</c> or parsing Markdown.
    /// </summary>
    public static string RenderSummary(DiffReport report) => Serialize(new
    {
        added = report.Added,
        modified = report.Modified,
        deleted = report.Deleted,
        attributionIncluded = report.AttributionIncluded,
    });

    private static string Serialize(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
}
