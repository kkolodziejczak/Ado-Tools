using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdoWrite.Approval;

/// <summary>
/// The machine-wide switch that turns approval off. Only an administrator can change it: on Windows
/// the machine-level environment variable, read from the machine scope so a value set for one process
/// or for the user is ignored; elsewhere a file under /etc, which counts only while it and its folder
/// belong to root and the user running the tool can't write either.
/// </summary>
public static partial class MachineApprovalSetting
{
    public const string VariableName = "ADO_WRITE_APPROVAL";
    public const string UnixFilePath = "/etc/ado-tools/approval";

    public static bool IsOff(string? value) => string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    public static string? Read() =>
        OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable(VariableName, EnvironmentVariableTarget.Machine)
            : ReadUnixFile(UnixFilePath, OnlyAnAdministratorCanChange);

    /// <summary>
    /// The file's text, or null when it is missing, unreadable, or it or its folder isn't
    /// <paramref name="onlyAnAdministratorCanChange"/>: then an agent running as the user could have
    /// written it, so approval stays on.
    /// </summary>
    public static string? ReadUnixFile(string path, Func<string, bool> onlyAnAdministratorCanChange)
    {
        try
        {
            return File.Exists(path) && onlyAnAdministratorCanChange(path) && onlyAnAdministratorCanChange(Path.GetDirectoryName(path)!)
                ? File.ReadAllText(path)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Owned by root, which alone can change its permissions, and not writable by this process. When
    /// either can't be checked, it counts as changeable.
    /// </summary>
    internal static bool OnlyAnAdministratorCanChange(string path) => OwnerId(path) == "0" && !UserCanWrite(path);

    /// <summary>The numeric owner from stat(1), whose options differ between macOS and Linux.</summary>
    private static string? OwnerId(string path)
    {
        var start = new ProcessStartInfo("stat") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        string[] arguments = OperatingSystem.IsMacOS() ? ["-f", "%u", path] : ["-c", "%u", path];
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? stat = Process.Start(start);
            if (stat is null)
            {
                return null;
            }

            string owner = stat.StandardOutput.ReadToEnd().Trim();
            if (!stat.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                stat.Kill();
                return null;
            }

            return stat.ExitCode == 0 ? owner : null;
        }
        catch (System.ComponentModel.Win32Exception)
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
