using System.Diagnostics;

namespace AdoWrite.Approval;

public enum ApprovalWindowState
{
    Opened,
    AlreadyOpen,
    NotOpened
}

/// <summary>The window where the person approves queued writes.</summary>
public interface IApprovalWindow
{
    /// <summary>Opens the window unless one is open already.</summary>
    ApprovalWindowState EnsureOpen();
}

/// <summary>
/// Opens <c>approval review</c> of the running tool in a console window of its own. An open window
/// holds <c>window.lock</c> in the outbox, so a second one is not started. Only Windows can open a
/// console window for the person; elsewhere they start the command in a terminal.
/// </summary>
public sealed class ConsoleApprovalWindow(string outboxDirectory) : IApprovalWindow
{
    public ApprovalWindowState EnsureOpen()
    {
        using (FileStream? probe = TryHold(outboxDirectory))
        {
            if (probe is null)
            {
                return ApprovalWindowState.AlreadyOpen;
            }
        }

        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } executable)
        {
            return ApprovalWindowState.NotOpened;
        }

        // A tool run through the dotnet host (dotnet ado-write.dll) names its assembly first.
        string arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{Environment.GetCommandLineArgs()[0]}\" approval review"
            : "approval review";
        try
        {
            // Shell execution gives a console program a new console window of its own.
            using Process? process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true });
            return process is null ? ApprovalWindowState.NotOpened : ApprovalWindowState.Opened;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ApprovalWindowState.NotOpened;
        }
    }

    /// <summary>The lock an open window holds; null when another process holds it.</summary>
    public static FileStream? TryHold(string outboxDirectory)
    {
        Directory.CreateDirectory(outboxDirectory);
        try
        {
            return new FileStream(Path.Combine(outboxDirectory, "window.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
