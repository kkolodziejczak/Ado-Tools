using System.Diagnostics;
using System.Text.Json;

namespace AdoRead.IntegrationTests.Cli;

public sealed class ConfigCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ado-read-e2e-{Guid.NewGuid():N}");

    [Fact]
    public async Task ConfigInit_EmitsOneJsonDocumentAndNeverPersistsToken()
    {
        string configPath = Path.Combine(_directory, "custom", "config.json");

        ProcessResult result = await RunAsync(
            "config", "init",
            "--organization", "https://dev.azure.com/example",
            "--config", configPath,
            "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument stdout = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("success", stdout.RootElement.GetProperty("status").GetString());
        Assert.True(File.Exists(configPath));
        string persisted = await File.ReadAllTextAsync(configPath, CancellationToken.None);
        Assert.DoesNotContain("token", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigInit_RequiresForceToOverwrite()
    {
        string configPath = Path.Combine(_directory, "config.json");
        ProcessResult first = await RunAsync(
            "config", "init", "--organization", "https://dev.azure.com/original", "--config", configPath);
        ProcessResult second = await RunAsync(
            "config", "init", "--organization", "https://dev.azure.com/replacement", "--config", configPath);
        ProcessResult forced = await RunAsync(
            "config", "init", "--organization", "https://dev.azure.com/replacement", "--config", configPath, "--force");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(2, second.ExitCode);
        using JsonDocument failure = JsonDocument.Parse(second.StandardOutput);
        Assert.Equal("configurationExists", failure.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, forced.ExitCode);
    }

    [Fact]
    public async Task InvalidOrganization_EmitsStructuredJsonAndExitTwo()
    {
        string configPath = Path.Combine(_directory, "config.json");

        ProcessResult result = await RunAsync(
            "config", "init",
            "--organization", "http://dev.azure.com/example",
            "--config", configPath,
            "--compact");

        Assert.Equal(2, result.ExitCode);
        using JsonDocument stdout = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("error", stdout.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, stdout.RootElement.GetProperty("errors").GetArrayLength());
        Assert.False(File.Exists(configPath));
    }

    [Fact]
    public async Task ParserError_DoesNotEchoRejectedSecretToken()
    {
        const string canary = "parser-secret-canary";

        ProcessResult result = await RunAsync("config", "show", "--compact=true", canary);

        Assert.Equal(2, result.ExitCode);
        using JsonDocument stdout = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("invalidArguments", stdout.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.DoesNotContain(canary, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, result.StandardError, StringComparison.Ordinal);
        Assert.Single(result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        string toolRoot = FindToolRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = toolRoot
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AdoRead.Cli.dll"));
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ado-read.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource();
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string FindToolRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AdoRead.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AdoRead.slnx from the test output directory.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
