using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Sprints;

namespace AdoRead.Commands.Shared;

/// <summary>
/// Project and team discovery. These sit at the top level of every tool because a caller who does
/// not yet know the project or team starts here before any feature command needs one.
/// </summary>
public static class DiscoveryCommands
{
    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        var context = new ReadCommandContext(services, names.Root);
        Command projects = CreateProjectsCommand(context, services);
        Command teams = CreateTeamsCommand(context, services);
        target.Subcommands.Add(projects);
        target.Subcommands.Add(teams);
        return new Dictionary<Command, string>
        {
            [projects] = "projectSearch",
            [teams] = "teamSearch"
        };
    }

    private static Command CreateProjectsCommand(ReadCommandContext context, CommandServices services)
    {
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        var command = new Command("projects", "List every accessible project in the organization.");
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "projectSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            async (session, configuration, token) =>
            {
                IReadOnlyList<AdoProject> projects = await session.Projects.GetAllAsync(token);
                return new SearchEnvelope<ProjectDocument>(
                    "1.0",
                    "projectSearch",
                    projects.Count > 0 ? "success" : "notFound",
                    new { organizationUrl = configuration.OrganizationUrl },
                    services.TimeProvider.GetUtcNow(),
                    projects.Select(project => new ProjectDocument(project.Id, project.Name, project.Url)).ToArray(),
                    []);
            },
            cancellationToken));
        return command;
    }

    private static Command CreateTeamsCommand(ReadCommandContext context, CommandServices services)
    {
        Option<string?> configOption = ReadCommandContext.CreateConfigOption();
        Option<string?> projectOption = ReadCommandContext.CreateProjectOption();
        var allOption = new Option<bool>("--all")
        {
            Description = "Include teams you are not a member of."
        };
        var command = new Command("teams", "List teams in a project, to pick one for the sprint commands.");
        command.Options.Add(configOption);
        command.Options.Add(projectOption);
        command.Options.Add(allOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "teamSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(services.CompactOption),
            async (session, configuration, token) =>
            {
                string project = context.RequireProject(parseResult.GetValue(projectOption), configuration);
                bool mineOnly = !parseResult.GetValue(allOption);
                IReadOnlyList<AdoTeam> teams = await session.Teams.GetTeamsAsync(project, mineOnly, token);
                return new SearchEnvelope<TeamDocument>(
                    "1.0",
                    "teamSearch",
                    teams.Count > 0 ? "success" : "notFound",
                    new TeamQuery(configuration.OrganizationUrl, project, mineOnly),
                    services.TimeProvider.GetUtcNow(),
                    teams.Select(team => new TeamDocument(team.Id, team.Name, team.Description)).ToArray(),
                    []);
            },
            cancellationToken));
        return command;
    }
}
