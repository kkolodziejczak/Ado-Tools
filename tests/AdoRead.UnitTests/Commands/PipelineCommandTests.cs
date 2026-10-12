using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Pipelines;
using AdoRead.PullRequests;
using AdoRead.UnitTests.Credentials;
using AdoRead.UnitTests.Privacy;
using AdoRead.UnitTests.Pipelines;

namespace AdoRead.UnitTests.Commands;

public sealed class PipelineCommandTests
{
    private static readonly AdoProject Payments = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Payments", null);

    [Theory]
    [InlineData("nope", "invalidPullRequestId")]
    [InlineData("0", "invalidPullRequestId")]
    public async Task Pr_InvalidReferenceExitsTwoWithoutOpeningASession(string reference, string code)
    {
        var (exitCode, output, factory) = await InvokeAsync(["pipelines", "pr", reference, "--project", "Payments", "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(code, FirstError(output).GetProperty("code").GetString());
        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public async Task Pr_UrlFromAnotherOrganizationIsRejectedBeforeASession()
    {
        var (exitCode, output, factory) = await InvokeAsync(
            ["pipelines", "pr", "https://dev.azure.com/fabricated/Payments/_git/Repo/pullrequest/42", "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("organizationMismatch", FirstError(output).GetProperty("code").GetString());
        Assert.Equal(0, factory.Created);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    public async Task Pr_LogLinesOutsideOneToFiveHundredAreRejected(string logLines)
    {
        var (exitCode, output, _) = await InvokeAsync(
            ["pipelines", "pr", "42", "--project", "Payments", "--log-lines", logLines, "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidLogLines", FirstError(output).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Pr_UrlProjectThatDiffersFromTheProjectOptionIsRejected()
    {
        var (exitCode, output, factory) = await InvokeAsync(
            ["pipelines", "pr", "https://dev.azure.com/example/Payments/_git/Repo/pullrequest/42", "--project", "Other", "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("projectMismatch", FirstError(output).GetProperty("code").GetString());
        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public async Task Run_BuildReferenceWithoutABuildIdIsRejected()
    {
        var (exitCode, output, _) = await InvokeAsync(
            ["pipelines", "run", "https://dev.azure.com/example/Payments/_build/results", "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidBuildId", FirstError(output).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Run_BuildUrlSuppliesTheProjectAndTheBuildId()
    {
        var pipelines = new FakePipelineSource
        {
            Builds = { [300] = Build(300) }
        };

        var (exitCode, output, factory) = await InvokeAsync(
            ["pipelines", "run", "https://dev.azure.com/example/Payments/_build/results?buildId=300", "--compact"],
            pipelines);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(1, factory.Created);
        Assert.Equal("pipelineRun", output.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Payments", output.RootElement.GetProperty("query").GetProperty("project").GetString());
        Assert.Contains("build:300", pipelines.Calls);
    }

    [Theory]
    [InlineData("some")]
    [InlineData("passed")]
    public async Task Tests_OutcomeOtherThanFailedOrAllIsRejected(string outcome)
    {
        var (exitCode, output, _) = await InvokeAsync(
            ["pipelines", "tests", "300", "--project", "Payments", "--outcome", outcome, "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidOutcome", FirstError(output).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    public async Task Tests_TopOutsideOneToAThousandIsRejected(string top)
    {
        var (exitCode, output, _) = await InvokeAsync(
            ["pipelines", "tests", "300", "--project", "Payments", "--top", top, "--compact"]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidTop", FirstError(output).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Run_WithoutAStoredPolicyRefusesBeforeOpeningASession()
    {
        var (exitCode, output, factory) = await InvokeAsync(
            ["pipelines", "run", "300", "--project", "Payments", "--compact"],
            vault: new MemoryCredentialVault());

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("privacyPolicyRequired", FirstError(output).GetProperty("code").GetString());
        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public async Task Run_StandardPrivacyDoesNotDownloadLogs()
    {
        var pipelines = new FakePipelineSource
        {
            Builds = { [300] = Build(300) },
            Timelines =
            {
                [300] = new SourceTimeline(
                [
                    new SourceTimelineRecord(Guid.NewGuid(), "Task", "Compile", "failed", 1, 1, 0, 501, "agent-01", [])
                ])
            },
            Logs = { [300] = [new SourceBuildLog(501, 4)] }
        };

        var (exitCode, _, _) = await InvokeAsync(
            ["pipelines", "run", "300", "--project", "Payments", "--privacy", "standard", "--compact"],
            pipelines);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.DoesNotContain(pipelines.Calls, call => call.StartsWith("logs:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tests_OneUnreadableRunIsPartialWhileTheOtherRunStillReports()
    {
        var pipelines = new FakePipelineSource
        {
            Builds = { [300] = Build(300) },
            TestRuns = { [300] = [new SourceTestRun(7001, "Unit", 1, 0, 0, 0, 1), new SourceTestRun(7002, "Other", 1, 0, 0, 0, 1)] },
            TestResults = { [7002] = [new SourceTestResult(7002, "Fine", "Fabricated.Fine", null, 1, "Failed", null, null, "boom", null)] }
        };
        pipelines.UnreadableTestRuns.Add(7001);

        var (exitCode, output, _) = await InvokeAsync(
            ["pipelines", "tests", "300", "--project", "Payments", "--compact"],
            pipelines);

        Assert.Equal(ExitCodes.Partial, exitCode);
        Assert.Equal("partial", output.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, output.RootElement.GetProperty("results").GetArrayLength());
    }

    private static async Task<(int ExitCode, JsonDocument Output, RecordingSessionFactory Factory)> InvokeAsync(
        string[] arguments,
        FakePipelineSource? pipelines = null,
        ICredentialVault? vault = null)
    {
        var writer = new StringWriter();
        var factory = new RecordingSessionFactory(new PipelineSession(pipelines ?? new FakePipelineSource()));
        CommandApplication application = TestTool.Umbrella(
            new ConfiguredStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            new FakePatProvider(),
            factory,
            vault ?? TestPrivacy.ConsentedVault());
        int exitCode = await application.InvokeAsync([.. arguments], CancellationToken.None);
        return (exitCode, JsonDocument.Parse(writer.ToString()), factory);
    }

    private static JsonElement FirstError(JsonDocument output) => output.RootElement.GetProperty("errors")[0];

    private static SourceBuild Build(int id) => new(
        id,
        $"20260709.{id}",
        10,
        "Payments CI",
        "completed",
        "failed",
        "refs/pull/42/merge",
        "merge-1",
        new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero),
        null,
        null,
        "engineer@example.invalid",
        "engineer@example.invalid");

    private sealed class ConfiguredStore : IConfigurationStore
    {
        public string DefaultPath => "unused";

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(
                new Uri("https://dev.azure.com/example"),
                AdoReadConfiguration.DefaultRequestTimeoutSeconds,
                AdoReadConfiguration.DefaultMaxParallelProjects));

        public Task SaveAsync(string? overridePath, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePatProvider : IPatProvider
    {
        public string GetRequiredPat(Uri organizationUrl) => "test-pat";
    }

    private sealed class RecordingSessionFactory(IAdoSession session) : IAdoSessionFactory
    {
        public int Created { get; private set; }

        public IAdoSession Create(Uri organizationUrl, string pat)
        {
            Created++;
            return session;
        }
    }

    private sealed class PipelineSession(IPipelineSource pipelines) : IAdoSession
    {
        public IProjectCatalog Projects { get; } = new FakeProjectCatalog([Payments]);

        public IPullRequestSource PullRequests { get; } = new FakePullRequestSource(TestPullRequests.Create(Payments, 42) with { MergeCommitId = "merge-1" });

        public IPipelineSource Pipelines { get; } = pipelines;

        public IWorkItemSource WorkItems => throw new NotSupportedException();

        public ISprintSource Sprints => throw new NotSupportedException();

        public ITeamClient Teams => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeProjectCatalog(IReadOnlyList<AdoProject> projects) : IProjectCatalog
    {
        public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(projects);
    }

    private sealed class FakePullRequestSource(PullRequestDocument pullRequest) : IPullRequestSource
    {
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public Task<PullRequestDocument?> TryGetAsync(AdoProject project, int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(pullRequest);

        public Task<PullRequestDocument> GetAllDetailsAsync(PullRequestDocument document, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
