using AdoRead.Configuration;

namespace AdoRead.Credentials;

/// <summary>Builds the credential failures that every vault reports, so codes and wording stay identical.</summary>
internal static class CredentialStoreErrors
{
    public static CredentialException Unavailable(string vaultName, string reason) =>
        new(
            "credentialStoreUnavailable",
            $"The {vaultName} is not available: {reason} The token is never stored in a file or environment variable.");

    public static CredentialException Failed(string vaultName, string operation, string reason) =>
        new(
            "credentialStoreFailed",
            $"The {vaultName} could not {operation} the token: {reason}");
}
