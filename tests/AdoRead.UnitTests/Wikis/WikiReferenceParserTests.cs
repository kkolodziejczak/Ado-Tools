using AdoRead.Wikis;

namespace AdoRead.UnitTests.Wikis;

public sealed class WikiReferenceParserTests
{
    [Theory]
    [InlineData("https://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki/51/ignored", "example", "Payments", "Payments.wiki", 51)]
    [InlineData("https://example.visualstudio.com/Payments/_wiki/wikis/Payments.wiki/51/ignored", "example", "Payments", "Payments.wiki", 51)]
    [InlineData("https://dev.azure.com/example/Payments/_apis/wiki/wikis/Payments.wiki/pages/51", "example", "Payments", "Payments.wiki", 51)]
    public void ParseUrl_AcceptsPageIdShapes(string value, string organization, string project, string wiki, int id)
    {
        WikiReference reference = Assert.IsType<WikiReference>(WikiReferenceParser.ParseUrl(value));
        Assert.Equal(organization, reference.Organization);
        Assert.Equal(project, reference.Project);
        Assert.Equal(wiki, reference.Wiki);
        Assert.Equal(id, Assert.IsType<WikiPageIdSelector>(reference.Selector).Value);
    }

    [Fact]
    public void ParseUrl_AcceptsEncodedPathOnce()
    {
        WikiReference reference = Assert.IsType<WikiReference>(WikiReferenceParser.ParseUrl("https://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki?pagePath=%2FArchitecture%2FAuth%2520Flow"));
        Assert.Equal("/Architecture/Auth%20Flow", Assert.IsType<WikiPathSelector>(reference.Selector).Value);
    }

    [Theory]
    [InlineData("http://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki/51")]
    [InlineData("https://example.com/example/Payments/_wiki/wikis/Payments.wiki/51")]
    [InlineData("https://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki/0")]
    [InlineData("https://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki?pagePath=%ZZ")]
    public void ParseUrl_RejectsUnsafeOrMalformedValues(string value) => Assert.Null(WikiReferenceParser.ParseUrl(value));

    [Fact]
    public void ParseUrl_RejectsConflictingPageIdAndPath() => Assert.Null(WikiReferenceParser.ParseUrl("https://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki/51/x?pagePath=%2Fother"));

    [Fact]
    public void Create_RejectsUnknownVersionType() => Assert.Null(WikiReferenceParser.Create("Project", "Wiki", "/Page", null, "main", "unknown"));

    [Theory]
    [InlineData(".", "Wiki")]
    [InlineData("..", "Wiki")]
    [InlineData("Project/Child", "Wiki")]
    [InlineData("Project\\Child", "Wiki")]
    [InlineData("Project", ".")]
    [InlineData("Project", "..")]
    [InlineData("Project", " .. ")]
    [InlineData("Project", "../..")]
    [InlineData("Project", "a/../../b")]
    public void Create_RejectsUnsafeProjectAndWikiSegments(string project, string wiki) =>
        Assert.Null(WikiReferenceParser.Create(project, wiki, "/Page", null, null, null));

    [Fact]
    public void ParseUrl_RejectsMalformedEncodingInAnyQueryValue() => Assert.Null(WikiReferenceParser.ParseUrl("https://dev.azure.com/example/Payments/_wiki/wikis/Payments.wiki?pagePath=%2FPage&x=%ZZ"));
}
