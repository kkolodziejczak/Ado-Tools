using AdoRead.AzureDevOps;
using Microsoft.TeamFoundation.Policy.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>The pull-request calls behind <c>pr requeue</c> and <c>pr vote</c>.</summary>
public interface IPullRequestReviewClient
{
    /// <summary>Every policy evaluation that applies to the pull request.</summary>
    Task<IReadOnlyList<PolicyEvaluationRecord>> GetPolicyEvaluationsAsync(
        PullRequestLocation location,
        CancellationToken cancellationToken);

    /// <summary>One evaluation as it is now.</summary>
    Task<PolicyEvaluationRecord> GetPolicyEvaluationAsync(
        PullRequestLocation location,
        Guid evaluationId,
        CancellationToken cancellationToken);

    /// <summary>Runs the policy again; for a build policy Azure DevOps queues a new build, cancelling a running one.</summary>
    Task<PolicyEvaluationRecord> RequeuePolicyEvaluationAsync(
        PullRequestLocation location,
        Guid evaluationId,
        CancellationToken cancellationToken);

    Task<IdentityRefWithVote> SetVoteAsync(
        PullRequestLocation location,
        Guid reviewerId,
        short vote,
        CancellationToken cancellationToken);
}

public sealed class SdkPullRequestReviewClient(PolicyHttpClient policies, GitHttpClient git) : IPullRequestReviewClient
{
    public Task<IReadOnlyList<PolicyEvaluationRecord>> GetPolicyEvaluationsAsync(
        PullRequestLocation location,
        CancellationToken cancellationToken) =>
        PolicyEvaluations.GetAllAsync(policies, location.ProjectId, location.PullRequestId, cancellationToken);

    public Task<PolicyEvaluationRecord> GetPolicyEvaluationAsync(
        PullRequestLocation location,
        Guid evaluationId,
        CancellationToken cancellationToken) =>
        policies.GetPolicyEvaluationAsync(location.ProjectId, evaluationId, userState: null, cancellationToken);

    public Task<PolicyEvaluationRecord> RequeuePolicyEvaluationAsync(
        PullRequestLocation location,
        Guid evaluationId,
        CancellationToken cancellationToken) =>
        policies.RequeuePolicyEvaluationAsync(location.ProjectId, evaluationId, userState: null, cancellationToken);

    public Task<IdentityRefWithVote> SetVoteAsync(
        PullRequestLocation location,
        Guid reviewerId,
        short vote,
        CancellationToken cancellationToken) =>
        git.CreatePullRequestReviewerAsync(
            new IdentityRefWithVote { Id = reviewerId.ToString(), Vote = vote },
            location.ProjectId,
            location.RepositoryId,
            location.PullRequestId,
            reviewerId.ToString(),
            userState: null,
            cancellationToken);
}
