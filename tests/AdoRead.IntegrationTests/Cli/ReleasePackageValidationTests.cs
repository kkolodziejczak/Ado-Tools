using System.Diagnostics;
using System.IO.Compression;

namespace AdoRead.IntegrationTests.Cli;

public sealed class ReleasePackageValidationTests
{
    [Fact]
    public async Task PackageValidation_RejectsModernPatLikeTextInBinaryPackageEntries()
    {
        string toolRoot = FindToolRoot();
        string scriptPath = Path.Combine(toolRoot, "scripts", "Verify-ReleasePackage.ps1");
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-package-validation-{Guid.NewGuid():N}");
        string packagePath = Path.Combine(temporaryDirectory, "AdoRead.Tool.1.0.0.nupkg");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            await CreatePackageAsync(packagePath);

            ProcessResult result = await RunValidationAsync(scriptPath, toolRoot, packagePath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("package:tools/net10.0/any/AdoRead.Cli.dll", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static readonly (string Project, string PackageId, string Command)[] Tools =
    [
        ("AdoRead.Cli", "AdoRead.Tool", "ado-read"),
        ("AdoRead.PullRequests.Cli", "AdoRead.PullRequests.Tool", "ado-read-pr"),
        ("AdoRead.WorkItems.Cli", "AdoRead.WorkItems.Tool", "ado-read-work-item"),
        ("AdoRead.Wiki.Cli", "AdoRead.Wiki.Tool", "ado-read-wiki"),
        ("AdoRead.Sprints.Cli", "AdoRead.Sprints.Tool", "ado-read-sprints"),
        ("AdoRead.Pipelines.Cli", "AdoRead.Pipelines.Tool", "ado-read-pipelines"),
    ];

    [Fact]
    public async Task ReleaseMetadata_UsesTheV2PackageAndReviewContract()
    {
        string toolRoot = FindToolRoot();
        string smokeScript = Path.Combine(toolRoot, "scripts", "Test-ToolPackage.ps1");
        string readme = Path.Combine(toolRoot, "README.md");
        string workflow = Path.Combine(toolRoot, ".github", "workflows", "ci.yml");
        string liveWikiSmoke = Path.Combine(toolRoot, "tests", "AdoRead.IntegrationTests", "Wikis", "LiveWikiSmokeTests.cs");
        string liveWorkItemSmoke = Path.Combine(toolRoot, "tests", "AdoRead.IntegrationTests", "WorkItems", "LiveWorkItemSmokeTests.cs");

        foreach ((string project, string packageId, string command) in Tools)
        {
            string projectFile = Path.Combine(toolRoot, "src", project, $"{project}.csproj");
            string projectContract = await File.ReadAllTextAsync(projectFile);
            Assert.Contains("<Version>1.0.0</Version>", projectContract, StringComparison.Ordinal);
            Assert.Contains($"<PackageId>{packageId}</PackageId>", projectContract, StringComparison.Ordinal);
            Assert.Contains($"<ToolCommandName>{command}</ToolCommandName>", projectContract, StringComparison.Ordinal);
        }

        string smokeContract = await File.ReadAllTextAsync(smokeScript);
        Assert.Contains("[string]$PackageVersion = '1.0.0'", smokeContract, StringComparison.Ordinal);
        Assert.Contains("--download-images", smokeContract, StringComparison.Ordinal);
        Assert.Contains("schemaVersion -ne '2.1'", smokeContract, StringComparison.Ordinal);
        Assert.Contains("'wiki', 'list'", smokeContract, StringComparison.Ordinal);
        Assert.Contains("'wiki', 'pages'", smokeContract, StringComparison.Ordinal);
        Assert.Contains("'wiki', 'page'", smokeContract, StringComparison.Ordinal);
        Assert.Contains("wiki search authentication", smokeContract, StringComparison.Ordinal);
        Assert.Contains("'pipelines', 'pr'", smokeContract, StringComparison.Ordinal);
        string readmeContract = await File.ReadAllTextAsync(readme);
        Assert.Contains("--version 1.0.0", readmeContract, StringComparison.Ordinal);
        Assert.Contains("ado-read-pipelines", readmeContract, StringComparison.Ordinal);
        Assert.Contains("Build (Read)", readmeContract, StringComparison.Ordinal);
        Assert.Contains("Test Management (Read)", readmeContract, StringComparison.Ordinal);
        Assert.Contains("testMessages", readmeContract, StringComparison.Ordinal);
        Assert.Contains("maskedSecretCount", readmeContract, StringComparison.Ordinal);
        Assert.Contains("--download-images", readmeContract, StringComparison.Ordinal);
        Assert.Contains("schema `2.1`", readmeContract, StringComparison.Ordinal);
        Assert.Contains("non-image", readmeContract, StringComparison.Ordinal);
        Assert.Contains("untrusted evidence", readmeContract, StringComparison.Ordinal);
        Assert.Contains("Every item serializes `attachmentId`", readmeContract, StringComparison.Ordinal);
        Assert.Contains("invocation-owned staging files and any invocation-finalized files", readmeContract, StringComparison.Ordinal);
        Assert.Contains("Wiki (Read)", readmeContract, StringComparison.Ordinal);
        Assert.Contains("only one selected Wiki-backed Markdown file", readmeContract, StringComparison.Ordinal);
        Assert.Contains("must never join it into a shell command", readmeContract, StringComparison.Ordinal);
        Assert.Contains("wikiCatalog", readmeContract, StringComparison.Ordinal);
        Assert.Contains("wikiPageCatalog", readmeContract, StringComparison.Ordinal);
        Assert.Contains("wikiPageLookup", readmeContract, StringComparison.Ordinal);
        Assert.Contains("wikiFullTextSearch", readmeContract, StringComparison.Ordinal);
        Assert.Contains("and every Wiki kind", readmeContract, StringComparison.Ordinal);
        Assert.Contains("ADO_READ_WIKI_PAGE_PATH", readmeContract, StringComparison.Ordinal);
        Assert.Contains("ADO_READ_WIKI_SEARCH_TEXT", readmeContract, StringComparison.Ordinal);
        string liveSmokeContract = await File.ReadAllTextAsync(liveWikiSmoke);
        Assert.Contains("ADO_READ_LIVE_TESTS", liveSmokeContract, StringComparison.Ordinal);
        Assert.Contains("ADO_READ_WIKI_PAGE_PATH", liveSmokeContract, StringComparison.Ordinal);
        Assert.Contains("ADO_READ_WIKI_SEARCH_TEXT", liveSmokeContract, StringComparison.Ordinal);
        string workflowContract = await File.ReadAllTextAsync(workflow);
        Assert.Contains("$version = '1.0.0'", workflowContract, StringComparison.Ordinal);
        foreach ((string _, string packageId, string command) in Tools)
        {
            Assert.Contains($"PackageId = '{packageId}'", workflowContract, StringComparison.Ordinal);
            Assert.Contains($"CommandName = '{command}'", workflowContract, StringComparison.Ordinal);
        }

        string liveImageContract = await File.ReadAllTextAsync(liveWorkItemSmoke);
        Assert.Contains("ADO_READ_LIVE_IMAGE_WORK_ITEM_ID", liveImageContract, StringComparison.Ordinal);
        Assert.Contains("--download-images", liveImageContract, StringComparison.Ordinal);
        Assert.Contains("acceptanceCriteria", liveImageContract, StringComparison.Ordinal);
        Assert.Contains("SHA256", liveImageContract, StringComparison.Ordinal);
        Assert.Contains("Directory.Delete", liveImageContract, StringComparison.Ordinal);
    }

    private static async Task CreatePackageAsync(string packagePath)
    {
        using ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("tools/net10.0/any/AdoRead.Cli.dll");
        await using Stream stream = entry.Open();
        string modernPatLikeText = string.Concat(new string('a', 75), "AZDO", new string('b', 5));
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes($"binary-prefix|{modernPatLikeText}|binary-suffix");
        await stream.WriteAsync(bytes);
    }

    private static async Task<ProcessResult> RunValidationAsync(string scriptPath, string toolRoot, string packagePath)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("-ToolRoot");
        startInfo.ArgumentList.Add(toolRoot);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start package validation.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
