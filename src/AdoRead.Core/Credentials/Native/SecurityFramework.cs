// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/MacOS/Native/SecurityFramework.cs. Reduced to the SecItem calls and
// constants that a single generic password needs.

using System.Runtime.InteropServices;

namespace AdoRead.Credentials.Native;

internal static class SecurityFramework
{
    private const string SecurityFrameworkLib = "/System/Library/Frameworks/Security.framework/Security";

    public static readonly IntPtr Handle;
    public static readonly IntPtr kSecClass;
    public static readonly IntPtr kSecClassGenericPassword;
    public static readonly IntPtr kSecAttrService;
    public static readonly IntPtr kSecAttrAccount;
    public static readonly IntPtr kSecValueData;
    public static readonly IntPtr kSecReturnData;
    public static readonly IntPtr kSecMatchLimit;
    public static readonly IntPtr kSecMatchLimitOne;

    static SecurityFramework()
    {
        Handle = LibSystem.dlopen(SecurityFrameworkLib, 0);

        kSecClass = LibSystem.GetGlobal(Handle, "kSecClass");
        kSecClassGenericPassword = LibSystem.GetGlobal(Handle, "kSecClassGenericPassword");
        kSecAttrService = LibSystem.GetGlobal(Handle, "kSecAttrService");
        kSecAttrAccount = LibSystem.GetGlobal(Handle, "kSecAttrAccount");
        kSecValueData = LibSystem.GetGlobal(Handle, "kSecValueData");
        kSecReturnData = LibSystem.GetGlobal(Handle, "kSecReturnData");
        kSecMatchLimit = LibSystem.GetGlobal(Handle, "kSecMatchLimit");
        kSecMatchLimitOne = LibSystem.GetGlobal(Handle, "kSecMatchLimitOne");
    }

    public const int errSecSuccess = 0;
    public const int errSecDuplicateItem = -25299;
    public const int errSecItemNotFound = -25300;

    [DllImport(SecurityFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SecItemDelete(IntPtr query);
}
