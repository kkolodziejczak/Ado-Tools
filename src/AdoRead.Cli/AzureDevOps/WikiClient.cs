using AdoRead.Wikis;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Wiki.WebApi;

namespace AdoRead.AzureDevOps;

public interface IWikiPageSdk
{
    Task<WikiPageResponse> GetByPathAsync(string project, string wiki, string path, VersionControlRecursionType recursion, GitVersionDescriptor? version, bool includeContent, CancellationToken cancellationToken);
    Task<WikiPageResponse> GetByIdAsync(string project, string wiki, int pageId, VersionControlRecursionType recursion, bool includeContent, CancellationToken cancellationToken);
    Task<IReadOnlyList<WikiV2>> GetAllAsync(string project, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public sealed class WikiPageSdk(WikiHttpClient client) : IWikiPageSdk
{
    public Task<WikiPageResponse> GetByPathAsync(string project, string wiki, string path, VersionControlRecursionType recursion, GitVersionDescriptor? version, bool includeContent, CancellationToken cancellationToken) => client.GetPageAsync(project, wiki, path, recursion, version, includeContent, null, cancellationToken);
    public Task<WikiPageResponse> GetByIdAsync(string project, string wiki, int pageId, VersionControlRecursionType recursion, bool includeContent, CancellationToken cancellationToken) => client.GetPageByIdAsync(project, wiki, pageId, recursion, includeContent, null, cancellationToken);
    public async Task<IReadOnlyList<WikiV2>> GetAllAsync(string project, CancellationToken cancellationToken) => await client.GetAllWikisAsync(project, null, cancellationToken);
}

public sealed class SdkWikiClient(IWikiPageSdk client) : IWikiClient
{
    public async Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken) =>
        (await client.GetAllAsync(project, cancellationToken))
        .Select(wiki => new SourceWiki(wiki.Id, wiki.Name, ToLowerCamel(wiki.Type.ToString()), wiki.ProjectId, wiki.RepositoryId == Guid.Empty ? null : wiki.RepositoryId, wiki.MappedPath, wiki.Versions.Select(version => new SourceWikiVersion(version.Version, version.VersionType.ToString().ToLowerInvariant())).ToArray(), ToUri(wiki.Url), ToUri(wiki.RemoteUrl)))
        .ToArray();
    public async Task<SourceWikiPage> GetPageByPathAsync(string project, string wiki, string path, WikiVersionDocument? version, CancellationToken cancellationToken)
    {
        WikiPageResponse response = await client.GetByPathAsync(project, wiki, path, VersionControlRecursionType.None, ToVersion(version), true, cancellationToken);
        return Map(response.Page, response.Page.Content);
    }

    public async Task<SourceWikiPage> GetPageByIdAsync(string project, string wiki, int pageId, CancellationToken cancellationToken)
    {
        WikiPageResponse response = await client.GetByIdAsync(project, wiki, pageId, VersionControlRecursionType.None, true, cancellationToken);
        return Map(response.Page, response.Page.Content);
    }

    private static GitVersionDescriptor? ToVersion(WikiVersionDocument? version)
    {
        if (version is null) return null;
        if (!Enum.TryParse(version.Type, true, out GitVersionType type) || !Enum.IsDefined(type)) throw new WikiSourceException("invalidWikiReference", "The Wiki version type is invalid.", false);
        return new GitVersionDescriptor { Version = version.Value, VersionType = type };
    }
    private static SourceWikiPage Map(WikiPage page, string? content) => new(page.Id, page.Path, page.GitItemPath, content, ToUri(page.Url), ToUri(page.RemoteUrl));
    private static string ToLowerCamel(string value) => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
}
