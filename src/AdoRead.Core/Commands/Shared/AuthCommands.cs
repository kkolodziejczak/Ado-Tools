using System.CommandLine;
using System.Text.Json.Serialization;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;

namespace AdoRead.Commands.Shared;

/// <summary>
/// Stores, checks and removes the personal access token of one organization in the OS credential
/// vault. No command accepts a token value: login reads it from a masked terminal prompt. Read and write
/// tools share the token. Login and status ask Azure DevOps whose token it is, so login stores only a
/// token Azure DevOps accepts, and status finds one that has expired. Login in a read tool also asks a
/// person for the privacy level when the organization has none yet; a write tool asks for none, because
/// it returns no Azure DevOps content.
/// </summary>
public static class AuthCommands
{
    private const string PrivacyLevelPrompt =
        "Choose what AI agents may receive from this organization:\n"
        + "  1 minimal   metadata only: ids, titles, states, paths, dates, counts and links\n"
        + "  2 standard  metadata plus text, comments, test messages and stack traces; people are aliased\n"
        + "  3 full      everything, including custom fields, images, people and build logs\n"
        + "Privacy level 1, 2 or 3 (empty keeps content withheld): ";

    public static IReadOnlyDictionary<Command, string> Configure(Command target, ToolCommandNames names, CommandServices services)
    {
        var authCommand = new Command("auth", "Store, check or remove the token of an organization in the OS credential vault.");
        Command login = CreateLoginCommand(names, services);
        Command status = CreateStatusCommand(names, services);
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

    private static Command CreateLoginCommand(ToolCommandNames names, CommandServices services)
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
            names,
            services,
            cancellationToken));
        return command;
    }

    private static Command CreateStatusCommand(ToolCommandNames names, CommandServices services)
    {
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command("status", "Report whether a token is stored for an organization, in which vault, and whose it is.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            "authStatus",
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            organizationUrl => ReadTokenStateAsync(organizationUrl, names, services, cancellationToken),
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
        ToolCommandNames names,
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
            organizationUrl => LoginAsync(organizationUrl, names, services, cancellationToken),
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
            Uri organizationUrl = await OrganizationSelection.ResolveAsync(organization, configPath, services.ConfigurationStore, cancellationToken);
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
        catch (TokenOwnerException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("azureDevOps", null, exception.Code, exception.Message, exception.Transient),
                ExitCodes.Error,
                compact,
                services,
                cancellationToken);
        }
    }

    private static async Task<LoginView> LoginAsync(
        Uri organizationUrl,
        ToolCommandNames names,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        string account = CredentialAccount.For(organizationUrl);
        string token = services.TerminalInput.ReadMaskedLine($"Personal access token for {account}: ").Trim();
        if (token.Length == 0)
        {
            throw new ConfigurationException("tokenRequired", "No token was entered, so nothing was stored.");
        }

        string actingAs;
        try
        {
            actingAs = await services.TokenOwners.FindDisplayNameAsync(organizationUrl, token, cancellationToken);
        }
        catch (TokenOwnerException exception)
        {
            throw new TokenOwnerException(exception.Code, $"{Explain(exception)} Nothing was stored.", exception.Transient, exception);
        }

        string? stored = services.CredentialVault.Read(account);
        bool? replacedStoredToken = !string.IsNullOrEmpty(stored) && stored != token ? true : null;
        services.CredentialVault.Write(account, token);
        bool? legacyWriteTokenRemoved = RemoveLegacyWriteToken(organizationUrl, services);
        if (services.ToolKind == ToolKind.Write)
        {
            return new LoginView(services.CredentialVault.Kind, TokenStored: true, actingAs, PrivacyConfigured: null, Notice: null, replacedStoredToken, legacyWriteTokenRemoved);
        }

        bool privacyConfigured;
        try
        {
            privacyConfigured = EnsurePrivacyLevel(organizationUrl, services);
        }
        catch (CredentialException exception)
        {
            throw new CredentialException(
                exception.Code,
                $"The token was stored and every Ado-Tools tool now uses it, but the privacy level could not be checked or saved. {exception.Message}");
        }

        return new LoginView(
            services.CredentialVault.Kind,
            TokenStored: true,
            actingAs,
            PrivacyConfigured: privacyConfigured,
            Notice: privacyConfigured
                ? null
                : $"No privacy level was set, so content stays withheld until '{names.Invocation("privacy set <minimal|standard|full>")}' is run in an interactive terminal.",
            replacedStoredToken,
            legacyWriteTokenRemoved);
    }

    /// <summary>True when a token from the separate write account of earlier prereleases was removed, otherwise null.</summary>
    private static bool? RemoveLegacyWriteToken(Uri organizationUrl, CommandServices services) =>
        services.CredentialVault.Remove(CredentialAccount.ForLegacyWriteToken(organizationUrl)) ? true : null;

    /// <summary>Asks for a level when the organization has none. An empty or invalid answer stores nothing.</summary>
    private static bool EnsurePrivacyLevel(Uri organizationUrl, CommandServices services)
    {
        if (FindPolicy(organizationUrl, services) is not null)
        {
            return true;
        }

        PrivacyLevel? level = ParseLevelChoice(services.TerminalInput.ReadLine(PrivacyLevelPrompt));
        if (level is null)
        {
            return false;
        }

        services.PrivacyPolicies.Save(organizationUrl, StoredPrivacyPolicy.CreateNew(new PrivacyPolicy(level.Value)));
        return true;
    }

    /// <summary>An unreadable stored policy counts as none, so the person is asked again and the new answer replaces it.</summary>
    private static StoredPrivacyPolicy? FindPolicy(Uri organizationUrl, CommandServices services)
    {
        try
        {
            return services.PrivacyPolicies.Find(organizationUrl);
        }
        catch (PrivacyException)
        {
            return null;
        }
    }

    private static PrivacyLevel? ParseLevelChoice(string answer) => answer.Trim() switch
    {
        "1" => PrivacyLevel.Minimal,
        "2" => PrivacyLevel.Standard,
        "3" => PrivacyLevel.Full,
        _ => null
    };

    /// <summary>A failure keeps its code; its message says that a token is stored and, when it was rejected, how to replace it.</summary>
    private static async Task<TokenStateView> ReadTokenStateAsync(
        Uri organizationUrl,
        ToolCommandNames names,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        string? token = services.CredentialVault.Read(CredentialAccount.For(organizationUrl));
        if (string.IsNullOrEmpty(token))
        {
            return new TokenStateView(services.CredentialVault.Kind, TokenStored: false, ActingAs: null);
        }

        try
        {
            string actingAs = await services.TokenOwners.FindDisplayNameAsync(organizationUrl, token, cancellationToken);
            return new TokenStateView(services.CredentialVault.Kind, TokenStored: true, actingAs);
        }
        catch (TokenOwnerException exception)
        {
            string message = exception.Code == "unauthorized"
                ? $"A token is stored, but {Explain(exception)} Store a new one with `{names.Invocation("auth login")}`."
                : $"A token is stored, but it could not be checked: {exception.Message}";
            throw new TokenOwnerException(exception.Code, message, exception.Transient, exception);
        }
    }

    private static string Explain(TokenOwnerException exception) =>
        exception.Code == "unauthorized"
            ? "Azure DevOps rejected the token: it may be mistyped, expired or revoked, or made for another organization."
            : exception.Message;

    private static TokenRemovalView RemoveToken(Uri organizationUrl, CommandServices services) =>
        new(
            services.CredentialVault.Kind,
            TokenRemoved: services.CredentialVault.Remove(CredentialAccount.For(organizationUrl)),
            RemoveLegacyWriteToken(organizationUrl, services));

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

    private sealed record TokenStateView(
        string Vault,
        bool TokenStored,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActingAs);

    private sealed record LoginView(
        string Vault,
        bool TokenStored,
        string ActingAs,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PrivacyConfigured,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notice,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ReplacedStoredToken,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? LegacyWriteTokenRemoved);

    private sealed record TokenRemovalView(
        string Vault,
        bool TokenRemoved,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? LegacyWriteTokenRemoved);
}
