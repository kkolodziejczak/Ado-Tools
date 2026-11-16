using System.CommandLine;
using AdoRead.Commands;
using AdoRead.Commands.Shared;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using AdoWrite.Reviews;

namespace AdoWrite.Commands;

/// <summary>
/// Pull-request writes: <c>comment</c> opens a new thread, general or attached to lines with
/// <c>--file</c> and <c>--line</c>; <c>reply</c> continues an existing thread; <c>requeue</c> runs
/// expired or named build policies again; <c>vote</c> sets the token owner's vote.
/// </summary>
public static class PullRequestWriteModule
{
    public static CommandModule Definition { get; } = Create(new AdoWriteSessionFactory());

    public static CommandModule Create(IAdoWriteSessionFactory sessionFactory) => new(
        "pr",
        "Comment on a pull request or on its lines, reply in one of its threads, requeue its builds, or vote on it.",
        (target, _, services) => Configure(target, new WriteCommandContext(services, sessionFactory)));

    private static IReadOnlyDictionary<Command, string> Configure(Command target, WriteCommandContext context)
    {
        Command comment = CreateCommentCommand(context);
        Command reply = CreateReplyCommand(context);
        Command requeue = CreateRequeueCommand(context);
        Command vote = CreateVoteCommand(context);
        target.Subcommands.Add(comment);
        target.Subcommands.Add(reply);
        target.Subcommands.Add(requeue);
        target.Subcommands.Add(vote);
        return new Dictionary<Command, string>
        {
            [comment] = CommentKinds.PullRequestComment,
            [reply] = CommentKinds.PullRequestReply,
            [requeue] = ReviewKinds.PullRequestRequeue,
            [vote] = ReviewKinds.PullRequestVote
        };
    }

    private static Command CreateCommentCommand(WriteCommandContext context)
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

    private static Command CreateReplyCommand(WriteCommandContext context)
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

    private static Command CreateRequeueCommand(WriteCommandContext context)
    {
        Argument<string> reference = CreateReferenceArgument();
        var evaluation = new Option<string[]>("--evaluation")
        {
            Description = "Requeue this build policy evaluation, for example a failed build, instead of every expired one. Repeat for several; the IDs are in the result."
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "List the pull request's build policies and what would be requeued, without requeueing."
        };
        Option<string?> config = ReadCommandContext.CreateConfigOption();
        var command = new Command(
            "requeue",
            "Requeue the pull request's expired build policies, or the named ones. A build that runs or waits to run is left alone.");
        command.Arguments.Add(reference);
        command.Options.Add(evaluation);
        command.Options.Add(dryRun);
        command.Options.Add(config);
        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            ReviewKinds.PullRequestRequeue,
            parseResult,
            config,
            _ =>
            {
                AdoReference pullRequest = ParsePullRequest(parseResult.GetValue(reference));
                HashSet<Guid> selected = ParseEvaluations(parseResult.GetValue(evaluation) ?? []);
                bool preview = parseResult.GetValue(dryRun);
                return Task.FromResult(new WriteTarget<RequeuedBuildDocument>(
                    pullRequest,
                    new RequeueQuery(pullRequest.Id, [.. selected.Order()]),
                    (session, _, token) =>
                        new BuildRequeuer(session.PullRequests, session.Reviews)
                            .RequeueAsync(pullRequest.Id, selected, preview, token)));
            },
            cancellationToken));
        return command;
    }

    private static HashSet<Guid> ParseEvaluations(string[] values) =>
        values
            .Select(value => Guid.TryParse(value, out Guid id)
                ? id
                : throw WriteException.Rejected("invalidEvaluationId", "--evaluation must be a policy evaluation ID (a GUID)."))
            .ToHashSet();

    private static Command CreateVoteCommand(WriteCommandContext context)
    {
        Argument<string> reference = CreateReferenceArgument();
        var vote = new Option<string?>("--vote")
        {
            Description = "Your vote: " + VoteNames + "."
        };
        var confirm = new Option<bool>("--confirm")
        {
            Description = "Required to cast the vote: confirms that the person the token belongs to agreed to it."
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Show the current and the new vote without voting. Needs no --confirm."
        };
        Option<string?> config = ReadCommandContext.CreateConfigOption();
        var command = new Command("vote", "Vote on a pull request as the person the write token belongs to.");
        command.Arguments.Add(reference);
        command.Options.Add(vote);
        command.Options.Add(confirm);
        command.Options.Add(dryRun);
        command.Options.Add(config);
        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            ReviewKinds.PullRequestVote,
            parseResult,
            config,
            _ =>
            {
                AdoReference pullRequest = ParsePullRequest(parseResult.GetValue(reference));
                PullRequestVote chosen = PullRequestVote.Parse(parseResult.GetValue(vote))
                    ?? throw WriteException.Rejected("invalidVote", "--vote must be one of " + VoteNames + ".");
                bool preview = parseResult.GetValue(dryRun);
                if (!preview && !parseResult.GetValue(confirm))
                {
                    throw WriteException.Rejected(
                        "confirmationRequired",
                        "A vote needs --confirm, given only after the person the token belongs to agreed to it. Use --dry-run to check it first.");
                }

                return Task.FromResult(new WriteTarget<CastVoteDocument>(
                    pullRequest,
                    new VoteQuery(pullRequest.Id, chosen.Name),
                    async (session, organizationUrl, token) =>
                        [await new PullRequestVoter(session.PullRequests, session.Reviews, session.People, organizationUrl)
                            .VoteAsync(pullRequest.Id, chosen, preview, token)]));
            },
            cancellationToken));
        return command;
    }

    private static string VoteNames => string.Join(", ", PullRequestVote.All.Select(vote => vote.Name));

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

        string[] segments = file.Trim().Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();
        string path = "/" + string.Join('/', segments);
        if (segments.Length == 0)
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

internal sealed record RequeueQuery(int Id, IReadOnlyList<Guid> Evaluations);

internal sealed record VoteQuery(int Id, string Vote);
