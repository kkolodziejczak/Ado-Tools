using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;

namespace AdoWrite.UnitTests.Approval;

public sealed class ApprovalReviewTests : IDisposable
{
    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly FakeWriteSessionFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ado-write-review-" + Guid.NewGuid().ToString("N"));
    private readonly ApprovalOutbox _outbox;

    public ApprovalReviewTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), "fabricated-token");
        _outbox = new ApprovalOutbox(_root, TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Review_ShowsTheTextThatWillBePostedBeforeAnythingIsPosted()
    {
        await Queue("pr", "comment", "42", "--body", "Why twice?", "--agent", "Claude Opus 5.5");
        var console = new ScriptedConsole(ConsoleKey.Q);

        await Review(console);

        string screen = console.LastScreen;
        Assert.Contains("1 waiting · 0 posted · 0 rejected", screen);
        Assert.Contains("PR 42  comment", screen);
        Assert.Contains("Queued by: Claude Opus 5.5", screen);
        Assert.Contains("Will be posted as you:", screen);
        Assert.Contains("  Why twice?", screen);
        Assert.Contains(ApprovalBoardView.Keys, screen);
        Assert.Empty(_session.FakePullRequests.CreatedThreads);
        Assert.Single(_outbox.List());
    }

    [Fact]
    public async Task Review_PostsOnYAndRejectsOnN()
    {
        await Queue("pr", "comment", "42", "--body", "Post me");
        await Queue("work-item", "comment", "7", "--body", "Reject me");

        await Review(new ScriptedConsole(ConsoleKey.Y, ConsoleKey.N, ConsoleKey.Q));

        Assert.Single(_session.FakePullRequests.CreatedThreads);
        Assert.Empty(_session.FakeWorkItems.Added);
        Assert.Empty(_outbox.List());
    }

    [Fact]
    public async Task Review_ArrowsPickAnyWriteAndTheRestStayQueued()
    {
        await Queue("pr", "comment", "42", "--body", "Later");
        await Queue("work-item", "comment", "7", "--body", "Now");
        var console = new ScriptedConsole(ConsoleKey.DownArrow, ConsoleKey.Y, ConsoleKey.Q);

        await Review(console);

        Assert.Contains("Now", Assert.Single(_session.FakeWorkItems.Added).Markdown);
        Assert.Empty(_session.FakePullRequests.CreatedThreads);
        Assert.Equal("pullRequestComment", Assert.Single(_outbox.List()).Kind);
        Assert.Contains("1 waiting · 1 posted · 0 rejected", console.LastScreen);
    }

    [Fact]
    public async Task Review_PostsAllRemainingOnA()
    {
        await Queue("pr", "comment", "42", "--body", "One");
        await Queue("work-item", "comment", "7", "--body", "Two");

        await Review(new ScriptedConsole(ConsoleKey.A, ConsoleKey.Q));

        Assert.Single(_session.FakePullRequests.CreatedThreads);
        Assert.Single(_session.FakeWorkItems.Added);
        Assert.Empty(_outbox.List());
    }

    [Fact]
    public async Task Review_KeepsAWriteThatFailedQueuedAndSaysWhy()
    {
        await Queue("pr", "reply", "42", "--thread", "999", "--body", "Nobody home");
        var console = new ScriptedConsole(ConsoleKey.Y, ConsoleKey.Q);

        await Review(console);

        Assert.Single(_outbox.List());
        Assert.Contains("failed", console.LastScreen);
        Assert.Contains("Failed: ", console.LastScreen);
    }

    [Fact]
    public async Task Review_PicksUpWritesQueuedWhileItIsOpen()
    {
        var console = new ScriptedConsole(null, ConsoleKey.Y, ConsoleKey.Q);
        console.BeforeKey = index =>
        {
            if (index == 0)
            {
                Queue("pr", "comment", "42", "--body", "Arrived later").GetAwaiter().GetResult();
            }
        };

        await Review(console);

        Assert.Single(_session.FakePullRequests.CreatedThreads);
    }

    [Fact]
    public async Task ReviewCommand_RefusesAConsoleWithRedirectedInput()
    {
        await Queue("pr", "comment", "42", "--body", "Q");
        var output = new StringWriter();

        int exitCode = await Composition(output).CreateUmbrella("ado-write", [ApprovalModule.Create(_outbox, new ScriptedConsole { IsInteractive = false }, _factory)])
            .InvokeAsync(["approval", "open", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("consoleRequired", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(_session.FakePullRequests.CreatedThreads);
        Assert.Single(_outbox.List());
    }

    [Fact]
    public async Task ReviewCommand_RefusesASecondWindow()
    {
        using FileStream? first = ConsoleApprovalWindow.TryHold(_root);
        var output = new StringWriter();

        int exitCode = await Composition(output).CreateUmbrella("ado-write", [ApprovalModule.Create(_outbox, new ScriptedConsole(ConsoleKey.Q), _factory)])
            .InvokeAsync(["approval", "open", "--compact"], CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("approvalWindowOpen", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void Board_DecidingTheSelectedWriteMovesToTheNextOpenOneAndWraps()
    {
        var board = new ApprovalBoard();
        board.Add([Request("a"), Request("b"), Request("c")]);
        board.Move(2);

        board.Mark("c", ApprovalItemStatus.Posted);
        Assert.Equal("a", board.Current!.Request.Id);

        board.Mark("a", ApprovalItemStatus.Rejected);
        Assert.Equal("b", board.Current!.Request.Id);

        board.Mark("b", ApprovalItemStatus.Failed, "timeout");
        Assert.Equal("b", board.Current!.Request.Id);
        Assert.Equal(1, board.OpenCount);
    }

    [Fact]
    public void View_DescribesEachKindFromItsArguments()
    {
        Assert.Equal("PR 42  comment · /src/App.cs:3", ApprovalBoardView.Describe(Request("a", "pullRequestComment", "42", "--file=/src/App.cs", "--line=3")));
        Assert.Equal("PR 42  vote · approve", ApprovalBoardView.Describe(Request("a", "pullRequestVote", "https://dev.azure.com/fabricated/p/_git/r/pullrequest/42", "--vote=approve")));
        Assert.Equal("New PR  Add export", ApprovalBoardView.Describe(Request("a", "pullRequestCreate", "--title=Add export")));
    }

    [Fact]
    public void View_NamesAnotherConfigurationBecauseItCanMeanAnotherOrganization()
    {
        var board = new ApprovalBoard();
        board.Add([Request("a", "pullRequestComment", "42", "--config=C:\\other\\config.json")]);

        string screen = string.Join('\n', ApprovalBoardView.Render(board, new Dictionary<string, JsonElement>(), TimeZoneInfo.Utc).Select(line => line.Text));

        Assert.Contains("Configuration: C:\\other\\config.json", screen);
        Assert.Contains("Loading the preview", screen);
    }

    private static ApprovalRequest Request(string id, string kind = "pullRequestComment", params string[] arguments) =>
        new(id, kind, arguments.Length == 0 ? ["42"] : arguments, DateTimeOffset.UnixEpoch);

    private Task Review(ScriptedConsole console) =>
        new ApprovalReview(
                _outbox,
                new ApprovalRunner(new FixedConfigurationStore(), _factory, _vault, new FakeTokenOwners(), TimeProvider.System),
                console,
                TimeZoneInfo.Utc)
            .RunAsync(CancellationToken.None);

    private Task<int> Queue(params string[] args)
    {
        WriteApproval approval = WriteApproval.QueueIn(_outbox, new ClosedWindow());
        return Composition(new StringWriter())
            .CreateUmbrella("ado-write", [PullRequestWriteModule.Create(_factory, approval), WorkItemCommentModule.Create(_factory, approval)])
            .InvokeAsync([.. args, "--compact"], CancellationToken.None);
    }

    private ToolComposition Composition(StringWriter output) => new(
        new FixedConfigurationStore(),
        new JsonOutput(output),
        TimeProvider.System,
        credentialVault: _vault,
        toolKind: ToolKind.Write,
        tokenOwners: new FakeTokenOwners());

    private sealed class ClosedWindow : IApprovalWindow
    {
        public ApprovalWindowState EnsureOpen() => ApprovalWindowState.NotOpened;
    }

    /// <summary>Returns its keys in order; null stands for a wait with no key pressed. Quits when they run out.</summary>
    private sealed class ScriptedConsole(params ConsoleKey?[] keys) : IApprovalConsole
    {
        private int _next;

        public bool IsInteractive { get; init; } = true;

        public Action<int>? BeforeKey { get; set; }

        public string LastScreen { get; private set; } = string.Empty;

        public ConsoleKeyInfo? ReadKey(TimeSpan wait)
        {
            BeforeKey?.Invoke(_next);
            ConsoleKey? key = _next < keys.Length ? keys[_next] : ConsoleKey.Q;
            _next++;
            return key is { } pressed ? new ConsoleKeyInfo('\0', pressed, false, false, false) : null;
        }

        public void Draw(IReadOnlyList<ScreenLine> lines) => LastScreen = string.Join('\n', lines.Select(line => line.Text));
    }
}
