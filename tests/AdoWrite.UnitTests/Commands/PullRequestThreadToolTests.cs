using System.Net;
using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests.Commands;

/// <summary><c>pr resolve</c> and <c>pr unresolve</c>, driven through the CLI as an agent calls them.</summary>
public sealed class PullRequestThreadToolTests
{
    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;

    public PullRequestThreadToolTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), "fabricated-token");
        _session.FakePullRequests.KeepsCreated = true;
        AddThread(7, CommentThreadStatus.Active);
    }

    [Fact]
    public async Task Resolve_MarksTheThreadResolvedWithoutPostingAComment()
    {
        int exitCode = await Run("pr", "resolve", "42", "--thread", "7");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([(7, CommentThreadStatus.Fixed)], _session.FakePullRequests.StatusUpdates);
        Assert.Empty(_session.FakePullRequests.CreatedComments);
        JsonElement envelope = Envelope();
        Assert.Equal("pullRequestResolve", envelope.GetProperty("kind").GetString());
        JsonElement result = envelope.GetProperty("results")[0];
        Assert.Equal("changed", result.GetProperty("outcome").GetString());
        Assert.Equal("fixed", result.GetProperty("status").GetString());
        Assert.Equal("active", result.GetProperty("previousStatus").GetString());
        Assert.Equal(7, result.GetProperty("threadId").GetInt32());
        Assert.Contains("discussionId=7", result.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("comment").ValueKind);
    }

    [Theory]
    [InlineData(CommentThreadStatus.Fixed)]
    [InlineData(CommentThreadStatus.WontFix)]
    [InlineData(CommentThreadStatus.Closed)]
    [InlineData(CommentThreadStatus.ByDesign)]
    public async Task Resolve_LeavesAThreadThatIsAlreadyClosedAlone(CommentThreadStatus status)
    {
        AddThread(8, status);

        int exitCode = await Run("pr", "resolve", "42", "--thread", "8");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_session.FakePullRequests.StatusUpdates);
        Assert.Equal("unchanged", Result().GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Resolve_WithABodyRepliesInTheThreadThenResolvesIt()
    {
        int exitCode = await Run("pr", "resolve", "42", "--thread", "7", "--body", "Fixed in the last push.");

        Assert.Equal(ExitCodes.Success, exitCode);
        (int threadId, Comment comment) = Assert.Single(_session.FakePullRequests.CreatedComments);
        Assert.Equal(7, threadId);
        Assert.StartsWith("Fixed in the last push.", comment.Content, StringComparison.Ordinal);
        Assert.Equal([(7, CommentThreadStatus.Fixed)], _session.FakePullRequests.StatusUpdates);
        Assert.Equal("created", Result().GetProperty("comment").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Resolve_RunTwiceWithABodyPostsTheCommentOnce()
    {
        await Run("pr", "resolve", "42", "--thread", "7", "--body", "Fixed in the last push.");
        _output.GetStringBuilder().Clear();

        int exitCode = await Run("pr", "resolve", "42", "--thread", "7", "--body", "Fixed in the last push.");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Single(_session.FakePullRequests.CreatedComments);
        Assert.Single(_session.FakePullRequests.StatusUpdates);
        Assert.Equal("duplicate", Result().GetProperty("comment").GetProperty("outcome").GetString());
        Assert.Equal("unchanged", Result().GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Resolve_IsPartialWhenTheCommentWasPostedButTheStatusCouldNotBeSet()
    {
        _session.FakePullRequests.UpdateThreadFailure = new VssServiceResponseException(HttpStatusCode.Forbidden, "Forbidden.", null);

        int exitCode = await Run("pr", "resolve", "42", "--thread", "7", "--body", "Fixed.");

        Assert.Equal(ExitCodes.Partial, exitCode);
        JsonElement envelope = Envelope();
        Assert.Equal("partial", envelope.GetProperty("status").GetString());
        Assert.Equal("failed", Result().GetProperty("outcome").GetString());
        Assert.Equal("created", Result().GetProperty("comment").GetProperty("outcome").GetString());
        Assert.Equal("forbidden", envelope.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unresolve_MakesAResolvedThreadActiveAgain()
    {
        AddThread(8, CommentThreadStatus.Fixed);

        int exitCode = await Run("pr", "unresolve", "42", "--thread", "8");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([(8, CommentThreadStatus.Active)], _session.FakePullRequests.StatusUpdates);
        Assert.Equal("pullRequestUnresolve", Envelope().GetProperty("kind").GetString());
        Assert.Equal("active", Result().GetProperty("status").GetString());
        Assert.Equal("fixed", Result().GetProperty("previousStatus").GetString());
    }

    [Fact]
    public async Task Unresolve_LeavesAnActiveThreadAlone()
    {
        int exitCode = await Run("pr", "unresolve", "42", "--thread", "7");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_session.FakePullRequests.StatusUpdates);
        Assert.Equal("unchanged", Result().GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Resolve_DryRunChangesNothing()
    {
        int exitCode = await Run("pr", "resolve", "42", "--thread", "7", "--body", "Fixed.", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_session.FakePullRequests.StatusUpdates);
        Assert.Empty(_session.FakePullRequests.CreatedComments);
        Assert.Equal("preview", Result().GetProperty("outcome").GetString());
        Assert.Equal("fixed", Result().GetProperty("status").GetString());
        Assert.Equal("preview", Result().GetProperty("comment").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Resolve_NeedsAThread()
    {
        int exitCode = await Run("pr", "resolve", "42");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("threadRequired", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_ReportsAThreadThePullRequestDoesNotHave()
    {
        int exitCode = await Run("pr", "resolve", "42", "--thread", "99");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Equal("threadNotFound", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(_session.FakePullRequests.StatusUpdates);
    }

    [Fact]
    public async Task Resolve_RefusesCommentOptionsWithoutABody()
    {
        int exitCode = await Run("pr", "resolve", "42", "--thread", "7", "--mention", "someone@example.com");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("bodyRequired", Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(_session.FakePullRequests.StatusUpdates);
    }

    private void AddThread(int id, CommentThreadStatus status) =>
        _session.FakePullRequests.Threads.Add(new GitPullRequestCommentThread
        {
            Id = id,
            Status = status,
            Comments = [Fabricated.CommentBy(1, "Please rename this.", Fabricated.Reviewer)]
        });

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
