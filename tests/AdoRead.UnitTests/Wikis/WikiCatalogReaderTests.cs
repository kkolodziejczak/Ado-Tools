using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Wikis;

public sealed class WikiCatalogReaderTests
{
    [Fact]
    public async Task ReadPagesAsync_MapsMissingPagePathToAStableError()
    {
        var reader = new WikiCatalogReader(new Source(), new Uri("https://dev.azure.com/fabricated/"), TimeProvider.System, new ToolCommandNames("ado-read", "wiki"));

        SearchEnvelope<WikiPageSummaryDocument> result = await reader.ReadPagesAsync("Project", "Wiki", null, 50, null, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("invalidAzureDevOpsResponse", Assert.Single(result.Errors).Code);
    }

    private sealed class Source : IWikiSource
    {
        public Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SourceWikiPageCatalog> GetPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken) => Task.FromResult(new SourceWikiPageCatalog([new(1, null)], null));
    }
}
