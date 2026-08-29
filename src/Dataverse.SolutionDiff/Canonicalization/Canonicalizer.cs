using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Dataverse.SolutionDiff.Configuration;

namespace Dataverse.SolutionDiff.Canonicalization;

/// <summary>
/// Produces the deterministic, noise-free representation of a component that the
/// differ compares. Canonicalized output never leaves the tool, so it only needs
/// to be consistent, not valid-for-reimport. Identical semantic content must always
/// produce byte-identical canonical output.
/// </summary>
public static class Canonicalizer
{
    /// <summary>Canonicalizes XML: strips volatile elements/attributes, sorts attributes and child elements, normalizes whitespace and line endings.</summary>
    public static string CanonicalizeXml(string xml, DiffConfig config)
    {
        XDocument doc;
        try
        {
            // Default load options discard insignificant whitespace.
            doc = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return CanonicalizeText(xml);
        }

        if (doc.Root is null)
        {
            return CanonicalizeText(xml);
        }

        CanonicalizeElement(doc.Root, config);

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            doc.Save(writer);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Canonicalizes a solution.xml manifest. Identical to <see cref="CanonicalizeXml"/>
    /// except that the solution version (SolutionManifest/Version) is dropped: Dataverse
    /// and export pipelines auto-increment it between exports, which would otherwise
    /// report every solution manifest as Modified in every comparison.
    /// </summary>
    public static string CanonicalizeSolutionManifest(string xml, DiffConfig config)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return CanonicalizeText(xml);
        }

        if (doc.Root is null)
        {
            return CanonicalizeText(xml);
        }

        foreach (var manifest in doc.Root.DescendantsAndSelf("SolutionManifest").ToList())
        {
            manifest.Elements("Version").Remove();
        }

        return CanonicalizeXml(doc.ToString(SaveOptions.DisableFormatting), config);
    }

    /// <summary>Canonicalizes JSON: recursively sorts object properties, normalizes formatting and line endings.</summary>
    public static string CanonicalizeJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                WriteValue(doc.RootElement, writer);
            }

            return Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return CanonicalizeText(json);
        }
    }

    /// <summary>Normalizes text (YAML formula definitions, plain web resources): LF endings, no trailing whitespace, single trailing newline.</summary>
    public static string CanonicalizeText(string text)
    {
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines).TrimEnd('\n') + "\n";
    }

    /// <summary>Binary content (plugin assemblies, images) is compared by hash only.</summary>
    public static string CanonicalizeBinary(byte[] content) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Heuristic for extensionless files (e.g. DAXIF-extracted web resources): valid UTF-8 without NUL bytes is treated as text.</summary>
    public static bool LooksLikeText(byte[] content)
    {
        var limit = Math.Min(content.Length, 8000);
        for (var i = 0; i < limit; i++)
        {
            if (content[i] == 0)
            {
                return false;
            }
        }

        try
        {
            new UTF8Encoding(false, true).GetString(content, 0, limit);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static void CanonicalizeElement(XElement element, DiffConfig config)
    {
        element.Attributes()
            .Where(a => config.ShouldStripAttribute(a.Name.LocalName))
            .Remove();

        // Volatile text values embedded *inside* otherwise-meaningful fields. The
        // canonical case is DAXIF stamping a sync timestamp into plugin-step
        // <Description> ("Synced with DAXIF# v.x by 'user' at <timestamp>"), which
        // makes every step look changed on every export.
        foreach (var node in element.Nodes().OfType<XText>())
        {
            node.Value = NormalizeTextValue(node.Value);
        }

        var sortedAttributes = element.Attributes()
            .OrderBy(a => a.Name.LocalName, StringComparer.Ordinal)
            .ToList();
        foreach (var attribute in sortedAttributes)
        {
            attribute.Remove();
        }

        element.Add(sortedAttributes.Cast<object>().ToArray());

        foreach (var child in element.Elements().ToList())
        {
            if (config.ShouldStripElement(child.Name.LocalName))
            {
                child.Remove();
                continue;
            }

            CanonicalizeElement(child, config);
        }

    }

    // Matches DAXIF / sync-tool timestamps embedded in text fields, e.g.
    // "Synced with DAXIF# v.1.0.0.0 by 'runneradmin' at 2026-08-07 09:59:08 GMT+00:00".
    private static readonly System.Text.RegularExpressions.Regex SyncTimestampRegex =
        new(@"Synced with DAXIF#.*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches the auto-incremented assembly version inside a fully-qualified assembly
    // name (e.g. PluginTypeName "Foo.Plugins.Bar, ILMerged.Plugins, Version=1.0.583.596,
    // Culture=neutral, PublicKeyToken=..."). Every plugin build bumps the version, which
    // would otherwise mark every referencing step as Modified. The assembly binary itself
    // is separately hash-compared, so the version string here is redundant build noise.
    private static readonly System.Text.RegularExpressions.Regex AssemblyVersionRegex =
        new(@", Version=\d+\.\d+\.\d+\.\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string NormalizeTextValue(string value)
    {
        if (SyncTimestampRegex.IsMatch(value))
        {
            return SyncTimestampRegex.Replace(value, "Synced with DAXIF#");
        }

        if (AssemblyVersionRegex.IsMatch(value))
        {
            return AssemblyVersionRegex.Replace(value, string.Empty);
        }

        return value;
    }
    private static void WriteValue(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteValue(item, writer);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
