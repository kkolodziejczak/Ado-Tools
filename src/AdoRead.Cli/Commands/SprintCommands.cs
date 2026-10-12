using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Sprints;

namespace AdoRead.Commands;

/// <summary>
/// Sprint reading plus the discovery commands that lead to it. Sprints are team scoped, so a
/// caller who does not yet know the project or team walks:
/// <c>projects</c> -> <c>teams</c> -> <c>sprints</c> -> <c>sprint</c>, or stores defaults once
/// with <c>config init --project --team</c>. Every "I need more input" answer is a structured
/// error naming the next command to run.
/// </summary>
public static class SprintCommands
{
    public static IReadOnlyDictionary<Command, string> AddTo(
        RootCommand rootCommand,
        Option<bool> compactOption,
        IConfigurationStore configurationStore,
        IPatProvider patProvider,
        IAdoSessionFactory sessionFactory,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider)
    {
        var context = new CommandContext(
            compactOption,
            configurationStore,
            patProvider,
            sessionFactory,
            jsonOutput,
            timeProvider);
        Command projects = CreateProjectsCommand(context);
        Command teams = CreateTeamsCommand(context);
        Command sprints = CreateSprintsCommand(context);
        Command sprint = CreateSprintCommand(context);
        rootCommand.Subcommands.Add(projects);
        rootCommand.Subcommands.Add(teams);
        rootCommand.Subcommands.Add(sprints);
        rootCommand.Subcommands.Add(sprint);
        return new Dictionary<Command, string>
        {
            [projects] = "projectSearch",
            [teams] = "teamSearch",
            [sprints] = "iterationSearch",
            [sprint] = "sprintSearch"
        };
    }

    private static Command CreateProjectsCommand(CommandContext context)
    {
        Option<string?> configOption = CreateConfigOption();
        var command = new Command("projects", "List every accessible project in the organization.");
        command.Options.Add(configOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "projectSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(context.CompactOption),
            async (session, configuration, token) =>
            {
                IReadOnlyList<AdoProject> projects = await session.Projects.GetAllAsync(token);
                return new SearchEnvelope<ProjectDocument>(
                    "1.0",
                    "projectSearch",
                    projects.Count > 0 ? "success" : "notFound",
                    new { organizationUrl = configuration.OrganizationUrl },
                    context.TimeProvider.GetUtcNow(),
                    projects.Select(project => new ProjectDocument(project.Id, project.Name, project.Url)).ToArray(),
                    []);
            },
            cancellationToken));
        return command;
    }

    private static Command CreateTeamsCommand(CommandContext context)
    {
        Option<string?> configOption = CreateConfigOption();
        Option<string?> projectOption = CreateProjectOption();
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
            parseResult.GetValue(context.CompactOption),
            async (session, configuration, token) =>
            {
                string project = RequireProject(parseResult.GetValue(projectOption), configuration);
                bool mineOnly = !parseResult.GetValue(allOption);
                IReadOnlyList<AdoTeam> teams = await session.Teams.GetTeamsAsync(project, mineOnly, token);
                return new SearchEnvelope<TeamDocument>(
                    "1.0",
                    "teamSearch",
                    teams.Count > 0 ? "success" : "notFound",
                    new TeamQuery(configuration.OrganizationUrl, project, mineOnly),
                    context.TimeProvider.GetUtcNow(),
                    teams.Select(team => new TeamDocument(team.Id, team.Name, team.Description)).ToArray(),
                    []);
            },
            cancellationToken));
        return command;
    }

    private static Command CreateSprintsCommand(CommandContext context)
    {
        Option<string?> configOption = CreateConfigOption();
        Option<string?> projectOption = CreateProjectOption();
        Option<string?> teamOption = CreateTeamOption();
        var timeframeOption = new Option<string?>("--timeframe")
        {
            Description = "Limit to 'current', 'past' or 'future'. Omit for every iteration."
        };
        var command = new Command("sprints", "List a team's iterations, to pick one for 'ado-read sprint'.");
        command.Options.Add(configOption);
        command.Options.Add(projectOption);
        command.Options.Add(teamOption);
        command.Options.Add(timeframeOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "iterationSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(context.CompactOption),
            async (session, configuration, token) =>
            {
                string project = RequireProject(parseResult.GetValue(projectOption), configuration);
                string team = RequireTeam(parseResult.GetValue(teamOption), configuration);
                var reader = new SprintReader(session.Sprints, configuration.OrganizationUrl, context.TimeProvider);
                return await reader.ListIterationsAsync(
                    project,
                    team,
                    parseResult.GetValue(timeframeOption),
                    token);
            },
            cancellationToken));
        return command;
    }

    private static Command CreateSprintCommand(CommandContext context)
    {
        Option<string?> configOption = CreateConfigOption();
        Option<string?> projectOption = CreateProjectOption();
        Option<string?> teamOption = CreateTeamOption();
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
            "sprint",
            "Overview of a sprint's work items. Use 'ado-read work-item <id>' for one item's detail.");
        command.Options.Add(configOption);
        command.Options.Add(projectOption);
        command.Options.Add(teamOption);
        command.Options.Add(sprintOption);
        command.Options.Add(stateOption);
        command.SetAction(async (parseResult, cancellationToken) => await context.RunAsync(
            "sprintSearch",
            parseResult.GetValue(configOption),
            parseResult.GetValue(context.CompactOption),
            async (session, configuration, token) =>
            {
                string project = RequireProject(parseResult.GetValue(projectOption), configuration);
                string team = RequireTeam(parseResult.GetValue(teamOption), configuration);
                var reader = new SprintReader(session.Sprints, configuration.OrganizationUrl, context.TimeProvider);
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

    private static string RequireProject(string? value, AdoReadConfiguration configuration) =>
        Require(
            value ?? configuration.DefaultProject,
            "projectRequired",
            "No project given and none configured. Run 'ado-read projects' to list them, then pass "
            + "--project or store one with 'ado-read config init --project <name> --force'.");

    private static string RequireTeam(string? value, AdoReadConfiguration configuration) =>
        Require(
            value ?? configuration.DefaultTeam,
            "teamRequired",
            "No team given and none configured. Run 'ado-read teams' to list them, then pass "
            + "--team or store one with 'ado-read config init --team <name> --force'.");

    private static string Require(string? value, string code, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ConfigurationException(code, message) : value.Trim();

    private static Option<string?> CreateConfigOption() => new("--config")
    {
        Description = "Override the default ~/.ado-read/config.json path."
    };

    private static Option<string?> CreateProjectOption() => new("--project")
    {
        Description = "Project name or ID. Defaults to the configured project."
    };

    private static Option<string?> CreateTeamOption() => new("--team")
    {
        Description = "Team name or ID. Defaults to the configured team."
    };

    /// <summary>
    /// Shared plumbing for the read commands: load configuration, read the PAT, apply the
    /// whole-command timeout, open one session, and map every expected failure onto a single
    /// JSON envelope and exit code.
    /// </summary>
    private sealed record CommandContext(
        Option<bool> CompactOption,
        IConfigurationStore ConfigurationStore,
        IPatProvider PatProvider,
        IAdoSessionFactory SessionFactory,
        IJsonOutput JsonOutput,
        TimeProvider TimeProvider)
    {
        public async Task<int> RunAsync<T>(
            string kind,
            string? configPath,
            bool compact,
            Func<IAdoSession, AdoReadConfiguration, CancellationToken, Task<SearchEnvelope<T>>> readAsync,
            CancellationToken cancellationToken)
        {
            try
            {
                AdoReadConfiguration configuration = await ConfigurationStore.LoadAsync(configPath, cancellationToken);
                string pat = PatProvider.GetRequiredPat();
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(configuration.RequestTimeout);
                await using IAdoSession session = SessionFactory.Create(configuration.OrganizationUrl, pat);
                SearchEnvelope<T> envelope = await readAsync(session, configuration, requestTimeout.Token);
                cancellationToken.ThrowIfCancellationRequested();
                await JsonOutput.WriteAsync(envelope, compact, cancellationToken);
                return ExitCodes.FromStatus(envelope.Status);
            }
            catch (ConfigurationException exception)
            {
                return await WriteErrorAsync(
                    kind,
                    new SearchError("configuration", null, exception.Code, exception.Message, false),
                    ExitCodes.InvalidInputOrConfiguration,
                    compact,
                    cancellationToken);
            }
            catch (CredentialException exception)
            {
                return await WriteErrorAsync(
                    kind,
                    new SearchError("authentication", null, exception.Code, exception.Message, false),
                    ExitCodes.Error,
                    compact,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await WriteErrorAsync(
                    kind,
                    new SearchError("azureDevOps", null, "timeout", "The Azure DevOps request timed out.", true),
                    ExitCodes.Error,
                    compact,
                    CancellationToken.None);
            }
        }

        private async Task<int> WriteErrorAsync(
            string kind,
            SearchError error,
            int exitCode,
            bool compact,
            CancellationToken cancellationToken)
        {
            SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
                kind,
                [error],
                TimeProvider.GetUtcNow());
            await JsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return exitCode;
        }
    }
}
