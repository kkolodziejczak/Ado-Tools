using Microsoft.TeamFoundation.SourceControl.WebApi;
using AdoRead.Wikis;

namespace AdoRead.AzureDevOps;

public sealed class WikiGitClient(GitHttpClient client) : IWikiGitClient
{
    public async Task<string> GetMarkdownAsync(Guid projectId, Guid repositoryId, string path, WikiVersionDocument version, CancellationToken cancellationToken)
    {
        var descriptor = new GitVersionDescriptor { Version = version.Value };
        if (Enum.TryParse(version.Type, true, out GitVersionType type)) descriptor.VersionType = type;
        GitItem item = await client.GetItemAsync(projectId, repositoryId, path, scopePath: null, recursionLevel: VersionControlRecursionType.None, includeContentMetadata: false, latestProcessedChange: false, download: false, versionDescriptor: descriptor, includeContent: true, resolveLfs: false, sanitize: false, userState: null, cancellationToken);
        return item.Content ?? string.Empty;
    }
}
