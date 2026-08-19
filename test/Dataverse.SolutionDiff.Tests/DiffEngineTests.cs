using Dataverse.SolutionDiff.Attribution;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Model;
using Dataverse.SolutionDiff.Reporting;
using Xunit;

namespace Dataverse.SolutionDiff.Tests;

public class DiffEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dvdrift-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Diff_ClassifiesAddedModifiedDeleted()
    {
        var report = await RunOffline();

        Assert.Equal(2, report.Added);
        Assert.Equal(2, report.Modified);
        Assert.Equal(1, report.Deleted);
        Assert.False(report.AttributionIncluded);

        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Added && c.Type == ComponentType.Attribute && c.Name == "Account.custom_bar");
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Added && c.Type == ComponentType.EnvironmentVariableDefinition && c.Name == "custom_TestSetting");
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Modified && c.Type == ComponentType.Flow && c.Name == "MySyncFlow");
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Deleted && c.Type == ComponentType.View && c.Name == "Account.Active Accounts");
    }

    [Fact]
    public async Task RecreatedForm_MatchesByName_ReportsIdChanged()
    {
        var report = await RunOffline();

        var form = report.Changes.Single(c => c.Type == ComponentType.Form);
        Assert.Equal(ChangeKind.Modified, form.Kind);
        Assert.True(form.IdChanged);
        Assert.Equal("11111111-1111-1111-1111-111111111111", form.OldId);
        Assert.Equal("33333333-3333-3333-3333-333333333333", form.NewId);
    }

    [Fact]
    public async Task IdenticalSnapshots_ReportNoChanges()
    {
        var baseline = Fixtures.WriteSnapshot(_root, current: false);
        var report = await DiffEngine.RunAsync(baseline, baseline, new DiffConfig(), null);

        Assert.Equal(0, report.Added + report.Modified + report.Deleted);
        Assert.Empty(report.Changes);
    }

    [Fact]
    public async Task AttributionJoin_MatchesMangledNames_AndAttachesState()
    {
        // API name has spaces/dashes that the export file name stripped.
        var source = new FakeAttributionSource(new AttributionRecord(
            "My Sync Flow",
            "44444444-4444-4444-4444-444444444444",
            new AttributionInfo("Jane Doe", new DateTimeOffset(2026, 8, 7, 7, 42, 0, TimeSpan.Zero), null, 1, "Activated", 2)));

        var report = await RunWith(source);

        Assert.True(report.AttributionIncluded);
        var flow = report.Changes.Single(c => c.Type == ComponentType.Flow);
        Assert.Equal("Jane Doe", flow.Attribution?.ModifiedBy);
        Assert.Equal("Activated", flow.Attribution?.StateLabel);
    }

    [Fact]
    public async Task AttributionJoin_UnmatchedComponents_DegradeGracefully()
    {
        var report = await RunWith(new FakeAttributionSource());

        Assert.True(report.AttributionIncluded);
        Assert.All(report.Changes, c => Assert.Null(c.Attribution));
    }

    [Fact]
    public async Task AttributionJoin_AmbiguousNormalizedNames_DegradeGracefully()
    {
        var source = new FakeAttributionSource(
            new AttributionRecord(
                "My Sync Flow",
                null,
                new AttributionInfo("Jane Doe", null, null, 1, "Activated", 2)),
            new AttributionRecord(
                "My-Sync-Flow",
                null,
                new AttributionInfo("John Doe", null, null, 0, "Draft", 1)));

        var report = await RunWith(source);

        var flow = report.Changes.Single(c => c.Type == ComponentType.Flow);
        Assert.Null(flow.Attribution);
    }

    [Fact]
    public async Task Attribution_RequestsOnlyChangedTypes()
    {
        var snapshot = Fixtures.WriteSnapshot(_root, current: false);
        var source = new FakeAttributionSource();

        await DiffEngine.RunAsync(snapshot, snapshot, new DiffConfig(), source);

        Assert.Empty(source.RequestedTypes);
    }

    [Fact]
    public async Task Determinism_SameInputs_ProduceByteIdenticalReports()
    {
        var baseline = Fixtures.WriteSnapshot(_root, current: false);
        var current = Fixtures.WriteSnapshot(_root, current: true);

        var first = await DiffEngine.RunAsync(baseline, current, new DiffConfig(), null);
        var second = await DiffEngine.RunAsync(baseline, current, new DiffConfig(), null);

        Assert.Equal(MarkdownReporter.Render(first), MarkdownReporter.Render(second));
        Assert.Equal(JsonReporter.Render(first), JsonReporter.Render(second));
    }

    [Fact]
    public async Task Determinism_ZipAndFolder_ProduceIdenticalReports()
    {
        var baseline = Fixtures.WriteSnapshot(_root, current: false);
        var current = Fixtures.WriteSnapshot(_root, current: true);
        var zip = Path.Combine(_root, "current.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(current, zip);

        var fromFolders = await DiffEngine.RunAsync(baseline, current, new DiffConfig(), null);
        var fromZip = await DiffEngine.RunAsync(baseline, zip, new DiffConfig(), null);

        Assert.Equal(JsonReporter.Render(fromFolders), JsonReporter.Render(fromZip));
    }

    [Fact]
    public async Task Markdown_ContainsSummaryAndChangeAttribution()
    {
        var source = new FakeAttributionSource(new AttributionRecord(
            "My Sync Flow",
            "44444444-4444-4444-4444-444444444444",
            new AttributionInfo("Jane Doe", new DateTimeOffset(2026, 8, 7, 7, 42, 0, TimeSpan.Zero), null, 1, "Activated", 2)));

        var markdown = MarkdownReporter.Render(await RunWith(source));

        Assert.Contains("**2 added · 2 modified · 1 deleted**", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Active automations", markdown, StringComparison.Ordinal);
        Assert.Contains("| Flow | MySyncFlow | Jane Doe | 2026-08-07 07:42 |", markdown, StringComparison.Ordinal);
        Assert.Contains("Account.Information _(id changed)_", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_EscapesTableBreakingContent()
    {
        var report = new DiffReport(
            1,
            0,
            0,
            true,
            [new ComponentChange(
                ComponentType.Flow,
                "Pipe|Flow\r\nSecond line",
                ChangeKind.Added,
                false,
                null,
                null,
                new AttributionInfo("Jane|Doe\nAdmin", null, null, null, null, null))]);

        var markdown = MarkdownReporter.Render(report);

        Assert.Contains("| Flow | Pipe\\|Flow<br>Second line | Jane\\|Doe<br>Admin | — |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_IsParseableAndCarriesSummary()
    {
        var json = JsonReporter.Render(await RunOffline());
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var summary = doc.RootElement.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("added").GetInt32());
        Assert.Equal(2, summary.GetProperty("modified").GetInt32());
        Assert.Equal(1, summary.GetProperty("deleted").GetInt32());
        Assert.False(summary.GetProperty("attributionIncluded").GetBoolean());
    }

    private Task<DiffReport> RunOffline()
    {
        var baseline = Fixtures.WriteSnapshot(_root, current: false);
        var current = Fixtures.WriteSnapshot(_root, current: true);
        return DiffEngine.RunAsync(baseline, current, new DiffConfig(), null);
    }

    private Task<DiffReport> RunWith(IAttributionSource source)
    {
        var baseline = Fixtures.WriteSnapshot(_root, current: false);
        var current = Fixtures.WriteSnapshot(_root, current: true);
        return DiffEngine.RunAsync(baseline, current, new DiffConfig(), source);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeAttributionSource : IAttributionSource
    {
        private readonly AttributionRecord[] _records;

        public IReadOnlyList<ComponentType> RequestedTypes { get; private set; } = [];

        public FakeAttributionSource(params AttributionRecord[] records) => _records = records;

        public Task<IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>>> GetRecordsAsync(
            IReadOnlyCollection<ComponentType> types,
            CancellationToken cancellationToken = default)
        {
            RequestedTypes = types.ToArray();
            return Task.FromResult<IReadOnlyDictionary<ComponentType, IReadOnlyList<AttributionRecord>>>(
                types.ToDictionary(
                    type => type,
                    type => (IReadOnlyList<AttributionRecord>)(type is ComponentType.Flow or ComponentType.Workflow ? _records : [])));
        }
    }
}
