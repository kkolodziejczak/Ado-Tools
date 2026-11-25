using System.CommandLine;
using System.Net;
using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests.Commands;

public sealed class WriteToolTests
{
    private const string Token = "fabricated-token";
    private const string Agent = "Claude Opus 5.5";

    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;

    public WriteToolTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), Token);
    }

    public static TheoryData<string, string[]> SliceCommands => new()
    {
        { "pr", ["auth", "comment", "config", "reply", "requeue", "vote"] },
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
        CommandModule module = feature == "pr" ? PullRequestWriteModule.Create(_factory) : WorkItemCommentModule.Create(_factory);

        RootCommand root = Composition().CreateSlice($"ado-write-{feature}", module).RootCommand;

        Assert.Equal(expected, root.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PullRequestComment_PostsALineCommentWithTheTokenTheReadToolsUse()
    {
        int exitCode = await Umbrella().InvokeAsync(
            ["pr", "comment", "https://dev.azure.com/fabricated/Fabricated%20App/_git/Fabricated.Repo/pullrequest/42", "--file", @"src\App.cs", "--line", "2", "--body", "Why twice?", "--agent", Agent, "--mention", "reviewer@example.com", "--mention", "00000000-0000-0000-0000-00000000c003", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(Token, _factory.Pat);
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
                ["work-item", "comment", "7", "--body-file", bodyFile, "--dry-run", "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Empty(_session.FakeWorkItems.Added);
            JsonElement result = Envelope().GetProperty("results")[0];
            Assert.Equal("preview", result.GetProperty("outcome").GetString());
            Assert.StartsWith("Which markets?\nAnd currencies?\n\n<!-- ado-write:", result.GetProperty("text").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    [Theory]
    [InlineData("bodyRequired", "pr", "comment", "42", "--agent", Agent)]
    [InlineData("bodyRequired", "pr", "comment", "42", "--body", "Q", "--body-file", "q.md", "--agent", Agent)]
    [InlineData("invalidAgent", "pr", "comment", "42", "--body", "Q", "--agent", " ")]
    [InlineData("invalidPullRequestId", "pr", "comment", "0", "--body", "Q", "--agent", Agent)]
    [InlineData("fileRequired", "pr", "comment", "42", "--line", "3", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidLine", "pr", "comment", "42", "--file", "/src/App.cs", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidLine", "pr", "comment", "42", "--file", "/src/App.cs", "--line", "3", "--end-line", "2", "--body", "Q", "--agent", Agent)]
    [InlineData("invalidSide", "pr", "comment", "42", "--file", "/src/App.cs", "--line", "3", "--side", "middle", "--body", "Q", "--agent", Agent)]
    [InlineData("threadRequired", "pr", "reply", "42", "--body", "Q", "--agent", Agent)]
    [InlineData("attachmentUnreadable", "work-item", "comment", "7", "--body", "Q", "--agent", Agent, "--attach", "no-such-file.png")]
    [InlineData("attachmentUnreadable", "work-item", "comment", "7", "--body", "Q", "--agent", Agent, "--attach", "")]
    [InlineData("invalidAttachments", "work-item", "comment", "7", "--body", "Q", "--agent", Agent, "--attach", "1", "--attach", "2", "--attach", "3", "--attach", "4", "--attach", "5", "--attach", "6", "--attach", "7", "--attach", "8", "--attach", "9", "--attach", "10", "--attach", "11")]
    [InlineData("invalidWorkItemId", "work-item", "comment", "https://dev.azure.com/fabricated/App/_git/Repo/pullrequest/4", "--body", "Q", "--agent", Agent)]
    [InlineData("organizationMismatch", "work-item", "comment", "https://dev.azure.com/second/App/_workitems/edit/7", "--body", "Q", "--agent", Agent)]
    public async Task InvalidRequests_AreRejectedWithoutOpeningASession(string expectedCode, params string[] args)
    {
        int exitCode = await Umbrella().InvokeAsync([.. args, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(expectedCode, Envelope().GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(_factory.Pat);
    }

    [Fact]
    public async Task MissingToken_NamesTheLoginOfThisTool()
    {
        _vault.Remove(CredentialAccount.For(Fabricated.Organization));

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
        string message = error.GetProperty("message").GetString()!;
        Assert.Contains("Code (Read & write)", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Work Items", message, StringComparison.Ordinal);
        Assert.Contains("ado-write auth login", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthorizedPost_NamesTheScopesTheWriteTokenNeeds()
    {
        _session.FakePullRequests.PullRequestFailure = new VssUnauthorizedException("VS30063: Denied.");

        int exitCode = await Umbrella().InvokeAsync(["pr", "comment", "42", "--body", "Q", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = Envelope().GetProperty("errors")[0];
        Assert.Equal(("azureDevOps", "unauthorized"), (error.GetProperty("scope").GetString(), error.GetProperty("code").GetString()));
        string message = error.GetProperty("message").GetString()!;
        Assert.Contains("Code (Read & write)", message, StringComparison.Ordinal);
        Assert.Contains("ado-write auth login", message, StringComparison.Ordinal);
        Assert.DoesNotContain("VS30063", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenRejectedOnConnectingNamesTheLoginCommand()
    {
        _factory.CreateFailure = new VssUnauthorizedException("VS30063: Denied.");

        int exitCode = await Umbrella().InvokeAsync(["pr", "comment", "42", "--body", "Q", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = Envelope().GetProperty("errors")[0];
        Assert.Equal(("authentication", "unauthorized"), (error.GetProperty("scope").GetString(), error.GetProperty("code").GetString()));
        Assert.Equal(
            "Azure DevOps rejected the token: it may be mistyped, expired or revoked, or made for another organization. Store a new one with `ado-write auth login`.",
            error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ForbiddenWorkItemComment_NamesOnlyTheScopesThisCommandNeeds()
    {
        _session.FakeWorkItems.AddCommentFailure = new VssServiceResponseException(HttpStatusCode.Forbidden, "Denied.", null);

        int exitCode = await Umbrella().InvokeAsync(["work-item", "comment", "7", "--body", "Q", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        string message = Envelope().GetProperty("errors")[0].GetProperty("message").GetString()!;
        Assert.Contains("Work Items (Read & write)", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Code (Read & write)", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkItemComment_UploadsAttachedFilesAndReportsThem()
    {
        string file = Path.Combine(Path.GetTempPath(), $"ado-write-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllTextAsync(file, "png bytes");

            int exitCode = await Umbrella().InvokeAsync(
                ["work-item", "comment", "7", "--body", "See the chart.", "--agent", Agent, "--attach", file, "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Single(_session.FakeWorkItems.AttachmentUpdates);
            JsonElement attachment = Envelope().GetProperty("results")[0].GetProperty("attachments")[0];
            Assert.Equal(Path.GetFileName(file), attachment.GetProperty("name").GetString());
            Assert.Equal(9, attachment.GetProperty("length").GetInt64());
            Assert.StartsWith("https://dev.azure.com/fabricated/_apis/wit/attachments/", attachment.GetProperty("url").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
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
    public async Task AuthLogin_StoresTheTokenTheReadToolsUseAndAsksNoPrivacyLevel()
    {
        var terminal = new ScriptedTerminalInput("new-token");

        int exitCode = await Umbrella(terminal).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("Personal access token for https://dev.azure.com/fabricated: ", Assert.Single(terminal.Prompts));
        Assert.Equal("new-token", _vault.Read(CredentialAccount.For(Fabricated.Organization)));
        Assert.Null(_vault.Read($"{CredentialAccount.For(Fabricated.Organization)}#write"));
        JsonElement result = Envelope().GetProperty("results")[0];
        Assert.True(result.GetProperty("tokenStored").GetBoolean());
        Assert.Equal(FakeTokenOwners.DisplayName, result.GetProperty("actingAs").GetString());
        Assert.False(result.TryGetProperty("privacyConfigured", out _));
    }

    [Fact]
    public async Task AuthLogout_RemovesTheSharedToken()
    {
        int exitCode = await Umbrella().InvokeAsync(["auth", "logout", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(_vault.Read(CredentialAccount.For(Fabricated.Organization)));
    }

    [Fact]
    public async Task OneLoginInAReadToolSignsInTheWriteToolsAndOneLogoutSignsThemAllOut()
    {
        _vault.Remove(CredentialAccount.For(Fabricated.Organization));
        var readOutput = new StringWriter();
        CommandApplication read = new ToolComposition(
                new FixedConfigurationStore(),
                new JsonOutput(readOutput),
                TimeProvider.System,
                credentialVault: _vault,
                terminalInput: new ScriptedTerminalInput("read-login-token", "2"),
                tokenOwners: new FakeTokenOwners())
            .CreateUmbrella("ado-read", []);

        Assert.Equal(ExitCodes.Success, await read.InvokeAsync(["auth", "login", "--compact"], CancellationToken.None));
        Assert.True(await WriteToolSeesATokenAsync());
        Assert.Equal(ExitCodes.Success, await Umbrella().InvokeAsync(["pr", "comment", "42", "--body", "Q", "--compact"], CancellationToken.None));
        Assert.Equal("read-login-token", _factory.Pat);

        _output.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await Umbrella().InvokeAsync(["auth", "logout", "--compact"], CancellationToken.None));
        readOutput.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await read.InvokeAsync(["auth", "status", "--compact"], CancellationToken.None));

        Assert.False(JsonDocument.Parse(readOutput.ToString()).RootElement.GetProperty("results")[0].GetProperty("tokenStored").GetBoolean());
        Assert.False(await WriteToolSeesATokenAsync());
        Assert.NotNull(_vault.Read(CredentialAccount.ForPrivacy(Fabricated.Organization)));
    }

    private async Task<bool> WriteToolSeesATokenAsync()
    {
        _output.GetStringBuilder().Clear();
        await Umbrella().InvokeAsync(["auth", "status", "--compact"], CancellationToken.None);
        bool stored = Envelope().GetProperty("results")[0].GetProperty("tokenStored").GetBoolean();
        _output.GetStringBuilder().Clear();
        return stored;
    }

    private CommandApplication Umbrella(ScriptedTerminalInput? terminal = null) =>
        Composition(terminal).CreateUmbrella(
            "ado-write",
            [PullRequestWriteModule.Create(_factory), WorkItemCommentModule.Create(_factory)]);

    private ToolComposition Composition(ScriptedTerminalInput? terminal = null) =>
        new(
            new FixedConfigurationStore(),
            new JsonOutput(_output),
            TimeProvider.System,
            credentialVault: _vault,
            terminalInput: terminal,
            toolKind: ToolKind.Write,
            tokenOwners: new FakeTokenOwners());

    private JsonElement Envelope() => JsonDocument.Parse(_output.ToString()).RootElement;
}
