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
        new[] { "project", "list" },
        new[] { "team", "list" },
        new[] { "pr", "show", "1" },
        new[] { "work-item", "show", "1" },
        new[] { "wiki", "list" },
        new[] { "wiki", "page", "list", "--wiki", "Wiki" },
        new[] { "wiki", "page", "show", "--wiki", "Wiki", "--path", "/Page" },
        new[] { "wiki", "page", "search", "authentication" },
        new[] { "sprint", "list" },
        new[] { "sprint", "show" },
        new[] { "pipeline", "run", "list", "--pr", "1" },
        new[] { "pipeline", "run", "show", "1" },
        new[] { "pipeline", "test", "list", "--run", "1" },
    };

    // Every name the tools shipped before one naming scheme, so none of them is accepted by mistake.
    public static TheoryData<string[]> RemovedUmbrellaCommands => new()
    {
        new[] { "wikis" },
        new[] { "wiki-pages", "--wiki", "Wiki" },
        new[] { "wiki-page", "--path", "/Page" },
        new[] { "wiki-search", "authentication" },
        new[] { "wiki", "pages", "--wiki", "Wiki" },
        new[] { "wiki", "search", "authentication" },
        new[] { "sprints", "show" },
        new[] { "sprint" },
        new[] { "projects" },
        new[] { "teams" },
        new[] { "pr", "1" },
        new[] { "work-item", "1" },
        new[] { "pipelines", "pr", "1" },
        new[] { "pipeline", "run", "1" },
        new[] { "pipeline", "tests", "1" },
    };

    public static TheoryData<string, string[]> SliceCommands => new()
    {
        { "pr", ["show", "1"] },
        { "pr", ["privacy", "show"] },
        { "pr", ["config", "show"] },
        { "pr", ["project", "list"] },
        { "pr", ["team", "list"] },
        { "work-item", ["show", "1"] },
        { "work-item", ["config", "show"] },
        { "work-item", ["team", "list"] },
        { "wiki", ["list"] },
        { "wiki", ["page", "list", "--wiki", "Wiki"] },
        { "wiki", ["page", "show", "--wiki", "Wiki", "--path", "/Page"] },
        { "wiki", ["page", "search", "authentication"] },
        { "wiki", ["project", "list"] },
        { "sprint", ["list"] },
        { "sprint", ["show"] },
        { "sprint", ["config", "show"] },
        { "sprint", ["project", "list"] },
        { "sprint", ["privacy", "reset"] },
        { "wiki", ["privacy", "set", "minimal"] },
        { "pipeline", ["run", "list", "--pr", "126"] },
        { "pipeline", ["run", "show", "9002"] },
        { "pipeline", ["test", "list", "--run", "9002"] },
    };

    public static TheoryData<string, string[]> ForeignCommands => new()
    {
        { "pr", ["wiki", "list"] },
        { "pr", ["work-item", "show", "1"] },
        { "pr", ["sprint", "list"] },
        { "work-item", ["wiki", "list"] },
        { "work-item", ["pr", "show", "1"] },
        { "work-item", ["sprint", "show"] },
        { "wiki", ["pr", "show", "1"] },
        { "wiki", ["work-item", "show", "1"] },
        { "wiki", ["sprint", "list"] },
        { "wiki", ["1"] },
        { "sprint", ["wiki", "list"] },
        { "sprint", ["pr", "show", "1"] },
        { "sprint", ["work-item", "show", "1"] },
        { "pipeline", ["pr", "show", "1"] },
    };

    private static readonly CommandModule[] AllModules =
    [
        PullRequestModule.Definition,
        WorkItemModule.Definition,
        WikiModule.Definition,
        SprintModule.Definition,
        PipelineModule.Definition
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
    [InlineData("list", "list")]
    [InlineData("show", "project list")]
    public async Task WikiSlice_HintsNameTheSliceCommands(string command, string nextCommand)
    {
        var writer = new StringWriter();
        CommandApplication application = ComposeSlice("wiki", writer, new ConfiguredStore());

        await application.InvokeAsync(["page", command, "--compact"], CancellationToken.None);

        JsonElement next = FirstError(writer.ToString()).GetProperty("next");
        Assert.Equal(nextCommand, next.GetProperty("command").GetString());
    }

    [Theory]
    [InlineData("list", "wiki list")]
    [InlineData("show", "project list")]
    public async Task UmbrellaWiki_HintsNameTheGroupedCommands(string command, string nextCommand)
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, credentialVault: TestPrivacy.ConsentedVault())
            .CreateUmbrella("ado-read", AllModules);

        await application.InvokeAsync(["wiki", "page", command, "--compact"], CancellationToken.None);

        JsonElement next = FirstError(writer.ToString()).GetProperty("next");
        Assert.Equal(nextCommand, next.GetProperty("command").GetString());
    }

    [Fact]
    public async Task SliceSprints_MissingProjectNamesTheSliceProjectsCommand()
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, new Pat(), new UnusedSessionFactory(), credentialVault: TestPrivacy.ConsentedVault())
            .CreateSlice("ado-read-sprint", SprintModule.Definition);

        await application.InvokeAsync(["show", "--compact"], CancellationToken.None);

        string message = FirstError(writer.ToString()).GetProperty("message").GetString()!;
        Assert.Contains("Run 'ado-read-sprint project list'", message, StringComparison.Ordinal);
        Assert.Contains("'ado-read-sprint config init --project <name> --force'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UmbrellaSprints_MissingProjectNamesTheUmbrellaProjectsCommand()
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, new Pat(), new UnusedSessionFactory(), credentialVault: TestPrivacy.ConsentedVault())
            .CreateUmbrella("ado-read", AllModules);

        await application.InvokeAsync(["sprint", "show", "--compact"], CancellationToken.None);

        string message = FirstError(writer.ToString()).GetProperty("message").GetString()!;
        Assert.Contains("Run 'ado-read project list'", message, StringComparison.Ordinal);
        Assert.Contains("'ado-read config init --project <name> --force'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedTokenNamesTheLoginCommandInsteadOfAnUnexpectedError()
    {
        var writer = new StringWriter();
        CommandApplication application = new ToolComposition(new ConfiguredStore(), new JsonOutput(writer), TimeProvider.System, new Pat(), new RejectingSessionFactory(), credentialVault: TestPrivacy.ConsentedVault())
            .CreateSlice("ado-read-pr", PullRequestModule.Definition);

        int exitCode = await application.InvokeAsync(["show", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = FirstError(writer.ToString());
        Assert.Equal(("authentication", "unauthorized"), (error.GetProperty("scope").GetString(), error.GetProperty("code").GetString()));
        Assert.Equal(
            "Azure DevOps rejected the token: it may be mistyped, expired or revoked, or made for another organization. Store a new one with `ado-read-pr auth login`.",
            error.GetProperty("message").GetString());
    }

    private static CommandApplication ComposeSlice(string feature, StringWriter writer, IConfigurationStore? store = null) =>
        new ToolComposition(store ?? new ProbeStore(), new JsonOutput(writer), TimeProvider.System, credentialVault: TestPrivacy.ConsentedVault())
            .CreateSlice($"ado-read-{feature}", feature switch
            {
                "pr" => PullRequestModule.Definition,
                "work-item" => WorkItemModule.Definition,
                "wiki" => WikiModule.Definition,
                "sprint" => SprintModule.Definition,
                "pipeline" => PipelineModule.Definition,
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

    /// <summary>Like the SDK, which connects when the session asks for its first client.</summary>
    private sealed class RejectingSessionFactory : IAdoSessionFactory
    {
        public IAdoSession Create(Uri organizationUrl, string pat) =>
            throw new Microsoft.VisualStudio.Services.Common.VssUnauthorizedException("VS30063: secret service response");
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
