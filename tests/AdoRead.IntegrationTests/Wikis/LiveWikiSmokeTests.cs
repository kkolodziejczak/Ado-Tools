using AdoRead.Output;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Wikis;

namespace AdoRead.IntegrationTests.Wikis;

public sealed class LiveWikiSmokeTests
{
    [Fact]
    public async Task ReadConfiguredWikiPage_WhenLiveTestsAreExplicitlyEnabled()
    {
        if (!LiveTestsEnabled()) return;

        (string project, string wiki) = WikiSelection();
        string path = RequiredEnvironmentVariable("ADO_READ_WIKI_PAGE_PATH");
        WikiReference? reference = WikiReferenceParser.Create(project, wiki, path, null, null, null, null);
        Assert.NotNull(reference);
        AdoReadConfiguration configuration = await new ConfigurationStore().LoadAsync(null, CancellationToken.None);
        string pat = LiveTestCredentials.TokenFor(configuration);
        await using IAdoSession session = new AdoSessionFactory().Create(configuration.OrganizationUrl, pat);
        using var timeout = new CancellationTokenSource(configuration.RequestTimeout);

        var reader = new WikiPageReader(session.Wikis, configuration.OrganizationUrl, TimeProvider.System);
        var result = await reader.ReadAsync(reference, timeout.Token);

        Assert.Equal("success", result.Status);
        Assert.NotEmpty(result.Results);
    }

    [Fact]
    public async Task SearchConfiguredWiki_WhenLiveTestsAreExplicitlyEnabled()
    {
        if (!LiveTestsEnabled()) return;

        (string project, string wiki) = WikiSelection();
        string searchText = RequiredEnvironmentVariable("ADO_READ_WIKI_SEARCH_TEXT");
        AdoReadConfiguration configuration = await new ConfigurationStore().LoadAsync(null, CancellationToken.None);
        string pat = LiveTestCredentials.TokenFor(configuration);
        await using IAdoSession session = new AdoSessionFactory().Create(configuration.OrganizationUrl, pat);
        using var timeout = new CancellationTokenSource(configuration.RequestTimeout);

        var reader = new WikiSearchReader(session.Wikis, TimeProvider.System, new ToolCommandNames("ado-read", "wiki"));
        var result = await reader.ReadAsync(searchText, project, null, 0, 10, timeout.Token);

        Assert.Contains(result.Status, new[] { "success", "partial" });
        WikiSearchHitDocument[] selected = result.Results.Where(hit => string.Equals(hit.Wiki.Id.ToString(), wiki, StringComparison.OrdinalIgnoreCase) || string.Equals(hit.Wiki.Name, wiki, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(selected);
        Assert.All(selected, hit => Assert.NotEmpty(hit.Matches));

        WikiSearchHitDocument hit = selected[0];
        WikiReference? reference = WikiReferenceParser.Create(hit.Project.Id.ToString(), hit.Wiki.Id.ToString(), null, null, hit.GitPath, hit.Wiki.Version, null);
        Assert.NotNull(reference);
        var page = await new WikiPageReader(session.Wikis, configuration.OrganizationUrl, TimeProvider.System).ReadAsync(reference, timeout.Token);
        Assert.Equal("success", page.Status);
        Assert.NotEmpty(page.Results);
    }

    private static bool LiveTestsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("ADO_READ_LIVE_TESTS"), "1", StringComparison.Ordinal);

    private static (string Project, string Wiki) WikiSelection() =>
        (RequiredEnvironmentVariable("ADO_READ_WIKI_PROJECT"), RequiredEnvironmentVariable("ADO_READ_WIKI"));

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the live Wiki smoke test.");
}
