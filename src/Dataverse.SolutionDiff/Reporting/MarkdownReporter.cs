using System.Globalization;
using System.Text;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Reporting;

/// <summary>
/// Renders the human-readable report. Output contains no generation timestamps and
/// uses stable ordering, so identical inputs produce byte-identical Markdown.
/// </summary>
public static class MarkdownReporter
{
    /// <summary>
    /// Reports with more changes than this render every section collapsed, so a big diff
    /// stays skimmable in a GitHub job summary or PR comment. It is all-or-nothing: a mix of
    /// open and collapsed sections makes the headers hard to pick out.
    /// </summary>
    private const int CollapseThreshold = 10;

    public static string Render(DiffReport report)
    {
        var sb = new StringBuilder();
        sb.Append("# Dataverse Solution Diff\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"{Counts(report)}\n\n");
        sb.Append(report.AttributionIncluded
            ? "_Attribution: live Dataverse join (point-in-time)_\n"
            : "_Attribution: not included (offline run — state and modified-by unavailable)_\n");

        sb.Append("\n## Changes\n");
        if (report.Changes.Count == 0)
        {
            sb.Append("\nNo changes.\n");
            return sb.ToString();
        }

        var openAttribute = report.Changes.Count > CollapseThreshold ? "" : " open";
        foreach (var kind in new[] { ChangeKind.Added, ChangeKind.Modified, ChangeKind.Deleted })
        {
            var group = report.Changes.Where(c => c.Kind == kind).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            sb.Append(CultureInfo.InvariantCulture,
                $"\n<details{openAttribute}>\n<summary><b>{kind}</b> ({group.Count})</summary>\n\n");
            // Without the join there is nothing to put in the attribution columns, so they
            // are dropped rather than filled with a column of em dashes.
            sb.Append(report.AttributionIncluded
                ? "| Type | Component | Modified by | Modified on (UTC) |\n|---|---|---|---|\n"
                : "| Type | Component |\n|---|---|\n");
            foreach (var c in group)
            {
                var name = Esc(c.Name) + (c.IdChanged ? " _(id changed)_" : "");
                sb.Append(report.AttributionIncluded
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {c.Type} | {name} | {Esc(c.Attribution?.ModifiedBy)} | {Format(c.Attribution?.ModifiedOn)} |\n")
                    : string.Create(CultureInfo.InvariantCulture, $"| {c.Type} | {name} |\n"));
            }

            sb.Append("\n</details>\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The counts line on its own, for embedding where the full tables are too verbose -
    /// release notes, chat notifications, PR comments. Byte-identical to the line the full
    /// report opens with, so the compact and full outputs cannot drift apart.
    /// </summary>
    public static string RenderSummary(DiffReport report) => Counts(report) + "\n";

    private static string Counts(DiffReport report) => string.Create(
        CultureInfo.InvariantCulture,
        $"**{report.Added} added · {report.Modified} modified · {report.Deleted} deleted**");

    private static string Esc(string? value) => (value ?? "—")
        .Replace("\r\n", "<br>", StringComparison.Ordinal)
        .Replace("\r", "<br>", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal);

    private static string Format(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
