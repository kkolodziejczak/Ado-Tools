// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/Windows/WindowsCredentialManager.cs. Reduced to one generic credential per
// account: no enumeration, namespace scoping, legacy target matching or URI userinfo handling.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AdoRead.Configuration;
using AdoRead.Credentials.Native;

namespace AdoRead.Credentials;

internal sealed class WindowsCredentialVault : ICredentialVault
{
    private const int ErrorNotFound = 1168;

    public string Kind => "windowsCredentialManager";

    public string? Read(string account)
    {
        if (!Advapi32.CredRead(TargetName(account), CredentialType.Generic, 0, out IntPtr credentialPtr))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw Failed("read", error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Win32Credential>(credentialPtr);
            return SecretFromBlob(credential.CredentialBlob, credential.CredentialBlobSize);
        }
        finally
        {
            Advapi32.CredFree(credentialPtr);
        }
    }

    public void Write(string account, string secret)
    {
        byte[] secretBytes = Encoding.Unicode.GetBytes(secret);
        IntPtr blob = Marshal.AllocHGlobal(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blob, secretBytes.Length);
            var credential = new Win32Credential
            {
                Type = CredentialType.Generic,
                TargetName = TargetName(account),
                UserName = account,
                CredentialBlobSize = secretBytes.Length,
                CredentialBlob = blob,
                // LOCAL_MACHINE is per user on this machine. It does not roam with the domain profile.
                Persist = CredentialPersist.LocalMachine
            };

            if (!Advapi32.CredWrite(ref credential, 0))
            {
                throw Failed("write", Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            ZeroAndFree(blob, secretBytes.Length);
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public bool Remove(string account)
    {
        if (Advapi32.CredDelete(TargetName(account), CredentialType.Generic, 0))
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw Failed("remove", error);
    }

    private static string TargetName(string account) => $"{CredentialVault.ServiceName}:{account}";

    private static string SecretFromBlob(IntPtr blob, int size)
    {
        if (size == 0 || blob == IntPtr.Zero)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[size];
        Marshal.Copy(blob, bytes, 0, size);
        try
        {
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ZeroAndFree(IntPtr blob, int size)
    {
        Marshal.Copy(new byte[size], 0, blob, size);
        Marshal.FreeHGlobal(blob);
    }

    private static CredentialException Failed(string operation, int error) =>
        CredentialStoreErrors.Failed("Windows Credential Manager", operation, $"Windows error {error}.");
}
