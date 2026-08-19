using Dataverse.SolutionDiff.Classification;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Loading;
using Dataverse.SolutionDiff.Model;
using Xunit;

namespace Dataverse.SolutionDiff.Tests;

public class ComponentExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dvdrift-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RawZipExtractLayout_ClassifiesAllComponentTypes()
    {
        var dir = Fixtures.WriteSnapshot(_root, current: false);
        var components = ComponentExtractor.Extract(SnapshotLoader.Load(dir), new DiffConfig());

        Assert.Equal(7, components.Count);
        Assert.Contains(components, c => c.Type == ComponentType.Entity && c.Name == "Account");
        Assert.Contains(components, c => c.Type == ComponentType.Attribute && c.Name == "Account.custom_foo");
        Assert.Contains(components, c => c.Type == ComponentType.Form && c.Name == "Account.Information");
        Assert.Contains(components, c => c.Type == ComponentType.View && c.Name == "Account.Active Accounts");
        Assert.Contains(components, c => c.Type == ComponentType.Flow && c.Name == "MySyncFlow");
        Assert.Contains(components, c => c.Type == ComponentType.WebResource && c.Name == "new_testjs");
        Assert.Contains(components, c => c.Type == ComponentType.Other && c.Name == "Solution manifest");
    }

    [Fact]
    public void FileNameGuids_AreStrippedFromNamesAndKeptAsIds()
    {
        var dir = Fixtures.WriteSnapshot(_root, current: false);
        var components = ComponentExtractor.Extract(SnapshotLoader.Load(dir), new DiffConfig());

        var flow = components.Single(c => c.Type == ComponentType.Flow);
        Assert.Equal("MySyncFlow", flow.Name);
        Assert.Equal("44444444-4444-4444-4444-444444444444", flow.Id);

        var webResource = components.Single(c => c.Type == ComponentType.WebResource);
        Assert.Equal("new_testjs", webResource.Name);
        Assert.Equal("55555555-5555-5555-5555-555555555555", webResource.Id);
    }

    [Fact]
    public void PacUnpackLayout_ClassifiesEntityFolder()
    {
        var dir = Fixtures.WritePacSnapshot(_root);
        var components = ComponentExtractor.Extract(SnapshotLoader.Load(dir), new DiffConfig());

        Assert.Equal(4, components.Count);
        Assert.Contains(components, c => c.Type == ComponentType.Entity && c.Name == "account");
        Assert.Contains(components, c => c.Type == ComponentType.Attribute && c.Name == "account.custom_foo");
        Assert.Contains(components, c => c.Type == ComponentType.Form && c.Name == "account.Information (Main)" && c.Id == "66666666-6666-6666-6666-666666666666");
        Assert.Contains(components, c => c.Type == ComponentType.View && c.Name == "account.Active Accounts" && c.Id == "77777777-7777-7777-7777-777777777777");
    }

    [Fact]
    public void StandardAttributes_CollapseIntoOneComponentPerEntity()
    {
        var snapshot = Path.Combine(_root, "standard-attributes");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), """
            <ImportExportXml>
              <Entities>
                <Entity>
                  <Name>custom_example</Name>
                  <EntityInfo>
                    <entity Name="custom_example">
                      <attributes>
                        <attribute><LogicalName>custom_entity</LogicalName><IsCustomField>1</IsCustomField></attribute>
                        <attribute><LogicalName>createdon</LogicalName><IsCustomField>0</IsCustomField></attribute>
                        <attribute><LogicalName>statecode</LogicalName><IsCustomField>0</IsCustomField></attribute>
                        <attribute><LogicalName>custom_noflag</LogicalName></attribute>
                      </attributes>
                    </entity>
                  </EntityInfo>
                </Entity>
              </Entities>
            </ImportExportXml>
            """);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());
        var attributes = components.Where(component => component.Type == ComponentType.Attribute).ToList();

        Assert.Contains(attributes, attribute => attribute.Name == "custom_example.custom_entity");
        // A missing IsCustomField flag is treated as custom so nothing is silently dropped.
        Assert.Contains(attributes, attribute => attribute.Name == "custom_example.custom_noflag");
        var standard = Assert.Single(attributes, attribute => attribute.Name == "custom_example (standard attributes)");
        Assert.Contains("createdon", standard.CanonicalContent, StringComparison.Ordinal);
        Assert.Contains("statecode", standard.CanonicalContent, StringComparison.Ordinal);
        Assert.DoesNotContain(attributes, attribute => attribute.Name == "custom_example.createdon");
        Assert.Equal(3, attributes.Count);
    }

    [Fact]
    public void SameNamedForms_AreDistinguishedByFormType()
    {
        var snapshot = Path.Combine(_root, "form-types");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), """
            <ImportExportXml>
              <Entities>
                <Entity>
                  <Name>custom_example</Name>
                  <FormXml>
                    <forms>
                      <systemform type="main"><formid>{11111111-1111-1111-1111-111111111111}</formid><name>Information</name></systemform>
                      <systemform type="quickCreate"><formid>{22222222-2222-2222-2222-222222222222}</formid><name>Information</name></systemform>
                      <systemform type="quickView"><formid>{33333333-3333-3333-3333-333333333333}</formid><name>Information</name></systemform>
                    </forms>
                  </FormXml>
                </Entity>
              </Entities>
            </ImportExportXml>
            """);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());
        var forms = components.Where(component => component.Type == ComponentType.Form).ToList();

        Assert.Contains(forms, form => form.Name == "custom_example.Information (Main)");
        Assert.Contains(forms, form => form.Name == "custom_example.Information (Quick Create)");
        Assert.Contains(forms, form => form.Name == "custom_example.Information (Quick View)");
        Assert.Equal(3, forms.Select(form => form.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SameNamedFormsWithoutType_UseMeaningfulDescriptionsWhenAvailable()
    {
        var snapshot = Path.Combine(_root, "forms-without-types");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), """
            <ImportExportXml>
              <Entities>
                <Entity>
                  <Name>custom_example</Name>
                  <FormXml>
                    <forms>
                      <systemform><formid>{11111111-1111-1111-1111-111111111111}</formid><LocalizedNames><LocalizedName description="Information" /></LocalizedNames><Descriptions><Description description="A card form for this entity." /></Descriptions></systemform>
                      <systemform><formid>{22222222-2222-2222-2222-222222222222}</formid><LocalizedNames><LocalizedName description="Information" /></LocalizedNames><Descriptions><Description description="A form for this entity." /></Descriptions></systemform>
                      <systemform><formid>{33333333-3333-3333-3333-333333333333}</formid><LocalizedNames><LocalizedName description="Information" /></LocalizedNames><Descriptions><Description description="" /></Descriptions></systemform>
                    </forms>
                  </FormXml>
                </Entity>
              </Entities>
            </ImportExportXml>
            """);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());
        var forms = components.Where(component => component.Type == ComponentType.Form).ToList();

        Assert.Contains(forms, form => form.Name == "custom_example.Information (Card)");
        Assert.Contains(forms, form => form.Name == "custom_example.Information (Main)");
        Assert.Contains(forms, form => form.Name == "custom_example.Information");
        Assert.DoesNotContain(forms, form => form.Name.Contains("11111111", StringComparison.Ordinal));
    }

    [Fact]
    public void PacSameNamedForms_AreDistinguishedByFormTypeFolder()
    {
        var snapshot = Path.Combine(_root, "pac-form-types");
        var formRoot = Path.Combine(snapshot, "Entities", "custom_example", "FormXml");
        Directory.CreateDirectory(Path.Combine(formRoot, "main"));
        Directory.CreateDirectory(Path.Combine(formRoot, "quickCreate"));
        File.WriteAllText(Path.Combine(formRoot, "main", "{11111111-1111-1111-1111-111111111111}.xml"),
            "<systemform><formid>{11111111-1111-1111-1111-111111111111}</formid><name>Information</name></systemform>");
        File.WriteAllText(Path.Combine(formRoot, "quickCreate", "{22222222-2222-2222-2222-222222222222}.xml"),
            "<systemform><formid>{22222222-2222-2222-2222-222222222222}</formid><name>Information</name></systemform>");

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());
        var forms = components.Where(component => component.Type == ComponentType.Form).ToList();

        Assert.Contains(forms, form => form.Name == "custom_example.Information (Main)");
        Assert.Contains(forms, form => form.Name == "custom_example.Information (Quick Create)");
    }

    [Fact]
    public void ZipInput_ProducesSameComponentsAsFolder()
    {
        var dir = Fixtures.WriteSnapshot(_root, current: false);
        var zipPath = Path.Combine(_root, "baseline.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zipPath);

        var fromFolder = ComponentExtractor.Extract(SnapshotLoader.Load(dir), new DiffConfig());
        var fromZip = ComponentExtractor.Extract(SnapshotLoader.Load(zipPath), new DiffConfig());

        Assert.Equal(fromFolder, fromZip);
    }

  [Fact]
  public void RawAndManifestlessPacLayouts_UseSameLocalComponentKeys()
  {
    var raw = ComponentExtractor.Extract(
      SnapshotLoader.Load(Fixtures.WriteSnapshot(Path.Combine(_root, "raw-layout"), current: false)),
      new DiffConfig());
    var pac = ComponentExtractor.Extract(
      SnapshotLoader.Load(Fixtures.WritePacSnapshot(Path.Combine(_root, "pac-layout"))),
      new DiffConfig());

    Assert.Equal(
      raw.Single(component => component.Type == ComponentType.Entity).Key,
      pac.Single(component => component.Type == ComponentType.Entity).Key);
    Assert.Equal(
      raw.Single(component => component.Type == ComponentType.Attribute).Key,
      pac.Single(component => component.Type == ComponentType.Attribute).Key);
  }

    [Fact]
    public void OneItemContainer_ProducesSameComponentsAsStandaloneSolution()
    {
        var standalone = Fixtures.WriteSnapshot(Path.Combine(_root, "standalone"), current: false);
      Directory.CreateDirectory(Path.Combine(standalone, "FutureComponents"));
      File.WriteAllText(Path.Combine(standalone, "FutureComponents", "new-kind.txt"), "future content");
        var container = Path.Combine(_root, "one-item-container");
        Directory.CreateDirectory(container);
        System.IO.Compression.ZipFile.CreateFromDirectory(standalone, Path.Combine(container, "RenamedPackage.zip"));

        var fromStandalone = ComponentExtractor.Extract(SnapshotLoader.Load(standalone), new DiffConfig());
        var fromContainer = ComponentExtractor.Extract(SnapshotLoader.Load(container), new DiffConfig());

        Assert.Equal(fromStandalone, fromContainer);
    }

    [Fact]
    public void ContainerWithDuplicateUniqueNames_ReportsAmbiguousScopes()
    {
        var container = Path.Combine(_root, "duplicate-scopes");
        Directory.CreateDirectory(container);
        System.IO.Compression.ZipFile.CreateFromDirectory(
            Fixtures.WriteSnapshot(Path.Combine(_root, "duplicate-a"), current: false),
            Path.Combine(container, "First.zip"));
        System.IO.Compression.ZipFile.CreateFromDirectory(
            Fixtures.WriteSnapshot(Path.Combine(_root, "duplicate-b"), current: false),
            Path.Combine(container, "Second.zip"));

        var exception = Assert.Throws<DiffException>(() =>
            ComponentExtractor.Extract(SnapshotLoader.Load(container), new DiffConfig()));

        Assert.Contains("multiple solution scopes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ContosoBase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestWithoutUniqueName_ReportsManifestPath()
    {
        var snapshot = Path.Combine(_root, "missing-unique-name");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "solution.xml"), "<ImportExportXml><SolutionManifest /></ImportExportXml>");

        var exception = Assert.Throws<DiffException>(() =>
            ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig()));

        Assert.Contains("solution.xml", exception.Message, StringComparison.Ordinal);
        Assert.Contains("has no UniqueName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerZip_NestedSolutions_AreScopedBySolutionName()
    {
        // Real-world bundle shape: one outer zip holding one inner zip per solution
        // (what multi-solution export pipelines produce).
        var innerA = Fixtures.WriteSnapshot(Path.Combine(_root, "innerA"), current: false, uniqueName: "AlphaSolution");
        var innerB = Fixtures.WriteSnapshot(Path.Combine(_root, "innerB"), current: false, uniqueName: "BetaSolution");
        var containerA = Path.Combine(_root, "containerA");
        var containerB = Path.Combine(_root, "containerB");
        Directory.CreateDirectory(containerA);
        Directory.CreateDirectory(containerB);
        System.IO.Compression.ZipFile.CreateFromDirectory(innerA, Path.Combine(containerA, "AlphaSolution.zip"));
        System.IO.Compression.ZipFile.CreateFromDirectory(innerB, Path.Combine(containerA, "BetaSolution.zip"));
        System.IO.Compression.ZipFile.CreateFromDirectory(innerA, Path.Combine(containerB, "AlphaSolution.zip"));
        var bundlePath = Path.Combine(_root, "bundle.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(containerA, bundlePath);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(bundlePath), new DiffConfig());

        Assert.Contains(components, c => c.Name == "AlphaSolution / MySyncFlow" && c.Type == ComponentType.Flow);
        Assert.Contains(components, c => c.Name == "BetaSolution / MySyncFlow" && c.Type == ComponentType.Flow);
        // Same logical name in two solutions must not collide:
        Assert.Equal(2, components.Count(c => c.Type == ComponentType.Flow));
        Assert.Contains(components, c => c.Name == "AlphaSolution / Solution manifest");
    }

    [Fact]
    public void ContainerZip_RemovedNestedSolution_ShowsAsDeletedComponents()
    {
        var containerA = Path.Combine(_root, "containerA2");
        var containerB = Path.Combine(_root, "containerB2");
        Directory.CreateDirectory(containerA);
        Directory.CreateDirectory(containerB);
        System.IO.Compression.ZipFile.CreateFromDirectory(Fixtures.WriteSnapshot(Path.Combine(_root, "src1"), current: false, uniqueName: "AlphaSolution"), Path.Combine(containerA, "AlphaSolution.zip"));
        System.IO.Compression.ZipFile.CreateFromDirectory(Fixtures.WriteSnapshot(Path.Combine(_root, "src2"), current: false, uniqueName: "BetaSolution"), Path.Combine(containerA, "BetaSolution.zip"));
        System.IO.Compression.ZipFile.CreateFromDirectory(Fixtures.WriteSnapshot(Path.Combine(_root, "src3"), current: false, uniqueName: "AlphaSolution"), Path.Combine(containerB, "AlphaSolution.zip"));

        var changes = Dataverse.SolutionDiff.Diffing.SnapshotDiffer.Diff(
            ComponentExtractor.Extract(SnapshotLoader.Load(containerA), new DiffConfig()),
            ComponentExtractor.Extract(SnapshotLoader.Load(containerB), new DiffConfig()));

        Assert.True(changes.Count > 0);
        Assert.All(changes, c => Assert.Equal(Dataverse.SolutionDiff.Model.ChangeKind.Deleted, c.Kind));
        Assert.All(changes, c => Assert.StartsWith("BetaSolution / ", c.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateLogicalKeysAndIds_AreDisambiguatedForDiffing()
    {
        var baseline = Path.Combine(_root, "duplicates-baseline");
        var current = Path.Combine(_root, "duplicates-current");
        var id = "88888888-8888-8888-8888-888888888888";

        foreach (var root in new[] { baseline, current })
        {
            Directory.CreateDirectory(Path.Combine(root, "Workflows", "a"));
            Directory.CreateDirectory(Path.Combine(root, "Workflows", "b"));
            File.WriteAllText(Path.Combine(root, "Workflows", "a", $"Duplicate-{id}.json"), "{}");
            File.WriteAllText(Path.Combine(root, "Workflows", "b", $"Duplicate-{id}.json"), "{}");
        }

        var baselineComponents = ComponentExtractor.Extract(SnapshotLoader.Load(baseline), new DiffConfig());
        var currentComponents = ComponentExtractor.Extract(SnapshotLoader.Load(current), new DiffConfig());

        Assert.Equal(2, baselineComponents.Count);
        Assert.Equal(2, baselineComponents.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Empty(Dataverse.SolutionDiff.Diffing.SnapshotDiffer.Diff(baselineComponents, currentComponents));
    }

    [Fact]
    public void CustomizationsSections_UseAttributeNamesAndIds()
    {
        var snapshot = Path.Combine(_root, "sections");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), """
            <ImportExportXml>
              <roles><role Name="CRM Admin" RoleId="{11111111-1111-1111-1111-111111111111}" /></roles>
              <OptionSets><OptionSet Name="new_category" OptionSetId="{22222222-2222-2222-2222-222222222222}" /></OptionSets>
              <SdkMessageProcessingSteps>
                <SdkMessageProcessingStep Name="Account: Update" SdkMessageProcessingStepId="{33333333-3333-3333-3333-333333333333}" />
              </SdkMessageProcessingSteps>
            </ImportExportXml>
            """);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());

        Assert.Contains(components, component =>
            component.Type == ComponentType.SecurityRole &&
            component.Name == "CRM Admin" &&
            component.Id == "11111111-1111-1111-1111-111111111111");
        Assert.Contains(components, component =>
            component.Type == ComponentType.OptionSet &&
            component.Name == "new_category" &&
            component.Id == "22222222-2222-2222-2222-222222222222");
        Assert.Contains(components, component =>
            component.Type == ComponentType.PluginStep &&
            component.Name == "Account: Update" &&
            component.Id == "33333333-3333-3333-3333-333333333333");
    }

    [Fact]
    public void MalformedStructuralXml_ReportsSnapshotPath()
    {
        var snapshot = Path.Combine(_root, "malformed");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), "<ImportExportXml><Entities>");

        var exception = Assert.Throws<DiffException>(() =>
            ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig()));

        Assert.Contains("customizations.xml", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not valid XML", exception.Message, StringComparison.Ordinal);
    }

      [Fact]
      public void MalformedPacEntityXml_ReportsSnapshotPath()
      {
        var snapshot = Path.Combine(_root, "malformed-pac");
        var entityDirectory = Path.Combine(snapshot, "Entities", "account");
        Directory.CreateDirectory(entityDirectory);
        File.WriteAllText(Path.Combine(entityDirectory, "Entity.xml"), "<entity><attributes>");

        var exception = Assert.Throws<DiffException>(() =>
          ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig()));

        Assert.Contains("Entities/account/Entity.xml", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not valid XML", exception.Message, StringComparison.Ordinal);
      }

    [Fact]
    public void InteractionCentricDashboard_UsesLocalizedNameAndFormId()
    {
        var snapshot = Path.Combine(_root, "dashboard");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), """
            <ImportExportXml>
              <InteractionCentricDashboards>
                <InteractionCentricDashboard unmodified="1">
                  <LocalizedNames>
                    <LocalizedName description="Dashboard til kontakter" languagecode="1030" />
                    <LocalizedName description="Contacts Dashboard" languagecode="1033" />
                  </LocalizedNames>
                  <FormId>{70ad8b15-e3f3-4d7d-9e4b-16cb4e51b484}</FormId>
                </InteractionCentricDashboard>
              </InteractionCentricDashboards>
            </ImportExportXml>
            """);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());

        var dashboard = Assert.Single(components, c => c.Type == ComponentType.Dashboard);
        Assert.Equal("Dashboard til kontakter", dashboard.Name);
        Assert.Equal("70ad8b15-e3f3-4d7d-9e4b-16cb4e51b484", dashboard.Id);
        Assert.DoesNotContain(components, c => c.Name == "unnamed");
    }

    [Fact]
    public void ExternalRelationships_GroupedPerIncludedEntity()
    {
        var snapshot = Path.Combine(_root, "relationships");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(snapshot, "customizations.xml"), """
            <ImportExportXml>
              <Entities>
                <Entity><Name>custom_entity</Name></Entity>
                <Entity><Name>custom_entity_archive</Name></Entity>
                <Entity><Name>Contact</Name></Entity>
              </Entities>
              <EntityRelationships>
                <EntityRelationship Name="activity_pointer_custom_entity">
                  <ReferencingEntityName>custom_entity</ReferencingEntityName>
                  <ReferencedEntityName>ActivityPointer</ReferencedEntityName>
                </EntityRelationship>
                <EntityRelationship Name="msdyn_playbookinstance_custom_entities">
                  <ReferencingEntityName>custom_entity</ReferencingEntityName>
                  <ReferencedEntityName>msdyn_playbookinstance</ReferencedEntityName>
                </EntityRelationship>
                <EntityRelationship Name="msdyn_quicksendemail_contact">
                  <ReferencingEntityName>Contact</ReferencingEntityName>
                  <ReferencedEntityName>msdyn_quicksendemail</ReferencedEntityName>
                </EntityRelationship>
                <EntityRelationship Name="adx_invitation_invitecontacts">
                  <Entity1LogicalName>adx_invitation</Entity1LogicalName>
                  <Entity2LogicalName>account</Entity2LogicalName>
                </EntityRelationship>
                <EntityRelationship Name="custom_entityId">
                  <ReferencingEntityName>custom_entity_archive</ReferencingEntityName>
                  <ReferencedEntityName>custom_entity</ReferencedEntityName>
                </EntityRelationship>
              </EntityRelationships>
            </ImportExportXml>
            """);

        var components = ComponentExtractor.Extract(SnapshotLoader.Load(snapshot), new DiffConfig());
        var relationships = components.Where(c => c.Type == ComponentType.EntityRelationship).ToList();

        // Relationships reaching outside the solution collapse into one group per included table.
        Assert.Contains(relationships, c => c.Name == "custom_entity (external relationships)");
        Assert.Contains(relationships, c => c.Name == "Contact (external relationships)");
        // Neither table included (N:N field names) → single global bucket.
        Assert.Contains(relationships, c => c.Name == "(external relationships)");
        // Both tables included → hand-designed, kept per-item.
        Assert.Contains(relationships, c => c.Name == "custom_entityId");
        Assert.Equal(4, relationships.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
