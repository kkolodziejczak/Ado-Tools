using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Terminal;
using AdoRead.UnitTests.Credentials;
using AdoRead.UnitTests.Privacy;
using AdoRead.UnitTests.Terminal;

namespace AdoRead.UnitTests.Commands;

public sealed class PrivacyCommandTests
{
    private static readonly Uri Organization = new("https://dev.azure.com/fabricated/");

    private static readonly CommandModule[] AllModules =
    [
        PullRequestModule.Definition,
        WorkItemModule.Definition,
        WikiModule.Definition,
        SprintModule.Definition
    ];

    public static TheoryData<string[]> ReadCommandsWithoutPolicy => new()
    {
        new[] { "projects" },
        new[] { "teams" },
        new[] { "pr", "126" },
        new[] { "work-item", "42" },
        new[] { "wiki", "list" },
        new[] { "wiki", "search", "authentication" },
        new[] { "sprints", "show" },
    };

    [Fact]
    public async Task Set_LooseningWithRedirectedInputIsRefusedAndStoresNothing()
    {
        var vault = Seeded(PrivacyLevel.Standard);
        var terminal = new ScriptedTerminalInput(isInputRedirected: true, answer: null);
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["privacy", "set", "full", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("interactiveTerminalRequired", FirstError(writer.ToString()).GetProperty("code").GetString());
        Assert.Equal(PrivacyLevel.Standard, StoredPolicy(vault).Policy.Level);
        Assert.Empty(terminal.LinePrompts);
    }

    [Fact]
    public async Task Set_FirstPolicyWithRedirectedInputIsRefused()
    {
        var vault = new MemoryCredentialVault();
        var writer = new StringWriter();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer)
            .InvokeAsync(["privacy", "set", "standard", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("interactiveTerminalRequired", FirstError(writer.ToString()).GetProperty("code").GetString());
        Assert.Null(vault.Read(CredentialAccount.ForPrivacy(Organization)));
    }

    [Fact]
    public async Task Set_TighteningWithRedirectedInputSucceedsWithoutConfirmation()
    {
        var vault = Seeded(PrivacyLevel.Full);
        var terminal = new ScriptedTerminalInput(isInputRedirected: true, answer: null);
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["privacy", "set", "minimal", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(PrivacyLevel.Minimal, StoredPolicy(vault).Policy.Level);
        Assert.Empty(terminal.LinePrompts);
        Assert.Equal("privacySet", FirstEnvelopeKind(writer.ToString()));
    }

    [Fact]
    public async Task Set_LevelNamesAreCaseInsensitive()
    {
        var vault = Seeded(PrivacyLevel.Full);

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: true, answer: null), new StringWriter())
            .InvokeAsync(["privacy", "set", "STANDARD", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(PrivacyLevel.Standard, StoredPolicy(vault).Policy.Level);
    }

    [Fact]
    public async Task Set_LooseningWithTheTypedOrganizationNameStoresTheNewPolicy()
    {
        var vault = Seeded(PrivacyLevel.Standard);
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: null, "fabricated");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["privacy", "set", "full", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(PrivacyLevel.Full, StoredPolicy(vault).Policy.Level);
        string prompt = Assert.Single(terminal.LinePrompts);
        Assert.Contains("newly grants people, customFields, images", prompt, StringComparison.Ordinal);
        Assert.Contains("'fabricated'", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_LooseningWithAWrongNameStoresNothing()
    {
        var vault = Seeded(PrivacyLevel.Standard);
        string before = vault.Read(CredentialAccount.ForPrivacy(Organization))!;
        var writer = new StringWriter();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: null, "fabricated-two"), writer)
            .InvokeAsync(["privacy", "set", "full", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("confirmationFailed", FirstError(writer.ToString()).GetProperty("code").GetString());
        Assert.Equal(before, vault.Read(CredentialAccount.ForPrivacy(Organization)));
    }

    [Fact]
    public async Task Set_FirstPolicyStoresAfterTheTypedOrganizationName()
    {
        var vault = new MemoryCredentialVault();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: null, "Fabricated"), new StringWriter())
            .InvokeAsync(["privacy", "set", "standard", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(PrivacyLevel.Standard, StoredPolicy(vault).Policy.Level);
    }

    [Fact]
    public async Task Set_UpdatingKeepsTheExistingAliasKey()
    {
        var key = Enumerable.Repeat((byte)9, 32).ToArray();
        var vault = Seeded(PrivacyLevel.Full, key);

        await App(vault, new ScriptedTerminalInput(isInputRedirected: true, answer: null), new StringWriter())
            .InvokeAsync(["privacy", "set", "standard", "--compact"], CancellationToken.None);
        int loosened = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: null, "fabricated"), new StringWriter())
            .InvokeAsync(["privacy", "set", "full", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, loosened);
        Assert.Equal(key, StoredPolicy(vault).AliasKey);
    }

    [Fact]
    public async Task Set_RejectsAnUnknownLevelWithoutPrompting()
    {
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: null);
        var writer = new StringWriter();

        int exitCode = await App(new MemoryCredentialVault(), terminal, writer)
            .InvokeAsync(["privacy", "set", "everything", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidPrivacyLevel", FirstError(writer.ToString()).GetProperty("code").GetString());
        Assert.Empty(terminal.LinePrompts);
    }

    [Fact]
    public async Task Set_RejectsACategoryThatIsBothAllowedAndDenied()
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Full), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer)
            .InvokeAsync(["privacy", "set", "standard", "--allow", "images", "--deny", "images", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("conflictingPrivacyOverrides", FirstError(writer.ToString()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Set_AllowAndDenyRefineTheLevelPreset()
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: null, "fabricated");

        int exitCode = await App(vault, terminal, new StringWriter())
            .InvokeAsync(["privacy", "set", "standard", "--allow", "customFields", "--deny", "comments", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        PrivacyPolicy policy = StoredPolicy(vault).Policy;
        Assert.True(policy.Allows(PrivacyCategory.CustomFields));
        Assert.False(policy.Allows(PrivacyCategory.Comments));
        Assert.True(policy.Allows(PrivacyCategory.Text));
    }

    [Fact]
    public async Task Show_ReportsTheLevelAndTheEffectiveCategories()
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Standard), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer)
            .InvokeAsync(["privacy", "show", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        JsonElement result = FirstResult(writer.ToString());
        Assert.True(result.GetProperty("configured").GetBoolean());
        Assert.Equal("standard", result.GetProperty("level").GetString());
        Dictionary<string, bool> allowed = result.GetProperty("categories").EnumerateArray()
            .ToDictionary(item => item.GetProperty("category").GetString()!, item => item.GetProperty("allowed").GetBoolean());
        Assert.True(allowed["text"]);
        Assert.True(allowed["comments"]);
        Assert.False(allowed["people"]);
        Assert.False(allowed["customFields"]);
        Assert.False(allowed["images"]);
    }

    [Fact]
    public async Task Show_WithoutAPolicyReportsNotConfiguredAndNoContent()
    {
        var writer = new StringWriter();

        int exitCode = await App(new MemoryCredentialVault(), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer)
            .InvokeAsync(["privacy", "show", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        JsonElement result = FirstResult(writer.ToString());
        Assert.False(result.GetProperty("configured").GetBoolean());
        Assert.All(result.GetProperty("categories").EnumerateArray(), item => Assert.False(item.GetProperty("allowed").GetBoolean()));
    }

    [Fact]
    public async Task Show_ACorruptPolicyIsReportedAsInvalidNotAsNoPolicy()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(CredentialAccount.ForPrivacy(Organization), "{\"version\":9}");
        var writer = new StringWriter();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer)
            .InvokeAsync(["privacy", "show", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        JsonElement error = FirstError(writer.ToString());
        Assert.Equal("privacy", error.GetProperty("scope").GetString());
        Assert.Equal("privacyPolicyInvalid", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reset_RemovesThePolicyButKeepsTheToken()
    {
        var vault = Seeded(PrivacyLevel.Full);
        vault.Write(CredentialAccount.For(Organization), "fabricated-token-value");
        var firstWriter = new StringWriter();
        var secondWriter = new StringWriter();
        var terminal = new ScriptedTerminalInput(isInputRedirected: true, answer: null);

        int first = await App(vault, terminal, firstWriter).InvokeAsync(["privacy", "reset", "--compact"], CancellationToken.None);
        int second = await App(vault, terminal, secondWriter).InvokeAsync(["privacy", "reset", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, first);
        Assert.True(FirstResult(firstWriter.ToString()).GetProperty("policyRemoved").GetBoolean());
        Assert.Equal(ExitCodes.Success, second);
        Assert.False(FirstResult(secondWriter.ToString()).GetProperty("policyRemoved").GetBoolean());
        Assert.Null(vault.Read(CredentialAccount.ForPrivacy(Organization)));
        Assert.Equal("fabricated-token-value", vault.Read(CredentialAccount.For(Organization)));
    }

    [Theory]
    [MemberData(nameof(ReadCommandsWithoutPolicy))]
    public async Task Read_WithoutAPolicyIsRefusedBeforeAnySessionOpens(string[] arguments)
    {
        var factory = new FixtureSessionFactory(new FixtureSession(new FixtureWorkItemSource()));
        var writer = new StringWriter();

        int exitCode = await App(new MemoryCredentialVault(), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, factory)
            .InvokeAsync([.. arguments, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        JsonElement error = FirstError(writer.ToString());
        Assert.Equal("privacy", error.GetProperty("scope").GetString());
        Assert.Equal("privacyPolicyRequired", error.GetProperty("code").GetString());
        Assert.Contains("privacy set <minimal|standard|full>", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(factory.Created);
        Assert.Equal("error", FirstEnvelopeStatus(writer.ToString()));
    }

    [Fact]
    public async Task WorkItem_PerCallMinimalWithholdsContentOnAFullPolicy()
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Full), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, WorkItemSessions())
            .InvokeAsync(["work-item", "42", "--privacy", "minimal", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        JsonElement result = FirstResult(writer.ToString());
        Assert.Null(result.GetProperty("description").GetProperty("raw").GetString());
        Assert.Equal("minimal", PrivacyLevelOf(writer.ToString()));
        JsonElement privacy = FirstPrivacy(writer.ToString());
        Assert.True(privacy.GetProperty("restrictedByCall").GetBoolean());
        Assert.Equal(["text", "comments", "customFields", "images"], Strings(privacy.GetProperty("withheld")));
        Assert.Equal(["people"], Strings(privacy.GetProperty("aliased")));
    }

    [Fact]
    public async Task WorkItem_PerCallFullCannotLoosenAMinimalPolicy()
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Minimal), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, WorkItemSessions())
            .InvokeAsync(["work-item", "42", "--privacy", "full", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(FirstResult(writer.ToString()).GetProperty("description").GetProperty("raw").GetString());
        Assert.Equal("minimal", PrivacyLevelOf(writer.ToString()));
        Assert.False(FirstPrivacy(writer.ToString()).GetProperty("restrictedByCall").GetBoolean());
    }

    [Fact]
    public async Task WorkItem_PerCallWithholdDeniesOnlyTheNamedCategory()
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Full), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, WorkItemSessions())
            .InvokeAsync(["work-item", "42", "--withhold", "comments", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        JsonElement result = FirstResult(writer.ToString());
        Assert.Equal("<p>Fabricated description</p>", result.GetProperty("description").GetProperty("raw").GetString());
        Assert.Null(result.GetProperty("comments")[0].GetProperty("rawText").GetString());
        Assert.True(FirstPrivacy(writer.ToString()).GetProperty("restrictedByCall").GetBoolean());
    }

    [Theory]
    [InlineData("--privacy", "everything", "invalidPrivacyLevel")]
    [InlineData("--withhold", "passwords", "invalidPrivacyCategory")]
    public async Task Read_RejectsAnUnknownPerCallPrivacyValue(string option, string value, string code)
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Full), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, WorkItemSessions())
            .InvokeAsync(["work-item", "42", option, value, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal(code, FirstError(writer.ToString()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task WorkItem_WithheldImagesAreNeverFetchedAndNothingIsWritten()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-withheld-{Guid.NewGuid():N}");
        var source = new FixtureWorkItemSource();
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Standard), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, new FixtureSessionFactory(new FixtureSession(source)))
            .InvokeAsync(["work-item", "42", "--download-images", outputDirectory, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("2.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.False(document.RootElement.GetProperty("query").TryGetProperty("downloadImagesDirectory", out _));
        Assert.False(document.RootElement.GetProperty("results")[0].TryGetProperty("media", out _));
        Assert.Contains("images", Strings(FirstPrivacy(writer.ToString()).GetProperty("withheld")));
        Assert.Equal(0, source.AttachmentCallCount);
        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public async Task Teams_MinimalWithholdsTheDescription()
    {
        var writer = new StringWriter();

        int exitCode = await App(Seeded(PrivacyLevel.Minimal), new ScriptedTerminalInput(isInputRedirected: true, answer: null), writer, new FixtureSessionFactory(new FixtureSession(new FixtureWorkItemSource())))
            .InvokeAsync(["teams", "--project", "Fabricated.Alpha", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        JsonElement team = FirstResult(writer.ToString());
        Assert.Equal("Fabricated.Alpha Team", team.GetProperty("name").GetString());
        Assert.Null(team.GetProperty("description").GetString());
        Assert.Equal(["text"], Strings(FirstPrivacy(writer.ToString()).GetProperty("withheld")));
    }

    private static CommandApplication App(
        ICredentialVault vault,
        ITerminalInput terminal,
        TextWriter writer,
        IAdoSessionFactory? sessionFactory = null) =>
        new ToolComposition(
            new FabricatedStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            new StaticPat(),
            sessionFactory ?? new FixtureSessionFactory(new FixtureSession(new FixtureWorkItemSource())),
            vault,
            terminal)
            .CreateUmbrella("ado-read", AllModules);

    private static FixtureSessionFactory WorkItemSessions() =>
        new(new FixtureSession(new FixtureWorkItemSource()));

    private static MemoryCredentialVault Seeded(PrivacyLevel level, byte[]? key = null)
    {
        var vault = new MemoryCredentialVault();
        vault.Write(
            CredentialAccount.ForPrivacy(Organization),
            new StoredPrivacyPolicy(new PrivacyPolicy(level), key ?? TestPrivacy.Key).ToJson());
        return vault;
    }

    private static StoredPrivacyPolicy StoredPolicy(MemoryCredentialVault vault) =>
        StoredPrivacyPolicy.Parse(vault.Read(CredentialAccount.ForPrivacy(Organization))!);

    private static JsonElement FirstError(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("errors")[0].Clone();
    }

    private static JsonElement FirstResult(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("results")[0].Clone();
    }

    private static JsonElement FirstPrivacy(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("privacy").Clone();
    }

    private static string PrivacyLevelOf(string output) => FirstPrivacy(output).GetProperty("level").GetString()!;

    private static string FirstEnvelopeKind(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("kind").GetString()!;
    }

    private static string FirstEnvelopeStatus(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("status").GetString()!;
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToArray();
}
