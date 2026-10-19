using System.Text;
using System.Text.RegularExpressions;
using AdoWrite.AzureDevOps;

namespace AdoWrite.Comments;

/// <summary>
/// Turns a draft into the Markdown that is posted: mentions, the body, the attached files, a visible
/// footer naming the agent and the person it posts for, and a hidden marker that lets a retry find the
/// comment instead of posting it twice. Mentions are resolved before anything is uploaded, so a bad
/// mention leaves no orphaned files.
/// </summary>
internal sealed class CommentComposer(IPeopleDirectory people)
{
    /// <summary>The token owner: the comments are posted as them, and earlier posts are theirs.</summary>
    public Task<Person> GetAuthorAsync(CancellationToken cancellationToken) =>
        AzureDevOpsCall.RunAsync(() => people.GetSignedInUserAsync(cancellationToken), cancellationToken);

    /// <summary>A dry run uploads nothing; its links name the files instead of pointing at them.</summary>
    public async Task<ComposedComment> ComposeAsync(
        CommentDraft draft,
        string key,
        Person author,
        Func<Task<IReadOnlyList<AttachmentLink>>> uploadAsync,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MentionDocument> mentions = await ResolveMentionsAsync(draft.Mentions, cancellationToken);
        IReadOnlyList<AttachmentLink> links = draft.DryRun || draft.Attachments.Count == 0
            ? draft.Attachments.Select(file => new AttachmentLink(file, null, Reused: false)).ToArray()
            : await uploadAsync();
        return new ComposedComment(
            Render(draft, mentions, links, author.DisplayName, key),
            mentions,
            links.Select(link => link.ToDocument()).ToArray());
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

    private static string Render(
        CommentDraft draft,
        IReadOnlyList<MentionDocument> mentions,
        IReadOnlyList<AttachmentLink> links,
        string onBehalfOf,
        string key)
    {
        var text = new StringBuilder();
        if (mentions.Count > 0)
        {
            text.AppendJoin(' ', mentions.Select(mention => $"@<{mention.Id.ToString("D").ToUpperInvariant()}>"));
            text.Append("\n\n");
        }

        text.Append(draft.Body);
        if (links.Count > 0)
        {
            text.Append('\n');
        }

        foreach (AttachmentLink link in links)
        {
            string label = link.File.Name.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
            // Parentheses would end the Markdown link early, and an absolute URI keeps them unescaped.
            string target = (link.Url?.AbsoluteUri ?? $"attachment:{Uri.EscapeDataString(link.File.Name)}")
                .Replace("(", "%28", StringComparison.Ordinal)
                .Replace(")", "%29", StringComparison.Ordinal);
            text.Append('\n');
            text.Append(link.File.IsImage ? $"![{label}]({target})" : $"📎 [{label}]({target})");
        }

        text.Append("\n\n---\n");
        text.Append($"🤖 *Posted by AI agent {draft.Agent} on behalf of {onBehalfOf}*");
        text.Append("\n\n");
        text.Append(CommentMarker.Render(key));
        return text.ToString();
    }
}

internal sealed record ComposedComment(
    string Text,
    IReadOnlyList<MentionDocument> Mentions,
    IReadOnlyList<AttachmentDocument> Attachments);

/// <summary>The hidden <c>&lt;!-- ado-write:KEY --&gt;</c> line at the end of every posted comment.</summary>
internal static partial class CommentMarker
{
    public const string Prefix = "<!-- ado-write:";

    public static string Render(string key) => $"{Prefix}{key} -->";

    /// <summary>The attachment comment that ties a work-item attachment to the post with <paramref name="key"/> and to the file's content.</summary>
    public static string AttachmentTag(string key, CommentAttachment file) => $"ado-write:{key} sha256:{file.Sha256}";

    public static bool Marks(string? text, string key) =>
        text?.Contains(Render(key), StringComparison.Ordinal) == true;

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    /// <summary>Colons let a caller namespace keys, such as <c>refine-it:123:r2</c>; nothing can close the HTML comment.</summary>
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex KeyPattern();
}
