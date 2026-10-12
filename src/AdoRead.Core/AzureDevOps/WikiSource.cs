using System.Net;
using AdoRead.Wikis;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public sealed class WikiSource(IWikiClient client, IWikiPageCatalogClient? pageCatalogClient = null, IWikiSearchClient? searchClient = null, IWikiGitClient? gitClient = null) : IWikiSource
{
    public async Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken)
    {
        try { return reference.Selector switch { WikiPathSelector path => await client.GetPageByPathAsync(reference.Project, reference.Wiki, path.Value, reference.Version, cancellationToken), WikiPageIdSelector id => await client.GetPageByIdAsync(reference.Project, reference.Wiki, id.Value, cancellationToken), WikiGitPathSelector path => await GetGitPageAsync(reference, path, cancellationToken), _ => throw new InvalidOperationException() }; }
        catch (VssServiceResponseException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound) { return null; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception)) { throw AdoReadExceptionTranslator.WikiFailure(exception); }
    }

    public async Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken)
    {
        try { return await client.GetWikisAsync(project, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception)) { throw AdoReadExceptionTranslator.WikiFailure(exception); }
    }

    public async Task<SourceWikiPageCatalog> GetPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken)
    {
        try { return await (pageCatalogClient ?? throw new NotSupportedException()).GetPagesAsync(project, wiki, version, top, continuationToken, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception)) { throw AdoReadExceptionTranslator.WikiFailure(exception); }
    }

    public async Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken)
    {
        try { return await (searchClient ?? throw new NotSupportedException()).SearchAsync(text, project, wiki, skip, top, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception)) { throw AdoReadExceptionTranslator.WikiFailure(exception); }
    }

    private async Task<SourceWikiPage> GetGitPageAsync(WikiReference reference, WikiGitPathSelector selector, CancellationToken cancellationToken)
    {
        SourceWiki[] matches = (await client.GetWikisAsync(reference.Project, cancellationToken)).Where(candidate => string.Equals(candidate.Id.ToString(), reference.Wiki, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.Name, reference.Wiki, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new WikiSourceException("wikiRequired", "The selected Wiki must resolve to exactly one Wiki.", false);
        SourceWiki wiki = matches[0];
        if (reference.Version is null) throw new WikiSourceException("invalidWikiReference", "A Git path requires an exact Wiki version.", false);
        WikiVersionDocument exactVersion = WikiVersionResolver.Resolve(wiki.Versions, reference.Version.Value, reference.Version.Type) ?? throw new WikiSourceException("invalidWikiReference", "The Wiki version must match exactly one advertised version.", false);
        string path = ValidateGitPath(selector.Value, wiki.MappedPath);
        Guid repositoryId = wiki.RepositoryId ?? (string.Equals(wiki.Type, "projectWiki", StringComparison.OrdinalIgnoreCase) ? wiki.Id : throw new WikiSourceException("wikiRepositoryRequired", "The selected Wiki does not advertise a backing repository.", false));
        string content = await (gitClient ?? throw new NotSupportedException()).GetMarkdownAsync(wiki.ProjectId, repositoryId, path, exactVersion, cancellationToken);
        return new SourceWikiPage(null, path, path, content, null, null);
    }

    private static string ValidateGitPath(string value, string? mappedPath)
    {
        if (!value.StartsWith('/') || !value.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || value.Contains("//", StringComparison.Ordinal) || value.Split('/').Any(part => part is "." or "..") || value.EndsWith("/.order", StringComparison.OrdinalIgnoreCase)) throw new WikiSourceException("invalidWikiGitPath", "The Git path must be one Markdown file in the selected Wiki.", false);
        string boundary = string.IsNullOrEmpty(mappedPath) || mappedPath == "/" ? "/" : mappedPath.TrimEnd('/') + "/";
        if (!value.StartsWith(boundary, StringComparison.Ordinal)) throw new WikiSourceException("invalidWikiGitPath", "The Git path is outside the selected Wiki.", false);
        return value;
    }
}
