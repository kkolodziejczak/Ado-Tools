using System.Net;
using Microsoft.TeamFoundation.Build.WebApi;
using BuildTimeline = Microsoft.TeamFoundation.Build.WebApi.Timeline;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public sealed class PipelineSource(IPipelineClient client) : IPipelineSource
{
    private const int PageSize = 200;

    public async Task<IReadOnlyList<SourceBuild>> GetPullRequestBuildsAsync(
        string project,
        int pullRequestId,
        string repositoryId,
        int top,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Build> builds = await client.GetPullRequestBuildsAsync(
                project,
                $"refs/pull/{pullRequestId}/merge",
                repositoryId,
                top,
                cancellationToken);
            return builds.Select(MapBuild).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    public async Task<SourceBuild?> GetBuildAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        try
        {
            return MapBuild(await client.GetBuildAsync(project, buildId, cancellationToken));
        }
        catch (VssServiceResponseException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    public async Task<SourceTimeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        try
        {
            BuildTimeline? timeline = await client.GetTimelineAsync(project, buildId, cancellationToken);
            return new SourceTimeline((timeline?.Records ?? []).Select(MapTimelineRecord).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    public async Task<IReadOnlyList<SourceBuildLog>> GetBuildLogsAsync(
        string project,
        int buildId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<BuildLog> logs = await client.GetBuildLogsAsync(project, buildId, cancellationToken);
            return logs.Select(log => new SourceBuildLog(log.Id, log.LineCount)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    public async Task<IReadOnlyList<string>> GetBuildLogLinesAsync(
        string project,
        int buildId,
        int logId,
        long startLine,
        long endLine,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetBuildLogLinesAsync(project, buildId, logId, startLine, endLine, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    public async Task<IReadOnlyList<SourceTestRun>> GetTestRunsAsync(
        string project,
        int buildId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<TestRun> runs = await client.GetTestRunsAsync(project, buildId, cancellationToken);
            return runs.Select(MapTestRun).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    public async Task<IReadOnlyList<SourceTestResult>> GetTestResultsAsync(
        string project,
        int testRunId,
        bool failedOnly,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var results = new List<SourceTestResult>();
        try
        {
            // The service caps a page; keep requesting pages until the caller's budget is met or the run ends.
            while (results.Count < maxCount)
            {
                int pageSize = Math.Min(PageSize, maxCount - results.Count);
                IReadOnlyList<TestCaseResult> page = await client.GetTestResultsAsync(
                    project,
                    testRunId,
                    failedOnly,
                    results.Count,
                    pageSize,
                    cancellationToken);
                results.AddRange(page.Select(result => MapTestResult(testRunId, result)));
                if (page.Count < pageSize)
                {
                    break;
                }
            }

            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PipelineFailure(exception);
        }
    }

    private static SourceBuild MapBuild(Build source) => new(
        source.Id,
        source.BuildNumber,
        source.Definition?.Id,
        source.Definition?.Name,
        StableEnum(source.Status),
        StableEnum(source.Result),
        source.SourceBranch,
        source.SourceVersion,
        ToUtc(source.QueueTime),
        ToUtc(source.StartTime),
        ToUtc(source.FinishTime),
        Person(source.RequestedFor),
        Person(source.RequestedBy));

    private static string? Person(IdentityRef? identity) => identity?.UniqueName ?? identity?.DisplayName;

    private static SourceTimelineRecord MapTimelineRecord(TimelineRecord source) => new(
        source.Id,
        source.RecordType,
        source.Name,
        StableEnum(source.Result),
        source.Order,
        source.ErrorCount,
        source.WarningCount,
        source.Log?.Id,
        source.WorkerName,
        (source.Issues ?? []).Select(MapIssue).ToArray());

    private static SourceIssue MapIssue(Issue source) => new(
        StableEnum<IssueType>(source.Type),
        source.Category,
        source.Message,
        new Dictionary<string, string>(source.Data ?? new Dictionary<string, string>(), StringComparer.Ordinal));

    private static SourceTestRun MapTestRun(TestRun source) => new(
        source.Id,
        source.Name,
        source.TotalTests,
        source.PassedTests,
        source.NotApplicableTests,
        source.IncompleteTests,
        source.UnanalyzedTests);

    private static SourceTestResult MapTestResult(int testRunId, TestCaseResult source) => new(
        testRunId,
        source.TestCaseTitle,
        source.AutomatedTestName,
        source.AutomatedTestStorage,
        source.DurationInMs,
        source.Outcome,
        source.FailureType,
        source.FailingSince is null
            ? null
            : new SourceFailingSince(
                ToUtc(source.FailingSince.Date),
                source.FailingSince.Build?.Id,
                source.FailingSince.Build?.Number),
        source.ErrorMessage,
        source.StackTrace);

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null || value == default
        ? null
        : new DateTimeOffset(value.Value.ToUniversalTime());

    private static string? StableEnum<T>(T? value) where T : struct, Enum
    {
        if (value is null)
        {
            return null;
        }

        string text = value.Value.ToString();
        return string.IsNullOrEmpty(text)
            ? null
            : char.ToLowerInvariant(text[0]) + text[1..];
    }
}
