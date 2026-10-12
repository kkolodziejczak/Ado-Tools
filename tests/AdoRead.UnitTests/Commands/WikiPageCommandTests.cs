using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Commands;

public sealed class WikiPageCommandTests
{
    [Fact]
    public async Task InvokeAsync_GitPathWithoutVersionIsAValidatedWikiReferenceError()
    {
        var writer = new StringWriter();
        int exit = await App(new Store(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", "Wiki", "--git-path", "/Page.md", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wikiPageLookup", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("invalidWikiReference", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }
    [Theory]
    [InlineData("https://dev.azure.com/fabricated/Project/_wiki/wikis/Wiki/1/Page", "organizationMismatch", 1)]
    [InlineData("http://dev.azure.com/example/Project/_wiki/wikis/Wiki/1/Page", "invalidWikiReference", 0)]
    public async Task InvokeAsync_RejectsUnsafeReferenceBeforeSession(string url, string code, int configLoads)
    {
        var store = new Store(); var pat = new Pat(); var factory = new Factory(new Source()); var writer = new StringWriter();
        int exit = await App(store, pat, factory, writer).InvokeAsync(["wiki-page", url, "--compact"], CancellationToken.None);
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit); Assert.Equal(configLoads, store.Loads); Assert.Equal(0, pat.Reads); Assert.Equal(0, factory.Calls); using JsonDocument json = JsonDocument.Parse(writer.ToString()); Assert.Equal(code, json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }
    [Theory]
    [InlineData("404", "notFound", 3)]
    [InlineData("error", "error", 5)]
    public async Task InvokeAsync_MapsSourceTerminalStates(string page, string status, int expectedExit)
    {
        var writer = new StringWriter(); int exit = await App(new Store(), new Pat(), new Factory(new Source(page)), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", "Wiki", "--page-id", "1", "--compact"], CancellationToken.None);
        Assert.Equal(expectedExit, exit); using JsonDocument json = JsonDocument.Parse(writer.ToString()); Assert.Equal(status, json.RootElement.GetProperty("status").GetString()); Assert.Equal("2.0", json.RootElement.GetProperty("schemaVersion").GetString());
    }
    [Fact]
    public async Task InvokeAsync_UsesWikiV2EnvelopeForMalformedArguments()
    {
        var writer = new StringWriter(); int exit = await App(new Store(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki-page", "--page-id", "0", "--compact"], CancellationToken.None);
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit); using JsonDocument json = JsonDocument.Parse(writer.ToString()); Assert.Equal("wikiPageLookup", json.RootElement.GetProperty("kind").GetString()); Assert.Equal("2.0", json.RootElement.GetProperty("schemaVersion").GetString()); Assert.Equal("invalidWikiReference", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_RejectsMalformedSelectorBeforeLoadingMissingConfiguration()
    {
        var writer = new StringWriter();

        int exit = await App(new MissingStore(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki-page", "--wiki", "Wiki", "--page-id", "0", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalidWikiReference", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }
    [Fact]
    public async Task InvokeAsync_MapsWholeCommandTimeoutToWikiV2Error()
    {
        var writer = new StringWriter(); int exit = await App(new Store(1), new Pat(), new Factory(new Source("delay")), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", "Wiki", "--page-id", "1", "--compact"], CancellationToken.None);
        Assert.Equal(ExitCodes.Error, exit); using JsonDocument json = JsonDocument.Parse(writer.ToString()); Assert.Equal("wikiPageLookup", json.RootElement.GetProperty("kind").GetString()); Assert.Equal("timeout", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()); Assert.True(json.RootElement.GetProperty("errors")[0].GetProperty("transient").GetBoolean());
    }
    [Fact]
    public async Task InvokeAsync_MapsCallerCancellationToEstablishedExitCode()
    {
        var writer = new StringWriter(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); int exit = await App(new Store(), new Pat(), new Factory(new Source("delay")), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", "Wiki", "--page-id", "1", "--compact"], cancellation.Token);
        Assert.Equal(ExitCodes.Cancelled, exit); using JsonDocument json = JsonDocument.Parse(writer.ToString()); Assert.Equal("wikiPageLookup", json.RootElement.GetProperty("kind").GetString()); Assert.Equal("cancelled", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_UsesConfiguredDefaultProjectForExplicitSelector()
    {
        var source = new Source(); var writer = new StringWriter();

        int exit = await App(new Store(defaultProject: "Configured Project"), new Pat(), new Factory(source), writer).InvokeAsync(["wiki-page", "--wiki", "Wiki", "--path", "/P", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Configured Project", source.LastReference!.Project);
    }

    [Fact]
    public async Task InvokeAsync_RejectsExplicitlyBlankProjectInsteadOfUsingConfiguredDefault()
    {
        var source = new Source(); var writer = new StringWriter();

        int exit = await App(new Store(defaultProject: "Configured Project"), new Pat(), new Factory(source), writer).InvokeAsync(["wiki-page", "--project", "   ", "--wiki", "Wiki", "--path", "/P", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("projectRequired", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(source.LastReference);
    }

    [Theory]
    [InlineData("Configured Project", "wikis")]
    [InlineData(null, "projects")]
    public async Task InvokeAsync_NoReferenceProvidesStructuredWizardHint(string? defaultProject, string nextCommand)
    {
        var writer = new StringWriter();

        int exit = await App(new Store(defaultProject: defaultProject), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki-page", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement next = json.RootElement.GetProperty("errors")[0].GetProperty("next");
        Assert.Equal(nextCommand, next.GetProperty("command").GetString());
        if (defaultProject is not null) Assert.Equal(defaultProject, next.GetProperty("arguments")[1].GetString());
    }

    [Fact]
    public async Task InvokeAsync_NoSelectorUsesExplicitProjectInWizardHint()
    {
        var writer = new StringWriter();

        int exit = await App(new Store(defaultProject: "Configured Project"), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki-page", "--project", "Explicit Project", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement next = json.RootElement.GetProperty("errors")[0].GetProperty("next");
        Assert.Equal("wikis", next.GetProperty("command").GetString());
        Assert.Equal("Explicit Project", next.GetProperty("arguments")[1].GetString());
    }

    [Theory]
    [InlineData("Project/Child", "Wiki", "invalidProject")]
    [InlineData(" . ", "Wiki", "invalidProject")]
    [InlineData("Project", "..", "invalidWikiReference")]
    [InlineData("Project", " .. ", "invalidWikiReference")]
    [InlineData("Project", "../..", "invalidWikiReference")]
    public async Task InvokeAsync_RejectsUnsafeProjectAndWikiAsInvalidInput(string project, string wiki, string code)
    {
        var writer = new StringWriter();

        int exit = await App(new Store(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki-page", "--project", project, "--wiki", wiki, "--path", "/P", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("command", error.GetProperty("scope").GetString());
        Assert.Equal(code, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_NormalizesWikiBeforeUsingIt()
    {
        var source = new Source(); var writer = new StringWriter();

        int exit = await App(new Store(), new Pat(), new Factory(source), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", " Wiki ", "--path", "/P", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Wiki", source.LastReference!.Wiki);
    }

    [Fact]
    public async Task InvokeAsync_ResolvesTypelessDirectPageVersionFromAdvertisedTag()
    {
        var source = new Source(); var writer = new StringWriter();

        int exit = await App(new Store(), new Pat(), new Factory(source), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", "Wiki", "--path", "/P", "--version", "v1.0", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("tag", source.LastReference!.Version!.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_RejectsTypelessVersionUnlessItMatchesExactlyOneAdvertisedVersion(bool duplicate)
    {
        IReadOnlyList<SourceWikiVersion> versions = duplicate ? [new("release", "branch"), new("release", "tag")] : [new("main", "branch")];
        var source = new Source(versions: versions); var writer = new StringWriter();

        int exit = await App(new Store(), new Pat(), new Factory(source), writer).InvokeAsync(["wiki-page", "--project", "Project", "--wiki", "Wiki", "--path", "/P", "--version", "release", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalidWikiReference", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, source.GetPageCalls);
    }
    private static CommandApplication App(IConfigurationStore store, Pat pat, Factory factory, StringWriter writer) => new CommandFactory(store, new JsonOutput(writer), TimeProvider.System, pat, factory).Create();
    private sealed class Store(int seconds = AdoReadConfiguration.DefaultRequestTimeoutSeconds, string? defaultProject = null) : IConfigurationStore { public int Loads { get; private set; } public string DefaultPath => "x"; public Task<AdoReadConfiguration> LoadAsync(string? p, CancellationToken t) { Loads++; return Task.FromResult(AdoReadConfiguration.Create(new Uri("https://dev.azure.com/example"), seconds, AdoReadConfiguration.DefaultMaxParallelProjects, defaultProject)); } public Task SaveAsync(string? p, AdoReadConfiguration c, bool o, CancellationToken t) => throw new NotSupportedException(); }
    private sealed class MissingStore : IConfigurationStore { public string DefaultPath => "x"; public Task<AdoReadConfiguration> LoadAsync(string? p, CancellationToken t) => throw new ConfigurationException("configurationNotFound", "Configuration file was not found at x."); public Task SaveAsync(string? p, AdoReadConfiguration c, bool o, CancellationToken t) => throw new NotSupportedException(); }
    private sealed class Pat : IPatProvider { public int Reads { get; private set; } public string GetRequiredPat() { Reads++; return "pat"; } }
    private sealed class Factory(Source source) : IAdoSessionFactory { public int Calls { get; private set; } public IAdoSession Create(Uri u, string p) { Calls++; return new Session(source); } }
    private sealed class Session(IWikiSource source) : IAdoSession { public IWikiSource Wikis { get; } = source; public IProjectCatalog Projects => throw new NotSupportedException(); public IPullRequestSource PullRequests => throw new NotSupportedException(); public IWorkItemSource WorkItems => throw new NotSupportedException(); public ISprintSource Sprints => throw new NotSupportedException(); public ITeamClient Teams => throw new NotSupportedException(); public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class Source(string mode = "success", IReadOnlyList<SourceWikiVersion>? versions = null) : IWikiSource
    {
        public WikiReference? LastReference { get; private set; }
        public int GetPageCalls { get; private set; }
        public async Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken token) { GetPageCalls++; LastReference = reference; if (mode == "delay") await Task.Delay(Timeout.InfiniteTimeSpan, token); return mode == "404" ? null : mode == "error" ? throw new WikiSourceException("unauthorized", "safe", false) : new(1, "/P", "/P.md", "# P", null, null); }
        public Task<IReadOnlyList<SourceWiki>> GetWikisAsync(string project, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SourceWiki>>([new(Guid.NewGuid(), "Wiki", "codeWiki", Guid.NewGuid(), Guid.NewGuid(), "/", versions ?? [new("main", "branch"), new("v1.0", "tag")], null, null)]);
    }
}
