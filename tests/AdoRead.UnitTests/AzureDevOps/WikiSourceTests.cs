using AdoRead.AzureDevOps;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class WikiSourceTests
{
    [Fact]
    public async Task GetPageAsync_DispatchesPathWithVersionAndOnlyOneRead()
    {
        var client = new FakeClient(); var source = new WikiSource(client);
        await source.GetPageAsync(new WikiReference("example", "Project", "Wiki", new WikiPathSelector("/Page"), new WikiVersionDocument("main", "Branch")), CancellationToken.None);
        Assert.Equal(("Project", "Wiki", "/Page", "main"), client.PathCall); Assert.Equal(0, client.IdCalls);
    }
    [Fact]
    public async Task GetPageAsync_DispatchesIdWithoutVersion()
    {
        var client = new FakeClient(); var source = new WikiSource(client);
        await source.GetPageAsync(new WikiReference("example", "Project", "Wiki", new WikiPageIdSelector(51)), CancellationToken.None);
        Assert.Equal(51, client.Id); Assert.Null(client.PathCall);
    }
    private sealed class FakeClient : IWikiClient
    {
        public (string, string, string, string?)? PathCall { get; private set; }
        public int IdCalls { get; private set; }
        public int Id { get; private set; }
        public Task<SourceWikiPage> GetPageByPathAsync(string project, string wiki, string path, WikiVersionDocument? version, CancellationToken cancellationToken) { PathCall = (project, wiki, path, version?.Value); return Task.FromResult(Page()); }
        public Task<SourceWikiPage> GetPageByIdAsync(string project, string wiki, int pageId, CancellationToken cancellationToken) { IdCalls++; Id = pageId; return Task.FromResult(Page()); }
        private static SourceWikiPage Page() => new(51, "/Page", "/Page.md", "# Markdown", null, null);
    }
}
