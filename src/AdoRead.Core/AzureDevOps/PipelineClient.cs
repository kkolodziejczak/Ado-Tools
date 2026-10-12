using Microsoft.TeamFoundation.Build.WebApi;
using BuildTimeline = Microsoft.TeamFoundation.Build.WebApi.Timeline;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.TestResults.WebApi;

namespace AdoRead.AzureDevOps;

/// <summary>The SDK calls a pipeline read needs, returning SDK types. <see cref="PipelineSource"/> maps them.</summary>
public interface IPipelineClient
{
    Task<IReadOnlyList<Build>> GetPullRequestBuildsAsync(
        string project,
        string branchName,
        string repositoryId,
        int top,
        CancellationToken cancellationToken);

    /// <summary>Throws the service's not-found response when the build does not exist.</summary>
    Task<Build> GetBuildAsync(string project, int buildId, CancellationToken cancellationToken);

    Task<BuildTimeline?> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BuildLog>> GetBuildLogsAsync(string project, int buildId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetBuildLogLinesAsync(
        string project,
        int buildId,
        int logId,
        long startLine,
        long endLine,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TestRun>> GetTestRunsAsync(string project, int buildId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TestCaseResult>> GetTestResultsAsync(
        string project,
        int testRunId,
        bool failedOnly,
        int skip,
        int top,
        CancellationToken cancellationToken);
}

public sealed class SdkPipelineClient(
    BuildHttpClient builds,
    TestManagementHttpClient testManagement,
    TestResultsHttpClient testResults) : IPipelineClient
{
    public async Task<IReadOnlyList<Build>> GetPullRequestBuildsAsync(
        string project,
        string branchName,
        string repositoryId,
        int top,
        CancellationToken cancellationToken) =>
        await builds.GetBuildsAsync(
            project,
            reasonFilter: BuildReason.PullRequest,
            top: top,
            queryOrder: BuildQueryOrder.QueueTimeDescending,
            branchName: branchName,
            repositoryId: repositoryId,
            repositoryType: "TfsGit",
            cancellationToken: cancellationToken);

    public Task<Build> GetBuildAsync(string project, int buildId, CancellationToken cancellationToken) =>
        builds.GetBuildAsync(project, buildId, cancellationToken: cancellationToken);

    public async Task<BuildTimeline?> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken) =>
        await builds.GetBuildTimelineAsync(project, buildId, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<BuildLog>> GetBuildLogsAsync(
        string project,
        int buildId,
        CancellationToken cancellationToken) =>
        await builds.GetBuildLogsAsync(project, buildId, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<string>> GetBuildLogLinesAsync(
        string project,
        int buildId,
        int logId,
        long startLine,
        long endLine,
        CancellationToken cancellationToken) =>
        await builds.GetBuildLogLinesAsync(project, buildId, logId, startLine, endLine, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<TestRun>> GetTestRunsAsync(
        string project,
        int buildId,
        CancellationToken cancellationToken) =>
        await testManagement.GetTestRunsAsync(
            project,
            buildUri: $"vstfs:///Build/Build/{buildId}",
            includeRunDetails: true,
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<TestCaseResult>> GetTestResultsAsync(
        string project,
        int testRunId,
        bool failedOnly,
        int skip,
        int top,
        CancellationToken cancellationToken) =>
        await testResults.GetTestResultsAsync(
            project,
            testRunId,
            skip: skip,
            top: top,
            outcomes: failedOnly ? [TestOutcome.Failed] : null,
            cancellationToken: cancellationToken);
}
