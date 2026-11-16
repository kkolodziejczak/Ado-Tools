using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.Reviews;

/// <summary>
/// Sets the token owner's vote on an active pull request. A vote equal to the current one sends
/// nothing, so running the command twice is safe.
/// </summary>
internal sealed class PullRequestVoter(
    IPullRequestCommentClient pullRequests,
    IPullRequestReviewClient reviews,
    IPeopleDirectory people,
    Uri organizationUrl)
{
    public async Task<CastVoteDocument> VoteAsync(
        int pullRequestId,
        PullRequestVote vote,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        (GitPullRequest pullRequest, PullRequestLocation location) =
            await PullRequestLookup.FindAsync(pullRequests, pullRequestId, cancellationToken);
        if (pullRequest.Status != PullRequestStatus.Active)
        {
            throw WriteException.Rejected(
                "pullRequestNotActive",
                $"Pull request {pullRequestId} is {pullRequest.Status.ToString().ToLowerInvariant()}; only an active pull request takes votes.");
        }

        Person voter = await AzureDevOpsCall.RunAsync(() => people.GetSignedInUserAsync(cancellationToken), cancellationToken);
        short previous = pullRequest.Reviewers?
            .FirstOrDefault(reviewer => Guid.TryParse(reviewer.Id, out Guid id) && id == voter.Id)?.Vote ?? 0;
        string outcome = previous == vote.Value ? VoteOutcomes.Unchanged
            : dryRun ? VoteOutcomes.Preview
            : VoteOutcomes.Voted;
        if (outcome == VoteOutcomes.Voted)
        {
            await AzureDevOpsCall.RunAsync(
                () => reviews.SetVoteAsync(location, voter.Id, vote.Value, cancellationToken),
                cancellationToken);
        }

        return new CastVoteDocument(
            outcome,
            pullRequestId,
            vote.Name,
            PullRequestVote.NameOf(previous),
            PullRequestLookup.BrowserUrl(organizationUrl, location));
    }
}
