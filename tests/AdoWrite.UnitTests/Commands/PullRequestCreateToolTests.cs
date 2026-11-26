using System.Net;
using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests.Commands;

/// <summary><c>pr create</c>, driven through the CLI as an agent calls it.</summary>
public sealed class PullRequestCreateToolTests
{
    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;

    public PullRequestCreateToolTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), "fabricated-token");
    }

    private FakePullRequestCreateClient Creation => _session.FakeCreation;

    [Fact]
    public async Task Create_OpensAPullRequestFromTheSourceIntoTheDefaultBranch()
    {
        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "feature/retry", "--title", "Retry failed payments");

        Assert.Equal(ExitCodes.Success, exitCode);
        GitPullRequest created = Assert.Single(Creation.Created);
        Assert.Equal("refs/heads/feature/retry", created.SourceRefName);
        Assert.Equal("refs/heads/main", created.TargetRefName);
        Assert.Equal("Retry failed payments", created.Title);
        Assert.False(created.IsDraft);
        JsonElement envelope = Envelope();
        Assert.Equal("pullRequestCreate", envelope.GetProperty("kind").GetString());
        JsonElement result = Result();
        Assert.Equal("created", result.GetProperty("outcome").GetString());
        Assert.Equal(FakePullRequestCreateClient.CreatedId, result.GetProperty("pullRequestId").GetInt32());
        Assert.EndsWith($"/pullrequest/{FakePullRequestCreateClient.CreatedId}", result.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(Fabricated.Author.DisplayName, result.GetProperty("actingAs").GetString());
    }

    [Fact]
    public async Task Create_ReturnsTheActivePullRequestForTheSameBranchesInsteadOfASecondOne()
    {
        Creation.Active.Add(new GitPullRequest
        {
            PullRequestId = 77,
            Title = "Earlier",
            SourceRefName = "refs/heads/feature/retry",
            TargetRefName = "refs/heads/release"
        });

        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "refs/heads/feature/retry", "--target", "release", "--title", "Again");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(Creation.Created);
        Assert.Equal("existing", Result().GetProperty("outcome").GetString());
        Assert.Equal(77, Result().GetProperty("pullRequestId").GetInt32());
        Assert.Equal("Earlier", Result().GetProperty("title").GetString());
        Assert.Contains("already", Result().GetProperty("notice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_SendsTheBodyFooterReviewersWorkItemsAndDraft()
    {
        _session.FakePeople.Matches["reviewer@example.com"] = [Fabricated.Reviewer];

        int exitCode = await Run(
            "pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository,
            "--source", "feature/retry", "--title", "Retry", "--body", "Retries a failed payment once.",
            "--agent", "Claude Opus 5.5", "--reviewer", "reviewer@example.com", "--work-item", "12345", "--work-item", "12346", "--draft");

        Assert.Equal(ExitCodes.Success, exitCode);
        GitPullRequest created = Assert.Single(Creation.Created);
        Assert.StartsWith("Retries a failed payment once.", created.Description, StringComparison.Ordinal);
        Assert.Contains($"Created by Claude Opus 5.5 on behalf of {Fabricated.Author.DisplayName}", created.Description, StringComparison.Ordinal);
        Assert.Equal([Fabricated.Reviewer.Id.ToString()], created.Reviewers.Select(reviewer => reviewer.Id));
        Assert.Equal(["12345", "12346"], created.WorkItemRefs.Select(item => item.Id));
        Assert.True(created.IsDraft);
        Assert.Equal(Fabricated.Reviewer.Id, Result().GetProperty("reviewers")[0].GetProperty("id").GetGuid());
        Assert.Equal([12345, 12346], Result().GetProperty("workItems").EnumerateArray().Select(item => item.GetInt32()));
    }

    [Fact]
    public async Task Create_DryRunShowsTheDescriptionAndCreatesNothing()
    {
        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "feature/retry", "--title", "Retry", "--body", "Text.", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(Creation.Created);
        Assert.Equal("preview", Result().GetProperty("outcome").GetString());
        Assert.Equal("Text.", Result().GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, Result().GetProperty("pullRequestId").ValueKind);
    }

    [Fact]
    public async Task Create_FindsARepositoryByIdWithoutAProject()
    {
        int exitCode = await Run("pr", "create", "--repository", Fabricated.RepositoryId.ToString(), "--source", "feature/retry", "--title", "Retry");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal((null, Fabricated.RepositoryId.ToString()), Creation.RepositoryLookups.Single());
    }

    [Fact]
    public async Task Create_ReturnsThePullRequestAzureDevOpsSaysAlreadyExists()
    {
        Creation.CreateFailure = new VssServiceResponseException(HttpStatusCode.Conflict, "TF401179: An active pull request already exists.", null);
        Creation.AppearsOnConflict = new GitPullRequest
        {
            PullRequestId = 78,
            Title = "Opened meanwhile",
            SourceRefName = "refs/heads/feature/retry",
            TargetRefName = "refs/heads/main"
        };

        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "feature/retry", "--title", "Retry");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("existing", Result().GetProperty("outcome").GetString());
        Assert.Equal(78, Result().GetProperty("pullRequestId").GetInt32());
    }

    [Theory]
    [InlineData("sourceRequired", "--project", "Payments", "--repository", "Api", "--title", "T")]
    [InlineData("titleRequired", "--project", "Payments", "--repository", "Api", "--source", "feature/x")]
    [InlineData("repositoryRequired", "--project", "Payments", "--source", "feature/x", "--title", "T")]
    [InlineData("projectRequired", "--repository", "Api", "--source", "feature/x", "--title", "T")]
    [InlineData("invalidWorkItemId", "--project", "Payments", "--repository", "Api", "--source", "feature/x", "--title", "T", "--work-item", "x")]
    public async Task Create_RejectsIncompleteInputBeforeCallingAzureDevOps(string code, params string[] options)
    {
        int exitCode = await Run(["pr", "create", .. options]);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(code, Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(Creation.RepositoryLookups);
    }

    [Fact]
    public async Task Create_RejectsTheSameSourceAndTarget()
    {
        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "main", "--title", "T");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("sameBranch", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(Creation.Created);
    }

    [Fact]
    public async Task Create_RejectsADescriptionAzureDevOpsWouldCut()
    {
        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "feature/x", "--title", "T", "--body", new string('x', 4001));

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("descriptionTooLong", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(Creation.Created);
    }

    [Fact]
    public async Task Create_ReportsAnUnknownRepository()
    {
        Creation.RepositoryFailure = Fabricated.NotFound();

        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", "Nope", "--source", "feature/x", "--title", "T");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Equal("repositoryNotFound", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_SaysWhyAzureDevOpsRefusedThePullRequest()
    {
        Creation.CreateFailure = new VssServiceResponseException(HttpStatusCode.BadRequest, "TF401398: The source branch no longer exists.", null);

        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "feature/gone", "--title", "T");

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = Envelope().GetProperty("errors")[0];
        Assert.Equal("pullRequestRefused", error.GetProperty("code").GetString());
        Assert.Contains("branch", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TF401398", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ReportsAReviewerNobodyMatchesAndCreatesNothing()
    {
        int exitCode = await Run("pr", "create", "--project", Fabricated.Project, "--repository", Fabricated.Repository, "--source", "feature/x", "--title", "T", "--reviewer", "ghost@example.com");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("reviewerNotFound", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(Creation.Created);
    }

    private Task<int> Run(params string[] args) =>
        new ToolComposition(
                new FixedConfigurationStore(),
                new JsonOutput(_output),
                TimeProvider.System,
                credentialVault: _vault,
                toolKind: ToolKind.Write)
            .CreateUmbrella("ado-write", [PullRequestWriteModule.Create(_factory), WorkItemCommentModule.Create(_factory)])
            .InvokeAsync([.. args, "--compact"], CancellationToken.None);

    private JsonElement Envelope() => JsonDocument.Parse(_output.ToString()).RootElement;

    private JsonElement Result() => Envelope().GetProperty("results")[0];
}
