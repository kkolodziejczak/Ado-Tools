using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.PullRequests;
using AdoRead.Text;

namespace AdoRead.Pipelines;

/// <summary>
/// Finds the pipeline runs of a pull request or a build and diagnoses them. Content the privacy policy
/// withholds is not fetched: build logs are downloaded only when logs are allowed.
/// </summary>
public sealed class PipelineReader(
    IProjectCatalog projectCatalog,
    IPullRequestSource pullRequests,
    IPipelineSource pipelines,
    Uri organizationUrl,
    TimeProvider timeProvider,
    EffectivePrivacy privacy)
{
    public const int MaxDefinitionsPerPullRequest = 20;
    public const int MaxBuildsScanned = 200;
    public const int MaxFailedTasksWithLogs = 10;
    private const int MaxParallelBuilds = 4;

    /// <summary>The categories that can change a pull-request or build document.</summary>
    public static IReadOnlyList<PrivacyCategory> RunCategories { get; } =
        [PrivacyCategory.TestMessages, PrivacyCategory.StackTraces, PrivacyCategory.Logs, PrivacyCategory.People];

    public static IReadOnlyList<PrivacyCategory> TestCategories { get; } =
        [PrivacyCategory.TestMessages, PrivacyCategory.StackTraces];

    /// <summary>The latest validation run of each pipeline definition for the pull request's current head.</summary>
    public async Task<SearchEnvelope<PullRequestPipelinesDocument>> ReadPullRequestAsync(
        PullRequestPipelinesQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            AdoProject? project = await FindProjectAsync(query.Project, cancellationToken);
            if (project is null)
            {
                return Failure<PullRequestPipelinesDocument>(
                    PipelineKinds.PullRequest,
                    query,
                    new SearchError("command", query.Project, "projectNotFound", "No accessible project has this name.", false),
                    status: "notFound");
            }

            PullRequestDocument? pullRequest = await pullRequests.TryGetAsync(project, query.PullRequestId, cancellationToken);
            if (pullRequest is null)
            {
                return CreateEnvelope<PullRequestPipelinesDocument>(PipelineKinds.PullRequest, query, "notFound", [], [], RunCategories);
            }

            if (query.Repository is not null
                && !string.Equals(pullRequest.Repository.Name, query.Repository, StringComparison.OrdinalIgnoreCase))
            {
                return Failure<PullRequestPipelinesDocument>(
                    PipelineKinds.PullRequest,
                    query,
                    new SearchError(
                        "command",
                        project.Name,
                        "pullRequestNotInRepository",
                        "The pull request belongs to a different repository than the one given.",
                        false),
                    status: "notFound");
            }

            IReadOnlyList<SourceBuild> builds = await pipelines.GetPullRequestBuildsAsync(
                project.Name,
                query.PullRequestId,
                pullRequest.Repository.Id.ToString(),
                MaxBuildsScanned,
                cancellationToken);
            (IReadOnlyList<SourceBuild> headBuilds, string headMatch) = ForHead(builds, pullRequest.MergeCommitId);
            IReadOnlyList<SourceBuild> latest = LatestPerDefinition(headBuilds);
            PipelineRunRead[] reads = await ReadRunsAsync(project.Name, latest, query.LogLines, cancellationToken);
            var document = new PullRequestPipelinesDocument(
                pullRequest.Id,
                pullRequest.Title,
                pullRequest.Project.Name,
                pullRequest.Repository.Name,
                pullRequest.SourceRefName,
                pullRequest.MergeCommitId,
                headMatch,
                pullRequest.Links.Browser,
                reads.Select(read => read.Document).ToArray());
            SearchError[] errors = reads.SelectMany(read => read.Errors).ToArray();
            return CreateEnvelope<PullRequestPipelinesDocument>(
                PipelineKinds.PullRequest,
                query,
                errors.Length == 0 ? "success" : "partial",
                [document],
                errors,
                RunCategories);
        }
        catch (PullRequestSourceException exception)
        {
            return Failure<PullRequestPipelinesDocument>(
                PipelineKinds.PullRequest,
                query,
                new SearchError("azureDevOps", query.Project, exception.Code, exception.Message, exception.Transient));
        }
        catch (PipelineSourceException exception)
        {
            return Failure<PullRequestPipelinesDocument>(
                PipelineKinds.PullRequest,
                query,
                new SearchError("build", query.Project, exception.Code, exception.Message, exception.Transient));
        }
    }

    public async Task<SearchEnvelope<PipelineRunDocument>> ReadBuildAsync(
        PipelineRunQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            SourceBuild? build = await pipelines.GetBuildAsync(query.Project, query.BuildId, cancellationToken);
            if (build is null)
            {
                return CreateEnvelope<PipelineRunDocument>(PipelineKinds.Run, query, "notFound", [], [], RunCategories);
            }

            PipelineRunRead read = await ReadRunAsync(query.Project, build, query.LogLines, cancellationToken);
            return CreateEnvelope<PipelineRunDocument>(
                PipelineKinds.Run,
                query,
                read.Errors.Count == 0 ? "success" : "partial",
                [read.Document],
                read.Errors,
                RunCategories);
        }
        catch (PipelineSourceException exception)
        {
            return Failure<PipelineRunDocument>(
                PipelineKinds.Run,
                query,
                new SearchError("build", query.Project, exception.Code, exception.Message, exception.Transient));
        }
    }

    public async Task<SearchEnvelope<PipelineTestResultDocument>> ReadTestsAsync(
        PipelineTestsQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            SourceBuild? build = await pipelines.GetBuildAsync(query.Project, query.BuildId, cancellationToken);
            if (build is null)
            {
                return CreateEnvelope<PipelineTestResultDocument>(PipelineKinds.Tests, query, "notFound", [], [], TestCategories);
            }

            var errors = new List<SearchError>();
            IReadOnlyList<SourceTestRun> testRuns = await Attempt(
                () => pipelines.GetTestRunsAsync(query.Project, query.BuildId, cancellationToken),
                query.Project,
                query.BuildId,
                "test runs",
                errors) ?? [];
            bool failedOnly = string.Equals(query.Outcome, "failed", StringComparison.Ordinal);
            var results = new List<PipelineTestResultDocument>();
            var masker = new SecretMasker();
            foreach (SourceTestRun run in testRuns)
            {
                int budget = query.Top - results.Count;
                if (budget == 0)
                {
                    break;
                }

                IReadOnlyList<SourceTestResult>? page = await Attempt(
                    () => pipelines.GetTestResultsAsync(query.Project, run.Id, failedOnly, budget, cancellationToken),
                    query.Project,
                    query.BuildId,
                    "test results",
                    errors);
                results.AddRange((page ?? []).Select(test => PipelineDiagnosis.TestResult(test, privacy, masker)));
            }

            return CreateEnvelope(
                PipelineKinds.Tests,
                query,
                TestsStatus(results.Count, errors.Count),
                results,
                errors,
                TestCategories);
        }
        catch (PipelineSourceException exception)
        {
            return Failure<PipelineTestResultDocument>(
                PipelineKinds.Tests,
                query,
                new SearchError("build", query.Project, exception.Code, exception.Message, exception.Transient));
        }
    }

    private async Task<AdoProject?> FindProjectAsync(string name, CancellationToken cancellationToken) =>
        (await projectCatalog.GetAllAsync(cancellationToken))
        .FirstOrDefault(project => string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds for the pull request's current merge commit when the service reports one. Without a merge
    /// commit, every build of the pull request is a candidate and the match is only by queue order.
    /// </summary>
    private static (IReadOnlyList<SourceBuild> Builds, string HeadMatch) ForHead(
        IReadOnlyList<SourceBuild> builds,
        string? mergeCommitId) =>
        mergeCommitId is null
            ? (builds, "latestQueued")
            : (builds.Where(build => string.Equals(build.SourceVersion, mergeCommitId, StringComparison.OrdinalIgnoreCase)).ToArray(), "mergeCommit");

    private static IReadOnlyList<SourceBuild> LatestPerDefinition(IReadOnlyList<SourceBuild> builds) =>
        builds
            .GroupBy(build => build.DefinitionId ?? 0)
            .Select(group => group.OrderByDescending(QueuedAt).ThenByDescending(build => build.Id).First())
            .OrderByDescending(QueuedAt)
            .ThenByDescending(build => build.Id)
            .Take(MaxDefinitionsPerPullRequest)
            .ToArray();

    private static DateTimeOffset QueuedAt(SourceBuild build) => build.QueuedAtUtc ?? DateTimeOffset.MinValue;

    private async Task<PipelineRunRead[]> ReadRunsAsync(
        string project,
        IReadOnlyList<SourceBuild> builds,
        int logLines,
        CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(MaxParallelBuilds, MaxParallelBuilds);
        return await Task.WhenAll(builds.Select(build => ReadThrottledRunAsync(
            concurrency,
            project,
            build,
            logLines,
            cancellationToken)));
    }

    private async Task<PipelineRunRead> ReadThrottledRunAsync(
        SemaphoreSlim concurrency,
        string project,
        SourceBuild build,
        int logLines,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            return await ReadRunAsync(project, build, logLines, cancellationToken);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<PipelineRunRead> ReadRunAsync(
        string project,
        SourceBuild build,
        int logLines,
        CancellationToken cancellationToken)
    {
        var errors = new List<SearchError>();
        SourceTimeline timeline = await Attempt(
            () => pipelines.GetTimelineAsync(project, build.Id, cancellationToken),
            project,
            build.Id,
            "timeline",
            errors) ?? SourceTimeline.Empty;
        IReadOnlyList<SourceTestRun> testRuns = await Attempt(
            () => pipelines.GetTestRunsAsync(project, build.Id, cancellationToken),
            project,
            build.Id,
            "test runs",
            errors) ?? [];
        IReadOnlyList<SourceTestResult> failedTests = await ReadFailedTestsAsync(
            project,
            build.Id,
            testRuns,
            errors,
            cancellationToken);
        IReadOnlyList<PipelineLogTailDocument> logTails = privacy.Allows(PrivacyCategory.Logs)
            ? await ReadLogTailsAsync(project, build.Id, timeline, logLines, errors, cancellationToken)
            : [];
        var evidence = new PipelineRunEvidence(
            build,
            timeline,
            testRuns,
            failedTests,
            logTails,
            PipelineDiagnosis.BuildLink(organizationUrl, project, build.Id));
        return new PipelineRunRead(PipelineDiagnosis.Describe(evidence, privacy, new SecretMasker()), errors);
    }

    /// <summary>Fetches failed results run by run until the per-build budget is spent.</summary>
    private async Task<IReadOnlyList<SourceTestResult>> ReadFailedTestsAsync(
        string project,
        int buildId,
        IReadOnlyList<SourceTestRun> testRuns,
        List<SearchError> errors,
        CancellationToken cancellationToken)
    {
        var failed = new List<SourceTestResult>();
        foreach (SourceTestRun run in testRuns)
        {
            int budget = PipelineDiagnosis.MaxFailedTests - failed.Count;
            if (budget == 0)
            {
                break;
            }

            IReadOnlyList<SourceTestResult>? page = await Attempt(
                () => pipelines.GetTestResultsAsync(project, run.Id, failedOnly: true, budget, cancellationToken),
                project,
                buildId,
                "failed tests",
                errors);
            failed.AddRange(page ?? []);
        }

        return failed;
    }

    /// <summary>The last <paramref name="logLines"/> lines of the first failed tasks, using the line count from the build log list.</summary>
    private async Task<IReadOnlyList<PipelineLogTailDocument>> ReadLogTailsAsync(
        string project,
        int buildId,
        SourceTimeline timeline,
        int logLines,
        List<SearchError> errors,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceBuildLog>? logs = await Attempt(
            () => pipelines.GetBuildLogsAsync(project, buildId, cancellationToken),
            project,
            buildId,
            "logs",
            errors);
        if (logs is null)
        {
            return [];
        }

        var tails = new List<PipelineLogTailDocument>();
        foreach (SourceTimelineRecord task in PipelineDiagnosis.FailedTasksWithLogs(PipelineDiagnosis.FailedRecords(timeline))
                     .Take(MaxFailedTasksWithLogs))
        {
            SourceBuildLog? log = logs.FirstOrDefault(candidate => candidate.Id == task.LogId);
            if (log is null || log.LineCount is not > 0)
            {
                continue;
            }

            int logId = log.Id;
            long lastLine = log.LineCount.Value;
            long firstLine = Math.Max(1, lastLine - logLines + 1);
            IReadOnlyList<string>? lines = await Attempt(
                () => pipelines.GetBuildLogLinesAsync(project, buildId, logId, firstLine, lastLine, cancellationToken),
                project,
                buildId,
                "log lines",
                errors);
            if (lines is not null)
            {
                tails.Add(new PipelineLogTailDocument(logId, task.Name, lastLine, firstLine, lines));
            }
        }

        return tails;
    }

    /// <summary>Runs one read. A failure becomes an error for this build and the read continues without it.</summary>
    private static async Task<T?> Attempt<T>(
        Func<Task<T>> read,
        string project,
        int buildId,
        string stage,
        List<SearchError> errors) where T : class
    {
        try
        {
            return await read();
        }
        catch (PipelineSourceException exception)
        {
            errors.Add(new SearchError(
                "build",
                project,
                exception.Code,
                $"Build {buildId} {stage} could not be read. {exception.Message}",
                exception.Transient));
            return null;
        }
    }

    private static string TestsStatus(int resultCount, int errorCount) => (resultCount, errorCount) switch
    {
        ( > 0, 0) => "success",
        ( > 0, _) => "partial",
        (_, 0) => "success",
        _ => "error"
    };

    private SearchEnvelope<T> CreateEnvelope<T>(
        string kind,
        object query,
        string status,
        IReadOnlyList<T> results,
        IReadOnlyList<SearchError> errors,
        IReadOnlyList<PrivacyCategory> categories) =>
        PrivacyEnvelopes.Describe(
            new SearchEnvelope<T>("1.0", kind, status, query, timeProvider.GetUtcNow(), results, errors),
            privacy,
            categories);

    private SearchEnvelope<T> Failure<T>(string kind, object query, SearchError error, string status = "error") =>
        CreateEnvelope<T>(kind, query, status, [], [error], []);

    private sealed record PipelineRunRead(PipelineRunDocument Document, IReadOnlyList<SearchError> Errors);
}
