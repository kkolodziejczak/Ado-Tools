using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.PullRequests;

namespace AdoRead.UnitTests.Commands;

public sealed class PullRequestCommandTests
{
    [Fact]
    public async Task InvokeAsync_WiresConfiguredFanOutAndReturnsPartialExitCode()
    {
        var writer = new StringWriter();
        var pullRequests = new FakePullRequestSource();
        var session = new FakeAdoSession(
            new FakeProjectCatalog(
            [
                new AdoProject(Guid.NewGuid(), "Match", null),
                new AdoProject(Guid.NewGuid(), "Broken", null)
            ]),
            pullRequests);
        var factory = new FakeAdoSessionFactory(session);
        CommandApplication application = CreateApplication(writer, factory);

        int exitCode = await application.InvokeAsync(["pr", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Partial, exitCode);
        using JsonDocument output = JsonDocument.Parse(writer.ToString());
        Assert.Equal("pullRequestSearch", output.RootElement.GetProperty("kind").GetString());
        Assert.Equal("partial", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("Broken", output.RootElement.GetProperty("errors")[0].GetProperty("project").GetString());
        Assert.Equal("test-pat", factory.Pat);
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData("https://dev.azure.com/example/Payments/_git/Payments.Api/pullrequest/42")]
    [InlineData("https://dev.azure.com/example/Payments/_apis/git/repositories/repo/pullRequests/42?api-version=7.1")]
    [InlineData("https://example.visualstudio.com/Payments/_git/Payments.Api/pullrequest/42")]
    public async Task InvokeAsync_AcceptsPullRequestUrl(string reference)
    {
        var writer = new StringWriter();
        var pullRequests = new FakePullRequestSource();
        var factory = new FakeAdoSessionFactory(
            new FakeAdoSession(
                new FakeProjectCatalog([new AdoProject(Guid.NewGuid(), "Match", null)]),
                pullRequests));
        CommandApplication application = CreateApplication(writer, factory);

        int exitCode = await application.InvokeAsync(["pr", reference, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([42], pullRequests.RequestedIds);
        using JsonDocument output = JsonDocument.Parse(writer.ToString());
        Assert.Equal(42, output.RootElement.GetProperty("query").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_RejectsPullRequestUrlFromDifferentOrganizationBeforeCreatingSession()
    {
        var writer = new StringWriter();
        var factory = new FakeAdoSessionFactory(
            new FakeAdoSession(new FakeProjectCatalog([]), new FakePullRequestSource()));
        CommandApplication application = CreateApplication(writer, factory);

        int exitCode = await application.InvokeAsync(
            ["pr", "https://dev.azure.com/fabricated/Payments/_git/Repo/pullrequest/42", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument output = JsonDocument.Parse(writer.ToString());
        Assert.Equal("organizationMismatch", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(factory.Pat);
    }

    [Theory]
    [InlineData("https://dev.azure.com/example/Payments/_workitems/edit/42")]
    [InlineData("http://dev.azure.com/example/Payments/_git/Repo/pullrequest/42")]
    [InlineData("https://example.com/example/Payments/_git/Repo/pullrequest/42")]
    public async Task InvokeAsync_RejectsInvalidPullRequestUrlBeforeCreatingSession(string reference)
    {
        var writer = new StringWriter();
        var factory = new FakeAdoSessionFactory(
            new FakeAdoSession(new FakeProjectCatalog([]), new FakePullRequestSource()));
        CommandApplication application = CreateApplication(writer, factory);

        int exitCode = await application.InvokeAsync(
            ["pr", reference, "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument output = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalidPullRequestId", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(factory.Pat);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonPositiveIdBeforeCreatingSession()
    {
        var writer = new StringWriter();
        var factory = new FakeAdoSessionFactory(
            new FakeAdoSession(new FakeProjectCatalog([]), new FakePullRequestSource()));
        CommandApplication application = CreateApplication(writer, factory);

        int exitCode = await application.InvokeAsync(["pr", "0", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument output = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalidPullRequestId", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(factory.Pat);
    }

    [Fact]
    public async Task InvokeAsync_TimeoutPreservesCompletedMatchesAsPartial()
    {
        var writer = new StringWriter();
        var session = new FakeAdoSession(
            new FakeProjectCatalog(
            [
                new AdoProject(Guid.NewGuid(), "Fast", null),
                new AdoProject(Guid.NewGuid(), "Slow", null)
            ]),
            new TimeoutPullRequestSource());
        var factory = new FakeAdoSessionFactory(session);
        CommandApplication application = TestTool.Umbrella(
            new FakeConfigurationStore(requestTimeoutSeconds: 1),
            new JsonOutput(writer),
            TimeProvider.System,
            new FakePatProvider(),
            factory);

        int exitCode = await application.InvokeAsync(["pr", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Partial, exitCode);
        using JsonDocument output = JsonDocument.Parse(writer.ToString());
        Assert.Equal("partial", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("Fast", output.RootElement.GetProperty("results")[0].GetProperty("project").GetProperty("name").GetString());
        Assert.Equal("timeout", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    private static CommandApplication CreateApplication(
        StringWriter writer,
        IAdoSessionFactory sessionFactory) =>
        TestTool.Umbrella(
            new FakeConfigurationStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            new FakePatProvider(),
            sessionFactory);

    private sealed class FakeConfigurationStore(
        int requestTimeoutSeconds = AdoReadConfiguration.DefaultRequestTimeoutSeconds) : IConfigurationStore
    {
        public string DefaultPath => "unused";

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(
                new Uri("https://dev.azure.com/example"),
                requestTimeoutSeconds,
                maxParallelProjects: 2));

        public Task SaveAsync(
            string? overridePath,
            AdoReadConfiguration configuration,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePatProvider : IPatProvider
    {
        public string GetRequiredPat() => "test-pat";
    }

    private sealed class FakeAdoSessionFactory(FakeAdoSession session) : IAdoSessionFactory
    {
        public string? Pat { get; private set; }

        public IAdoSession Create(Uri organizationUrl, string pat)
        {
            Pat = pat;
            return session;
        }
    }

    private sealed class FakeAdoSession(
        IProjectCatalog projects,
        IPullRequestSource pullRequests) : IAdoSession
    {
        public IProjectCatalog Projects { get; } = projects;

        public IPullRequestSource PullRequests { get; } = pullRequests;

        public IWorkItemSource WorkItems => throw new NotSupportedException();

        public ISprintSource Sprints => throw new NotSupportedException();

        public ITeamClient Teams => throw new NotSupportedException();

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeProjectCatalog(IReadOnlyList<AdoProject> projects) : IProjectCatalog
    {
        public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(projects);
    }

    private sealed class FakePullRequestSource : IPullRequestSource
    {
        public List<int> RequestedIds { get; } = [];

        // The organization-scoped fast path is not under test here; returning null
        // exercises the project scan.
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken)
        {
            RequestedIds.Add(id);
            if (project.Name == "Broken")
            {
                return Task.FromException<PullRequestDocument?>(
                    new PullRequestSourceException("forbidden", "Cannot read this project.", false));
            }

            return Task.FromResult<PullRequestDocument?>(TestPullRequests.Create(project, id));
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken) => Task.FromResult(pullRequest);
    }

    private sealed class TimeoutPullRequestSource : IPullRequestSource
    {
        // The organization-scoped fast path is not under test here; returning null
        // exercises the project scan.
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public async Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken)
        {
            if (project.Name == "Slow")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return TestPullRequests.Create(project, id);
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken) => Task.FromResult(pullRequest);
    }
}
