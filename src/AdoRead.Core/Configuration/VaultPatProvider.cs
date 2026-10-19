using AdoRead.Credentials;

namespace AdoRead.Configuration;

/// <summary>Reads the token that <c>auth login</c> stored in the OS credential vault for an organization.</summary>
public sealed class VaultPatProvider(ICredentialVault vault, string loginCommand, TokenScope scope = TokenScope.Read) : IPatProvider
{
    public string GetRequiredPat(Uri organizationUrl)
    {
        string account = CredentialAccount.ForToken(organizationUrl, scope);
        string? token = vault.Read(account);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CredentialException(
                "missingPat",
                $"No token is stored for {account}. Run `{loginCommand}`.");
        }

        return token;
    }
}
