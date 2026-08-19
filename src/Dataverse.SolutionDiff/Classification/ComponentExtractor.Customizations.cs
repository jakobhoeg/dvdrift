using System.Text;
using System.Xml.Linq;
using Dataverse.SolutionDiff.Canonicalization;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Classification;

public static partial class ComponentExtractor
{
    private sealed record ItemSection(
        string SectionName,
        string ChildName,
        ComponentType ComponentType,
        string NameField,
        string IdField,
        bool CaptureId = true);

    private static readonly ItemSection[] PerItemSections =
    [
        new("Roles", "Role", ComponentType.SecurityRole, "Name", "RoleId"),
        new("optionsets", "optionset", ComponentType.OptionSet, "Name", "OptionSetId"),
        new("AppModules", "AppModule", ComponentType.AppModule, "UniqueName", "AppModuleIdUnique"),
        new("SdkMessageProcessingSteps", "SdkMessageProcessingStep", ComponentType.PluginStep, "Name", "SdkMessageProcessingStepId"),
        new("ServiceEndpoints", "ServiceEndpoint", ComponentType.ServiceEndpoint, "Name", "ServiceEndpointId"),
        new("connectionreferences", "connectionreference", ComponentType.ConnectionReference, "connectionreferencelogicalname", "connectionreferenceid"),
        new("CustomControls", "CustomControl", ComponentType.PcfControl, "Name", "CustomControlId"),
        new("AIModels", "AIModel", ComponentType.Other, "Name", "AIModelId"),
    ];

    private static readonly HashSet<string> BespokeSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Entities", "Workflows", "WebResources", "Languages",
    };

    private static readonly HashSet<string> MonolithicSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "SiteMap", "RibbonCustomizations", "Templates", "EntityMaps", "OrganizationSettings",
        "FieldSecurityProfiles", "DuplicateRules", "SLAs", "RoutingRules", "ConvertRules",
        "SimilarityRules", "MobileOfflineProfiles", "ImportMaps", "EntityDataProviders",
    };

    private static readonly HashSet<string> HandledSections = new(
        BespokeSections
            .Concat(MonolithicSections)
            .Concat(PerItemSections.Select(section => section.SectionName))
            .Concat(["EntityRelationships", "Dashboards", "InteractionCentricDashboards"]),
        StringComparer.OrdinalIgnoreCase);

    private static void DecomposeCustomizations(
        RawFile file,
        string scope,
        bool hasWorkflowFiles,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var root = ParseRoot(file);
        if (root.Name.LocalName != "ImportExportXml")
        {
            components.Add(Make(ComponentType.Other, file.Name, null, Canonicalizer.CanonicalizeXml(file.Text(), config)));
            return;
        }

        foreach (var entity in root.Element("Entities")?.Elements("Entity") ?? [])
        {
            ExtractEntity(scope, entity, config, components);
        }

        if (!hasWorkflowFiles)
        {
            foreach (var workflow in root.Element("Workflows")?.Elements("Workflow") ?? [])
            {
                var name = workflow.Attribute("Name")?.Value ?? workflow.Attribute("WorkflowId")?.Value ?? "unknown";
                components.Add(Make(
                    ComponentType.Workflow,
                    ScopedName(scope, name),
                    workflow.Attribute("WorkflowId")?.Value,
                    Canonicalizer.CanonicalizeXml(workflow.ToString(), config)));
            }
        }

        foreach (var definition in PerItemSections)
        {
            var section = root.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, definition.SectionName, StringComparison.OrdinalIgnoreCase));
            if (section is null)
            {
                continue;
            }

            foreach (var child in section.Elements().Where(element =>
                string.Equals(element.Name.LocalName, definition.ChildName, StringComparison.OrdinalIgnoreCase)))
            {
                var id = ReadValue(child, definition.IdField);
                var name = ReadValue(child, definition.NameField) ?? id ?? "unnamed";
                components.Add(Make(
                    definition.ComponentType,
                    ScopedName(scope, name),
                    definition.CaptureId ? id : null,
                    Canonicalizer.CanonicalizeXml(child.ToString(), config)));
            }
        }

        ExtractDashboards(root, scope, config, components);
        ExtractEntityRelationships(root, scope, config, components);

        foreach (var section in root.Elements().Where(element => MonolithicSections.Contains(element.Name.LocalName)))
        {
            if (section.Elements().Any())
            {
                components.Add(Make(
                    ComponentType.Other,
                    ScopedName(scope, section.Name.LocalName),
                    null,
                    Canonicalizer.CanonicalizeXml(section.ToString(), config)));
            }
        }

        foreach (var section in root.Elements().Where(element =>
            !HandledSections.Contains(element.Name.LocalName) && element.Elements().Any()))
        {
            components.Add(Make(
                ComponentType.Other,
                ScopedName(scope, section.Name.LocalName),
                null,
                Canonicalizer.CanonicalizeXml(section.ToString(), config)));
        }
    }

    private static void ExtractEntity(
        string scope,
        XElement entity,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var entityName = entity.Element("Name")?.Value?.Trim();
        if (string.IsNullOrEmpty(entityName))
        {
            entityName = "unknown";
        }

        ExtractAttributes(
            scope,
            entityName,
            entity.Element("EntityInfo")?.Element("entity")?.Element("attributes")?.Elements("attribute") ?? [],
            config,
            components);

        foreach (var form in entity.Element("FormXml")?.Elements("forms")?.Elements("systemform") ?? [])
        {
            ExtractForm(scope, entityName, form, form.ToString(), "unnamed form", ReadValue(form, "type"), config, components);
        }

        foreach (var view in entity.Element("SavedQueries")?.Descendants("savedquery") ?? [])
        {
            ExtractView(scope, entityName, view, view.ToString(), "unnamed view", config, components);
        }

        var shell = new XElement(entity);
        shell.Element("EntityInfo")?.Element("entity")?.Element("attributes")?.Remove();
        shell.Element("FormXml")?.Remove();
        shell.Element("SavedQueries")?.Remove();
        components.Add(Make(
            ComponentType.Entity,
            ScopedName(scope, entityName),
            null,
            Canonicalizer.CanonicalizeXml(shell.ToString(), config)));
    }

    private static string? ReadValue(XElement element, string fieldName) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, fieldName, StringComparison.OrdinalIgnoreCase))?.Value
        ?? element.Elements().FirstOrDefault(child =>
            string.Equals(child.Name.LocalName, fieldName, StringComparison.OrdinalIgnoreCase))?.Value;

    // Platform-owned attributes (IsCustomField=0) are entity boilerplate: a new activity
    // entity alone adds ~70 of them (createdon, ownerid, statecode, ...). They collapse
    // into one bucket per entity; custom attributes stay individually diffable. A missing
    // flag is treated as custom so nothing is ever silently dropped.
    private static bool IsStandardAttribute(XElement attribute) =>
        string.Equals(attribute.Element("IsCustomField")?.Value.Trim(), "0", StringComparison.OrdinalIgnoreCase);

    private static string FormDisplayName(XElement? form, string? id, string fallback, string? formType = null)
    {
        var name = form?.Element("name")?.Value
            ?? form?.Element("LocalizedNames")?.Element("LocalizedName")?.Attribute("description")?.Value
            ?? (id is not null ? "form " + id : fallback);
        var typeLabel = FormTypeLabel(formType) ?? FormDescriptionLabel(form);
        return typeLabel is null ? name : $"{name} ({typeLabel})";
    }

    private static string? FormTypeLabel(string? formType) => formType?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "main" => "Main",
        "quickcreate" or "quickcreateform" => "Quick Create",
        "quickview" or "quickviewform" => "Quick View",
        "card" or "cardform" => "Card",
        var value => value,
    };

    // Dataverse descriptions contain the only reliable form-type clue in some exports:
    // "A card form for this entity." / "A main form for this entity." / quick-create text.
    private static string? FormDescriptionLabel(XElement? form)
    {
        var description = form?.Element("Descriptions")?.Elements("Description")
            .Select(element => element.Attribute("description")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return description?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            var text when text.Contains("card form", StringComparison.Ordinal) => "Card",
            var text when text.Contains("quick create", StringComparison.Ordinal) => "Quick Create",
            var text when text.Contains("quick view", StringComparison.Ordinal) => "Quick View",
            var text when text.Contains("main form", StringComparison.Ordinal) => "Main",
            var text when text.Contains("form for this entity", StringComparison.Ordinal) => "Main",
            _ => null,
        };
    }

    // Dashboards store identity in LocalizedNames/FormId rather than Name attributes,
    // so they need bespoke extraction (per-item sections only check attributes/elements).
    private static void ExtractDashboards(XElement root, string scope, DiffConfig config, List<SolutionComponent> components)
    {
        foreach (var sectionName in new[] { "Dashboards", "InteractionCentricDashboards" })
        {
            var section = root.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, sectionName, StringComparison.OrdinalIgnoreCase));
            if (section is null)
            {
                continue;
            }

            foreach (var dashboard in section.Elements())
            {
                var id = ReadValue(dashboard, "FormId") ?? ReadValue(dashboard, "formid");
                var name = dashboard.Element("LocalizedNames")?.Element("LocalizedName")?.Attribute("description")?.Value
                    ?? ReadValue(dashboard, "Name")
                    ?? (id is not null ? "dashboard " + id : "unnamed");
                components.Add(Make(
                    ComponentType.Dashboard,
                    ScopedName(scope, name),
                    NormalizeGuid(id),
                    Canonicalizer.CanonicalizeXml(dashboard.ToString(), config)));
            }
        }
    }

    // Relationships are classified by whether the linked tables belong to the
    // solution itself (its <Entities> section) — no hardcoded system-table lists.
    // Both tables in the solution: hand-designed, kept per-item. One or neither:
    // auto-generated lookup noise (a new activity entity alone produces dozens of
    // relationships to first-party tables) — collapsed into one component per
    // in-solution table, or a single global bucket when neither side is included.
    private static void ExtractEntityRelationships(XElement root, string scope, DiffConfig config, List<SolutionComponent> components)
    {
        var section = root.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "EntityRelationships", StringComparison.OrdinalIgnoreCase));
        if (section is null)
        {
            return;
        }

        var solutionEntities = new HashSet<string>(
            (root.Element("Entities")?.Elements("Entity") ?? [])
                .Select(e => e.Element("Name")?.Value?.Trim())
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!),
            StringComparer.OrdinalIgnoreCase);

        var externalGroups = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in section.Elements("EntityRelationship"))
        {
            var name = relationship.Attribute("Name")?.Value ?? "unnamed";
            // 1:N relationships use Referencing/Referenced; N:N use Entity1/Entity2.
            var first = ReadValue(relationship, "ReferencingEntityName") ?? ReadValue(relationship, "Entity1LogicalName");
            var second = ReadValue(relationship, "ReferencedEntityName") ?? ReadValue(relationship, "Entity2LogicalName");
            var firstIncluded = first is not null && solutionEntities.Contains(first);
            var secondIncluded = second is not null && solutionEntities.Contains(second);

            if (firstIncluded && secondIncluded)
            {
                components.Add(Make(
                    ComponentType.EntityRelationship,
                    ScopedName(scope, name),
                    null,
                    Canonicalizer.CanonicalizeXml(relationship.ToString(), config)));
                continue;
            }

            var groupKey = firstIncluded ? first! : secondIncluded ? second! : "(external relationships)";
            if (!externalGroups.TryGetValue(groupKey, out var builder))
            {
                builder = new StringBuilder();
                externalGroups[groupKey] = builder;
            }

            builder.Append(relationship.ToString(SaveOptions.DisableFormatting)).Append('\n');
        }

        foreach (var (groupName, builder) in externalGroups.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            components.Add(Make(
                ComponentType.EntityRelationship,
                ScopedName(scope, groupName == "(external relationships)"
                    ? groupName
                    : groupName + " (external relationships)"),
                null,
                Canonicalizer.CanonicalizeText(builder.ToString())));
        }
    }
}