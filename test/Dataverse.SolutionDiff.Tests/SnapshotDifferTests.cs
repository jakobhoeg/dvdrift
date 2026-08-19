using Dataverse.SolutionDiff.Diffing;
using Dataverse.SolutionDiff.Model;
using Xunit;

namespace Dataverse.SolutionDiff.Tests;

public class SnapshotDifferTests
{
    [Fact]
    public void SoleManifestlessScope_MatchesManifestScopedComponents()
    {
        var scoped = MakeComponent("Flow|sync", "Sync") with { ScopeIdentity = "Contoso" };
        var unscoped = MakeComponent("Flow|sync", "Sync");

        var changes = SnapshotDiffer.Diff([scoped], [unscoped]);

        Assert.Empty(changes);
    }

    [Fact]
    public void DifferentManifestScopes_DoNotMatch()
    {
        var baseline = MakeComponent("Flow|sync", "Sync") with { ScopeIdentity = "ContosoA" };
        var current = MakeComponent("Flow|sync", "Sync") with { ScopeIdentity = "ContosoB" };

        var changes = SnapshotDiffer.Diff([baseline], [current]);

        Assert.Contains(changes, change => change.Kind == ChangeKind.Added);
        Assert.Contains(changes, change => change.Kind == ChangeKind.Deleted);
    }

    [Fact]
    public void DuplicateKeys_ReportSnapshotAndKey()
    {
        var components = new[]
        {
            MakeComponent("Flow|duplicate", "Duplicate A"),
            MakeComponent("flow|DUPLICATE", "Duplicate B"),
        };

        var exception = Assert.Throws<DiffException>(() => SnapshotDiffer.Diff(components, []));

        Assert.Contains("baseline snapshot", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Flow|duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SolutionComponent MakeComponent(string key, string name) =>
        new(ComponentType.Flow, name, null, key, "{}");
}