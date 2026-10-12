using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Sprints;

namespace AdoRead.Commands;

/// <summary>
/// Sprint reading. Sprints are team scoped, so a caller who does not yet know the project or team
/// walks the shared <c>projects</c> and <c>teams</c> commands, then <c>list</c> and <c>show</c>, or
/// stores defaults once with <c>config init --project --team</c>. Every "I need more input" answer
/// is a structured error naming the next command to run.
/// </summary>
public static class SprintModule
{
    public static CommandModule Definition { get; } = new(
        "sprints",
        "Read a team's sprints and the work items in them.",
        Configure);

    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        var context = new ReadCommandContext(services, names.Root);
        Command list = CreateListCommand(context, names, services);
        Command show = CreateShowCommand(context, names, services);
        target.Subcommands.Add(list);
        target.Subcommands.Add(show);
        return new Dictionary<Command, string>
        {
            [list] = "iterationSearch",
            [show] = "sprintSearch"
        };
    }

    private static Command CreateListCommand(ReadCommandContext context, ToolCommandNames names, CommandServices services)
    {
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        Option<string?> projectOption = ReadCommandContext.CreateProjectOption();
        Option<string?> teamOption = ReadCommandContext.CreateTeamOption();
        var timeframeOption = new Option<string?>("--timeframe")
        {
            Description = "Limit to 'current', 'past' or 'future'. Omit for every iteration."
        };
        var command = new Command("list", $"List a team's iterations, to pick one for '{names.Invocation("show")}'.");
        command.Options.Add(configOption);
        command.Options.Add(projectOption);
        command.Options.Add(teamOption);
        command.Options.Add(timeframeOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "iterationSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            async (session, configuration, token) =>
            {
                string project = context.RequireProject(parseResult.GetValue(projectOption), configuration);
                string team = context.RequireTeam(parseResult.GetValue(teamOption), configuration);
                var reader = new SprintReader(session.Sprints, configuration.OrganizationUrl, services.TimeProvider);
                return await reader.ListIterationsAsync(
                    project,
                    team,
                    parseResult.GetValue(timeframeOption),
                    token);
            },
            cancellationToken));
        return command;
    }

    private static Command CreateShowCommand(ReadCommandContext context, ToolCommandNames names, CommandServices services)
    {
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        Option<string?> projectOption = ReadCommandContext.CreateProjectOption();
        Option<string?> teamOption = ReadCommandContext.CreateTeamOption();
        var sprintOption = new Option<string?>("--sprint")
        {
            Description = "Iteration name or path. Omit for the team's current sprint."
        };
        var stateOption = new Option<string[]>("--state")
        {
            Description = "Keep only these work-item states, for example --state Done --state Closed.",
            AllowMultipleArgumentsPerToken = true
        };
        var command = new Command(
            "show",
            "Overview of a sprint's work items. Use the work-item command for one item's detail.");
        command.Options.Add(configOption);
        command.Options.Add(projectOption);
        command.Options.Add(teamOption);
        command.Options.Add(sprintOption);
        command.Options.Add(stateOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "sprintSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            async (session, configuration, token) =>
            {
                string project = context.RequireProject(parseResult.GetValue(projectOption), configuration);
                string team = context.RequireTeam(parseResult.GetValue(teamOption), configuration);
                var reader = new SprintReader(session.Sprints, configuration.OrganizationUrl, services.TimeProvider);
                return await reader.ReadAsync(
                    project,
                    team,
                    parseResult.GetValue(sprintOption),
                    parseResult.GetValue(stateOption) ?? [],
                    token);
            },
            cancellationToken));
        return command;
    }
}
