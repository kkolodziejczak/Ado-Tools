using System.Net;
using AdoRead.AzureDevOps;
using AdoRead.PullRequests;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class PullRequestSourceTests
{
    private static readonly Uri OrganizationUrl = TestPullRequests.OrganizationUrl;

    [Fact]
    public async Task TryGetAsync_UsesProjectScopedLookupAndMapsReviewMetadata()
    {
        Guid projectId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        var client = new FakeGitClient
        {
            PullRequest = new GitPullRequest
            {
                PullRequestId = 42,
                Title = "PR",
                Description = "Description",
                Status = PullRequestStatus.Active,
                MergeStatus = PullRequestAsyncStatus.Succeeded,
                IsDraft = true,
                Repository = new GitRepository { Id = repositoryId, Name = "App" },
                CreatedBy = new IdentityRef { DisplayName = "Creator", Id = "creator" },
                Reviewers =
                [
                    new IdentityRefWithVote
                    {
                        DisplayName = "Reviewer",
                        Id = "reviewer",
                        Vote = -5,
                        IsRequired = true,
                        IsContainer = false
                    }
                ]
            }
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument? result = await source.TryGetAsync(
            new AdoProject(projectId, "Payments", null),
            42,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(projectId, client.ProjectId);
        Assert.Equal(42, client.PullRequestId);
        Assert.Equal(repositoryId, result.Repository.Id);
        Assert.Equal(OrganizationUrl, result.OrganizationUrl);
        Assert.Equal(-5, Assert.Single(result.Reviewers).Vote);
        Assert.Equal("waitingForAuthor", result.Reviewers[0].ReviewStatus);
        Assert.True(result.Reviewers[0].IsRequired);
    }

    [Fact]
    public async Task GetAllDetailsAsync_MapsCurrentThreadProvenanceAndAvoidsRemovedHistoryCalls()
    {
        var client = new FakeGitClient
        {
            Iterations =
            [
                new GitPullRequestIteration
                {
                    Id = 1,
                    SourceRefCommit = new GitCommitRef { CommitId = "reviewed" }
                },
                new GitPullRequestIteration
                {
                    Id = 2,
                    SourceRefCommit = new GitCommitRef { CommitId = "latest" }
                }
            ],
            Threads =
            [
                CreateThread(secondComparingIteration: 2)
            ]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(
            CreatePullRequest(),
            CancellationToken.None);

        PullRequestThreadDocument thread = Assert.Single(result.Threads);
        Assert.False(thread.IsPossiblyOutdated);
        Assert.Equal("latest", thread.ReviewedCommitId);
        Assert.Equal("latest", thread.LatestCommitId);
        Assert.Equal("/src/RetryPolicy.cs", thread.FilePath);
        Assert.Equal(12, thread.StartLine);
        Assert.Equal(12, thread.EndLine);
    }

    [Fact]
    public async Task GetAllDetailsAsync_MapsOlderThreadProvenance()
    {
        var client = new FakeGitClient
        {
            Iterations =
            [
                new GitPullRequestIteration { Id = 1, SourceRefCommit = new GitCommitRef { CommitId = "reviewed" } },
                new GitPullRequestIteration { Id = 2, SourceRefCommit = new GitCommitRef { CommitId = "latest" } }
            ],
            Threads = [CreateThread(secondComparingIteration: 1)]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(CreatePullRequest(), CancellationToken.None);

        PullRequestThreadDocument thread = Assert.Single(result.Threads);
        Assert.True(thread.IsPossiblyOutdated);
        Assert.Equal("reviewed", thread.ReviewedCommitId);
        Assert.Equal("latest", thread.LatestCommitId);
    }

    [Fact]
    public async Task GetAllDetailsAsync_LeavesUnavailableProvenanceNullable()
    {
        var client = new FakeGitClient
        {
            Threads = [CreateThread(secondComparingIteration: null)]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(CreatePullRequest(), CancellationToken.None);

        PullRequestThreadDocument thread = Assert.Single(result.Threads);
        Assert.Null(thread.IsPossiblyOutdated);
        Assert.Null(thread.ReviewedCommitId);
        Assert.Null(thread.LatestCommitId);
    }

    [Fact]
    public async Task GetAllDetailsAsync_LeavesMissingSourceCommitProvenanceNullable()
    {
        var client = new FakeGitClient
        {
            Iterations =
            [
                new GitPullRequestIteration { Id = 1 },
                new GitPullRequestIteration { Id = 2, SourceRefCommit = new GitCommitRef { CommitId = "  " } }
            ],
            Threads = [CreateThread(secondComparingIteration: 1)]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(CreatePullRequest(), CancellationToken.None);

        PullRequestThreadDocument thread = Assert.Single(result.Threads);
        Assert.Null(thread.IsPossiblyOutdated);
        Assert.Null(thread.ReviewedCommitId);
        Assert.Null(thread.LatestCommitId);
    }

    [Fact]
    public async Task GetAllDetailsAsync_DoesNotTreatAnOlderSourceCommitAsLatest()
    {
        var client = new FakeGitClient
        {
            Iterations =
            [
                new GitPullRequestIteration { Id = 1, SourceRefCommit = new GitCommitRef { CommitId = "reviewed" } },
                new GitPullRequestIteration { Id = 2 }
            ],
            Threads = [CreateThread(secondComparingIteration: 1)]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(CreatePullRequest(), CancellationToken.None);

        PullRequestThreadDocument thread = Assert.Single(result.Threads);
        Assert.Equal("reviewed", thread.ReviewedCommitId);
        Assert.Null(thread.LatestCommitId);
        Assert.Null(thread.IsPossiblyOutdated);
    }

    [Fact]
    public async Task GetAllDetailsAsync_LeavesNonpositiveIterationProvenanceNullable()
    {
        var client = new FakeGitClient
        {
            Iterations = [new GitPullRequestIteration { Id = 0, SourceRefCommit = new GitCommitRef { CommitId = "ignored" } }],
            Threads = [CreateThread(secondComparingIteration: 0)]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(CreatePullRequest(), CancellationToken.None);

        PullRequestThreadDocument thread = Assert.Single(result.Threads);
        Assert.Null(thread.IsPossiblyOutdated);
        Assert.Null(thread.ReviewedCommitId);
        Assert.Null(thread.LatestCommitId);
    }

    [Fact]
    public async Task GetAllDetailsAsync_PreservesEverySdkThreadStatusIncludingUnknown()
    {
        CommentThreadStatus[] statuses = Enum.GetValues<CommentThreadStatus>();
        var client = new FakeGitClient
        {
            Threads = statuses.Select((status, index) => new GitPullRequestCommentThread
            {
                Id = index + 1,
                Status = status
            }).ToArray()
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument result = await source.GetAllDetailsAsync(
            CreatePullRequest(),
            CancellationToken.None);

        Assert.Equal(
            ["unknown", "active", "fixed", "wontFix", "closed", "byDesign", "pending"],
            result.Threads.Select(thread => thread.Status));
    }

    [Fact]
    public async Task TryGetAsync_MapsOnlyUniqueNamesForRetainedIdentities()
    {
        var client = new FakeGitClient
        {
            PullRequest = new GitPullRequest
            {
                PullRequestId = 42,
                Repository = new GitRepository { Id = Guid.NewGuid(), Name = "App" },
                CreatedBy = new IdentityRef
                {
                    DisplayName = "Creator display name",
                    Id = "creator-id",
                    UniqueName = "creator@example.com"
                },
                Reviewers =
                [
                    new IdentityRefWithVote
                    {
                        DisplayName = "Reviewer display name",
                        Id = "reviewer-id",
                        UniqueName = "reviewer@example.com"
                    }
                ]
            },
            Threads =
            [
                CreateThread(secondComparingIteration: null, author: new IdentityRef
                {
                    DisplayName = "Comment display name",
                    Id = "comment-id",
                    UniqueName = "commenter@example.com"
                })
            ]
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument? shallow = await source.TryGetAsync(
            new AdoProject(Guid.NewGuid(), "Payments", null),
            42,
            CancellationToken.None);
        PullRequestDocument result = await source.GetAllDetailsAsync(shallow!, CancellationToken.None);

        Assert.Equal("creator@example.com", shallow!.CreatorUniqueName);
        Assert.Equal("reviewer@example.com", Assert.Single(shallow.Reviewers).UniqueName);
        Assert.Equal("commenter@example.com", Assert.Single(Assert.Single(result.Threads).Comments).AuthorUniqueName);
    }

    [Fact]
    public async Task TryGetByIdAsync_ResolvesTheProjectFromTheOrganizationScopedResponse()
    {
        Guid projectId = Guid.NewGuid();
        var client = new FakeGitClient
        {
            PullRequestById = new GitPullRequest
            {
                PullRequestId = 42,
                Title = "PR",
                Status = PullRequestStatus.Active,
                Repository = new GitRepository
                {
                    Id = Guid.NewGuid(),
                    Name = "App",
                    ProjectReference = new TeamProjectReference { Id = projectId, Name = "Payments" }
                }
            }
        };
        var source = new PullRequestSource(client, OrganizationUrl);

        PullRequestDocument? result = await source.TryGetByIdAsync(42, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(42, client.ByIdRequested);
        Assert.Equal(projectId, result.Project.Id);
        Assert.Equal("Payments", result.Project.Name);
        Assert.Equal("App", result.Repository.Name);
    }

    [Fact]
    public async Task TryGetByIdAsync_ReturnsNullSoTheCallerFallsBackToScanning()
    {
        var source = new PullRequestSource(new FakeGitClient(), OrganizationUrl);

        Assert.Null(await source.TryGetByIdAsync(42, CancellationToken.None));
    }

    private static PullRequestDocument CreatePullRequest() =>
        TestPullRequests.Create(new AdoProject(Guid.NewGuid(), "Payments", null), 42, "App");

    private static GitPullRequestCommentThread CreateThread(short? secondComparingIteration, IdentityRef? author = null) =>
        new()
        {
            Id = 7,
            Status = CommentThreadStatus.Active,
            ThreadContext = new CommentThreadContext
            {
                FilePath = "/src/RetryPolicy.cs",
                RightFileStart = new CommentPosition { Line = 12 },
                RightFileEnd = new CommentPosition { Line = 12 }
            },
            PullRequestThreadContext = secondComparingIteration is null
                ? null
                : new GitPullRequestCommentThreadContext
                {
                    IterationContext = new CommentIterationContext
                    {
                        SecondComparingIteration = secondComparingIteration.Value
                    }
                },
            Comments =
            [
                new Comment { Id = 1, ParentCommentId = 0, Author = author }
            ]
        };

    private sealed class FakeGitClient : IPullRequestGitClient
    {

        public GitPullRequest PullRequest { get; init; } = new();

        public GitPullRequest? PullRequestById { get; init; }

        public int? ByIdRequested { get; private set; }

        public IReadOnlyList<GitPullRequestIteration> Iterations { get; init; } = [];

        public IReadOnlyList<GitPullRequestCommentThread> Threads { get; init; } = [];

        public Guid? ProjectId { get; private set; }

        public int? PullRequestId { get; private set; }

        public Task<GitPullRequest> GetByIdAsync(int pullRequestId, CancellationToken cancellationToken)
        {
            ByIdRequested = pullRequestId;
            return PullRequestById is null
                ? Task.FromException<GitPullRequest>(
                    new VssServiceResponseException(HttpStatusCode.NotFound, "not found", null))
                : Task.FromResult(PullRequestById);
        }

        public Task<GitPullRequest> GetPullRequestAsync(
            Guid projectId,
            int pullRequestId,
            CancellationToken cancellationToken)
        {
            ProjectId = projectId;
            PullRequestId = pullRequestId;
            return Task.FromResult(PullRequest);
        }

        public Task<IReadOnlyList<ResourceRef>> GetWorkItemsAsync(
            Guid projectId,
            Guid repositoryId,
            int pullRequestId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ResourceRef>>([]);

        public Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(
            Guid projectId,
            Guid repositoryId,
            int pullRequestId,
            CancellationToken cancellationToken) => Task.FromResult(Threads);

        public Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(
            Guid projectId,
            Guid repositoryId,
            int pullRequestId,
            CancellationToken cancellationToken) => Task.FromResult(Iterations);

    }
}
