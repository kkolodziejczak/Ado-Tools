using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.PullRequests;

namespace AdoRead.Commands;

/// <summary>Pull-request lookup across every accessible project: <c>pr &lt;id-or-url&gt;</c>.</summary>
public static class PullRequestModule
{
    public static CommandModule Definition { get; } = new(
        "pr",
        "Find a pull request across all accessible projects.",
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
        target.Arguments.Add(referenceArgument);
        target.Options.Add(configOption);
        target.SetAction(async (parseResult, cancellationToken) => await ReadAsync(
            parseResult.GetValue(referenceArgument),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services.ConfigurationStore,
            services.PatProvider,
            services.SessionFactory,
            services.JsonOutput,
            services.TimeProvider,
            cancellationToken));
        return new Dictionary<Command, string> { [target] = "pullRequestSearch" };
    }

    private static async Task<int> ReadAsync(
        string? value,
        string? configPath,
        bool compact,
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

            string pat = patProvider.GetRequiredPat(configuration.OrganizationUrl);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessionFactory.Create(configuration.OrganizationUrl, pat);
            var reader = new PullRequestReader(
                session.Projects,
                session.PullRequests,
                configuration.OrganizationUrl,
                configuration.MaxParallelProjects,
                timeProvider);
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
