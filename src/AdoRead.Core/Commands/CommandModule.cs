using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Terminal;

namespace AdoRead.Commands;

/// <summary>
/// Adds one feature's commands to <paramref name="target"/>: its arguments, options and action when
/// the target is itself the command, or its subcommands otherwise. Returns the envelope kind of each
/// command it added so the application can label errors.
/// </summary>
public delegate IReadOnlyDictionary<Command, string> ConfigureModule(
    Command target,
    ToolCommandNames names,
    CommandServices services);

/// <summary>A feature that a tool composes. <see cref="Name"/> is its group name in the umbrella tool.</summary>
public sealed record CommandModule(string Name, string Description, ConfigureModule Configure);

public sealed record CommandServices(
    IConfigurationStore ConfigurationStore,
    IPatProvider PatProvider,
    IAdoSessionFactory SessionFactory,
    IJsonOutput JsonOutput,
    TimeProvider TimeProvider,
    Option<bool> CompactOption,
    ICredentialVault CredentialVault,
    ITerminalInput TerminalInput,
    PrivacyGate Privacy,
    PrivacyPolicyStore PrivacyPolicies)
{
    /// <summary>Selects the vault token: read tools and write tools keep separate tokens.</summary>
    public TokenScope TokenScope { get; init; } = TokenScope.Read;

    public string TokenAccount(Uri organizationUrl) => CredentialAccount.ForToken(organizationUrl, TokenScope);
}
