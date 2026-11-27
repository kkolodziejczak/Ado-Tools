using System.Text.Json;

namespace AdoWrite.Approval;

/// <summary>
/// Reads a write command's JSON envelope for the window: what a dry run says the write will do, and
/// what a real run did.
/// </summary>
public static class ApprovalPreview
{
    /// <summary>The text, vote, status or builds the write sends, or the error that stops it.</summary>
    public static IReadOnlyList<string> Lines(JsonElement envelope)
    {
        if (Error(envelope) is { } error)
        {
            return [error];
        }

        var lines = new List<string>();
        foreach (JsonElement result in envelope.GetProperty("results").EnumerateArray())
        {
            if (String(result, "title") is { } title)
            {
                lines.Add($"Title: {title} ({String(result, "sourceRefName")} → {String(result, "targetRefName")})");
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
                lines.Add($"{String(result, "name")}: {String(result, "outcome")}");
            }

            string? text = String(result, "text")
                ?? String(result, "description")
                ?? (result.TryGetProperty("comment", out JsonElement comment) && comment.ValueKind == JsonValueKind.Object ? String(comment, "text") : null);
            if (text is not null)
            {
                lines.AddRange(text.ReplaceLineEndings("\n").Split('\n').Where(line => !line.StartsWith("<!-- ado-write:", StringComparison.Ordinal)));
            }
        }

        return lines.Count > 0 ? lines : ["(no change)"];
    }

    /// <summary>The URL of what a real run posted, or null with <see cref="Error"/> saying why.</summary>
    public static string? PostedUrl(JsonElement envelope) =>
        Error(envelope) is null && envelope.GetProperty("results").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } result
            ? String(result, "url") ?? String(result, "outcome")
            : null;

    /// <summary>The first error of a run that did not fully succeed, as <c>code: message</c>.</summary>
    public static string? Error(JsonElement envelope)
    {
        if (String(envelope, "status") == "success")
        {
            return null;
        }

        return envelope.TryGetProperty("errors", out JsonElement errors) && errors.GetArrayLength() > 0
            ? $"{String(errors[0], "code")}: {String(errors[0], "message")}"
            : $"The write ended with status {String(envelope, "status")}.";
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
