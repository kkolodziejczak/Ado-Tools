namespace AdoRead.Credentials;

/// <summary>
/// The operating system's per-user credential vault. It holds one generic secret per account and
/// is the only place a personal access token may live.
/// </summary>
public interface ICredentialVault
{
    /// <summary>Stable vault name shown in command output, such as "windowsCredentialManager".</summary>
    string Kind { get; }

    string? Read(string account);

    void Write(string account, string secret);

    bool Remove(string account);
}
