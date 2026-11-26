using System.Net;
using AdoRead.AzureDevOps;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class PullRequestSourceListTests
{
    private static readonly AdoProject Project = new(Guid.NewGuid(), "Payments", null);

    [Theory]
    [InlineData("active", PullRequestStatus.Active)]
    [InlineData("completed", PullRequestStatus.Completed)]
    [InlineData("abandoned", PullRequestStatus.Abandoned)]
    [InlineData("all", PullRequestStatus.All)]
    public async Task ListAsync_SendsTheStatusAndIdentitiesToAzureDevOps(string status, PullRequestStatus expected)
    {
        var creator = Guid.NewGuid();
        var client = new ListClient();

        await new PullRequestSource(client, new Uri("https://dev.azure.com/example/"))
            .ListAsync(Project, new PullRequestSearch(status, creator, null), 100, 50, CancellationToken.None);

        Assert.Equal(expected, client.Criteria!.Status);
        Assert.Equal(creator, client.Criteria.CreatorId);
        Assert.Null(client.Criteria.ReviewerId);
        Assert.Equal((Project.Id, 100, 50), (client.ProjectId, client.Skip, client.Top));
    }

    [Fact]
    public async Task ListAsync_KeepsTheIdentitiesAndVotesTheDocumentLeavesOut()
    {
        var creatorId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var client = new ListClient
        {
            Page =
            [
                new GitPullRequest
                {
                    PullRequestId = 7,
                    Title = "Seven",
                    Repository = new GitRepository { Id = Guid.NewGuid(), Name = "Repo" },
                    CreatedBy = new IdentityRef { Id = creatorId.ToString(), UniqueName = "ana@example.com", DisplayName = "Ana" },
                    Reviewers = [new IdentityRefWithVote { Id = reviewerId.ToString(), UniqueName = "bob@example.com", DisplayName = "Bob", Vote = -5 }]
                }
            ]
        };

        IReadOnlyList<PullRequestCandidate> page = await new PullRequestSource(client, new Uri("https://dev.azure.com/example/"))
            .ListAsync(Project, new PullRequestSearch("active", null, null), 0, 100, CancellationToken.None);

        PullRequestCandidate candidate = Assert.Single(page);
        Assert.Equal(7, candidate.Document.Id);
        Assert.Equal("Payments", candidate.Document.Project.Name);
        Assert.Equal(new PullRequestPerson(creatorId, "ana@example.com", "Ana"), candidate.Creator);
        Assert.Equal(new PullRequestReviewerVote(new PullRequestPerson(reviewerId, "bob@example.com", "Bob"), -5), Assert.Single(candidate.Reviewers));
        Assert.Equal("waitingForAuthor", Assert.Single(candidate.Document.Reviewers).ReviewStatus);
    }

    [Fact]
    public async Task ListAsync_TranslatesAServiceFailure()
    {
        var client = new ListClient { Failure = new VssServiceResponseException(HttpStatusCode.Forbidden, "secret", null) };

        PullRequestSourceException exception = await Assert.ThrowsAsync<PullRequestSourceException>(() =>
            new PullRequestSource(client, new Uri("https://dev.azure.com/example/"))
                .ListAsync(Project, new PullRequestSearch("active", null, null), 0, 100, CancellationToken.None));

        Assert.Equal("forbidden", exception.Code);
    }

    private sealed class ListClient : IPullRequestGitClient
    {
        public IReadOnlyList<GitPullRequest> Page { get; init; } = [];

        public Exception? Failure { get; init; }

        public GitPullRequestSearchCriteria? Criteria { get; private set; }

        public Guid ProjectId { get; private set; }

        public int Skip { get; private set; }

        public int Top { get; private set; }

        public Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(
            Guid projectId,
            GitPullRequestSearchCriteria criteria,
            int skip,
            int top,
            CancellationToken cancellationToken)
        {
            (ProjectId, Criteria, Skip, Top) = (projectId, criteria, skip, top);
            return Failure is null ? Task.FromResult(Page) : throw Failure;
        }

        public Task<GitPullRequest> GetByIdAsync(int pullRequestId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitPullRequest> GetPullRequestAsync(Guid projectId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceRef>> GetWorkItemsAsync(Guid projectId, Guid repositoryId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(Guid projectId, Guid repositoryId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(Guid projectId, Guid repositoryId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
