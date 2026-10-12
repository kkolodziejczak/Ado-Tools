using System.CommandLine;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;

namespace AdoRead.Commands.Shared;

/// <summary>
/// Sets, shows and resets the content policy of one organization. Tightening is open to agents;
/// loosening needs an interactive terminal and a typed confirmation, so only a person can grant more.
/// </summary>
public static class PrivacyCommands
{
    public static IReadOnlyDictionary<Command, string> Configure(Command target, CommandServices services)
    {
        var privacyCommand = new Command(
            "privacy",
            "Show, set or reset which content categories AI agents may receive, per organization. Only a person can loosen the policy.");
        Command show = CreateShowCommand(services);
        Command set = CreateSetCommand(services);
        Command reset = CreateResetCommand(services);
        privacyCommand.Subcommands.Add(show);
        privacyCommand.Subcommands.Add(set);
        privacyCommand.Subcommands.Add(reset);
        target.Subcommands.Add(privacyCommand);
        return new Dictionary<Command, string>
        {
            [show] = "privacyShow",
            [set] = "privacySet",
            [reset] = "privacyReset"
        };
    }

    private static Command CreateShowCommand(CommandServices services)
    {
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command("show", "Show the privacy level and the effective allow or deny of each content category.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            "privacyShow",
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            organizationUrl => Task.FromResult(Show(organizationUrl, services)),
            cancellationToken));
        return command;
    }

    private static Command CreateSetCommand(CommandServices services)
    {
        var levelArgument = new Argument<string>("level")
        {
            Description = "minimal (metadata only), standard (metadata, text and comments; people aliased) or full (everything). Case-insensitive."
        };
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var allowOption = new Option<string[]>("--allow")
        {
            Description = "Allow one category on top of the level: text, comments, people, customFields or images. Repeatable.",
            AllowMultipleArgumentsPerToken = true
        };
        var denyOption = new Option<string[]>("--deny")
        {
            Description = "Deny one category on top of the level: text, comments, people, customFields or images. Repeatable.",
            AllowMultipleArgumentsPerToken = true
        };
        var command = new Command(
            "set",
            "Set the privacy policy of an organization. Loosening needs an interactive terminal and a typed confirmation; tightening does not.");
        command.Arguments.Add(levelArgument);
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.Options.Add(allowOption);
        command.Options.Add(denyOption);
        command.SetAction(async (parseResult, cancellationToken) => await SetAsync(
            parseResult.GetValue(levelArgument) ?? string.Empty,
            parseResult.GetValue(allowOption) ?? [],
            parseResult.GetValue(denyOption) ?? [],
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            cancellationToken));
        return command;
    }

    private static Command CreateResetCommand(CommandServices services)
    {
        Option<string?> organizationOption = CreateOrganizationOption();
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command(
            "reset",
            "Remove the privacy policy of an organization. Afterwards no content is returned until a person runs privacy set.");
        command.Options.Add(organizationOption);
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            "privacyReset",
            parseResult.GetValue(organizationOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            services,
            organizationUrl => Task.FromResult(Reset(organizationUrl, services)),
            cancellationToken));
        return command;
    }

    private static async Task<int> SetAsync(
        string level,
        string[] allowed,
        string[] denied,
        string? organization,
        string? configPath,
        bool compact,
        CommandServices services,
        CancellationToken cancellationToken)
    {
        PrivacyPolicy requested;
        try
        {
            requested = PrivacyPolicy.FromNames(level, allowed, denied);
        }
        catch (PrivacyException exception)
        {
            return await WriteErrorAsync(
                "privacySet",
                new SearchError("command", null, exception.Code, exception.Message, false),
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                services,
                cancellationToken);
        }

        return await RunAsync(
            "privacySet",
            organization,
            configPath,
            compact,
            services,
            organizationUrl => Task.FromResult(Set(organizationUrl, requested, services)),
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
        catch (PrivacyException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("privacy", null, exception.Code, exception.Message, false),
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

    private static PrivacyStateView Show(Uri organizationUrl, CommandServices services) =>
        View(services.PrivacyPolicies.Find(organizationUrl)?.Policy, services.PrivacyPolicies.VaultKind);

    private static PrivacyStateView Set(Uri organizationUrl, PrivacyPolicy requested, CommandServices services)
    {
        StoredPrivacyPolicy? stored = FindUsablePolicy(organizationUrl, services);
        if (stored is null || requested.IsLooserThan(stored.Policy))
        {
            ConfirmLoosening(organizationUrl, requested, stored?.Policy, services);
        }

        StoredPrivacyPolicy next = stored is null
            ? StoredPrivacyPolicy.CreateNew(requested)
            : stored with { Policy = requested };
        services.PrivacyPolicies.Save(organizationUrl, next);
        return View(next.Policy, services.PrivacyPolicies.VaultKind);
    }

    /// <summary>
    /// A person must confirm any change that grants more than the stored policy, or any first policy,
    /// by typing the organization name. Nothing is stored unless the name matches.
    /// </summary>
    private static void ConfirmLoosening(Uri organizationUrl, PrivacyPolicy requested, PrivacyPolicy? current, CommandServices services)
    {
        if (services.TerminalInput.IsInputRedirected)
        {
            throw new PrivacyException(
                "interactiveTerminalRequired",
                "Loosening the privacy policy, or setting it for the first time, needs an interactive terminal. Nothing was changed.");
        }

        string[] granted = requested.AllowedCategories
            .Where(category => current is null || !current.Allows(category))
            .Select(PrivacyCategories.NameOf)
            .ToArray();
        string organizationName = OrganizationName(organizationUrl);
        string grants = granted.Length == 0 ? "no content category" : string.Join(", ", granted);
        string answer = services.TerminalInput.ReadLine(
            $"This policy newly grants {grants} to AI agents for {organizationUrl}.\n"
            + $"Type the organization name '{organizationName}' to confirm: ");
        if (!string.Equals(answer.Trim(), organizationName, StringComparison.OrdinalIgnoreCase))
        {
            throw new PrivacyException(
                "confirmationFailed",
                "The organization name did not match, so the privacy policy was not changed.");
        }
    }

    private static PrivacyRemovalView Reset(Uri organizationUrl, CommandServices services) =>
        new(services.PrivacyPolicies.VaultKind, PolicyRemoved: services.PrivacyPolicies.Remove(organizationUrl));

    /// <summary>An unreadable stored policy counts as none, so setting a policy replaces it.</summary>
    private static StoredPrivacyPolicy? FindUsablePolicy(Uri organizationUrl, CommandServices services)
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

    private static PrivacyStateView View(PrivacyPolicy? policy, string vault) => policy is null
        ? new PrivacyStateView(vault, Configured: false, Level: null, Categories: CategoriesOf(allowed: _ => false))
        : new PrivacyStateView(vault, Configured: true, Level: PrivacyLevels.NameOf(policy.Level), Categories: CategoriesOf(policy.Allows));

    private static IReadOnlyList<CategoryView> CategoriesOf(Func<PrivacyCategory, bool> allowed) =>
        PrivacyCategories.All
            .Select(category => new CategoryView(PrivacyCategories.NameOf(category), allowed(category)))
            .ToArray();

    private static string OrganizationName(Uri organizationUrl) =>
        Uri.UnescapeDataString(organizationUrl.AbsolutePath.Trim('/'));

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

    private sealed record PrivacyStateView(
        string Vault,
        bool Configured,
        string? Level,
        IReadOnlyList<CategoryView> Categories);

    private sealed record CategoryView(string Category, bool Allowed);

    private sealed record PrivacyRemovalView(string Vault, bool PolicyRemoved);
}
