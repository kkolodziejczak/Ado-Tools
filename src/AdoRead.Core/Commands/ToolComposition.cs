using System.CommandLine;
using System.Globalization;
using AdoRead.AzureDevOps;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Terminal;

namespace AdoRead.Commands;

/// <summary>
/// Composes shared commands and feature modules into one command tree. The umbrella mounts each
/// module under its group name; a slice mounts its single module directly on the executable.
/// A write tool shares only <c>config</c> and <c>auth</c>, and the token, with the read tools: it returns no
/// comment, work-item or other Azure DevOps text, so it has no privacy policy or discovery commands.
/// </summary>
public sealed class ToolComposition(
    IConfigurationStore configurationStore,
    IJsonOutput jsonOutput,
    TimeProvider timeProvider,
    IPatProvider? patProvider = null,
    IAdoSessionFactory? sessionFactory = null,
    ICredentialVault? credentialVault = null,
    ITerminalInput? terminalInput = null,
    ToolKind toolKind = ToolKind.Read,
    ITokenOwnerLookup? tokenOwners = null)
{
    private readonly IAdoSessionFactory _sessionFactory = sessionFactory ?? new AdoSessionFactory();
    private readonly ICredentialVault _credentialVault = credentialVault ?? CredentialVault.ForCurrentPlatform();
    private readonly ITerminalInput _terminalInput = terminalInput ?? new ConsoleTerminalInput();
    private readonly ITokenOwnerLookup _tokenOwners = tokenOwners ?? new SdkTokenOwnerLookup();

    public CommandApplication CreateUmbrella(string executable, IReadOnlyList<CommandModule> modules)
    {
        RootCommand rootCommand = NewRootCommand(toolKind == ToolKind.Write
            ? "Post comments to Azure DevOps on your behalf and report them as structured JSON."
            : "Read Azure DevOps context as structured JSON.");
        Option<bool> compactOption = AddCompactOption(rootCommand);
        CommandServices services = CreateServices(compactOption, executable);
        var sharedNames = new ToolCommandNames(executable, null);
        Dictionary<Command, string> kinds = ConfigureShared(rootCommand, sharedNames, services);
        foreach (CommandModule module in modules)
        {
            var group = new Command(module.Name, module.Description);
            rootCommand.Subcommands.Add(group);
            Include(kinds, module.Configure(group, new ToolCommandNames(executable, module.Name), services));
        }

        return new CommandApplication(rootCommand, compactOption, kinds, jsonOutput, timeProvider, sharedNames.Invocation("auth login"));
    }

    public CommandApplication CreateSlice(string executable, CommandModule module)
    {
        RootCommand rootCommand = NewRootCommand(module.Description);
        Option<bool> compactOption = AddCompactOption(rootCommand);
        CommandServices services = CreateServices(compactOption, executable);
        var names = new ToolCommandNames(executable, null);
        Dictionary<Command, string> kinds = ConfigureShared(rootCommand, names, services);
        Include(kinds, module.Configure(rootCommand, names, services));

        return new CommandApplication(rootCommand, compactOption, kinds, jsonOutput, timeProvider, names.Invocation("auth login"));
    }

    // Every description is English, so System.CommandLine's own texts must be too, whatever the
    // machine's language. It translates option descriptions such as --version's when the root
    // command is built, and help headings when help is shown, so the culture is set before both.
    private static RootCommand NewRootCommand(string description)
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        return new RootCommand(description);
    }

    private static Option<bool> AddCompactOption(RootCommand rootCommand)
    {
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Emit compact JSON.",
            Recursive = true
        };
        rootCommand.Options.Add(compactOption);
        return compactOption;
    }

    private CommandServices CreateServices(Option<bool> compactOption, string executable)
    {
        var privacyPolicies = new PrivacyPolicyStore(_credentialVault);
        var names = new ToolCommandNames(executable, null);
        return new(
            configurationStore,
            patProvider ?? new VaultPatProvider(_credentialVault, names.Invocation("auth login")),
            _sessionFactory,
            jsonOutput,
            timeProvider,
            compactOption,
            _credentialVault,
            _terminalInput,
            new PrivacyGate(privacyPolicies, names),
            privacyPolicies)
        {
            ToolKind = toolKind,
            TokenOwners = _tokenOwners
        };
    }

    private static Dictionary<Command, string> ConfigureShared(
        RootCommand rootCommand,
        ToolCommandNames names,
        CommandServices services)
    {
        var kinds = new Dictionary<Command, string>();
        Include(kinds, ConfigCommands.Configure(rootCommand, names, services));
        Include(kinds, AuthCommands.Configure(rootCommand, names, services));
        if (services.ToolKind == ToolKind.Write)
        {
            return kinds;
        }

        Include(kinds, PrivacyCommands.Configure(rootCommand, services));
        Include(kinds, DiscoveryCommands.Configure(rootCommand, names, services));
        return kinds;
    }

    private static void Include(Dictionary<Command, string> kinds, IReadOnlyDictionary<Command, string> added)
    {
        foreach ((Command command, string kind) in added)
        {
            kinds[command] = kind;
        }
    }
}
