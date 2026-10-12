namespace AdoRead.Credentials;

public static class CredentialVault
{
    /// <summary>Service name under which every ado-read token is stored in the vault.</summary>
    public const string ServiceName = "ado-read";

    /// <summary>
    /// Chooses the vault of the running operating system. An unsupported system gets a vault that
    /// fails with <c>credentialStoreUnavailable</c> when used, so commands that do not touch the
    /// vault, such as help and configuration, keep working.
    /// </summary>
    public static ICredentialVault ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialVault();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSKeychainVault();
        }

        if (OperatingSystem.IsLinux())
        {
            return new SecretServiceVault();
        }

        return new UnsupportedCredentialVault();
    }
}
