using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Pipelines;
using AdoRead.Privacy;
using AdoRead.PullRequests;
using AdoRead.UnitTests.Privacy;

namespace AdoRead.UnitTests.Pipelines;

public sealed class PipelineReaderTests
{
    private static readonly Uri Organization = new("https://dev.azure.com/example/");
    private static readonly AdoProject Payments = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Payments", null);
    private static readonly DateTimeOffset Start = new(2026, 7, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadPullRequestAsync_ReportsTheLatestBuildPerDefinitionForTheMergeCommit()
    {
        var pipelines = new FakePipelineSource
        {
            PullRequestBuilds =
            [
                Build(300, definitionId: 10, sourceVersion: "merge-1", Start.AddHours(2)),
                Build(299, definitionId: 10, sourceVersion: "merge-1", Start.AddHours(1)),
                Build(298, definitionId: 11, sourceVersion: "old-merge", Start.AddHours(3)),
                Build(297, definitionId: 12, sourceVersion: "merge-1", Start)
            ]
        };

        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(pipelines, Privacy(PrivacyLevel.Full));

        Assert.Equal("success", envelope.Status);
        PullRequestPipelinesDocument pullRequest = Assert.Single(envelope.Results);
        Assert.Equal("mergeCommit", pullRequest.HeadMatch);
        Assert.Equal(new[] { 300, 297 }, pullRequest.Runs.Select(run => run.BuildId).ToArray());
    }

    [Fact]
    public async Task ReadPullRequestAsync_WithoutAMergeCommitMatchesByQueueOrder()
    {
        var pipelines = new FakePipelineSource
        {
            PullRequestBuilds =
            [
                Build(300, definitionId: 10, sourceVersion: "any-1", Start.AddHours(2)),
                Build(299, definitionId: 10, sourceVersion: "any-2", Start.AddHours(1))
            ]
        };

        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(
            pipelines,
            Privacy(PrivacyLevel.Full),
            mergeCommitId: null);

        PullRequestPipelinesDocument pullRequest = Assert.Single(envelope.Results);
        Assert.Equal("latestQueued", pullRequest.HeadMatch);
        Assert.Equal(new[] { 300 }, pullRequest.Runs.Select(run => run.BuildId).ToArray());
    }

    [Fact]
    public async Task ReadPullRequestAsync_WithholdingLogsNeverDownloadsThem()
    {
        var pipelines = FailingPipelines();

        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(pipelines, Privacy(PrivacyLevel.Standard));

        Assert.Equal("success", envelope.Status);
        Assert.DoesNotContain(pipelines.Calls, call => call.StartsWith("logs:", StringComparison.Ordinal));
        Assert.DoesNotContain(pipelines.Calls, call => call.StartsWith("logLines:", StringComparison.Ordinal));
        Assert.Null(Assert.Single(Assert.Single(envelope.Results).Runs).LogTails);
    }

    [Fact]
    public async Task ReadPullRequestAsync_AllowingLogsDownloadsTheLastLinesOfFailedTasks()
    {
        var pipelines = FailingPipelines();

        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(pipelines, Privacy(PrivacyLevel.Full), logLines: 2);

        PipelineLogTailDocument tail = Assert.Single(Assert.Single(Assert.Single(envelope.Results).Runs).LogTails!);
        Assert.Equal(new[] { "error CS0246", "Build failed" }, tail.Lines.ToArray());
        Assert.Equal(3, tail.FirstLine);
        Assert.Contains("logLines:300:501:3-4", pipelines.Calls);
    }

    [Fact]
    public async Task ReadPullRequestAsync_OneRunThatCannotReadItsTestsIsPartialWithAnError()
    {
        var pipelines = FailingPipelines();
        pipelines.UnreadableTestRuns.Add(7001);

        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(pipelines, Privacy(PrivacyLevel.Full));

        Assert.Equal("partial", envelope.Status);
        Assert.Single(envelope.Results);
        SearchError error = Assert.Single(envelope.Errors);
        Assert.Equal("build", error.Scope);
        Assert.Equal("forbidden", error.Code);
        Assert.Contains("Build 300 failed tests could not be read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadPullRequestAsync_PullRequestWithoutBuildsSucceedsWithNoRuns()
    {
        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(
            new FakePipelineSource(),
            Privacy(PrivacyLevel.Full));

        Assert.Equal("success", envelope.Status);
        Assert.Empty(Assert.Single(envelope.Results).Runs);
    }

    [Fact]
    public async Task ReadPullRequestAsync_UnknownPullRequestIsNotFound()
    {
        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(
            new FakePipelineSource(),
            Privacy(PrivacyLevel.Full),
            pullRequestExists: false);

        Assert.Equal("notFound", envelope.Status);
        Assert.Empty(envelope.Results);
        Assert.Empty(envelope.Errors);
    }

    [Fact]
    public async Task ReadPullRequestAsync_RepositoryFilterThatDoesNotMatchIsNotFoundWithAReason()
    {
        SearchEnvelope<PullRequestPipelinesDocument> envelope = await ReadPullRequestAsync(
            new FakePipelineSource(),
            Privacy(PrivacyLevel.Full),
            repository: "Other");

        Assert.Equal("notFound", envelope.Status);
        Assert.Equal("pullRequestNotInRepository", Assert.Single(envelope.Errors).Code);
    }

    [Fact]
    public async Task ReadPullRequestAsync_UnknownProjectIsNotFoundWithAReason()
    {
        var reader = new PipelineReader(
            new FakeProjectCatalog([]),
            new FakePullRequestSource(TestPullRequests.Create(Payments, 42)),
            new FakePipelineSource(),
            Organization,
            TimeProvider.System,
            Privacy(PrivacyLevel.Full));

        SearchEnvelope<PullRequestPipelinesDocument> envelope = await reader.ReadPullRequestAsync(
            new PullRequestPipelinesQuery(42, Organization, "Missing", null, 50),
            CancellationToken.None);

        Assert.Equal("notFound", envelope.Status);
        Assert.Equal("projectNotFound", Assert.Single(envelope.Errors).Code);
    }

    [Fact]
    public async Task ReadPullRequestAsync_ScansAtMostTwoHundredBuilds()
    {
        var pipelines = new FakePipelineSource();

        await ReadPullRequestAsync(pipelines, Privacy(PrivacyLevel.Full));

        Assert.Contains("prBuilds:42:200", pipelines.Calls);
    }

    [Fact]
    public async Task ReadBuildAsync_MissingBuildIsNotFound()
    {
        var reader = Reader(new FakePipelineSource(), Privacy(PrivacyLevel.Full));

        SearchEnvelope<PipelineRunDocument> envelope = await reader.ReadBuildAsync(
            new PipelineRunQuery(404, Organization, "Payments", 50),
            CancellationToken.None);

        Assert.Equal("notFound", envelope.Status);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task ReadBuildAsync_DiagnosesTheBuildAndLinksItsResultsPage()
    {
        var pipelines = FailingPipelines();
        var reader = Reader(pipelines, Privacy(PrivacyLevel.Full));

        SearchEnvelope<PipelineRunDocument> envelope = await reader.ReadBuildAsync(
            new PipelineRunQuery(300, Organization, "Payments", 50),
            CancellationToken.None);

        Assert.Equal("success", envelope.Status);
        PipelineRunDocument run = Assert.Single(envelope.Results);
        Assert.Equal(new Uri("https://dev.azure.com/example/Payments/_build/results?buildId=300"), run.BrowserUrl);
    }

    [Fact]
    public async Task ReadTestsAsync_FailedOutcomeReturnsOnlyFailedResultsUpToTheTop()
    {
        var pipelines = FailingPipelines();
        var reader = Reader(pipelines, Privacy(PrivacyLevel.Full));

        SearchEnvelope<PipelineTestResultDocument> envelope = await reader.ReadTestsAsync(
            new PipelineTestsQuery(300, Organization, "Payments", "failed", 1),
            CancellationToken.None);

        Assert.Equal("success", envelope.Status);
        PipelineTestResultDocument test = Assert.Single(envelope.Results);
        Assert.Equal("Failed", test.Outcome);
        Assert.Contains((7001, true, 1), pipelines.TestResultRequests);
    }

    [Fact]
    public async Task ReadTestsAsync_AllOutcomeIncludesPassedResults()
    {
        var pipelines = FailingPipelines();
        var reader = Reader(pipelines, Privacy(PrivacyLevel.Full));

        SearchEnvelope<PipelineTestResultDocument> envelope = await reader.ReadTestsAsync(
            new PipelineTestsQuery(300, Organization, "Payments", "all", 10),
            CancellationToken.None);

        Assert.Equal(new[] { "Failed", "Failed", "Passed" }, envelope.Results.Select(test => test.Outcome).ToArray());
        Assert.Contains((7001, false, 10), pipelines.TestResultRequests);
    }

    [Fact]
    public async Task ReadTestsAsync_StandardPrivacyKeepsMessagesAndStackTraces()
    {
        var reader = Reader(FailingPipelines(), Privacy(PrivacyLevel.Standard));

        SearchEnvelope<PipelineTestResultDocument> envelope = await reader.ReadTestsAsync(
            new PipelineTestsQuery(300, Organization, "Payments", "failed", 10),
            CancellationToken.None);

        PipelineTestResultDocument test = envelope.Results[0];
        Assert.Equal("Expected 5 but was 3 password=***", test.ErrorMessage);
        Assert.NotNull(test.StackTrace);
        Assert.Empty(envelope.Privacy!.Withheld);
    }

    private static FakePipelineSource FailingPipelines()
    {
        var pipelines = new FakePipelineSource
        {
            PullRequestBuilds = [Build(300, definitionId: 10, sourceVersion: "merge-1", Start.AddHours(1))],
            Builds = { [300] = Build(300, definitionId: 10, sourceVersion: "merge-1", Start.AddHours(1)) },
            Timelines =
            {
                [300] = new SourceTimeline(
                [
                    new SourceTimelineRecord(
                        Guid.Parse("00000000-0000-0000-0000-000000000501"),
                        "Task",
                        "Compile",
                        "failed",
                        1,
                        1,
                        0,
                        501,
                        "agent-01",
                        [new SourceIssue("error", "DotNetCoreCLI", "CS0246", new Dictionary<string, string>())])
                ])
            },
            Logs = { [300] = [new SourceBuildLog(501, 4)] },
            LogLines = { [501] = ["Compiling", "warning CS1591", "error CS0246", "Build failed"] },
            TestRuns = { [300] = [new SourceTestRun(7001, "Unit", 3, 1, 0, 0, 2)] },
            TestResults =
            {
                [7001] =
                [
                    new SourceTestResult(
                        7001,
                        "Retries once",
                        "Fabricated.RetryTests.RetriesOnce",
                        "Fabricated.Tests.dll",
                        12,
                        "Failed",
                        "New Issue",
                        null,
                        "Expected 5 but was 3 password=hunter2",
                        "at Fabricated.RetryTests.RetriesOnce()"),
                    new SourceTestResult(7001, "Second", "Fabricated.Second", "Fabricated.Tests.dll", 3, "Failed", null, null, "x", null),
                    new SourceTestResult(7001, "Passing", "Fabricated.Passing", "Fabricated.Tests.dll", 1, "Passed", null, null, null, null)
                ]
            }
        };
        return pipelines;
    }

    private static async Task<SearchEnvelope<PullRequestPipelinesDocument>> ReadPullRequestAsync(
        FakePipelineSource pipelines,
        EffectivePrivacy privacy,
        string? mergeCommitId = "merge-1",
        bool pullRequestExists = true,
        string? repository = null,
        int logLines = 50)
    {
        PullRequestDocument? pullRequest = pullRequestExists
            ? TestPullRequests.Create(Payments, 42) with { MergeCommitId = mergeCommitId }
            : null;
        var reader = new PipelineReader(
            new FakeProjectCatalog([Payments]),
            new FakePullRequestSource(pullRequest),
            pipelines,
            Organization,
            TimeProvider.System,
            privacy);
        return await reader.ReadPullRequestAsync(
            new PullRequestPipelinesQuery(42, Organization, "Payments", repository, logLines),
            CancellationToken.None);
    }

    private static PipelineReader Reader(FakePipelineSource pipelines, EffectivePrivacy privacy) => new(
        new FakeProjectCatalog([Payments]),
        new FakePullRequestSource(TestPullRequests.Create(Payments, 42)),
        pipelines,
        Organization,
        TimeProvider.System,
        privacy);

    private static EffectivePrivacy Privacy(PrivacyLevel level) => TestPrivacy.For(new PrivacyPolicy(level));

    private static SourceBuild Build(int id, int definitionId, string sourceVersion, DateTimeOffset queued) => new(
        id,
        $"20260709.{id}",
        definitionId,
        $"Definition {definitionId}",
        "completed",
        "failed",
        "refs/pull/42/merge",
        sourceVersion,
        queued,
        queued.AddSeconds(20),
        queued.AddMinutes(5),
        "engineer@example.invalid",
        "engineer@example.invalid");

    private sealed class FakeProjectCatalog(IReadOnlyList<AdoProject> projects) : IProjectCatalog
    {
        public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(projects);
    }

    private sealed class FakePullRequestSource(PullRequestDocument? pullRequest) : IPullRequestSource
    {
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public Task<PullRequestDocument?> TryGetAsync(AdoProject project, int id, CancellationToken cancellationToken) =>
            Task.FromResult(pullRequest);

        public Task<PullRequestDocument> GetAllDetailsAsync(PullRequestDocument pullRequest, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
