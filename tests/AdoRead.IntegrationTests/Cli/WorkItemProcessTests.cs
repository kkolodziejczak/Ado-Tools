using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AdoRead.IntegrationTests.Cli;

public sealed class WorkItemProcessTests
{
    [Fact]
    public async Task WorkItem_FakeProcessOutputMatchesFabricatedSnapshot()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = FindFakeHostAssembly(toolRoot);
        ProcessResult result = await RunAsync(toolRoot, assemblyPath, "work-item", "123", "--compact");

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        using JsonDocument actual = JsonDocument.Parse(result.StandardOutput);
        string snapshotPath = Path.Combine(
            toolRoot,
            "tests",
            "AdoRead.IntegrationTests",
            "Fixtures",
            "work-item.json");
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        Assert.True(
            JsonElement.DeepEquals(expected.RootElement, actual.RootElement),
            $"Process output did not match {snapshotPath}.{Environment.NewLine}{result.StandardOutput}");
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("unrelated-typo")]
    public async Task WorkItem_FakeProcessParserErrorsKeepSchema21WhenMediaOptionIsBound(string caseName)
    {
        string toolRoot = FindToolRoot();
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-parser-{Guid.NewGuid():N}");
        string[] arguments = caseName == "missing-id"
            ? ["work-item", "--download-images", outputDirectory, "--compact"]
            : ["work-item", "42", "--download-images", outputDirectory, "--unrelated-typo", "--compact"];

        try
        {
            ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), arguments);

            Assert.Equal(2, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("workItemSearch", output.RootElement.GetProperty("kind").GetString());
            Assert.Equal("2.1", output.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("invalidArguments", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkItem_FakeProcessDownloadsAcceptanceCriteriaPngWithDeterministicMediaManifest()
    {
        string toolRoot = FindToolRoot();
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-images-{Guid.NewGuid():N}");
        byte[] expectedBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        string expectedHash = Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant();
        string attachmentId = "6d8e1ce4-1172-4d07-a83f-35f8d7f5889a";

        try
        {
            ProcessResult result = await RunAsync(
                toolRoot,
                FindFakeHostAssembly(toolRoot),
                "work-item",
                "123",
                "--download-images",
                outputDirectory,
                "--compact");

            Assert.Equal(0, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
            Assert.True(Directory.Exists(outputDirectory));
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            JsonElement document = output.RootElement;
            Assert.Equal("workItemSearch", document.GetProperty("kind").GetString());
            Assert.Equal("2.1", document.GetProperty("schemaVersion").GetString());
            Assert.Equal("success", document.GetProperty("status").GetString());
            Assert.Equal(
                Path.GetFullPath(outputDirectory),
                document.GetProperty("query").GetProperty("downloadImagesDirectory").GetString());

            JsonElement workItem = Assert.Single(document.GetProperty("results").EnumerateArray());
            JsonElement media = workItem.GetProperty("media");
            JsonElement item = Assert.Single(media.GetProperty("items").EnumerateArray());
            Assert.Equal(attachmentId, item.GetProperty("attachmentId").GetString());
            Assert.Equal("checkout-layout.png", item.GetProperty("originalFileName").GetString());
            Assert.Equal("downloaded", item.GetProperty("status").GetString());
            Assert.Equal("png", item.GetProperty("format").GetString());
            Assert.Equal("image/png", item.GetProperty("contentType").GetString());
            Assert.Equal(expectedBytes.Length, item.GetProperty("byteLength").GetInt64());
            Assert.Equal(expectedHash, item.GetProperty("sha256").GetString());
            Assert.Equal(
                new[] { "acceptanceCriteria", "attachmentRelation" },
                item.GetProperty("sources")
                    .EnumerateArray()
                    .Select(source => source.GetProperty("kind").GetString()!)
                    .ToArray());

            string localPath = item.GetProperty("localPath").GetString()!;
            Assert.Equal(
                Path.Combine(outputDirectory, $"{attachmentId.Replace("-", string.Empty, StringComparison.Ordinal)}.png"),
                localPath);
            Assert.True(Path.IsPathFullyQualified(localPath));
            Assert.True(File.Exists(localPath));
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(localPath));
            Assert.Empty(Directory.EnumerateFiles(outputDirectory, ".partial-*"));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkItem_FakeProcessDownloadsAllSourcesAndReportsPartialVisualEvidence()
    {
        string toolRoot = FindToolRoot();
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-images-{Guid.NewGuid():N}");
        const string sharedId = "00000000-0000-0000-0000-00000000000a";
        const string relationOnlyId = "00000000-0000-0000-0000-00000000000b";
        const string invalidId = "00000000-0000-0000-0000-00000000000c";
        byte[] webpBytes =
        [
            0x52, 0x49, 0x46, 0x46, 0x34, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50,
            0x56, 0x50, 0x38, 0x58, 0x0A, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x56, 0x50, 0x38, 0x20, 0x16, 0x00, 0x00, 0x00,
            0x30, 0x01, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00,
            0x0E, 0xC0, 0xFE, 0x25, 0xA4, 0x00, 0x03, 0x70, 0x00, 0x00, 0x00, 0x00
        ];

        try
        {
            Directory.CreateDirectory(outputDirectory);
            ProcessResult result = await RunAsync(
                toolRoot,
                FindFakeHostAssembly(toolRoot),
                "work-item",
                "124",
                "--download-images",
                outputDirectory,
                "--compact");

            Assert.Equal(4, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            JsonElement document = output.RootElement;
            Assert.Equal("workItemSearch", document.GetProperty("kind").GetString());
            Assert.Equal("2.1", document.GetProperty("schemaVersion").GetString());
            Assert.Equal("partial", document.GetProperty("status").GetString());
            Assert.Equal(
                Path.GetFullPath(outputDirectory),
                document.GetProperty("query").GetProperty("downloadImagesDirectory").GetString());

            JsonElement workItem = Assert.Single(document.GetProperty("results").EnumerateArray());
            JsonElement[] items = workItem.GetProperty("media").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(4, items.Length);

            JsonElement shared = Assert.Single(items, item => HasAttachmentId(item, sharedId));
            Assert.Equal("downloaded", shared.GetProperty("status").GetString());
            Assert.Equal(
                ["description", "acceptanceCriteria", "comment", "comment", "attachmentRelation"],
                shared.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("kind").GetString()!).ToArray());
            Assert.Equal(
                Path.Combine(outputDirectory, $"{sharedId.Replace("-", string.Empty, StringComparison.Ordinal)}.png"),
                shared.GetProperty("localPath").GetString());

            JsonElement relationOnly = Assert.Single(items, item => HasAttachmentId(item, relationOnlyId));
            Assert.Equal("downloaded", relationOnly.GetProperty("status").GetString());
            Assert.Equal("webp", relationOnly.GetProperty("format").GetString());
            Assert.Equal(webpBytes, await File.ReadAllBytesAsync(relationOnly.GetProperty("localPath").GetString()!));

            JsonElement invalid = Assert.Single(items, item => HasAttachmentId(item, invalidId));
            Assert.Equal("failed", invalid.GetProperty("status").GetString());
            Assert.Equal("unsupportedImageFormat", invalid.GetProperty("error").GetProperty("code").GetString());
            Assert.False(invalid.TryGetProperty("localPath", out _));

            JsonElement unsafeReference = Assert.Single(
                items,
                item => item.GetProperty("attachmentId").ValueKind == JsonValueKind.Null);
            Assert.Equal("skipped", unsafeReference.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, unsafeReference.GetProperty("attachmentId").ValueKind);
            Assert.Equal("unsupportedImageReference", unsafeReference.GetProperty("error").GetProperty("code").GetString());
            Assert.False(unsafeReference.GetProperty("error").GetProperty("message").GetString()!.Contains("evil", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("mediaIncomplete", document.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal(2, Directory.EnumerateFiles(outputDirectory).Count());
            Assert.Empty(Directory.EnumerateFiles(outputDirectory, ".partial-*"));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkItem_FakeProcessEmitsTheLeanV2PersonContract()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "work-item", "123", "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        JsonElement document = output.RootElement;
        JsonElement workItem = Assert.Single(document.GetProperty("results").EnumerateArray());
        JsonElement comment = Assert.Single(workItem.GetProperty("comments").EnumerateArray());

        Assert.Equal("2.0", document.GetProperty("schemaVersion").GetString());
        Assert.Equal("engineer@example.invalid", workItem.GetProperty("assignedToUniqueName").GetString());
        Assert.False(workItem.TryGetProperty("assignedTo", out _));
        Assert.Equal("reviewer@example.invalid", comment.GetProperty("createdByUniqueName").GetString());
        Assert.False(comment.TryGetProperty("createdBy", out _));
    }

    [Fact]
    public async Task WorkItem_FakeProcessImageBearingDefaultReadRemainsSchema20WithoutMediaDownload()
    {
        string toolRoot = FindToolRoot();
        ProcessResult result = await RunAsync(toolRoot, FindFakeHostAssembly(toolRoot), "work-item", "125", "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("2.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.False(document.RootElement.GetProperty("query").TryGetProperty("downloadImagesDirectory", out _));
        Assert.False(document.RootElement.GetProperty("results")[0].TryGetProperty("media", out _));
    }

    [Fact]
    public async Task WorkItem_FakeProcessCreatesNonexistentImageDirectory()
    {
        string toolRoot = FindToolRoot();
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-images-{Guid.NewGuid():N}");

        try
        {
            Assert.False(Directory.Exists(outputDirectory));
            ProcessResult result = await RunAsync(
                toolRoot,
                FindFakeHostAssembly(toolRoot),
                "work-item",
                "123",
                "--download-images",
                outputDirectory,
                "--compact");

            Assert.Equal(0, result.ExitCode);
            Assert.True(Directory.Exists(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkItem_FakeProcessWithNoImageCandidatesReturnsEmptySchema21Media()
    {
        string toolRoot = FindToolRoot();
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-images-{Guid.NewGuid():N}");

        try
        {
            ProcessResult result = await RunAsync(
                toolRoot,
                FindFakeHostAssembly(toolRoot),
                "work-item",
                "126",
                "--download-images",
                outputDirectory,
                "--compact");

            Assert.Equal(0, result.ExitCode);
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("success", document.RootElement.GetProperty("status").GetString());
            Assert.Empty(document.RootElement.GetProperty("results")[0].GetProperty("media").GetProperty("items").EnumerateArray());
            Assert.True(Directory.Exists(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkItem_FakeProcessAcceptsBrowserUrl()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = FindFakeHostAssembly(toolRoot);

        ProcessResult result = await RunAsync(
            toolRoot,
            assemblyPath,
            "work-item",
            "https://dev.azure.com/fabricated/FabricatedApp/_workitems/edit/123",
            "--compact");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(123, output.RootElement.GetProperty("query").GetProperty("id").GetInt32());
        Assert.Equal(123, output.RootElement.GetProperty("results")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task WorkItem_FakeProcessReturnsNotFoundExitCode()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = FindFakeHostAssembly(toolRoot);

        ProcessResult result = await RunAsync(toolRoot, assemblyPath, "work-item", "404", "--compact");

        Assert.Equal(3, result.ExitCode);
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("notFound", output.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task WorkItem_FakeProcessReturnsSchema21NotFoundWithoutImageOutput()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = FindFakeHostAssembly(toolRoot);
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-not-found-images-{Guid.NewGuid():N}");

        try
        {
            ProcessResult result = await RunAsync(
                toolRoot,
                assemblyPath,
                "work-item",
                "404",
                "--download-images",
                outputDirectory,
                "--compact");

            Assert.Equal(3, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("workItemSearch", output.RootElement.GetProperty("kind").GetString());
            Assert.Equal("2.1", output.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("notFound", output.RootElement.GetProperty("status").GetString());
            Assert.Empty(output.RootElement.GetProperty("results").EnumerateArray());
            Assert.True(Directory.Exists(outputDirectory));
            Assert.Empty(Directory.EnumerateFiles(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkItem_ProductionProcessReturnsAuthenticationErrorWhenPatIsMissing()
    {
        string toolRoot = FindToolRoot();
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Cli.dll");
        string directory = Path.Combine(Path.GetTempPath(), $"ado-read-work-item-{Guid.NewGuid():N}");
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
                "work-item",
                "123",
                "--config",
                configPath,
                "--compact");

            Assert.Equal(5, result.ExitCode);
            using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("error", output.RootElement.GetProperty("status").GetString());
            Assert.Equal("2.0", output.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("authentication", output.RootElement.GetProperty("errors")[0].GetProperty("scope").GetString());
            Assert.Equal("missingPat", output.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
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

        startInfo.Environment.Remove("ADO_READ_PAT");

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ado-read process.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static bool HasAttachmentId(JsonElement item, string expected) =>
        item.TryGetProperty("attachmentId", out JsonElement attachmentId)
        && string.Equals(attachmentId.GetString(), expected, StringComparison.Ordinal);

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
