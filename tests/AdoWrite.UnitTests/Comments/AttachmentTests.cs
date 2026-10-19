using System.Net;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests.Comments;

public sealed class AttachmentTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ado-write-attachments-").FullName;
    private readonly FakePullRequestCommentClient _pullRequests = new();
    private readonly FakeWorkItemCommentClient _workItems = new();
    private readonly FakePeopleDirectory _people = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ReadAsync_HashesTheFileAndKnowsImages()
    {
        CommentAttachment chart = await File("chart.PNG", "png bytes");
        CommentAttachment notes = await File("notes.pdf", "pdf bytes");

        Assert.Equal(("chart.PNG", 9L), (chart.Name, chart.Length));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("png bytes"u8)), chart.Sha256);
        Assert.True(chart.IsImage);
        Assert.False(notes.IsImage);
        Assert.Equal($"{chart.Sha256[..12]}-chart.PNG", chart.StoredName);
    }

    [Fact]
    public async Task ReadAsync_RejectsMissingAndEmptyFiles()
    {
        string empty = Path.Combine(_directory, "empty.txt");
        await System.IO.File.WriteAllBytesAsync(empty, []);

        WriteException missing = await Assert.ThrowsAsync<WriteException>(
            () => CommentAttachment.ReadAsync(Path.Combine(_directory, "missing.txt"), CancellationToken.None));
        WriteException tooSmall = await Assert.ThrowsAsync<WriteException>(
            () => CommentAttachment.ReadAsync(empty, CancellationToken.None));

        Assert.Equal("attachmentUnreadable", missing.Code);
        Assert.Equal("attachmentTooLarge", tooSmall.Code);
    }

    [Fact]
    public async Task Draft_RejectsTwoFilesWithTheSameNameAndKeysOnTheirContent()
    {
        CommentAttachment first = await File("a.txt", "one");
        string other = Directory.CreateDirectory(Path.Combine(_directory, "other")).FullName;
        await System.IO.File.WriteAllTextAsync(Path.Combine(other, "A.TXT"), "two");
        CommentAttachment second = await CommentAttachment.ReadAsync(Path.Combine(other, "A.TXT"), CancellationToken.None);

        WriteException exception = Assert.Throws<WriteException>(() => Draft(attachments: [first, second]));

        Assert.Equal("invalidAttachments", exception.Code);
        Assert.NotEqual(Draft(attachments: [first]).KeyFor("workItem", 7), Draft(attachments: [second with { Name = "a.txt" }]).KeyFor("workItem", 7));
        Assert.NotEqual(Draft().KeyFor("workItem", 7), Draft(attachments: [first]).KeyFor("workItem", 7));
    }

    [Fact]
    public async Task PullRequest_UploadsUnderHashedNamesAndLinksFilesBeforeTheFooter()
    {
        CommentAttachment chart = await File("chart.png", "png bytes");
        CommentAttachment notes = await File("notes [v2].pdf", "pdf bytes");

        PostedCommentDocument posted = await PullRequestCommenter().CommentAsync(
            Fabricated.PullRequestId,
            null,
            Draft(attachments: [chart, notes]),
            CancellationToken.None);

        Assert.Equal([chart.StoredName, notes.StoredName], _pullRequests.Uploads.Select(upload => upload.Name));
        Assert.Equal("png bytes"u8.ToArray(), _pullRequests.Uploads[0].Content);
        string text = _pullRequests.CreatedThreads[0].Comments[0].Content;
        Uri chartUrl = posted.Attachments[0].Url!;
        Uri notesUrl = posted.Attachments[1].Url!;
        Assert.Contains($"Question?\n\n![chart.png]({chartUrl.AbsoluteUri})\n📎 [notes \\[v2\\].pdf]({notesUrl.AbsoluteUri})\n\n---\n", text, StringComparison.Ordinal);
        Assert.All(posted.Attachments, attachment => Assert.False(attachment.Reused));
        Assert.Equal((chart.Name, chart.Length, chart.Sha256), (posted.Attachments[0].Name, posted.Attachments[0].Length, posted.Attachments[0].Sha256));
    }

    [Fact]
    public async Task PullRequest_ReusesAFileAnInterruptedRunAlreadyUploaded()
    {
        CommentAttachment chart = await File("chart.png", "png bytes");
        _pullRequests.Attachments.Add(new Attachment { DisplayName = chart.StoredName, Url = "https://dev.azure.com/fabricated/earlier/chart.png" });

        PostedCommentDocument posted = await PullRequestCommenter().CommentAsync(
            Fabricated.PullRequestId,
            null,
            Draft(attachments: [chart]),
            CancellationToken.None);

        Assert.Empty(_pullRequests.Uploads);
        AttachmentDocument attachment = Assert.Single(posted.Attachments);
        Assert.True(attachment.Reused);
        Assert.Equal("https://dev.azure.com/fabricated/earlier/chart.png", attachment.Url!.AbsoluteUri);
    }

    [Fact]
    public async Task PullRequest_DryRunAndABadMentionUploadNothing()
    {
        CommentAttachment chart = await File("chart.png", "png bytes");

        PostedCommentDocument preview = await PullRequestCommenter().CommentAsync(
            Fabricated.PullRequestId,
            null,
            Draft(attachments: [chart], dryRun: true),
            CancellationToken.None);
        await Assert.ThrowsAsync<WriteException>(() => PullRequestCommenter().CommentAsync(
            Fabricated.PullRequestId,
            null,
            Draft(attachments: [chart], mentions: ["nobody@example.com"]),
            CancellationToken.None));

        Assert.Empty(_pullRequests.Uploads);
        Assert.Contains("![chart.png](attachment:chart.png)", preview.Text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(preview.Attachments).Url);
    }

    [Fact]
    public async Task WorkItem_UploadsAndAttachesFilesInOneUpdateBeforeTheComment()
    {
        CommentAttachment chart = await File("chart.png", "png bytes");
        CommentAttachment notes = await File("notes.pdf", "pdf bytes");
        CommentDraft draft = Draft(attachments: [chart, notes], key: "refine-it:7:r1");

        PostedCommentDocument posted = await WorkItemCommenter().CommentAsync(Fabricated.WorkItemId, draft, CancellationToken.None);

        Assert.Equal(["chart.png", "notes.pdf"], _workItems.Uploads);
        IReadOnlyList<WorkItemAttachment> update = Assert.Single(_workItems.AttachmentUpdates);
        Assert.Equal(["chart.png", "notes.pdf"], update.Select(attachment => attachment.Name));
        Assert.All(update, attachment => Assert.Equal("ado-write:refine-it:7:r1", attachment.Comment));
        string markdown = Assert.Single(_workItems.Added).Markdown;
        Assert.Contains($"![chart.png]({update[0].Url.AbsoluteUri})", markdown, StringComparison.Ordinal);
        Assert.Contains($"📎 [notes.pdf]({update[1].Url.AbsoluteUri})", markdown, StringComparison.Ordinal);
        Assert.Equal(CommentOutcomes.Created, posted.Outcome);
        Assert.Equal(2, posted.Attachments.Count);
    }

    [Fact]
    public async Task WorkItem_RetryAfterAFailedCommentReusesTheAttachedFiles()
    {
        CommentAttachment chart = await File("chart.png", "png bytes");
        CommentDraft draft = Draft(attachments: [chart], key: "refine-it:7:r1");
        _workItems.AddCommentFailure = new VssServiceResponseException(HttpStatusCode.ServiceUnavailable, "Down.", null);

        await Assert.ThrowsAsync<WriteException>(() => WorkItemCommenter().CommentAsync(Fabricated.WorkItemId, draft, CancellationToken.None));
        _workItems.AddCommentFailure = null;
        PostedCommentDocument retry = await WorkItemCommenter().CommentAsync(Fabricated.WorkItemId, draft, CancellationToken.None);

        Assert.Single(_workItems.Uploads);
        Assert.Single(_workItems.AttachmentUpdates);
        Assert.True(Assert.Single(retry.Attachments).Reused);
        Assert.Equal(CommentOutcomes.Created, retry.Outcome);
    }

    [Fact]
    public async Task WorkItem_DoesNotReuseAFileAttachedForAnotherPost()
    {
        CommentAttachment chart = await File("chart.png", "png bytes");
        _workItems.Attached.Add(new WorkItemAttachment(new Uri("https://dev.azure.com/fabricated/_apis/wit/attachments/old"), "chart.png", "ado-write:other-key"));

        PostedCommentDocument posted = await WorkItemCommenter().CommentAsync(
            Fabricated.WorkItemId,
            Draft(attachments: [chart], key: "refine-it:7:r2"),
            CancellationToken.None);

        Assert.Single(_workItems.Uploads);
        Assert.False(Assert.Single(posted.Attachments).Reused);
    }

    private async Task<CommentAttachment> File(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        await System.IO.File.WriteAllTextAsync(path, content);
        return await CommentAttachment.ReadAsync(path, CancellationToken.None);
    }

    private PullRequestCommenter PullRequestCommenter() => new(_pullRequests, _people, Fabricated.Organization);

    private WorkItemCommenter WorkItemCommenter() => new(_workItems, _people, Fabricated.Organization);

    private static CommentDraft Draft(
        IReadOnlyList<CommentAttachment>? attachments = null,
        string[]? mentions = null,
        string? key = null,
        bool dryRun = false) =>
        CommentDraft.Create("Question?", "Claude Opus 5.5", mentions ?? [], key, dryRun, attachments);
}
