using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;

namespace AdoWrite.UnitTests.Approval;

public sealed class ApprovalQueueTests : IDisposable
{
    private const string Token = "fabricated-token";

    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ado-write-approval-" + Guid.NewGuid().ToString("N"));
    private readonly FakeApprovalWindow _window = new();
    private readonly ApprovalOutbox _outbox;

    public ApprovalQueueTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), Token);
        _outbox = new ApprovalOutbox(Path.Combine(_root, "outbox"), TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_IsQueuedForApprovalInsteadOfPosted()
    {
        int exitCode = await Run("pr", "reply", "42", "--thread", "7", "--body", "Why twice?", "--agent", "Claude Opus 5.5");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(_factory.Pat);
        JsonElement envelope = JsonDocument.Parse(_output.ToString()).RootElement;
        Assert.Equal("pendingApproval", envelope.GetProperty("status").GetString());
        JsonElement result = envelope.GetProperty("results")[0];
        Assert.Equal("pendingApproval", result.GetProperty("outcome").GetString());
        Assert.Equal("opened", result.GetProperty("window").GetString());
        ApprovalRequest request = Assert.Single(_outbox.List());
        Assert.Equal(result.GetProperty("approvalId").GetString(), request.Id);
        Assert.Equal("pullRequestReply", request.Kind);
        Assert.Equal(["42", "--thread=7", "--agent=Claude Opus 5.5", "--body=Why twice?"], request.Arguments);
        Assert.Equal(1, _window.Opened);
    }

    [Fact]
    public async Task Write_FreezesTheBodyFileAndAttachmentsSoLaterChangesDoNotReachThePost()
    {
        Directory.CreateDirectory(_root);
        string bodyFile = Path.Combine(_root, "body.md");
        string chart = Path.Combine(_root, "chart.png");
        await File.WriteAllTextAsync(bodyFile, "- first\n- second\n");
        await File.WriteAllBytesAsync(chart, [1, 2, 3]);

        await Run("work-item", "comment", "7", "--body-file", bodyFile, "--attach", chart);
        await File.WriteAllTextAsync(bodyFile, "changed");
        await File.WriteAllBytesAsync(chart, [9]);

        ApprovalRequest request = Assert.Single(_outbox.List());
        Assert.Contains("--body=- first\n- second", request.Arguments);
        string attached = Assert.Single(request.Arguments, argument => argument.StartsWith("--attach=", StringComparison.Ordinal))["--attach=".Length..];
        Assert.NotEqual(chart, attached);
        Assert.Equal("chart.png", Path.GetFileName(attached));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(attached));
    }

    [Fact]
    public async Task Write_DoesNotOpenASecondWindow()
    {
        _window.IsOpen = true;

        await Run("pr", "vote", "42", "--vote", "approve");

        Assert.Equal(0, _window.Opened);
        Assert.Equal("alreadyOpen", Result().GetProperty("window").GetString());
    }

    [Fact]
    public async Task DryRun_RunsAtOnceAndQueuesNothing()
    {
        int exitCode = await Run("pr", "vote", "42", "--vote", "approve", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("preview", Result().GetProperty("outcome").GetString());
        Assert.Empty(_outbox.List());
        Assert.Equal(0, _window.Opened);
    }

    [Fact]
    public async Task InvalidInput_IsRefusedAtOnceAndQueuesNothing()
    {
        int exitCode = await Run("pr", "reply", "42", "--body", "Q");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Empty(_outbox.List());
    }

    [Fact]
    public async Task QueuedWrite_IsPostedByTheRunnerWithTheSameArguments()
    {
        await Run("pr", "comment", "42", "--body", "Why twice?");
        ApprovalRequest request = Assert.Single(_outbox.List());
        var runner = new ApprovalRunner(new FixedConfigurationStore(), _factory, _vault, new FakeTokenOwners(), TimeProvider.System);

        JsonElement preview = await runner.RunAsync(request, preview: true, CancellationToken.None);
        JsonElement posted = await runner.RunAsync(request, preview: false, CancellationToken.None);

        Assert.Equal("preview", preview.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("created", posted.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal(Token, _factory.Pat);
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData(" OFF ", true)]
    [InlineData("on", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MachineSetting_OnlyOffTurnsApprovalOff(string? value, bool off) =>
        Assert.Equal(off, MachineApprovalSetting.IsOff(value));

    private JsonElement Result() => JsonDocument.Parse(_output.ToString()).RootElement.GetProperty("results")[0];

    private Task<int> Run(params string[] args)
    {
        WriteApproval approval = WriteApproval.QueueIn(_outbox, _window);
        return new ToolComposition(
                new FixedConfigurationStore(),
                new JsonOutput(_output),
                TimeProvider.System,
                credentialVault: _vault,
                toolKind: ToolKind.Write,
                tokenOwners: new FakeTokenOwners())
            .CreateUmbrella("ado-write", [PullRequestWriteModule.Create(_factory, approval), WorkItemCommentModule.Create(_factory, approval)])
            .InvokeAsync([.. args, "--compact"], CancellationToken.None);
    }

    private sealed class FakeApprovalWindow : IApprovalWindow
    {
        public bool IsOpen { get; set; }

        public int Opened { get; private set; }

        public ApprovalWindowState EnsureOpen()
        {
            if (IsOpen)
            {
                return ApprovalWindowState.AlreadyOpen;
            }

            Opened++;
            return ApprovalWindowState.Opened;
        }
    }
}
