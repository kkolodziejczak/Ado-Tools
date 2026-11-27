using System.Globalization;
using System.Text;
using System.Text.Json;
using AdoWrite.Comments;
using AdoWrite.Reviews;

namespace AdoWrite.Approval;

public enum ScreenTone
{
    Normal,
    Dim,
    Warning,
    Posted,
    Failed
}

public sealed record ScreenLine(string Text, ScreenTone Tone = ScreenTone.Normal);

/// <summary>Where the window is with its one write.</summary>
public enum PromptStage
{
    /// <summary>The dry run is running.</summary>
    Loading,

    /// <summary>The dry run is shown; Enter posts it.</summary>
    Ready,

    /// <summary>The dry run run again before posting differs from the one read, which is shown now; Enter posts it.</summary>
    Changed,

    /// <summary>The write is checked once more and posted.</summary>
    Posting,

    /// <summary>The dry run failed, so the write cannot be posted.</summary>
    PreviewFailed,

    Posted,
    NotPosted
}

/// <summary>
/// The approval window as lines of text: what the write is and where it goes, the dry run, and the
/// keys, which come last so a long text never pushes them off the screen. Every text an agent chose is
/// made printable first, so it cannot move the cursor, clear the screen or reorder what is shown.
/// Pure, so the screen is tested without a console.
/// </summary>
public static class ApprovalScreen
{
    public const string ReadyKeys = "[Enter] post as you    [any other key] cancel, nothing is posted";
    public const string CloseKey = "[any key] close";

    /// <param name="preview">The dry-run envelope; null while it loads.</param>
    /// <param name="note">The error of a failed dry run or post, or the URL of a posted write.</param>
    public static IReadOnlyList<ScreenLine> Render(ApprovalRequest request, PromptStage stage, JsonElement? preview, string? note = null)
    {
        var lines = new List<ScreenLine>
        {
            new(Describe(request)),
            new($"Organization: {request.Organization}", ScreenTone.Dim),
            new($"Posted as: {(preview is { } envelope ? ApprovalPreview.ActingAs(envelope) : null) ?? "the owner of the token"}", ScreenTone.Dim),
            new($"Requested by: {request.Option("--agent") ?? "an agent that gave no --agent"} · {request.RequestedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}", ScreenTone.Dim),
        };
        if (request.Option("--config") is { } configuration)
        {
            lines.Add(new($"Configuration: {configuration}", ScreenTone.Dim));
        }

        lines.Add(new(string.Empty));
        if (stage == PromptStage.Changed)
        {
            lines.Add(new("It changed since you read it. Read it again before you post.", ScreenTone.Warning));
            lines.Add(new(string.Empty));
        }

        lines.AddRange(stage switch
        {
            PromptStage.Loading => [new ScreenLine("Running the dry run…", ScreenTone.Dim)],
            PromptStage.PreviewFailed => [new ScreenLine($"The dry run failed, so this write can't be posted: {note}", ScreenTone.Failed)],
            _ when preview is { } shown => ApprovalPreview.Lines(shown).Select(line => new ScreenLine("  " + line)),
            _ => []
        });
        lines.Add(new(string.Empty));
        lines.AddRange(stage switch
        {
            PromptStage.Ready or PromptStage.Changed => [new ScreenLine(ReadyKeys)],
            PromptStage.Posting => [new ScreenLine("Checking it is unchanged, then posting…", ScreenTone.Dim)],
            PromptStage.Posted => [new ScreenLine($"Posted: {note}", ScreenTone.Posted), new ScreenLine(CloseKey)],
            PromptStage.NotPosted => [new ScreenLine($"Not posted, or only partly: {note}", ScreenTone.Failed), new ScreenLine(CloseKey)],
            PromptStage.PreviewFailed => [new ScreenLine(CloseKey)],
            _ => []
        });
        return [.. lines.Select(line => line with { Text = Printable(line.Text) })];
    }

    /// <summary>A screen with one message, for a write that cannot be shown.</summary>
    public static IReadOnlyList<ScreenLine> Message(string message) =>
        [new(Printable(message), ScreenTone.Failed), new(string.Empty), new(CloseKey)];

    /// <summary>One line naming the target and what the write does, from the arguments alone.</summary>
    public static string Describe(ApprovalRequest request)
    {
        string target = ShortTarget(request.Target);
        return request.Kind switch
        {
            CommentKinds.PullRequestComment when request.Option("--file") is { } file =>
                $"PR {target} · comment on {file}:{request.Option("--line")}",
            CommentKinds.PullRequestComment => $"PR {target} · comment",
            CommentKinds.PullRequestReply => $"PR {target} · reply in thread {request.Option("--thread")}",
            ThreadKinds.PullRequestResolve => $"PR {target} · resolve thread {request.Option("--thread")}",
            ThreadKinds.PullRequestUnresolve => $"PR {target} · unresolve thread {request.Option("--thread")}",
            ReviewKinds.PullRequestVote => $"PR {target} · vote {request.Option("--vote")}",
            ReviewKinds.PullRequestRequeue => $"PR {target} · requeue builds",
            CreateKinds.PullRequestCreate => "New pull request",
            CommentKinds.WorkItemComment => $"Work item {target} · comment",
            _ => request.Kind
        };
    }

    /// <summary>
    /// The text with every control and format character shown as its code, such as <c>^[</c> for escape
    /// and <c>&lt;U+202E&gt;</c> for a direction override, so the terminal draws exactly these characters.
    /// </summary>
    public static string Printable(string text)
    {
        var printable = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(character))
            {
                case UnicodeCategory.Control when character < ' ':
                    printable.Append('^').Append((char)(character + '@'));
                    break;
                case UnicodeCategory.Control or UnicodeCategory.Format:
                    printable.Append(CultureInfo.InvariantCulture, $"<U+{(int)character:X4}>");
                    break;
                default:
                    printable.Append(character);
                    break;
            }
        }

        return printable.ToString();
    }

    /// <summary>A URL is shown by its last segment, the ID.</summary>
    private static string ShortTarget(string? target) =>
        target is not null && Uri.TryCreate(target, UriKind.Absolute, out Uri? url)
            ? url.Segments[^1].Trim('/')
            : target ?? string.Empty;
}
