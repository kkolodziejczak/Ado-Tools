using System.CommandLine;
using System.Text.Json.Serialization;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoWrite.Approval;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using AdoWrite.Reviews;

namespace AdoWrite.Commands;

/// <summary>
/// Shared plumbing for the write commands: resolve the target and its input, load configuration,
/// check the organization, read the token, apply the whole-command timeout, open one session,
/// and map every expected failure onto a single JSON envelope and exit code. Nothing reaches Azure
/// DevOps until the target, the input and the configuration are valid. Unless <paramref name="approval"/>
/// is granted, a write that is not a dry run is queued for the approval window instead of posted.
/// </summary>
internal sealed class WriteCommandContext(
    CommandServices services,
    IAdoWriteSessionFactory sessionFactory,
    WriteApproval approval,
    ToolCommandNames names)
{
    private readonly string _loginCommand = names.Root.Invocation("auth login");

    /// <summary>A comment command: the target is resolved first, then the draft is read and checked.</summary>
    public Task<int> RunAsync(
        string kind,
        ParseResult parseResult,
        CommentOptions options,
        Func<CommentTarget> resolveTarget,
        CancellationToken cancellationToken) =>
        RunAsync(
            kind,
            parseResult,
            options.Config,
            async token =>
            {
                CommentTarget target = resolveTarget();
                CommentDraft draft = await options.ReadDraftAsync(parseResult, token);
                return new WriteTarget<PostedCommentDocument>(
                    target.Reference,
                    target.Query,
                    async (session, organizationUrl, requestToken) =>
                        new WriteOutcome<PostedCommentDocument>([await target.PostAsync(session, organizationUrl, draft, requestToken)]),
                    draft.DryRun,
                    draft.Body);
            },
            cancellationToken);

    /// <param name="prepare">Validates the command input without Azure DevOps and returns what to write.</param>
    public async Task<int> RunAsync<TDocument>(
        string kind,
        ParseResult parseResult,
        Option<string?> config,
        Func<CancellationToken, Task<WriteTarget<TDocument>>> prepare,
        CancellationToken cancellationToken)
    {
        bool compact = parseResult.GetValue(services.CompactOption);
        try
        {
            WriteTarget<TDocument> target = await prepare(cancellationToken);
            AdoReadConfiguration configuration = await services.ConfigurationStore.LoadAsync(
                parseResult.GetValue(config),
                cancellationToken);
            if (!target.Reference.IsFor(configuration.OrganizationUrl))
            {
                throw WriteException.Rejected(
                    "organizationMismatch",
                    "The URL belongs to a different organization than the configured organization.");
            }

            string pat = services.PatProvider.GetRequiredPat(configuration.OrganizationUrl);
            if (!target.IsPreview && approval.Outbox is { } outbox)
            {
                return await QueueAsync(kind, parseResult, target, outbox, compact, cancellationToken);
            }

            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoWriteSession session = sessionFactory.Create(configuration.OrganizationUrl, pat);
            WriteOutcome<TDocument> outcome = await target.WriteAsync(session, configuration.OrganizationUrl, requestTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = new SearchEnvelope<TDocument>(
                "1.0",
                kind,
                outcome.Failure is null ? "success" : "partial",
                target.Query,
                services.TimeProvider.GetUtcNow(),
                outcome.Results,
                outcome.Failure is null ? [] : [WithScopeHint(outcome.Failure, kind)]);
            await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return outcome.Failure is null ? ExitCodes.Success : ExitCodes.Partial;
        }
        catch (WriteException exception)
        {
            return await WriteErrorAsync(kind, WithScopeHint(exception.ToError(), kind), exception.ExitCode, compact, cancellationToken);
        }
        catch (ConfigurationException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("configuration", null, exception.Code, exception.Message, false),
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                cancellationToken);
        }
        catch (CredentialException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("authentication", null, exception.Code, exception.Message, false),
                ExitCodes.Error,
                compact,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("azureDevOps", null, "timeout", WriteException.TimeoutMessage, true),
                ExitCodes.Error,
                compact,
                CancellationToken.None);
        }
    }

    private async Task<int> QueueAsync<TDocument>(
        string kind,
        ParseResult parseResult,
        WriteTarget<TDocument> target,
        ApprovalOutbox outbox,
        bool compact,
        CancellationToken cancellationToken)
    {
        ApprovalRequest request = outbox.Enqueue(kind, parseResult, target.Body);
        ApprovalWindowState window = approval.Window?.EnsureOpen() ?? ApprovalWindowState.NotOpened;
        string message = "Not posted yet. It waits for the person the token belongs to, who posts or rejects it in the ado-write approval window. "
            + "Don't assume it was posted: check later with ado-read."
            + (window == ApprovalWindowState.NotOpened
                ? $" Ask them to run '{names.Root.Invocation("approval open")}' in a terminal of their own."
                : string.Empty);
        var envelope = new SearchEnvelope<PendingWriteDocument>(
            "1.0",
            kind,
            PendingWriteDocument.PendingApproval,
            target.Query,
            services.TimeProvider.GetUtcNow(),
            [new PendingWriteDocument(PendingWriteDocument.PendingApproval, request.Id, WindowName(window), message)],
            []);
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return ExitCodes.Success;
    }

    private static string WindowName(ApprovalWindowState window) => window switch
    {
        ApprovalWindowState.Opened => "opened",
        ApprovalWindowState.AlreadyOpen => "alreadyOpen",
        _ => "notOpened"
    };

    /// <summary>A refused token's error names the scopes this command needs and the login that replaces the token.</summary>
    private SearchError WithScopeHint(SearchError error, string kind) =>
        error with { Message = TokenScopeHint.AppendTo(error.Code, error.Message, ScopesFor(kind), _loginCommand) };

    private static string ScopesFor(string kind) => kind switch
    {
        CommentKinds.WorkItemComment => "Work Items (Read & write), plus Identity (Read) to mention people by mail address",
        CommentKinds.PullRequestComment or CommentKinds.PullRequestReply => "Code (Read & write), plus Identity (Read) to mention people by mail address",
        ReviewKinds.PullRequestRequeue or ReviewKinds.PullRequestVote => "Code (Read & write)",
        CreateKinds.PullRequestCreate => "Code (Read & write), plus Identity (Read) to add reviewers by mail address",
        ThreadKinds.PullRequestResolve or ThreadKinds.PullRequestUnresolve => "Code (Read & write), plus Identity (Read) to mention people by mail address",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown write command.")
    };

    private async Task<int> WriteErrorAsync(
        string kind,
        SearchError error,
        int exitCode,
        bool compact,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(kind, [error], services.TimeProvider.GetUtcNow());
        if (exitCode == ExitCodes.NotFound)
        {
            envelope = envelope with { Status = "notFound" };
        }

        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }
}

/// <summary>
/// What a write command acts on: the parsed reference, the query echoed in the envelope, and the write
/// itself. <see cref="IsPreview"/> marks a dry run, which never waits for approval; <see cref="Body"/> is
/// the text read from <c>--body</c> or <c>--body-file</c>, which a queued write keeps.
/// </summary>
internal sealed record WriteTarget<TDocument>(
    AdoReference Reference,
    object Query,
    Func<IAdoWriteSession, Uri, CancellationToken, Task<WriteOutcome<TDocument>>> WriteAsync,
    bool IsPreview,
    string? Body = null);

/// <summary>A write queued for approval: nothing was posted yet.</summary>
internal sealed record PendingWriteDocument(string Outcome, string ApprovalId, string Window, string Message)
{
    public const string PendingApproval = "pendingApproval";
}

/// <summary>What a write did. With a <see cref="Failure"/> the results are partial: the write stopped part-way.</summary>
internal sealed record WriteOutcome<TDocument>(IReadOnlyList<TDocument> Results, SearchError? Failure = null);

/// <summary>What a comment command posts to: the parsed reference, the query echoed in the envelope, and the post itself.</summary>
internal sealed record CommentTarget(
    AdoReference Reference,
    CommentQuery Query,
    Func<IAdoWriteSession, Uri, CommentDraft, CancellationToken, Task<PostedCommentDocument>> PostAsync);

internal sealed record CommentQuery(
    int Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ThreadId = null);
