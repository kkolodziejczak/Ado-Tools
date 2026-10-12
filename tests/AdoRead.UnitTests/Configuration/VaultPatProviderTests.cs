using AdoRead.Configuration;
using AdoRead.UnitTests.Credentials;

namespace AdoRead.UnitTests.Configuration;

public sealed class VaultPatProviderTests
{
    private const string Organization = "https://dev.azure.com/fabricated";

    [Fact]
    public void GetRequiredPat_ReturnsTheTokenStoredForTheOrganization()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(Organization, "stored-token");
        var provider = new VaultPatProvider(vault, "ado-read auth login");

        string token = provider.GetRequiredPat(new Uri("https://dev.azure.com/Fabricated/"));

        Assert.Equal("stored-token", token);
    }

    [Fact]
    public void GetRequiredPat_WhenNoTokenIsStored_NamesTheLoginCommand()
    {
        var provider = new VaultPatProvider(new MemoryCredentialVault(), "ado-read auth login");

        CredentialException exception = Assert.Throws<CredentialException>(
            () => provider.GetRequiredPat(new Uri(Organization + "/")));

        Assert.Equal("missingPat", exception.Code);
        Assert.Equal(
            "No token is stored for https://dev.azure.com/fabricated. Run `ado-read auth login`.",
            exception.Message);
    }

    [Fact]
    public void GetRequiredPat_TreatsABlankStoredTokenAsMissing()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(Organization, "   ");
        var provider = new VaultPatProvider(vault, "ado-read-wiki auth login");

        CredentialException exception = Assert.Throws<CredentialException>(
            () => provider.GetRequiredPat(new Uri(Organization)));

        Assert.Equal("missingPat", exception.Code);
        Assert.Contains("Run `ado-read-wiki auth login`.", exception.Message, StringComparison.Ordinal);
    }
}
