using AdoRead.AzureDevOps;
using AdoRead.Output;

namespace AdoRead.Wikis;

public sealed class WikiCatalogReader(IWikiSource source, Uri organizationUrl, TimeProvider timeProvider, ToolCommandNames commands)
{
    public async Task<SearchEnvelope<WikiCatalogDocument>> ReadWikisAsync(string project, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<SourceWiki> wikis = await source.GetWikisAsync(project, cancellationToken);
            return new("2.0", "wikiCatalog", wikis.Count == 0 ? "notFound" : "success", new WikiCatalogQuery(organizationUrl, project), timeProvider.GetUtcNow(), wikis.OrderBy(wiki => wiki.Name, StringComparer.Ordinal).Select(MapWiki).ToArray(), []);
        }
        catch (WikiSourceException exception) { return new("2.0", "wikiCatalog", "error", new WikiCatalogQuery(organizationUrl, project), timeProvider.GetUtcNow(), [], [new SearchError("azureDevOps", project, exception.Code, exception.Message, exception.Transient)]); }
    }

    public async Task<SearchEnvelope<WikiPageSummaryDocument>> ReadPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken)
    {
        try
        {
            SourceWikiPageCatalog catalog = await source.GetPagesAsync(project, wiki, version, top, continuationToken, cancellationToken);
            return new("2.0", "wikiPageCatalog", catalog.Pages.Count == 0 ? "notFound" : "success", new WikiPageCatalogQuery(project, wiki, version), timeProvider.GetUtcNow(), catalog.Pages.Select(page => new WikiPageSummaryDocument(page.Id, page.Path, PageHint(project, wiki, page.Path, version))).ToArray(), [], new PageMetadataDocument(null, top, catalog.Pages.Count, null, catalog.ContinuationToken, null));
        }
        catch (WikiSourceException exception) { return new("2.0", "wikiPageCatalog", "error", new WikiPageCatalogQuery(project, wiki, version), timeProvider.GetUtcNow(), [], [new SearchError("azureDevOps", project, exception.Code, exception.Message, exception.Transient)], new PageMetadataDocument(null, top, 0, null, null, null)); }
    }

    private WikiCatalogDocument MapWiki(SourceWiki wiki) => new(wiki.Id, wiki.Name, wiki.Type, wiki.ProjectId, wiki.MappedPath, wiki.Versions.OrderBy(version => version.Value, StringComparer.Ordinal).Select(version => new WikiCatalogVersionDocument(version.Value, version.Type, new CommandHintDocument(commands.Path("pages"), VersionArguments(wiki, version)))).ToArray(), new WikiPageLinksDocument(wiki.RestUrl, wiki.BrowserUrl));

    private static IReadOnlyList<string> VersionArguments(SourceWiki wiki, SourceWikiVersion version) => string.IsNullOrEmpty(version.Type)
        ? ["--project", wiki.ProjectId.ToString(), "--wiki", wiki.Id.ToString(), "--version", version.Value]
        : ["--project", wiki.ProjectId.ToString(), "--wiki", wiki.Id.ToString(), "--version", version.Value, "--version-type", version.Type];

    private CommandHintDocument PageHint(string project, string wiki, string? path, WikiVersionDocument? version)
    {
        if (string.IsNullOrEmpty(path)) throw new WikiSourceException("invalidAzureDevOpsResponse", "Azure DevOps returned a Wiki page without a path.", false);
        var arguments = new List<string> { "--project", project, "--wiki", wiki, "--path", path };
        if (version is not null) { arguments.Add("--version"); arguments.Add(version.Value); if (!string.IsNullOrEmpty(version.Type)) { arguments.Add("--version-type"); arguments.Add(version.Type); } }
        return new CommandHintDocument(commands.Path("page"), arguments);
    }
}
