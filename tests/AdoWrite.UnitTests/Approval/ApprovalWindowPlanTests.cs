namespace AdoWrite.UnitTests.Approval;

public sealed class ApprovalWindowPlanTests
{
    private static readonly string[] Tool = ["/home/me/.dotnet/tools/ado-write", "approval", "open"];

    [Fact]
    public void Windows_StartsTheToolInAConsoleOfItsOwn()
    {
        WindowLaunch launch = Assert.Single(ApprovalWindowPlan.For(ApprovalPlatform.Windows, [@"C:\tools\ado-write.exe", "approval", "open"], _ => true, _ => null));

        Assert.Equal(@"C:\tools\ado-write.exe", launch.File);
        Assert.Equal(["approval", "open"], launch.Arguments);
        Assert.True(launch.ShellExecute);
    }

    [Fact]
    public void MacOS_RunsTheToolInTerminalWithEveryArgumentQuoted()
    {
        string[] tool = ["/Users/me/my tools/ado-write", "approval", "open"];

        WindowLaunch launch = Assert.Single(ApprovalWindowPlan.For(ApprovalPlatform.MacOS, tool, _ => true, _ => null));

        Assert.Equal("osascript", launch.File);
        Assert.True(launch.WaitsForLauncher);
        Assert.Equal(
            ["-e", "tell application \"Terminal\" to do script \"'/Users/me/my tools/ado-write' 'approval' 'open'\"", "-e", "tell application \"Terminal\" to activate"],
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

    [Theory]
    [InlineData(ApprovalPlatform.Windows, "SSH_CONNECTION")]
    [InlineData(ApprovalPlatform.MacOS, "SSH_TTY")]
    public void OverSsh_WindowsAndMacOSOpenNothing_BecauseNobodyWouldSeeTheWindow(ApprovalPlatform platform, string variable) =>
        Assert.Empty(ApprovalWindowPlan.For(platform, Tool, _ => true, name => name == variable ? "10.0.0.2 50000 10.0.0.1 22" : null));

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
