using System.Diagnostics;

namespace AdoRead.IntegrationTests.Cli;

public sealed class ToolHelpProcessTests
{
    public static TheoryData<string, string[]> Tools => new()
    {
        { "AdoRead.Cli", ["config", "auth", "projects", "teams", "pr", "work-item", "wiki", "sprints"] },
        { "AdoRead.PullRequests.Cli", ["config", "auth", "projects", "teams"] },
        { "AdoRead.WorkItems.Cli", ["config", "auth", "projects", "teams"] },
        { "AdoRead.Wiki.Cli", ["config", "auth", "projects", "teams", "list", "pages", "page", "search"] },
        { "AdoRead.Sprints.Cli", ["config", "auth", "projects", "teams", "list", "show"] },
    };

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task Help_ExitsZeroAndListsOnlyThisToolsCommands(string assemblyName, string[] expectedCommands)
    {
        string help = await RunHelpAsync(assemblyName);

        Assert.Equal(
            expectedCommands.OrderBy(name => name, StringComparer.Ordinal),
            ListedCommands(help).OrderBy(name => name, StringComparer.Ordinal));
    }

    private static async Task<string> RunHelpAsync(string assemblyName)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll"));
        startInfo.ArgumentList.Add("--help");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {assemblyName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(await standardError));
        return await standardOutput;
    }

    private static List<string> ListedCommands(string help)
    {
        var names = new List<string>();
        bool inCommandSection = false;
        foreach (string line in help.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line == "Commands:")
            {
                inCommandSection = true;
                continue;
            }

            if (!inCommandSection)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            names.Add(line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        }

        return names;
    }
}
