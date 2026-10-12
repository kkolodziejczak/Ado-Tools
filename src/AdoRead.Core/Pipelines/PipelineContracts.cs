using AdoRead.AzureDevOps;

namespace AdoRead.Pipelines;

/// <summary>The envelope kinds of the pipeline commands.</summary>
public static class PipelineKinds
{
    public const string PullRequest = "pipelinePullRequest";
    public const string Run = "pipelineRun";
    public const string Tests = "pipelineTests";
}

public sealed record PullRequestPipelinesQuery(
    int PullRequestId,
    Uri OrganizationUrl,
    string Project,
    string? Repository,
    int LogLines);

public sealed record PipelineRunQuery(int BuildId, Uri OrganizationUrl, string Project, int LogLines);

public sealed record PipelineTestsQuery(int BuildId, Uri OrganizationUrl, string Project, string Outcome, int Top);

/// <summary>A pull request and the latest validation run of each of its pipeline definitions.</summary>
public sealed record PullRequestPipelinesDocument(
    int Id,
    string? Title,
    string ProjectName,
    string RepositoryName,
    string? SourceRefName,
    string? MergeCommitId,
    string HeadMatch,
    Uri? PullRequestUrl,
    IReadOnlyList<PipelineRunDocument> Runs);

/// <summary>
/// One build and its diagnosis. Null lists are content the privacy policy withholds; empty lists mean
/// the content was allowed and there was none.
/// </summary>
public sealed record PipelineRunDocument(
    int BuildId,
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
    Uri? BrowserUrl,
    string? RequestedFor,
    string? RequestedBy,
    int FailedStepCount,
    IReadOnlyList<PipelineStepDocument> FailedSteps,
    IReadOnlyList<PipelineTestRunDocument> TestRuns,
    int FailedTestCount,
    IReadOnlyList<PipelineTestResultDocument> FailedTests,
    bool FailedTestsTruncated,
    IReadOnlyList<PipelineLogTailDocument>? LogTails,
    int MaskedSecretCount);

/// <summary>A failed stage, job or task of a build.</summary>
public sealed record PipelineStepDocument(
    Guid Id,
    string? RecordType,
    string? Name,
    string? Result,
    int? ErrorCount,
    int? WarningCount,
    string? WorkerName,
    IReadOnlyList<PipelineIssueDocument>? Issues);

public sealed record PipelineIssueDocument(
    string? Type,
    string? Category,
    string? Message,
    IReadOnlyDictionary<string, string> Data);

public sealed record PipelineTestRunDocument(
    int Id,
    string? Name,
    int? Total,
    int? Passed,
    int? Failed,
    int? NotApplicable,
    int? Incomplete,
    int? UnanalyzedTests);

/// <summary>A test result, failed or not. Messages and stack traces are null when withheld.</summary>
public sealed record PipelineTestResultDocument(
    int? TestRunId,
    string? TestCaseTitle,
    string? AutomatedTestName,
    string? AutomatedTestStorage,
    double? DurationInMs,
    string? Outcome,
    string? FailureType,
    PipelineFailingSinceDocument? FailingSince,
    string? ErrorMessage,
    bool? ErrorMessageTruncated,
    string? StackTrace,
    bool? StackTraceTruncated);

public sealed record PipelineFailingSinceDocument(DateTimeOffset? DateUtc, int? BuildId, string? BuildNumber);

/// <summary>The last lines of one failed task's log, numbered 1-based from the start of the log.</summary>
public sealed record PipelineLogTailDocument(
    int LogId,
    string? StepName,
    long? LineCount,
    long FirstLine,
    IReadOnlyList<string> Lines);

/// <summary>The raw evidence for one build, as fetched. <see cref="PipelineDiagnosis"/> turns it into a run document.</summary>
public sealed record PipelineRunEvidence(
    SourceBuild Build,
    SourceTimeline Timeline,
    IReadOnlyList<SourceTestRun> TestRuns,
    IReadOnlyList<SourceTestResult> FailedTests,
    IReadOnlyList<PipelineLogTailDocument> LogTails,
    Uri? BrowserUrl);
