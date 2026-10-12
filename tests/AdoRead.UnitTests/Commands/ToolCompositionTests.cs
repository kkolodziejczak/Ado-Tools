using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.UnitTests.Privacy;

namespace AdoRead.UnitTests.Commands;

public sealed class ToolCompositionTests
{
    // Each command below parses and reaches its action. The probe store then fails the action with a
    // configuration error, which proves the command exists without contacting Azure DevOps.
    public static TheoryData<string[]> UmbrellaCommands => new()
    {
        new[] { "config", "show" },
        new[] { "privacy", "show" },
        new[] { "privacy", "set", "minimal" },
        new[] { "privacy", "reset" },
        new[] { "projects" },
        new[] { "teams" },
        new[] { "pr", "1" },
        new[] { "work-item", "1" },
        new[] { "wiki", "list" },
        new[] { "wiki", "pages", "--wiki", "Wiki" },
        new[] { "wiki", "page", "--wiki", "Wiki", "--path", "/Page" },
        new[] { "wiki", "search", "authentication" },
        new[] { "sprints", "list" },
        new[] { "sprints", "show" },
    };

    public static TheoryData<string[]> RemovedUmbrellaCommands => new()
    {
        new[] { "wikis" },
        new[] { "wiki-pages", "--wiki", "Wiki" },
        new[] { "wiki-page", "--path", "/Page" },
        new[] { "wiki-search", "authentication" },
        new[] { "sprints" },
        new[] { "sprint" },
    };

    public static TheoryData<string, string[]> SliceCommands => new()
    {
        { "pr", ["1"] },
        { "pr", ["privacy", "show"] },
        { "pr", ["config", "show"] },
        { "pr", ["projects"] },
        { "pr", ["teams"] },
        { "work-item", ["1"] },
        { "work-item", ["config", "show"] },
        { "work-item", ["teams"] },
        { "wiki", ["list"] },
        { "wiki", ["pages", "--wiki", "Wiki"] },
        { "wiki", ["page", "--wiki", "Wiki", "--path", "/Page"] },
        { "wiki", ["search", "authentication"] },
        { "wiki", ["projects"] },
        { "sprints", ["list"] },
        { "sprints", ["show"] },
        { "sprints", ["config", "show"] },
        { "sprints", ["projects"] },
        { "sprints", ["privacy", "reset"] },
        { "wiki", ["privacy", "set", "minimal"] },
    };

    public static TheoryData<string, string[]> ForeignCommands => new()
    {
        { "pr", ["wiki", "list"] },
        { "pr", ["work-item", "1"] },
        { "pr", ["sprints", "list"] },
        { "work-item", ["wiki", "list"] },
        { "work-item", ["pr", "1"] },
        { "work-item", ["sprints", "show"] },
        { "wiki", ["pr", "1"] },
        { "wiki", ["work-item", "1"] },
        { "wiki", ["sprints", "list"] },
        { "wiki", ["1"] },
        { "sprints", ["wiki", "list"] },
        { "sprints", ["pr", "1"] },
        { "sprints", ["work-item", "1"] },
    };

    private static readonly CommandModule[] AllModules =
    [
        PullRequestModule.Definition,
        WorkItemModule.Definition,
        WikiModule.Definition,
        SprintModule.Definition
    ];

    [Theory]
    [MemberData(nameof(UmbrellaCommands))]
    public async Task Umbrella_MountsEveryFeatureCommandUnderItsGroup(string[] arguments)
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ProbeStore(), new JsonOutput(writer), TimeProvider.System, credentialVault: TestPrivacy.ConsentedVault())
            .CreateUmbrella("ado-read", AllModules);

        int exitCode = await application.InvokeAsync([.. arguments, "--compact"], CancellationToken.None);

        AssertReachedCommand(exitCode, writer.ToString());
    }

    [Theory]
    [MemberData(nameof(RemovedUmbrellaCommands))]
    public async Task Umbrella_NoLongerMountsTheFlatCommandNames(string[] arguments)
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ProbeStore(), new JsonOutput(writer), TimeProvider.System, credentialVault: TestPrivacy.ConsentedVault())
            .CreateUmbrella("ado-read", AllModules);

        int exitCode = await application.InvokeAsync([.. arguments, "--compact"], CancellationToken.None);

        AssertInvalidArguments(exitCode, writer.ToString());
    }

    [Theory]
    [MemberData(nameof(SliceCommands))]
    public async Task Slice_MountsItsOwnCommandsOnTheExecutable(string feature, string[] arguments)
    {
        var writer = new StringWriter();
        CommandApplication application = ComposeSlice(feature, writer);

        int exitCode = await application.InvokeAsync([.. arguments, "--compact"], CancellationToken.None);

        AssertReachedCommand(exitCode, writer.ToString());
    }

    [Theory]
    [MemberData(nameof(ForeignCommands))]
    public async Task Slice_RejectsCommandsOfOtherFeatures(string feature, string[] arguments)
    {
        var writer = new StringWriter();
        CommandApplication application = ComposeSlice(feature, writer);

        int exitCode = await application.InvokeAsync([.. arguments, "--compact"], CancellationToken.None);

        AssertInvalidArguments(exitCode, writer.ToString());
    }

    [Theory]
    [InlineData("pages", "list")]
    [InlineData("page", "projects")]
    public async Task WikiSlice_HintsNameTheSliceCommands(string command, string nextCommand)
    {
        var writer = new StringWriter();
        CommandApplication application = ComposeSlice("wiki", writer, new ConfiguredStore());

        await application.InvokeAsync([command, "--compact"], CancellationToken.None);

        JsonElement next = FirstError(writer.ToString()).GetProperty("next");
        Assert.Equal(nextCommand, next.GetProperty("command").GetString());
    }

    [Theory]
    [InlineData("pages", "wiki list")]
    [InlineData("page", "projects")]
    public async Task UmbrellaWiki_HintsNameTheGroupedCommands(string command, string nextCommand)
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, credentialVault: TestPrivacy.ConsentedVault())
            .CreateUmbrella("ado-read", AllModules);

        await application.InvokeAsync(["wiki", command, "--compact"], CancellationToken.None);

        JsonElement next = FirstError(writer.ToString()).GetProperty("next");
        Assert.Equal(nextCommand, next.GetProperty("command").GetString());
    }

    [Fact]
    public async Task SliceSprints_MissingProjectNamesTheSliceProjectsCommand()
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, new Pat(), new UnusedSessionFactory(), credentialVault: TestPrivacy.ConsentedVault())
            .CreateSlice("ado-read-sprints", SprintModule.Definition);

        await application.InvokeAsync(["show", "--compact"], CancellationToken.None);

        string message = FirstError(writer.ToString()).GetProperty("message").GetString()!;
        Assert.Contains("Run 'ado-read-sprints projects'", message, StringComparison.Ordinal);
        Assert.Contains("'ado-read-sprints config init --project <name> --force'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UmbrellaSprints_MissingProjectNamesTheUmbrellaProjectsCommand()
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, new Pat(), new UnusedSessionFactory(), credentialVault: TestPrivacy.ConsentedVault())
            .CreateUmbrella("ado-read", AllModules);

        await application.InvokeAsync(["sprints", "show", "--compact"], CancellationToken.None);

        string message = FirstError(writer.ToString()).GetProperty("message").GetString()!;
        Assert.Contains("Run 'ado-read projects'", message, StringComparison.Ordinal);
        Assert.Contains("'ado-read config init --project <name> --force'", message, StringComparison.Ordinal);
    }

    private static CommandApplication ComposeSlice(string feature, StringWriter writer, IConfigurationStore? store = null) =>
        new ToolComposition(store ?? new ProbeStore(), new JsonOutput(writer), TimeProvider.System, credentialVault: TestPrivacy.ConsentedVault())
            .CreateSlice($"ado-read-{feature}", feature switch
            {
                "pr" => PullRequestModule.Definition,
                "work-item" => WorkItemModule.Definition,
                "wiki" => WikiModule.Definition,
                "sprints" => SprintModule.Definition,
                _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
            });

    private static void AssertReachedCommand(int exitCode, string output)
    {
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("probeReached", FirstError(output).GetProperty("code").GetString());
    }

    private static void AssertInvalidArguments(int exitCode, string output)
    {
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidArguments", FirstError(output).GetProperty("code").GetString());
    }

    private static JsonElement FirstError(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("errors")[0].Clone();
    }

    private sealed class ProbeStore : IConfigurationStore
    {
        public string DefaultPath => "probe";

        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) =>
            throw new ConfigurationException("probeReached", "The command reached its action.");

        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ConfiguredStore : IConfigurationStore
    {
        public string DefaultPath => "configured";

        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(new Uri("https://dev.azure.com/example"), 30, 1, null));

        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Pat : IPatProvider
    {
        public string GetRequiredPat(Uri organizationUrl) => "pat";
    }

    private sealed class UnusedSessionFactory : IAdoSessionFactory
    {
        public IAdoSession Create(Uri organizationUrl, string pat) => new UnusedSession();
    }

    private sealed class UnusedSession : IAdoSession
    {
        public IProjectCatalog Projects => throw new NotSupportedException();
        public IPullRequestSource PullRequests => throw new NotSupportedException();
        public IWorkItemSource WorkItems => throw new NotSupportedException();
        public ISprintSource Sprints => throw new NotSupportedException();
        public ITeamClient Teams => throw new NotSupportedException();
        public IWikiSource Wikis => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
