// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/Windows/Native/Advapi32.cs. Only the generic-credential read, write, delete
// and free calls are kept.

using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace AdoRead.Credentials.Native;

// https://learn.microsoft.com/windows/win32/api/wincred/
internal static class Advapi32
{
    private const string LibraryName = "advapi32.dll";

    [DllImport(LibraryName, EntryPoint = "CredReadW", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, CredentialType type, int reserved, out IntPtr credential);

    [DllImport(LibraryName, EntryPoint = "CredWriteW", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredWrite(ref Win32Credential credential, int flags);

    [DllImport(LibraryName, EntryPoint = "CredDeleteW", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredDelete(string target, CredentialType type, int flags);

    [DllImport(LibraryName, EntryPoint = "CredFree", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern void CredFree(IntPtr credential);
}

// Enum values from wincred.h
internal enum CredentialType
{
    Generic = 1,
}

internal enum CredentialPersist
{
    None = 0,
    Session = 1,
    LocalMachine = 2,
    Enterprise = 3,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct Win32Credential
{
    public int Flags;
    public CredentialType Type;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? TargetName;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? Comment;
    public FILETIME LastWritten;
    public int CredentialBlobSize;
    public IntPtr CredentialBlob;
    public CredentialPersist Persist;
    public int AttributeCount;
    public IntPtr Attributes;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? TargetAlias;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? UserName;
}
