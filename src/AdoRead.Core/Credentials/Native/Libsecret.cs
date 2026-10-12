// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/Linux/Native/Libsecret.cs. Reduced to the synchronous Secret Service calls
// that store, search and clear one item. Pointers are IntPtr, so the project needs no unsafe code.

using System.Runtime.InteropServices;

namespace AdoRead.Credentials.Native;

internal static class Libsecret
{
    private const string LibraryName = "libsecret-1.so.0";

    public const int SECRET_SERVICE_OPEN_SESSION = 1 << 1;
    public const int SECRET_SERVICE_LOAD_COLLECTIONS = 1 << 2;
    public const int SECRET_SEARCH_ALL = 1 << 1;
    public const int SECRET_SEARCH_UNLOCK = 1 << 2;
    public const int SECRET_SCHEMA_DONT_MATCH_NAME = 1 << 1;
    public const int SECRET_SCHEMA_ATTRIBUTE_STRING = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SecretSchemaAttribute
    {
        public string? name;
        public int type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SecretSchema
    {
        public string? name;
        public int flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public SecretSchemaAttribute[] attributes;
        public int reserved;
        public IntPtr reserved1;
        public IntPtr reserved2;
        public IntPtr reserved3;
        public IntPtr reserved4;
        public IntPtr reserved5;
        public IntPtr reserved6;
        public IntPtr reserved7;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_service_get_sync(int flags, IntPtr cancellable, out IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_service_search_sync(
        IntPtr service,
        ref SecretSchema schema,
        IntPtr attributes,
        int flags,
        IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool secret_service_store_sync(
        IntPtr service,
        ref SecretSchema schema,
        IntPtr attributes,
        string? collection,
        string label,
        IntPtr value,
        IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool secret_service_clear_sync(
        IntPtr service,
        ref SecretSchema schema,
        IntPtr attributes,
        IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int secret_service_unlock_sync(
        IntPtr service,
        IntPtr objects,
        IntPtr cancellable,
        out IntPtr unlocked,
        out IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool secret_item_get_locked(IntPtr item);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool secret_item_load_secret_sync(IntPtr item, IntPtr cancellable, out IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_item_get_secret(IntPtr item);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_value_new(byte[] secret, nint length, string contentType);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_value_get(IntPtr value, out nint length);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void secret_value_unref(IntPtr value);
}
