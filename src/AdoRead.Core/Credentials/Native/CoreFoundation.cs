// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/MacOS/Native/CoreFoundation.cs. Reduced to the dictionary, string, data and
// memory calls that the Keychain query needs.

using System.Runtime.InteropServices;

namespace AdoRead.Credentials.Native;

internal static class CoreFoundation
{
    private const string CoreFoundationFrameworkLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public static readonly IntPtr kCFBooleanTrue;

    static CoreFoundation()
    {
        IntPtr handle = LibSystem.dlopen(CoreFoundationFrameworkLib, 0);
        kCFBooleanTrue = LibSystem.GetGlobal(handle, "kCFBooleanTrue");
    }

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, long capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void CFDictionaryAddValue(IntPtr theDict, IntPtr key, IntPtr value);

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CFStringCreateWithBytes(
        IntPtr alloc,
        byte[] bytes,
        long numBytes,
        CFStringEncoding encoding,
        [MarshalAs(UnmanagedType.U1)] bool isExternalRepresentation);

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, long length);

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern long CFDataGetLength(IntPtr theData);

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CFDataGetBytePtr(IntPtr theData);

    [DllImport(CoreFoundationFrameworkLib, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void CFRelease(IntPtr cf);
}

internal enum CFStringEncoding
{
    kCFStringEncodingUTF8 = 0x08000100,
}
