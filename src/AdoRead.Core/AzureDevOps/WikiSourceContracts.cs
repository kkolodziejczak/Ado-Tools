using AdoRead.Wikis;

namespace AdoRead.AzureDevOps;

public interface IWikiSource
{
    Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<SourceWikiPageCatalog> GetPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken) => throw new NotSupportedException();
}
public interface IWikiSearchClient
{
    Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken);
}
public interface IWikiGitClient
{
    Task<string> GetMarkdownAsync(Guid projectId, Guid repositoryId, string path, WikiVersionDocument version, CancellationToken cancellationToken);
}
public interface IWikiClient
{
    Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<SourceWikiPage> GetPageByPathAsync(string project, string wiki, string path, WikiVersionDocument? version, CancellationToken cancellationToken);
    Task<SourceWikiPage> GetPageByIdAsync(string project, string wiki, int pageId, CancellationToken cancellationToken);
}
public sealed record SourceWikiPage(int? Id, string? Path, string? GitItemPath, string? Content, Uri? RestUrl, Uri? BrowserUrl);
public sealed record SourceWiki(Guid Id, string Name, string Type, Guid ProjectId, Guid? RepositoryId, string? MappedPath, IReadOnlyList<SourceWikiVersion> Versions, Uri? RestUrl, Uri? BrowserUrl);
public sealed record SourceWikiVersion(string Value, string? Type);
internal static class WikiVersionResolver
{
    public static WikiVersionDocument? Resolve(IReadOnlyList<SourceWikiVersion> versions, string? value, string? type)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        SourceWikiVersion[] matches = versions.Where(candidate => string.Equals(candidate.Value, value, StringComparison.Ordinal) && (string.IsNullOrWhiteSpace(type) || string.Equals(candidate.Type, type, StringComparison.OrdinalIgnoreCase))).ToArray();
        return matches.Length == 1 ? new WikiVersionDocument(matches[0].Value, matches[0].Type) : null;
    }
}
public sealed record SourceWikiPageSummary(int? Id, string? Path);
public sealed record SourceWikiPageCatalog(IReadOnlyList<SourceWikiPageSummary> Pages, string? ContinuationToken);
public sealed record SourceWikiSearchHit(string FileName, string GitPath, Guid ProjectId, string ProjectName, Guid WikiId, string WikiName, string? MappedPath, string Version, IReadOnlyList<SourceWikiSearchMatch> Matches);
public sealed record SourceWikiSearchMatch(string Field, IReadOnlyList<string> Highlights);
public sealed record SourceWikiSearchResult(IReadOnlyList<SourceWikiSearchHit> Hits, int Count, int InfoCode);
public interface IWikiPageCatalogClient
{
    Task<SourceWikiPageCatalog> GetPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken);
}
public sealed class WikiSourceException(string code, string message, bool transient, Exception? innerException = null) : Exception(message, innerException)
{ public string Code { get; } = code; public bool Transient { get; } = transient; }
