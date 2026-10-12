using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Text;
using AdoRead.WorkItems;

namespace AdoRead.IntegrationTests.WorkItems;

public sealed class LiveWorkItemSmokeTests
{
    [Fact]
    public async Task ReadConfiguredWorkItem_WhenLiveTestsAreExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ADO_READ_LIVE_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        string idValue = Environment.GetEnvironmentVariable("ADO_READ_WORK_ITEM_ID")
            ?? throw new InvalidOperationException("ADO_READ_WORK_ITEM_ID is required for the live smoke test.");
        Assert.True(int.TryParse(idValue, out int id) && id > 0, "ADO_READ_WORK_ITEM_ID must be a positive integer.");
        AdoReadConfiguration configuration = await new ConfigurationStore().LoadAsync(null, CancellationToken.None);
        string pat = new PatProvider().GetRequiredPat();
        await using IAdoSession session = new AdoSessionFactory()
            .Create(configuration.OrganizationUrl, pat);
        var reader = new WorkItemReader(
            session.WorkItems,
            new WorkItemMapper(new RichTextNormalizer()),
            configuration.OrganizationUrl,
            TimeProvider.System);
        using var timeout = new CancellationTokenSource(configuration.RequestTimeout);

        var result = await reader.ReadAsync(id, timeout.Token);

        Assert.Equal("success", result.Status);
        Assert.Equal(id, Assert.Single(result.Results).Id);
    }

    [Fact]
    public async Task DownloadDedicatedImageWorkItem_WhenLiveVisualTestsAreExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ADO_READ_LIVE_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        string idValue = Environment.GetEnvironmentVariable("ADO_READ_LIVE_IMAGE_WORK_ITEM_ID")
            ?? throw new InvalidOperationException("ADO_READ_LIVE_IMAGE_WORK_ITEM_ID is required for the live visual acceptance test.");
        Assert.True(
            int.TryParse(idValue, out int id) && id > 0,
            "ADO_READ_LIVE_IMAGE_WORK_ITEM_ID must be a positive integer.");
        string expectedFormat = Environment.GetEnvironmentVariable("ADO_READ_LIVE_IMAGE_FORMAT")
            ?? throw new InvalidOperationException("ADO_READ_LIVE_IMAGE_FORMAT is required for the live visual acceptance test.");
        Assert.Contains(
            expectedFormat.ToLowerInvariant(),
            new[] { "png", "jpg", "webp" });

        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ado-read-live-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, "AdoRead.Cli.dll");
            Assert.True(File.Exists(assemblyPath), $"The CLI assembly was not found at {assemblyPath}.");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("work-item");
            startInfo.ArgumentList.Add(id.ToString());
            startInfo.ArgumentList.Add("--download-images");
            startInfo.ArgumentList.Add(outputDirectory);
            startInfo.ArgumentList.Add("--compact");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the live ado-read process.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
                await standardOutput;
                await standardError;
                throw;
            }

            Assert.Equal(0, process.ExitCode);
            string errorOutput = await standardError;
            Assert.True(string.IsNullOrWhiteSpace(errorOutput), errorOutput);

            using JsonDocument output = JsonDocument.Parse(await standardOutput);
            JsonElement document = output.RootElement;
            Assert.Equal("workItemSearch", document.GetProperty("kind").GetString());
            Assert.Equal("2.1", document.GetProperty("schemaVersion").GetString());
            Assert.Equal("success", document.GetProperty("status").GetString());
            Assert.Equal(
                Path.GetFullPath(outputDirectory),
                document.GetProperty("query").GetProperty("downloadImagesDirectory").GetString());

            JsonElement workItem = Assert.Single(document.GetProperty("results").EnumerateArray());
            JsonElement media = workItem.GetProperty("media");
            Assert.Equal(Path.GetFullPath(outputDirectory), media.GetProperty("outputDirectory").GetString());
            JsonElement image = Assert.Single(
                media.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("status").GetString() == "downloaded"
                    && item.GetProperty("sources").EnumerateArray()
                        .Any(source => source.GetProperty("kind").GetString() == "acceptanceCriteria"));
            string format = image.GetProperty("format").GetString()!;
            Assert.Equal(expectedFormat.ToLowerInvariant(), format);
            string localPath = Path.GetFullPath(image.GetProperty("localPath").GetString()!);
            string ownedDirectory = Path.GetFullPath(outputDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            Assert.StartsWith(ownedDirectory, localPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(localPath));

            byte[] bytes = await File.ReadAllBytesAsync(localPath);
            Assert.NotEmpty(bytes);
            Assert.Equal(bytes.LongLength, image.GetProperty("byteLength").GetInt64());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                image.GetProperty("sha256").GetString());
            Assert.True(HasSupportedSignature(bytes, format));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static bool HasSupportedSignature(byte[] bytes, string? format) => format switch
    {
        "png" => bytes.Length >= 8
            && bytes[0] == 137
            && bytes[1] == 80
            && bytes[2] == 78
            && bytes[3] == 71
            && bytes[4] == 13
            && bytes[5] == 10
            && bytes[6] == 26
            && bytes[7] == 10,
        "jpg" => bytes.Length >= 3
            && bytes[0] == 255
            && bytes[1] == 216
            && bytes[2] == 255,
        "webp" => bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P',
        _ => false
    };
}
