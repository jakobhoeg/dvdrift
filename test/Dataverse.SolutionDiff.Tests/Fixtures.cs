namespace Dataverse.SolutionDiff.Tests;

/// <summary>
/// Hand-built synthetic snapshots in the raw zip-extract layout (contoso-style,
/// no real customer data). Baseline and current differ in exactly these ways:
/// noise (OrganizationVersion, IntroducedVersion), one added attribute, one form
/// modified AND re-created (new formid), one deleted view, one modified flow,
/// one added environment variable definition.
/// </summary>
public static class Fixtures
{
    public const string ContentTypesXml = "<Types />";

    public const string SolutionXml = """
        <ImportExportXml version="9.2.0.0" SolutionPackageVersion="9.2" languagecode="1033" generatedBy="CrmLive">
          <SolutionManifest>
            <UniqueName>ContosoBase</UniqueName>
            <Version>1.0.0.1</Version>
            <Managed>0</Managed>
          </SolutionManifest>
        </ImportExportXml>
        """;

    public const string BaselineCustomizations = """
        <ImportExportXml OrganizationVersion="9.2.26071.167" CRMServerServiceabilityVersion="9.2.26071.00167">
          <Entities>
            <Entity>
              <Name LocalizedName="Account" OriginalName="">Account</Name>
              <EntityInfo>
                <entity Name="Account">
                  <attributes>
                    <attribute PhysicalName="custom_foo">
                      <Type>text</Type>
                      <LogicalName>custom_foo</LogicalName>
                      <IntroducedVersion>1.0.0.1</IntroducedVersion>
                    </attribute>
                  </attributes>
                </entity>
              </EntityInfo>
              <FormXml>
                <forms type="main">
                  <systemform>
                    <formid>{11111111-1111-1111-1111-111111111111}</formid>
                    <IntroducedVersion>1.0.0.1</IntroducedVersion>
                    <name>Information</name>
                    <form>
                      <tabs>
                        <tab id="{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}">
                          <labels><label description="General" languagecode="1033" /></labels>
                        </tab>
                      </tabs>
                    </form>
                  </systemform>
                </forms>
              </FormXml>
              <SavedQueries>
                <savedqueries>
                  <savedquery>
                    <savedqueryid>{22222222-2222-2222-2222-222222222222}</savedqueryid>
                    <IntroducedVersion>1.0.0.1</IntroducedVersion>
                    <name>Active Accounts</name>
                  </savedquery>
                </savedqueries>
              </SavedQueries>
            </Entity>
          </Entities>
        </ImportExportXml>
        """;

    public const string CurrentCustomizations = """
        <ImportExportXml OrganizationVersion="9.2.26071.999" CRMServerServiceabilityVersion="9.2.26071.00999">
          <Entities>
            <Entity>
              <Name LocalizedName="Account" OriginalName="">Account</Name>
              <EntityInfo>
                <entity Name="Account">
                  <attributes>
                    <attribute PhysicalName="custom_foo">
                      <Type>text</Type>
                      <LogicalName>custom_foo</LogicalName>
                      <IntroducedVersion>1.0.0.2</IntroducedVersion>
                    </attribute>
                    <attribute PhysicalName="custom_bar">
                      <Type>text</Type>
                      <LogicalName>custom_bar</LogicalName>
                      <IntroducedVersion>1.0.0.2</IntroducedVersion>
                    </attribute>
                  </attributes>
                </entity>
              </EntityInfo>
              <FormXml>
                <forms type="main">
                  <systemform>
                    <formid>{33333333-3333-3333-3333-333333333333}</formid>
                    <IntroducedVersion>1.0.0.2</IntroducedVersion>
                    <name>Information</name>
                    <form>
                      <tabs>
                        <tab id="{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}">
                          <labels><label description="Overview" languagecode="1033" /></labels>
                        </tab>
                      </tabs>
                    </form>
                  </systemform>
                </forms>
              </FormXml>
            </Entity>
          </Entities>
        </ImportExportXml>
        """;

    public const string BaselineFlowJson = """
        {
          "properties": {
            "connectionReferences": {},
            "definition": {
              "triggers": { "manual": { "kind": "Http", "type": "Request" } },
              "actions": {}
            }
          }
        }
        """;

    // Same flow, one action added, and top-level property order shuffled to prove
    // JSON canonicalization sorts properties before comparing.
    public const string CurrentFlowJson = """
        {
          "properties": {
            "definition": {
              "actions": { "Send_email": { "type": "ApiConnection", "inputs": {} } },
              "triggers": { "manual": { "kind": "Http", "type": "Request" } }
            },
            "connectionReferences": {}
          }
        }
        """;

    public const string EnvVarXml = """
        <environmentvariabledefinition schemaname="custom_TestSetting">
          <displayname>Test Setting</displayname>
          <type>100000000</type>
          <IntroducedVersion>1.0.0.2</IntroducedVersion>
        </environmentvariabledefinition>
        """;

    public const string FlowFileName = "MySyncFlow-44444444-4444-4444-4444-444444444444.json";

    // DAXIF-style concatenated name+guid with no separator and no file extension.
    public const string WebResourceFileName = "new_testjs55555555-5555-5555-5555-555555555555";

    public const string WebResourceContent = "console.log(\"hello\");\n";

    /// <summary>Writes a snapshot folder and returns its path.</summary>
    public static string WriteSnapshot(string root, bool current, string uniqueName = "ContosoBase")
    {
        var dir = Path.Combine(root, current ? "current" : "baseline");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "[Content_Types].xml"), ContentTypesXml);
      File.WriteAllText(
        Path.Combine(dir, "solution.xml"),
        SolutionXml.Replace("<UniqueName>ContosoBase</UniqueName>", $"<UniqueName>{uniqueName}</UniqueName>", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(dir, "customizations.xml"), current ? CurrentCustomizations : BaselineCustomizations);

        var workflows = Path.Combine(dir, "Workflows");
        Directory.CreateDirectory(workflows);
        File.WriteAllText(Path.Combine(workflows, FlowFileName), current ? CurrentFlowJson : BaselineFlowJson);

        var webResources = Path.Combine(dir, "WebResources");
        Directory.CreateDirectory(webResources);
        File.WriteAllText(Path.Combine(webResources, WebResourceFileName), WebResourceContent);

        if (current)
        {
            var envVarDir = Path.Combine(dir, "environmentvariabledefinitions", "custom_TestSetting");
            Directory.CreateDirectory(envVarDir);
            File.WriteAllText(Path.Combine(envVarDir, "environmentvariabledefinition.xml"), EnvVarXml);
        }

        return dir;
    }

    /// <summary>Writes a minimal pac solution unpack layout and returns its path.</summary>
    public static string WritePacSnapshot(string root)
    {
        var entityDir = Path.Combine(root, "pac", "Entities", "account");
        Directory.CreateDirectory(Path.Combine(entityDir, "FormXml", "main"));
        Directory.CreateDirectory(Path.Combine(entityDir, "SavedQueries"));

        File.WriteAllText(Path.Combine(entityDir, "Entity.xml"), """
            <entity Name="account">
              <attributes>
                <attribute PhysicalName="custom_foo">
                  <LogicalName>custom_foo</LogicalName>
                  <IntroducedVersion>1.0.0.0</IntroducedVersion>
                </attribute>
              </attributes>
            </entity>
            """);

        File.WriteAllText(Path.Combine(entityDir, "FormXml", "main", "{66666666-6666-6666-6666-666666666666}.xml"), """
            <systemform>
              <formid>{66666666-6666-6666-6666-666666666666}</formid>
              <name>Information</name>
            </systemform>
            """);

        File.WriteAllText(Path.Combine(entityDir, "SavedQueries", "{77777777-7777-7777-7777-777777777777}.xml"), """
            <savedquery>
              <savedqueryid>{77777777-7777-7777-7777-777777777777}</savedqueryid>
              <name>Active Accounts</name>
            </savedquery>
            """);

        return Path.Combine(root, "pac");
    }
}
