using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Text;
using AdoRead.WorkItems;

namespace AdoRead.Commands;

/// <summary>Work-item lookup with comments and optional image download: <c>work-item show &lt;id-or-url&gt;</c>.</summary>
public static class WorkItemModule
{
    public static CommandModule Definition { get; } = new(
        "work-item",
        "Read Azure DevOps work items and their comments.",
        Configure);

    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        var referenceArgument = new Argument<string>("id-or-url")
        {
            Description = "Positive work-item ID or Azure DevOps work-item URL."
        };
        var configOption = new Option<string?>("--config")
        {
            Description = "Override the default ~/.ado-read/config.json path."
        };
        var downloadImagesOption = new Option<string?>("--download-images")
        {
            Description = "Download supported work-item images to an empty directory. Needs the images privacy category."
        };
        var privacyOptions = new PrivacyRequestOptions();
        var command = new Command("show", "Read one Azure DevOps work item and all of its comments.");
        command.Arguments.Add(referenceArgument);
        command.Options.Add(configOption);
        command.Options.Add(downloadImagesOption);
        privacyOptions.AddTo(command);
        target.Subcommands.Add(command);
        command.SetAction(async (parseResult, cancellationToken) => await ReadAsync(
            parseResult.GetValue(referenceArgument),
            parseResult.GetValue(configOption),
            parseResult.GetValue(downloadImagesOption),
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
            [command] = "workItemSearch",
            [list] = WorkItemListReader.ListKind
        };
    }

    private const int DefaultTop = 50;

    /// <summary>One batched read of at most 200 items, the most Azure DevOps returns per call.</summary>
    private const int MaxTop = 200;

    private static Command CreateListCommand(ToolCommandNames names, CommandServices services)
    {
        var context = new ReadCommandContext(services, names.Root);
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var projectOption = new Option<string?>("--project")
        {
            Description = "Only work items in this project. Omit to search every project."
        };
        var tagOption = new Option<string[]>("--tag")
        {
            Description = "Only work items with this tag. Repeat to require several tags.",
            AllowMultipleArgumentsPerToken = true
        };
        var assigneeOption = new Option<string?>("--assignee")
        {
            Description = "Only work items assigned to this person: 'me' for the token owner, 'none' for unassigned, or a name or e-mail."
        };
        var stateOption = new Option<string[]>("--state")
        {
            Description = "Only work items in one of these states, for example --state New --state Active.",
            AllowMultipleArgumentsPerToken = true
        };
        var areaOption = new Option<string[]>("--area")
        {
            Description = @"Only work items under one of these area paths, for example --area 'Payments\Checkout'.",
            AllowMultipleArgumentsPerToken = true
        };
        var typeOption = new Option<string[]>("--type")
        {
            Description = "Only work items of one of these types, for example --type Bug --type 'User Story'.",
            AllowMultipleArgumentsPerToken = true
        };
        var wiqlOption = new Option<string?>("--wiql")
        {
            Description = "Run this flat WIQL query (SELECT [System.Id] FROM WorkItems WHERE …) instead of the filter options."
        };
        var topOption = new Option<int>("--top")
        {
            Description = $"Return at most this many matches, 1 to {MaxTop}. Default {DefaultTop}.",
            DefaultValueFactory = _ => DefaultTop
        };
        var fullOption = new Option<bool>("--full")
        {
            Description = $"Return every match as '{names.Invocation("show")}' would, with comments, instead of a short entry."
        };
        var privacyOptions = new PrivacyRequestOptions();
        var command = new Command(
            "list",
            $"List the work items that match filters or a WIQL query, newest change first. Read one with '{names.Invocation("show")}'.");
        foreach (Option option in new Option[] { configOption, projectOption, tagOption, assigneeOption, stateOption, areaOption, typeOption, wiqlOption, topOption, fullOption })
        {
            command.Options.Add(option);
        }

        privacyOptions.AddTo(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool compact = parseResult.GetValue(services.CompactOption);
            bool full = parseResult.GetValue(fullOption);
            int top = parseResult.GetValue(topOption);
            string? wiql = parseResult.GetValue(wiqlOption);
            var filter = new WorkItemFilter(
                parseResult.GetValue(projectOption),
                parseResult.GetValue(tagOption) ?? [],
                parseResult.GetValue(assigneeOption),
                parseResult.GetValue(stateOption) ?? [],
                parseResult.GetValue(areaOption) ?? [],
                parseResult.GetValue(typeOption) ?? []);
            if (top is < 1 or > MaxTop)
            {
                return await WriteListErrorAsync(services, "invalidTop", $"--top must be between 1 and {MaxTop}.", compact, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(wiql) && !filter.IsEmpty)
            {
                return await WriteListErrorAsync(
                    services,
                    "conflictingOptions",
                    "Pass either --wiql or the filter options (--project, --tag, --assignee, --state, --area, --type), not both.",
                    compact,
                    cancellationToken);
            }

            bool ownQuery = !string.IsNullOrWhiteSpace(wiql);
            string query = ownQuery ? wiql!.Trim() : WorkItemWiql.Build(filter);
            return full
                ? await context.RunAsync(
                    WorkItemListReader.FullKind,
                    parseResult.GetValue(configOption),
                    compact,
                    privacyOptions.Read(parseResult),
                    (session, configuration, privacy, token) => Reader(session, configuration, privacy)
                        .ListFullAsync(new WorkItemListQuery(configuration.OrganizationUrl, ownQuery ? null : filter, query, top, true), token),
                    cancellationToken)
                : await context.RunAsync(
                    WorkItemListReader.ListKind,
                    parseResult.GetValue(configOption),
                    compact,
                    privacyOptions.Read(parseResult),
                    (session, configuration, privacy, token) => Reader(session, configuration, privacy)
                        .ListAsync(new WorkItemListQuery(configuration.OrganizationUrl, ownQuery ? null : filter, query, top, false), token),
                    cancellationToken);

            WorkItemListReader Reader(IAdoSession session, AdoReadConfiguration configuration, EffectivePrivacy privacy) =>
                new(session.WorkItems, configuration.OrganizationUrl, services.TimeProvider, privacy);
        });
        return command;
    }

    private static async Task<int> WriteListErrorAsync(
        CommandServices services,
        string code,
        string message,
        bool compact,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            WorkItemListReader.ListKind,
            [new SearchError("command", null, code, message, false)],
            services.TimeProvider.GetUtcNow());
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return ExitCodes.InvalidInputOrConfiguration;
    }

    private static async Task<int> ReadAsync(
        string? value,
        string? configPath,
        string? downloadImagesPath,
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
        bool downloadRequested = downloadImagesPath is not null;
        string resolvedOutputDirectory = string.Empty;
        AdoReference? reference = AdoReferenceParser.ParseWorkItem(value);
        if (reference is null)
        {
            return await WriteErrorAsync(
                "command",
                "invalidWorkItemId",
                "The work-item reference must be a positive integer or an Azure DevOps work-item URL.",
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                jsonOutput,
                timeProvider,
                cancellationToken,
                downloadRequested);
        }

        try
        {
            AdoReadConfiguration configuration = await configurationStore.LoadAsync(configPath, cancellationToken);
            if (!reference.IsFor(configuration.OrganizationUrl))
            {
                return await WriteErrorAsync(
                    "command",
                    "organizationMismatch",
                    "The work-item URL belongs to a different organization than the configured organization.",
                    ExitCodes.InvalidInputOrConfiguration,
                    compact,
                    jsonOutput,
                    timeProvider,
                    cancellationToken,
                    downloadRequested);
            }

            EffectivePrivacy privacy = privacyGate.Require(configuration.OrganizationUrl, privacyRequest);

            // Withheld images are never downloaded, so their output directory is neither checked nor created.
            bool mediaRequested = downloadRequested && privacy.Allows(PrivacyCategory.Images);
            if (mediaRequested && !WorkItemImageOutputDirectory.TryResolve(downloadImagesPath, out resolvedOutputDirectory))
            {
                return await WriteErrorAsync(
                    "command",
                    "invalidImageOutputDirectory",
                    "The image output directory must be nonexistent or an existing empty directory.",
                    ExitCodes.InvalidInputOrConfiguration,
                    compact,
                    jsonOutput,
                    timeProvider,
                    cancellationToken,
                    mediaRequested);
            }

            if (mediaRequested && !WorkItemImageOutputDirectory.TryCreate(resolvedOutputDirectory))
            {
                return await WriteErrorAsync(
                    "command",
                    "invalidImageOutputDirectory",
                    "The image output directory could not be created or written.",
                    ExitCodes.InvalidInputOrConfiguration,
                    compact,
                    jsonOutput,
                    timeProvider,
                    cancellationToken,
                    mediaRequested);
            }

            string pat = patProvider.GetRequiredPat(configuration.OrganizationUrl);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessionFactory.Create(configuration.OrganizationUrl, pat);
            var reader = new WorkItemReader(
                session.WorkItems,
                new WorkItemMapper(new RichTextNormalizer()),
                configuration.OrganizationUrl,
                timeProvider,
                privacy);
            SearchEnvelope<WorkItemDocument> envelope = await reader.ReadAsync(
                reference.Id,
                mediaRequested ? resolvedOutputDirectory : null,
                requestTimeout.Token);
            await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return HasInvalidImageOutputDirectoryError(envelope)
                ? ExitCodes.InvalidInputOrConfiguration
                : ExitCodes.FromStatus(envelope.Status);
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
                cancellationToken,
                downloadRequested);
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
                cancellationToken,
                downloadRequested);
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
                cancellationToken,
                downloadRequested);
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
                downloadRequested,
                transient: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await WriteErrorAsync(
                "command",
                "cancelled",
                "The command was cancelled.",
                ExitCodes.Cancelled,
                compact,
                jsonOutput,
                timeProvider,
                CancellationToken.None,
                downloadRequested,
                transient: true);
        }
    }

    private static bool HasInvalidImageOutputDirectoryError(SearchEnvelope<WorkItemDocument> envelope) =>
        envelope.Status == "error"
        && envelope.Errors.Any(error =>
            string.Equals(error.Code, "invalidImageOutputDirectory", StringComparison.Ordinal));

    private static async Task<int> WriteErrorAsync(
        string scope,
        string code,
        string message,
        int exitCode,
        bool compact,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        bool mediaRequested,
        bool transient = false)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            "workItemSearch",
            [new SearchError(scope, null, code, message, transient)],
            timeProvider.GetUtcNow()) with
        { SchemaVersion = mediaRequested ? "2.1" : "2.0" };
        await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }
}
