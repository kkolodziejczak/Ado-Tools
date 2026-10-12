using AdoRead.AzureDevOps;
using AdoRead.Privacy;
using AdoRead.Text;

namespace AdoRead.Pipelines;

/// <summary>
/// Turns one build's evidence into the documents an agent reads. Bounds, secret masking and privacy are
/// applied here, so every pipeline command follows the same rules. Masking runs before truncation, so a
/// cut can never leave half of a secret behind.
/// </summary>
public static class PipelineDiagnosis
{
    public const int MaxFailedSteps = 50;
    public const int MaxFailedTests = 100;
    public const int MaxIssues = 50;
    public const int MaxMessageLength = 4000;

    private static readonly HashSet<string> FailedResults = new(StringComparer.Ordinal) { "failed", "succeededWithIssues", "canceled" };
    private static readonly string[] IssueDataKeys = ["sourcePath", "lineNumber", "code"];

    public static IReadOnlyList<SourceTimelineRecord> FailedRecords(SourceTimeline timeline) =>
        timeline.Records
            .Where(record => record.Result is not null && FailedResults.Contains(record.Result))
            .OrderBy(record => record.Order ?? int.MaxValue)
            .ThenBy(record => record.Name, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<SourceTimelineRecord> FailedTasksWithLogs(IReadOnlyList<SourceTimelineRecord> failedRecords) =>
        failedRecords.Where(record => record.RecordType == "Task" && record.LogId is not null).ToArray();

    public static Uri BuildLink(Uri organizationUrl, string project, int buildId) =>
        new(organizationUrl, $"{Uri.EscapeDataString(project)}/_build/results?buildId={buildId}");

    public static PipelineRunDocument Describe(PipelineRunEvidence evidence, EffectivePrivacy privacy, SecretMasker masker)
    {
        SourceBuild build = evidence.Build;
        IReadOnlyList<SourceTimelineRecord> failedRecords = FailedRecords(evidence.Timeline);
        IReadOnlyList<PipelineTestRunDocument> testRuns = evidence.TestRuns.Select(MapTestRun).ToArray();
        IReadOnlyList<PipelineTestResultDocument> failedTests = evidence.FailedTests
            .Select(test => TestResult(test, privacy, masker))
            .ToArray();
        int failedTestCount = FailedTestCount(testRuns, failedTests.Count);
        IReadOnlyList<PipelineStepDocument> steps = Steps(failedRecords, privacy, masker);
        IReadOnlyList<PipelineLogTailDocument>? logTails = LogTails(evidence.LogTails, privacy, masker);
        return new PipelineRunDocument(
            build.Id,
            build.BuildNumber,
            build.DefinitionId,
            build.DefinitionName,
            build.Status,
            build.Result,
            build.SourceBranch,
            build.SourceVersion,
            build.QueuedAtUtc,
            build.StartedAtUtc,
            build.FinishedAtUtc,
            evidence.BrowserUrl,
            privacy.Person(build.RequestedFor),
            privacy.Person(build.RequestedBy),
            failedRecords.Count,
            steps,
            testRuns,
            failedTestCount,
            failedTests,
            failedTests.Count >= MaxFailedTests && failedTestCount > failedTests.Count,
            logTails,
            masker.MaskedCount);
    }

    public static PipelineTestResultDocument TestResult(SourceTestResult test, EffectivePrivacy privacy, SecretMasker masker)
    {
        BoundedText? errorMessage = privacy.Allows(PrivacyCategory.TestMessages)
            ? Bound(masker.Mask(test.ErrorMessage))
            : null;
        BoundedText? stackTrace = privacy.Allows(PrivacyCategory.StackTraces)
            ? Bound(masker.Mask(test.StackTrace))
            : null;
        return new PipelineTestResultDocument(
            test.TestRunId,
            test.TestCaseTitle,
            test.AutomatedTestName,
            test.AutomatedTestStorage,
            test.DurationInMs,
            test.Outcome,
            test.FailureType,
            FailingSince(test.FailingSince),
            errorMessage?.Text,
            errorMessage?.Truncated,
            stackTrace?.Text,
            stackTrace?.Truncated);
    }

    private static PipelineTestRunDocument MapTestRun(SourceTestRun run) => new(
        run.Id,
        run.Name,
        run.Total,
        run.Passed,
        FailedCount(run),
        run.NotApplicable,
        run.Incomplete,
        run.Unanalyzed);

    /// <summary>The service's unanalyzed count when it reports one; otherwise the total minus the other outcomes.</summary>
    private static int? FailedCount(SourceTestRun run) =>
        run.Unanalyzed ?? (run.Total is { } total
                           && run.Passed is { } passed
                           && run.NotApplicable is { } notApplicable
                           && run.Incomplete is { } incomplete
            ? total - passed - notApplicable - incomplete
            : null);

    /// <summary>The run-level count when every run reports one; otherwise the number of results actually fetched.</summary>
    private static int FailedTestCount(IReadOnlyList<PipelineTestRunDocument> testRuns, int fetchedCount) =>
        testRuns.All(run => run.Failed is not null)
            ? testRuns.Sum(run => run.Failed ?? 0)
            : fetchedCount;

    private static IReadOnlyList<PipelineStepDocument> Steps(
        IReadOnlyList<SourceTimelineRecord> failedRecords,
        EffectivePrivacy privacy,
        SecretMasker masker)
    {
        var steps = new List<PipelineStepDocument>();
        int issueBudget = MaxIssues;
        foreach (SourceTimelineRecord step in failedRecords.Take(MaxFailedSteps))
        {
            IReadOnlyList<PipelineIssueDocument>? issues = null;
            if (privacy.Allows(PrivacyCategory.TestMessages))
            {
                SourceIssue[] taken = step.Issues.Take(issueBudget).ToArray();
                issueBudget -= taken.Length;
                issues = taken.Select(issue => Issue(issue, masker)).ToArray();
            }

            steps.Add(new PipelineStepDocument(
                step.Id,
                step.RecordType,
                step.Name,
                step.Result,
                step.ErrorCount,
                step.WarningCount,
                privacy.Allows(PrivacyCategory.Logs) ? step.WorkerName : null,
                issues));
        }

        return steps;
    }

    private static PipelineIssueDocument Issue(SourceIssue issue, SecretMasker masker) => new(
        issue.Type,
        issue.Category,
        masker.Mask(issue.Message),
        IssueData(issue.Data, masker));

    private static IReadOnlyDictionary<string, string> IssueData(IReadOnlyDictionary<string, string> data, SecretMasker masker) =>
        data.Where(pair => IssueDataKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => masker.Mask(pair.Value) ?? string.Empty, StringComparer.Ordinal);

    private static PipelineFailingSinceDocument? FailingSince(SourceFailingSince? failingSince) =>
        failingSince is null
            ? null
            : new PipelineFailingSinceDocument(failingSince.Date, failingSince.BuildId, failingSince.BuildNumber);

    private static IReadOnlyList<PipelineLogTailDocument>? LogTails(
        IReadOnlyList<PipelineLogTailDocument> tails,
        EffectivePrivacy privacy,
        SecretMasker masker) =>
        privacy.Allows(PrivacyCategory.Logs)
            ? tails.Select(tail => tail with
            {
                Lines = tail.Lines.Select(line => masker.Mask(line) ?? string.Empty).ToArray()
            }).ToArray()
            : null;

    private static BoundedText? Bound(string? maskedText)
    {
        if (maskedText is null)
        {
            return null;
        }

        return maskedText.Length > MaxMessageLength
            ? new BoundedText(maskedText[..MaxMessageLength], true)
            : new BoundedText(maskedText, false);
    }

    private sealed record BoundedText(string Text, bool Truncated);
}
