using AdoRead.AzureDevOps;
using AdoRead.WorkItems;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;
using System.Net;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class WorkItemSourceTests
{
    [Fact]
    public async Task GetAsync_MarksHtmlFieldsPresentOnTheWorkItemAsRichText()
    {
        var client = new FakeWorkItemTrackingClient
        {
            WorkItem = new WorkItem
            {
                Id = 42,
                Rev = 1,
                Fields = new Dictionary<string, object>
                {
                    ["System.TeamProject"] = "Payments",
                    ["System.Description"] = "<p>Pay</p>",
                    ["Custom.Notes"] = "<p>Notes</p>",
                    ["Custom.Plain"] = "<not html>"
                }
            },
            Fields =
            [
                new WorkItemField2 { ReferenceName = "System.Description", Type = FieldType.Html },
                new WorkItemField2 { ReferenceName = "Custom.Notes", Type = FieldType.Html },
                new WorkItemField2 { ReferenceName = "Custom.Plain", Type = FieldType.PlainText },
                new WorkItemField2 { ReferenceName = "Custom.Unused", Type = FieldType.Html }
            ]
        };

        SourceWorkItem? workItem = await new WorkItemSource(client).GetAsync(42, CancellationToken.None);

        Assert.Equal(["Payments"], client.FieldProjects);
        Assert.Equal(["Custom.Notes", "System.Description"], workItem!.RichTextFields.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetAllCommentsAsync_FollowsContinuationTokensAndPreservesModernCommentData()
    {
        var first = new Comment
        {
            Id = 2,
            Version = 3,
            Text = "raw second",
            RenderedText = "<p>rendered second</p>",
            IsDeleted = true,
            CreatedDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Mentions =
            [
                new CommentMention
                {
                    ArtifactId = "42",
                    ArtifactType = "workItem",
                    TargetId = "42"
                }
            ],
            Reactions =
            [
                new CommentReaction
                {
                    Type = (CommentReactionType)0,
                    Count = 3,
                    IsCurrentUserEngaged = true
                }
            ]
        };
        var second = new Comment
        {
            Id = 1,
            Version = 1,
            Text = "raw first",
            RenderedText = "<p>rendered first</p>",
            IsDeleted = false
        };
        var client = new FakeWorkItemTrackingClient(
            new CommentList { Comments = [first], ContinuationToken = "next-page" },
            new CommentList { Comments = [second] });
        var source = new WorkItemSource(client);

        IReadOnlyList<WorkItemCommentDocument> comments = await source.GetAllCommentsAsync(
            "Payments",
            42,
            CancellationToken.None);

        Assert.Equal([null, "next-page"], client.ContinuationTokens);
        Assert.Equal([1, 2], comments.Select(comment => comment.Id));
        WorkItemCommentDocument deleted = Assert.Single(comments, comment => comment.Id == 2);
        Assert.True(deleted.IsDeleted);
        Assert.Equal("raw second", deleted.RawText);
        Assert.Equal("<p>rendered second</p>", deleted.RenderedHtml);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), deleted.CreatedAtUtc);
        Assert.Equal("42", Assert.Single(deleted.Mentions).TargetId);
        Assert.Equal(3, Assert.Single(deleted.Reactions).Count);
        Assert.True(deleted.Reactions[0].IsCurrentUserEngaged);
    }

    [Fact]
    public async Task GetAllCommentsAsync_RejectsRepeatedContinuationToken()
    {
        var client = new FakeWorkItemTrackingClient(
            new CommentList { Comments = [], ContinuationToken = "repeat" },
            new CommentList { Comments = [], ContinuationToken = "repeat" });
        var source = new WorkItemSource(client);

        WorkItemSourceException exception = await Assert.ThrowsAsync<WorkItemSourceException>(() =>
            source.GetAllCommentsAsync("Payments", 42, CancellationToken.None));

        Assert.Equal("commentPaginationLoop", exception.Code);
        Assert.False(exception.Transient);
    }

    [Fact]
    public async Task GetAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new WorkItemSource(new CancelledWorkItemTrackingClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(42, cancellation.Token));
    }

    [Fact]
    public async Task GetAsync_TranslatesRejectedPatWithoutLeakingServiceMessage()
    {
        var source = new WorkItemSource(new FailedWorkItemTrackingClient(
            new VssServiceResponseException(HttpStatusCode.Unauthorized, "secret service response", null)));

        WorkItemSourceException exception = await Assert.ThrowsAsync<WorkItemSourceException>(() =>
            source.GetAsync(42, CancellationToken.None));

        Assert.Equal("unauthorized", exception.Code);
        Assert.Equal("The PAT was rejected by Azure DevOps.", exception.Message);
        Assert.DoesNotContain("secret service response", exception.Message, StringComparison.Ordinal);
        Assert.False(exception.Transient);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForOrganizationScopedNotFoundResponse()
    {
        var source = new WorkItemSource(new FailedWorkItemTrackingClient(
            new VssServiceResponseException(HttpStatusCode.NotFound, "not found", null)));

        SourceWorkItem? result = await source.GetAsync(404, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAttachmentContentAsync_TranslatesRejectedAttachmentWithoutLeakingServiceMessage()
    {
        var source = new WorkItemSource(new FailedWorkItemTrackingClient(
            new VssServiceResponseException(HttpStatusCode.Forbidden, "private attachment response", null)));

        WorkItemSourceException exception = await Assert.ThrowsAsync<WorkItemSourceException>(() =>
            source.GetAttachmentContentAsync(
                "Payments",
                Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a"),
                "image.png",
                CancellationToken.None));

        Assert.Equal("forbidden", exception.Code);
        Assert.Equal("The PAT cannot read this Azure DevOps resource.", exception.Message);
        Assert.DoesNotContain("private attachment response", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeWorkItemTrackingClient(params CommentList[] pages) : IWorkItemTrackingClient
    {
        private int _pageIndex;

        public List<string?> ContinuationTokens { get; } = [];

        public WorkItem? WorkItem { get; init; }

        public IReadOnlyList<WorkItemField2> Fields { get; init; } = [];

        public List<string> FieldProjects { get; } = [];

        public Task<IReadOnlyList<WorkItemField2>> GetFieldsAsync(string project, CancellationToken cancellationToken)
        {
            FieldProjects.Add(project);
            return Task.FromResult(Fields);
        }

        public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
            IReadOnlyList<int> ids,
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(WorkItem ?? throw new NotSupportedException());

        public Task<CommentList> GetCommentsAsync(
            string project,
            int id,
            string? continuationToken,
            CancellationToken cancellationToken)
        {
            ContinuationTokens.Add(continuationToken);
            return Task.FromResult(pages[_pageIndex++]);
        }

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancelledWorkItemTrackingClient : IWorkItemTrackingClient
    {
        public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
            IReadOnlyList<int> ids,
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken) =>
            Task.FromCanceled<WorkItem>(cancellationToken);

        public Task<IReadOnlyList<WorkItemField2>> GetFieldsAsync(string project, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CommentList> GetCommentsAsync(
            string project,
            int id,
            string? continuationToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailedWorkItemTrackingClient(Exception exception) : IWorkItemTrackingClient
    {
        public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
            IReadOnlyList<int> ids,
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken) =>
            Task.FromException<WorkItem>(exception);

        public Task<IReadOnlyList<WorkItemField2>> GetFieldsAsync(string project, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CommentList> GetCommentsAsync(
            string project,
            int id,
            string? continuationToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) => Task.FromException<Stream>(exception);
    }
}
