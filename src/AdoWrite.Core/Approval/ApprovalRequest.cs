using System.Text.Json.Serialization;
using AdoWrite.Comments;
using AdoWrite.Reviews;

namespace AdoWrite.Approval;

/// <summary>
/// A queued write: its kind and the arguments of the write command, with every file input already
/// copied in (<c>--body=</c> text, <c>--attach=</c> copies in the outbox), so it runs as it was queued.
/// </summary>
public sealed record ApprovalRequest(string Id, string Kind, IReadOnlyList<string> Arguments, DateTimeOffset QueuedAtUtc)
{
    /// <summary>The command each kind runs. Only these run: a request of any other kind is never shown or posted.</summary>
    private static readonly Dictionary<string, string[]> Commands = new(StringComparer.Ordinal)
    {
        [CommentKinds.PullRequestComment] = ["pr", "comment"],
        [CommentKinds.PullRequestReply] = ["pr", "reply"],
        [CommentKinds.WorkItemComment] = ["work-item", "comment"],
        [ThreadKinds.PullRequestResolve] = ["pr", "resolve"],
        [ThreadKinds.PullRequestUnresolve] = ["pr", "unresolve"],
        [ReviewKinds.PullRequestRequeue] = ["pr", "requeue"],
        [ReviewKinds.PullRequestVote] = ["pr", "vote"],
        [CreateKinds.PullRequestCreate] = ["pr", "create"],
    };

    public static bool IsKnownKind(string kind) => Commands.ContainsKey(kind);

    [JsonIgnore]
    public IReadOnlyList<string> Command => Commands[Kind];

    /// <summary>The value of <c>--name=value</c>, the form every option is stored in.</summary>
    public string? Option(string name) =>
        Arguments.FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

    /// <summary>The positional ID or URL, which comes before every option.</summary>
    [JsonIgnore]
    public string? Target => Arguments.Count > 0 && !Arguments[0].StartsWith("--", StringComparison.Ordinal) ? Arguments[0] : null;
}
