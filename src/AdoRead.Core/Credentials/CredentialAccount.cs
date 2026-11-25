namespace AdoRead.Credentials;

/// <summary>Maps an organization to the vault accounts that hold its token and its privacy policy.</summary>
public static class CredentialAccount
{
    /// <summary>
    /// The account of the organization's token, which every read and write tool uses: the normalized
    /// organization URL, for example <c>https://dev.azure.com/fabricated</c>.
    /// </summary>
    public static string For(Uri organizationUrl) =>
        organizationUrl.AbsoluteUri.TrimEnd('/').ToLowerInvariant();

    /// <summary>
    /// The account that holds the privacy policy of an organization. It is separate from the token
    /// account so that logging out never removes the policy.
    /// </summary>
    public static string ForPrivacy(Uri organizationUrl) => $"{For(organizationUrl)}#privacy";

    /// <summary>
    /// Where prereleases before the shared token kept the write tools' token. Nothing reads it any more;
    /// login and logout remove it so that no write token outlives a logout.
    /// </summary>
    public static string ForLegacyWriteToken(Uri organizationUrl) => $"{For(organizationUrl)}#write";
}
