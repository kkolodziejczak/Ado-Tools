using AdoRead.Configuration;
using AdoRead.Credentials;

namespace AdoRead.IntegrationTests;

/// <summary>Reads the token that <c>auth login</c> stored for the configured organization. Used only by the live smoke tests.</summary>
internal static class LiveTestCredentials
{
    public static string TokenFor(AdoReadConfiguration configuration) =>
        new VaultPatProvider(CredentialVault.ForCurrentPlatform(), "ado-read auth login")
            .GetRequiredPat(configuration.OrganizationUrl);
}
