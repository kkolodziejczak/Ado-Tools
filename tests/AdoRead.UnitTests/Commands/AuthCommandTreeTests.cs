using System.CommandLine;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.UnitTests.Credentials;

namespace AdoRead.UnitTests.Commands;

public sealed class AuthCommandTreeTests
{
    public static TheoryData<string> ToolExecutables => new()
    {
        "ado-read",
        "ado-read-pr",
        "ado-read-work-item",
        "ado-read-wiki",
        "ado-read-sprints",
    };

    [Theory]
    [MemberData(nameof(ToolExecutables))]
    public void EveryToolExposesAuthLoginStatusAndLogout(string executable)
    {
        CommandApplication application = Compose(executable);

        Command auth = Assert.Single(application.RootCommand.Subcommands, command => command.Name == "auth");
        Assert.Equal(
            new[] { "login", "logout", "status" },
            auth.Subcommands.Select(command => command.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(ToolExecutables))]
    public void NoCommandInAnyToolAcceptsATokenOption(string executable)
    {
        CommandApplication application = Compose(executable);

        IEnumerable<string> optionNames = Descendants(application.RootCommand)
            .SelectMany(command => command.Options)
            .SelectMany(option => option.Aliases);

        Assert.DoesNotContain(optionNames, IsTokenOption);
    }

    private static bool IsTokenOption(string alias) =>
        alias.TrimStart('-').Split('-').Any(segment => segment is "token" or "tokens" or "pat" or "pats");

    private static IEnumerable<Command> Descendants(Command command)
    {
        yield return command;
        foreach (Command subcommand in command.Subcommands)
        {
            foreach (Command descendant in Descendants(subcommand))
            {
                yield return descendant;
            }
        }
    }

    private static CommandApplication Compose(string executable)
    {
        var composition = new ToolComposition(
            new UnusedStore(),
            new JsonOutput(TextWriter.Null),
            TimeProvider.System,
            patProvider: null,
            sessionFactory: null,
            credentialVault: new MemoryCredentialVault(),
            terminalInput: null);

        return executable switch
        {
            "ado-read" => composition.CreateUmbrella(executable, [PullRequestModule.Definition, WorkItemModule.Definition, WikiModule.Definition, SprintModule.Definition]),
            "ado-read-pr" => composition.CreateSlice(executable, PullRequestModule.Definition),
            "ado-read-work-item" => composition.CreateSlice(executable, WorkItemModule.Definition),
            "ado-read-wiki" => composition.CreateSlice(executable, WikiModule.Definition),
            "ado-read-sprints" => composition.CreateSlice(executable, SprintModule.Definition),
            _ => throw new ArgumentOutOfRangeException(nameof(executable), executable, null)
        };
    }

    private sealed class UnusedStore : IConfigurationStore
    {
        public string DefaultPath => "unused";

        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
