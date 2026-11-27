using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;

namespace AdoWrite.UnitTests.Approval;

public sealed class PendingWriteTests : IDisposable
{
    private const string Token = "fabricated-token";

    private readonly FakeWriteSession _session = new();
    private readonly MemoryCredentialVault _vault = new();
    private readonly StringWriter _output = new();
    private readonly FakeWriteSessionFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ado-write-approval-" + Guid.NewGuid().ToString("N"));
    private readonly PendingWrites _pending;
    private readonly FakeApprovalWindow _window;

    public PendingWriteTests()
    {
        _factory = new FakeWriteSessionFactory(_session);
        _vault.Write(CredentialAccount.For(Fabricated.Organization), Token);
        _pending = new PendingWrites(Path.Combine(_root, "approvals"), TimeProvider.System);
        _window = new FakeApprovalWindow(_pending);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_OpensAWindowOfItsOwnInsteadOfPosting()
    {
        int exitCode = await Run("pr", "reply", "42", "--thread", "7", "--body", "Why twice?", "--agent", "Claude Opus 5.5");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(_factory.Pat);
        JsonElement envelope = JsonDocument.Parse(_output.ToString()).RootElement;
        Assert.Equal("pendingApproval", envelope.GetProperty("status").GetString());
        JsonElement result = envelope.GetProperty("results")[0];
        Assert.Equal("pendingApproval", result.GetProperty("outcome").GetString());
        string id = result.GetProperty("approvalId").GetString()!;
        Assert.Equal([id], _window.Opened);
        ApprovalRequest request = Assert.IsType<ApprovalRequest>(_window.Claimed.Single());
        Assert.Equal("pullRequestReply", request.Kind);
        Assert.Equal(Fabricated.Organization, request.Organization);
        Assert.Equal(["42", "--thread=7", "--agent=Claude Opus 5.5", "--body=Why twice?"], request.Arguments);
    }

    [Fact]
    public async Task Write_IsRefusedAndNotKeptWhenNoWindowCanOpen()
    {
        _window.Opens = false;

        int exitCode = await Run("pr", "vote", "42", "--vote", "approve");

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement envelope = JsonDocument.Parse(_output.ToString()).RootElement;
        Assert.Equal("approvalWindowUnavailable", envelope.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(Directory.GetDirectories(_pending.Directory));
        Assert.Empty(_session.FakeReviews.Votes);
    }

    [Fact]
    public async Task Write_FreezesTheBodyFileAndAttachmentsSoLaterChangesDoNotReachThePost()
    {
        Directory.CreateDirectory(_root);
        string bodyFile = Path.Combine(_root, "body.md");
        string chart = Path.Combine(_root, "chart.png");
        await File.WriteAllTextAsync(bodyFile, "- first\n- second\n");
        await File.WriteAllBytesAsync(chart, [1, 2, 3]);
        _window.ClaimsOnOpen = false;

        await Run("work-item", "comment", "7", "--body-file", bodyFile, "--attach", chart);
        await File.WriteAllTextAsync(bodyFile, "changed");
        await File.WriteAllBytesAsync(chart, [9]);

        ApprovalRequest request = _pending.Claim(_window.Opened.Single())!;
        Assert.Contains("--body=- first\n- second", request.Arguments);
        string attached = Assert.Single(request.Arguments, argument => argument.StartsWith("--attach=", StringComparison.Ordinal))["--attach=".Length..];
        Assert.NotEqual(chart, attached);
        Assert.Equal("chart.png", Path.GetFileName(attached));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(attached));
    }

    [Fact]
    public async Task DryRun_RunsAtOnceAndSavesNothing()
    {
        int exitCode = await Run("pr", "vote", "42", "--vote", "approve", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("preview", Result().GetProperty("outcome").GetString());
        Assert.Empty(_window.Opened);
        Assert.False(Directory.Exists(_pending.Directory));
    }

    [Fact]
    public async Task InvalidInput_IsRefusedAtOnceAndSavesNothing()
    {
        int exitCode = await Run("pr", "reply", "42", "--body", "Q");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Empty(_window.Opened);
    }

    [Theory]
    [InlineData("pr", "comment", "42", "--body", "Hi")]
    [InlineData("pr", "resolve", "42", "--thread", "7")]
    [InlineData("pr", "unresolve", "42", "--thread", "7")]
    [InlineData("pr", "requeue", "42")]
    [InlineData("pr", "vote", "42", "--vote", "approve")]
    [InlineData("pr", "create", "--repository", "Fabricated.Repo", "--project", "Fabricated App", "--source", "feature/x", "--title", "Add x")]
    [InlineData("work-item", "comment", "7", "--body", "Hi")]
    public async Task EveryWriteWaitsForApproval(params string[] args)
    {
        int exitCode = await Run(args);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("pendingApproval", Result().GetProperty("outcome").GetString());
        Assert.Null(_factory.Pat);
    }

    [Fact]
    public async Task Runner_PostsTheWriteWithTheSameArguments()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "Why twice?");
        ApprovalRequest request = _pending.Claim(_window.Opened.Single())!;

        JsonElement preview = await Runner().RunAsync(request, preview: true, CancellationToken.None);
        JsonElement posted = await Runner().RunAsync(request, preview: false, CancellationToken.None);

        Assert.Equal("preview", preview.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("created", posted.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal(Token, _factory.Pat);
    }

    [Fact]
    public async Task Runner_RefusesWhenTheConfigurationNowPointsToAnotherOrganization()
    {
        _window.ClaimsOnOpen = false;
        await Run("work-item", "comment", "7", "--body", "Internal note");
        ApprovalRequest request = _pending.Claim(_window.Opened.Single())!;
        var configuration = new FixedConfigurationStore { Organization = new Uri("https://dev.azure.com/second/") };

        JsonElement posted = await new ApprovalRunner(configuration, _factory, _vault, new FakeTokenOwners(), TimeProvider.System)
            .RunAsync(request, preview: false, CancellationToken.None);

        Assert.Equal("organizationChanged", posted.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Empty(_session.FakeWorkItems.Added);
    }

    [Fact]
    public async Task Runner_PostsWithTheConfigurationItChecked_EvenWhenTheFileChangesRightAfter()
    {
        _window.ClaimsOnOpen = false;
        await Run("work-item", "comment", "7", "--body", "Internal note");
        ApprovalRequest request = _pending.Claim(_window.Opened.Single())!;
        var second = new Uri("https://dev.azure.com/second/");
        _vault.Write(CredentialAccount.For(second), "second-token");
        var configuration = new SequencedConfigurationStore(Fabricated.Organization, second);

        JsonElement posted = await new ApprovalRunner(configuration, _factory, _vault, new FakeTokenOwners(), TimeProvider.System)
            .RunAsync(request, preview: false, CancellationToken.None);

        Assert.Equal("success", posted.GetProperty("status").GetString());
        Assert.Equal(Fabricated.Organization, _factory.OrganizationUrl);
        Assert.Equal(Token, _factory.Pat);
    }

    [Fact]
    public void Pinned_KeepsTheTargetBranchTheDryRunShowed()
    {
        ApprovalRequest request = new("20260101-000000-0123abcd", "pullRequestCreate", ["--title=Add x", "--source=feature/x"], Fabricated.Organization, DateTimeOffset.UnixEpoch);
        JsonElement preview = JsonSerializer.SerializeToElement(new { status = "success", results = new[] { new { outcome = "preview", targetRefName = "refs/heads/main" } } });

        Assert.Equal(["--title=Add x", "--source=feature/x", "--target=refs/heads/main"], request.Pinned(preview)!.Arguments);
    }

    [Fact]
    public void Pinned_RequeuesOnlyTheBuildsTheDryRunShowed()
    {
        ApprovalRequest request = new("20260101-000000-0123abcd", "pullRequestRequeue", ["42"], Fabricated.Organization, DateTimeOffset.UnixEpoch);
        JsonElement preview = JsonSerializer.SerializeToElement(new
        {
            status = "success",
            results = new[] { new { outcome = "preview", evaluationId = "eval-a" }, new { outcome = "notExpired", evaluationId = "eval-b" } }
        });
        JsonElement nothing = JsonSerializer.SerializeToElement(new { status = "success", results = new[] { new { outcome = "notExpired", evaluationId = "eval-b" } } });

        Assert.Equal(["42", "--evaluation=eval-a"], request.Pinned(preview)!.Arguments);
        Assert.Null(request.Pinned(nothing));
    }

    [Fact]
    public async Task Claim_GivesTheWriteToOneWindowOnly()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "Once");
        string id = _window.Opened.Single();

        Assert.NotNull(_pending.Claim(id));
        Assert.Null(_pending.Claim(id));
        Assert.False(_pending.Withdraw(id));
    }

    [Fact]
    public async Task Withdraw_DeletesAWriteNoWindowClaimedAndNoWindowCanClaimItAfter()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "Too late");
        string id = _window.Opened.Single();

        Assert.True(_pending.Withdraw(id));
        Assert.Null(_pending.Claim(id));
        Assert.Empty(Directory.GetDirectories(_pending.Directory));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../config")]
    [InlineData("20260101-000000-abc")]
    public void Claim_RefusesAnIdItDidNotMake(string id) => Assert.Null(_pending.Claim(id));

    [Fact]
    public void Claim_RefusesAHandWrittenRequestThatWouldOnlyDryRun()
    {
        const string id = "20260101-000000-0123abcd";
        Directory.CreateDirectory(Path.Combine(_pending.Directory, id));
        File.WriteAllText(
            Path.Combine(_pending.Directory, id, "request.json"),
            JsonSerializer.Serialize(new ApprovalRequest(id, "pullRequestComment", ["42", "--body=x", "--dry-run"], Fabricated.Organization, DateTimeOffset.UnixEpoch), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Null(_pending.Claim(id));
    }

    [Fact]
    public async Task Window_WithdrawsTheWriteWhenNoWindowStarts()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "x");
        string id = _window.Opened.Single();

        bool opened = new ConsoleApprovalWindow(_pending, TimeSpan.FromSeconds(5), _ => [() => false]).Open(id);

        Assert.False(opened);
        Assert.Empty(Directory.GetDirectories(_pending.Directory));
    }

    [Fact]
    public async Task Window_WithdrawsTheWriteWhenTheStartedWindowNeverClaimsIt()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "x");
        string id = _window.Opened.Single();

        bool opened = new ConsoleApprovalWindow(_pending, TimeSpan.FromMilliseconds(300), _ => [() => true]).Open(id);

        Assert.False(opened);
        Assert.Null(_pending.Claim(id));
    }

    [Fact]
    public async Task Window_ReportsOpenOnceTheStartedWindowClaimsTheWrite()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "x");
        string id = _window.Opened.Single();

        bool opened = new ConsoleApprovalWindow(_pending, TimeSpan.FromSeconds(5), _ => [() => _pending.Claim(id) is not null]).Open(id);

        Assert.True(opened);
        Assert.Single(Directory.GetDirectories(_pending.Directory));
    }

    [Fact]
    public async Task Window_KeepsTheWriteWithAWindowThatClaimedItAlthoughItsLauncherFailed()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "x");
        string id = _window.Opened.Single();

        // Like an osascript that timed out after Terminal had started the window.
        bool opened = new ConsoleApprovalWindow(_pending, TimeSpan.FromSeconds(5), _ => [() => _pending.Claim(id) is null]).Open(id);

        Assert.True(opened);
        Assert.Single(Directory.GetDirectories(_pending.Directory));
    }

    [Fact]
    public async Task Window_TriesTheNextTerminalWhenOneStartsButNeverShowsTheWindow()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "x");
        string id = _window.Opened.Single();
        int tried = 0;

        bool opened = new ConsoleApprovalWindow(
                _pending,
                TimeSpan.FromMilliseconds(300),
                _ => [() => ++tried > 0, () => ++tried > 0 && _pending.Claim(id) is not null])
            .Open(id);

        Assert.True(opened);
        Assert.Equal(2, tried);
    }

    [Fact]
    public async Task Save_DeletesWritesWhoseWindowClosedWithoutCleaningUpADayAgo()
    {
        _window.ClaimsOnOpen = false;
        await Run("pr", "comment", "42", "--body", "Abandoned");
        string abandoned = _window.Opened.Single();
        Assert.NotNull(_pending.Claim(abandoned));
        File.SetLastWriteTimeUtc(Path.Combine(_pending.Directory, abandoned, "claim"), DateTime.UtcNow.AddDays(-2));
        await Run("pr", "comment", "42", "--body", "Open now");
        string open = _window.Opened[1];
        Assert.NotNull(_pending.Claim(open));

        await Run("pr", "comment", "42", "--body", "Next");

        Assert.False(Directory.Exists(Path.Combine(_pending.Directory, abandoned)));
        Assert.True(Directory.Exists(Path.Combine(_pending.Directory, open)));
    }

    [Theory]
    [InlineData("--dry-run")]
    [InlineData("--dry-run=true")]
    public void Claim_RefusesAHandWrittenRequestThatWouldOnlyDryRunInAnyForm(string dryRun)
    {
        const string id = "20260101-000000-0123abce";
        Directory.CreateDirectory(Path.Combine(_pending.Directory, id));
        File.WriteAllText(
            Path.Combine(_pending.Directory, id, "request.json"),
            JsonSerializer.Serialize(new ApprovalRequest(id, "pullRequestComment", ["42", "--body=x", dryRun], Fabricated.Organization, DateTimeOffset.UnixEpoch), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Null(_pending.Claim(id));
    }

    [Fact]
    public void Claim_DeletesAWriteItTookButCannotRead()
    {
        const string id = "20260101-000000-0123abcd";
        Directory.CreateDirectory(Path.Combine(_pending.Directory, id));
        File.WriteAllText(Path.Combine(_pending.Directory, id, "request.json"), "{ not json");

        Assert.Null(_pending.Claim(id));
        Assert.Empty(Directory.GetDirectories(_pending.Directory));
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData(" OFF ", true)]
    [InlineData("on", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MachineSetting_OnlyOffTurnsApprovalOff(string? value, bool off) =>
        Assert.Equal(off, MachineApprovalSetting.IsOff(value));

    [Fact]
    public void MachineSetting_TheUnixFileCountsOnlyWhenOnlyAnAdministratorCanChangeItAndItsFolder()
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "approval");
        File.WriteAllText(file, "off");

        Assert.Equal("off", MachineApprovalSetting.ReadUnixFile(file, _ => true));
        Assert.Null(MachineApprovalSetting.ReadUnixFile(file, path => path != file));
        Assert.Null(MachineApprovalSetting.ReadUnixFile(file, path => path != _root));
        Assert.Null(MachineApprovalSetting.ReadUnixFile(Path.Combine(_root, "missing"), _ => true));
    }

    [Fact]
    public void MachineSetting_OnMacOSAndLinux_TrustsRootsFoldersOnlyAndNeverTheUsersOwn()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Root itself may write /etc, so for root it doesn't count either.
        Assert.Equal(Environment.UserName != "root", MachineApprovalSetting.OnlyAnAdministratorCanChange("/etc"));
        Directory.CreateDirectory(_root);
        Assert.False(MachineApprovalSetting.OnlyAnAdministratorCanChange(_root));
    }

    private ApprovalRunner Runner() => new(new FixedConfigurationStore(), _factory, _vault, new FakeTokenOwners(), TimeProvider.System);

    private JsonElement Result() => JsonDocument.Parse(_output.ToString()).RootElement.GetProperty("results")[0];

    private Task<int> Run(params string[] args)
    {
        WriteApproval approval = WriteApproval.Required(_pending, _window);
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

    /// <summary>Each load returns the next organization, then stays on the last: a configuration file that changes between reads.</summary>
    private sealed class SequencedConfigurationStore(params Uri[] organizations) : IConfigurationStore
    {
        private int _loads;

        public string DefaultPath => "memory://config";

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(
                organizations[Math.Min(_loads++, organizations.Length - 1)],
                AdoReadConfiguration.DefaultRequestTimeoutSeconds,
                AdoReadConfiguration.DefaultMaxParallelProjects));

        public Task SaveAsync(string? overridePath, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>A window that opens at once and, unless told otherwise, claims the write as a real one would.</summary>
    private sealed class FakeApprovalWindow(PendingWrites pending) : IApprovalWindow
    {
        public bool Opens { get; set; } = true;

        public bool ClaimsOnOpen { get; set; } = true;

        public List<string> Opened { get; } = [];

        public List<ApprovalRequest?> Claimed { get; } = [];

        public bool Open(string id)
        {
            Opened.Add(id);
            if (!Opens)
            {
                pending.Withdraw(id);
                return false;
            }

            if (ClaimsOnOpen)
            {
                Claimed.Add(pending.Claim(id));
            }

            return true;
        }
    }
}
