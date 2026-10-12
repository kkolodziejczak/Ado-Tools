using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Commands;

public sealed class WikiDiscoveryCommandTests
{
    [Fact]
    public async Task Wikis_UsesExplicitProjectAndReturnsArgumentVectorHints()
    {
        var writer = new StringWriter();
        var factory = new Factory();

        int exit = await App(new Store("Configured Project"), factory, writer).InvokeAsync(
            ["wiki", "list", "--project", "Explicit Project", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wikiCatalog", json.RootElement.GetProperty("kind").GetString());
        JsonElement wiki = json.RootElement.GetProperty("results")[0];
        Assert.Equal("Space Wiki", wiki.GetProperty("name").GetString());
        Assert.Equal("wiki pages", wiki.GetProperty("versions")[0].GetProperty("next").GetProperty("command").GetString());
        Assert.Equal("--project", wiki.GetProperty("versions")[0].GetProperty("next").GetProperty("arguments")[0].GetString());
        Assert.Equal("Explicit Project", Assert.Single(factory.Source.Projects));
    }

    [Theory]
    [InlineData("wiki list", "wikiCatalog")]
    [InlineData("wiki pages", "wikiPageCatalog")]
    public async Task DiscoveryCommands_UseV2EnvelopeForMalformedArguments(string command, string kind)
    {
        var writer = new StringWriter();

        int exit = await App(new Store("Project"), new Factory(), writer).InvokeAsync([.. command.Split(' '), "--bogus", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal(kind, json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("2.0", json.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Theory]
    [InlineData("wiki list", "wikiCatalog")]
    [InlineData("wiki pages", "wikiPageCatalog")]
    public async Task DiscoveryCommands_UseV2AuthenticationErrorForMissingPat(string command, string kind)
    {
        var writer = new StringWriter();
        string[] arguments = command == "wiki list" ? [.. command.Split(' '), "--project", "Project", "--compact"] : [.. command.Split(' '), "--project", "Project", "--wiki", "Wiki", "--compact"];

        int exit = await App(new Store("Project"), new Factory(), writer, new MissingPat()).InvokeAsync(arguments, CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal(kind, json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("2.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("authentication", json.RootElement.GetProperty("errors")[0].GetProperty("scope").GetString());
        Assert.Equal("missingPat", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task WikiPages_UsesConfiguredProjectAndPreservesOneContinuationToken()
    {
        var writer = new StringWriter();

        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(
            ["wiki", "pages", "--wiki", "Space Wiki", "--top", "50", "--continuation-token", "opaque+token/==", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wikiPageCatalog", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("opaque+next/==", json.RootElement.GetProperty("page").GetProperty("continuationToken").GetString());
        JsonElement page = json.RootElement.GetProperty("results")[0];
        Assert.False(page.TryGetProperty("content", out _));
        Assert.False(page.TryGetProperty("viewStats", out _));
        Assert.Equal("--path", page.GetProperty("next").GetProperty("arguments")[4].GetString());
    }

    [Fact]
    public async Task WikiPages_PreservesSelectedVersionInPageHintAndAllowsTheMaximumTop()
    {
        var writer = new StringWriter();
        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(["wiki", "pages", "--wiki", "Space Wiki", "--version", "wikiMaster", "--version-type", "branch", "--top", "100", "--compact"], CancellationToken.None);
        Assert.Equal(ExitCodes.Success, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement arguments = json.RootElement.GetProperty("results")[0].GetProperty("next").GetProperty("arguments");
        Assert.Equal("wikiMaster", arguments[7].GetString());
        Assert.Equal("branch", arguments[9].GetString());
    }

    [Fact]
    public async Task WikiPages_RejectsTopAboveTheLocalMaximum()
    {
        var writer = new StringWriter();
        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(["wiki", "pages", "--wiki", "Space Wiki", "--top", "101", "--compact"], CancellationToken.None);
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("command", error.GetProperty("scope").GetString());
        Assert.Equal("invalidTop", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WikiPages_InvalidVersionPointsBackToTheSelectedProjectsWikis()
    {
        var writer = new StringWriter();

        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(["wiki", "pages", "--wiki", "Space Wiki", "--version", "missing", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("invalidWikiReference", error.GetProperty("code").GetString());
        JsonElement next = error.GetProperty("next");
        Assert.Equal("wiki list", next.GetProperty("command").GetString());
        Assert.Equal(["--project", "Configured Project"], next.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("wiki list", "projectRequired", "projects", "configuration")]
    [InlineData("wiki pages", "wikiRequired", "wiki list", "command")]
    public async Task DiscoveryCommands_ProvideStructuredRecoveryHintsWhenContextIsMissing(
        string command,
        string errorCode,
        string nextCommand,
        string scope)
    {
        var writer = new StringWriter();

        int exit = await App(new Store(null), new Factory(), writer).InvokeAsync([.. command.Split(' '), "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal(scope, error.GetProperty("scope").GetString());
        Assert.Equal(errorCode, error.GetProperty("code").GetString());
        Assert.Equal(nextCommand, error.GetProperty("next").GetProperty("command").GetString());
    }

    [Theory]
    [InlineData("wiki list")]
    [InlineData("wiki pages")]
    public async Task DiscoveryCommands_RejectExplicitlyBlankProjectInsteadOfUsingConfiguredDefault(string command)
    {
        var writer = new StringWriter();
        string[] arguments = command == "wiki list"
            ? [.. command.Split(' '), "--project", "   ", "--compact"]
            : [.. command.Split(' '), "--project", "   ", "--wiki", "Space Wiki", "--compact"];

        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(arguments, CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("command", error.GetProperty("scope").GetString());
        Assert.Equal("projectRequired", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("wiki list")]
    [InlineData("wiki pages")]
    public async Task DiscoveryCommands_RejectUnsafeProjectAsInvalidInput(string command)
    {
        var writer = new StringWriter();
        string[] arguments = command == "wiki list"
            ? [.. command.Split(' '), "--project", "Project/Child", "--compact"]
            : [.. command.Split(' '), "--project", "Project/Child", "--wiki", "Space Wiki", "--compact"];

        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(arguments, CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("command", error.GetProperty("scope").GetString());
        Assert.Equal("invalidProject", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("..")]
    [InlineData(" .. ")]
    [InlineData("../..")]
    [InlineData("a/../../b")]
    public async Task WikiPages_RejectsUnsafeWikiBeforeCreatingASession(string wiki)
    {
        var writer = new StringWriter();
        var factory = new Factory();

        int exit = await App(new Store("Configured Project"), factory, writer).InvokeAsync(["wiki", "pages", "--wiki", wiki, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("command", error.GetProperty("scope").GetString());
        Assert.Equal("invalidWikiReference", error.GetProperty("code").GetString());
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("wiki list")]
    [InlineData("wiki pages")]
    public async Task DiscoveryCommands_AttributePaddedUnsafeProjectToCommandInput(string command)
    {
        var writer = new StringWriter();
        string[] arguments = command == "wiki list"
            ? [.. command.Split(' '), "--project", " .. ", "--compact"]
            : [.. command.Split(' '), "--project", " .. ", "--wiki", "Space Wiki", "--compact"];

        int exit = await App(new Store("Configured Project"), new Factory(), writer).InvokeAsync(arguments, CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("command", error.GetProperty("scope").GetString());
        Assert.Equal("invalidProject", error.GetProperty("code").GetString());
    }

    private static CommandApplication App(Store store, Factory factory, StringWriter writer, IPatProvider? pat = null) =>
        TestTool.Umbrella(store, new JsonOutput(writer), TimeProvider.System, pat ?? new Pat(), factory);

    private sealed class Store(string? defaultProject) : IConfigurationStore
    {
        public string DefaultPath => "x";

        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(new Uri("https://dev.azure.com/example"), 30, 1, defaultProject));

        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Pat : IPatProvider
    {
        public string GetRequiredPat(Uri organizationUrl) => "pat";
    }

    private sealed class MissingPat : IPatProvider
    {
        public string GetRequiredPat(Uri organizationUrl) => throw new CredentialException("missingPat", "No token is stored for https://dev.azure.com/fabricated. Run `ado-read auth login`.");
    }

    private sealed class Factory : IAdoSessionFactory
    {
        public Source Source { get; } = new();
        public int Calls { get; private set; }
        public IAdoSession Create(Uri organizationUrl, string pat) { Calls++; return new Session(Source); }
    }

    private sealed class Session(IWikiSource source) : IAdoSession
    {
        public IProjectCatalog Projects => throw new NotSupportedException();
        public IPullRequestSource PullRequests => throw new NotSupportedException();
        public IWorkItemSource WorkItems => throw new NotSupportedException();
        public ISprintSource Sprints => throw new NotSupportedException();
        public ITeamClient Teams => throw new NotSupportedException();
        public IWikiSource Wikis { get; } = source;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Source : IWikiSource
    {
        public List<string> Projects { get; } = [];
        public Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken) { Projects.Add(project); return Task.FromResult<IReadOnlyList<SourceWiki>>([new(Guid.NewGuid(), "Zulu Wiki", "ProjectWiki", Guid.NewGuid(), null, "/", [new("z", "branch"), new("a", "tag")], null, null), new(Guid.NewGuid(), "Space Wiki", "ProjectWiki", Guid.NewGuid(), null, "/", [new("wikiMaster", "branch")], null, null)]); }
        public Task<SourceWikiPageCatalog> GetPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken) => Task.FromResult(new SourceWikiPageCatalog([new(1, "/leading -- path")], "opaque+next/=="));
    }
}
