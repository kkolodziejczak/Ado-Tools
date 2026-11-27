using System.Globalization;
using System.Text.Json;
using AdoWrite.Comments;
using AdoWrite.Reviews;

namespace AdoWrite.Approval;

public enum ScreenTone
{
    Normal,
    Dim,
    Current,
    Posted,
    Failed
}

public sealed record ScreenLine(string Text, ScreenTone Tone = ScreenTone.Normal);

/// <summary>
/// The approval window as lines of text: the counts, one row per queued write, the selected write's
/// details with the text it posts, and the keys. Pure, so the screen is tested without a console.
/// </summary>
public static class ApprovalBoardView
{
    public const string Keys = "[↑↓] move  [y] post  [n] reject  [a] post all remaining  [q] quit";

    /// <param name="previews">Dry-run envelopes by request ID; a missing one is still loading.</param>
    public static IReadOnlyList<ScreenLine> Render(ApprovalBoard board, IReadOnlyDictionary<string, JsonElement> previews, TimeZoneInfo timeZone)
    {
        var lines = new List<ScreenLine>
        {
            new($"{board.OpenCount} waiting · {board.Count(ApprovalItemStatus.Posted)} posted · {board.Count(ApprovalItemStatus.Rejected)} rejected"),
            new(string.Empty)
        };
        if (board.Current is not { } current)
        {
            lines.Add(new("Nothing to approve. Writes an agent queues appear here.", ScreenTone.Dim));
            lines.Add(new(string.Empty));
            lines.Add(new("[q] quit", ScreenTone.Dim));
            return lines;
        }

        for (int index = 0; index < board.Items.Count; index++)
        {
            lines.Add(Row(board.Items[index], index, index == board.Selected));
        }

        lines.Add(new(string.Empty));
        lines.Add(new(Describe(current.Request)));
        lines.Add(new($"Queued by: {current.Request.Option("--agent") ?? "an agent that gave no --agent"} · {TimeZoneInfo.ConvertTime(current.Request.QueuedAtUtc, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture)}", ScreenTone.Dim));
        if (current.Request.Option("--config") is { } configuration)
        {
            // Another configuration can mean another organization, so the person sees which one.
            lines.Add(new($"Configuration: {configuration}", ScreenTone.Dim));
        }

        lines.Add(new(string.Empty));
        lines.AddRange(Detail(current, previews));
        lines.Add(new(string.Empty));
        lines.Add(new(Keys));
        return lines;
    }

    private static ScreenLine Row(ApprovalItem item, int index, bool selected)
    {
        (string mark, string state, ScreenTone tone) = item.Status switch
        {
            ApprovalItemStatus.Posted => ("✓", "posted", ScreenTone.Posted),
            ApprovalItemStatus.Rejected => ("✗", "rejected", ScreenTone.Dim),
            ApprovalItemStatus.Failed => ("!", "failed", ScreenTone.Failed),
            _ => (selected ? "●" : "○", string.Empty, ScreenTone.Normal)
        };
        string text = $"{(selected ? ">" : " ")} {mark} {index + 1,-2} {Describe(item.Request),-48} {state}".TrimEnd();
        return new(text, selected ? ScreenTone.Current : tone);
    }

    /// <summary>One line naming the target and what the write does, from the arguments alone.</summary>
    public static string Describe(ApprovalRequest request)
    {
        string target = ShortTarget(request.Target);
        return request.Kind switch
        {
            CommentKinds.PullRequestComment when request.Option("--file") is { } file =>
                $"PR {target}  comment · {file}:{request.Option("--line")}",
            CommentKinds.PullRequestComment => $"PR {target}  comment",
            CommentKinds.PullRequestReply => $"PR {target}  reply · thread {request.Option("--thread")}",
            ThreadKinds.PullRequestResolve => $"PR {target}  resolve · thread {request.Option("--thread")}",
            ThreadKinds.PullRequestUnresolve => $"PR {target}  unresolve · thread {request.Option("--thread")}",
            ReviewKinds.PullRequestVote => $"PR {target}  vote · {request.Option("--vote")}",
            ReviewKinds.PullRequestRequeue => $"PR {target}  requeue builds",
            CreateKinds.PullRequestCreate => $"New PR  {request.Option("--title")}",
            CommentKinds.WorkItemComment => $"WI {target}  comment",
            _ => request.Kind
        };
    }

    /// <summary>A URL is shown by its last segment, the ID.</summary>
    private static string ShortTarget(string? target) =>
        target is not null && Uri.TryCreate(target, UriKind.Absolute, out Uri? url)
            ? url.Segments[^1].Trim('/')
            : target ?? string.Empty;

    private static IEnumerable<ScreenLine> Detail(ApprovalItem item, IReadOnlyDictionary<string, JsonElement> previews)
    {
        switch (item.Status)
        {
            case ApprovalItemStatus.Posted:
                return [new($"Posted: {item.Note}", ScreenTone.Posted)];
            case ApprovalItemStatus.Rejected:
                return [new("Rejected: nothing was posted.", ScreenTone.Dim)];
        }

        var lines = new List<ScreenLine>();
        if (item.Status == ApprovalItemStatus.Failed)
        {
            lines.Add(new($"Failed: {item.Note}", ScreenTone.Failed));
        }

        lines.Add(new("Will be posted as you:"));
        lines.AddRange(previews.TryGetValue(item.Request.Id, out JsonElement preview)
            ? ApprovalPreview.Lines(preview).Select(line => new ScreenLine("  " + line))
            : [new ScreenLine("  Loading the preview…", ScreenTone.Dim)]);
        return lines;
    }
}
