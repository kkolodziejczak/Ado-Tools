using AdoRead.AzureDevOps;
using AdoRead.Output;

namespace AdoRead.Wikis;

public sealed class WikiPageReader(IWikiSource source, Uri organizationUrl, TimeProvider timeProvider)
{
    public async Task<SearchEnvelope<WikiPageDocument>> ReadAsync(WikiReference reference, CancellationToken cancellationToken)
    {
        var query = new WikiPageQuery(organizationUrl, reference.Project, reference.Wiki, reference.Selector switch { WikiPathSelector path => new("path", path.Value), WikiPageIdSelector id => new("pageId", id.Value.ToString()), WikiGitPathSelector path => new("gitPath", path.Value), _ => throw new InvalidOperationException() }, reference.Version);
        try { SourceWikiPage? page = await source.GetPageAsync(reference, cancellationToken); return page is null ? Create(query, "notFound", [], []) : Create(query, "success", [new(page.Id, page.Path, page.GitItemPath, reference.Selector is WikiGitPathSelector ? "repository" : "wikiApi", "markdown", page.Content, reference.Version, new(page.RestUrl, page.BrowserUrl))], []); }
        catch (WikiSourceException exception) { return Create(query, "error", [], [new SearchError("azureDevOps", null, exception.Code, exception.Message, exception.Transient)]); }
    }
    private SearchEnvelope<WikiPageDocument> Create(WikiPageQuery query, string status, IReadOnlyList<WikiPageDocument> results, IReadOnlyList<SearchError> errors) => new("2.0", "wikiPageLookup", status, query, timeProvider.GetUtcNow(), results, errors);
}
