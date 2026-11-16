using System.Globalization;

namespace AdoWrite.Reviews;

/// <summary>
/// One build policy of a pull request after <c>pr requeue</c>. <see cref="Outcome"/> is one of
/// <see cref="RequeueOutcomes"/>; <see cref="Status"/> is the evaluation status after the command.
/// <see cref="IsExpired"/> and <see cref="BuildId"/> come from the evaluation's context, which
/// Azure DevOps does not document, so they are false and null when it leaves them out.
/// </summary>
public sealed record RequeuedBuildDocument(
    Guid EvaluationId,
    string Name,
    string Status,
    bool IsExpired,
    bool IsBlocking,
    int? BuildId,
    string Outcome);

public static class RequeueOutcomes
{
    public const string Requeued = "requeued";
    public const string Preview = "preview";

    /// <summary>The build still runs, or waits to run, so a requeue would cancel it.</summary>
    public const string InProgress = "inProgress";

    /// <summary>Without <c>--evaluation</c> only expired builds are requeued.</summary>
    public const string NotExpired = "notExpired";

    /// <summary>With <c>--evaluation</c> only the named builds are requeued.</summary>
    public const string NotSelected = "notSelected";
}

/// <summary>The result of <c>pr vote</c>. <see cref="Outcome"/> is <c>voted</c>, <c>unchanged</c> or <c>preview</c>.</summary>
public sealed record CastVoteDocument(string Outcome, int PullRequestId, string Vote, string PreviousVote, Uri Url);

public static class VoteOutcomes
{
    public const string Voted = "voted";
    public const string Unchanged = "unchanged";
    public const string Preview = "preview";
}

/// <summary>A reviewer vote by the name the CLI uses and the value Azure DevOps stores.</summary>
public sealed record PullRequestVote(string Name, short Value)
{
    public static readonly IReadOnlyList<PullRequestVote> All =
    [
        new("approve", 10),
        new("approve-with-suggestions", 5),
        new("none", 0),
        new("wait-for-author", -5),
        new("reject", -10)
    ];

    public static PullRequestVote? Parse(string? name) =>
        All.FirstOrDefault(vote => string.Equals(vote.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string NameOf(short value) =>
        All.FirstOrDefault(vote => vote.Value == value)?.Name ?? value.ToString(CultureInfo.InvariantCulture);
}

public static class ReviewKinds
{
    public const string PullRequestRequeue = "pullRequestRequeue";
    public const string PullRequestVote = "pullRequestVote";
}
