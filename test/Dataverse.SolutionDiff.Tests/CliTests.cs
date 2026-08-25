using Dataverse.SolutionDiff.Cli;
using Xunit;

namespace Dataverse.SolutionDiff.Tests;

public class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dvdrift-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DefaultRun_WritesMarkdownReport_Exit0()
    {
        var (baseline, current) = WriteSnapshots();
        var outFile = Path.Combine(_root, "report.md");

        var exit = await Program.Main([baseline, current, "--offline", "--out", outFile]);

        Assert.Equal(0, exit);
        var report = await File.ReadAllTextAsync(outFile);
        Assert.StartsWith("# Dataverse Solution Diff\n", report, StringComparison.Ordinal);
        Assert.Contains("**2 added · 2 modified · 1 deleted**", report, StringComparison.Ordinal);
        Assert.Contains("_Attribution: not included (offline run", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailOnChange_Exit1_WhenChangesExist()
    {
        var (baseline, current) = WriteSnapshots();
        var exit = await Program.Main([baseline, current, "--offline", "--fail-on-change", "--out", Path.Combine(_root, "r.md")]);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task FailOnChange_Exit0_WhenSnapshotsIdentical()
    {
        var baseline = Fixtures.WriteSnapshot(_root, current: false);
        var exit = await Program.Main([baseline, baseline, "--offline", "--fail-on-change", "--out", Path.Combine(_root, "r.md")]);
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RemovedActiveAutomationFlag_Exit3()
    {
        var (baseline, current) = WriteSnapshots();
        var exit = await Program.Main([baseline, current, "--offline", "--fail-on-active-automation", "--out", Path.Combine(_root, "r.md")]);
        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task JsonFormat_ProducesParseableOutput()
    {
        var (baseline, current) = WriteSnapshots();
        var outFile = Path.Combine(_root, "report.json");

        var exit = await Program.Main([baseline, current, "--offline", "--format", "json", "--out", outFile]);

        Assert.Equal(0, exit);
        using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(outFile));
        Assert.Equal(2, doc.RootElement.GetProperty("summary").GetProperty("added").GetInt32());
    }

    [Fact]
    public async Task SummaryOnly_Markdown_IsCountsLineOnly()
    {
        var (baseline, current) = WriteSnapshots();
        var outFile = Path.Combine(_root, "summary.md");

        var exit = await Program.Main([baseline, current, "--offline", "--summary-only", "--out", outFile]);

        Assert.Equal(0, exit);
        var report = await File.ReadAllTextAsync(outFile);
        Assert.Equal("**2 added · 2 modified · 1 deleted**\n", report);
    }

    [Fact]
    public async Task SummaryOnly_Markdown_MatchesTheFullReportsCountsLine()
    {
        var (baseline, current) = WriteSnapshots();
        var full = Path.Combine(_root, "full.md");
        var summary = Path.Combine(_root, "summary.md");

        await Program.Main([baseline, current, "--offline", "--out", full]);
        await Program.Main([baseline, current, "--offline", "--summary-only", "--out", summary]);

        var countsLine = (await File.ReadAllTextAsync(summary)).TrimEnd('\n');
        Assert.Contains(countsLine, await File.ReadAllTextAsync(full), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummaryOnly_Json_HoistsCountsToTheRoot()
    {
        var (baseline, current) = WriteSnapshots();
        var outFile = Path.Combine(_root, "summary.json");

        var exit = await Program.Main([baseline, current, "--offline", "--format", "json", "--summary-only", "--out", outFile]);

        Assert.Equal(0, exit);
        using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(outFile));
        Assert.Equal(2, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("modified").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("deleted").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("changes", out _));
    }

    [Fact]
    public async Task SummaryOnly_StillGatesWithFailOnChange()
    {
        var (baseline, current) = WriteSnapshots();
        var exit = await Program.Main([baseline, current, "--offline", "--summary-only", "--fail-on-change", "--out", Path.Combine(_root, "s.md")]);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task MissingInput_Exit3()
    {
        var exit = await Program.Main(["does-not-exist", "also-not", "--offline", "--out", Path.Combine(_root, "r.md")]);
        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task UnknownOption_Exit3()
    {
        var (baseline, current) = WriteSnapshots();
        var exit = await Program.Main([baseline, current, "--frobnicate", "--out", Path.Combine(_root, "r.md")]);
        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task UnknownFormat_Exit3()
    {
        var (baseline, current) = WriteSnapshots();
        var exit = await Program.Main([baseline, current, "--offline", "--format", "xml", "--out", Path.Combine(_root, "r.md")]);
        Assert.Equal(3, exit);
    }

    private (string Baseline, string Current) WriteSnapshots() =>
        (Fixtures.WriteSnapshot(_root, current: false), Fixtures.WriteSnapshot(_root, current: true));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
