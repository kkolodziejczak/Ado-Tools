using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.UnitTests.Approval;

public sealed class ApprovalPromptTests : IDisposable
{
    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly FakeWriteSessionFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ado-write-prompt-" + Guid.NewGuid().ToString("N"));
    private readonly PendingWrites _pending;

    public ApprovalPromptTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), "fabricated-token");
        _pending = new PendingWrites(_root, TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Enter_PostsTheWriteOnlyAfterItsDryRunWasShown()
    {
        string id = await Save("pr", "comment", "42", "--body", "Why twice?", "--agent", "Claude Opus 5.5");
        var console = new ScriptedConsole(ConsoleKey.Enter);
        int postedBeforeEnter = -1;
        console.BeforeKey = index => postedBeforeEnter = index == 0 ? _session.FakePullRequests.CreatedThreads.Count : postedBeforeEnter;

        await Prompt(console, id);

        string read = console.Screens[1];
        Assert.Contains("PR 42 · comment", read);
        Assert.Contains($"Organization: {Fabricated.Organization}", read);
        Assert.Contains($"Posted as: {Fabricated.Author.DisplayName}", read);
        Assert.Contains("Requested by: Claude Opus 5.5", read);
        Assert.Contains("  Why twice?", read);
        Assert.Contains(ApprovalScreen.ReadyKeys, read);
        Assert.Equal(0, postedBeforeEnter);
        Assert.Single(_session.FakePullRequests.CreatedThreads);
        Assert.Contains("Posted: ", console.LastScreen);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.N)]
    [InlineData(ConsoleKey.Spacebar)]
    public async Task AnyOtherKey_CancelsAndNothingIsPosted(ConsoleKey key)
    {
        string id = await Save("work-item", "comment", "7", "--body", "Not this one");

        await Prompt(new ScriptedConsole(key), id);

        Assert.Empty(_session.FakeWorkItems.Added);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task Window_DeletesTheWriteWhenItCloses_SoNoOtherWindowCanPostIt()
    {
        string id = await Save("pr", "comment", "42", "--body", "Once");

        await Prompt(new ScriptedConsole(ConsoleKey.Enter, ConsoleKey.Enter), id);
        await Prompt(new ScriptedConsole(ConsoleKey.Enter, ConsoleKey.Enter), id);

        Assert.Single(_session.FakePullRequests.CreatedThreads);
    }

    [Fact]
    public async Task FailedDryRun_CannotBePosted()
    {
        string id = await Save("pr", "reply", "42", "--thread", "999", "--body", "Nobody home");
        var console = new ScriptedConsole(ConsoleKey.Enter, ConsoleKey.Enter);

        await Prompt(console, id);

        Assert.Empty(_session.FakePullRequests.CreatedComments);
        Assert.Contains("The dry run failed, so this write can't be posted", console.LastScreen);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task Changed_SinceTheDryRunWasRead_IsShownAgainAndNeedsAnotherEnter()
    {
        AddThread(7, CommentThreadStatus.Active);
        string id = await Save("pr", "resolve", "42", "--thread", "7");
        var console = new ScriptedConsole(ConsoleKey.Enter, ConsoleKey.Escape);
        console.BeforeKey = index =>
        {
            if (index == 0)
            {
                _session.FakePullRequests.ChangeStatusElsewhere(7, CommentThreadStatus.WontFix);
            }
        };

        await Prompt(console, id);

        Assert.Empty(_session.FakePullRequests.StatusUpdates);
        Assert.Contains("It changed since you read it", console.LastScreen);
        Assert.Contains("Thread status: wontFix → wontFix", console.LastScreen);
    }

    [Fact]
    public async Task KeysTypedBeforeAScreenWasShown_NeverDecideIt()
    {
        string id = await Save("pr", "comment", "42", "--body", "Read me first");
        var console = new ScriptedConsole(ConsoleKey.Enter, ConsoleKey.Enter);

        await Prompt(console, id);

        Assert.All(console.Reads, read => Assert.True(read.DiscardedAfterLastDraw));
    }

    [Fact]
    public async Task OrganizationChanged_AfterTheWriteWasRequested_CannotBePosted()
    {
        string id = await Save("work-item", "comment", "7", "--body", "Internal note");
        var console = new ScriptedConsole(ConsoleKey.Enter);
        var other = new FixedConfigurationStore { Organization = new Uri("https://dev.azure.com/second/") };

        await new ApprovalPrompt(_pending, new ApprovalRunner(other, _factory, _vault, new FakeTokenOwners(), TimeProvider.System), console)
            .RunAsync(id, CancellationToken.None);

        Assert.Empty(_session.FakeWorkItems.Added);
        Assert.Contains("organizationChanged", console.LastScreen);
    }

    [Fact]
    public async Task UnknownWrite_SaysSoAndPostsNothing()
    {
        var console = new ScriptedConsole(ConsoleKey.Enter);

        await Prompt(console, "20260101-000000-0123abcd");

        Assert.Contains("This write isn't waiting here", console.LastScreen);
        Assert.Empty(_session.FakePullRequests.CreatedThreads);
    }

    [Fact]
    public void Screen_ShowsControlAndDirectionCharactersAsCodes_SoTheTextCannotDisguiseItself()
    {
        JsonElement preview = Envelope(new { outcome = "preview", actingAs = "Example Author", text = "ok\u001b[2J\u001b[Hfake\b\r‮evil" });

        IReadOnlyList<ScreenLine> lines = ApprovalScreen.Render(Request("pullRequestComment", "42"), PromptStage.Ready, preview);

        string screen = string.Join('\n', lines.Select(line => line.Text));
        Assert.Contains("ok^[[2J^[[Hfake^H", screen);
        Assert.Contains("<U+202E>evil", screen);
        Assert.DoesNotContain(screen.Replace("\n", string.Empty, StringComparison.Ordinal), character => char.IsControl(character));
    }

    [Fact]
    public void Screen_ShowsWhatAPullRequestWillBeCreatedWith()
    {
        JsonElement preview = Envelope(new
        {
            outcome = "preview",
            title = "Add export",
            sourceRefName = "refs/heads/feature/export",
            targetRefName = "refs/heads/main",
            isDraft = true,
            project = "Fabricated App",
            repository = "Fabricated.Repo",
            reviewers = new[] { new { input = "reviewer@example.com" } },
            workItems = new[] { 7 },
            actingAs = "Example Author",
            description = "Adds CSV export."
        });

        string screen = string.Join('\n', ApprovalScreen.Render(Request("pullRequestCreate", "--title=Add export"), PromptStage.Ready, preview).Select(line => line.Text));

        Assert.Contains("Repository: Fabricated App/Fabricated.Repo  feature/export → main", screen);
        Assert.Contains("Draft: yes", screen);
        Assert.Contains("Reviewers: reviewer@example.com", screen);
        Assert.Contains("Work items: 7", screen);
        Assert.Contains("Adds CSV export.", screen);
    }

    [Fact]
    public async Task OpenCommand_RefusesAConsoleWithRedirectedInput()
    {
        string id = await Save("pr", "comment", "42", "--body", "Q");
        var output = new StringWriter();

        int exitCode = await Composition(output).CreateUmbrella("ado-write", [ApprovalModule.Create(_pending, new ScriptedConsole { IsInteractive = false }, _factory)])
            .InvokeAsync(["approval", "open", id, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("consoleRequired", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(_session.FakePullRequests.CreatedThreads);
        Assert.NotNull(_pending.Claim(id));
    }

    private static ApprovalRequest Request(string kind, params string[] arguments) =>
        new("20260101-000000-0123abcd", kind, arguments, Fabricated.Organization, DateTimeOffset.UnixEpoch);

    private static JsonElement Envelope(object result) =>
        JsonSerializer.SerializeToElement(new { status = "success", results = new[] { result } });

    private void AddThread(int id, CommentThreadStatus status) =>
        _session.FakePullRequests.Threads.Add(new GitPullRequestCommentThread
        {
            Id = id,
            Status = status,
            Comments = [Fabricated.CommentBy(1, "Please rename this.", Fabricated.Reviewer)]
        });

    private Task Prompt(ScriptedConsole console, string id) =>
        new ApprovalPrompt(_pending, new ApprovalRunner(new FixedConfigurationStore(), _factory, _vault, new FakeTokenOwners(), TimeProvider.System), console)
            .RunAsync(id, CancellationToken.None);

    /// <summary>Saves a write as an agent's command would, and returns its ID.</summary>
    private async Task<string> Save(params string[] args)
    {
        var window = new RecordingWindow();
        int exitCode = await Composition(new StringWriter())
            .CreateUmbrella("ado-write", [PullRequestWriteModule.Create(_factory, WriteApproval.Required(_pending, window)), WorkItemCommentModule.Create(_factory, WriteApproval.Required(_pending, window))])
            .InvokeAsync([.. args, "--compact"], CancellationToken.None);
        Assert.Equal(ExitCodes.Success, exitCode);
        return window.Id!;
    }

    private ToolComposition Composition(StringWriter output) => new(
        new FixedConfigurationStore(),
        new JsonOutput(output),
        TimeProvider.System,
        credentialVault: _vault,
        toolKind: ToolKind.Write,
        tokenOwners: new FakeTokenOwners());

    /// <summary>Opens nothing and leaves the write for the window under test to claim.</summary>
    private sealed class RecordingWindow : IApprovalWindow
    {
        public string? Id { get; private set; }

        public bool Open(string id)
        {
            Id = id;
            return true;
        }
    }

    /// <summary>Returns its keys in order, then Escape. Records every screen and whether typed-ahead keys were dropped before each read.</summary>
    private sealed class ScriptedConsole(params ConsoleKey[] keys) : IApprovalConsole
    {
        private int _next;
        private bool _discardedSinceDraw;

        public bool IsInteractive { get; init; } = true;

        public Action<int>? BeforeKey { get; set; }

        public List<string> Screens { get; } = [];

        public List<(ConsoleKey Key, bool DiscardedAfterLastDraw)> Reads { get; } = [];

        public string LastScreen => Screens.Count > 0 ? Screens[^1] : string.Empty;

        public ConsoleKeyInfo ReadKey()
        {
            BeforeKey?.Invoke(_next);
            ConsoleKey key = _next < keys.Length ? keys[_next] : ConsoleKey.Escape;
            _next++;
            Reads.Add((key, _discardedSinceDraw));
            return new ConsoleKeyInfo('\0', key, false, false, false);
        }

        public void DiscardTypedKeys() => _discardedSinceDraw = true;

        public void Draw(IReadOnlyList<ScreenLine> lines)
        {
            _discardedSinceDraw = false;
            Screens.Add(string.Join('\n', lines.Select(line => line.Text)));
        }
    }
}
