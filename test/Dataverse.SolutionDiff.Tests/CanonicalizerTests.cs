using Dataverse.SolutionDiff.Canonicalization;
using Dataverse.SolutionDiff.Configuration;
using Xunit;

namespace Dataverse.SolutionDiff.Tests;

public class CanonicalizerTests
{
    private static readonly DiffConfig Config = new();

    [Fact]
    public void Xml_VolatileNoise_ProducesIdenticalOutput()
    {
        var a = """
            <ImportExportXml OrganizationVersion="9.2.1.1" CRMServerServiceabilityVersion="9.2.1.1">
              <Entity><IntroducedVersion>1.0.0.1</IntroducedVersion><Name>Account</Name></Entity>
            </ImportExportXml>
            """;
        var b = """
            <ImportExportXml OrganizationVersion="9.9.9.9" CRMServerServiceabilityVersion="9.9.9.9">
              <Entity><IntroducedVersion>2.0.0.0</IntroducedVersion><Name>Account</Name></Entity>
            </ImportExportXml>
            """;

        Assert.Equal(Canonicalizer.CanonicalizeXml(a, Config), Canonicalizer.CanonicalizeXml(b, Config));
    }

    [Fact]
    public void Xml_StripRules_AreCaseInsensitive()
    {
        var config = new DiffConfig
        {
            StripElements = ["volatileelement"],
            StripAttributes = ["volatileattribute"],
        };
        var a = "<Root VolatileAttribute=\"old\"><VolatileElement>old</VolatileElement><Name>Account</Name></Root>";
        var b = "<Root VolatileAttribute=\"new\"><VolatileElement>new</VolatileElement><Name>Account</Name></Root>";

        Assert.Equal(Canonicalizer.CanonicalizeXml(a, config), Canonicalizer.CanonicalizeXml(b, config));
    }

    [Fact]
    public void Xml_ReorderedAttributesProduceIdenticalOutput_ButChildOrderIsPreserved()
    {
        var a = "<Root b=\"2\" a=\"1\"><Child x=\"1\"/><Child x=\"2\"/></Root>";
        var reorderedAttributes = "<Root a=\"1\" b=\"2\"><Child x=\"1\"/><Child x=\"2\"/></Root>";
        var reorderedChildren = "<Root a=\"1\" b=\"2\"><Child x=\"2\"/><Child x=\"1\"/></Root>";

        Assert.Equal(Canonicalizer.CanonicalizeXml(a, Config), Canonicalizer.CanonicalizeXml(reorderedAttributes, Config));
        Assert.NotEqual(Canonicalizer.CanonicalizeXml(a, Config), Canonicalizer.CanonicalizeXml(reorderedChildren, Config));
    }

    [Fact]
    public void Xml_Output_IsDeterministicAndLfNormalized()
    {
        var xml = "<Root>\r\n  <Name>Account</Name>\r\n</Root>";
        var first = Canonicalizer.CanonicalizeXml(xml, Config);
        var second = Canonicalizer.CanonicalizeXml(xml, Config);

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_ReorderedProperties_ProduceIdenticalOutput()
    {
        var a = """{ "b": 1, "a": { "y": true, "x": [1, 2] } }""";
        var b = """{ "a": { "x": [1, 2], "y": true }, "b": 1 }""";

        Assert.Equal(Canonicalizer.CanonicalizeJson(a), Canonicalizer.CanonicalizeJson(b));
    }

    [Fact]
    public void Json_ArraysKeepOrder()
    {
        var a = """{ "x": [1, 2] }""";
        var b = """{ "x": [2, 1] }""";

        Assert.NotEqual(Canonicalizer.CanonicalizeJson(a), Canonicalizer.CanonicalizeJson(b));
    }

    [Fact]
    public void Text_NormalizesLineEndingsAndTrailingWhitespace()
    {
        var a = "line1  \r\nline2\r\n";
        var b = "line1\nline2\n";

        Assert.Equal(Canonicalizer.CanonicalizeText(b), Canonicalizer.CanonicalizeText(a));
    }

    [Fact]
    public void LooksLikeText_DetectsBinary()
    {
        Assert.True(Canonicalizer.LooksLikeText("console.log(1);"u8.ToArray()));
        Assert.False(Canonicalizer.LooksLikeText(new byte[] { 0x4D, 0x5A, 0x00, 0x01 }));
    }
}
