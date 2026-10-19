using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AdoWrite.Comments;

/// <summary>
/// What the agent asked to post, validated before any Azure DevOps call. <see cref="Key"/> is the
/// caller's duplicate key; without one, the target and content derive it, so an identical retry is
/// recognised as the same comment.
/// </summary>
public sealed partial record CommentDraft(
    string Body,
    string Agent,
    IReadOnlyList<string> Mentions,
    string? Key,
    bool DryRun)
{
    private const int MaxAgentLength = 64;
    private const int MaxMentions = 20;

    public static CommentDraft Create(string? body, string? agent, IReadOnlyList<string> mentions, string? key, bool dryRun)
    {
        string normalizedBody = TrimBlankLines((body ?? string.Empty).ReplaceLineEndings("\n"));
        if (normalizedBody.Length == 0)
        {
            throw WriteException.Rejected("bodyRequired", "The comment body is empty.");
        }

        if (normalizedBody.Contains(CommentMarker.Prefix, StringComparison.Ordinal))
        {
            throw WriteException.Rejected("invalidBody", "The comment body must not contain an ado-write marker.");
        }

        string normalizedAgent = (agent ?? string.Empty).Trim();
        if (normalizedAgent.Length is 0 or > MaxAgentLength || !AgentPattern().IsMatch(normalizedAgent))
        {
            throw WriteException.Rejected(
                "invalidAgent",
                $"--agent names the AI agent and model that wrote the comment, for example \"Claude Opus 5.5\". Use up to {MaxAgentLength} letters, digits, spaces and . - _ ( ) + /.");
        }

        string[] normalizedMentions = mentions
            .Select(mention => mention.Trim())
            .Where(mention => mention.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedMentions.Length > MaxMentions || normalizedMentions.Any(mention => mention.Any(char.IsWhiteSpace)))
        {
            throw WriteException.Rejected(
                "invalidMention",
                $"Mention up to {MaxMentions} people, each by mail address or identity ID.");
        }

        string? normalizedKey = key?.Trim();
        if (normalizedKey is not null && !CommentMarker.IsValidKey(normalizedKey))
        {
            throw WriteException.Rejected(
                "invalidKey",
                "--key must be 1 to 64 letters, digits, dots, dashes or underscores.");
        }

        return new CommentDraft(normalizedBody, normalizedAgent, normalizedMentions, normalizedKey, dryRun);
    }

    /// <summary>
    /// The duplicate key: the caller's, or a hash of the target, the mentions and the body. A key names
    /// one comment per pull request or work item. Each hashed part is length-prefixed, so text moving
    /// between the mentions and the body cannot produce the same key.
    /// </summary>
    public string KeyFor(params object?[] target)
    {
        if (Key is not null)
        {
            return Key;
        }

        IEnumerable<string> parts = target.Select(part => part?.ToString() ?? string.Empty)
            .Append(Mentions.Count.ToString(CultureInfo.InvariantCulture))
            .Concat(Mentions.Select(mention => mention.ToLowerInvariant()))
            .Append(Body);
        string identity = string.Concat(parts.Select(part => $"{part.Length}:{part}"));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>Drops blank lines around the text and trailing spaces, but keeps the first line's indentation, which Markdown can need.</summary>
    private static string TrimBlankLines(string text)
    {
        string trimmed = text.TrimEnd();
        int firstLine = 0;
        for (int index = 0; index < trimmed.Length && char.IsWhiteSpace(trimmed[index]); index++)
        {
            if (trimmed[index] == '\n')
            {
                firstLine = index + 1;
            }
        }

        return trimmed[firstLine..];
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._()+/-]+$")]
    private static partial Regex AgentPattern();
}
