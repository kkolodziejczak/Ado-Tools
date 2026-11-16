using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>Finds a pull request by its organization-wide ID, and where it lives, before any write to it.</summary>
internal static class PullRequestLookup
{
    public static async Task<(GitPullRequest PullRequest, PullRequestLocation Location)> FindAsync(
        IPullRequestCommentClient client,
        int pullRequestId,
        CancellationToken cancellationToken)
    {
        GitPullRequest pullRequest = await AzureDevOpsCall.RunAsync(
            () => client.GetPullRequestAsync(pullRequestId, cancellationToken),
            cancellationToken,
            WriteException.NotFound("pullRequestNotFound", $"Pull request {pullRequestId} was not found or the token cannot read it."));
        GitRepository? repository = pullRequest.Repository;
        if (repository?.ProjectReference is null)
        {
            throw WriteException.NotFound("pullRequestNotFound", $"Azure DevOps did not say which repository pull request {pullRequestId} belongs to.");
        }

        return (pullRequest, new PullRequestLocation(
            pullRequestId,
            repository.ProjectReference.Id,
            repository.ProjectReference.Name,
            repository.Id,
            repository.Name));
    }

    /// <summary>Like <see cref="FindAsync"/>, for a write that only an active pull request takes.</summary>
    public static async Task<(GitPullRequest PullRequest, PullRequestLocation Location)> FindActiveAsync(
        IPullRequestCommentClient client,
        int pullRequestId,
        CancellationToken cancellationToken)
    {
        (GitPullRequest pullRequest, PullRequestLocation location) = await FindAsync(client, pullRequestId, cancellationToken);
        return pullRequest.Status == PullRequestStatus.Active
            ? (pullRequest, location)
            : throw WriteException.Rejected(
                "pullRequestNotActive",
                $"Pull request {pullRequestId} is {pullRequest.Status.ToString().ToLowerInvariant()}; only an active pull request takes this change.");
    }

    public static Uri BrowserUrl(Uri organizationUrl, PullRequestLocation location, int? threadId = null) =>
        new(
            organizationUrl,
            $"{Uri.EscapeDataString(location.ProjectName)}/_git/{Uri.EscapeDataString(location.RepositoryName)}/pullrequest/{location.PullRequestId}"
            + (threadId is null ? string.Empty : $"?discussionId={threadId}"));
}
