using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>The calls <c>pr create</c> needs: find the repository, find an open pull request for the same branches, create one.</summary>
public interface IPullRequestCreateClient
{
    /// <summary>A repository by ID anywhere in the organization, or by name in <paramref name="project"/>.</summary>
    Task<GitRepository> GetRepositoryAsync(string? project, string repository, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitPullRequest>> GetActivePullRequestsAsync(
        GitRepository repository,
        string sourceRefName,
        string targetRefName,
        CancellationToken cancellationToken);

    Task<GitPullRequest> CreatePullRequestAsync(
        GitRepository repository,
        GitPullRequest pullRequest,
        CancellationToken cancellationToken);
}

public sealed class SdkPullRequestCreateClient(GitHttpClient client) : IPullRequestCreateClient
{
    public Task<GitRepository> GetRepositoryAsync(string? project, string repository, CancellationToken cancellationToken) =>
        project is null
            ? client.GetRepositoryAsync(Guid.Parse(repository), userState: null, cancellationToken)
            : client.GetRepositoryAsync(project, repository, userState: null, cancellationToken);

    public async Task<IReadOnlyList<GitPullRequest>> GetActivePullRequestsAsync(
        GitRepository repository,
        string sourceRefName,
        string targetRefName,
        CancellationToken cancellationToken) =>
        await client.GetPullRequestsAsync(
            repository.Id,
            new GitPullRequestSearchCriteria
            {
                Status = PullRequestStatus.Active,
                SourceRefName = sourceRefName,
                TargetRefName = targetRefName
            },
            maxCommentLength: null,
            skip: null,
            top: null,
            userState: null,
            cancellationToken);

    public Task<GitPullRequest> CreatePullRequestAsync(
        GitRepository repository,
        GitPullRequest pullRequest,
        CancellationToken cancellationToken) =>
        client.CreatePullRequestAsync(
            pullRequest,
            repository.Id,
            supportsIterations: null,
            userState: null,
            cancellationToken);
}
