using AdoRead.AzureDevOps;

namespace AdoRead.UnitTests.Pipelines;

/// <summary>An in-memory pipeline source. Every call is recorded so tests can prove what was fetched.</summary>
internal sealed class FakePipelineSource : IPipelineSource
{
    public List<SourceBuild> PullRequestBuilds { get; set; } = [];

    public Dictionary<int, SourceBuild> Builds { get; } = [];

    public Dictionary<int, SourceTimeline> Timelines { get; } = [];

    public Dictionary<int, IReadOnlyList<SourceBuildLog>> Logs { get; } = [];

    public Dictionary<int, IReadOnlyList<string>> LogLines { get; } = [];

    public Dictionary<int, IReadOnlyList<SourceTestRun>> TestRuns { get; } = [];

    public Dictionary<int, IReadOnlyList<SourceTestResult>> TestResults { get; } = [];

    /// <summary>Test run ids whose results fail to read with a forbidden error.</summary>
    public HashSet<int> UnreadableTestRuns { get; } = [];

    public List<string> Calls { get; } = [];

    public List<(int TestRunId, bool FailedOnly, int MaxCount)> TestResultRequests { get; } = [];

    public Task<IReadOnlyList<SourceBuild>> GetPullRequestBuildsAsync(
        string project,
        int pullRequestId,
        string repositoryId,
        int top,
        CancellationToken cancellationToken)
    {
        Calls.Add($"prBuilds:{pullRequestId}:{top}");
        return Task.FromResult<IReadOnlyList<SourceBuild>>(PullRequestBuilds.Take(top).ToArray());
    }

    public Task<SourceBuild?> GetBuildAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        Calls.Add($"build:{buildId}");
        return Task.FromResult(Builds.GetValueOrDefault(buildId));
    }

    public Task<SourceTimeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        Calls.Add($"timeline:{buildId}");
        return Task.FromResult(Timelines.GetValueOrDefault(buildId) ?? SourceTimeline.Empty);
    }

    public Task<IReadOnlyList<SourceBuildLog>> GetBuildLogsAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        Calls.Add($"logs:{buildId}");
        return Task.FromResult(Logs.GetValueOrDefault(buildId) ?? []);
    }

    public Task<IReadOnlyList<string>> GetBuildLogLinesAsync(
        string project,
        int buildId,
        int logId,
        long startLine,
        long endLine,
        CancellationToken cancellationToken)
    {
        Calls.Add($"logLines:{buildId}:{logId}:{startLine}-{endLine}");
        IReadOnlyList<string> lines = LogLines.GetValueOrDefault(logId) ?? [];
        return Task.FromResult<IReadOnlyList<string>>(
            lines.Skip((int)startLine - 1).Take((int)(endLine - startLine + 1)).ToArray());
    }

    public Task<IReadOnlyList<SourceTestRun>> GetTestRunsAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        Calls.Add($"testRuns:{buildId}");
        return Task.FromResult(TestRuns.GetValueOrDefault(buildId) ?? []);
    }

    public Task<IReadOnlyList<SourceTestResult>> GetTestResultsAsync(
        string project,
        int testRunId,
        bool failedOnly,
        int maxCount,
        CancellationToken cancellationToken)
    {
        Calls.Add($"testResults:{testRunId}");
        TestResultRequests.Add((testRunId, failedOnly, maxCount));
        if (UnreadableTestRuns.Contains(testRunId))
        {
            return Task.FromException<IReadOnlyList<SourceTestResult>>(new PipelineSourceException(
                "forbidden",
                "The PAT cannot read this Azure DevOps resource.",
                transient: false));
        }

        IEnumerable<SourceTestResult> results = TestResults.GetValueOrDefault(testRunId) ?? [];
        if (failedOnly)
        {
            results = results.Where(result => result.Outcome == "Failed");
        }

        return Task.FromResult<IReadOnlyList<SourceTestResult>>(results.Take(maxCount).ToArray());
    }
}
