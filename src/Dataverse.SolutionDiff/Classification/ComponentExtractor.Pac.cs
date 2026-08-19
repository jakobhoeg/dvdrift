using System.Xml.Linq;
using Dataverse.SolutionDiff.Canonicalization;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Classification;

public static partial class ComponentExtractor
{
    private static void ClassifyPacEntityFile(
        string scope,
        string rel,
        RawFile file,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var parts = rel.Split('/');
        if (parts.Length < 3)
        {
            var shallowContent = Canonicalizer.LooksLikeText(file.Content)
                ? Canonicalizer.CanonicalizeText(file.Text())
                : Canonicalizer.CanonicalizeBinary(file.Content);
            components.Add(new SolutionComponent(
                ComponentType.Other, rel, null, SolutionComponent.MakeKey(ComponentType.Other, rel), shallowContent));
            return;
        }

        var entityName = parts[1];

        if (parts.Length == 3 && string.Equals(parts[2], "Entity.xml", StringComparison.OrdinalIgnoreCase))
        {
            var root = ParseRoot(file);
            var attributes = root.Element("attributes");
            if (attributes is not null)
            {
                ExtractAttributes(scope, entityName, attributes.Elements("attribute"), config, components);

                var shell = new XElement(root);
                shell.Element("attributes")?.Remove();
                components.Add(Make(ComponentType.Entity, ScopedName(scope, entityName), null, Canonicalizer.CanonicalizeXml(shell.ToString(), config)));
            }
            else
            {
                components.Add(Make(ComponentType.Entity, ScopedName(scope, entityName), null, Canonicalizer.CanonicalizeXml(root.ToString(), config)));
            }

            return;
        }

        var subFolder = parts[2];
        if (subFolder.Equals("FormXml", StringComparison.OrdinalIgnoreCase) &&
            file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            var root = ParseRoot(file);
            var formType = parts.Length > 3 ? parts[3] : null;
            ExtractForm(scope, entityName, root, file.Text(), file.Name, formType, config, components);
            return;
        }

        if (subFolder.Equals("SavedQueries", StringComparison.OrdinalIgnoreCase) &&
            file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            var root = ParseRoot(file);
            ExtractView(scope, entityName, root, file.Text(), file.Name, config, components);
            return;
        }

        var content = Canonicalizer.LooksLikeText(file.Content)
            ? Canonicalizer.CanonicalizeText(file.Text())
            : Canonicalizer.CanonicalizeBinary(file.Content);
        components.Add(new SolutionComponent(
            ComponentType.Other, rel, null, SolutionComponent.MakeKey(ComponentType.Other, rel), content));
    }
}