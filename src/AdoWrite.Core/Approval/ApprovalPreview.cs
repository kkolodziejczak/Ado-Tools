using System.Globalization;
using System.Text.Json;

namespace AdoWrite.Approval;

/// <summary>
/// Reads a write command's JSON envelope for the window: what a dry run says the write will do, and
/// what a real run did.
/// </summary>
public static class ApprovalPreview
{
    /// <summary>Everything the write sends: title, repository, reviewers, lines, mentions, attachments, vote, status, builds and text.</summary>
    public static IReadOnlyList<string> Lines(JsonElement envelope)
    {
        var lines = new List<string>();
        foreach (JsonElement result in Results(envelope))
        {
            if (String(result, "title") is { } title)
            {
                lines.Add($"Title: {title}");
                lines.Add($"Repository: {String(result, "project")}/{String(result, "repository")}  {Branch(String(result, "sourceRefName"))} → {Branch(String(result, "targetRefName"))}");
                if (Bool(result, "isDraft"))
                {
                    lines.Add("Draft: yes");
                }

                AddList(lines, "Reviewers", result, "reviewers", reviewer => String(reviewer, "input"));
                AddList(lines, "Work items", result, "workItems", item => item.ValueKind == JsonValueKind.Number ? item.GetInt32().ToString(CultureInfo.InvariantCulture) : null);
            }

            if (String(result, "previousVote") is { } previousVote)
            {
                lines.Add($"Vote: {previousVote} → {String(result, "vote")}");
            }

            if (String(result, "previousStatus") is { } previousStatus)
            {
                lines.Add($"Thread status: {previousStatus} → {String(result, "status")}");
            }

            if (String(result, "evaluationId") is not null)
            {
                lines.Add($"Build {String(result, "name")}: {String(result, "outcome")}");
            }

            JsonElement comment = result.TryGetProperty("comment", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object ? nested : result;
            AddComment(lines, comment);
            if (String(result, "description") is { } description)
            {
                // A description is posted as it stands, so every line of it is shown.
                lines.Add(string.Empty);
                lines.AddRange(description.ReplaceLineEndings("\n").Split('\n'));
            }
        }

        return lines.Count > 0 ? lines : ["(no change)"];
    }

    /// <summary>Who the write is posted as: the token owner the dry run names.</summary>
    public static string? ActingAs(JsonElement envelope) =>
        Results(envelope)
            .Select(result => String(result, "actingAs")
                ?? (result.TryGetProperty("comment", out JsonElement comment) && comment.ValueKind == JsonValueKind.Object ? String(comment, "actingAs") : null))
            .FirstOrDefault(name => name is not null);

    /// <summary>Whether two dry runs describe the same write, so the post sends what the person read.</summary>
    public static bool SameWrite(JsonElement read, JsonElement now) =>
        Error(read) is null && Error(now) is null
        && read.TryGetProperty("results", out JsonElement before) && now.TryGetProperty("results", out JsonElement after)
        && before.GetRawText() == after.GetRawText();

    /// <summary>The URL of what a real run posted, or its outcome when it has none.</summary>
    public static string? PostedUrl(JsonElement envelope) =>
        Results(envelope).FirstOrDefault() is { ValueKind: JsonValueKind.Object } result
            ? String(result, "url") ?? String(result, "outcome")
            : null;

    /// <summary>The first error of a run that did not fully succeed, as <c>code: message</c>.</summary>
    public static string? Error(JsonElement envelope)
    {
        if (String(envelope, "status") == "success")
        {
            return null;
        }

        return envelope.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
            ? $"{String(errors[0], "code")}: {String(errors[0], "message")}"
            : $"The write ended with status {String(envelope, "status")}.";
    }

    private static void AddComment(List<string> lines, JsonElement comment)
    {
        if (comment.TryGetProperty("anchor", out JsonElement anchor) && anchor.ValueKind == JsonValueKind.Object)
        {
            lines.Add($"On: {String(anchor, "filePath")} lines {Int(anchor, "startLine")}–{Int(anchor, "endLine")} ({String(anchor, "side")} side)");
        }

        AddList(lines, "Mentions", comment, "mentions", mention => String(mention, "input"));
        if (comment.TryGetProperty("attachments", out JsonElement attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement attachment in attachments.EnumerateArray())
            {
                string sha = String(attachment, "sha256") ?? string.Empty;
                lines.Add($"Attachment: {String(attachment, "name")} ({Int(attachment, "length")} bytes, sha256 {sha[..Math.Min(12, sha.Length)]})");
            }
        }

        if (String(comment, "text") is { } text)
        {
            lines.Add(string.Empty);
            lines.AddRange(CommentLines(text));
        }
    }

    /// <summary>
    /// The comment's lines without its hidden duplicate marker, which ado-write adds itself: a body that
    /// contains a marker is refused before it is saved, so no other line is left out.
    /// </summary>
    private static IEnumerable<string> CommentLines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n').Where(line => !line.StartsWith("<!-- ado-write:", StringComparison.Ordinal));

    private static void AddList(List<string> lines, string label, JsonElement element, string name, Func<JsonElement, string?> item)
    {
        if (element.TryGetProperty(name, out JsonElement values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
        {
            lines.Add($"{label}: {string.Join(", ", values.EnumerateArray().Select(item))}");
        }
    }

    private static IEnumerable<JsonElement> Results(JsonElement envelope) =>
        envelope.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
            : [];

    private static string? Branch(string? refName) => refName?.StartsWith("refs/heads/", StringComparison.Ordinal) == true ? refName["refs/heads/".Length..] : refName;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
