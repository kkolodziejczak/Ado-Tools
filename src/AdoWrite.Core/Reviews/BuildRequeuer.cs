using AdoRead.AzureDevOps;
using AdoWrite.AzureDevOps;
using AdoRead.Output;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.Policy.WebApi;

namespace AdoWrite.Reviews;

/// <summary>
/// Requeues the build policies of an active pull request, as the portal's "Re-queue" button does: by
/// default every expired build, or exactly the evaluations the caller names. A build that runs, or is
/// queued and not expired, is never requeued, because Azure DevOps would cancel it; each evaluation is
/// read again right before its requeue, to catch a build started since the list was read. A failure
/// stops the remaining requeues, and the report still lists what was already requeued.
/// </summary>
internal sealed class BuildRequeuer(IPullRequestCommentClient pullRequests, IPullRequestReviewClient reviews)
{
    /// <summary>The policy type ID of "Build" (build validation) policies.</summary>
    public static readonly Guid BuildPolicyType = PolicyEvaluations.BuildPolicyType;

    public async Task<RequeueReport> RequeueAsync(
        int pullRequestId,
        IReadOnlySet<Guid> selected,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        (_, PullRequestLocation location) = await PullRequestLookup.FindActiveAsync(pullRequests, pullRequestId, cancellationToken);
        IReadOnlyList<PolicyEvaluationRecord> evaluations = await AzureDevOpsCall.RunAsync(
            () => reviews.GetPolicyEvaluationsAsync(location, cancellationToken),
            cancellationToken);
        PolicyEvaluationRecord[] builds = evaluations
            .Where(evaluation => evaluation.Configuration?.Type?.Id == BuildPolicyType)
            .DistinctBy(evaluation => evaluation.EvaluationId)
            .ToArray();
        Guid[] unknown = selected.Where(id => builds.All(build => build.EvaluationId != id)).ToArray();
        if (unknown.Length > 0)
        {
            throw WriteException.NotFound(
                "evaluationNotFound",
                $"Pull request {pullRequestId} has no build policy evaluation {string.Join(", ", unknown)}. Run pr requeue --dry-run to list its build policies.");
        }

        var results = new List<RequeuedBuildDocument>(builds.Length);
        SearchError? failure = null;
        foreach (PolicyEvaluationRecord build in builds)
        {
            if (failure is not null)
            {
                string skipped = Decide(build, selected);
                results.Add(Document(build, build, skipped == RequeueOutcomes.Requeued ? RequeueOutcomes.NotAttempted : skipped));
                continue;
            }

            try
            {
                results.Add(await RequeueAsync(location, build, selected, dryRun, cancellationToken));
            }
            catch (WriteException exception) when (exception.Code != "timeout")
            {
                failure = exception.ToError();
                results.Add(Document(build, build, RequeueOutcomes.Failed));
            }
            catch (Exception exception) when (exception is WriteException or OperationCanceledException)
            {
                failure = new SearchError("azureDevOps", null, "timeout", UncertainMessage(build.EvaluationId), true);
                results.Add(Document(build, build, RequeueOutcomes.Uncertain));
            }
        }

        return new RequeueReport(results, failure);
    }

    private static string UncertainMessage(Guid evaluationId) =>
        $"The requeue of build policy evaluation {evaluationId} timed out and may have reached Azure DevOps. Run pr requeue --dry-run to see its status before naming it again; requeues after it were not attempted.";

    private async Task<RequeuedBuildDocument> RequeueAsync(
        PullRequestLocation location,
        PolicyEvaluationRecord build,
        IReadOnlySet<Guid> selected,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        string outcome = Decide(build, selected);
        if (outcome != RequeueOutcomes.Requeued)
        {
            return Document(build, build, outcome);
        }

        if (dryRun)
        {
            return Document(build, build, RequeueOutcomes.Preview);
        }

        PolicyEvaluationRecord current = await AzureDevOpsCall.RunAsync(
            () => reviews.GetPolicyEvaluationAsync(location, build.EvaluationId, cancellationToken),
            cancellationToken);
        outcome = Decide(current, selected);
        if (outcome != RequeueOutcomes.Requeued)
        {
            return Document(build, current, outcome);
        }

        PolicyEvaluationRecord requeued = await AzureDevOpsCall.RunAsync(
            () => reviews.RequeuePolicyEvaluationAsync(location, build.EvaluationId, cancellationToken),
            cancellationToken);
        return Document(build, requeued, RequeueOutcomes.Requeued);
    }

    private static string Decide(PolicyEvaluationRecord build, IReadOnlySet<Guid> selected)
    {
        bool expired = PolicyEvaluations.IsExpired(build);
        if (selected.Count > 0 && !selected.Contains(build.EvaluationId))
        {
            return RequeueOutcomes.NotSelected;
        }

        if (build.Status == PolicyEvaluationStatus.Running || (build.Status == PolicyEvaluationStatus.Queued && !expired))
        {
            return RequeueOutcomes.InProgress;
        }

        return selected.Count == 0 && !expired ? RequeueOutcomes.NotExpired : RequeueOutcomes.Requeued;
    }

    /// <summary><paramref name="current"/> is the evaluation after the command, which a requeue replaces.</summary>
    private static RequeuedBuildDocument Document(PolicyEvaluationRecord build, PolicyEvaluationRecord current, string outcome) =>
        new(
            build.EvaluationId,
            PolicyEvaluations.NameOf(build),
            PolicyEvaluations.StatusOf(current),
            PolicyEvaluations.IsExpired(current),
            build.Configuration?.IsBlocking ?? false,
            PolicyEvaluations.BuildIdOf(current),
            outcome);
}
