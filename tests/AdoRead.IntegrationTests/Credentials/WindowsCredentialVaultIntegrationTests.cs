using AdoRead.Credentials;

namespace AdoRead.IntegrationTests.Credentials;

public sealed class WindowsCredentialVaultIntegrationTests
{
    [WindowsFact]
    public void CredentialManager_RoundTripsATokenForAUniqueAccount()
    {
        ICredentialVault vault = CredentialVault.ForCurrentPlatform();
        string account = $"https://dev.azure.com/ado-read-test-{Guid.NewGuid():N}";
        const string firstToken = "fabricated-token-ünïcode-1234";
        const string secondToken = "fabricated-token-second-5678";

        try
        {
            Assert.Equal("windowsCredentialManager", vault.Kind);
            Assert.Null(vault.Read(account));

            vault.Write(account, firstToken);
            Assert.Equal(firstToken, vault.Read(account));

            vault.Write(account, secondToken);
            Assert.Equal(secondToken, vault.Read(account));

            Assert.True(vault.Remove(account));
            Assert.Null(vault.Read(account));
            Assert.False(vault.Remove(account));
        }
        finally
        {
            vault.Remove(account);
        }
    }
}
