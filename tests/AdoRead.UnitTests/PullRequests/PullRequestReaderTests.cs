using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.PullRequests;

namespace AdoRead.UnitTests.PullRequests;

public sealed class PullRequestReaderTests
{
    private static readonly Uri OrganizationUrl = new("https://dev.azure.com/example/");
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadAsync_OrganizationScopedHitResolvesTheProjectWithoutScanning()
    {
        var catalog = new FakeProjectCatalog([new AdoProject(Guid.NewGuid(), "Alpha", null)]);
        var reader = new PullRequestReader(
            catalog,
            new DirectHitSource(),
            OrganizationUrl,
            2,
            new FixedTimeProvider(Now));

        var result = await reader.ReadAsync(42, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("Payments", Assert.Single(result.Results).Project.Name);
        Assert.Empty(result.Errors);
        Assert.False(catalog.WasListed);
    }

    [Fact]
    public async Task ReadAsync_ReturnsEveryMatchInDeterministicOrder()
    {
        AdoProject[] projects =
        [
            new(Guid.NewGuid(), "Zulu", null),
            new(Guid.NewGuid(), "Alpha", null),
            new(Guid.NewGuid(), "Beta", null)
        ];
        var source = new FakePullRequestSource(new Dictionary<string, ProbeBehavior>
        {
            ["Zulu"] = ProbeBehavior.Match("Repo B"),
            ["Alpha"] = ProbeBehavior.Match("Repo Z"),
            ["Beta"] = ProbeBehavior.Match("Repo A")
        });
        PullRequestReader reader = CreateReader(projects, source, maxParallelProjects: 2);

        var result = await reader.ReadAsync(42, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal(["Alpha", "Beta", "Zulu"], result.Results.Select(item => item.Project.Name));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadAsync_PreservesMatchesAndProjectErrorsAsPartial()
    {
        AdoProject[] projects =
        [
            new(Guid.NewGuid(), "Readable", null),
            new(Guid.NewGuid(), "Forbidden", null)
        ];
        var source = new FakePullRequestSource(new Dictionary<string, ProbeBehavior>
        {
            ["Readable"] = ProbeBehavior.Match("Repo"),
            ["Forbidden"] = ProbeBehavior.Failure("forbidden", transient: false)
        });
        PullRequestReader reader = CreateReader(projects, source, maxParallelProjects: 2);

        var result = await reader.ReadAsync(42, CancellationToken.None);

        Assert.Equal("partial", result.Status);
        Assert.Single(result.Results);
        Assert.Equal("Forbidden", Assert.Single(result.Errors).Project);
    }

    [Fact]
    public async Task ReadAsync_CompleteSearchWithoutMatchesReturnsNotFound()
    {
        AdoProject[] projects = [new(Guid.NewGuid(), "Alpha", null)];
        PullRequestReader reader = CreateReader(
            projects,
            new FakePullRequestSource(new Dictionary<string, ProbeBehavior>()),
            1);

        var result = await reader.ReadAsync(404, CancellationToken.None);

        Assert.Equal("notFound", result.Status);
        Assert.Empty(result.Results);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadAsync_NoResultsWithAnyIncompleteProjectReturnsError()
    {
        AdoProject[] projects =
        [
            new(Guid.NewGuid(), "Missing", null),
            new(Guid.NewGuid(), "Broken", null)
        ];
        var source = new FakePullRequestSource(new Dictionary<string, ProbeBehavior>
        {
            ["Broken"] = ProbeBehavior.Failure("serviceUnavailable", transient: true)
        });
        PullRequestReader reader = CreateReader(projects, source, 2);

        var result = await reader.ReadAsync(42, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Empty(result.Results);
        Assert.Equal("serviceUnavailable", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_OrganizationLevelProjectDiscoveryFailureReturnsFatalError()
    {
        var reader = new PullRequestReader(
            new FailedProjectCatalog(),
            new FakePullRequestSource(new Dictionary<string, ProbeBehavior>()),
            OrganizationUrl,
            2,
            new FixedTimeProvider(Now));

        var result = await reader.ReadAsync(42, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Empty(result.Results);
        Assert.Equal("unauthorized", Assert.Single(result.Errors).Code);
        Assert.Equal("azureDevOps", result.Errors[0].Scope);
    }

    [Fact]
    public async Task ReadAsync_UsesConfiguredBoundedConcurrency()
    {
        AdoProject[] projects = Enumerable.Range(1, 6)
            .Select(index => new AdoProject(Guid.NewGuid(), $"Project {index}", null))
            .ToArray();
        var source = new ControlledConcurrencySource(expectedStarts: 2);
        PullRequestReader reader = CreateReader(projects, source, maxParallelProjects: 2);

        Task<SearchEnvelope<PullRequestDocument>> read = reader.ReadAsync(42, CancellationToken.None);
        await source.WaitForExpectedStartsAsync();

        Assert.Equal(2, source.MaximumConcurrency);
        source.ReleaseAll();
        await read;
        Assert.Equal(2, source.MaximumConcurrency);
    }

    [Fact]
    public async Task ReadAsync_SettlesOutstandingProjectReadsWhenCancellationIsRequested()
    {
        AdoProject[] projects = [new(Guid.NewGuid(), "Blocked", null)];
        var source = new CancelledPullRequestSource();
        PullRequestReader reader = CreateReader(projects, source, 1);
        using var cancellation = new CancellationTokenSource();

        Task<SearchEnvelope<PullRequestDocument>> read = reader.ReadAsync(42, cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var result = await read;

        Assert.Equal("error", result.Status);
        Assert.Equal("cancelled", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadAsync_CancellationPreservesCompletedMatchesAndMarksUnfinishedProjects()
    {
        AdoProject[] projects =
        [
            new(Guid.NewGuid(), "Fast", null),
            new(Guid.NewGuid(), "Slow", null)
        ];
        var source = new PartiallyCompletedSource();
        PullRequestReader reader = CreateReader(projects, source, 2);
        using var cancellation = new CancellationTokenSource();

        Task<SearchEnvelope<PullRequestDocument>> read = reader.ReadAsync(42, cancellation.Token);
        await source.FastCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var result = await read;

        Assert.Equal("partial", result.Status);
        Assert.Equal("Fast", Assert.Single(result.Results).Project.Name);
        Assert.Equal("Slow", Assert.Single(result.Errors).Project);
        Assert.Equal("cancelled", result.Errors[0].Code);
    }

    private static PullRequestReader CreateReader(
        IReadOnlyList<AdoProject> projects,
        IPullRequestSource source,
        int maxParallelProjects) =>
        new(
            new FakeProjectCatalog(projects),
            source,
            OrganizationUrl,
            maxParallelProjects,
            new FixedTimeProvider(Now));

    private sealed class FakeProjectCatalog(IReadOnlyList<AdoProject> projects) : IProjectCatalog
    {
        public bool WasListed { get; private set; }

        public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken)
        {
            WasListed = true;
            return Task.FromResult(projects);
        }
    }

    /// <summary>
    /// Answers the organization-scoped lookup and fails loudly if the caller scans anyway.
    /// </summary>
    private sealed class DirectHitSource : IPullRequestSource
    {
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(
                TestPullRequests.Create(new AdoProject(Guid.NewGuid(), "Payments", null), id, "App"));

        public Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken) => throw new NotSupportedException("Should not scan.");

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken) => Task.FromResult(pullRequest);
    }

    private sealed class FailedProjectCatalog : IProjectCatalog
    {
        public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<AdoProject>>(
                new ProjectCatalogException("unauthorized", "The PAT was rejected.", false));
    }

    private sealed class FakePullRequestSource(
        IReadOnlyDictionary<string, ProbeBehavior> behaviors) : IPullRequestSource
    {
        // The organization-scoped fast path is not under test here; returning null
        // exercises the project scan.
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken)
        {
            if (!behaviors.TryGetValue(project.Name, out ProbeBehavior? behavior))
            {
                return Task.FromResult<PullRequestDocument?>(null);
            }

            if (behavior.Exception is not null)
            {
                return Task.FromException<PullRequestDocument?>(behavior.Exception);
            }

            return Task.FromResult<PullRequestDocument?>(
                CreatePullRequest(project, id, behavior.RepositoryName!));
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken) => Task.FromResult(pullRequest);
    }

    private sealed class ControlledConcurrencySource(int expectedStarts) : IPullRequestSource
    {
        private readonly TaskCompletionSource _expectedStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _started;
        private int _maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public Task WaitForExpectedStartsAsync() => _expectedStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void ReleaseAll() => _release.TrySetResult();

        // The organization-scoped fast path is not under test here; returning null
        // exercises the project scan.
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public async Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            if (Interlocked.Increment(ref _started) == expectedStarts)
            {
                _expectedStarted.TrySetResult();
            }

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private void UpdateMaximum(int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _maximumConcurrency);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maximumConcurrency, value, current) != current);
        }
    }

    private sealed class CancelledPullRequestSource : IPullRequestSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The organization-scoped fast path is not under test here; returning null
        // exercises the project scan.
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public async Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PartiallyCompletedSource : IPullRequestSource
    {
        public TaskCompletionSource FastCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The organization-scoped fast path is not under test here; returning null
        // exercises the project scan.
        public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PullRequestDocument?>(null);

        public async Task<PullRequestDocument?> TryGetAsync(
            AdoProject project,
            int id,
            CancellationToken cancellationToken)
        {
            if (project.Name == "Slow")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return CreatePullRequest(project, id, "Repo");
        }

        public Task<PullRequestDocument> GetAllDetailsAsync(
            PullRequestDocument pullRequest,
            CancellationToken cancellationToken)
        {
            FastCompleted.TrySetResult();
            return Task.FromResult(pullRequest);
        }
    }

    private static PullRequestDocument CreatePullRequest(AdoProject project, int id, string repositoryName) =>
        TestPullRequests.Create(project, id, repositoryName);

    private sealed record ProbeBehavior(
        string? RepositoryName,
        PullRequestSourceException? Exception)
    {
        public static ProbeBehavior Match(string repositoryName) => new(repositoryName, null);

        public static ProbeBehavior Failure(string code, bool transient) =>
            new(null, new PullRequestSourceException(code, "Safe failure.", transient));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
