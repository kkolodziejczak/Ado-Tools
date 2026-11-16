using System.Text.Json;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.Policy.WebApi;
using Newtonsoft.Json.Linq;

namespace AdoWrite.Reviews;

/// <summary>
/// Requeues the build policies of a pull request, as the portal's "Re-queue" button does: by default
/// every expired build, or exactly the evaluations the caller names. A build that runs or waits to run
/// is never requeued, because Azure DevOps would cancel it, so running the command twice is safe.
/// </summary>
internal sealed class BuildRequeuer(IPullRequestCommentClient pullRequests, IPullRequestReviewClient reviews)
{
    /// <summary>The policy type ID of "Build" (build validation) policies.</summary>
    public static readonly Guid BuildPolicyType = Guid.Parse("0609b952-1397-4640-95ec-e00a01b2c241");

    public async Task<IReadOnlyList<RequeuedBuildDocument>> RequeueAsync(
        int pullRequestId,
        IReadOnlySet<Guid> selected,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        (_, PullRequestLocation location) = await PullRequestLookup.FindAsync(pullRequests, pullRequestId, cancellationToken);
        IReadOnlyList<PolicyEvaluationRecord> evaluations = await AzureDevOpsCall.RunAsync(
            () => reviews.GetPolicyEvaluationsAsync(location, cancellationToken),
            cancellationToken);
        PolicyEvaluationRecord[] builds = evaluations
            .Where(evaluation => evaluation.Configuration?.Type?.Id == BuildPolicyType)
            .ToArray();
        Guid[] unknown = selected.Where(id => builds.All(build => build.EvaluationId != id)).ToArray();
        if (unknown.Length > 0)
        {
            throw WriteException.NotFound(
                "evaluationNotFound",
                $"Pull request {pullRequestId} has no build policy evaluation {string.Join(", ", unknown)}. Run pr requeue --dry-run to list its build policies.");
        }

        var results = new List<RequeuedBuildDocument>(builds.Length);
        foreach (PolicyEvaluationRecord build in builds)
        {
            results.Add(await RequeueAsync(location, build, selected, dryRun, cancellationToken));
        }

        return results;
    }

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

        PolicyEvaluationRecord requeued = await AzureDevOpsCall.RunAsync(
            () => reviews.RequeuePolicyEvaluationAsync(location, build.EvaluationId, cancellationToken),
            cancellationToken);
        return Document(build, requeued, RequeueOutcomes.Requeued);
    }

    private static string Decide(PolicyEvaluationRecord build, IReadOnlySet<Guid> selected)
    {
        bool expired = IsExpired(build);
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
            NameOf(build),
            current.Status is { } status ? JsonNamingPolicy.CamelCase.ConvertName(status.ToString()) : "unknown",
            IsExpired(current),
            build.Configuration?.IsBlocking ?? false,
            current.Context?["buildId"] is { Type: JTokenType.Integer } buildId ? buildId.Value<int>() : null,
            outcome);

    /// <summary>The policy's own display name, else the policy type's name.</summary>
    private static string NameOf(PolicyEvaluationRecord build) =>
        build.Configuration?.Settings?["displayName"] is { Type: JTokenType.String } name && !string.IsNullOrWhiteSpace(name.Value<string>())
            ? name.Value<string>()!
            : build.Configuration?.Type?.DisplayName ?? "Build";

    private static bool IsExpired(PolicyEvaluationRecord build) =>
        build.Context?["isExpired"] is { Type: JTokenType.Boolean } expired && expired.Value<bool>();
}
