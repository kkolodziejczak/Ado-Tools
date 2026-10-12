using AdoRead.AzureDevOps;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class WikiGitHandoffTests
{
    [Theory]
    [InlineData("/docs/../secret.md")]
    [InlineData("/docsx/page.md")]
    [InlineData("/docs/page.txt")]
    public async Task GetPageAsync_RejectsUnsafeGitPathsBeforeGitRead(string path)
    {
        var git = new Git(); var source = new WikiSource(new Client(), gitClient: git);
        await Assert.ThrowsAsync<WikiSourceException>(() => source.GetPageAsync(new WikiReference("org", "Project", "Wiki", new WikiGitPathSelector(path), new WikiVersionDocument("main", null)), CancellationToken.None));
        Assert.Equal(0, git.Calls);
    }

    [Fact]
    public async Task GetPageAsync_UsesResolvedWikiIdsExactPathAndExactVersion()
    {
        var git = new Git(); var source = new WikiSource(new Client(), gitClient: git);
        SourceWikiPage? page = await source.GetPageAsync(new WikiReference("org", "Project", "Wiki", new WikiGitPathSelector("/docs/Page.md"), new WikiVersionDocument("main", null)), CancellationToken.None);
        Assert.Equal((Guid.Parse("00000000-0000-0000-0000-000000000101"), Guid.Parse("00000000-0000-0000-0000-000000000501"), "/docs/Page.md", "main", "branch"), git.Call);
        Assert.Equal("# Page", page!.Content);
    }

    [Fact]
    public async Task GetPageAsync_UsesProjectWikiIdWhenRepositoryIdIsMissing()
    {
        Guid wikiId = Guid.Parse("00000000-0000-0000-0000-000000000401");
        var git = new Git(); var source = new WikiSource(new Client(new(wikiId, "Wiki", "projectWiki", Guid.Parse("00000000-0000-0000-0000-000000000101"), null, "/docs", [new("main", "branch")], null, null)), gitClient: git);

        await source.GetPageAsync(new WikiReference("org", "Project", "Wiki", new WikiGitPathSelector("/docs/Page.md"), new WikiVersionDocument("main", null)), CancellationToken.None);

        Assert.Equal(wikiId, git.Call.Item2);
    }

    [Fact]
    public async Task GetPageAsync_RejectsCodeWikiWithoutRepositoryIdBeforeGitRead()
    {
        var git = new Git(); var source = new WikiSource(new Client(new(Guid.NewGuid(), "Wiki", "codeWiki", Guid.NewGuid(), null, "/docs", [new("main", "branch")], null, null)), gitClient: git);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => source.GetPageAsync(new WikiReference("org", "Project", "Wiki", new WikiGitPathSelector("/docs/Page.md"), new WikiVersionDocument("main", null)), CancellationToken.None));

        Assert.Equal("wikiRepositoryRequired", error.Code);
        Assert.Equal(0, git.Calls);
    }

    private sealed class Client(SourceWiki? wiki = null) : IWikiClient
    {
        public Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SourceWiki>>([wiki ?? new(Guid.Parse("00000000-0000-0000-0000-000000000401"), "Wiki", "codeWiki", Guid.Parse("00000000-0000-0000-0000-000000000101"), Guid.Parse("00000000-0000-0000-0000-000000000501"), "/docs", [new("main", "branch")], null, null)]);
        public Task<SourceWikiPage> GetPageByPathAsync(string project, string wiki, string path, WikiVersionDocument? version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SourceWikiPage> GetPageByIdAsync(string project, string wiki, int pageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Git : IWikiGitClient
    {
        public int Calls { get; private set; }
        public (Guid, Guid, string, string, string?) Call { get; private set; }
        public Task<string> GetMarkdownAsync(Guid projectId, Guid repositoryId, string path, WikiVersionDocument version, CancellationToken cancellationToken) { Calls++; Call = (projectId, repositoryId, path, version.Value, version.Type); return Task.FromResult("# Page"); }
    }
}
