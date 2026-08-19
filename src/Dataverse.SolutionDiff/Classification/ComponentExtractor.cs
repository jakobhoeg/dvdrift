using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dataverse.SolutionDiff.Canonicalization;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Classification;

/// <summary>
/// Decomposes a raw snapshot into logical components. Handles these layouts uniformly:
/// <list type="bullet">
/// <item>Raw zip extract / DAXIF extended export (monolithic customizations.xml + Workflows/, WebResources/, ... folders)</item>
/// <item>pac solution unpack / SolutionPackager layout (Entities/&lt;name&gt;/... per-component folders)</item>
/// <item>Solution .zip files, and container zips/folders holding several solution zips
/// (expanded by the loader; the nested zip name becomes a scope prefix on component names)</item>
/// </list>
/// The layout root is detected per file by locating the first known root segment
/// (e.g. <c>Workflows/</c>, <c>customizations.xml</c>); everything before it is the
/// scope — a solution folder or expanded nested zip — and is shown in names as
/// <c>"Scope / Component"</c> so identically named components in different solutions
/// never collide.
/// Component identity is matched logical-name first; trailing GUIDs in file names
/// (both "-guid" and concatenated forms) are stripped from names and kept as Ids.
/// Anything unrecognized is kept as an <see cref="ComponentType.Other"/> component
/// keyed by path so it is never silently dropped.
/// </summary>
public static partial class ComponentExtractor
{
    public static IReadOnlyList<SolutionComponent> Extract(IReadOnlyList<RawFile> files, DiffConfig config)
    {
        var scopes = SnapshotScopeDetector.Detect(files);
        var components = new List<SolutionComponent>();

        foreach (var detectedScope in scopes)
        {
            var firstComponentIndex = components.Count;
            var scope = string.Empty;
            var scopeFiles = detectedScope.Files.ToList();

            var hasWorkflowFiles = scopeFiles.Any(s =>
                s.RelativePath.StartsWith("Workflows/", StringComparison.OrdinalIgnoreCase) &&
                (s.File.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                 s.File.Name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)));

            // Folder-grouped component types: all files belonging to one logical
            // component are merged into a single comparable unit. The group name is
            // the raw folder/prefix (GUID suffix kept); display name and id are
            // derived from it afterwards.
            AddGroupedComponents(scopeFiles, scope, "CanvasApps/", ComponentType.CanvasApp, CanvasAppGroupName, config, components);
            AddGroupedComponents(scopeFiles, scope, "Controls/", ComponentType.PcfControl, static rel => rel.Split('/')[1], config, components);
            AddGroupedComponents(scopeFiles, scope, "PluginAssemblies/", ComponentType.PluginAssembly, static rel => rel.Split('/')[1], config, components);
            AddGroupedComponents(scopeFiles, scope, "customapis/", ComponentType.CustomApi, static rel => rel.Split('/')[1], config, components);
            AddGroupedComponents(scopeFiles, scope, "Connectors/", ComponentType.CustomConnector, static rel => rel.Split('/')[1], config, components);
            AddGroupedComponents(scopeFiles, scope, "bots/", ComponentType.Bot, static rel => rel.Split('/')[1], config, components);
            AddGroupedComponents(scopeFiles, scope, "botcomponents/", ComponentType.BotComponent, static rel => rel.Split('/')[1], config, components);
            AddGroupedComponents(scopeFiles, scope, "Reports/", ComponentType.Report, static rel => rel.Split('/').Length > 2 ? rel.Split('/')[2] : rel, config, components);

            foreach (var item in scopeFiles)
            {
                ClassifyFile(item.File, scope, item.RelativePath, config, components);
            }

            foreach (var customizations in scopeFiles.Where(s =>
                string.Equals(s.RelativePath, "customizations.xml", StringComparison.OrdinalIgnoreCase)))
            {
                DecomposeCustomizations(customizations.File, scope, hasWorkflowFiles, config, components);
            }

            for (var index = firstComponentIndex; index < components.Count; index++)
            {
                var component = components[index];
                components[index] = component with
                {
                    Name = scopes.Count > 1 ? ScopedName(detectedScope.DisplayName, component.Name) : component.Name,
                    ScopeIdentity = detectedScope.Identity.Length > 0 ? detectedScope.Identity : null,
                };
            }
        }

        return Disambiguate(components);
    }

    private static void ClassifyFile(RawFile file, string scope, string rel, DiffConfig config, List<SolutionComponent> components)
    {
        var name = file.Name;

        if (string.Equals(name, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rel, "customizations.xml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "ExtendedSolution.xml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(rel, "solution.xml", StringComparison.OrdinalIgnoreCase))
        {
            components.Add(Make(ComponentType.Other, ScopedName(scope, "Solution manifest"), null, Canonicalizer.CanonicalizeXml(file.Text(), config)));
            return;
        }

        // Folder-grouped roots are handled by AddGroupedComponents.
        var firstSegment = rel.Split('/')[0];
        if (firstSegment.Equals("CanvasApps", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("Controls", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("PluginAssemblies", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("Connectors", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("bots", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("botcomponents", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("Reports", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("customapis", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (firstSegment.Equals("settingdefinitions", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            components.Add(Make(ComponentType.SettingDefinition, ScopedName(scope, StripExtension(name)), null, Canonicalizer.CanonicalizeXml(file.Text(), config)));
            return;
        }

        if (firstSegment.Equals("connectionreferences", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            components.Add(Make(ComponentType.ConnectionReference, ScopedName(scope, StripExtension(name)), null, Canonicalizer.CanonicalizeJson(file.Text())));
            return;
        }

        if (rel.StartsWith("Workflows/", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = StripExtension(name);
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                components.Add(Make(ComponentType.Flow, ScopedName(scope, StripGuid(baseName)), ExtractGuid(baseName), Canonicalizer.CanonicalizeJson(file.Text())));
            }
            else if (name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                components.Add(Make(ComponentType.Workflow, ScopedName(scope, StripGuid(baseName)), ExtractGuid(baseName), Canonicalizer.CanonicalizeXml(file.Text(), config)));
            }

            // .json.meta.xml / similar sidecar files are skipped: state in them is
            // unreliable; the authoritative state comes from the API join.
            return;
        }

        if (firstSegment.Equals("WebResources", StringComparison.OrdinalIgnoreCase))
        {
            var resourceName = StripGuid(StripExtension(name));
            var content = Canonicalizer.LooksLikeText(file.Content)
                ? Canonicalizer.CanonicalizeText(file.Text())
                : Canonicalizer.CanonicalizeBinary(file.Content);
            components.Add(Make(ComponentType.WebResource, ScopedName(scope, resourceName), ExtractGuid(name), content));
            return;
        }

        if (firstSegment.Equals("Formulas", StringComparison.OrdinalIgnoreCase))
        {
            var formulaName = StripExtension(name).Replace("-FormulaDefinitions", "", StringComparison.OrdinalIgnoreCase);
            var content = name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? Canonicalizer.CanonicalizeXml(file.Text(), config)
                : Canonicalizer.CanonicalizeText(file.Text());
            components.Add(Make(ComponentType.Formula, ScopedName(scope, StripGuid(formulaName)), ExtractGuid(name), content));
            return;
        }

        if (rel.StartsWith("environmentvariabledefinitions/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(name, "environmentvariabledefinition.xml", StringComparison.OrdinalIgnoreCase))
        {
            var schemaName = rel.Split('/')[1];
            components.Add(Make(ComponentType.EnvironmentVariableDefinition, ScopedName(scope, schemaName), null, Canonicalizer.CanonicalizeXml(file.Text(), config)));
            return;
        }

        if (rel.StartsWith("appactions/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(name, "appaction.xml", StringComparison.OrdinalIgnoreCase))
        {
            components.Add(Make(ComponentType.AppAction, ScopedName(scope, rel.Split('/')[1]), null, Canonicalizer.CanonicalizeXml(file.Text(), config)));
            return;
        }

        if (firstSegment.Equals("Entities", StringComparison.OrdinalIgnoreCase))
        {
            ClassifyPacEntityFile(scope, rel, file, config, components);
            return;
        }

        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            components.Add(Make(ComponentType.PluginAssembly, ScopedName(scope, name[..^4]), null, Canonicalizer.CanonicalizeBinary(file.Content)));
            return;
        }

        // Fallback: visible as Other, keyed by full path.
        var fallbackContent = Canonicalizer.LooksLikeText(file.Content)
            ? Canonicalizer.CanonicalizeText(file.Text())
            : Canonicalizer.CanonicalizeBinary(file.Content);
        components.Add(new SolutionComponent(
            ComponentType.Other, rel, null, SolutionComponent.MakeKey(ComponentType.Other, rel), fallbackContent));
    }

    /// <summary>
    /// Merges all files under <paramref name="rootPrefix"/> that belong to the same
    /// logical component (canvas app, PCF control, plugin assembly, custom api) into
    /// one component whose canonical content is the deterministic concatenation of
    /// its files' canonical contents.
    /// </summary>
    private static void AddGroupedComponents(
        List<ScopedSnapshotFile> scopeFiles,
        string scope,
        string rootPrefix,
        ComponentType type,
        Func<string, string> groupName,
        DiffConfig config,
        List<SolutionComponent> components)
    {
        var groups = scopeFiles
            .Where(s => s.RelativePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                        s.RelativePath.Split('/').Length > 1)
            .GroupBy(s => groupName(s.RelativePath), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var content = new StringBuilder();
            foreach (var item in group.OrderBy(s => s.RelativePath, StringComparer.Ordinal))
            {
                content.Append("-- ").Append(item.RelativePath).Append('\n');
                content.Append(CanonicalizeAuto(item.File, config).TrimEnd('\n')).Append('\n');
            }

            var id = ExtractGuid(group.Key);
            var displayName = id is not null ? StripGuid(group.Key) : group.Key;
            components.Add(Make(type, ScopedName(scope, displayName), id, content.ToString()));
        }
    }

    /// <summary>
    /// Canvas app export files are named "&lt;app&gt;_&lt;role&gt;" (BackgroundImageUri,
    /// DocumentUri.msapp, AdditionalUris0_identity.json, ...); the group is the app prefix.
    /// </summary>
    private static string CanvasAppGroupName(string rel)
    {
        var fileName = rel.Split('/')[^1];
        var match = CanvasAppRoleRegex().Match(fileName);
        return match.Success ? match.Groups[1].Value : fileName;
    }

    private static string CanonicalizeAuto(RawFile file, DiffConfig config)
    {
        if (!Canonicalizer.LooksLikeText(file.Content))
        {
            return Canonicalizer.CanonicalizeBinary(file.Content);
        }

        if (file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return Canonicalizer.CanonicalizeXml(file.Text(), config);
        }

        if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Canonicalizer.CanonicalizeJson(file.Text());
        }

        return Canonicalizer.CanonicalizeText(file.Text());
    }

    private static string ScopedName(string scope, string name)
    {
        if (scope.Length == 0)
        {
            return name;
        }

        var lastSegment = scope.Split('/')[^1];
        return lastSegment + " / " + name;
    }

    private static SolutionComponent Make(ComponentType type, string name, string? id, string content) =>
        new(type, name, NormalizeGuid(id), SolutionComponent.MakeKey(type, name), content);

    private static string? NormalizeGuid(string? id) =>
        id?.Trim().Trim('{', '}').ToLowerInvariant() is { Length: > 0 } guid ? guid : null;

    private static string StripExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string? ExtractGuid(string name)
    {
        var match = GuidSuffixRegex().Match(name);
        return match.Success ? match.Value.TrimStart('-', '_', ' ') : null;
    }

    private static string StripGuid(string name) => GuidSuffixRegex().Replace(name, "");

    /// <summary>
    /// Two components with the same logical key (rare, e.g. duplicate web resource
    /// display names) are disambiguated so the differ can match them deterministically
    /// instead of collapsing or crashing.
    /// </summary>
    private static IReadOnlyList<SolutionComponent> Disambiguate(List<SolutionComponent> components) =>
        components
            .GroupBy(
                component => (component.ScopeIdentity ?? string.Empty) + "\u0001" + component.Key,
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(g =>
            {
                var list = g.ToList();
                if (list.Count == 1)
                {
                    return list;
                }

                return list
                    .Select((component, index) => component with
                    {
                        Key = string.Join('|', component.Key, component.Id ?? "no-id", index.ToString(CultureInfo.InvariantCulture))
                    })
                    .ToList();
            })
            .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex("[-_ ]?\\{?[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\\}?$", RegexOptions.CultureInvariant)]
    private static partial Regex GuidSuffixRegex();

    [GeneratedRegex("^(.+?)_(?:BackgroundImageUri|DocumentUri\\.msapp|AdditionalUris.*|metadata\\.json|Entropy.*|DataSources.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CanvasAppRoleRegex();
}
