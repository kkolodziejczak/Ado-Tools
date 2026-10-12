using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;

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
    Option<bool> CompactOption);
