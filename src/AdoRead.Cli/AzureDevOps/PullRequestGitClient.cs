using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public interface IPullRequestGitClient
{
    /// <summary>
    /// Organization-scoped lookup. Pull-request IDs are unique across an organization, so this
    /// resolves the owning project without the caller naming it.
    /// </summary>
    Task<GitPullRequest> GetByIdAsync(int pullRequestId, CancellationToken cancellationToken);

    Task<GitPullRequest> GetPullRequestAsync(Guid projectId, int pullRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceRef>> GetWorkItemsAsync(
        Guid projectId,
        Guid repositoryId,
        int pullRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(
        Guid projectId,
        Guid repositoryId,
        int pullRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(
        Guid projectId,
        Guid repositoryId,
        int pullRequestId,
        CancellationToken cancellationToken);

}

public sealed class SdkPullRequestGitClient(GitHttpClient client) : IPullRequestGitClient
{
    public Task<GitPullRequest> GetByIdAsync(int pullRequestId, CancellationToken cancellationToken) =>
        client.GetPullRequestByIdAsync(pullRequestId, userState: null, cancellationToken);

    public Task<GitPullRequest> GetPullRequestAsync(
        Guid projectId,
        int pullRequestId,
        CancellationToken cancellationToken) =>
        client.GetPullRequestByIdAsync(projectId, pullRequestId, userState: null, cancellationToken);

    public async Task<IReadOnlyList<ResourceRef>> GetWorkItemsAsync(
        Guid projectId,
        Guid repositoryId,
        int pullRequestId,
        CancellationToken cancellationToken) =>
        await client.GetPullRequestWorkItemRefsAsync(
            projectId,
            repositoryId,
            pullRequestId,
            userState: null,
            cancellationToken);

    public async Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(
        Guid projectId,
        Guid repositoryId,
        int pullRequestId,
        CancellationToken cancellationToken) =>
        await client.GetThreadsAsync(
            projectId,
            repositoryId,
            pullRequestId,
            iteration: null,
            baseIteration: null,
            userState: null,
            cancellationToken);

    public async Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(
        Guid projectId,
        Guid repositoryId,
        int pullRequestId,
        CancellationToken cancellationToken) =>
        await client.GetPullRequestIterationsAsync(
            projectId,
            repositoryId,
            pullRequestId,
            includeCommits: true,
            userState: null,
            cancellationToken);

}
