using System.Diagnostics;
using System.Text.Json;

namespace AdoRead.IntegrationTests.Cli;

public sealed class WikiPageProcessTests
{
    [Fact]
    public async Task WikiPage_FakeProcessReadsOneMarkdownPageFromUrl()
    {
        string root = FindRoot(); string fake = Path.Combine(root, "tests", "AdoRead.FakeHost", "bin", "Release", "net10.0", "AdoRead.FakeHost.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root }; start.ArgumentList.Add(fake); start.ArgumentList.Add("wiki"); start.ArgumentList.Add("page"); start.ArgumentList.Add("https://dev.azure.com/fabricated/FabricatedApp/_wiki/wikis/FabricatedApp.wiki/51/Authentication"); start.ArgumentList.Add("--compact");
        using Process process = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token); string output = await process.StandardOutput.ReadToEndAsync();
        Assert.Equal(0, process.ExitCode); using JsonDocument json = JsonDocument.Parse(output); using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "tests", "AdoRead.IntegrationTests", "Fixtures", "wiki-page.json"))); Assert.True(JsonElement.DeepEquals(expected.RootElement, json.RootElement), output);
    }
    private static string FindRoot() { DirectoryInfo? current = new(AppContext.BaseDirectory); while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "AdoRead.slnx"))) return current.FullName; current = current.Parent; } throw new DirectoryNotFoundException(); }
}
