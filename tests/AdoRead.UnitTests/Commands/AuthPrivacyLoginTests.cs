using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Terminal;
using AdoRead.UnitTests.Credentials;
using AdoRead.UnitTests.Privacy;
using AdoRead.UnitTests.Terminal;

namespace AdoRead.UnitTests.Commands;

/// <summary>Login asks for the privacy level of an organization that has none, and logout never removes a policy.</summary>
public sealed class AuthPrivacyLoginTests
{
    private const string Token = "fabricated-token-value-1234";

    private static readonly Uri Organization = new("https://dev.azure.com/fabricated/");

    [Fact]
    public async Task Login_WhenNoPolicyExists_AsksForALevelAndStoresTheChoice()
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: Token, "3");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(PrivacyLevel.Full, StoredPrivacyPolicy.Parse(vault.Read(CredentialAccount.ForPrivacy(Organization))!).Policy.Level);
        string prompt = Assert.Single(terminal.LinePrompts);
        Assert.Contains("1 minimal", prompt, StringComparison.Ordinal);
        Assert.Contains("3 full", prompt, StringComparison.Ordinal);
        JsonElement result = FirstResult(writer.ToString());
        Assert.True(result.GetProperty("privacyConfigured").GetBoolean());
        Assert.False(result.TryGetProperty("notice", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("4")]
    [InlineData("full")]
    public async Task Login_AnEmptyOrInvalidLevelStoresNoPolicy(string answer)
    {
        var vault = new MemoryCredentialVault();
        var writer = new StringWriter();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: Token, answer), writer)
            .InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(Token, vault.Read(CredentialAccount.For(Organization)));
        Assert.Null(vault.Read(CredentialAccount.ForPrivacy(Organization)));
        JsonElement result = FirstResult(writer.ToString());
        Assert.False(result.GetProperty("privacyConfigured").GetBoolean());
        Assert.Contains("privacy set", result.GetProperty("notice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithAnExistingPolicyDoesNotAsk()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(
            CredentialAccount.ForPrivacy(Organization),
            new StoredPrivacyPolicy(new PrivacyPolicy(PrivacyLevel.Standard), TestPrivacy.Key).ToJson());
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: Token);

        int exitCode = await App(vault, terminal, new StringWriter()).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(terminal.LinePrompts);
        Assert.Equal(PrivacyLevel.Standard, StoredPrivacyPolicy.Parse(vault.Read(CredentialAccount.ForPrivacy(Organization))!).Policy.Level);
    }

    [Fact]
    public async Task Login_ReplacesAnUnreadablePolicyWhenThePersonAnswers()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(CredentialAccount.ForPrivacy(Organization), "{\"version\":7}");
        var writer = new StringWriter();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: Token, "1"), writer)
            .InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(PrivacyLevel.Minimal, StoredPrivacyPolicy.Parse(vault.Read(CredentialAccount.ForPrivacy(Organization))!).Policy.Level);
        Assert.True(FirstResult(writer.ToString()).GetProperty("privacyConfigured").GetBoolean());
    }

    [Fact]
    public async Task Logout_RemovesTheTokenAndKeepsThePolicy()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(CredentialAccount.For(Organization), Token);
        vault.Write(
            CredentialAccount.ForPrivacy(Organization),
            new StoredPrivacyPolicy(new PrivacyPolicy(PrivacyLevel.Full), TestPrivacy.Key).ToJson());

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: null), new StringWriter())
            .InvokeAsync(["auth", "logout", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Null(vault.Read(CredentialAccount.For(Organization)));
        Assert.NotNull(vault.Read(CredentialAccount.ForPrivacy(Organization)));
    }

    private static CommandApplication App(ICredentialVault vault, ITerminalInput terminal, TextWriter writer) =>
        new ToolComposition(
            new FabricatedStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            patProvider: null,
            sessionFactory: null,
            credentialVault: vault,
            terminalInput: terminal)
            .CreateUmbrella("ado-read", []);

    private static JsonElement FirstResult(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("results")[0].Clone();
    }
}
