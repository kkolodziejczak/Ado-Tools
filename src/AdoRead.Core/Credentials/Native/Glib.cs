// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/Linux/Native/Glib.cs and Gobject.cs. Reduced to the hash table, list and error
// calls that a Secret Service lookup needs. Hash and destroy callbacks are passed as native function
// pointers, so no managed delegates have to stay alive for the native side.

using System.Runtime.InteropServices;

namespace AdoRead.Credentials.Native;

internal static class Glib
{
    private const string LibraryName = "libglib-2.0.so.0";

    [StructLayout(LayoutKind.Sequential)]
    public struct GList
    {
        public IntPtr data;
        public IntPtr next;
        public IntPtr prev;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GError
    {
        public int domain;
        public int code;
        public IntPtr message;
    }

    /// <summary>Address of a function exported by GLib, for passing as a callback.</summary>
    public static IntPtr Export(string symbol) =>
        NativeLibrary.GetExport(NativeLibrary.Load(LibraryName), symbol);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr g_hash_table_new_full(IntPtr hashFunc, IntPtr keyEqualFunc, IntPtr keyDestroyFunc, IntPtr valueDestroyFunc);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool g_hash_table_insert(IntPtr hashTable, IntPtr key, IntPtr value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_hash_table_destroy(IntPtr hashTable);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_list_free_full(IntPtr list, IntPtr freeFunc);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_error_free(IntPtr error);
}

internal static class Gobject
{
    private const string LibraryName = "libgobject-2.0.so.0";

    /// <summary>Address of <c>g_object_unref</c>, for releasing list items.</summary>
    public static IntPtr UnrefFunction => NativeLibrary.GetExport(NativeLibrary.Load(LibraryName), "g_object_unref");

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_object_unref(IntPtr @object);
}
