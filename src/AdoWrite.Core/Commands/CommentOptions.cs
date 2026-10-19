using System.CommandLine;
using AdoRead.Commands.Shared;
using AdoWrite.Comments;

namespace AdoWrite.Commands;

/// <summary>The options every comment command shares: the content, who wrote it, mentions, the duplicate key and a dry run.</summary>
internal sealed class CommentOptions
{
    public Option<string?> Body { get; } = new("--body")
    {
        Description = "Comment text in Markdown. Use --body-file for longer text."
    };

    public Option<string?> BodyFile { get; } = new("--body-file")
    {
        Description = "Read the Markdown comment text from this UTF-8 file, or from standard input when the path is '-'."
    };

    public Option<string?> Agent { get; } = new("--agent")
    {
        Description = "The AI agent and model that wrote the comment, for example \"Claude Opus 5.5\". It is named in the comment footer."
    };

    public Option<string[]> Mention { get; } = new("--mention")
    {
        Description = "Mention and notify a person by mail address or identity ID. Repeat for several people."
    };

    public Option<string?> Key { get; } = new("--key")
    {
        Description = "Duplicate key. A comment already carrying it is returned instead of posting again. Defaults to a hash of the target and content."
    };

    public Option<bool> DryRun { get; } = new("--dry-run")
    {
        Description = "Resolve the target, lines and mentions and show the comment text without posting it."
    };

    public Option<string?> Config { get; } = ReadCommandContext.CreateConfigOption();

    public void AddTo(Command command)
    {
        command.Options.Add(Body);
        command.Options.Add(BodyFile);
        command.Options.Add(Agent);
        command.Options.Add(Mention);
        command.Options.Add(Key);
        command.Options.Add(DryRun);
        command.Options.Add(Config);
    }

    /// <summary>Exactly one of <c>--body</c> and <c>--body-file</c> gives the text; the draft validates the rest.</summary>
    public async Task<CommentDraft> ReadDraftAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? body = parseResult.GetValue(Body);
        string? bodyFile = parseResult.GetValue(BodyFile);
        if ((body is null) == (bodyFile is null))
        {
            throw WriteException.Rejected("bodyRequired", "Give the comment text with exactly one of --body and --body-file.");
        }

        return CommentDraft.Create(
            body ?? await ReadBodyFileAsync(bodyFile!, cancellationToken),
            parseResult.GetValue(Agent),
            parseResult.GetValue(Mention) ?? [],
            parseResult.GetValue(Key),
            parseResult.GetValue(DryRun));
    }

    private static async Task<string> ReadBodyFileAsync(string path, CancellationToken cancellationToken)
    {
        if (path == "-")
        {
            return await Console.In.ReadToEndAsync(cancellationToken);
        }

        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw WriteException.Rejected("bodyFileUnreadable", "The --body-file could not be read.");
        }
    }
}
