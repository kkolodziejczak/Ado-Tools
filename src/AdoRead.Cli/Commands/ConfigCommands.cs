using System.CommandLine;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.Commands;

public static class ConfigCommands
{
    public static Command AddTo(
        RootCommand rootCommand,
        Option<bool> compactOption,
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider)
    {
        var configCommand = new Command("config", "Manage non-secret ado-read configuration.");
        configCommand.Subcommands.Add(CreateInitCommand(compactOption, configurationStore, jsonOutput, timeProvider));
        configCommand.Subcommands.Add(CreateShowCommand(compactOption, configurationStore, jsonOutput, timeProvider));
        rootCommand.Subcommands.Add(configCommand);
        return configCommand;
    }

    private static Command CreateInitCommand(
        Option<bool> compactOption,
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider)
    {
        var organizationOption = new Option<string>("--organization")
        {
            Description = "Azure DevOps organization URL.",
            Required = true
        };
        var configOption = CreateConfigOption();
        var forceOption = new Option<bool>("--force")
        {
            Description = "Replace an existing configuration file."
        };
        var projectOption = new Option<string?>("--project")
        {
            Description = "Default project for the sprint commands. Discover one with 'ado-read projects'."
        };
        var teamOption = new Option<string?>("--team")
        {
            Description = "Default team for the sprint commands. Discover one with 'ado-read teams'."
        };
        var command = new Command("init", "Create ado-read configuration.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.Options.Add(forceOption);
        command.Options.Add(projectOption);
        command.Options.Add(teamOption);
        command.SetAction(async (parseResult, cancellationToken) => await InitializeAsync(
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(forceOption),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(teamOption),
            parseResult.GetValue(compactOption),
            configurationStore,
            jsonOutput,
            timeProvider,
            cancellationToken));
        return command;
    }

    private static async Task<int> InitializeAsync(
        string? organization,
        string? configPath,
        bool force,
        string? project,
        string? team,
        bool compact,
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoReadConfiguration configuration = CreateConfiguration(organization, project, team);
            await configurationStore.SaveAsync(configPath, configuration, force, cancellationToken);
            await WriteSuccessAsync(configuration, compact, jsonOutput, timeProvider, cancellationToken);
            return ExitCodes.Success;
        }
        catch (Exception exception) when (exception is ConfigurationException or UriFormatException)
        {
            SearchError error = exception is UriFormatException
                ? new SearchError(
                    "configuration",
                    null,
                    "invalidOrganizationUrl",
                    "The organization URL must use the form https://dev.azure.com/{organization}.",
                    false)
                : SearchError.FromException(exception);
            await WriteErrorAsync(error, compact, jsonOutput, timeProvider, cancellationToken);
            return ExitCodes.InvalidInputOrConfiguration;
        }
    }

    private static AdoReadConfiguration CreateConfiguration(string? organization, string? project, string? team)
    {
        if (string.IsNullOrWhiteSpace(organization)
            || !Uri.TryCreate(organization, UriKind.Absolute, out Uri? organizationUrl))
        {
            throw new ConfigurationException(
                "invalidOrganizationUrl",
                "The organization URL must use the form https://dev.azure.com/{organization}.");
        }

        return AdoReadConfiguration.Create(
            organizationUrl,
            AdoReadConfiguration.DefaultRequestTimeoutSeconds,
            AdoReadConfiguration.DefaultMaxParallelProjects,
            project,
            team);
    }

    private static Command CreateShowCommand(
        Option<bool> compactOption,
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider)
    {
        var configOption = CreateConfigOption();
        var command = new Command("show", "Show ado-read configuration without credentials.");
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await ShowAsync(
            parseResult.GetValue(configOption),
            parseResult.GetValue(compactOption),
            configurationStore,
            jsonOutput,
            timeProvider,
            cancellationToken));
        return command;
    }

    private static async Task<int> ShowAsync(
        string? configPath,
        bool compact,
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoReadConfiguration configuration = await configurationStore.LoadAsync(configPath, cancellationToken);
            await WriteSuccessAsync(configuration, compact, jsonOutput, timeProvider, cancellationToken);
            return ExitCodes.Success;
        }
        catch (ConfigurationException exception)
        {
            await WriteErrorAsync(
                SearchError.FromException(exception),
                compact,
                jsonOutput,
                timeProvider,
                cancellationToken);
            return ExitCodes.InvalidInputOrConfiguration;
        }
    }

    private static async Task WriteSuccessAsync(
        AdoReadConfiguration configuration,
        bool compact,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var view = new ConfigurationView(
            configuration.SchemaVersion,
            configuration.OrganizationUrl,
            checked((int)configuration.RequestTimeout.TotalSeconds),
            configuration.MaxParallelProjects,
            configuration.DefaultProject,
            configuration.DefaultTeam);
        SearchEnvelope<ConfigurationView> envelope = SearchEnvelopeFactory.Success(
            "configuration",
            [view],
            timeProvider.GetUtcNow());
        await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
    }

    private static async Task WriteErrorAsync(
        SearchError error,
        bool compact,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            "configuration",
            [error],
            timeProvider.GetUtcNow());
        await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
    }

    private static Option<string?> CreateConfigOption() => new("--config")
    {
        Description = "Override the default ~/.ado-read/config.json path."
    };

    private sealed record ConfigurationView(
        int SchemaVersion,
        Uri OrganizationUrl,
        int RequestTimeoutSeconds,
        int MaxParallelProjects,
        string? DefaultProject,
        string? DefaultTeam);
}
