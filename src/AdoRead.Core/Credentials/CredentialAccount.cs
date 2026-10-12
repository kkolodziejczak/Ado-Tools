namespace AdoRead.Credentials;

/// <summary>Maps an organization to the vault account that holds its token.</summary>
public static class CredentialAccount
{
    /// <summary>The normalized organization URL, for example <c>https://dev.azure.com/fabricated</c>.</summary>
    public static string For(Uri organizationUrl) =>
        organizationUrl.AbsoluteUri.TrimEnd('/').ToLowerInvariant();
}
