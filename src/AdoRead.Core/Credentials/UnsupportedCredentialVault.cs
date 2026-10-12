using AdoRead.Configuration;

namespace AdoRead.Credentials;

internal sealed class UnsupportedCredentialVault : ICredentialVault
{
    public string Kind => "unsupported";

    public string? Read(string account) => throw Unavailable();

    public void Write(string account, string secret) => throw Unavailable();

    public bool Remove(string account) => throw Unavailable();

    private static CredentialException Unavailable() => new(
        "credentialStoreUnavailable",
        "No supported credential vault exists on this operating system. ado-read supports Windows Credential Manager, macOS Keychain and Linux Secret Service.");
}
