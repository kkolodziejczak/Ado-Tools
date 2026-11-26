using System.Text.Json;
using Microsoft.TeamFoundation.Policy.WebApi;
using Newtonsoft.Json.Linq;

namespace AdoRead.AzureDevOps;

/// <summary>
/// A pull request's branch-policy evaluations, and how both tools read one: its name, status, whether
/// its build expired, and the build it ran. Azure DevOps keeps <c>isExpired</c> and <c>buildId</c> in an
/// undocumented context, so they are false and null when it leaves them out.
/// </summary>
public static class PolicyEvaluations
{
    /// <summary>The policy type ID of "Build" (build validation) policies.</summary>
    public static readonly Guid BuildPolicyType = Guid.Parse("0609b952-1397-4640-95ec-e00a01b2c241");

    private const int PageSize = 100;

    /// <summary>Every evaluation that applies to the pull request, page by page.</summary>
    public static async Task<IReadOnlyList<PolicyEvaluationRecord>> GetAllAsync(
        PolicyHttpClient policies,
        Guid projectId,
        int pullRequestId,
        CancellationToken cancellationToken)
    {
        string artifactId = $"vstfs:///CodeReview/CodeReviewId/{projectId}/{pullRequestId}";
        var evaluations = new List<PolicyEvaluationRecord>();
        while (true)
        {
            List<PolicyEvaluationRecord> page = await policies.GetPolicyEvaluationsAsync(
                projectId,
                artifactId,
                includeNotApplicable: false,
                top: PageSize,
                skip: evaluations.Count,
                userState: null,
                cancellationToken);
            evaluations.AddRange(page);
            if (page.Count < PageSize)
            {
                return evaluations;
            }
        }
    }

    /// <summary>The policy's own display name, else the policy type's name.</summary>
    public static string NameOf(PolicyEvaluationRecord evaluation) =>
        evaluation.Configuration?.Settings?["displayName"] is { Type: JTokenType.String } name && !string.IsNullOrWhiteSpace(name.Value<string>())
            ? name.Value<string>()!
            : evaluation.Configuration?.Type?.DisplayName ?? "Build";

    /// <summary><c>approved</c>, <c>rejected</c>, <c>running</c>, <c>queued</c>, <c>notApplicable</c> or <c>broken</c>.</summary>
    public static string StatusOf(PolicyEvaluationRecord evaluation) =>
        evaluation.Status is { } status ? JsonNamingPolicy.CamelCase.ConvertName(status.ToString()) : "unknown";

    public static bool IsExpired(PolicyEvaluationRecord evaluation) =>
        evaluation.Context?["isExpired"] is { Type: JTokenType.Boolean } expired && expired.Value<bool>();

    public static int? BuildIdOf(PolicyEvaluationRecord evaluation) =>
        evaluation.Context?["buildId"] is { Type: JTokenType.Integer } buildId ? buildId.Value<int>() : null;
}
