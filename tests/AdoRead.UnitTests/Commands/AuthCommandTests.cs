using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Terminal;
using AdoRead.UnitTests.Credentials;
using AdoRead.UnitTests.Terminal;

namespace AdoRead.UnitTests.Commands;

public sealed class AuthCommandTests
{
    private const string Token = "fabricated-token-value-1234";
    private const string Organization = "https://dev.azure.com/fabricated";

    public static TheoryData<string[]> TokenArguments => new()
    {
        new[] { "auth", "login", "--token", Token },
        new[] { "auth", "login", "--pat", Token },
        new[] { "auth", "login", "--personal-access-token", Token },
        new[] { "auth", "status", "--token", Token },
        new[] { "auth", "logout", "--pat", Token },
    };

    [Fact]
    public async Task Login_StoresTypedTokenForTheConfiguredOrganizationWithoutEchoingIt()
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: Token, "2");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(Token, vault.Read(Organization));
        Assert.Equal(
            "Personal access token for https://dev.azure.com/fabricated: ",
            Assert.Single(terminal.Prompts));
        string output = writer.ToString();
        Assert.DoesNotContain(Token, output, StringComparison.Ordinal);
        JsonElement envelope = JsonDocument.Parse(output).RootElement;
        Assert.Equal("authLogin", envelope.GetProperty("kind").GetString());
        Assert.Equal("success", envelope.GetProperty("status").GetString());
        Assert.Equal("https://dev.azure.com/fabricated/", envelope.GetProperty("query").GetProperty("organizationUrl").GetString());
        JsonElement result = FirstResult(output);
        Assert.Equal("memory", result.GetProperty("vault").GetString());
        Assert.True(result.GetProperty("tokenStored").GetBoolean());
    }

    [Fact]
    public async Task Login_UsesTheOrganizationOptionAndNormalizesItsAccount()
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: Token, "2");

        int exitCode = await App(vault, terminal, new StringWriter()).InvokeAsync(
            ["auth", "login", "--organization", "https://dev.azure.com/Fabricated/", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(Token, vault.Read(Organization));
    }

    [Fact]
    public async Task Login_RefusesRedirectedInputBeforePromptingOrStoring()
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: true, answer: Token, "2");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Empty(terminal.Prompts);
        Assert.Equal("interactiveTerminalRequired", FirstError(writer.ToString()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_RejectsABlankTokenWithoutStoringAnything()
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: "   ");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("tokenRequired", FirstError(writer.ToString()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_ReportsAnUnavailableVaultAsAnAuthenticationError()
    {
        var vault = new MemoryCredentialVault(available: false);
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: Token, "2");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync(["auth", "login", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        JsonElement error = FirstError(writer.ToString());
        Assert.Equal("authentication", error.GetProperty("scope").GetString());
        Assert.Equal("credentialStoreUnavailable", error.GetProperty("code").GetString());
        Assert.DoesNotContain(Token, writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ReportsAStoredTokenAndTheVaultWithoutRevealingIt()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(Organization, Token);
        var writer = new StringWriter();

        int exitCode = await App(vault, new ScriptedTerminalInput(isInputRedirected: false, answer: null), writer)
            .InvokeAsync(["auth", "status", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        string output = writer.ToString();
        Assert.DoesNotContain(Token, output, StringComparison.Ordinal);
        JsonElement result = FirstResult(output);
        Assert.True(result.GetProperty("tokenStored").GetBoolean());
        Assert.Equal("memory", result.GetProperty("vault").GetString());
    }

    [Fact]
    public async Task Status_ReportsAMissingTokenAsNotStored()
    {
        var writer = new StringWriter();

        int exitCode = await App(new MemoryCredentialVault(), new ScriptedTerminalInput(isInputRedirected: false, answer: null), writer)
            .InvokeAsync(["auth", "status", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.False(FirstResult(writer.ToString()).GetProperty("tokenStored").GetBoolean());
    }

    [Fact]
    public async Task Logout_RemovesTheStoredTokenAndReportsWhetherOneWasRemoved()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(Organization, Token);
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: null);

        var firstWriter = new StringWriter();
        int firstExit = await App(vault, terminal, firstWriter).InvokeAsync(["auth", "logout", "--compact"], CancellationToken.None);
        var secondWriter = new StringWriter();
        int secondExit = await App(vault, terminal, secondWriter).InvokeAsync(["auth", "logout", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, firstExit);
        Assert.True(FirstResult(firstWriter.ToString()).GetProperty("tokenRemoved").GetBoolean());
        Assert.Equal(ExitCodes.Success, secondExit);
        Assert.False(FirstResult(secondWriter.ToString()).GetProperty("tokenRemoved").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(TokenArguments))]
    public async Task NoAuthCommandAcceptsATokenValueAsAnArgument(string[] arguments)
    {
        var vault = new MemoryCredentialVault();
        var terminal = new ScriptedTerminalInput(isInputRedirected: false, answer: Token, "2");
        var writer = new StringWriter();

        int exitCode = await App(vault, terminal, writer).InvokeAsync([.. arguments, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidArguments", FirstError(writer.ToString()).GetProperty("code").GetString());
        Assert.Empty(terminal.Prompts);
        Assert.DoesNotContain(Token, writer.ToString(), StringComparison.Ordinal);
    }

    private static CommandApplication App(MemoryCredentialVault vault, ITerminalInput terminal, TextWriter writer) =>
        new ToolComposition(
            new ConfiguredStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            patProvider: null,
            sessionFactory: null,
            credentialVault: vault,
            terminalInput: terminal)
            .CreateUmbrella("ado-read", []);

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

    private sealed class ConfiguredStore : IConfigurationStore
    {
        public string DefaultPath => "configured";

        public Task<AdoReadConfiguration> LoadAsync(string? path, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(new Uri(Organization + "/")));

        public Task SaveAsync(string? path, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
