using System.Xml;
using System.Xml.Linq;
using Dataverse.SolutionDiff.Model;

namespace Dataverse.SolutionDiff.Classification;

public static partial class ComponentExtractor
{
    private static XElement ParseRoot(RawFile file)
    {
        try
        {
            return XDocument.Parse(file.Text()).Root
                ?? throw new DiffException($"Snapshot file '{file.Path}' has no XML root element.");
        }
        catch (XmlException exception)
        {
            throw new DiffException(
                $"Snapshot file '{file.Path}' is not valid XML: {exception.Message}");
        }
    }
}