using System.Text;
using System.Text.RegularExpressions;
using AdoWrite.AzureDevOps;

namespace AdoWrite.Comments;

/// <summary>
/// Turns a draft into the Markdown that is posted: mentions, the body, a visible footer naming the
/// agent and the person it posts for, and a hidden marker that lets a retry find the comment instead
/// of posting it twice.
/// </summary>
internal sealed class CommentComposer(IPeopleDirectory people)
{
    /// <summary>The token owner: the comments are posted as them, and earlier posts are theirs.</summary>
    public Task<Person> GetAuthorAsync(CancellationToken cancellationToken) =>
        AzureDevOpsCall.RunAsync(() => people.GetSignedInUserAsync(cancellationToken), cancellationToken);

    public async Task<ComposedComment> ComposeAsync(CommentDraft draft, string key, Person author, CancellationToken cancellationToken)
    {
        IReadOnlyList<MentionDocument> mentions = await ResolveMentionsAsync(draft.Mentions, cancellationToken);
        return new ComposedComment(Render(draft, mentions, author.DisplayName, key), mentions);
    }

    /// <summary>An identity ID is used as given; anything else must match exactly one active person.</summary>
    private async Task<IReadOnlyList<MentionDocument>> ResolveMentionsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        var mentions = new List<MentionDocument>(inputs.Count);
        foreach (string input in inputs)
        {
            if (Guid.TryParse(input, out Guid id))
            {
                mentions.Add(new MentionDocument(input, id));
                continue;
            }

            IReadOnlyList<Person> matches = await AzureDevOpsCall.RunAsync(
                () => people.FindAsync(input, cancellationToken),
                cancellationToken);
            mentions.Add(matches.Count switch
            {
                1 => new MentionDocument(input, matches[0].Id),
                0 => throw WriteException.Rejected(
                    "mentionNotFound",
                    $"No active person matches the mention '{input}'. Mention by mail address or identity ID."),
                _ => throw WriteException.Rejected(
                    "mentionAmbiguous",
                    $"{matches.Count} people match the mention '{input}'. Mention by mail address or identity ID.")
            });
        }

        return mentions;
    }

    private static string Render(CommentDraft draft, IReadOnlyList<MentionDocument> mentions, string onBehalfOf, string key)
    {
        var text = new StringBuilder();
        if (mentions.Count > 0)
        {
            text.AppendJoin(' ', mentions.Select(mention => $"@<{mention.Id.ToString("D").ToUpperInvariant()}>"));
            text.Append("\n\n");
        }

        text.Append(draft.Body);
        text.Append("\n\n---\n");
        text.Append($"🤖 *Posted by AI agent {draft.Agent} on behalf of {onBehalfOf}*");
        text.Append("\n\n");
        text.Append(CommentMarker.Render(key));
        return text.ToString();
    }
}

internal sealed record ComposedComment(string Text, IReadOnlyList<MentionDocument> Mentions);

/// <summary>The hidden <c>&lt;!-- ado-write:KEY --&gt;</c> line at the end of every posted comment.</summary>
internal static partial class CommentMarker
{
    public const string Prefix = "<!-- ado-write:";

    public static string Render(string key) => $"{Prefix}{key} -->";

    public static bool Marks(string? text, string key) =>
        text?.Contains(Render(key), StringComparison.Ordinal) == true;

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex KeyPattern();
}
