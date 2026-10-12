using System.Diagnostics;
using System.Text.Json;

namespace AdoRead.IntegrationTests.Cli;

public sealed class WikiDiscoveryProcessTests
{
    [Fact]
    public async Task Wikis_FakeProcessMatchesFabricatedSnapshot()
    {
        using JsonDocument actual = await RunAsync("wikis", "--compact");
        await AssertSnapshotAsync("wikis.json", actual);
    }

    [Fact]
    public async Task WikiPages_FakeProcessUsesDefaultProjectAndOneContinuationStep()
    {
        using JsonDocument first = await RunAsync("wiki-pages", "--wiki", "FabricatedApp.wiki", "--compact");
        await AssertSnapshotAsync("wiki-pages.json", first);
        Assert.Equal("FabricatedApp", first.RootElement.GetProperty("query").GetProperty("project").GetString());
        Assert.Equal("52", first.RootElement.GetProperty("page").GetProperty("continuationToken").GetString());
        using JsonDocument second = await RunAsync("wiki-pages", "--wiki", "FabricatedApp.wiki", "--continuation-token", "52", "--compact");
        Assert.Equal(52, second.RootElement.GetProperty("results")[0].GetProperty("id").GetInt32());
        Assert.False(second.RootElement.GetProperty("page").TryGetProperty("continuationToken", out _));
    }

    private static async Task AssertSnapshotAsync(string name, JsonDocument actual)
    {
        string path = Path.Combine(FindRoot(), "tests", "AdoRead.IntegrationTests", "Fixtures", name);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), actual.RootElement.GetRawText());
    }

    private static async Task<JsonDocument> RunAsync(params string[] arguments)
    {
        string root = FindRoot(); string fake = Path.Combine(root, "tests", "AdoRead.FakeHost", "bin", "Release", "net10.0", "AdoRead.FakeHost.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
        start.ArgumentList.Add(fake); foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token); Assert.Equal(0, process.ExitCode); return JsonDocument.Parse(await process.StandardOutput.ReadToEndAsync());
    }

    private static string FindRoot() { DirectoryInfo? current = new(AppContext.BaseDirectory); while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "AdoRead.slnx"))) return current.FullName; current = current.Parent; } throw new DirectoryNotFoundException(); }
}
