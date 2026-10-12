using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Pipelines;
using AdoRead.Privacy;

namespace AdoRead.Commands;

/// <summary>Pipeline diagnosis: why a pull request's pipelines failed, and the failed tests of one build.</summary>
public static class PipelineModule
{
    private const int DefaultLogLines = 50;
    private const int MaxLogLines = 500;
    private const int DefaultTop = 100;
    private const int MaxTop = 1000;
    private const string LogLinesMessage = "The log line count must be between 1 and 500.";
    private const string ProjectConflictMessage =
        "The project in the URL differs from --project. Pass one project, or none, and let the URL decide.";

    public static CommandModule Definition { get; } = new(
        "pipelines",
        "Explain why pull-request pipelines failed: failed steps, failed tests, messages and log tails.",
        Configure);

    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        Command pullRequest = CreatePullRequestCommand(names, services);
        Command run = CreateRunCommand(names, services);
        Command tests = CreateTestsCommand(names, services);
        target.Subcommands.Add(pullRequest);
        target.Subcommands.Add(run);
        target.Subcommands.Add(tests);
        return new Dictionary<Command, string>
        {
            [pullRequest] = PipelineKinds.PullRequest,
            [run] = PipelineKinds.Run,
            [tests] = PipelineKinds.Tests
        };
    }

    private static Command CreatePullRequestCommand(ToolCommandNames names, CommandServices services)
    {
        var reference = new Argument<string>("pull-request-url-or-id")
        {
            Description = "Positive pull-request ID or Azure DevOps pull-request URL."
        };
        Option<string?> project = ReadCommandContext.CreateProjectOption();
        var repository = new Option<string?>("--repository")
        {
            Description = "Repository name the pull request must belong to. Checked against the pull request."
        };
        Option<int?> logLines = CreateLogLinesOption();
        Option<string?> config = ReadCommandContext.CreateConfigOption();
        var privacyOptions = new PrivacyRequestOptions();
        var command = new Command(
            "pr",
            "Show the latest validation run of each pipeline for a pull request, with why it failed.");
        command.Arguments.Add(reference);
        command.Options.Add(project);
        command.Options.Add(repository);
        command.Options.Add(logLines);
        command.Options.Add(config);
        privacyOptions.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => RunPullRequestAsync(
            services,
            names,
            parseResult.GetValue(reference),
            parseResult.GetValue(project),
            parseResult.GetValue(repository),
            parseResult.GetValue(logLines),
            parseResult.GetValue(config),
            parseResult.GetValue(services.CompactOption),
            privacyOptions.Read(parseResult),
            cancellationToken));
        return command;
    }

    private static Command CreateRunCommand(ToolCommandNames names, CommandServices services)
    {
        var reference = new Argument<string>("build-url-or-id")
        {
            Description = "Positive build ID or Azure DevOps build results URL (.../_build/results?buildId=N)."
        };
        Option<string?> project = ReadCommandContext.CreateProjectOption();
        Option<int?> logLines = CreateLogLinesOption();
        Option<string?> config = ReadCommandContext.CreateConfigOption();
        var privacyOptions = new PrivacyRequestOptions();
        var command = new Command("run", "Diagnose one pipeline run: failed steps, failed tests, messages and log tails.");
        command.Arguments.Add(reference);
        command.Options.Add(project);
        command.Options.Add(logLines);
        command.Options.Add(config);
        privacyOptions.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => RunBuildAsync(
            services,
            names,
            parseResult.GetValue(reference),
            parseResult.GetValue(project),
            parseResult.GetValue(logLines),
            parseResult.GetValue(config),
            parseResult.GetValue(services.CompactOption),
            privacyOptions.Read(parseResult),
            cancellationToken));
        return command;
    }

    private static Command CreateTestsCommand(ToolCommandNames names, CommandServices services)
    {
        var reference = new Argument<string>("build-url-or-id")
        {
            Description = "Positive build ID or Azure DevOps build results URL (.../_build/results?buildId=N)."
        };
        Option<string?> project = ReadCommandContext.CreateProjectOption();
        var outcome = new Option<string?>("--outcome")
        {
            Description = "failed (default) or all test results of the build."
        };
        var top = new Option<int?>("--top")
        {
            Description = $"Maximum number of test results. Default {DefaultTop}, maximum {MaxTop}."
        };
        Option<string?> config = ReadCommandContext.CreateConfigOption();
        var privacyOptions = new PrivacyRequestOptions();
        var command = new Command("tests", "List the test results of a build, with their messages and stack traces.");
        command.Arguments.Add(reference);
        command.Options.Add(project);
        command.Options.Add(outcome);
        command.Options.Add(top);
        command.Options.Add(config);
        privacyOptions.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => RunTestsAsync(
            services,
            names,
            parseResult.GetValue(reference),
            parseResult.GetValue(project),
            parseResult.GetValue(outcome),
            parseResult.GetValue(top),
            parseResult.GetValue(config),
            parseResult.GetValue(services.CompactOption),
            privacyOptions.Read(parseResult),
            cancellationToken));
        return command;
    }

    private static Option<int?> CreateLogLinesOption() => new("--log-lines")
    {
        Description = $"Last log lines to return per failed task. Default {DefaultLogLines}, range 1 to {MaxLogLines}."
    };

    private static async Task<int> RunPullRequestAsync(
        CommandServices services,
        ToolCommandNames names,
        string? value,
        string? projectOption,
        string? repository,
        int? logLines,
        string? configPath,
        bool compact,
        PrivacyRequest privacy,
        CancellationToken cancellationToken)
    {
        AdoReference? reference = AdoReferenceParser.ParsePullRequest(value);
        if (reference is null)
        {
            return await WriteErrorAsync(
                services,
                PipelineKinds.PullRequest,
                "invalidPullRequestId",
                "The pull-request reference must be a positive integer or an Azure DevOps pull-request URL.",
                compact,
                cancellationToken);
        }

        if (!IsValidLogLines(logLines))
        {
            return await WriteErrorAsync(services, PipelineKinds.PullRequest, "invalidLogLines", LogLinesMessage, compact, cancellationToken);
        }

        if (ProjectConflict(reference, projectOption))
        {
            return await WriteErrorAsync(services, PipelineKinds.PullRequest, "projectMismatch", ProjectConflictMessage, compact, cancellationToken);
        }

        var request = new ReadRequest(configPath, compact, privacy, reference, reference.Project ?? projectOption);
        return await RunAsync(
            services,
            names,
            PipelineKinds.PullRequest,
            request,
            (scope, token) => scope.Reader.ReadPullRequestAsync(
                new PullRequestPipelinesQuery(
                    reference.Id,
                    scope.OrganizationUrl,
                    scope.Project,
                    repository,
                    logLines ?? DefaultLogLines),
                token),
            cancellationToken);
    }

    private static async Task<int> RunBuildAsync(
        CommandServices services,
        ToolCommandNames names,
        string? value,
        string? projectOption,
        int? logLines,
        string? configPath,
        bool compact,
        PrivacyRequest privacy,
        CancellationToken cancellationToken)
    {
        AdoReference? reference = AdoReferenceParser.ParseBuild(value);
        if (reference is null)
        {
            return await WriteErrorAsync(
                services,
                PipelineKinds.Run,
                "invalidBuildId",
                "The build reference must be a positive integer or an Azure DevOps build results URL with a buildId.",
                compact,
                cancellationToken);
        }

        if (!IsValidLogLines(logLines))
        {
            return await WriteErrorAsync(services, PipelineKinds.Run, "invalidLogLines", LogLinesMessage, compact, cancellationToken);
        }

        if (ProjectConflict(reference, projectOption))
        {
            return await WriteErrorAsync(services, PipelineKinds.Run, "projectMismatch", ProjectConflictMessage, compact, cancellationToken);
        }

        var request = new ReadRequest(configPath, compact, privacy, reference, reference.Project ?? projectOption);
        return await RunAsync(
            services,
            names,
            PipelineKinds.Run,
            request,
            (scope, token) => scope.Reader.ReadBuildAsync(
                new PipelineRunQuery(reference.Id, scope.OrganizationUrl, scope.Project, logLines ?? DefaultLogLines),
                token),
            cancellationToken);
    }

    private static async Task<int> RunTestsAsync(
        CommandServices services,
        ToolCommandNames names,
        string? value,
        string? projectOption,
        string? outcome,
        int? top,
        string? configPath,
        bool compact,
        PrivacyRequest privacy,
        CancellationToken cancellationToken)
    {
        AdoReference? reference = AdoReferenceParser.ParseBuild(value);
        if (reference is null)
        {
            return await WriteErrorAsync(
                services,
                PipelineKinds.Tests,
                "invalidBuildId",
                "The build reference must be a positive integer or an Azure DevOps build results URL with a buildId.",
                compact,
                cancellationToken);
        }

        string normalizedOutcome = outcome?.Trim().ToLowerInvariant() ?? "failed";
        if (normalizedOutcome is not ("failed" or "all"))
        {
            return await WriteErrorAsync(services, PipelineKinds.Tests, "invalidOutcome", "The outcome must be failed or all.", compact, cancellationToken);
        }

        if ((top ?? DefaultTop) is < 1 or > MaxTop)
        {
            return await WriteErrorAsync(
                services,
                PipelineKinds.Tests,
                "invalidTop",
                $"The test result limit must be between 1 and {MaxTop}.",
                compact,
                cancellationToken);
        }

        if (ProjectConflict(reference, projectOption))
        {
            return await WriteErrorAsync(services, PipelineKinds.Tests, "projectMismatch", ProjectConflictMessage, compact, cancellationToken);
        }

        var request = new ReadRequest(configPath, compact, privacy, reference, reference.Project ?? projectOption);
        return await RunAsync(
            services,
            names,
            PipelineKinds.Tests,
            request,
            (scope, token) => scope.Reader.ReadTestsAsync(
                new PipelineTestsQuery(reference.Id, scope.OrganizationUrl, scope.Project, normalizedOutcome, top ?? DefaultTop),
                token),
            cancellationToken);
    }

    /// <summary>
    /// Runs one read in the order every read command uses: configuration, organization match, privacy,
    /// token, then one session. Only the expected failures become envelopes; each keeps its exit code.
    /// </summary>
    private static async Task<int> RunAsync<T>(
        CommandServices services,
        ToolCommandNames names,
        string kind,
        ReadRequest request,
        Func<ReadScope, CancellationToken, Task<SearchEnvelope<T>>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoReadConfiguration configuration = await services.ConfigurationStore.LoadAsync(request.ConfigPath, cancellationToken);
            if (request.Reference is not null && !request.Reference.IsFor(configuration.OrganizationUrl))
            {
                return await WriteErrorAsync(
                    services,
                    kind,
                    "organizationMismatch",
                    "The URL belongs to a different organization than the configured organization.",
                    request.Compact,
                    cancellationToken);
            }

            EffectivePrivacy privacy = services.Privacy.Require(configuration.OrganizationUrl, request.Privacy);
            string project = new ReadCommandContext(services, names).RequireProject(request.Project, configuration);
            string pat = services.PatProvider.GetRequiredPat(configuration.OrganizationUrl);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = services.SessionFactory.Create(configuration.OrganizationUrl, pat);
            var scope = new ReadScope(
                new PipelineReader(
                    session.Projects,
                    session.PullRequests,
                    session.Pipelines,
                    configuration.OrganizationUrl,
                    services.TimeProvider,
                    privacy),
                configuration.OrganizationUrl,
                project);
            SearchEnvelope<T> envelope = await read(scope, requestTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            await services.JsonOutput.WriteAsync(envelope, request.Compact, cancellationToken);
            return ExitCodes.FromStatus(envelope.Status);
        }
        catch (ConfigurationException exception)
        {
            return await WriteErrorAsync(
                services,
                kind,
                exception.Code,
                exception.Message,
                request.Compact,
                cancellationToken,
                scope: "configuration");
        }
        catch (PrivacyException exception)
        {
            return await WriteErrorAsync(
                services,
                kind,
                exception.Code,
                exception.Message,
                request.Compact,
                cancellationToken,
                scope: "privacy");
        }
        catch (CredentialException exception)
        {
            return await WriteErrorAsync(
                services,
                kind,
                exception.Code,
                exception.Message,
                request.Compact,
                cancellationToken,
                scope: "authentication",
                exitCode: ExitCodes.Error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await WriteErrorAsync(
                services,
                kind,
                "timeout",
                "The Azure DevOps request timed out.",
                request.Compact,
                CancellationToken.None,
                scope: "azureDevOps",
                exitCode: ExitCodes.Error,
                transient: true);
        }
    }

    private static bool IsValidLogLines(int? logLines) => logLines is null or >= 1 and <= MaxLogLines;

    private static bool ProjectConflict(AdoReference reference, string? projectOption) =>
        reference.Project is not null
        && projectOption is not null
        && !string.Equals(reference.Project, projectOption, StringComparison.OrdinalIgnoreCase);

    private static async Task<int> WriteErrorAsync(
        CommandServices services,
        string kind,
        string code,
        string message,
        bool compact,
        CancellationToken cancellationToken,
        string scope = "command",
        int exitCode = ExitCodes.InvalidInputOrConfiguration,
        bool transient = false)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            kind,
            [new SearchError(scope, null, code, message, transient)],
            services.TimeProvider.GetUtcNow());
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }

    private sealed record ReadRequest(
        string? ConfigPath,
        bool Compact,
        PrivacyRequest Privacy,
        AdoReference? Reference,
        string? Project);

    /// <summary>What a read command needs once configuration, privacy and the session are in place.</summary>
    private sealed record ReadScope(PipelineReader Reader, Uri OrganizationUrl, string Project);
}
