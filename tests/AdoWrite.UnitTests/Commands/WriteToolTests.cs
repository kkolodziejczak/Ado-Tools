using System.CommandLine;
using System.Net;
using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests.Commands;

public sealed class WriteToolTests
{
    private const string ReadToken = "fabricated-read-token";
    private const string WriteToken = "fabricated-write-token";
    private const string Agent = "Claude Opus 5.5";

    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;

    public WriteToolTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Read), ReadToken);
        _vault.Write(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Write), WriteToken);
    }

    public static TheoryData<string, string[]> SliceCommands => new()
    {
        { "pr", ["auth", "comment", "config", "reply"] },
        { "work-item", ["auth", "comment", "config"] },
    };

    [Fact]
    public void Umbrella_SharesOnlyConfigAndAuth()
    {
        RootCommand root = Umbrella().RootCommand;

        Assert.Equal(["auth", "config", "pr", "work-item"], root.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SliceCommands))]
    public void Slice_MountsItsCommandsOnTheExecutable(string feature, string[] expected)
    {
        CommandModule module = feature == "pr" ? PullRequestCommentModule.Create(_factory) : WorkItemCommentModule.Create(_factory);

        RootCommand root = Composition().CreateSlice($"ado-write-{feature}", module).RootCommand;

        Assert.Equal(expected, root.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PullRequestComment_PostsALineCommentWithTheWriteToken()
    {
        int exitCode = await Umbrella().InvokeAsync(
            ["pr", "comment", "https://dev.azure.com/fabricated/Fabricated%20App/_git/Fabricated.Repo/pullrequest/42", "--file", @"src\App.cs", "--line", "2", "--body", "Why twice?", "--agent", Agent, "--mention", "reviewer@example.com", "--mention", "00000000-0000-0000-0000-00000000c003", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(WriteToken, _factory.Pat);
        Assert.True(_session.Disposed);
        JsonElement envelope = Envelope();
        Assert.Equal("pullRequestComment", envelope.GetProperty("kind").GetString());
        Assert.Equal("success", envelope.GetProperty("status").GetString());
        Assert.Equal(42, envelope.GetProperty("query").GetProperty("id").GetInt32());
        JsonElement result = envelope.GetProperty("results")[0];
        Assert.Equal("created", result.GetProperty("outcome").GetString());
        Assert.Equal("/src/App.cs", result.GetProperty("anchor").GetProperty("filePath").GetString());
        Assert.Equal("right", result.GetProperty("anchor").GetProperty("side").GetString());
        Assert.True(result.GetProperty("markerKept").GetBoolean());
        Assert.False(result.TryGetProperty("text", out _));
        Assert.Equal(
            [Fabricated.Reviewer.Id, Guid.Parse("00000000-0000-0000-0000-00000000c003")],
            result.GetProperty("mentions").EnumerateArray().Select(mention => mention.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task PullRequestReply_RepliesInTheNamedThread()
    {
        _session.FakePullRequests.Threads.Add(new Microsoft.TeamFoundation.SourceControl.WebApi.GitPullRequestCommentThread
        {
            Id = 70,
            Comments = [new Microsoft.TeamFoundation.SourceControl.WebApi.Comment { Id = 1, Content = "Question" }]
        });

        int exitCode = await Umbrella().InvokeAsync(
            ["pr", "reply", "42", "--thread", "70", "--body", "Thanks!", "--agent", Agent, "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        JsonElement envelope = Envelope();
        Assert.Equal("pullRequestReply", envelope.GetProperty("kind").GetString());
        Assert.Equal(70, envelope.GetProperty("query").GetProperty("threadId").GetInt32());
        Assert.Equal(70, Assert.Single(_session.FakePullRequests.CreatedComments).ThreadId);
    }

    [Fact]
    public async Task WorkItemComment_ReadsTheBodyFromAFileAndPreviewsADryRun()
    {
        string bodyFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(bodyFile, "Which markets?\r\nAnd currencies?\r\n");

            int exitCode = await Umbrella().InvokeAsync(
                ["work-item", "comment", "7", "--body-file", bodyFile, "--agent", Agent, "--dry-run", "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Empty(_session.FakeWorkItems.Added);
            JsonElement result = Envelope().GetProperty("results")[0];
            Assert.Equal("preview", result.GetProperty("outcome").GetString());
            Assert.StartsWith("Which markets?\nAnd currencies?\n\n---\n", result.GetProperty("text").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    [Theory]
    [InlineData("bodyRequired", "pr", "comment", "42", "--agent", Agent)]
    [InlineData("bodyRequired", "pr", "comment", "42", "--body", "Q", "--body-file", "q.md", "--agent", Agent)]
    [InlineData("invalidAgent", "pr", "comment", "42", "--body", "Q")]
    [InlineData("invalidPullRequestId", "pr", "comment", "0", "--body", "Q", "--agent", Agent)]
    [InlineData("fileRequired", "pr", "comment", "42", "--line", "3", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidLine", "pr", "comment", "42", "--file", "/src/App.cs", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidLine", "pr", "comment", "42", "--file", "/src/App.cs", "--line", "3", "--end-line", "2", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidSide", "pr", "comment", "42", "--file", "/src/App.cs", "--line", "3", "--side", "middle", "--body", "Q", "--agent", Agent)]
    [InlineData("threadRequired", "pr", "reply", "42", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidWorkItemId", "work-item", "comment", "https://dev.azure.com/fabricated/App/_git/Repo/pullrequest/4", "--body", "Q", "--agent", Agent)]
    [InlineData("organizationMismatch", "work-item", "comment", "https://dev.azure.com/other/App/_workitems/edit/7", "--body", "Q", "--agent", Agent)]
    public async Task InvalidRequests_AreRejectedWithoutOpeningASession(string expectedCode, params string[] args)
    {
        int exitCode = await Umbrella().InvokeAsync([.. args, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(expectedCode, Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(_factory.Pat);
    }

    [Fact]
    public async Task MissingWriteToken_NamesTheWriteLoginEvenWhenAReadTokenIsStored()
    {
        _vault.Remove(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Write));

        int exitCode = await Umbrella().InvokeAsync(["work-item", "comment", "7", "--body", "Q", "--agent", Agent, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = Envelope().GetProperty("errors")[0];
        Assert.Equal("missingPat", error.GetProperty("code").GetString());
        Assert.Contains("ado-write auth login", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Null(_factory.Pat);
    }

    [Fact]
    public async Task ForbiddenPost_NamesTheScopesTheWriteTokenNeeds()
    {
        _session.FakePullRequests.PullRequestFailure = new VssServiceResponseException(HttpStatusCode.Forbidden, "Denied.", null);

        int exitCode = await Umbrella().InvokeAsync(["pr", "comment", "42", "--body", "Q", "--agent", Agent, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = Envelope().GetProperty("errors")[0];
        Assert.Equal(("azureDevOps", "forbidden"), (error.GetProperty("scope").GetString(), error.GetProperty("code").GetString()));
        Assert.Contains("Code (Read & write)", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTarget_ReturnsNotFound()
    {
        _session.FakePullRequests.PullRequestFailure = Fabricated.NotFound();

        int exitCode = await Umbrella().InvokeAsync(["pr", "comment", "42", "--body", "Q", "--agent", Agent, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.NotFound, exitCode);
        JsonElement envelope = Envelope();
        Assert.Equal("notFound", envelope.GetProperty("status").GetString());
        Assert.Equal("pullRequestNotFound", envelope.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task PullRequestComment_NormalizesTheFilePath()
    {
        int exitCode = await Umbrella().InvokeAsync(
            ["pr", "comment", "42", "--file", @".\src//./App.cs", "--line", "1", "--body", "Q", "--agent", Agent, "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("/src/App.cs", Assert.Single(_session.FakePullRequests.CreatedThreads).ThreadContext.FilePath);
    }

    [Fact]
    public async Task TimedOutPost_TellsTheAgentThatARetryIsSafe()
    {
        _session.FakePullRequests.PullRequestFailure = new TaskCanceledException("Timed out.");

        int exitCode = await Umbrella().InvokeAsync(["pr", "comment", "42", "--body", "Q", "--agent", Agent, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = Envelope().GetProperty("errors")[0];
        Assert.Equal("timeout", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("transient").GetBoolean());
        Assert.Contains("run the same command again", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthLogin_StoresTheWriteTokenSeparatelyAndAsksNoPrivacyLevel()
    {
        var terminal = new ScriptedTerminalInput("new-write-token");

        int exitCode = await Umbrella(terminal).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("Personal access token for https://dev.azure.com/fabricated#write: ", Assert.Single(terminal.Prompts));
        Assert.Equal("new-write-token", _vault.Read(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Write)));
        Assert.Equal(ReadToken, _vault.Read(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Read)));
        JsonElement result = Envelope().GetProperty("results")[0];
        Assert.True(result.GetProperty("tokenStored").GetBoolean());
        Assert.False(result.TryGetProperty("privacyConfigured", out _));
    }

    [Fact]
    public async Task AuthLogout_RemovesOnlyTheWriteToken()
    {
        int exitCode = await Umbrella().InvokeAsync(["auth", "logout", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(_vault.Read(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Write)));
        Assert.Equal(ReadToken, _vault.Read(CredentialAccount.ForToken(Fabricated.Organization, TokenScope.Read)));
    }

    private CommandApplication Umbrella(ScriptedTerminalInput? terminal = null) =>
        Composition(terminal).CreateUmbrella(
            "ado-write",
            [PullRequestCommentModule.Create(_factory), WorkItemCommentModule.Create(_factory)]);

    private ToolComposition Composition(ScriptedTerminalInput? terminal = null) =>
        new(
            new FixedConfigurationStore(),
            new JsonOutput(_output),
            TimeProvider.System,
            credentialVault: _vault,
            terminalInput: terminal,
            tokenScope: TokenScope.Write);

    private JsonElement Envelope() => JsonDocument.Parse(_output.ToString()).RootElement;
}
