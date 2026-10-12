using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Text;
using AdoRead.WorkItems;

namespace AdoRead.Commands;

/// <summary>Work-item lookup with comments and optional image download: <c>work-item &lt;id-or-url&gt;</c>.</summary>
public static class WorkItemModule
{
    public static CommandModule Definition { get; } = new(
        "work-item",
        "Read one Azure DevOps work item and all of its comments.",
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
            Description = "Download supported work-item images to an empty directory."
        };
        target.Arguments.Add(referenceArgument);
        target.Options.Add(configOption);
        target.Options.Add(downloadImagesOption);
        target.SetAction(async (parseResult, cancellationToken) => await ReadAsync(
            parseResult.GetValue(referenceArgument),
            parseResult.GetValue(configOption),
            parseResult.GetValue(downloadImagesOption),
            parseResult.GetValue(services.CompactOption),
            services.ConfigurationStore,
            services.PatProvider,
            services.SessionFactory,
            services.JsonOutput,
            services.TimeProvider,
            cancellationToken));
        return new Dictionary<Command, string> { [target] = "workItemSearch" };
    }

    private static async Task<int> ReadAsync(
        string? value,
        string? configPath,
        string? downloadImagesPath,
        bool compact,
        IConfigurationStore configurationStore,
        IPatProvider patProvider,
        IAdoSessionFactory sessionFactory,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        bool mediaRequested = downloadImagesPath is not null;
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
                mediaRequested);
        }

        if (mediaRequested
            && !WorkItemImageOutputDirectory.TryResolve(downloadImagesPath, out resolvedOutputDirectory))
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

            string pat = patProvider.GetRequiredPat();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessionFactory.Create(configuration.OrganizationUrl, pat);
            var reader = new WorkItemReader(
                session.WorkItems,
                new WorkItemMapper(new RichTextNormalizer()),
                configuration.OrganizationUrl,
                timeProvider);
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
                mediaRequested);
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
                mediaRequested);
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
                mediaRequested,
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
                mediaRequested,
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
