using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.PullRequests;

namespace AdoRead.Commands;

/// <summary>Pull-request lookup across every accessible project: <c>pr show &lt;id-or-url&gt;</c>.</summary>
public static class PullRequestModule
{
    public static CommandModule Definition { get; } = new(
        "pr",
        "Read pull requests across all accessible projects.",
        Configure);

    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        var referenceArgument = new Argument<string>("id-or-url")
        {
            Description = "Positive pull-request ID or Azure DevOps pull-request URL."
        };
        var configOption = new Option<string?>("--config")
        {
            Description = "Override the default ~/.ado-read/config.json path."
        };
        var command = new Command("show", "Find one pull request across all accessible projects.");
        command.Arguments.Add(referenceArgument);
        command.Options.Add(configOption);
        var privacyOptions = new PrivacyRequestOptions();
        privacyOptions.AddTo(command);
        target.Subcommands.Add(command);
        command.SetAction(async (parseResult, cancellationToken) => await ReadAsync(
            parseResult.GetValue(referenceArgument),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            privacyOptions.Read(parseResult),
            services.Privacy,
            services.ConfigurationStore,
            services.PatProvider,
            services.SessionFactory,
            services.JsonOutput,
            services.TimeProvider,
            cancellationToken));
        Command list = CreateListCommand(names, services);
        target.Subcommands.Add(list);
        return new Dictionary<Command, string>
        {
            [command] = "pullRequestSearch",
            [list] = PullRequestListReader.ListKind
        };
    }

    private const int DefaultTop = 50;
    private const int MaxTop = 200;

    private static Command CreateListCommand(ToolCommandNames names, CommandServices services)
    {
        var context = new ReadCommandContext(services, names.Root);
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var projectOption = new Option<string?>("--project")
        {
            Description = "Only pull requests in this project (name or ID). Omit to list every accessible project."
        };
        var repositoryOption = new Option<string?>("--repository")
        {
            Description = "Only pull requests in this repository (name or ID)."
        };
        var authorOption = new Option<string?>("--author")
        {
            Description = "Only pull requests this person created: 'me' for the token owner, or a unique or display name."
        };
        var reviewerOption = new Option<string?>("--reviewer")
        {
            Description = "Only pull requests this person reviews: 'me' for the token owner, or a unique or display name."
        };
        var voteOption = new Option<string?>("--vote")
        {
            Description = $"With --reviewer: only where that reviewer's vote is {string.Join(", ", PullRequestFilter.Votes.Keys)}."
        };
        var statusOption = new Option<string>("--status")
        {
            Description = $"{string.Join(", ", PullRequestFilter.Statuses)}. Default active.",
            DefaultValueFactory = _ => "active"
        };
        var topOption = new Option<int>("--top")
        {
            Description = $"Return at most this many pull requests, 1 to {MaxTop}. Default {DefaultTop}.",
            DefaultValueFactory = _ => DefaultTop
        };
        var fullOption = new Option<bool>("--full")
        {
            Description = $"Return every match as '{names.Invocation("show")}' would, with threads and work items, instead of a short entry."
        };
        var activityOption = new Option<bool>("--activity")
        {
            Description = "Add each pull request's checks (build and other branch policies, with expired builds) and its threads' status, comment count and last update, to notice what changed since the last call."
        };
        var privacyOptions = new PrivacyRequestOptions();
        var command = new Command(
            "list",
            $"List pull requests by author, reviewer, vote, status or repository, newest first. Read one with '{names.Invocation("show")}'.");
        foreach (Option option in new Option[] { configOption, projectOption, repositoryOption, authorOption, reviewerOption, voteOption, statusOption, topOption, fullOption, activityOption })
        {
            command.Options.Add(option);
        }

        privacyOptions.AddTo(command);
        Option<string?>[] filterOptions = [projectOption, repositoryOption, authorOption, reviewerOption, voteOption];
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool compact = parseResult.GetValue(services.CompactOption);
            bool full = parseResult.GetValue(fullOption);
            string kind = full ? PullRequestListReader.FullKind : PullRequestListReader.ListKind;
            string schemaVersion = full ? "2.0" : "1.0";
            int top = parseResult.GetValue(topOption);
            string status = (parseResult.GetValue(statusOption) ?? string.Empty).Trim().ToLowerInvariant();
            string? vote = Clean(parseResult.GetValue(voteOption))?.ToLowerInvariant();
            string? reviewer = Clean(parseResult.GetValue(reviewerOption));
            Option<string?>? blank = filterOptions.FirstOrDefault(option =>
                parseResult.GetResult(option) is not null && Clean(parseResult.GetValue(option)) is null);
            bool activity = parseResult.GetValue(activityOption);
            (string Code, string Message)? invalid =
                blank is not null ? ("emptyFilter", $"{blank.Name} needs a value.")
                : full && activity ? ("conflictingOptions", "--activity adds to the short entries; --full already returns every thread.")
                : top is < 1 or > MaxTop ? ("invalidTop", $"--top must be between 1 and {MaxTop}.")
                : !PullRequestFilter.Statuses.Contains(status) ? ("invalidStatus", $"--status must be one of {string.Join(", ", PullRequestFilter.Statuses)}.")
                : vote is not null && !PullRequestFilter.Votes.ContainsKey(vote) ? ("invalidVote", $"--vote must be one of {string.Join(", ", PullRequestFilter.Votes.Keys)}.")
                : vote is not null && reviewer is null ? ("conflictingOptions", "--vote needs --reviewer: it filters on that reviewer's vote.")
                : null;
            if (invalid is { } error)
            {
                return await context.RejectAsync(kind, error.Code, error.Message, compact, cancellationToken, schemaVersion);
            }

            var filter = new PullRequestFilter(
                Clean(parseResult.GetValue(projectOption)),
                Clean(parseResult.GetValue(repositoryOption)),
                Clean(parseResult.GetValue(authorOption)),
                reviewer,
                vote,
                status);
            return full
                ? await context.RunAsync(
                    kind,
                    parseResult.GetValue(configOption),
                    compact,
                    privacyOptions.Read(parseResult),
                    (session, configuration, privacy, token) => Reader(session, configuration, privacy).ListFullAsync(filter, top, token),
                    cancellationToken,
                    schemaVersion)
                : await context.RunAsync(
                    kind,
                    parseResult.GetValue(configOption),
                    compact,
                    privacyOptions.Read(parseResult),
                    (session, configuration, privacy, token) => Reader(session, configuration, privacy).ListAsync(filter, top, activity, token),
                    cancellationToken);

            PullRequestListReader Reader(IAdoSession session, AdoReadConfiguration configuration, EffectivePrivacy privacy) =>
                new(
                    session.Projects,
                    session.PullRequests,
                    session.GetTokenOwnerIdAsync,
                    configuration.OrganizationUrl,
                    configuration.MaxParallelProjects,
                    services.TimeProvider,
                    privacy);
        });
        return command;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<int> ReadAsync(
        string? value,
        string? configPath,
        bool compact,
        PrivacyRequest privacyRequest,
        PrivacyGate privacyGate,
        IConfigurationStore configurationStore,
        IPatProvider patProvider,
        IAdoSessionFactory sessionFactory,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        AdoReference? reference = AdoReferenceParser.ParsePullRequest(value);
        if (reference is null)
        {
            return await WriteErrorAsync(
                "command",
                "invalidPullRequestId",
                "The pull-request reference must be a positive integer or an Azure DevOps pull-request URL.",
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                jsonOutput,
                timeProvider,
                cancellationToken);
        }

        try
        {
            AdoReadConfiguration configuration = await configurationStore.LoadAsync(configPath, cancellationToken);
            if (!reference.IsFor(configuration.OrganizationUrl))
            {
                return await WriteErrorAsync(
                    "command",
                    "organizationMismatch",
                    "The pull-request URL belongs to a different organization than the configured organization.",
                    ExitCodes.InvalidInputOrConfiguration,
                    compact,
                    jsonOutput,
                    timeProvider,
                    cancellationToken);
            }

            EffectivePrivacy privacy = privacyGate.Require(configuration.OrganizationUrl, privacyRequest);
            string pat = patProvider.GetRequiredPat(configuration.OrganizationUrl);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessionFactory.Create(configuration.OrganizationUrl, pat);
            var reader = new PullRequestReader(
                session.Projects,
                session.PullRequests,
                configuration.OrganizationUrl,
                configuration.MaxParallelProjects,
                timeProvider,
                privacy);
            SearchEnvelope<PullRequestDocument> envelope = await reader.ReadAsync(reference.Id, requestTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            envelope = WithTimeoutErrors(envelope, requestTimeout.IsCancellationRequested);
            await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return ExitCodes.FromStatus(envelope.Status);
        }
        catch (ConfigurationException exception)
        {
            return await WriteErrorAsync(
                "configuration",
                exception.Code,
                exception.Message,
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                jsonOutput,
                timeProvider,
                cancellationToken);
        }
        catch (PrivacyException exception)
        {
            return await WriteErrorAsync(
                "privacy",
                exception.Code,
                exception.Message,
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                jsonOutput,
                timeProvider,
                cancellationToken);
        }
        catch (CredentialException exception)
        {
            return await WriteErrorAsync(
                "authentication",
                exception.Code,
                exception.Message,
                ExitCodes.Error,
                compact,
                jsonOutput,
                timeProvider,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await WriteErrorAsync(
                "azureDevOps",
                "timeout",
                "The Azure DevOps request timed out.",
                ExitCodes.Error,
                compact,
                jsonOutput,
                timeProvider,
                CancellationToken.None,
                transient: true);
        }
    }

    private static SearchEnvelope<PullRequestDocument> WithTimeoutErrors(
        SearchEnvelope<PullRequestDocument> envelope,
        bool requestTimedOut)
    {
        if (!requestTimedOut || !envelope.Errors.Any(error => error.Code == "cancelled"))
        {
            return envelope;
        }

        SearchError[] errors = envelope.Errors.Select(error => error.Code == "cancelled"
                ? error with
                {
                    Code = "timeout",
                    Message = "The Azure DevOps request timed out before this project completed.",
                    Transient = true
                }
                : error)
            .ToArray();
        return envelope with { Errors = errors };
    }

    private static async Task<int> WriteErrorAsync(
        string scope,
        string code,
        string message,
        int exitCode,
        bool compact,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        bool transient = false)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            "pullRequestSearch",
            [new SearchError(scope, null, code, message, transient)],
            timeProvider.GetUtcNow())
            with
        {
            SchemaVersion = "2.0"
        };
        await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }
}
