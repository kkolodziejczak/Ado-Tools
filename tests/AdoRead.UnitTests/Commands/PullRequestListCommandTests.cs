using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.PullRequests;

namespace AdoRead.UnitTests.Commands;

public sealed class PullRequestListCommandTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly AdoProject Payments = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Payments", null);
    private static readonly AdoProject Billing = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Billing", null);

    [Fact]
    public async Task List_ReturnsActivePullRequestsOfEveryProjectNewestFirstAsShortEntries()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, created: Day(1), creator: Ana));
        source.Add(Billing, Pr(Billing, 20, created: Day(3), creator: Bob));
        source.Add(Payments, Pr(Payments, 11, created: Day(2), creator: Ana));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("pullRequestList", root.GetProperty("kind").GetString());
        Assert.Equal([20, 11, 10], Ids(root));
        Assert.All(source.Criteria, criteria => Assert.Equal("active", criteria.Status));
        JsonElement entry = root.GetProperty("results")[0];
        Assert.Equal("Billing", entry.GetProperty("project").GetString());
        Assert.Equal("Repo", entry.GetProperty("repository").GetString());
        Assert.Equal("bob@example.com", entry.GetProperty("creatorUniqueName").GetString());
        Assert.Equal("refs/heads/feature", entry.GetProperty("sourceRefName").GetString());
        Assert.False(entry.TryGetProperty("threads", out _));
        Assert.False(entry.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task List_AuthorMeAsksAzureDevOpsForTheTokenOwnersPullRequests()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, creator: Me));
        source.Add(Payments, Pr(Payments, 11, creator: Ana));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--author", "me");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([10], Ids(root));
        Assert.All(source.Criteria, criteria => Assert.Equal(Owner, criteria.CreatorId));
    }

    [Fact]
    public async Task List_ReviewerMeWithVoteNoneFindsWhatIStillHaveToReview()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, reviewers: [(Me, 0)]));
        source.Add(Payments, Pr(Payments, 11, reviewers: [(Me, 10)]));
        source.Add(Payments, Pr(Payments, 12, reviewers: [(Ana, 0)]));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--reviewer", "me", "--vote", "none");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([10], Ids(root));
        Assert.All(source.Criteria, criteria => Assert.Equal(Owner, criteria.ReviewerId));
    }

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ANA SMITH")]
    public async Task List_NamedAuthorMatchesTheUniqueNameOrTheDisplayName(string author)
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, creator: Ana));
        source.Add(Payments, Pr(Payments, 11, creator: Bob));

        (_, JsonElement root) = await RunAsync(source, "pr", "list", "--author", author);

        Assert.Equal([10], Ids(root));
        Assert.All(source.Criteria, criteria => Assert.Null(criteria.CreatorId));
    }

    [Fact]
    public async Task List_FiltersByRepositoryAndProject()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, repository: "Api"));
        source.Add(Payments, Pr(Payments, 11, repository: "Web"));
        source.Add(Billing, Pr(Billing, 20, repository: "Api"));

        (_, JsonElement root) = await RunAsync(source, "pr", "list", "--project", "payments", "--repository", "api");

        Assert.Equal([10], Ids(root));
        Assert.DoesNotContain(source.Criteria, criteria => criteria.Project == Billing);
    }

    [Fact]
    public async Task List_ReportsAnUnknownProject()
    {
        (int exitCode, JsonElement root) = await RunAsync(new FakeListSource(), "pr", "list", "--project", "Nope");

        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Equal("projectNotFound", root.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("--status", "merged", "invalidStatus")]
    [InlineData("--vote", "maybe", "invalidVote")]
    [InlineData("--top", "0", "invalidTop")]
    [InlineData("--top", "201", "invalidTop")]
    public async Task List_RejectsInvalidOptionsBeforeCallingAzureDevOps(string option, string value, string code)
    {
        var source = new FakeListSource();
        string[] args = option == "--vote" ? ["pr", "list", "--reviewer", "me", option, value] : ["pr", "list", option, value];

        (int exitCode, JsonElement root) = await RunAsync(source, args);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(code, root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(source.Criteria);
    }

    [Fact]
    public async Task List_RejectsAVoteWithoutAReviewer()
    {
        var source = new FakeListSource();

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--vote", "none");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("conflictingOptions", root.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("abandoned")]
    [InlineData("all")]
    public async Task List_PassesTheStatusToAzureDevOps(string status)
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10));

        await RunAsync(source, "pr", "list", "--status", status);

        Assert.All(source.Criteria, criteria => Assert.Equal(status, criteria.Status));
    }

    [Fact]
    public async Task List_ReadsPagesUntilItHasEnoughMatches()
    {
        var source = new FakeListSource();
        for (int id = 250; id >= 1; id--)
        {
            source.Add(Payments, Pr(Payments, id, created: Day(id)));
        }

        (_, JsonElement root) = await RunAsync(source, "pr", "list", "--top", "150");

        Assert.Equal(150, root.GetProperty("results").GetArrayLength());
        Assert.Equal(250, Ids(root)[0]);
        Assert.Equal([0, 100], source.Criteria.Where(criteria => criteria.Project == Payments).Select(criteria => criteria.Skip).Order());
    }

    [Fact]
    public async Task List_IsPartialWhenItStoppedScanningAProjectBeforeFindingEnough()
    {
        var source = new FakeListSource();
        for (int id = 1200; id >= 1; id--)
        {
            source.Add(Payments, Pr(Payments, id, creator: id == 1150 ? Ana : Bob));
        }

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--author", "ana@example.com", "--status", "all");

        Assert.Equal(ExitCodes.Partial, exitCode);
        JsonElement error = root.GetProperty("errors")[0];
        Assert.Equal("scanLimitReached", error.GetProperty("code").GetString());
        Assert.Equal("Payments", error.GetProperty("project").GetString());
        Assert.Equal(10, source.Criteria.Count(criteria => criteria.Project == Payments));
    }

    [Fact]
    public async Task List_IsPartialWhenOneProjectCannotBeRead()
    {
        var source = new FakeListSource { BrokenProject = Billing };
        source.Add(Payments, Pr(Payments, 10));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list");

        Assert.Equal(ExitCodes.Partial, exitCode);
        Assert.Equal([10], Ids(root));
        Assert.Equal("Billing", root.GetProperty("errors")[0].GetProperty("project").GetString());
    }

    [Fact]
    public async Task List_KeepsPeopleOutOfTheOutputWhenPeopleAreWithheld()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, creator: Ana, reviewers: [(Ana, 0)]));

        (string json, int exitCode) = await RunRawAsync(source, "pr", "list", "--author", "ana@example.com", "--withhold", "people");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("\"id\":10", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ana", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_ActivityAddsChecksAndThreadsToEachEntry()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--activity");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("pullRequestList", root.GetProperty("kind").GetString());
        JsonElement entry = root.GetProperty("results")[0];
        Assert.Equal("succeeded", entry.GetProperty("mergeStatus").GetString());
        JsonElement check = entry.GetProperty("checks")[0];
        Assert.Equal("CI", check.GetProperty("name").GetString());
        Assert.Equal("approved", check.GetProperty("status").GetString());
        Assert.True(check.GetProperty("isExpired").GetBoolean());
        JsonElement thread = entry.GetProperty("threads")[0];
        Assert.Equal(7, thread.GetProperty("id").GetInt32());
        Assert.Equal("active", thread.GetProperty("status").GetString());
        Assert.Equal(2, thread.GetProperty("commentCount").GetInt32());
        Assert.Equal(31, thread.GetProperty("latestCommentId").GetInt32());
        Assert.True(thread.TryGetProperty("latestContentChangeAtUtc", out _));
        Assert.Equal(PolicyEvaluations.BuildPolicyType, check.GetProperty("typeId").GetGuid());
        Assert.Equal("source-commit-10", entry.GetProperty("sourceCommitId").GetString());
        Assert.Equal([10], source.ActivityReads);
    }

    [Fact]
    public async Task List_ActivityKeepsTheRowsItReadWhenTheTimeRunsOut()
    {
        var source = new FakeListSource { HangingActivityId = 11, RequestTimeoutSeconds = 1 };
        source.Add(Payments, Pr(Payments, 10, created: Day(1)));
        source.Add(Payments, Pr(Payments, 11, created: Day(2)));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--activity");

        Assert.Equal(ExitCodes.Partial, exitCode);
        Assert.Equal([11, 10], Ids(root));
        Assert.True(root.GetProperty("results")[1].TryGetProperty("checks", out _));
        JsonElement error = root.GetProperty("errors")[0];
        Assert.Equal("timeout", error.GetProperty("code").GetString());
        Assert.Contains("11", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_WithoutActivityReadsNoChecksOrThreads()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10));

        (_, JsonElement root) = await RunAsync(source, "pr", "list");

        Assert.Empty(source.ActivityReads);
        Assert.False(root.GetProperty("results")[0].TryGetProperty("checks", out _));
        Assert.False(root.GetProperty("results")[0].TryGetProperty("threads", out _));
    }

    [Fact]
    public async Task List_ActivityIsPartialWhenOnePullRequestsActivityCannotBeRead()
    {
        var source = new FakeListSource { BrokenActivityId = 11 };
        source.Add(Payments, Pr(Payments, 10, created: Day(1)));
        source.Add(Payments, Pr(Payments, 11, created: Day(2)));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--activity");

        Assert.Equal(ExitCodes.Partial, exitCode);
        Assert.Equal([11, 10], Ids(root));
        Assert.False(root.GetProperty("results")[0].TryGetProperty("checks", out _));
        Assert.True(root.GetProperty("results")[1].TryGetProperty("checks", out _));
        Assert.Contains("11", root.GetProperty("errors")[0].GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_RejectsActivityWithFull()
    {
        var source = new FakeListSource();

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--activity", "--full");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("conflictingOptions", root.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_FullReturnsTheShowDocumentOfEveryMatch()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10, created: Day(1)));
        source.Add(Payments, Pr(Payments, 11, created: Day(2)));

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--full");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("pullRequestSearch", root.GetProperty("kind").GetString());
        Assert.Equal([11, 10], Ids(root));
        Assert.Equal(1, root.GetProperty("results")[0].GetProperty("threads").GetArrayLength());
    }

    [Fact]
    public async Task List_FullKeepsItsKindAndSchemaWhenTheInputIsInvalid()
    {
        (_, JsonElement root) = await RunAsync(new FakeListSource(), "pr", "list", "--full", "--top", "0");

        Assert.Equal("pullRequestSearch", root.GetProperty("kind").GetString());
        Assert.Equal("2.0", root.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task List_FullReadsEachPullRequestWholeNotTheShortenedListCopy()
    {
        var source = new FakeListSource();
        source.Add(Payments, Pr(Payments, 10) with { Document = Pr(Payments, 10).Document with { Description = "The whole de" } });

        (_, JsonElement root) = await RunAsync(source, "pr", "list", "--full");

        Assert.Equal("The whole description.", root.GetProperty("results")[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task List_NeverReturnsAPullRequestTwiceWhenOneOpensBetweenPages()
    {
        var source = new FakeListSource { OpenedBetweenPages = Pr(Payments, 1000, created: Day(1000)) };
        for (int id = 150; id >= 1; id--)
        {
            source.Add(Payments, Pr(Payments, id, created: Day(id)));
        }

        (_, JsonElement root) = await RunAsync(source, "pr", "list", "--project", "Payments", "--top", "101");

        int[] ids = Ids(root);
        Assert.Equal(101, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public async Task List_IsPartialNotAnErrorWhenTheScanStoppedBeforeAnyMatch()
    {
        var source = new FakeListSource();
        for (int id = 1001; id >= 1; id--)
        {
            source.Add(Payments, Pr(Payments, id, creator: Bob));
        }

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", "--author", "ana@example.com");

        Assert.Equal(ExitCodes.Partial, exitCode);
        Assert.Equal("partial", root.GetProperty("status").GetString());
        Assert.DoesNotContain("--repository", root.GetProperty("errors")[0].GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--vote")]
    [InlineData("--author")]
    [InlineData("--reviewer")]
    [InlineData("--repository")]
    [InlineData("--project")]
    public async Task List_RejectsABlankFilterInsteadOfDroppingIt(string option)
    {
        var source = new FakeListSource();

        (int exitCode, JsonElement root) = await RunAsync(source, "pr", "list", option, " ");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("emptyFilter", root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(source.Criteria);
    }

    private static readonly Person Me = new(Owner, "me@example.com", "Me Myself");
    private static readonly Person Ana = new(Guid.NewGuid(), "ana@example.com", "Ana Smith");
    private static readonly Person Bob = new(Guid.NewGuid(), "bob@example.com", "Bob Jones");

    private sealed record Person(Guid Id, string UniqueName, string DisplayName)
    {
        public PullRequestPerson ToRef() => new(Id, UniqueName, DisplayName);
    }

    private static DateTimeOffset Day(int day) => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day);

    private static PullRequestCandidate Pr(
        AdoProject project,
        int id,
        DateTimeOffset? created = null,
        Person? creator = null,
        (Person Person, int Vote)[]? reviewers = null,
        string repository = "Repo")
    {
        creator ??= Bob;
        reviewers ??= [];
        PullRequestDocument document = TestPullRequests.Create(project, id, repository) with
        {
            CreatedAtUtc = created ?? Day(0),
            SourceCommitId = $"source-commit-{id}",
            CreatorUniqueName = creator.UniqueName,
            Reviewers = reviewers
                .Select(reviewer => new PullRequestReviewerDocument(reviewer.Person.UniqueName, reviewer.Vote, "noVote", false, false, null))
                .ToArray()
        };
        return new PullRequestCandidate(
            document,
            creator.ToRef(),
            reviewers.Select(reviewer => new PullRequestReviewerVote(reviewer.Person.ToRef(), reviewer.Vote)).ToArray());
    }

    private static int[] Ids(JsonElement root) =>
        root.GetProperty("results").EnumerateArray().Select(entry => entry.GetProperty("id").GetInt32()).ToArray();

    private static async Task<(int ExitCode, JsonElement Root)> RunAsync(FakeListSource source, params string[] args)
    {
        (string json, int exitCode) = await RunRawAsync(source, args);
        using JsonDocument document = JsonDocument.Parse(json);
        return (exitCode, document.RootElement.Clone());
    }

    private static async Task<(string Json, int ExitCode)> RunRawAsync(FakeListSource source, params string[] args)
    {
        var writer = new StringWriter();
        CommandApplication application = TestTool.Umbrella(
            new ConfiguredStore(source.RequestTimeoutSeconds),
            new JsonOutput(writer),
            TimeProvider.System,
            new FakePatProvider(),
            new FakeSessionFactory(source));
        int exitCode = await application.InvokeAsync([.. args, "--compact"], CancellationToken.None);
        return (writer.ToString(), exitCode);
    }

    private sealed record RecordedCriteria(AdoProject Project, string Status, Guid? CreatorId, Guid? ReviewerId, int Skip);

    private sealed class FakeListSource : IPullRequestSource, IProjectCatalog
    {
        private readonly Dictionary<Guid, List<PullRequestCandidate>> _byProject = [];

        public AdoProject? BrokenProject { get; init; }

        public PullRequestCandidate? OpenedBetweenPages { get; set; }

        public List<RecordedCriteria> Criteria { get; } = [];

        public void Add(AdoProject project, PullRequestCandidate candidate)
        {
            if (!_byProject.TryGetValue(project.Id, out List<PullRequestCandidate>? list))
            {
                _byProject[project.Id] = list = [];
            }

            list.Add(candidate);
        }

        public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdoProject>>([Billing, Payments]);

        public Task<IReadOnlyList<PullRequestCandidate>> ListAsync(
            AdoProject project,
            PullRequestSearch search,
            int skip,
            int top,
            CancellationToken cancellationToken)
        {
            lock (Criteria)
            {
                Criteria.Add(new RecordedCriteria(project, search.Status, search.CreatorId, search.ReviewerId, skip));
            }

            if (project == BrokenProject)
            {
                throw new PullRequestSourceException("forbidden", "Cannot read this project.", false);
            }

            // A pull request opened between two page reads shifts every later page by one.
            if (skip > 0 && OpenedBetweenPages is { } opened)
            {
                Add(project, opened);
                OpenedBetweenPages = null;
            }

            // Azure DevOps applies the creator and reviewer IDs itself and returns the newest first.
            IEnumerable<PullRequestCandidate> matches = _byProject.GetValueOrDefault(project.Id, [])
                .Where(candidate => search.CreatorId is null || candidate.Creator.Id == search.CreatorId)
                .Where(candidate => search.ReviewerId is null || candidate.Reviewers.Any(reviewer => reviewer.Reviewer.Id == search.ReviewerId))
                .OrderByDescending(candidate => candidate.Document.Id);
            return Task.FromResult<IReadOnlyList<PullRequestCandidate>>(matches.Skip(skip).Take(top).ToArray());
        }

        public int? BrokenActivityId { get; init; }

        /// <summary>A pull request whose activity read never finishes, so the command's time runs out on it.</summary>
        public int? HangingActivityId { get; init; }

        public int RequestTimeoutSeconds { get; init; } = AdoReadConfiguration.DefaultRequestTimeoutSeconds;

        public List<int> ActivityReads { get; } = [];

        public async Task<PullRequestActivityDocument> GetActivityAsync(PullRequestDocument pullRequest, CancellationToken cancellationToken)
        {
            lock (ActivityReads)
            {
                ActivityReads.Add(pullRequest.Id);
            }

            if (pullRequest.Id == HangingActivityId)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return pullRequest.Id == BrokenActivityId
                ? throw new PullRequestSourceException("forbidden", "Cannot read the policies.", false)
                : new PullRequestActivityDocument(
                    [new PullRequestCheckDocument(Guid.NewGuid(), "CI", PolicyEvaluations.BuildPolicyType, "Build", "approved", IsExpired: true, IsBlocking: true, BuildId: 501)],
                    [new PullRequestThreadActivityDocument(7, "active", 2, 31, Day(5), Day(4))]);
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(PullRequestDocument pullRequest, CancellationToken cancellationToken) =>
            Task.FromResult(pullRequest with
            {
                Threads = [new PullRequestThreadDocument(1, "active", false, false, null, null, null, null, null, [])]
            });

        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <summary>The single-PR read; unlike a listed page it carries the whole description.</summary>
        public Task<PullRequestDocument?> TryGetAsync(AdoProject project, int id, CancellationToken cancellationToken)
        {
            PullRequestCandidate? candidate = _byProject.GetValueOrDefault(project.Id, []).FirstOrDefault(item => item.Document.Id == id);
            return Task.FromResult(candidate is null ? null : candidate.Document with { Description = "The whole description." });
        }
    }

    private sealed class ConfiguredStore(int requestTimeoutSeconds = AdoReadConfiguration.DefaultRequestTimeoutSeconds) : IConfigurationStore
    {
        public string DefaultPath => "unused";

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(new Uri("https://dev.azure.com/example"), requestTimeoutSeconds));

        public Task SaveAsync(
            string? overridePath,
            AdoReadConfiguration configuration,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePatProvider : IPatProvider
    {
        public string GetRequiredPat(Uri organizationUrl) => "test-pat";
    }

    private sealed class FakeSessionFactory(FakeListSource source) : IAdoSessionFactory
    {
        public IAdoSession Create(Uri organizationUrl, string pat) => new Session(source);

        private sealed class Session(FakeListSource source) : IAdoSession
        {
            public IProjectCatalog Projects => source;

            public IPullRequestSource PullRequests => source;

            public IWorkItemSource WorkItems => throw new NotSupportedException();

            public ISprintSource Sprints => throw new NotSupportedException();

            public ITeamClient Teams => throw new NotSupportedException();

            public Task<Guid> GetTokenOwnerIdAsync(CancellationToken cancellationToken) => Task.FromResult(Owner);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
