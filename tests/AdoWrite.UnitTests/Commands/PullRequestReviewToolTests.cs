using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.AzureDevOps;
using AdoWrite.Commands;
using Microsoft.TeamFoundation.Policy.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.UnitTests.Commands;

/// <summary><c>pr requeue</c> and <c>pr vote</c>, driven through the CLI as an agent calls them.</summary>
public sealed class PullRequestReviewToolTests
{
    private static readonly Guid Expired = Guid.Parse("00000000-0000-0000-0000-0000000e0001");
    private static readonly Guid Passed = Guid.Parse("00000000-0000-0000-0000-0000000e0002");
    private static readonly Guid Failed = Guid.Parse("00000000-0000-0000-0000-0000000e0003");
    private static readonly Guid Running = Guid.Parse("00000000-0000-0000-0000-0000000e0004");
    private static readonly Guid Reviewers = Guid.Parse("00000000-0000-0000-0000-0000000e0005");

    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;

    public PullRequestReviewToolTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Write), "fabricated-write-token");
        _session.FakeReviews.Evaluations.AddRange(
        [
            FakePullRequestReviewClient.Build(Expired, PolicyEvaluationStatus.Approved, """{"isExpired":true,"buildId":501}""", "CI"),
            FakePullRequestReviewClient.Build(Passed, PolicyEvaluationStatus.Approved, """{"isExpired":false,"buildId":502}""", "Lint"),
            FakePullRequestReviewClient.Build(Failed, PolicyEvaluationStatus.Rejected, """{"isExpired":false,"buildId":503}""", displayName: null),
            FakePullRequestReviewClient.Build(Running, PolicyEvaluationStatus.Running, """{"buildId":504}"""),
            FakePullRequestReviewClient.Build(Reviewers, PolicyEvaluationStatus.Rejected, policyType: Guid.NewGuid())
        ]);
    }

    [Fact]
    public async Task Requeue_RequeuesOnlyExpiredBuildsAndReportsEveryBuildPolicy()
    {
        int exitCode = await Run("pr", "requeue", "42");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([(Fabricated.ProjectId, Expired)], _session.FakeReviews.Requeued);
        JsonElement envelope = Envelope();
        Assert.Equal("pullRequestRequeue", envelope.GetProperty("kind").GetString());
        Assert.Equal(
            [
                (Expired, "CI", "requeued", "queued", (int?)null),
                (Passed, "Lint", "notExpired", "approved", 502),
                (Failed, "Build", "notExpired", "rejected", 503),
                (Running, "PR build", "inProgress", "running", 504)
            ],
            envelope.GetProperty("results").EnumerateArray().Select(build => (
                build.GetProperty("evaluationId").GetGuid(),
                build.GetProperty("name").GetString(),
                build.GetProperty("outcome").GetString(),
                build.GetProperty("status").GetString(),
                build.GetProperty("buildId").ValueKind == JsonValueKind.Null ? null : (int?)build.GetProperty("buildId").GetInt32())));
    }

    [Fact]
    public async Task Requeue_RunTwice_RequeuesNothingTheSecondTime()
    {
        await Run("pr", "requeue", "42");
        _output.GetStringBuilder().Clear();

        int exitCode = await Run("pr", "requeue", "42");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Single(_session.FakeReviews.Requeued);
        Assert.Equal("inProgress", Result(Expired).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Requeue_NamedEvaluation_RequeuesAFailedBuildAndLeavesTheRest()
    {
        int exitCode = await Run("pr", "requeue", "42", "--evaluation", Failed.ToString(), "--evaluation", Running.ToString());

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([(Fabricated.ProjectId, Failed)], _session.FakeReviews.Requeued);
        Assert.Equal("requeued", Result(Failed).GetProperty("outcome").GetString());
        Assert.Equal("inProgress", Result(Running).GetProperty("outcome").GetString());
        Assert.Equal("notSelected", Result(Expired).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Requeue_DryRun_PreviewsWithoutRequeueing()
    {
        int exitCode = await Run("pr", "requeue", "42", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_session.FakeReviews.Requeued);
        Assert.Equal("preview", Result(Expired).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Requeue_UnknownEvaluation_RequeuesNothing()
    {
        int exitCode = await Run("pr", "requeue", "42", "--evaluation", Expired.ToString(), "--evaluation", Reviewers.ToString());

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Empty(_session.FakeReviews.Requeued);
        Assert.Equal("evaluationNotFound", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Vote_ApprovesAsTheTokenOwner()
    {
        int exitCode = await Run("pr", "vote", "42", "--vote", "approve", "--confirm");

        Assert.Equal(ExitCodes.Success, exitCode);
        (PullRequestLocation location, Guid reviewerId, short vote) = Assert.Single(_session.FakeReviews.Votes);
        Assert.Equal((Fabricated.RepositoryId, Fabricated.Author.Id, (short)10), (location.RepositoryId, reviewerId, vote));
        JsonElement envelope = Envelope();
        Assert.Equal("pullRequestVote", envelope.GetProperty("kind").GetString());
        JsonElement result = envelope.GetProperty("results")[0];
        Assert.Equal(("voted", "approve", "none"), (result.GetProperty("outcome").GetString(), result.GetProperty("vote").GetString(), result.GetProperty("previousVote").GetString()));
        Assert.Equal(
            "https://dev.azure.com/fabricated/Fabricated%20App/_git/Fabricated.Repo/pullrequest/42",
            result.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Vote_SameVoteAgain_SendsNothing()
    {
        _session.FakePullRequests.Reviewers.Add(new IdentityRefWithVote { Id = Fabricated.Author.Id.ToString(), Vote = -5 });

        int exitCode = await Run("pr", "vote", "42", "--vote", "wait-for-author", "--confirm");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_session.FakeReviews.Votes);
        Assert.Equal("unchanged", Envelope().GetProperty("results")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Vote_DryRun_NeedsNoConfirmationAndSendsNothing()
    {
        int exitCode = await Run("pr", "vote", "42", "--vote", "reject", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_session.FakeReviews.Votes);
        JsonElement result = Envelope().GetProperty("results")[0];
        Assert.Equal(("preview", "reject"), (result.GetProperty("outcome").GetString(), result.GetProperty("vote").GetString()));
    }

    [Fact]
    public async Task Vote_OnACompletedPullRequest_IsRejected()
    {
        _session.FakePullRequests.Status = PullRequestStatus.Completed;

        int exitCode = await Run("pr", "vote", "42", "--vote", "approve", "--confirm");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Empty(_session.FakeReviews.Votes);
        Assert.Equal("pullRequestNotActive", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("confirmationRequired", "pr", "vote", "42", "--vote", "approve")]
    [InlineData("invalidVote", "pr", "vote", "42", "--confirm")]
    [InlineData("invalidVote", "pr", "vote", "42", "--vote", "maybe", "--confirm")]
    [InlineData("invalidPullRequestId", "pr", "vote", "0", "--vote", "approve", "--confirm")]
    [InlineData("invalidPullRequestId", "pr", "requeue", "nope")]
    [InlineData("invalidEvaluationId", "pr", "requeue", "42", "--evaluation", "not-a-guid")]
    [InlineData("organizationMismatch", "pr", "requeue", "https://dev.azure.com/second/App/_git/Repo/pullrequest/42")]
    public async Task InvalidRequests_AreRejectedWithoutOpeningASession(string expectedCode, params string[] args)
    {
        int exitCode = await Run(args);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(expectedCode, Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(_factory.Pat);
    }

    private Task<int> Run(params string[] args) =>
        new ToolComposition(
                new FixedConfigurationStore(),
                new JsonOutput(_output),
                TimeProvider.System,
                credentialVault: _vault,
                tokenScope: TokenScope.Write)
            .CreateUmbrella("ado-write", [PullRequestWriteModule.Create(_factory), WorkItemCommentModule.Create(_factory)])
            .InvokeAsync([.. args, "--compact"], CancellationToken.None);

    private JsonElement Envelope() => JsonDocument.Parse(_output.ToString()).RootElement;

    private JsonElement Result(Guid evaluationId) =>
        Envelope().GetProperty("results").EnumerateArray().Single(build => build.GetProperty("evaluationId").GetGuid() == evaluationId);
}
