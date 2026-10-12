// Hand-written minimal equivalent of Git Credential Manager's macOS keychain store (https://github.com/git-ecosystem/git-credential-manager), modelled on src/Core/Interop/MacOS/MacOSKeychain.cs at commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// GCM mixes the legacy SecKeychain and SecItem APIs. This vault uses only SecItem for one generic password per account.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AdoRead.Credentials.Native;
using static AdoRead.Credentials.Native.CoreFoundation;
using static AdoRead.Credentials.Native.SecurityFramework;

namespace AdoRead.Credentials;

internal sealed class MacOSKeychainVault : ICredentialVault
{
    public string Kind => "macosKeychain";

    public string? Read(string account)
    {
        using var objects = new CoreFoundationObjects();
        IntPtr query = CreateItemQuery(objects, account);
        CFDictionaryAddValue(query, kSecMatchLimit, kSecMatchLimitOne);
        CFDictionaryAddValue(query, kSecReturnData, kCFBooleanTrue);

        int status = SecItemCopyMatching(query, out IntPtr result);
        objects.Track(result);
        if (status == errSecItemNotFound)
        {
            return null;
        }

        ThrowIfFailed("read", status);
        return SecretFromData(result);
    }

    public void Write(string account, string secret)
    {
        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            using var objects = new CoreFoundationObjects();
            IntPtr query = CreateItemQuery(objects, account);
            IntPtr data = objects.Track(CFDataCreate(IntPtr.Zero, secretBytes, secretBytes.Length));

            IntPtr update = objects.Track(CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero));
            CFDictionaryAddValue(update, kSecValueData, data);
            int status = SecItemUpdate(query, update);
            if (status == errSecItemNotFound)
            {
                CFDictionaryAddValue(query, kSecValueData, data);
                status = SecItemAdd(query, IntPtr.Zero);
            }

            ThrowIfFailed("write", status);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public bool Remove(string account)
    {
        using var objects = new CoreFoundationObjects();
        int status = SecItemDelete(CreateItemQuery(objects, account));
        if (status == errSecItemNotFound)
        {
            return false;
        }

        ThrowIfFailed("remove", status);
        return true;
    }

    private static IntPtr CreateItemQuery(CoreFoundationObjects objects, string account)
    {
        IntPtr query = objects.Track(CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero));
        CFDictionaryAddValue(query, kSecClass, kSecClassGenericPassword);
        CFDictionaryAddValue(query, kSecAttrService, objects.Track(CreateCFString(CredentialVault.ServiceName)));
        CFDictionaryAddValue(query, kSecAttrAccount, objects.Track(CreateCFString(account)));
        return query;
    }

    private static IntPtr CreateCFString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, CFStringEncoding.kCFStringEncodingUTF8, false);
    }

    private static string SecretFromData(IntPtr cfData)
    {
        long length = CFDataGetLength(cfData);
        byte[] bytes = new byte[length];
        Marshal.Copy(CFDataGetBytePtr(cfData), bytes, 0, bytes.Length);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ThrowIfFailed(string operation, int status)
    {
        if (status != errSecSuccess)
        {
            throw CredentialStoreErrors.Failed("macOS Keychain", operation, $"Security framework status {status}.");
        }
    }

    /// <summary>
    /// Releases the CoreFoundation objects created for one call. The query dictionary does not retain
    /// its keys or values, so they must stay alive until the Security call returns.
    /// </summary>
    private sealed class CoreFoundationObjects : IDisposable
    {
        private readonly List<IntPtr> _objects = [];

        public IntPtr Track(IntPtr cfObject)
        {
            if (cfObject != IntPtr.Zero)
            {
                _objects.Add(cfObject);
            }

            return cfObject;
        }

        public void Dispose()
        {
            foreach (IntPtr cfObject in _objects)
            {
                CFRelease(cfObject);
            }
        }
    }
}
