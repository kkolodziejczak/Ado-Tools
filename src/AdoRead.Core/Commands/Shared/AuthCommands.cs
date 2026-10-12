using System.CommandLine;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;

namespace AdoRead.Commands.Shared;

/// <summary>
/// Stores, checks and removes the personal access token of one organization in the OS credential
/// vault. No command accepts a token value: login reads it from a masked terminal prompt.
/// </summary>
public static class AuthCommands
{
    public static IReadOnlyDictionary<Command, string> Configure(Command target, CommandServices services)
    {
        var authCommand = new Command("auth", "Store, check or remove the token of an organization in the OS credential vault.");
        Command login = CreateLoginCommand(services);
        Command status = CreateStatusCommand(services);
        Command logout = CreateLogoutCommand(services);
        authCommand.Subcommands.Add(login);
        authCommand.Subcommands.Add(status);
        authCommand.Subcommands.Add(logout);
        target.Subcommands.Add(authCommand);
        return new Dictionary<Command, string>
        {
            [login] = "authLogin",
            [status] = "authStatus",
            [logout] = "authLogout"
        };
    }

    private static Command CreateLoginCommand(CommandServices services)
    {
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command(
            "login",
            "Store a personal access token for an organization. The token is typed at a masked prompt, never passed as an argument.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await LoginAsync(
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            cancellationToken));
        return command;
    }

    private static Command CreateStatusCommand(CommandServices services)
    {
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command("status", "Report whether a token is stored for an organization, and in which vault.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            "authStatus",
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            organizationUrl => Task.FromResult(ReadTokenState(organizationUrl, services)),
            cancellationToken));
        return command;
    }

    private static Command CreateLogoutCommand(CommandServices services)
    {
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command("logout", "Remove the token stored for an organization from the OS credential vault.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            "authLogout",
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            organizationUrl => Task.FromResult(RemoveToken(organizationUrl, services)),
            cancellationToken));
        return command;
    }

    private static async Task<int> LoginAsync(
        string? organization,
        string? configPath,
        bool compact,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        if (services.TerminalInput.IsInputRedirected)
        {
            return await WriteErrorAsync(
                "authLogin",
                new SearchError(
                    "command",
                    null,
                    "interactiveTerminalRequired",
                    "auth login reads the token from an interactive terminal. Run it in a terminal; a token is never accepted as an argument, a file or an environment variable.",
                    false),
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                services,
                cancellationToken);
        }

        return await RunAsync(
            "authLogin",
            organization,
            configPath,
            compact,
            services,
            organizationUrl => Task.FromResult(StoreToken(organizationUrl, services)),
            cancellationToken);
    }

    private static async Task<int> RunAsync<TView>(
        string kind,
        string? organization,
        string? configPath,
        bool compact,
        CommandServices services,
        Func<Uri, Task<TView>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            Uri organizationUrl = await ResolveOrganizationAsync(organization, configPath, services.ConfigurationStore, cancellationToken);
            TView view = await operation(organizationUrl);
            var envelope = new SearchEnvelope<TView>(
                "1.0",
                kind,
                "success",
                new OrganizationQuery(organizationUrl),
                services.TimeProvider.GetUtcNow(),
                [view],
                []);
            await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return ExitCodes.Success;
        }
        catch (ConfigurationException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("configuration", null, exception.Code, exception.Message, false),
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                services,
                cancellationToken);
        }
        catch (CredentialException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("authentication", null, exception.Code, exception.Message, false),
                ExitCodes.Error,
                compact,
                services,
                cancellationToken);
        }
    }

    private static TokenStateView StoreToken(Uri organizationUrl, CommandServices services)
    {
        string account = CredentialAccount.For(organizationUrl);
        string token = services.TerminalInput.ReadMaskedLine($"Personal access token for {account}: ").Trim();
        if (token.Length == 0)
        {
            throw new ConfigurationException("tokenRequired", "No token was entered, so nothing was stored.");
        }

        services.CredentialVault.Write(account, token);
        return new TokenStateView(services.CredentialVault.Kind, TokenStored: true);
    }

    private static TokenStateView ReadTokenState(Uri organizationUrl, CommandServices services)
    {
        string? token = services.CredentialVault.Read(CredentialAccount.For(organizationUrl));
        return new TokenStateView(services.CredentialVault.Kind, TokenStored: !string.IsNullOrEmpty(token));
    }

    private static TokenRemovalView RemoveToken(Uri organizationUrl, CommandServices services) =>
        new(services.CredentialVault.Kind, TokenRemoved: services.CredentialVault.Remove(CredentialAccount.For(organizationUrl)));

    private static async Task<Uri> ResolveOrganizationAsync(
        string? organization,
        string? configPath,
        IConfigurationStore configurationStore,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(organization))
        {
            if (!Uri.TryCreate(organization.Trim(), UriKind.Absolute, out Uri? organizationUrl))
            {
                throw new ConfigurationException(
                    "invalidOrganizationUrl",
                    "The organization URL must use the form https://dev.azure.com/{organization}.");
            }

            return AdoReadConfiguration.Create(organizationUrl).OrganizationUrl;
        }

        AdoReadConfiguration configuration = await configurationStore.LoadAsync(configPath, cancellationToken);
        return configuration.OrganizationUrl;
    }

    private static async Task<int> WriteErrorAsync(
        string kind,
        SearchError error,
        int exitCode,
        bool compact,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(kind, [error], services.TimeProvider.GetUtcNow());
        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }

    private static Option<string?> CreateOrganizationOption() => new("--organization")
    {
        Description = "Azure DevOps organization URL. Defaults to the organization in the configuration."
    };

    private sealed record OrganizationQuery(Uri OrganizationUrl);

    private sealed record TokenStateView(string Vault, bool TokenStored);

    private sealed record TokenRemovalView(string Vault, bool TokenRemoved);
}
