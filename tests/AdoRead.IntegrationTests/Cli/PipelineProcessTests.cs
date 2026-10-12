using System.Diagnostics;
using System.Text.Json;

namespace AdoRead.IntegrationTests.Cli;

public sealed class PipelineProcessTests
{
    [Theory]
    [InlineData("full", "pipelines-pr-full.json")]
    [InlineData("standard", "pipelines-pr-standard.json")]
    [InlineData("minimal", "pipelines-pr-minimal.json")]
    public async Task PipelinePullRequest_FakeProcessOutputMatchesFabricatedSnapshot(string privacyLevel, string snapshotName)
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(
            toolRoot,
            FindFakeHostAssembly(toolRoot),
            "pipelines",
            "pr",
            "126",
            "--project",
            "Fabricated.Alpha",
            "--privacy",
            privacyLevel,
            "--compact");

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        using JsonDocument actual = JsonDocument.Parse(result.StandardOutput);
        string snapshotPath = Path.Combine(toolRoot, "tests", "AdoRead.IntegrationTests", "Fixtures", snapshotName);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), result.StandardOutput);
    }

    [Fact]
    public async Task PipelinePullRequest_FakeProcessNeverEmitsTheRawSecretsInTheFixture()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(
            toolRoot,
            FindFakeHostAssembly(toolRoot),
            "pipelines",
            "pr",
            "126",
            "--project",
            "Fabricated.Alpha",
            "--compact");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("hunter2", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", result.StandardOutput, StringComparison.Ordinal);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.True(output.RootElement.GetProperty("results")[0].GetProperty("runs")[0].GetProperty("maskedSecretCount").GetInt32() > 0);
    }

    [Fact]
    public async Task PipelineRun_PartialBuildExitsFourWithAnError()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(
            toolRoot,
            FindFakeHostAssembly(toolRoot),
            "pipelines",
            "run",
            "8991",
            "--project",
            "Fabricated.Alpha",
            "--compact");

        Assert.Equal(4, result.ExitCode);
        string snapshotPath = Path.Combine(toolRoot, "tests", "AdoRead.IntegrationTests", "Fixtures", "pipelines-run-partial.json");
        using JsonDocument actual = JsonDocument.Parse(result.StandardOutput);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), result.StandardOutput);
        Assert.Equal("forbidden", actual.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task PipelineTests_FailedOutcomeMatchesFabricatedSnapshot()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(
            toolRoot,
            FindFakeHostAssembly(toolRoot),
            "pipelines",
            "tests",
            "9002",
            "--project",
            "Fabricated.Alpha",
            "--compact");

        Assert.Equal(0, result.ExitCode);
        string snapshotPath = Path.Combine(toolRoot, "tests", "AdoRead.IntegrationTests", "Fixtures", "pipelines-tests-failed.json");
        using JsonDocument actual = JsonDocument.Parse(result.StandardOutput);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), result.StandardOutput);
        Assert.Equal(2, actual.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task PipelineRun_UnknownBuildExitsThreeAsNotFound()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "pipelines", "run", "404", "--compact");

        Assert.Equal(3, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("notFound", output.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PipelinePullRequest_ProductionSliceReturnsV1ForInvalidArguments()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Pipelines.Cli.dll");

        ProcessResult result = await RunAsync(toolRoot, assemblyPath, "pr", "--compact");

        Assert.Equal(2, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("pipelinePullRequest", output.RootElement.GetProperty("kind").GetString());
        Assert.Equal("invalidArguments", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task PipelinePullRequest_ProductionSliceRequiresPrivacyPolicyBeforeTokenLookup()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Pipelines.Cli.dll");
        string directory = Path.Combine(Path.GetTempPath(), $"ado-read-pipelines-{Guid.NewGuid():N}");
        string configPath = Path.Combine(directory, "config.json");
        try
        {
            ProcessResult initialized = await RunAsync(
                toolRoot,
                assemblyPath,
                "config",
                "init",
                "--organization",
                "https://dev.azure.com/fabricated",
                "--config",
                configPath,
                "--compact");
            Assert.Equal(0, initialized.ExitCode);

            ProcessResult result = await RunAsync(
                toolRoot,
                assemblyPath,
                "pr",
                "126",
                "--project",
                "Fabricated.Alpha",
                "--config",
                configPath,
                "--compact");

            Assert.Equal(2, result.ExitCode);
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("privacy", output.RootElement.GetProperty("errors")[0].GetProperty("scope").GetString());
            Assert.Equal("privacyPolicyRequired", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string toolRoot,
        string assemblyPath,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = toolRoot
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the pipeline tool process.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string FindFakeHostAssembly(string toolRoot)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine the test build configuration.");
        return Path.Combine(toolRoot, "tests", "AdoRead.FakeHost", "bin", configuration, "net10.0", "AdoRead.FakeHost.dll");
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
