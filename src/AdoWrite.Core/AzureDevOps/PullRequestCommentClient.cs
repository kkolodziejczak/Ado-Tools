using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>The pull-request calls a comment needs: find the anchor, find earlier posts, then post.</summary>
public interface IPullRequestCommentClient
{
    /// <summary>Organization-scoped lookup; the result names the owning project and repository.</summary>
    Task<GitPullRequest> GetPullRequestAsync(int pullRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(
        PullRequestLocation location,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(
        PullRequestLocation location,
        CancellationToken cancellationToken);

    /// <summary>Every file the iteration changes, compared with the common commit of source and target.</summary>
    Task<IReadOnlyList<GitPullRequestChange>> GetChangesAsync(
        PullRequestLocation location,
        int iterationId,
        CancellationToken cancellationToken);

    Task<string> GetFileTextAsync(
        PullRequestLocation location,
        string path,
        string commitId,
        CancellationToken cancellationToken);

    Task<GitPullRequestCommentThread> CreateThreadAsync(
        PullRequestLocation location,
        GitPullRequestCommentThread thread,
        CancellationToken cancellationToken);

    Task<Comment> CreateCommentAsync(
        PullRequestLocation location,
        int threadId,
        Comment comment,
        CancellationToken cancellationToken);
}

/// <summary>Where a pull request lives. Every call after the lookup is scoped to it.</summary>
public sealed record PullRequestLocation(
    int PullRequestId,
    Guid ProjectId,
    string ProjectName,
    Guid RepositoryId,
    string RepositoryName);

public sealed class SdkPullRequestCommentClient(GitHttpClient client) : IPullRequestCommentClient
{
    private const int ChangePageSize = 1000;

    public Task<GitPullRequest> GetPullRequestAsync(int pullRequestId, CancellationToken cancellationToken) =>
        client.GetPullRequestByIdAsync(pullRequestId, userState: null, cancellationToken);

    public async Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(
        PullRequestLocation location,
        CancellationToken cancellationToken) =>
        await client.GetThreadsAsync(
            location.ProjectId,
            location.RepositoryId,
            location.PullRequestId,
            iteration: null,
            baseIteration: null,
            userState: null,
            cancellationToken);

    public async Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(
        PullRequestLocation location,
        CancellationToken cancellationToken) =>
        await client.GetPullRequestIterationsAsync(
            location.ProjectId,
            location.RepositoryId,
            location.PullRequestId,
            includeCommits: false,
            userState: null,
            cancellationToken);

    public async Task<IReadOnlyList<GitPullRequestChange>> GetChangesAsync(
        PullRequestLocation location,
        int iterationId,
        CancellationToken cancellationToken)
    {
        var changes = new List<GitPullRequestChange>();
        int? skip = 0;
        while (skip is not null)
        {
            GitPullRequestIterationChanges page = await client.GetPullRequestIterationChangesAsync(
                location.ProjectId,
                location.RepositoryId,
                location.PullRequestId,
                iterationId,
                top: ChangePageSize,
                skip: skip,
                compareTo: null,
                userState: null,
                cancellationToken);
            changes.AddRange(page.ChangeEntries ?? []);
            skip = page.NextSkip > 0 ? page.NextSkip : null;
        }

        return changes;
    }

    public async Task<string> GetFileTextAsync(
        PullRequestLocation location,
        string path,
        string commitId,
        CancellationToken cancellationToken)
    {
        await using Stream content = await client.GetItemTextAsync(
            location.ProjectId,
            location.RepositoryId,
            path,
            scopePath: null,
            recursionLevel: null,
            includeContentMetadata: null,
            latestProcessedChange: null,
            download: null,
            versionDescriptor: new GitVersionDescriptor
            {
                Version = commitId,
                VersionType = GitVersionType.Commit
            },
            includeContent: true,
            resolveLfs: true,
            sanitize: false,
            userState: null,
            cancellationToken);
        using var reader = new StreamReader(content);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public Task<GitPullRequestCommentThread> CreateThreadAsync(
        PullRequestLocation location,
        GitPullRequestCommentThread thread,
        CancellationToken cancellationToken) =>
        client.CreateThreadAsync(
            thread,
            location.ProjectId,
            location.RepositoryId,
            location.PullRequestId,
            userState: null,
            cancellationToken);

    public Task<Comment> CreateCommentAsync(
        PullRequestLocation location,
        int threadId,
        Comment comment,
        CancellationToken cancellationToken) =>
        client.CreateCommentAsync(
            comment,
            location.ProjectId,
            location.RepositoryId,
            location.PullRequestId,
            threadId,
            userState: null,
            cancellationToken);
}
