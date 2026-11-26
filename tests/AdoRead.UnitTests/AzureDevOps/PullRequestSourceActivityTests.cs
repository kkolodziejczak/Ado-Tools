using AdoRead.AzureDevOps;
using AdoRead.PullRequests;
using Microsoft.TeamFoundation.Policy.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json.Linq;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class PullRequestSourceActivityTests
{
    private static readonly AdoProject Project = new(Guid.NewGuid(), "Payments", null);

    [Fact]
    public async Task GetActivityAsync_CountsPeoplesCommentsPerThreadAndLeavesSystemThreadsOut()
    {
        var updated = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);
        var client = new ActivityClient
        {
            Threads =
            [
                Thread(3, CommentThreadStatus.Fixed, updated, Text(1), Text(2), Text(3, deleted: true)),
                Thread(4, CommentThreadStatus.Active, updated, System(1)),
                Deleted(Thread(5, CommentThreadStatus.Active, updated, Text(1))),
                Thread(6, CommentThreadStatus.Active, updated, Text(1))
            ]
        };

        PullRequestActivityDocument activity = await Source(client).GetActivityAsync(TestPullRequests.Create(Project, 42), CancellationToken.None);

        Assert.Equal(
            [(3, "fixed", 2), (6, "active", 1)],
            activity.Threads.Select(thread => (thread.Id, thread.Status, thread.CommentCount)));
        Assert.Equal(new DateTimeOffset(updated), activity.Threads[0].LastUpdatedAtUtc);
        Assert.Equal((Project.Id, 42), client.EvaluationsAskedFor);
    }

    [Fact]
    public async Task GetActivityAsync_DescribesEveryPolicyEvaluation()
    {
        Guid evaluationId = Guid.NewGuid();
        var client = new ActivityClient
        {
            Evaluations =
            [
                new PolicyEvaluationRecord
                {
                    EvaluationId = evaluationId,
                    Status = PolicyEvaluationStatus.Rejected,
                    Context = JObject.Parse("""{"isExpired":true,"buildId":501}"""),
                    Configuration = new PolicyConfiguration
                    {
                        IsBlocking = true,
                        Type = new PolicyTypeRef { Id = PolicyEvaluations.BuildPolicyType, DisplayName = "Build" },
                        Settings = JObject.Parse("""{"displayName":"CI"}""")
                    }
                }
            ]
        };

        PullRequestActivityDocument activity = await Source(client).GetActivityAsync(TestPullRequests.Create(Project, 42), CancellationToken.None);

        Assert.Equal(
            new PullRequestCheckDocument(evaluationId, "CI", "Build", "rejected", IsExpired: true, IsBlocking: true, BuildId: 501),
            Assert.Single(activity.Checks));
    }

    private static PullRequestSource Source(ActivityClient client) => new(client, new Uri("https://dev.azure.com/example/"));

    private static GitPullRequestCommentThread Thread(int id, CommentThreadStatus status, DateTime updated, params Comment[] comments) =>
        new() { Id = id, Status = status, LastUpdatedDate = updated, Comments = comments };

    private static GitPullRequestCommentThread Deleted(GitPullRequestCommentThread thread)
    {
        thread.IsDeleted = true;
        return thread;
    }

    private static Comment Text(int id, bool deleted = false) =>
        new() { Id = (short)id, CommentType = CommentType.Text, IsDeleted = deleted, Content = "x" };

    private static Comment System(int id) =>
        new() { Id = (short)id, CommentType = CommentType.System, Content = "Policy updated" };

    private sealed class ActivityClient : IPullRequestGitClient
    {
        public IReadOnlyList<GitPullRequestCommentThread> Threads { get; init; } = [];

        public IReadOnlyList<PolicyEvaluationRecord> Evaluations { get; init; } = [];

        public (Guid ProjectId, int PullRequestId)? EvaluationsAskedFor { get; private set; }

        public Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(Guid projectId, Guid repositoryId, int pullRequestId, CancellationToken cancellationToken) =>
            Task.FromResult(Threads);

        public Task<IReadOnlyList<PolicyEvaluationRecord>> GetPolicyEvaluationsAsync(Guid projectId, int pullRequestId, CancellationToken cancellationToken)
        {
            EvaluationsAskedFor = (projectId, pullRequestId);
            return Task.FromResult(Evaluations);
        }

        public Task<GitPullRequest> GetByIdAsync(int pullRequestId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitPullRequest> GetPullRequestAsync(Guid projectId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceRef>> GetWorkItemsAsync(Guid projectId, Guid repositoryId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(Guid projectId, Guid repositoryId, int pullRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
