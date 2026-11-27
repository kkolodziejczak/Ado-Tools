using System.Diagnostics;

namespace AdoWrite.Approval;

/// <summary>The window where the person approves one pending write.</summary>
public interface IApprovalWindow
{
    /// <summary>
    /// Opens a window for the write <paramref name="id"/> and waits until it has taken the write. False
    /// when no window took it in time: then the write is withdrawn and nothing can post it.
    /// </summary>
    bool Open(string id);
}

/// <summary>
/// Runs <c>approval open &lt;id&gt;</c> of the running tool in a terminal window of its own and waits
/// for that window to claim the write, so the agent is told the truth: a window is open, or the write
/// was not saved. Where no window can open, for example over SSH or on Linux without a desktop, the
/// write is withdrawn.
/// </summary>
/// <param name="startWindow">Starts a window for the write with this ID and says whether it started; by default this system's terminal.</param>
public sealed class ConsoleApprovalWindow(PendingWrites pending, TimeSpan? claimTimeout = null, Func<string, bool>? startWindow = null) : IApprovalWindow
{
    /// <summary>A cold start of Terminal on macOS can take several seconds.</summary>
    private static readonly TimeSpan DefaultClaimTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan ClaimPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<string, bool> _startWindow = startWindow ?? StartTerminal;

    public bool Open(string id)
    {
        bool started = _startWindow(id);
        DateTime deadline = DateTime.UtcNow + (claimTimeout ?? DefaultClaimTimeout);
        while (started && DateTime.UtcNow < deadline)
        {
            if (pending.IsClaimed(id))
            {
                return true;
            }

            Thread.Sleep(ClaimPollInterval);
        }

        // A window can claim it even when its launcher reported a failure, for example an osascript
        // that timed out after Terminal had started; then the write stays with that window.
        return !pending.Withdraw(id);
    }

    /// <summary>Tries this system's ways of opening a terminal that runs the window, until one starts.</summary>
    private static bool StartTerminal(string id) =>
        Tool(id) is { } tool && ApprovalWindowPlan.For(ApprovalWindowPlan.Current, tool, IsOnPath, Environment.GetEnvironmentVariable).Any(TryStart);

    /// <summary>This tool's own program, with <c>approval open &lt;id&gt;</c>; null when its path is unknown.</summary>
    private static string[]? Tool(string id)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return null;
        }

        // A tool run through the dotnet host (dotnet ado-write.dll) names its assembly first.
        return Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [executable, Environment.GetCommandLineArgs()[0], "approval", "open", id]
            : [executable, "approval", "open", id];
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

            if (!launch.WaitsForLauncher)
            {
                return true;
            }

            // A launcher that hands the window to another program says by its exit code whether it could.
            if (process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                return process.ExitCode == 0;
            }

            // Stopped, so it cannot open a window after the agent was told none opened.
            process.Kill(entireProcessTree: true);
            return false;
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

    /// <param name="tool">The tool's program and its arguments, ending with <c>approval open &lt;id&gt;</c>.</param>
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
