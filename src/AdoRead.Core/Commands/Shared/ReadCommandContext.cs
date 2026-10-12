using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.Commands.Shared;

/// <summary>
/// Shared plumbing for the Azure DevOps read commands: load configuration, read the PAT, apply the
/// whole-command timeout, open one session, and map every expected failure onto a single JSON
/// envelope and exit code. Hints name commands through the tool's root names.
/// </summary>
internal sealed class ReadCommandContext(CommandServices services, ToolCommandNames names)
{
    public async Task<int> RunAsync<T>(
        string kind,
        string? configPath,
        bool compact,
        Func<IAdoSession, AdoReadConfiguration, CancellationToken, Task<SearchEnvelope<T>>> readAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoReadConfiguration configuration = await services.ConfigurationStore.LoadAsync(configPath, cancellationToken);
            string pat = services.PatProvider.GetRequiredPat(configuration.OrganizationUrl);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = services.SessionFactory.Create(configuration.OrganizationUrl, pat);
            SearchEnvelope<T> envelope = await readAsync(session, configuration, requestTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return ExitCodes.FromStatus(envelope.Status);
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
                new SearchError("azureDevOps", null, "timeout", "The Azure DevOps request timed out.", true),
                ExitCodes.Error,
                compact,
                CancellationToken.None);
        }
    }

    public string RequireProject(string? value, AdoReadConfiguration configuration) =>
        Require(
            value ?? configuration.DefaultProject,
            "projectRequired",
            $"No project given and none configured. Run '{names.Invocation("projects")}' to list them, then pass "
            + $"--project or store one with '{names.Invocation("config init --project <name> --force")}'.");

    public string RequireTeam(string? value, AdoReadConfiguration configuration) =>
        Require(
            value ?? configuration.DefaultTeam,
            "teamRequired",
            $"No team given and none configured. Run '{names.Invocation("teams")}' to list them, then pass "
            + $"--team or store one with '{names.Invocation("config init --team <name> --force")}'.");

    private static string Require(string? value, string code, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ConfigurationException(code, message) : value.Trim();

    private async Task<int> WriteErrorAsync(
        string kind,
        SearchError error,
        int exitCode,
        bool compact,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            kind,
            [error],
            services.TimeProvider.GetUtcNow());
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }

    public static Option<string?> CreateConfigOption() => new("--config")
    {
        Description = "Override the default ~/.ado-read/config.json path."
    };

    public static Option<string?> CreateProjectOption() => new("--project")
    {
        Description = "Project name or ID. Defaults to the configured project."
    };

    public static Option<string?> CreateTeamOption() => new("--team")
    {
        Description = "Team name or ID. Defaults to the configured team."
    };
}
