using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.Reviews;

/// <summary>
/// Sets the token owner's vote on an active pull request that is not a draft. A vote equal to the
/// current one sends nothing, so running the command twice is safe. Like the portal, a vote adds the
/// token owner as a reviewer when they are not one yet.
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
            await PullRequestLookup.FindActiveAsync(pullRequests, pullRequestId, cancellationToken);
        if (pullRequest.IsDraft == true)
        {
            throw WriteException.Rejected(
                "pullRequestIsDraft",
                $"Pull request {pullRequestId} is a draft; Azure DevOps takes no votes until it is published.");
        }

        Person voter = await AzureDevOpsCall.RunAsync(() => people.GetSignedInUserAsync(cancellationToken), cancellationToken);
        short previous = pullRequest.Reviewers?
            .FirstOrDefault(reviewer => Guid.TryParse(reviewer.Id, out Guid id) && id == voter.Id)?.Vote ?? 0;
        string outcome = previous == vote.Value ? VoteOutcomes.Unchanged
            : dryRun ? VoteOutcomes.Preview
            : VoteOutcomes.Voted;
        short stored = vote.Value;
        if (outcome == VoteOutcomes.Voted)
        {
            IdentityRefWithVote reviewer = await AzureDevOpsCall.RunAsync(
                () => reviews.SetVoteAsync(location, voter.Id, vote.Value, cancellationToken),
                cancellationToken);
            stored = reviewer.Vote;
        }

        return new CastVoteDocument(
            outcome,
            pullRequestId,
            PullRequestVote.NameOf(stored),
            PullRequestVote.NameOf(previous),
            PullRequestLookup.BrowserUrl(organizationUrl, location));
    }
}
