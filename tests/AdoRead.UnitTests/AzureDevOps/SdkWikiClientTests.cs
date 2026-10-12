using AdoRead.AzureDevOps;
using AdoRead.Wikis;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Wiki.WebApi;
using Newtonsoft.Json;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class SdkWikiClientTests
{
    [Fact]
    public async Task GetPageByPathAsync_UsesOneContentReadWithoutRecursionAndMapsLeanFields()
    {
        var sdk = new FakeSdk(); var client = new SdkWikiClient(sdk);
        SourceWikiPage page = await client.GetPageByPathAsync("Project", "Wiki", "/Page", new WikiVersionDocument("main", "Branch"), CancellationToken.None);
        Assert.Equal(("Project", "Wiki", "/Page"), sdk.Path); Assert.Equal(VersionControlRecursionType.None, sdk.Recursion); Assert.True(sdk.IncludeContent); Assert.Equal("main", sdk.Version?.Version); Assert.Equal("# Markdown", page.Content);
    }
    [Fact]
    public async Task GetPageByIdAsync_UsesOneContentReadWithoutRecursion()
    {
        var sdk = new FakeSdk(); await new SdkWikiClient(sdk).GetPageByIdAsync("Project", "Wiki", 51, CancellationToken.None);
        Assert.Equal(51, sdk.Id); Assert.Equal(VersionControlRecursionType.None, sdk.Recursion); Assert.True(sdk.IncludeContent);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unsupported")]
    public async Task GetPageByPathAsync_RejectsMissingOrUnsupportedVersionType(string? versionType)
    {
        var sdk = new FakeSdk();

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => new SdkWikiClient(sdk).GetPageByPathAsync("Project", "Wiki", "/Page", new WikiVersionDocument("main", versionType), CancellationToken.None));

        Assert.Equal("invalidWikiReference", error.Code);
        Assert.Null(sdk.Path);
    }
    [Fact]
    public async Task GetWikisAsync_MapsSdkEnumValuesToTheJsonContract()
    {
        IReadOnlyList<SourceWiki> wikis = await new SdkWikiClient(new FakeSdk()).GetWikisAsync("Project", CancellationToken.None);
        Assert.Equal("projectWiki", wikis[0].Type);
        Assert.Equal("branch", wikis[0].Versions[0].Type);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000501"), wikis[0].RepositoryId);
    }
    private sealed class FakeSdk : IWikiPageSdk
    {
        public (string, string, string)? Path { get; private set; }
        public int Id { get; private set; }
        public VersionControlRecursionType Recursion { get; private set; }
        public GitVersionDescriptor? Version { get; private set; }
        public bool IncludeContent { get; private set; }
        public Task<WikiPageResponse> GetByPathAsync(string project, string wiki, string path, VersionControlRecursionType recursion, GitVersionDescriptor? version, bool includeContent, CancellationToken token) { Path = (project, wiki, path); Recursion = recursion; Version = version; IncludeContent = includeContent; return Task.FromResult(Page()); }
        public Task<WikiPageResponse> GetByIdAsync(string project, string wiki, int pageId, VersionControlRecursionType recursion, bool includeContent, CancellationToken token) { Id = pageId; Recursion = recursion; IncludeContent = includeContent; return Task.FromResult(Page()); }
        public Task<IReadOnlyList<WikiV2>> GetAllAsync(string project, CancellationToken token) => Task.FromResult<IReadOnlyList<WikiV2>>([new WikiV2 { Id = Guid.NewGuid(), Name = "Wiki", Type = WikiType.ProjectWiki, ProjectId = Guid.NewGuid(), RepositoryId = Guid.Parse("00000000-0000-0000-0000-000000000501"), Versions = [new GitVersionDescriptor { Version = "main", VersionType = GitVersionType.Branch }] }]);
        private static WikiPageResponse Page() => new() { Page = JsonConvert.DeserializeObject<WikiPage>("{ 'id': 51, 'path': '/Page', 'gitItemPath': '/Page.md', 'content': '# Markdown' }")! };
    }
}
