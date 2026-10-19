using System.CommandLine;
using AdoRead.Commands;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;

namespace AdoWrite.Commands;

/// <summary>
/// Pull-request comments: <c>comment</c> opens a new thread, general or attached to lines with
/// <c>--file</c> and <c>--line</c>; <c>reply</c> continues an existing thread.
/// </summary>
public static class PullRequestCommentModule
{
    public static CommandModule Definition { get; } = Create(new AdoWriteSessionFactory());

    public static CommandModule Create(IAdoWriteSessionFactory sessionFactory) => new(
        "pr",
        "Comment on a pull request or on its lines, or reply in one of its threads.",
        (target, _, services) => Configure(target, new CommentCommandContext(services, sessionFactory)));

    private static IReadOnlyDictionary<Command, string> Configure(Command target, CommentCommandContext context)
    {
        Command comment = CreateCommentCommand(context);
        Command reply = CreateReplyCommand(context);
        target.Subcommands.Add(comment);
        target.Subcommands.Add(reply);
        return new Dictionary<Command, string>
        {
            [comment] = CommentKinds.PullRequestComment,
            [reply] = CommentKinds.PullRequestReply
        };
    }

    private static Command CreateCommentCommand(CommentCommandContext context)
    {
        Argument<string> reference = CreateReferenceArgument();
        var file = new Option<string?>("--file")
        {
            Description = "Attach the comment to this file of the pull request, for example /src/App.cs."
        };
        var line = new Option<int?>("--line")
        {
            Description = "First line of the comment, numbered from 1. Needs --file."
        };
        var endLine = new Option<int?>("--end-line")
        {
            Description = "Last line of the comment. Defaults to --line."
        };
        var side = new Option<string?>("--side")
        {
            Description = "right (default) comments on the new version of the file, left on the old one."
        };
        var options = new CommentOptions();
        var command = new Command(
            "comment",
            "Start a new thread on a pull request. With --file and --line it is attached to those lines.");
        command.Arguments.Add(reference);
        command.Options.Add(file);
        command.Options.Add(line);
        command.Options.Add(endLine);
        command.Options.Add(side);
        options.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            CommentKinds.PullRequestComment,
            parseResult,
            options,
            () =>
            {
                AdoReference pullRequest = ParsePullRequest(parseResult.GetValue(reference));
                LineSelection? lines = ReadLines(
                    parseResult.GetValue(file),
                    parseResult.GetValue(line),
                    parseResult.GetValue(endLine),
                    parseResult.GetValue(side));
                return new CommentTarget(
                    pullRequest,
                    new CommentQuery(pullRequest.Id),
                    (session, organizationUrl, draft, token) =>
                        new PullRequestCommenter(session.PullRequests, session.People, organizationUrl)
                            .CommentAsync(pullRequest.Id, lines, draft, token));
            },
            cancellationToken));
        return command;
    }

    private static Command CreateReplyCommand(CommentCommandContext context)
    {
        Argument<string> reference = CreateReferenceArgument();
        var thread = new Option<int?>("--thread")
        {
            Description = "ID of the thread to reply in, as the pull-request read shows it."
        };
        var options = new CommentOptions();
        var command = new Command("reply", "Reply in an existing pull-request thread.");
        command.Arguments.Add(reference);
        command.Options.Add(thread);
        options.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            CommentKinds.PullRequestReply,
            parseResult,
            options,
            () =>
            {
                AdoReference pullRequest = ParsePullRequest(parseResult.GetValue(reference));
                int threadId = parseResult.GetValue(thread) is > 0 and int id
                    ? id
                    : throw WriteException.Rejected("threadRequired", "--thread must name a positive thread ID.");
                return new CommentTarget(
                    pullRequest,
                    new CommentQuery(pullRequest.Id, threadId),
                    (session, organizationUrl, draft, token) =>
                        new PullRequestCommenter(session.PullRequests, session.People, organizationUrl)
                            .ReplyAsync(pullRequest.Id, threadId, draft, token));
            },
            cancellationToken));
        return command;
    }

    private static Argument<string> CreateReferenceArgument() => new("id-or-url")
    {
        Description = "Positive pull-request ID or Azure DevOps pull-request URL."
    };

    private static AdoReference ParsePullRequest(string? value) =>
        AdoReferenceParser.ParsePullRequest(value)
        ?? throw WriteException.Rejected(
            "invalidPullRequestId",
            "The pull-request reference must be a positive integer or an Azure DevOps pull-request URL.");

    /// <summary>No <c>--file</c> means a general comment, so the line options are then rejected rather than ignored.</summary>
    private static LineSelection? ReadLines(string? file, int? line, int? endLine, string? side)
    {
        if (file is null)
        {
            return line is null && endLine is null && side is null
                ? null
                : throw WriteException.Rejected("fileRequired", "--line, --end-line and --side need --file.");
        }

        string path = "/" + file.Trim().Replace('\\', '/').TrimStart('/');
        if (path.Length == 1)
        {
            throw WriteException.Rejected("fileRequired", "--file must name a file of the pull request.");
        }

        if (line is not > 0)
        {
            throw WriteException.Rejected("invalidLine", "--file needs --line, a positive line number.");
        }

        int last = endLine ?? line.Value;
        if (last < line)
        {
            throw WriteException.Rejected("invalidLine", "--end-line must not be before --line.");
        }

        string normalizedSide = side?.Trim().ToLowerInvariant() ?? LineSelection.Right;
        return normalizedSide is LineSelection.Right or LineSelection.Left
            ? new LineSelection(path, line.Value, last, normalizedSide)
            : throw WriteException.Rejected("invalidSide", "--side must be right or left.");
    }
}
