using AdoRead.AzureDevOps;

/// <summary>
/// Fabricated pipeline evidence for pull request 126. Build 9001 passes; build 9002 fails with a failed
/// compile task, two failed tests and a log that holds a fake password and a fake JWT. Build 8990 is an
/// older run of the failing definition on another commit, so it must not be reported. Build 8991 fails
/// to read its test results, which exercises the partial path.
/// </summary>
internal sealed class FixturePipelineSource : IPipelineSource
{
    private const string SecretLogLine = "Connection string password=hunter2 was used by the fixture";
    private const string FakeJwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmYWJyaWNhdGVkIn0.ZmFrZS1zaWduYXR1cmUtZm9y";

    private static readonly SourceBuild PassingBuild = new(
        9001,
        "20260709.1",
        301,
        "Fabricated Build",
        "completed",
        "succeeded",
        "refs/pull/126/merge",
        "fabricated-merge-1",
        new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 9, 10, 0, 30, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 9, 10, 5, 0, TimeSpan.Zero),
        "engineer@example.invalid",
        "engineer@example.invalid");

    private static readonly SourceBuild FailingBuild = new(
        9002,
        "20260709.2",
        302,
        "Fabricated Unit Tests",
        "completed",
        "failed",
        "refs/pull/126/merge",
        "fabricated-merge-1",
        new DateTimeOffset(2026, 7, 9, 11, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 9, 11, 0, 20, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 9, 11, 9, 0, TimeSpan.Zero),
        "engineer@example.invalid",
        "engineer@example.invalid");

    private static readonly SourceBuild SupersededBuild = new(
        8990,
        "20260708.4",
        302,
        "Fabricated Unit Tests",
        "completed",
        "failed",
        "refs/pull/126/merge",
        "fabricated-old-merge",
        new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 8, 9, 0, 20, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 8, 9, 8, 0, TimeSpan.Zero),
        "engineer@example.invalid",
        "engineer@example.invalid");

    private static readonly SourceBuild UnreadableBuild = new(
        8991,
        "20260708.5",
        303,
        "Fabricated Integration",
        "completed",
        "failed",
        "refs/heads/main",
        "fabricated-main-1",
        new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 8, 12, 0, 20, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 8, 12, 4, 0, TimeSpan.Zero),
        "engineer@example.invalid",
        "engineer@example.invalid");

    private static readonly Guid StageId = Guid.Parse("00000000-0000-0000-0000-000000000901");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-000000000902");
    private static readonly Guid CompileTaskId = Guid.Parse("00000000-0000-0000-0000-000000000903");
    private static readonly Guid TestTaskId = Guid.Parse("00000000-0000-0000-0000-000000000904");
    private static readonly Guid PublishTaskId = Guid.Parse("00000000-0000-0000-0000-000000000905");

    public Task<IReadOnlyList<SourceBuild>> GetPullRequestBuildsAsync(
        string project,
        int pullRequestId,
        string repositoryId,
        int top,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceBuild>>(
            pullRequestId == 126 && project == "Fabricated.Alpha"
                ? [FailingBuild, PassingBuild, SupersededBuild]
                : []);

    public Task<SourceBuild?> GetBuildAsync(string project, int buildId, CancellationToken cancellationToken) =>
        Task.FromResult(Build(buildId));

    public Task<SourceTimeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken) =>
        Task.FromResult(buildId switch
        {
            9002 => FailingTimeline(),
            8991 => UnreadableTimeline(),
            _ => PassingTimeline()
        });

    public Task<IReadOnlyList<SourceBuildLog>> GetBuildLogsAsync(string project, int buildId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceBuildLog>>(
            buildId switch
            {
                9002 => [new SourceBuildLog(501, 4), new SourceBuildLog(502, 60)],
                8991 => [new SourceBuildLog(601, 3)],
                _ => []
            });

    public Task<IReadOnlyList<string>> GetBuildLogLinesAsync(
        string project,
        int buildId,
        int logId,
        long startLine,
        long endLine,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> lines = LogLines(logId);
        string[] window = lines
            .Skip((int)startLine - 1)
            .Take((int)(endLine - startLine + 1))
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(window);
    }

    public Task<IReadOnlyList<SourceTestRun>> GetTestRunsAsync(string project, int buildId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceTestRun>>(
            buildId switch
            {
                9002 => [new SourceTestRun(7001, "Fabricated.Alpha unit tests", 4, 2, 0, 0, 2)],
                9001 => [new SourceTestRun(7000, "Fabricated.Alpha build tests", 5, 5, 0, 0, 0)],
                8991 => [new SourceTestRun(7991, "Fabricated.Integration tests", 3, 1, 0, 0, 2)],
                _ => []
            });

    public Task<IReadOnlyList<SourceTestResult>> GetTestResultsAsync(
        string project,
        int testRunId,
        bool failedOnly,
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (testRunId == 7991)
        {
            return Task.FromException<IReadOnlyList<SourceTestResult>>(new PipelineSourceException(
                "forbidden",
                "The PAT cannot read this Azure DevOps resource. Pipeline diagnosis needs Build (Read) and Test Management (Read) scopes on the PAT.",
                transient: false));
        }

        IEnumerable<SourceTestResult> results = testRunId switch
        {
            7001 => AllTestResults(),
            7000 => PassingResults(),
            _ => []
        };
        if (failedOnly)
        {
            results = results.Where(result => result.Outcome == "Failed");
        }

        return Task.FromResult<IReadOnlyList<SourceTestResult>>(results.Take(maxCount).ToArray());
    }

    private static SourceBuild? Build(int buildId) => buildId switch
    {
        9001 => PassingBuild,
        9002 => FailingBuild,
        8990 => SupersededBuild,
        8991 => UnreadableBuild,
        _ => null
    };

    private static SourceTimeline PassingTimeline() => new(
    [
        new SourceTimelineRecord(StageId, "Stage", "Build", "succeeded", 1, 0, 0, null, "fabricated-agent-01", []),
        new SourceTimelineRecord(PublishTaskId, "Task", "Publish results", "succeeded", 2, 0, 0, 503, "fabricated-agent-01", [])
    ]);

    private static SourceTimeline FailingTimeline() => new(
    [
        new SourceTimelineRecord(StageId, "Stage", "Build", "failed", 1, 1, 0, null, null, []),
        new SourceTimelineRecord(JobId, "Job", "Unit tests", "failed", 2, 1, 2, null, null, []),
        new SourceTimelineRecord(
            CompileTaskId,
            "Task",
            "Compile solution",
            "failed",
            3,
            1,
            0,
            501,
            "fabricated-agent-01",
            [
                new SourceIssue(
                    "error",
                    "DotNetCoreCLI",
                    "CS0246: The type or namespace name 'Checkout' could not be found",
                    new Dictionary<string, string>
                    {
                        ["sourcePath"] = "src/Fabricated/Checkout.cs",
                        ["lineNumber"] = "42",
                        ["code"] = "CS0246",
                        ["columnNumber"] = "9"
                    })
            ]),
        new SourceTimelineRecord(
            TestTaskId,
            "Task",
            "Run unit tests",
            "succeededWithIssues",
            4,
            0,
            2,
            502,
            "fabricated-agent-01",
            [
                new SourceIssue(
                    "warning",
                    "General",
                    "Two tests were skipped by the fixture",
                    new Dictionary<string, string>())
            ]),
        new SourceTimelineRecord(PublishTaskId, "Task", "Publish results", "skipped", 5, 0, 0, null, null, [])
    ]);

    private static SourceTimeline UnreadableTimeline() => new(
    [
        new SourceTimelineRecord(TestTaskId, "Task", "Run integration tests", "failed", 1, 1, 0, 601, "fabricated-agent-02", [])
    ]);

    private static IReadOnlyList<string> LogLines(int logId) => logId switch
    {
        501 => ["Compiling Fabricated.Alpha", "warning CS1591: missing comment", "error CS0246: type not found", "Build failed"],
        502 => Enumerable.Range(1, 60)
            .Select(line => line switch
            {
                57 => SecretLogLine,
                59 => $"token {FakeJwt}",
                _ => $"Fabricated log line {line}"
            })
            .ToArray(),
        601 => ["Restoring packages", "Running integration tests", "error: fabricated integration failure"],
        _ => []
    };

    private static IEnumerable<SourceTestResult> AllTestResults() =>
    [
        new SourceTestResult(
            7001,
            "Declined payments retry once",
            "Fabricated.Payments.RetryTests.RetriesOnce",
            "Fabricated.Alpha.Tests.dll",
            412,
            "Failed",
            "New Issue",
            null,
            "Assert.Equal() Failure: Expected 5 but was 3. " + SecretLogLine,
            "   at Fabricated.Payments.RetryTests.RetriesOnce() in src/Fabricated/RetryTests.cs:line 42"),
        new SourceTestResult(
            7001,
            "Checkout total includes tax",
            "Fabricated.Checkout.TotalTests.IncludesTax",
            "Fabricated.Alpha.Tests.dll",
            88.5,
            "Failed",
            "Regression",
            new SourceFailingSince(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), 9000, "20260701.1"),
            "Expected 1200 but was 1100",
            "   at Fabricated.Checkout.TotalTests.IncludesTax() in src/Fabricated/TotalTests.cs:line 17"),
        new SourceTestResult(
            7001,
            "Checkout renders the basket",
            "Fabricated.Checkout.RenderTests.RendersBasket",
            "Fabricated.Alpha.Tests.dll",
            12,
            "Passed",
            null,
            null,
            null,
            null),
        new SourceTestResult(
            7001,
            "Checkout shows the empty state",
            "Fabricated.Checkout.RenderTests.ShowsEmptyState",
            "Fabricated.Alpha.Tests.dll",
            9,
            "Passed",
            null,
            null,
            null,
            null)
    ];

    private static IEnumerable<SourceTestResult> PassingResults() =>
    [
        new SourceTestResult(7000, "Build smoke", "Fabricated.Build.SmokeTests.Runs", "Fabricated.Build.Tests.dll", 3, "Passed", null, null, null, null)
    ];
}
