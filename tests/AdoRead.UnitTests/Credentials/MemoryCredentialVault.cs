using AdoRead.Configuration;
using AdoRead.Credentials;

namespace AdoRead.UnitTests.Credentials;

/// <summary>
/// An in-memory vault. An unavailable one fails every call the way a missing Secret Service does;
/// <see cref="FailingAccounts"/> fails only the calls for those accounts.
/// </summary>
internal sealed class MemoryCredentialVault(bool available = true) : ICredentialVault
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public string Kind => "memory";

    public HashSet<string> FailingAccounts { get; } = new(StringComparer.Ordinal);

    public string? Read(string account)
    {
        RequireAvailable(account);
        return _secrets.GetValueOrDefault(account);
    }

    public void Write(string account, string secret)
    {
        RequireAvailable(account);
        _secrets[account] = secret;
    }

    public bool Remove(string account)
    {
        RequireAvailable(account);
        return _secrets.Remove(account);
    }

    private void RequireAvailable(string account)
    {
        if (!available || FailingAccounts.Contains(account))
        {
            throw new CredentialException("credentialStoreUnavailable", "The memory vault is unavailable.");
        }
    }
}
