using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>The calls <c>pr create</c> needs: find the repository, find an open pull request for the same branches, create one.</summary>
public interface IPullRequestCreateClient
{
    /// <summary>A repository by ID anywhere in the organization, or by name in <paramref name="project"/>.</summary>
    Task<GitRepository> GetRepositoryAsync(string? project, string repository, CancellationToken cancellationToken);

    Task<bool> BranchExistsAsync(GitRepository repository, string refName, CancellationToken cancellationToken);

    /// <summary>Active pull requests between the two branches whose source is this repository, not a fork of it.</summary>
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
                SourceRepositoryId = repository.Id,
                SourceRefName = sourceRefName,
                TargetRefName = targetRefName
            },
            maxCommentLength: null,
            skip: null,
            top: null,
            userState: null,
            cancellationToken);

    /// <summary>The refs filter matches by prefix, so the exact name is checked among what it returns.</summary>
    public async Task<bool> BranchExistsAsync(GitRepository repository, string refName, CancellationToken cancellationToken)
    {
        List<GitRef> refs = await client.GetRefsAsync(
            repository.Id,
            filter: refName["refs/".Length..],
            userState: null,
            cancellationToken: cancellationToken);
        return refs.Any(gitRef => string.Equals(gitRef.Name, refName, StringComparison.Ordinal));
    }

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
