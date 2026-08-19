using System.Text;
using System.Xml.Linq;
using Dataverse.SolutionDiff.Canonicalization;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Classification;

public static partial class ComponentExtractor
{
    private static void ExtractAttributes(
        string scope,
        string entityName,
        IEnumerable<XElement> attributes,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var standardAttributes = new StringBuilder();
        foreach (var attribute in attributes)
        {
            if (IsStandardAttribute(attribute))
            {
                standardAttributes.Append(attribute.ToString(SaveOptions.DisableFormatting)).Append('\n');
                continue;
            }

            var logicalName = attribute.Element("LogicalName")?.Value ?? attribute.Element("Name")?.Value ?? "unknown";
            components.Add(Make(
                ComponentType.Attribute,
                ScopedName(scope, entityName + "." + logicalName),
                null,
                Canonicalizer.CanonicalizeXml(attribute.ToString(), config)));
        }

        if (standardAttributes.Length > 0)
        {
            components.Add(Make(
                ComponentType.Attribute,
                ScopedName(scope, entityName + " (standard attributes)"),
                null,
                Canonicalizer.CanonicalizeText(standardAttributes.ToString())));
        }
    }

    private static void ExtractForm(
        string scope,
        string entityName,
        XElement form,
        string canonicalXml,
        string fallback,
        string? formType,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var id = form.Element("formid")?.Value;
        components.Add(Make(
            ComponentType.Form,
            ScopedName(scope, entityName + "." + FormDisplayName(form, id, fallback, formType)),
            id,
            Canonicalizer.CanonicalizeXml(canonicalXml, config)));
    }

    private static void ExtractView(
        string scope,
        string entityName,
        XElement view,
        string canonicalXml,
        string fallback,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var id = view.Element("savedqueryid")?.Value;
        var viewName = view.Element("name")?.Value
            ?? view.Element("LocalizedNames")?.Element("LocalizedName")?.Attribute("description")?.Value
            ?? (id is not null ? "view " + id : fallback);
        components.Add(Make(
            ComponentType.View,
            ScopedName(scope, entityName + "." + viewName),
            id,
            Canonicalizer.CanonicalizeXml(canonicalXml, config)));
    }
}