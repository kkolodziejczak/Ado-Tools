using System.Diagnostics;
using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.WorkItems;

namespace AdoRead.UnitTests.Commands;

public sealed class WorkItemCommandTests
{
    [Fact]
    public async Task InvokeAsync_ReadsOrganizationScopedWorkItemAndWritesStructuredJson()
    {
        var writer = new StringWriter();
        var source = new FakeWorkItemSource
        {
            WorkItem = new SourceWorkItem(
                42,
                7,
                new Uri("https://dev.azure.com/example/_apis/wit/workitems/42"),
                new Dictionary<string, object?>
                {
                    ["System.TeamProject"] = "Payments",
                    ["System.WorkItemType"] = "User Story",
                    ["System.Title"] = "Pay an invoice"
                },
                Relations: [])
        };
        var session = new FakeAdoSession(source);
        var sessionFactory = new FakeAdoSessionFactory(session);
        CommandApplication application = CreateApplication(writer, sessionFactory);

        int exitCode = await application.InvokeAsync(["work-item", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("workItemSearch", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("success", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("query").GetProperty("id").GetInt32());
        Assert.Equal("Pay an invoice", document.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.Equal("Payments", source.CommentProject);
        Assert.Equal(0, source.AttachmentCallCount);
        Assert.Equal("test-pat", sessionFactory.Pat);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task InvokeAsync_ImageBearingWorkItemWithoutDownloadOptionKeepsSchema20AndSkipsAttachments()
    {
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var writer = new StringWriter();
        var source = new FakeWorkItemSource
        {
            WorkItem = new SourceWorkItem(
                42,
                7,
                null,
                new Dictionary<string, object?>
                {
                    ["System.TeamProject"] = "Payments",
                    ["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                        $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=screen.png\">"
                },
                [])
        };
        CommandApplication application = CreateApplication(
            writer,
            new FakeAdoSessionFactory(new FakeAdoSession(source)));

        int exitCode = await application.InvokeAsync(["work-item", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("2.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.False(document.RootElement.GetProperty("query").TryGetProperty("downloadImagesDirectory", out _));
        Assert.False(document.RootElement.GetProperty("results")[0].TryGetProperty("media", out _));
        Assert.Equal(0, source.AttachmentCallCount);
    }

    [Theory]
    [InlineData("https://dev.azure.com/example/Payments/_workitems/edit/42")]
    [InlineData("https://dev.azure.com/example/Payments/_apis/wit/workitems/42?api-version=7.1")]
    [InlineData("https://example.visualstudio.com/Payments/_workitems/edit/42")]
    public async Task InvokeAsync_AcceptsWorkItemUrl(string reference)
    {
        var writer = new StringWriter();
        var source = new FakeWorkItemSource
        {
            WorkItem = new SourceWorkItem(
                42,
                1,
                new Uri("https://dev.azure.com/example/_apis/wit/workitems/42"),
                new Dictionary<string, object?>
                {
                    ["System.TeamProject"] = "Payments",
                    ["System.WorkItemType"] = "User Story",
                    ["System.Title"] = "Pay an invoice"
                },
                Relations: [])
        };
        CommandApplication application = CreateApplication(
            writer,
            new FakeAdoSessionFactory(new FakeAdoSession(source)));

        int exitCode = await application.InvokeAsync(["work-item", reference, "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(42, source.RequestedId);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(42, document.RootElement.GetProperty("query").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_RejectsWorkItemUrlFromDifferentOrganizationBeforeReadingPat()
    {
        var writer = new StringWriter();
        var configurationStore = new CountingConfigurationStore();
        var patProvider = new CountingPatProvider();
        CommandApplication application = TestTool.Umbrella(
            configurationStore,
            new JsonOutput(writer),
            TimeProvider.System,
            patProvider,
            new FakeAdoSessionFactory(new FakeAdoSession(new FakeWorkItemSource())));

        int exitCode = await application.InvokeAsync(
            ["work-item", "https://dev.azure.com/fabricated/Payments/_workitems/edit/42", "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("organizationMismatch", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(1, configurationStore.LoadCount);
        Assert.Equal(0, patProvider.ReadCount);
    }

    [Theory]
    [InlineData("https://dev.azure.com/example/Payments/_git/Repo/pullrequest/42")]
    [InlineData("http://dev.azure.com/example/Payments/_workitems/edit/42")]
    [InlineData("https://example.com/example/Payments/_workitems/edit/42")]
    public async Task InvokeAsync_RejectsInvalidWorkItemUrlBeforeReadingConfiguration(string reference)
    {
        var writer = new StringWriter();
        var configurationStore = new CountingConfigurationStore();
        CommandApplication application = TestTool.Umbrella(
            configurationStore,
            new JsonOutput(writer),
            TimeProvider.System,
            new CountingPatProvider(),
            new FakeAdoSessionFactory(new FakeAdoSession(new FakeWorkItemSource())));

        int exitCode = await application.InvokeAsync(
            ["work-item", reference, "--compact"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalidWorkItemId", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, configurationStore.LoadCount);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonEmptyImageOutputDirectoryBeforeReadingPat()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "caller-owned.txt"), "keep");
        try
        {
            var writer = new StringWriter();
            var configurationStore = new CountingConfigurationStore();
            var patProvider = new CountingPatProvider();
            var sessionFactory = new FakeAdoSessionFactory(new FakeAdoSession(new FakeWorkItemSource()));
            CommandApplication application = TestTool.Umbrella(
                configurationStore,
                new JsonOutput(writer),
                TimeProvider.System,
                patProvider,
                sessionFactory);

            int exitCode = await application.InvokeAsync(
                ["work-item", "42", "--download-images", outputDirectory, "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
            using JsonDocument document = JsonDocument.Parse(writer.ToString());
            Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(
                "invalidImageOutputDirectory",
                document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal(0, configurationStore.LoadCount);
            Assert.Equal(0, patProvider.ReadCount);
            Assert.Null(sessionFactory.Pat);
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "caller-owned.txt")));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeAsync_RejectsReparsePointImageOutputDirectoryBeforeReadingPat()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ado-read-reparse-images-{Guid.NewGuid():N}");
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        CreateJunction(link, target);
        try
        {
            var writer = new StringWriter();
            var configurationStore = new CountingConfigurationStore();
            var patProvider = new CountingPatProvider();
            var sessionFactory = new FakeAdoSessionFactory(new FakeAdoSession(new FakeWorkItemSource()));
            CommandApplication application = TestTool.Umbrella(
                configurationStore,
                new JsonOutput(writer),
                TimeProvider.System,
                patProvider,
                sessionFactory);

            int exitCode = await application.InvokeAsync(
                ["work-item", "42", "--download-images", link, "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
            using JsonDocument document = JsonDocument.Parse(writer.ToString());
            Assert.Equal("invalidImageOutputDirectory", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal(0, configurationStore.LoadCount);
            Assert.Equal(0, patProvider.ReadCount);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_ReportsUncreatableImageOutputDirectoryAsInvalidInput()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ado-read-invalid-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string parentFile = Path.Combine(root, "parent-file");
        await File.WriteAllTextAsync(parentFile, "caller-owned");
        string outputDirectory = Path.Combine(parentFile, "images");
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var writer = new StringWriter();
        var configurationStore = new CountingConfigurationStore();
        var patProvider = new CountingPatProvider();
        var source = new FakeWorkItemSource
        {
            WorkItem = new SourceWorkItem(
                42,
                1,
                null,
                new Dictionary<string, object?>
                {
                    ["System.TeamProject"] = "Payments",
                    ["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                        $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=screen.png\">"
                },
                [])
        };

        try
        {
            CommandApplication application = TestTool.Umbrella(
                configurationStore,
                new JsonOutput(writer),
                TimeProvider.System,
                patProvider,
                new FakeAdoSessionFactory(new FakeAdoSession(source)));

            int exitCode = await application.InvokeAsync(
                ["work-item", "42", "--download-images", outputDirectory, "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
            using JsonDocument document = JsonDocument.Parse(writer.ToString());
            Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("invalidImageOutputDirectory", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal(0, patProvider.ReadCount);
            Assert.Equal(0, source.AttachmentCallCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("io")]
    [InlineData("unauthorized")]
    public async Task InvokeAsync_MetadataFilesystemFailureIsNotClassifiedAsImageOutputDirectoryFailure(string failure)
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-metadata-io-{Guid.NewGuid():N}");
        var source = new FakeWorkItemSource
        {
            ThrowMetadataIOException = failure == "io",
            ThrowMetadataUnauthorized = failure == "unauthorized"
        };
        var writer = new StringWriter();

        try
        {
            int exitCode = await CreateApplication(
                writer,
                new FakeAdoSessionFactory(new FakeAdoSession(source)))
                .InvokeAsync(
                    ["work-item", "42", "--download-images", outputDirectory, "--compact"],
                    CancellationToken.None);

            Assert.Equal(ExitCodes.Error, exitCode);
            using JsonDocument document = JsonDocument.Parse(writer.ToString());
            Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("unexpectedError", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.DoesNotContain("invalidImageOutputDirectory", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nonempty")]
    public async Task InvokeAsync_ImageOutputDirectoryRaceAfterPrevalidationReturnsSchema21InvalidDirectory(string race)
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-race-{Guid.NewGuid():N}");
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var source = new FakeWorkItemSource
        {
            WorkItem = new SourceWorkItem(
                42,
                1,
                null,
                new Dictionary<string, object?>
                {
                    ["System.TeamProject"] = "Payments",
                    ["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                        $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=screen.png\">"
                },
                []),
            BeforeGet = () =>
            {
                if (race == "missing")
                {
                    Directory.Delete(outputDirectory);
                }
                else
                {
                    File.WriteAllText(Path.Combine(outputDirectory, "caller-race.txt"), "caller-owned");
                }
            }
        };
        var writer = new StringWriter();

        try
        {
            int exitCode = await CreateApplication(
                writer,
                new FakeAdoSessionFactory(new FakeAdoSession(source)))
                .InvokeAsync(
                    ["work-item", "42", "--download-images", outputDirectory, "--compact"],
                    CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
            using JsonDocument document = JsonDocument.Parse(writer.ToString());
            Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("invalidImageOutputDirectory", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
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
    public async Task InvokeAsync_ReturnsNotFoundWithoutFetchingComments()
    {
        var writer = new StringWriter();
        var source = new FakeWorkItemSource();
        CommandApplication application = CreateApplication(
            writer,
            new FakeAdoSessionFactory(new FakeAdoSession(source)));

        int exitCode = await application.InvokeAsync(["work-item", "404", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.NotFound, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("notFound", document.RootElement.GetProperty("status").GetString());
        Assert.Null(source.CommentProject);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonPositiveIdBeforeReadingConfigurationOrPat()
    {
        var writer = new StringWriter();
        var configurationStore = new CountingConfigurationStore();
        var patProvider = new CountingPatProvider();
        var sessionFactory = new FakeAdoSessionFactory(new FakeAdoSession(new FakeWorkItemSource()));
        CommandApplication application = TestTool.Umbrella(
            configurationStore,
            new JsonOutput(writer),
            TimeProvider.System,
            patProvider,
            sessionFactory);

        int exitCode = await application.InvokeAsync(["work-item", "0", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalidWorkItemId", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, configurationStore.LoadCount);
        Assert.Equal(0, patProvider.ReadCount);
        Assert.Null(sessionFactory.Pat);
    }

    [Fact]
    public async Task InvokeAsync_MissingPatReturnsSafeAuthenticationError()
    {
        var writer = new StringWriter();
        var sessionFactory = new FakeAdoSessionFactory(new FakeAdoSession(new FakeWorkItemSource()));
        CommandApplication application = TestTool.Umbrella(
            new CountingConfigurationStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            new MissingPatProvider(),
            sessionFactory);

        int exitCode = await application.InvokeAsync(["work-item", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("authentication", document.RootElement.GetProperty("errors")[0].GetProperty("scope").GetString());
        Assert.Equal("missingPat", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(sessionFactory.Pat);
    }

    [Fact]
    public async Task InvokeAsync_AppliesConfiguredTimeoutToWholeRead()
    {
        var writer = new StringWriter();
        var source = new FakeWorkItemSource { DelayUntilCancelled = true };
        CommandApplication application = TestTool.Umbrella(
            new CountingConfigurationStore(requestTimeoutSeconds: 1),
            new JsonOutput(writer),
            TimeProvider.System,
            new CountingPatProvider(),
            new FakeAdoSessionFactory(new FakeAdoSession(source)));

        int exitCode = await application.InvokeAsync(["work-item", "42", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("timeout", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.True(document.RootElement.GetProperty("errors")[0].GetProperty("transient").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_TimeoutDuringImageDownloadReturnsSchema21AndCleansOutput()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ado-read-timeout-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var source = new FakeWorkItemSource
        {
            WorkItem = new SourceWorkItem(
                42,
                1,
                null,
                new Dictionary<string, object?>
                {
                    ["System.TeamProject"] = "Payments",
                    ["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                        $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=timeout.png\">"
                },
                []),
            DelayAttachmentUntilCancelled = true
        };
        var writer = new StringWriter();
        CommandApplication application = TestTool.Umbrella(
            new CountingConfigurationStore(requestTimeoutSeconds: 1),
            new JsonOutput(writer),
            TimeProvider.System,
            new CountingPatProvider(),
            new FakeAdoSessionFactory(new FakeAdoSession(source)));

        try
        {
            int exitCode = await application.InvokeAsync(
                ["work-item", "42", "--download-images", outputDirectory, "--compact"],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Error, exitCode);
            using JsonDocument document = JsonDocument.Parse(writer.ToString());
            Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("timeout", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*"));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static CommandApplication CreateApplication(
        StringWriter writer,
        IAdoSessionFactory sessionFactory) =>
        TestTool.Umbrella(
            new CountingConfigurationStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            new CountingPatProvider(),
            sessionFactory);

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create junction.");
        Assert.True(process.WaitForExit(5000), "Timed out creating junction.");
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class CountingConfigurationStore(
        int requestTimeoutSeconds = AdoReadConfiguration.DefaultRequestTimeoutSeconds) : IConfigurationStore
    {
        public string DefaultPath => "unused";

        public int LoadCount { get; private set; }

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(AdoReadConfiguration.Create(
                new Uri("https://dev.azure.com/example"),
                requestTimeoutSeconds));
        }

        public Task SaveAsync(
            string? overridePath,
            AdoReadConfiguration configuration,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingPatProvider : IPatProvider
    {
        public int ReadCount { get; private set; }

        public string GetRequiredPat()
        {
            ReadCount++;
            return "test-pat";
        }
    }

    private sealed class MissingPatProvider : IPatProvider
    {
        public string GetRequiredPat() => throw new CredentialException("missingPat", "ADO_READ_PAT is required.");
    }

    private sealed class FakeAdoSessionFactory(FakeAdoSession session) : IAdoSessionFactory
    {
        public string? Pat { get; private set; }

        public IAdoSession Create(Uri organizationUrl, string pat)
        {
            Pat = pat;
            return session;
        }
    }

    private sealed class FakeAdoSession(IWorkItemSource workItems) : IAdoSession
    {
        public IWorkItemSource WorkItems { get; } = workItems;

        public IProjectCatalog Projects => throw new NotSupportedException();

        public IPullRequestSource PullRequests => throw new NotSupportedException();

        public ISprintSource Sprints => throw new NotSupportedException();

        public ITeamClient Teams => throw new NotSupportedException();

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWorkItemSource : IWorkItemSource
    {
        public SourceWorkItem? WorkItem { get; init; }

        public bool DelayUntilCancelled { get; init; }

        public bool DelayAttachmentUntilCancelled { get; init; }

        public bool ThrowMetadataIOException { get; init; }

        public bool ThrowMetadataUnauthorized { get; init; }

        public Action? BeforeGet { get; init; }

        public int AttachmentCallCount { get; private set; }

        public string? CommentProject { get; private set; }

        public int? RequestedId { get; private set; }

        public async Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken)
        {
            RequestedId = id;
            BeforeGet?.Invoke();
            if (ThrowMetadataIOException)
            {
                throw new IOException("The metadata transport failed.");
            }

            if (ThrowMetadataUnauthorized)
            {
                throw new UnauthorizedAccessException("The metadata transport was denied.");
            }

            if (DelayUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return WorkItem;
        }

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken)
        {
            CommentProject = project;
            return Task.FromResult<IReadOnlyList<WorkItemCommentDocument>>([]);
        }

        public async Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken)
        {
            AttachmentCallCount++;
            if (DelayAttachmentUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            throw new NotSupportedException();
        }
    }
}
