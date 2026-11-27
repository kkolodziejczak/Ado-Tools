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
/// Opens <c>approval review</c> of the running tool in a terminal window of its own. An open window
/// holds <c>window.lock</c> in the outbox, so a second one is not started. When no window can be
/// opened, for example on a Linux machine without a desktop, the person starts the command themselves.
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

        if (Environment.ProcessPath is not { } executable)
        {
            return ApprovalWindowState.NotOpened;
        }

        // A tool run through the dotnet host (dotnet ado-write.dll) names its assembly first.
        string[] tool = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [executable, Environment.GetCommandLineArgs()[0], "approval", "review"]
            : [executable, "approval", "review"];
        foreach (WindowLaunch launch in ApprovalWindowPlan.For(ApprovalWindowPlan.Current, tool, IsOnPath, Environment.GetEnvironmentVariable))
        {
            if (TryStart(launch))
            {
                return ApprovalWindowState.Opened;
            }
        }

        return ApprovalWindowState.NotOpened;
    }

    private static bool TryStart(WindowLaunch launch)
    {
        var start = new ProcessStartInfo(launch.File) { UseShellExecute = launch.ShellExecute };
        if (launch.ShellExecute)
        {
            // Shell execution takes one command line; no argument here contains a quote.
            start.Arguments = string.Join(' ', launch.Arguments.Select(argument => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument));
        }
        else
        {
            foreach (string argument in launch.Arguments)
            {
                start.ArgumentList.Add(argument);
            }
        }

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            // A launcher that hands the window to another program says by its exit code whether it could.
            return !launch.WaitsForLauncher || (process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, program)));

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

public enum ApprovalPlatform
{
    Windows,
    MacOS,
    Linux,
    Other
}

/// <summary>
/// One way to start the window: a program and its arguments. <see cref="WaitsForLauncher"/> marks a
/// program that asks another one to open the window and exits, such as <c>osascript</c>.
/// </summary>
public sealed record WindowLaunch(string File, IReadOnlyList<string> Arguments, bool ShellExecute = false, bool WaitsForLauncher = false);

/// <summary>
/// How each system opens a terminal window that runs the tool, tried in order until one starts. Pure,
/// so every system's plan is tested on any system.
/// </summary>
public static class ApprovalWindowPlan
{
    /// <summary>Linux terminals with the option that runs a command, most generic first.</summary>
    private static readonly (string Program, string RunOption)[] LinuxTerminals =
    [
        ("x-terminal-emulator", "-e"),
        ("gnome-terminal", "--"),
        ("konsole", "-e"),
        ("xfce4-terminal", "-x"),
        ("xterm", "-e"),
    ];

    /// <param name="tool">The tool's program and its arguments, ending with <c>approval review</c>.</param>
    public static IReadOnlyList<WindowLaunch> For(
        ApprovalPlatform platform,
        IReadOnlyList<string> tool,
        Func<string, bool> isOnPath,
        Func<string, string?> environment) => platform switch
    {
        // Shell execution gives a console program a new console window of its own.
        ApprovalPlatform.Windows => [new WindowLaunch(tool[0], [.. tool.Skip(1)], ShellExecute: true)],
        ApprovalPlatform.MacOS =>
        [
            new WindowLaunch(
                "osascript",
                ["-e", $"tell application \"Terminal\" to do script \"{AppleScriptText(ShellCommand(tool))}\"", "-e", "tell application \"Terminal\" to activate"],
                WaitsForLauncher: true)
        ],
        // Without a desktop session no window can open. setsid keeps the window alive when the agent's
        // command, and the process group it ran in, ends.
        ApprovalPlatform.Linux when environment("DISPLAY") is { Length: > 0 } || environment("WAYLAND_DISPLAY") is { Length: > 0 } =>
        [
            .. LinuxTerminals
                .Where(terminal => isOnPath(terminal.Program))
                .Select(terminal => isOnPath("setsid")
                    ? new WindowLaunch("setsid", [terminal.Program, terminal.RunOption, .. tool])
                    : new WindowLaunch(terminal.Program, [terminal.RunOption, .. tool]))
        ],
        _ => []
    };

    public static ApprovalPlatform Current =>
        OperatingSystem.IsWindows() ? ApprovalPlatform.Windows
        : OperatingSystem.IsMacOS() ? ApprovalPlatform.MacOS
        : OperatingSystem.IsLinux() ? ApprovalPlatform.Linux
        : ApprovalPlatform.Other;

    /// <summary>Each argument in single quotes, so a path with spaces or quotes stays one argument.</summary>
    private static string ShellCommand(IReadOnlyList<string> tool) =>
        string.Join(' ', tool.Select(argument => "'" + argument.Replace("'", "'\\''", StringComparison.Ordinal) + "'"));

    private static string AppleScriptText(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
