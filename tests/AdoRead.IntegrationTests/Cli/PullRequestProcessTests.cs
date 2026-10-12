using System.Diagnostics;
using System.Text.Json;

namespace AdoRead.IntegrationTests.Cli;

public sealed class PullRequestProcessTests
{
    [Fact]
    public async Task PullRequest_FakeProcessOutputMatchesFabricatedSnapshot()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "pr", "126", "--compact");

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        using JsonDocument actual = JsonDocument.Parse(result.StandardOutput);
        string snapshotPath = Path.Combine(
            toolRoot,
            "tests",
            "AdoRead.IntegrationTests",
            "Fixtures",
            "pull-request.json");
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), result.StandardOutput);
        Assert.Single(actual.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task PullRequest_FakeProcessEmitsTheLeanV2Contract()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "pr", "126", "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        JsonElement document = output.RootElement;
        JsonElement pullRequest = Assert.Single(document.GetProperty("results").EnumerateArray());
        JsonElement reviewer = Assert.Single(pullRequest.GetProperty("reviewers").EnumerateArray());
        JsonElement thread = Assert.Single(pullRequest.GetProperty("threads").EnumerateArray());
        JsonElement comment = thread.GetProperty("comments")[0];

        Assert.Equal("2.0", document.GetProperty("schemaVersion").GetString());
        Assert.False(pullRequest.TryGetProperty("commits", out _));
        Assert.False(pullRequest.TryGetProperty("iterations", out _));
        Assert.False(pullRequest.TryGetProperty("changes", out _));
        Assert.Equal("creator@example.invalid", pullRequest.GetProperty("creatorUniqueName").GetString());
        Assert.False(reviewer.TryGetProperty("identity", out _));
        Assert.False(thread.TryGetProperty("properties", out _));
        Assert.False(thread.TryGetProperty("identities", out _));
        Assert.False(thread.TryGetProperty("iterationContext", out _));
        Assert.Equal("fabricated-source-1", thread.GetProperty("reviewedCommitId").GetString());
        Assert.Equal("fabricated-source-2", thread.GetProperty("latestCommitId").GetString());
        Assert.True(thread.GetProperty("isPossiblyOutdated").GetBoolean());
        Assert.Equal("reviewer@example.invalid", comment.GetProperty("authorUniqueName").GetString());
    }

    [Fact]
    public async Task PullRequest_FakeProcessAcceptsBrowserUrl()
    {
        string toolRoot = FindToolRoot();

        ProcessResult result = await RunAsync(
            toolRoot,
            FindFakeHostAssembly(toolRoot),
            "pr",
            "https://dev.azure.com/fabricated/Fabricated.Alpha/_git/Fabricated.Alpha.Repository/pullrequest/126",
            "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(126, output.RootElement.GetProperty("query").GetProperty("id").GetInt32());
        Assert.Equal(126, output.RootElement.GetProperty("results")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task PullRequest_FakeProcessReturnsEveryMatchingProject()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "pr", "123", "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(2, output.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task PullRequest_ProductionProcessReturnsV2ForInvalidArguments()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Cli.dll");

        ProcessResult result = await RunAsync(toolRoot, assemblyPath, "pr", "--compact");

        Assert.Equal(2, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("2.0", output.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("pullRequestSearch", output.RootElement.GetProperty("kind").GetString());
        Assert.Equal("invalidArguments", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(404, 3, "notFound")]
    [InlineData(124, 4, "partial")]
    [InlineData(125, 5, "error")]
    public async Task PullRequest_FakeProcessMapsCompletenessToExitCode(
        int id,
        int expectedExitCode,
        string expectedStatus)
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "pr", id.ToString(), "--compact");

        Assert.Equal(expectedExitCode, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(expectedStatus, output.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PullRequest_ProductionProcessRequiresPrivacyPolicyBeforeTokenLookup()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Cli.dll");
        string directory = Path.Combine(Path.GetTempPath(), $"ado-read-pr-{Guid.NewGuid():N}");
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
                "123",
                "--config",
                configPath,
                "--compact");

            Assert.Equal(2, result.ExitCode);
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("2.0", output.RootElement.GetProperty("schemaVersion").GetString());
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
            ?? throw new InvalidOperationException("Could not start ado-read process.");
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
        return Path.Combine(
            toolRoot,
            "tests",
            "AdoRead.FakeHost",
            "bin",
            configuration,
            "net10.0",
            "AdoRead.FakeHost.dll");
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
