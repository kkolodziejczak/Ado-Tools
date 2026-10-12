using AdoRead.AzureDevOps;
using AdoRead.Pipelines;
using AdoRead.Privacy;
using AdoRead.Text;
using AdoRead.UnitTests.Privacy;

namespace AdoRead.UnitTests.Pipelines;

public sealed class PipelineDiagnosisTests
{
    private static readonly DateTimeOffset Queued = new(2026, 7, 9, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describe_FullPrivacy_KeepsMessagesStackTracesAndLogsWithMaskedSecrets()
    {
        var masker = new SecretMasker();

        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(
                timeline: Timeline(FailedTask(order: 1, logId: 501, issues: [Issue("CS0246: missing type password=hunter2")])),
                testRuns: [new SourceTestRun(7001, "Unit", 3, 2, 0, 0, 1)],
                failedTests: [FailedTest("Expected 5 but was 3 password=hunter2", "at Fabricated.Test()")],
                logTails: [new PipelineLogTailDocument(501, "Compile", 2, 1, ["ok", "token=abc123xyz"])]),
            TestPrivacy.Full,
            masker);

        PipelineTestResultDocument test = Assert.Single(run.FailedTests);
        Assert.Equal("Expected 5 but was 3 password=***", test.ErrorMessage);
        Assert.Equal("at Fabricated.Test()", test.StackTrace);
        Assert.Equal("CS0246: missing type password=***", Assert.Single(Assert.Single(run.FailedSteps).Issues!).Message);
        Assert.Equal(new[] { "ok", "token=***" }, Assert.Single(run.LogTails!).Lines.ToArray());
        Assert.Equal(3, run.MaskedSecretCount);
        Assert.Equal(3, masker.MaskedCount);
        Assert.Equal("engineer@example.invalid", run.RequestedFor);
    }

    [Fact]
    public void Describe_MinimalPrivacy_WithholdsMessagesStackTracesLogsAndAliasesPeople()
    {
        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(
                timeline: Timeline(FailedTask(order: 1, logId: 501, issues: [Issue("error CS0246")], workerName: "agent-01")),
                testRuns: [new SourceTestRun(7001, "Unit", 3, 2, 0, 0, 1)],
                failedTests: [FailedTest("Expected 5 but was 3", "at Fabricated.Test()")],
                logTails: [new PipelineLogTailDocument(501, "Compile", 2, 1, ["line"])]),
            TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Minimal)),
            new SecretMasker());

        PipelineTestResultDocument test = Assert.Single(run.FailedTests);
        Assert.Equal("Fabricated.Payments.RetryTests.RetriesOnce", test.AutomatedTestName);
        Assert.Null(test.ErrorMessage);
        Assert.Null(test.ErrorMessageTruncated);
        Assert.Null(test.StackTrace);
        Assert.Null(Assert.Single(run.FailedSteps).Issues);
        Assert.Null(Assert.Single(run.FailedSteps).WorkerName);
        Assert.Null(run.LogTails);
        Assert.StartsWith("person-", run.RequestedFor, StringComparison.Ordinal);
        Assert.Equal(0, run.MaskedSecretCount);
    }

    [Fact]
    public void Describe_StandardPrivacy_KeepsMessagesButWithholdsLogsAndWorkerNames()
    {
        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(
                timeline: Timeline(FailedTask(order: 1, logId: 501, issues: [Issue("error")], workerName: "agent-01")),
                logTails: [new PipelineLogTailDocument(501, "Compile", 2, 1, ["line"])]),
            TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Standard)),
            new SecretMasker());

        Assert.Single(Assert.Single(run.FailedSteps).Issues!);
        Assert.Null(Assert.Single(run.FailedSteps).WorkerName);
        Assert.Null(run.LogTails);
    }

    [Fact]
    public void Describe_BoundsFailedStepsAndIssues()
    {
        SourceTimelineRecord[] failed = Enumerable.Range(1, 60)
            .Select(order => FailedTask(order: order, logId: null, issues: [Issue("one"), Issue("two")]))
            .ToArray();

        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(timeline: Timeline(failed)),
            TestPrivacy.Full,
            new SecretMasker());

        Assert.Equal(60, run.FailedStepCount);
        Assert.Equal(PipelineDiagnosis.MaxFailedSteps, run.FailedSteps.Count);
        Assert.Equal(PipelineDiagnosis.MaxIssues, run.FailedSteps.Sum(step => step.Issues!.Count));
    }

    [Fact]
    public void Describe_TruncatesLongErrorMessagesAndFlagsThem()
    {
        string longMessage = new string('x', PipelineDiagnosis.MaxMessageLength + 500);

        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(
                testRuns: [new SourceTestRun(7001, "Unit", 2, 0, 0, 0, 2)],
                failedTests: [FailedTest(longMessage, stackTrace: null)]),
            TestPrivacy.Full,
            new SecretMasker());

        PipelineTestResultDocument test = Assert.Single(run.FailedTests);
        Assert.Equal(PipelineDiagnosis.MaxMessageLength, test.ErrorMessage!.Length);
        Assert.True(test.ErrorMessageTruncated);
        Assert.Null(test.StackTrace);
    }

    [Fact]
    public void Describe_MasksBeforeTruncating_SoAPartialSecretNeverSurvives()
    {
        string message = new string('x', PipelineDiagnosis.MaxMessageLength - 4) + " password=hunter2 tail";

        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(
                testRuns: [new SourceTestRun(7001, "Unit", 1, 0, 0, 0, 1)],
                failedTests: [FailedTest(message, stackTrace: null)]),
            TestPrivacy.Full,
            new SecretMasker());

        Assert.DoesNotContain("hunter", Assert.Single(run.FailedTests).ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_PrefersUnanalyzedCountAndFallsBackToComputedTotals()
    {
        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(testRuns:
            [
                new SourceTestRun(7001, "Reported", 10, 7, 0, 0, 3),
                new SourceTestRun(7002, "Computed", 10, 7, 1, 0, null)
            ]),
            TestPrivacy.Full,
            new SecretMasker());

        Assert.Equal(new int?[] { 3, 2 }, run.TestRuns.Select(testRun => testRun.Failed).ToArray());
        Assert.Equal(5, run.FailedTestCount);
    }

    [Fact]
    public void Describe_FlagsTruncatedFailedTestsOnlyWhenTheBudgetWasReached()
    {
        SourceTestResult[] fetched = Enumerable.Range(1, PipelineDiagnosis.MaxFailedTests)
            .Select(index => FailedTest($"message {index}", stackTrace: null))
            .ToArray();
        SourceTestRun[] runs = [new SourceTestRun(7001, "Large", 150, 0, 0, 0, 150)];

        PipelineRunDocument budgetReached = PipelineDiagnosis.Describe(
            Evidence(testRuns: runs, failedTests: fetched),
            TestPrivacy.Full,
            new SecretMasker());
        PipelineRunDocument readFailed = PipelineDiagnosis.Describe(
            Evidence(testRuns: runs, failedTests: fetched[..2]),
            TestPrivacy.Full,
            new SecretMasker());

        Assert.Equal(150, budgetReached.FailedTestCount);
        Assert.True(budgetReached.FailedTestsTruncated);
        Assert.False(readFailed.FailedTestsTruncated);
    }

    [Fact]
    public void Describe_KeepsOnlyTheIssueDataKeysThatLocateTheProblem()
    {
        var issue = new SourceIssue(
            "error",
            "DotNetCoreCLI",
            "CS0246",
            new Dictionary<string, string>
            {
                ["sourcePath"] = "src/Checkout.cs",
                ["lineNumber"] = "42",
                ["code"] = "CS0246",
                ["columnNumber"] = "9"
            });

        PipelineRunDocument run = PipelineDiagnosis.Describe(
            Evidence(timeline: Timeline(FailedTask(order: 1, logId: 501, issues: [issue]))),
            TestPrivacy.Full,
            new SecretMasker());

        IReadOnlyDictionary<string, string> data = Assert.Single(Assert.Single(run.FailedSteps).Issues!).Data;
        Assert.Equal(new[] { "code", "lineNumber", "sourcePath" }, data.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void FailedRecords_SelectsFailedSucceededWithIssuesAndCanceledInOrder()
    {
        SourceTimeline timeline = Timeline(
            Step(4, "canceled", "Canceled"),
            Step(1, "succeeded", "Passing"),
            Step(3, "failed", "Failed"),
            Step(2, "succeededWithIssues", "Warned"));

        string?[] names = PipelineDiagnosis.FailedRecords(timeline).Select(step => step.Name).ToArray();

        Assert.Equal(new string?[] { "Warned", "Failed", "Canceled" }, names);
    }

    [Fact]
    public void FailedTasksWithLogs_OnlyReturnsFailedTaskRecordsThatHaveALog()
    {
        IReadOnlyList<SourceTimelineRecord> failed = PipelineDiagnosis.FailedRecords(Timeline(
            FailedTask(order: 1, logId: 501, issues: []),
            Step(2, "failed", "Job", recordType: "Job", logId: 502),
            FailedTask(order: 3, logId: null, issues: [])));

        int?[] logIds = PipelineDiagnosis.FailedTasksWithLogs(failed).Select(step => step.LogId).ToArray();

        Assert.Equal(new int?[] { 501 }, logIds);
    }

    [Fact]
    public void BuildLink_PointsAtTheBuildResultsPage()
    {
        Uri link = PipelineDiagnosis.BuildLink(new Uri("https://dev.azure.com/example/"), "Payments Api", 42);

        Assert.Equal("https://dev.azure.com/example/Payments%20Api/_build/results?buildId=42", link.AbsoluteUri);
    }

    private static PipelineRunEvidence Evidence(
        SourceTimeline? timeline = null,
        IReadOnlyList<SourceTestRun>? testRuns = null,
        IReadOnlyList<SourceTestResult>? failedTests = null,
        IReadOnlyList<PipelineLogTailDocument>? logTails = null) =>
        new(
            Build(),
            timeline ?? SourceTimeline.Empty,
            testRuns ?? [],
            failedTests ?? [],
            logTails ?? [],
            new Uri("https://dev.azure.com/example/Payments/_build/results?buildId=42"));

    private static SourceBuild Build() => new(
        42,
        "20260709.1",
        7,
        "Payments CI",
        "completed",
        "failed",
        "refs/pull/9/merge",
        "abc123",
        Queued,
        Queued.AddSeconds(20),
        Queued.AddMinutes(5),
        "engineer@example.invalid",
        "engineer@example.invalid");

    private static SourceTimeline Timeline(params SourceTimelineRecord[] records) => new(records);

    private static SourceTimelineRecord FailedTask(
        int order,
        int? logId,
        IReadOnlyList<SourceIssue> issues,
        string? workerName = "agent-01") =>
        new(Guid.NewGuid(), "Task", $"Task {order}", "failed", order, 1, 0, logId, workerName, issues);

    private static SourceTimelineRecord Step(int order, string result, string name, string recordType = "Task", int? logId = null) =>
        new(Guid.NewGuid(), recordType, name, result, order, 0, 0, logId, null, []);

    private static SourceIssue Issue(string message) => new("error", "General", message, new Dictionary<string, string>());

    private static SourceTestResult FailedTest(string errorMessage, string? stackTrace) => new(
        7001,
        "Declined payments retry once",
        "Fabricated.Payments.RetryTests.RetriesOnce",
        "Fabricated.Payments.Tests.dll",
        412,
        "Failed",
        "New Issue",
        null,
        errorMessage,
        stackTrace);
}
