using AdoRead.Configuration;
using AdoRead.Credentials;

namespace AdoRead.UnitTests.Credentials;

/// <summary>An in-memory vault. An unavailable one fails every call the way a missing Secret Service does.</summary>
internal sealed class MemoryCredentialVault(bool available = true) : ICredentialVault
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public string Kind => "memory";

    public string? Read(string account)
    {
        RequireAvailable();
        return _secrets.GetValueOrDefault(account);
    }

    public void Write(string account, string secret)
    {
        RequireAvailable();
        _secrets[account] = secret;
    }

    public bool Remove(string account)
    {
        RequireAvailable();
        return _secrets.Remove(account);
    }

    private void RequireAvailable()
    {
        if (!available)
        {
            throw new CredentialException("credentialStoreUnavailable", "The memory vault is unavailable.");
        }
    }
}
