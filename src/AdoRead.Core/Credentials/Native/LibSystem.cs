// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/MacOS/Native/LibSystem.cs.

using System.Runtime.InteropServices;

namespace AdoRead.Credentials.Native;

internal static class LibSystem
{
    private const string LibSystemLib = "/System/Library/Frameworks/System.framework/System";

    [DllImport(LibSystemLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr dlopen(string name, int flags);

    [DllImport(LibSystemLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr dlsym(IntPtr handle, string symbol);

    public static IntPtr GetGlobal(IntPtr handle, string symbol)
    {
        IntPtr ptr = dlsym(handle, symbol);
        return Marshal.PtrToStructure<IntPtr>(ptr);
    }
}
