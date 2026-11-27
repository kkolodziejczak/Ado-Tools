namespace AdoWrite.UnitTests.Approval;

public sealed class ApprovalWindowPlanTests
{
    private static readonly string[] Tool = ["/home/me/.dotnet/tools/ado-write", "approval", "review"];

    [Fact]
    public void Windows_StartsTheToolInAConsoleOfItsOwn()
    {
        WindowLaunch launch = Assert.Single(ApprovalWindowPlan.For(ApprovalPlatform.Windows, [@"C:\tools\ado-write.exe", "approval", "review"], _ => true, _ => null));

        Assert.Equal(@"C:\tools\ado-write.exe", launch.File);
        Assert.Equal(["approval", "review"], launch.Arguments);
        Assert.True(launch.ShellExecute);
    }

    [Fact]
    public void MacOS_RunsTheToolInTerminalWithEveryArgumentQuoted()
    {
        string[] tool = ["/Users/me/my tools/ado-write", "approval", "review"];

        WindowLaunch launch = Assert.Single(ApprovalWindowPlan.For(ApprovalPlatform.MacOS, tool, _ => true, _ => null));

        Assert.Equal("osascript", launch.File);
        Assert.True(launch.WaitsForLauncher);
        Assert.Equal(
            ["-e", "tell application \"Terminal\" to do script \"'/Users/me/my tools/ado-write' 'approval' 'review'\"", "-e", "tell application \"Terminal\" to activate"],
            launch.Arguments);
    }

    [Fact]
    public void Linux_TriesTheInstalledTerminalsInOrderInANewSession()
    {
        string[] installed = ["setsid", "gnome-terminal", "xterm"];

        IReadOnlyList<WindowLaunch> launches = ApprovalWindowPlan.For(ApprovalPlatform.Linux, Tool, installed.Contains, name => name == "DISPLAY" ? ":0" : null);

        Assert.Equal(["setsid", "setsid"], launches.Select(launch => launch.File));
        Assert.Equal(["gnome-terminal", "--", .. Tool], launches[0].Arguments);
        Assert.Equal(["xterm", "-e", .. Tool], launches[1].Arguments);
    }

    [Fact]
    public void Linux_WithoutADesktopSessionOpensNothing() =>
        Assert.Empty(ApprovalWindowPlan.For(ApprovalPlatform.Linux, Tool, _ => true, _ => null));

    [Fact]
    public void Linux_WithoutSetsidStartsTheTerminalDirectly()
    {
        WindowLaunch launch = Assert.Single(ApprovalWindowPlan.For(ApprovalPlatform.Linux, Tool, name => name == "konsole", name => name == "WAYLAND_DISPLAY" ? "wayland-0" : null));

        Assert.Equal("konsole", launch.File);
        Assert.Equal(["-e", .. Tool], launch.Arguments);
    }
}
