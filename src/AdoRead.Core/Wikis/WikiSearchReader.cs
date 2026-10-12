using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Privacy;

namespace AdoRead.Wikis;

public sealed class WikiSearchReader(IWikiSource source, TimeProvider timeProvider, ToolCommandNames commands, EffectivePrivacy privacy)
{
    public async Task<SearchEnvelope<WikiSearchHitDocument>> ReadAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken)
    {
        var query = new WikiSearchQuery(text, project, wiki, skip, top);
        try
        {
            SourceWikiSearchResult result = await source.SearchAsync(text, project, wiki, skip, top, cancellationToken);
            IReadOnlyList<WikiSearchHitDocument> hits = result.Hits.OrderBy(hit => hit.GitPath, StringComparer.Ordinal).ThenBy(hit => hit.ProjectId).ThenBy(hit => hit.WikiId).ThenBy(hit => hit.FileName, StringComparer.Ordinal).Select(Map).ToArray();
            (string status, SearchError[] errors) = Status(result.InfoCode, hits.Count);
            if (status == "success" && result.Count == 0 && hits.Count == 0) status = "notFound";
            return PrivacyEnvelopes.Describe(new SearchEnvelope<WikiSearchHitDocument>("2.0", "wikiFullTextSearch", status, query, timeProvider.GetUtcNow(), hits, errors, new PageMetadataDocument(skip, top, hits.Count, result.Count, null, result.InfoCode)), privacy, WikiPrivacy.SearchCategories);
        }
        catch (WikiSourceException exception) { return new("2.0", "wikiFullTextSearch", "error", query, timeProvider.GetUtcNow(), [], [new SearchError("azureDevOps", project, exception.Code, exception.Message, exception.Transient)], new PageMetadataDocument(skip, top, 0, null, null, null)); }
    }

    private WikiSearchHitDocument Map(SourceWikiSearchHit hit) => WikiPrivacy.SearchHit(new(hit.FileName, hit.GitPath, new(hit.ProjectId, hit.ProjectName), new(hit.WikiId, hit.WikiName, hit.MappedPath, hit.Version), hit.Matches.Select(match => new WikiSearchMatchDocument(match.Field, match.Highlights)).ToArray(), new CommandHintDocument(commands.Path("page"), ["--project", hit.ProjectId.ToString(), "--wiki", hit.WikiId.ToString(), "--git-path", hit.GitPath, "--version", hit.Version])), privacy);

    private static (string Status, SearchError[] Errors) Status(int infoCode, int hits) => infoCode switch
    {
        0 => ("success", []),
        1 or 2 or 6 or 7 or 9 => Incomplete(hits, "Wiki search indexing is incomplete.", true),
        3 or 4 or 5 or 19 or 20 => ("error", [new SearchError("command", null, "invalidWikiSearch", "The Wiki search query is invalid.", false)]),
        8 => Incomplete(hits, "Azure DevOps trimmed the Wiki search result limit.", true),
        10 or 11 => Incomplete(hits, "Azure DevOps could not complete the requested Wiki search capability.", false),
        _ => Incomplete(hits, "Wiki search returned an unknown completeness code.", true)
    };

    private static (string Status, SearchError[] Errors) Incomplete(int hits, string message, bool transient) =>
        (hits > 0 ? "partial" : "error", [new SearchError("azureDevOps", null, "wikiSearchIncomplete", message, transient)]);
}
