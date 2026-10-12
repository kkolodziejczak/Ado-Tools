namespace AdoRead.Credentials;

/// <summary>Maps an organization to the vault accounts that hold its token and its privacy policy.</summary>
public static class CredentialAccount
{
    /// <summary>The normalized organization URL, for example <c>https://dev.azure.com/fabricated</c>.</summary>
    public static string For(Uri organizationUrl) =>
        organizationUrl.AbsoluteUri.TrimEnd('/').ToLowerInvariant();

    /// <summary>
    /// The account that holds the privacy policy of an organization. It is separate from the token
    /// account so that logging out never removes the policy.
    /// </summary>
    public static string ForPrivacy(Uri organizationUrl) => $"{For(organizationUrl)}#privacy";
}
