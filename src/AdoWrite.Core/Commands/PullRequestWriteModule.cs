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
        (target, names, services) => Configure(target, new WriteCommandContext(services, sessionFactory, names.Root.Invocation("auth login"))));

    private static IReadOnlyDictionary<Command, string> Configure(Command target, WriteCommandContext context)
    {
        Command comment = CreateCommentCommand(context);
        Command reply = CreateReplyCommand(context);
        Command requeue = CreateRequeueCommand(context);
        Command vote = CreateVoteCommand(context);
        Command resolve = CreateThreadStatusCommand(
            context,
            "resolve",
            "Mark a pull-request thread resolved, optionally after replying in it.",
            ThreadKinds.PullRequestResolve,
            resolve: true);
        Command unresolve = CreateThreadStatusCommand(
            context,
            "unresolve",
            "Reopen a resolved thread by making it active again, optionally after replying in it.",
            ThreadKinds.PullRequestUnresolve,
            resolve: false);
        Command create = CreateCreateCommand(context);
        target.Subcommands.Add(create);
        target.Subcommands.Add(comment);
        target.Subcommands.Add(reply);
        target.Subcommands.Add(resolve);
        target.Subcommands.Add(unresolve);
        target.Subcommands.Add(requeue);
        target.Subcommands.Add(vote);
        return new Dictionary<Command, string>
        {
            [create] = CreateKinds.PullRequestCreate,
            [comment] = CommentKinds.PullRequestComment,
            [reply] = CommentKinds.PullRequestReply,
            [resolve] = ThreadKinds.PullRequestResolve,
            [unresolve] = ThreadKinds.PullRequestUnresolve,
            [requeue] = ReviewKinds.PullRequestRequeue,
            [vote] = ReviewKinds.PullRequestVote
        };
    }

    private static Command CreateCreateCommand(WriteCommandContext context)
    {
        var project = new Option<string?>("--project") { Description = "Project of the repository. Needed when --repository is a name." };
        var repository = new Option<string?>("--repository") { Description = "Repository name, or its ID." };
        var source = new Option<string?>("--source") { Description = "Branch with the changes, for example feature/retry or refs/heads/feature/retry." };
        var target = new Option<string?>("--target") { Description = "Branch to merge into. Defaults to the repository's default branch." };
        var title = new Option<string?>("--title") { Description = "Title of the pull request." };
        var body = new Option<string?>("--body") { Description = "Description in Markdown. Use --body-file for longer text." };
        var bodyFile = new Option<string?>("--body-file") { Description = "Read the Markdown description from this UTF-8 file, or from standard input when the path is '-'." };
        var agent = new Option<string?>("--agent")
        {
            Description = "The AI agent and model that wrote the pull request, for example \"Claude Opus 5.5\". Adds a footer naming it and you to the description."
        };
        var reviewer = new Option<string[]>("--reviewer") { Description = "Add a reviewer by mail address or identity ID. Repeat for several people." };
        var workItem = new Option<string[]>("--work-item") { Description = "Link a work item by its ID. Repeat for several." };
        var draft = new Option<bool>("--draft") { Description = "Open the pull request as a draft." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Check the repository, branches and reviewers and show the description, without creating anything." };
        Option<string?> config = ReadCommandContext.CreateConfigOption();
        var command = new Command(
            "create",
            "Open a pull request. An active pull request for the same branches is returned instead, so running it again opens no second one.");
        foreach (Option option in new Option[] { project, repository, source, target, title, body, bodyFile, agent, reviewer, workItem, draft, dryRun, config })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            CreateKinds.PullRequestCreate,
            parseResult,
            config,
            async token =>
            {
                string repositoryName = Clean(parseResult.GetValue(repository))
                    ?? throw WriteException.Rejected("repositoryRequired", "--repository must name the repository, by name or ID.");
                string? projectName = Clean(parseResult.GetValue(project));
                if (projectName is null && !Guid.TryParse(repositoryName, out _))
                {
                    throw WriteException.Rejected("projectRequired", "--repository is a name, so --project must name its project. A repository ID needs no project.");
                }

                string sourceRef = PullRequestRequest.RefName(Clean(parseResult.GetValue(source))
                    ?? throw WriteException.Rejected("sourceRequired", "--source must name the branch with the changes."), "--source");
                string? targetRef = Clean(parseResult.GetValue(target)) is { } named ? PullRequestRequest.RefName(named, "--target") : null;
                string titleText = Clean(parseResult.GetValue(title))
                    ?? throw WriteException.Rejected("titleRequired", "--title must give the pull request a title.");
                string? description = await ReadDescriptionAsync(parseResult.GetValue(body), parseResult.GetValue(bodyFile), token);
                int[] workItems = (parseResult.GetValue(workItem) ?? [])
                    .Select(value => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) && id > 0
                        ? id
                        : throw WriteException.Rejected("invalidWorkItemId", "--work-item must be a positive work-item ID."))
                    .Distinct()
                    .ToArray();
                var request = new PullRequestRequest(
                    projectName,
                    repositoryName,
                    sourceRef,
                    targetRef,
                    titleText,
                    description,
                    Clean(parseResult.GetValue(agent)),
                    [.. (parseResult.GetValue(reviewer) ?? []).Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
                    workItems,
                    parseResult.GetValue(draft),
                    parseResult.GetValue(dryRun));
                return new WriteTarget<CreatedPullRequestDocument>(
                    new AdoReference(0, null),
                    new CreateQuery(projectName, repositoryName, sourceRef, targetRef),
                    async (session, organizationUrl, requestToken) => new WriteOutcome<CreatedPullRequestDocument>(
                    [
                        await new PullRequestCreator(session.PullRequestCreation, session.People, organizationUrl).CreateAsync(request, requestToken)
                    ]));
            },
            cancellationToken));
        return command;
    }

    /// <summary>At most one of the two gives the description, which Azure DevOps keeps up to 4,000 characters.</summary>
    private static async Task<string?> ReadDescriptionAsync(string? body, string? bodyFile, CancellationToken cancellationToken)
    {
        if (body is not null && bodyFile is not null)
        {
            throw WriteException.Rejected("conflictingOptions", "Give the description with --body or --body-file, not both.");
        }

        string? text = body ?? (bodyFile is null ? null : await CommentOptions.ReadBodyFileAsync(bodyFile, cancellationToken));
        return text is { Length: > PullRequestRequest.MaxDescriptionLength }
            ? throw WriteException.Rejected("descriptionTooLong", $"The description has {text.Length} characters; Azure DevOps keeps {PullRequestRequest.MaxDescriptionLength}.")
            : text;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Command CreateThreadStatusCommand(WriteCommandContext context, string name, string description, string kind, bool resolve)
    {
        Argument<string> reference = CreateReferenceArgument();
        var thread = new Option<int?>("--thread")
        {
            Description = "ID of the thread, as the pull-request read shows it."
        };
        var options = new CommentOptions();
        var command = new Command(name, description + " With --body or --body-file the reply is posted first, and only once.");
        command.Arguments.Add(reference);
        command.Options.Add(thread);
        options.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            kind,
            parseResult,
            options.Config,
            async token =>
            {
                AdoReference pullRequest = ParsePullRequest(parseResult.GetValue(reference));
                int threadId = parseResult.GetValue(thread) is > 0 and int id
                    ? id
                    : throw WriteException.Rejected("threadRequired", "--thread must name a positive thread ID.");
                CommentDraft? reply = await options.ReadOptionalDraftAsync(parseResult, token);
                bool dryRun = parseResult.GetValue(options.DryRun);
                return new WriteTarget<ThreadStatusDocument>(
                    pullRequest,
                    new CommentQuery(pullRequest.Id, threadId),
                    (session, organizationUrl, requestToken) =>
                        new ThreadStatusChanger(session.PullRequests, session.People, organizationUrl)
                            .ChangeAsync(pullRequest.Id, threadId, resolve, reply, dryRun, requestToken));
            },
            cancellationToken));
        return command;
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
                    async (session, _, token) =>
                    {
                        RequeueReport report = await new BuildRequeuer(session.PullRequests, session.Reviews)
                            .RequeueAsync(pullRequest.Id, selected, preview, token);
                        return new WriteOutcome<RequeuedBuildDocument>(report.Builds, report.Failure);
                    }));
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
        var command = new Command("vote", "Vote on a pull request as the person the token belongs to.");
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
                        new WriteOutcome<CastVoteDocument>(
                        [
                            await new PullRequestVoter(session.PullRequests, session.Reviews, session.People, organizationUrl)
                                .VoteAsync(pullRequest.Id, chosen, preview, token)
                        ])));
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

internal sealed record CreateQuery(string? Project, string Repository, string SourceRefName, string? TargetRefName);
