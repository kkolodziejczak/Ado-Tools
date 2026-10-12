using System.CommandLine;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.Commands.Shared;

public static class ConfigCommands
{
    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        var configCommand = new Command("config", "Manage non-secret configuration.");
        configCommand.Subcommands.Add(CreateInitCommand(names, services));
        configCommand.Subcommands.Add(CreateShowCommand(services));
        target.Subcommands.Add(configCommand);
        return new Dictionary<Command, string> { [configCommand] = "configuration" };
    }

    private static Command CreateInitCommand(ToolCommandNames names, CommandServices services)
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
            Description = $"Default project for the sprint and wiki commands. Discover one with '{names.Invocation("projects")}'."
        };
        var teamOption = new Option<string?>("--team")
        {
            Description = $"Default team for the sprint commands. Discover one with '{names.Invocation("teams")}'."
        };
        var command = new Command("init", "Create configuration.");
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
            parseResult.GetValue(services.CompactOption),
            services,
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
        CommandServices services,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoReadConfiguration configuration = CreateConfiguration(organization, project, team);
            await services.ConfigurationStore.SaveAsync(configPath, configuration, force, cancellationToken);
            await WriteSuccessAsync(configuration, compact, services, cancellationToken);
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
            await WriteErrorAsync(error, compact, services, cancellationToken);
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

    private static Command CreateShowCommand(CommandServices services)
    {
        var configOption = CreateConfigOption();
        var command = new Command("show", "Show configuration without credentials.");
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await ShowAsync(
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            cancellationToken));
        return command;
    }

    private static async Task<int> ShowAsync(
        string? configPath,
        bool compact,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoReadConfiguration configuration = await services.ConfigurationStore.LoadAsync(configPath, cancellationToken);
            await WriteSuccessAsync(configuration, compact, services, cancellationToken);
            return ExitCodes.Success;
        }
        catch (ConfigurationException exception)
        {
            await WriteErrorAsync(SearchError.FromException(exception), compact, services, cancellationToken);
            return ExitCodes.InvalidInputOrConfiguration;
        }
    }

    private static async Task WriteSuccessAsync(
        AdoReadConfiguration configuration,
        bool compact,
        CommandServices services,
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
            services.TimeProvider.GetUtcNow());
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
    }

    private static async Task WriteErrorAsync(
        SearchError error,
        bool compact,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            "configuration",
            [error],
            services.TimeProvider.GetUtcNow());
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
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
