using System.Runtime.InteropServices;

namespace AdoWrite.Approval;

/// <summary>
/// The machine-wide switch that turns approval off. Only an administrator can change it: on Windows
/// the machine-level environment variable, read from the machine scope so a value set for one process
/// or for the user is ignored; elsewhere a file under /etc, which counts only while neither it nor its
/// folder can be written by the user running the tool.
/// </summary>
public static partial class MachineApprovalSetting
{
    public const string VariableName = "ADO_WRITE_APPROVAL";
    public const string UnixFilePath = "/etc/ado-tools/approval";

    public static bool IsOff(string? value) => string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    public static string? Read() =>
        OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable(VariableName, EnvironmentVariableTarget.Machine)
            : ReadUnixFile(UnixFilePath, UserCanWrite);

    /// <summary>
    /// The file's text, or null when it is missing, unreadable, or the user could change it: then an
    /// agent running as that user could have written it, so approval stays on.
    /// </summary>
    public static string? ReadUnixFile(string path, Func<string, bool> userCanWrite)
    {
        try
        {
            return File.Exists(path) && !userCanWrite(path) && !userCanWrite(Path.GetDirectoryName(path)!)
                ? File.ReadAllText(path)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const int WriteAccess = 2;

    /// <summary>access(2) with W_OK: whether this process may write the file or folder. When it can't be asked, assume it may.</summary>
    private static bool UserCanWrite(string path)
    {
        try
        {
            return Access(path, WriteAccess) == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    [DllImport("libc", EntryPoint = "access")]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
}
