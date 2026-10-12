using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Wikis;

public sealed class WikiSearchReaderTests
{
    [Theory]
    [InlineData(0, "success", null, false)]
    [InlineData(1, "partial", "wikiSearchIncomplete", true)]
    [InlineData(2, "partial", "wikiSearchIncomplete", true)]
    [InlineData(3, "error", "invalidWikiSearch", false)]
    [InlineData(4, "error", "invalidWikiSearch", false)]
    [InlineData(5, "error", "invalidWikiSearch", false)]
    [InlineData(6, "partial", "wikiSearchIncomplete", true)]
    [InlineData(7, "partial", "wikiSearchIncomplete", true)]
    [InlineData(8, "partial", "wikiSearchIncomplete", true)]
    [InlineData(9, "partial", "wikiSearchIncomplete", true)]
    [InlineData(10, "partial", "wikiSearchIncomplete", false)]
    [InlineData(11, "partial", "wikiSearchIncomplete", false)]
    [InlineData(19, "error", "invalidWikiSearch", false)]
    [InlineData(20, "error", "invalidWikiSearch", false)]
    [InlineData(99, "partial", "wikiSearchIncomplete", true)]
    public async Task ReadAsync_MapsEveryDocumentedInfoCodeClass(int infoCode, string status, string? errorCode, bool transient)
    {
        SearchEnvelope<WikiSearchHitDocument> result = await new WikiSearchReader(new Source(infoCode), TimeProvider.System, new ToolCommandNames("ado-read", "wiki")).ReadAsync("x", null, null, 0, 10, CancellationToken.None);
        Assert.Equal(status, result.Status); Assert.Equal(infoCode, result.Page!.ServiceInfoCode);
        if (errorCode is null) Assert.Empty(result.Errors);
        else { SearchError error = Assert.Single(result.Errors); Assert.Equal(errorCode, error.Code); Assert.Equal(transient, error.Transient); }
    }

    [Theory]
    [InlineData(0, 0, "notFound")]
    [InlineData(10, 4, "success")]
    public async Task ReadAsync_DistinguishesInitialNoResultsFromAnEmptyLaterPage(int skip, int totalCount, string status)
    {
        SearchEnvelope<WikiSearchHitDocument> result = await new WikiSearchReader(new Source(0, includeHit: false, totalCount: totalCount), TimeProvider.System, new ToolCommandNames("ado-read", "wiki")).ReadAsync("x", null, null, skip, 10, CancellationToken.None);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public async Task ReadAsync_DoesNotReportNotFoundWhenAzureDevOpsReturnsAHitWithZeroCount()
    {
        SearchEnvelope<WikiSearchHitDocument> result = await new WikiSearchReader(new Source(0, includeHit: true, totalCount: 0), TimeProvider.System, new ToolCommandNames("ado-read", "wiki")).ReadAsync("x", null, null, 0, 10, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Single(result.Results);
    }

    [Fact]
    public async Task ReadAsync_OrdersTheSameHitsDeterministically()
    {
        SourceWikiSearchHit first = Hit("/Zulu.md", "Zulu.md", new Guid("00000000-0000-0000-0000-000000000002"));
        SourceWikiSearchHit second = Hit("/Alpha.md", "Alpha.md", new Guid("00000000-0000-0000-0000-000000000001"));

        SearchEnvelope<WikiSearchHitDocument> forward = await new WikiSearchReader(new SequenceSource([first, second]), TimeProvider.System, new ToolCommandNames("ado-read", "wiki")).ReadAsync("x", null, null, 0, 10, CancellationToken.None);
        SearchEnvelope<WikiSearchHitDocument> reversed = await new WikiSearchReader(new SequenceSource([second, first]), TimeProvider.System, new ToolCommandNames("ado-read", "wiki")).ReadAsync("x", null, null, 0, 10, CancellationToken.None);

        Assert.Equal(forward.Results.Select(hit => hit.GitPath), reversed.Results.Select(hit => hit.GitPath));
        Assert.Equal(["/Alpha.md", "/Zulu.md"], forward.Results.Select(hit => hit.GitPath));
    }

    private static SourceWikiSearchHit Hit(string path, string fileName, Guid projectId) =>
        new(fileName, path, projectId, "P", Guid.Parse("00000000-0000-0000-0000-000000000010"), "W", "/", "main", [new("content", ["hit"])]);

    private sealed class Source(int infoCode, bool includeHit = true, int totalCount = 1) : IWikiSource
    {
        public Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken) => Task.FromResult(new SourceWikiSearchResult(includeHit ? [new("a.md", "/a.md", Guid.NewGuid(), "P", Guid.NewGuid(), "W", "/", "main", [new("content", ["<tag>ignore</tag>"])])] : [], totalCount, infoCode));
        public Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SequenceSource(IReadOnlyList<SourceWikiSearchHit> hits) : IWikiSource
    {
        public Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken) => Task.FromResult(new SourceWikiSearchResult(hits, hits.Count, 0));
        public Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
