namespace AdoRead.AzureDevOps;

/// <summary>
/// Reads build and test evidence from Azure DevOps as plain records, so that the pipeline reader and
/// its tests never touch SDK types.
/// </summary>
public interface IPipelineSource
{
    Task<IReadOnlyList<SourceBuild>> GetPullRequestBuildsAsync(
        string project,
        int pullRequestId,
        string repositoryId,
        int top,
        CancellationToken cancellationToken);

    /// <summary>Null when no build has that id in the project.</summary>
    Task<SourceBuild?> GetBuildAsync(string project, int buildId, CancellationToken cancellationToken);

    Task<SourceTimeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceBuildLog>> GetBuildLogsAsync(string project, int buildId, CancellationToken cancellationToken);

    /// <summary>Returns lines startLine..endLine inclusive, as numbered by the build log list.</summary>
    Task<IReadOnlyList<string>> GetBuildLogLinesAsync(
        string project,
        int buildId,
        int logId,
        long startLine,
        long endLine,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceTestRun>> GetTestRunsAsync(string project, int buildId, CancellationToken cancellationToken);

    /// <summary>Returns at most <paramref name="maxCount"/> results of one test run, failed results only when asked.</summary>
    Task<IReadOnlyList<SourceTestResult>> GetTestResultsAsync(
        string project,
        int testRunId,
        bool failedOnly,
        int maxCount,
        CancellationToken cancellationToken);
}

public sealed record SourceBuild(
    int Id,
    string? BuildNumber,
    int? DefinitionId,
    string? DefinitionName,
    string? Status,
    string? Result,
    string? SourceBranch,
    string? SourceVersion,
    DateTimeOffset? QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? RequestedFor,
    string? RequestedBy);

public sealed record SourceTimeline(IReadOnlyList<SourceTimelineRecord> Records)
{
    public static SourceTimeline Empty { get; } = new([]);
}

/// <summary>One entry of a build timeline: a stage, job, task or other operation.</summary>
public sealed record SourceTimelineRecord(
    Guid Id,
    string? RecordType,
    string? Name,
    string? Result,
    int? Order,
    int? ErrorCount,
    int? WarningCount,
    int? LogId,
    string? WorkerName,
    IReadOnlyList<SourceIssue> Issues);

public sealed record SourceIssue(
    string? Type,
    string? Category,
    string? Message,
    IReadOnlyDictionary<string, string> Data);

public sealed record SourceBuildLog(int Id, long? LineCount);

public sealed record SourceTestRun(
    int Id,
    string? Name,
    int? Total,
    int? Passed,
    int? NotApplicable,
    int? Incomplete,
    int? Unanalyzed);

public sealed record SourceTestResult(
    int TestRunId,
    string? TestCaseTitle,
    string? AutomatedTestName,
    string? AutomatedTestStorage,
    double? DurationInMs,
    string? Outcome,
    string? FailureType,
    SourceFailingSince? FailingSince,
    string? ErrorMessage,
    string? StackTrace);

public sealed record SourceFailingSince(DateTimeOffset? Date, int? BuildId, string? BuildNumber);

public sealed class PipelineSourceException(
    string code,
    string message,
    bool transient,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Transient { get; } = transient;
}
