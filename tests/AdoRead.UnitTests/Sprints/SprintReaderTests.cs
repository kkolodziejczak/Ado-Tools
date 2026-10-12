using AdoRead.AzureDevOps;
using AdoRead.Sprints;

namespace AdoRead.UnitTests.Sprints;

public sealed class SprintReaderTests
{
    private static readonly Uri OrganizationUrl = new("https://dev.azure.com/example/");
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadAsync_WithoutASprintNameUsesTheTeamsCurrentIteration()
    {
        var source = new FakeSprintSource();
        SprintReader reader = CreateReader(source);

        var result = await reader.ReadAsync("Payments", "Payments Team", null, [], CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("current", source.RequestedTimeframe);
        SprintDocument document = Assert.Single(result.Results);
        Assert.Equal("Sprint 11", document.Iteration.Name);
        Assert.Equal(3, document.WorkItems.Count);
    }

    [Fact]
    public async Task ReadAsync_MatchesANamedSprintCaseInsensitively()
    {
        var source = new FakeSprintSource();
        SprintReader reader = CreateReader(source);

        var result = await reader.ReadAsync("Payments", "Payments Team", "sprint 10", [], CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Sprint 10", Assert.Single(result.Results).Iteration.Name);
    }

    [Fact]
    public async Task ReadAsync_UnknownSprintNameReturnsNotFound()
    {
        SprintReader reader = CreateReader(new FakeSprintSource());

        var result = await reader.ReadAsync("Payments", "Payments Team", "Sprint 99", [], CancellationToken.None);

        Assert.Equal("notFound", result.Status);
        Assert.Empty(result.Results);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadAsync_StateFilterIsCaseInsensitiveAndCountsOnlyWhatItKept()
    {
        SprintReader reader = CreateReader(new FakeSprintSource());

        var result = await reader.ReadAsync(
            "Payments",
            "Payments Team",
            null,
            ["closed"],
            CancellationToken.None);

        SprintDocument document = Assert.Single(result.Results);
        Assert.Equal([201, 202], document.WorkItems.Select(item => item.Id).Order());
        Assert.Equal(2, document.CountsByState["Closed"]);
        Assert.False(document.CountsByState.ContainsKey("Active"));
    }

    [Fact]
    public async Task ReadAsync_WithoutAStateFilterCountsEveryState()
    {
        SprintReader reader = CreateReader(new FakeSprintSource());

        var result = await reader.ReadAsync("Payments", "Payments Team", null, [], CancellationToken.None);

        IReadOnlyDictionary<string, int> counts = Assert.Single(result.Results).CountsByState;
        Assert.Equal(1, counts["Active"]);
        Assert.Equal(2, counts["Closed"]);
        Assert.Equal(["Active", "Closed"], counts.Keys);
    }

    [Fact]
    public async Task ReadAsync_SourceFailureReturnsSafeErrorEnvelope()
    {
        var source = new FakeSprintSource
        {
            Failure = new WorkItemSourceException("forbidden", "The PAT cannot read this project.", false)
        };
        SprintReader reader = CreateReader(source);

        var result = await reader.ReadAsync("Payments", "Payments Team", null, [], CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("forbidden", Assert.Single(result.Errors).Code);
        Assert.Equal("Payments", result.Errors[0].Project);
    }

    [Fact]
    public async Task ListIterationsAsync_PassesTheTimeframeThroughAndReturnsEveryIteration()
    {
        var source = new FakeSprintSource();
        SprintReader reader = CreateReader(source);

        var result = await reader.ListIterationsAsync("Payments", "Payments Team", "past", CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("past", source.RequestedTimeframe);
        Assert.Equal(["Sprint 10", "Sprint 11"], result.Results.Select(iteration => iteration.Name));
    }

    private static SprintReader CreateReader(ISprintSource source) =>
        new(source, OrganizationUrl, new FixedTimeProvider(Now));

    private sealed class FakeSprintSource : ISprintSource
    {
        private static readonly SprintIterationDocument Past = new(
            Guid.NewGuid(),
            "Sprint 10",
            @"Payments\Sprint 10",
            new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero),
            "past");

        private static readonly SprintIterationDocument Current = new(
            Guid.NewGuid(),
            "Sprint 11",
            @"Payments\Sprint 11",
            new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero),
            "current");

        public WorkItemSourceException? Failure { get; init; }

        public string? RequestedTimeframe { get; private set; }

        public Task<IReadOnlyList<SprintIterationDocument>> GetIterationsAsync(
            string project,
            string team,
            string? timeframe,
            CancellationToken cancellationToken)
        {
            RequestedTimeframe = timeframe;
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<SprintIterationDocument>>(Failure);
            }

            IReadOnlyList<SprintIterationDocument> iterations = timeframe == "current"
                ? [Current]
                : [Past, Current];
            return Task.FromResult(iterations);
        }

        public Task<IReadOnlyList<SprintWorkItemDocument>> GetWorkItemsAsync(
            string project,
            string team,
            Guid iterationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SprintWorkItemDocument>>(
            [
                new SprintWorkItemDocument(
                    200, "User Story", "Capture payment", "Active", @"Payments\Sprint 11", "Ada Lovelace", null),
                new SprintWorkItemDocument(
                    201, "Bug", "Retry loop", "Closed", @"Payments\Sprint 11", "Ada Lovelace", null),
                new SprintWorkItemDocument(
                    202, "User Story", "Refund flow", "Closed", @"Payments\Sprint 11", null, null)
            ]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
