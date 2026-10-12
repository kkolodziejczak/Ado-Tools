using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Text;
using AdoRead.WorkItems;

namespace AdoRead.UnitTests.WorkItems;

public sealed class WorkItemReaderTests
{
    private static readonly Uri OrganizationUrl = new("https://dev.azure.com/example/");
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadAsync_FetchesItemAtOrganizationScopeThenCommentsUsingReturnedProject()
    {
        var item = new SourceWorkItem(
            42,
            3,
            null,
            new Dictionary<string, object?>
            {
                ["System.TeamProject"] = "Payments",
                ["System.Title"] = "Story"
            },
            []);
        var source = new FakeWorkItemSource(item, []);
        WorkItemReader reader = CreateReader(source);

        var result = await reader.ReadAsync(42, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Single(result.Results);
        Assert.Equal(42, source.RequestedId);
        Assert.Equal("Payments", source.CommentsProject);
        Assert.Equal(42, source.CommentsWorkItemId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadAsync_MissingItemReturnsNotFoundWithoutFetchingComments()
    {
        var source = new FakeWorkItemSource(item: null, comments: []);
        WorkItemReader reader = CreateReader(source);

        var result = await reader.ReadAsync(404, CancellationToken.None);

        Assert.Equal("notFound", result.Status);
        Assert.Empty(result.Results);
        Assert.Empty(result.Errors);
        Assert.Null(source.CommentsProject);
    }

    [Fact]
    public async Task ReadAsync_SourceFailureReturnsSafeErrorEnvelope()
    {
        var source = new FakeWorkItemSource(
            new WorkItemSourceException("forbidden", "The PAT cannot read work items.", transient: false));
        WorkItemReader reader = CreateReader(source);

        var result = await reader.ReadAsync(9, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Empty(result.Results);
        Assert.Equal("forbidden", result.Errors[0].Code);
        Assert.Equal("azureDevOps", result.Errors[0].Scope);
    }

    [Fact]
    public async Task ReadAsync_MissingProjectReturnsMalformedResponseError()
    {
        var item = new SourceWorkItem(12, 1, null, new Dictionary<string, object?>(), []);
        var source = new FakeWorkItemSource(item, []);
        WorkItemReader reader = CreateReader(source);

        var result = await reader.ReadAsync(12, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("missingProject", result.Errors[0].Code);
        Assert.Null(source.CommentsProject);
    }

    [Fact]
    public async Task ReadAsync_PreservesSuccessfulImageAndReportsPartialMediaFailure()
    {
        var attachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
        var item = new SourceWorkItem(
            42,
            3,
            null,
            new Dictionary<string, object?>
            {
                ["System.TeamProject"] = "Payments",
                ["System.Description"] =
                    $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{attachmentId:D}?fileName=valid.png\"><img src=\"https://evil.example/image.png\">"
            },
            []);
        var source = new FakeWorkItemSource(item, [])
        {
            Attachments = new Dictionary<Guid, byte[]> { [attachmentId] = PngBytes() }
        };
        WorkItemReader reader = CreateReader(source);
        string directory = Path.Combine(Path.GetTempPath(), $"ado-read-reader-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var result = await reader.ReadAsync(42, directory, CancellationToken.None);
            WorkItemDocument document = Assert.Single(result.Results);

            Assert.Equal("partial", result.Status);
            Assert.Equal(ExitCodes.Partial, ExitCodes.FromStatus(result.Status));
            Assert.Equal("mediaIncomplete", Assert.Single(result.Errors).Code);
            Assert.Equal(2, document.Media!.Items.Count);
            Assert.Equal(["downloaded", "skipped"], document.Media.Items.Select(item => item.Status).ToArray());
            Assert.Equal("unsupportedImageReference", document.Media.Items[1].Error!.Code);
            Assert.Equal(1, source.AttachmentCallCount);
            Assert.True(File.Exists(document.Media.Items[0].LocalPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_PreservesTextAndEarlierImagesWhenOneImageOutputIsUnauthorized()
    {
        var firstAttachmentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var unauthorizedAttachmentId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var item = new SourceWorkItem(
            42,
            3,
            null,
            new Dictionary<string, object?>
            {
                ["System.TeamProject"] = "Payments",
                ["System.Title"] = "Keep this work-item text",
                ["System.Description"] =
                    $"<img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{firstAttachmentId:D}?fileName=first.png\"><img src=\"https://dev.azure.com/example/Payments/_apis/wit/attachments/{unauthorizedAttachmentId:D}?fileName=locked.png\">"
            },
            []);
        var source = new FakeWorkItemSource(item, [])
        {
            Attachments = new Dictionary<Guid, byte[]> { [firstAttachmentId] = PngBytes() },
            UnauthorizedAttachmentId = unauthorizedAttachmentId
        };
        string directory = Path.Combine(Path.GetTempPath(), $"ado-read-reader-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var result = await CreateReader(source).ReadAsync(42, directory, CancellationToken.None);
            WorkItemDocument document = Assert.Single(result.Results);

            Assert.Equal("partial", result.Status);
            Assert.Equal("Keep this work-item text", document.Title);
            Assert.Equal(["downloaded", "failed"], document.Media!.Items.Select(item => item.Status).ToArray());
            Assert.Equal("imageOutputError", document.Media.Items[1].Error!.Code);
            Assert.True(File.Exists(document.Media.Items[0].LocalPath));
            Assert.Equal(ExitCodes.Partial, ExitCodes.FromStatus(result.Status));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static WorkItemReader CreateReader(IWorkItemSource source) =>
        new(
            source,
            new WorkItemMapper(new RichTextNormalizer()),
            OrganizationUrl,
            new FixedTimeProvider(Now));

    private static byte[] PngBytes() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private sealed class FakeWorkItemSource : IWorkItemSource
    {
        private readonly SourceWorkItem? _item;
        private readonly IReadOnlyList<WorkItemCommentDocument> _comments;
        private readonly WorkItemSourceException? _exception;

        public FakeWorkItemSource(SourceWorkItem? item, IReadOnlyList<WorkItemCommentDocument> comments)
        {
            _item = item;
            _comments = comments;
        }

        public FakeWorkItemSource(WorkItemSourceException exception)
        {
            _exception = exception;
            _comments = [];
        }

        public int? RequestedId { get; private set; }

        public string? CommentsProject { get; private set; }

        public int? CommentsWorkItemId { get; private set; }

        public IReadOnlyDictionary<Guid, byte[]> Attachments { get; init; } = new Dictionary<Guid, byte[]>();

        public Guid? UnauthorizedAttachmentId { get; init; }

        public int AttachmentCallCount { get; private set; }

        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken)
        {
            RequestedId = id;
            return _exception is null
                ? Task.FromResult(_item)
                : Task.FromException<SourceWorkItem?>(_exception);
        }

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken)
        {
            CommentsProject = project;
            CommentsWorkItemId = id;
            return Task.FromResult(_comments);
        }

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken)
        {
            AttachmentCallCount++;
            if (attachmentId == UnauthorizedAttachmentId)
            {
                return Task.FromException<Stream>(new UnauthorizedAccessException("The image file is locked."));
            }

            return Task.FromResult<Stream>(new MemoryStream(Attachments[attachmentId], writable: false));
        }

    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
