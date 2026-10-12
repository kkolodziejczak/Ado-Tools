using System.Diagnostics;

namespace AdoRead.IntegrationTests.Cli;

public sealed class HelpCommandTests
{
    [Fact]
    public async Task Help_UsesConventionalHumanReadableOutput()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Cli.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--help");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start ado-read process.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(await standardError));
        string output = await standardOutput;
        Assert.Contains("Read Azure DevOps context as structured JSON.", output, StringComparison.Ordinal);
        Assert.Contains("work-item", output, StringComparison.Ordinal);
        Assert.Contains("pr", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schemaVersion\"", output, StringComparison.Ordinal);
    }
}
