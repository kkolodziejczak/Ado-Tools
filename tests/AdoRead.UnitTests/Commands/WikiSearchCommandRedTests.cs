using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Commands;

public sealed class WikiSearchCommandRedTests
{
    [Fact]
    public async Task WikiSearch_RejectsTopAboveTwentyFiveWithItsOwnV2Envelope()
    {
        var writer = new StringWriter();
        var application = TestTool.Umbrella(new Store(), new JsonOutput(writer), TimeProvider.System);

        int exit = await application.InvokeAsync(["wiki", "search", "authentication", "--top", "26", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wikiFullTextSearch", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("2.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("invalidTop", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task WikiSearch_ReportsInvalidSkipWhenTopIsAlsoExplicit()
    {
        var writer = new StringWriter();

        int exit = await App(new Store(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki", "search", "authentication", "--skip", "-1", "--top", "5", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        AssertError(writer, "command", "invalidSkip", "The Wiki search offset cannot be negative.", transient: false);
    }

    [Fact]
    public async Task WikiSearch_MapsMissingPatToAuthenticationError()
    {
        var writer = new StringWriter();

        int exit = await App(new Store(), new MissingPat(), new Factory(new Source()), writer).InvokeAsync(["wiki", "search", "authentication", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exit);
        AssertError(writer, "authentication", "missingPat", "Environment variable ADO_READ_PAT is required.", transient: false);
    }

    [Fact]
    public async Task WikiSearch_PreservesConfigurationErrorMessageAndExitCode()
    {
        var writer = new StringWriter();

        int exit = await App(new MissingStore(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki", "search", "authentication", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        AssertError(writer, "configuration", "configurationNotFound", "Configuration file was not found at x.", transient: false);
    }

    [Fact]
    public async Task WikiSearch_MapsWholeCommandTimeoutToAzureDevOpsError()
    {
        var writer = new StringWriter();

        int exit = await App(new Store(timeoutSeconds: 1), new Pat(), new Factory(new Source(delay: true)), writer).InvokeAsync(["wiki", "search", "authentication", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exit);
        AssertError(writer, "azureDevOps", "timeout", "The Azure DevOps request timed out.", transient: true);
    }

    [Fact]
    public async Task WikiSearch_RejectsUndocumentedWikiFilterLocally()
    {
        var writer = new StringWriter();

        int exit = await App(new Store(), new Pat(), new Factory(new Source()), writer).InvokeAsync(["wiki", "search", "authentication", "--wiki", "Wiki", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        AssertError(writer, "command", "unsupportedWikiFilter", "Azure DevOps does not document a Wiki-name search filter.", transient: false);
    }

    [Fact]
    public async Task WikiSearch_UsesDocumentedDefaultWindow()
    {
        var writer = new StringWriter();
        var source = new Source();

        int exit = await App(new Store(), new Pat(), new Factory(source), writer).InvokeAsync(["wiki", "search", "authentication", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.NotFound, exit);
        Assert.Equal(0, source.LastSkip);
        Assert.Equal(10, source.LastTop);
    }

    [Theory]
    [InlineData("Explicit Project", "Configured Project", false, "Explicit Project")]
    [InlineData(null, "Configured Project", false, "Configured Project")]
    [InlineData(null, "Configured Project", true, null)]
    public async Task WikiSearch_UsesExplicitThenConfiguredProjectUnlessAllProjectsIsRequested(string? explicitProject, string? defaultProject, bool allProjects, string? expectedProject)
    {
        var writer = new StringWriter();
        var source = new Source();
        var arguments = new List<string> { "wiki", "search", "authentication" };
        if (explicitProject is not null) { arguments.Add("--project"); arguments.Add(explicitProject); }
        if (allProjects) arguments.Add("--all-projects");
        arguments.Add("--compact");

        int exit = await App(new Store(defaultProject: defaultProject), new Pat(), new Factory(source), writer).InvokeAsync(arguments.ToArray(), CancellationToken.None);

        Assert.Equal(ExitCodes.NotFound, exit);
        Assert.Equal(expectedProject, source.LastProject);
    }

    [Fact]
    public async Task WikiSearch_RejectsExplicitlyBlankProject()
    {
        var writer = new StringWriter();
        var source = new Source();
        var arguments = new List<string> { "wiki", "search", "authentication", "--project", "   " };
        arguments.Add("--compact");

        int exit = await App(new Store(defaultProject: "Configured Project"), new Pat(), new Factory(source), writer).InvokeAsync(arguments.ToArray(), CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        AssertError(writer, "command", "projectRequired", "The explicit project cannot be blank.", transient: false);
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData("Project/Child")]
    [InlineData(" . ")]
    public async Task WikiSearch_RejectsUnsafeProjectAsInvalidCommandInput(string project)
    {
        var writer = new StringWriter();
        var source = new Source();

        int exit = await App(new Store(), new Pat(), new Factory(source), writer).InvokeAsync(["wiki", "search", "authentication", "--project", project, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        AssertError(writer, "command", "invalidProject", "The Azure DevOps project identifier is invalid.", transient: false);
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("Project/Child")]
    public async Task WikiSearch_ReportsAllProjectsAndProjectAsMutuallyExclusiveBeforeValidatingProject(string project)
    {
        var writer = new StringWriter();
        var source = new Source();

        int exit = await App(new Store(), new Pat(), new Factory(source), writer).InvokeAsync(["wiki", "search", "authentication", "--all-projects", "--project", project, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exit);
        AssertError(writer, "command", "invalidWikiSearch", "The Wiki search request is invalid.", transient: false);
        Assert.Equal(0, source.Calls);
    }

    private static void AssertError(StringWriter writer, string scope, string code, string message, bool transient)
    {
        using JsonDocument json = JsonDocument.Parse(writer.ToString());
        JsonElement error = json.RootElement.GetProperty("errors")[0];
        Assert.Equal("2.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(scope, error.GetProperty("scope").GetString());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
        Assert.Equal(transient, error.GetProperty("transient").GetBoolean());
    }

    private static CommandApplication App(IConfigurationStore store, IPatProvider pat, IAdoSessionFactory factory, StringWriter writer) =>
        TestTool.Umbrella(store, new JsonOutput(writer), TimeProvider.System, pat, factory);

    private sealed class Store(int timeoutSeconds = AdoReadConfiguration.DefaultRequestTimeoutSeconds, string? defaultProject = null) : IConfigurationStore
    {
        public string DefaultPath => "x";
        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(new Uri("https://dev.azure.com/fabricated/"), timeoutSeconds, AdoReadConfiguration.DefaultMaxParallelProjects, defaultProject));
        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MissingStore : IConfigurationStore
    {
        public string DefaultPath => "x";
        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) => throw new ConfigurationException("configurationNotFound", "Configuration file was not found at x.");
        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Pat : IPatProvider { public string GetRequiredPat() => "pat"; }
    private sealed class MissingPat : IPatProvider { public string GetRequiredPat() => throw new CredentialException("missingPat", "Environment variable ADO_READ_PAT is required."); }
    private sealed class Factory(IWikiSource source) : IAdoSessionFactory { public IAdoSession Create(Uri organizationUrl, string pat) => new Session(source); }
    private sealed class Session(IWikiSource source) : IAdoSession
    {
        public IWikiSource Wikis { get; } = source;
        public IProjectCatalog Projects => throw new NotSupportedException();
        public IPullRequestSource PullRequests => throw new NotSupportedException();
        public IWorkItemSource WorkItems => throw new NotSupportedException();
        public ISprintSource Sprints => throw new NotSupportedException();
        public ITeamClient Teams => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Source(bool delay = false) : IWikiSource
    {
        public int? LastSkip { get; private set; }
        public int? LastTop { get; private set; }
        public string? LastProject { get; private set; }
        public int Calls { get; private set; }
        public Task<SourceWikiPage?> GetPageAsync(WikiReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken)
        {
            Calls++;
            LastSkip = skip;
            LastTop = top;
            LastProject = project;
            if (delay) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new SourceWikiSearchResult([], 0, 0);
        }
    }
}
