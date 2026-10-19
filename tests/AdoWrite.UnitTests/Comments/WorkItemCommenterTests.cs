using AdoRead.Output;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;

namespace AdoWrite.UnitTests.Comments;

public sealed class WorkItemCommenterTests
{
    private readonly FakeWorkItemCommentClient _client = new();
    private readonly FakePeopleDirectory _people = new();

    [Fact]
    public async Task CommentAsync_AddsAMarkdownCommentInTheWorkItemsProject()
    {
        CommentDraft draft = Draft("Which markets does this cover?", mentions: ["reviewer@example.com"]);

        PostedCommentDocument posted = await Commenter().CommentAsync(Fabricated.WorkItemId, draft, CancellationToken.None);

        (string project, int workItemId, string markdown) = Assert.Single(_client.Added);
        Assert.Equal((Fabricated.Project, Fabricated.WorkItemId), (project, workItemId));
        Assert.StartsWith("@<00000000-0000-0000-0000-00000000B002>\n\nWhich markets does this cover?\n\n---\n", markdown, StringComparison.Ordinal);
        Assert.EndsWith($"<!-- ado-write:{posted.Key} -->", markdown, StringComparison.Ordinal);
        Assert.Equal((CommentOutcomes.Created, "workItem", 7, 31), (posted.Outcome, posted.Target, posted.TargetId, posted.CommentId));
        Assert.Null(posted.ThreadId);
        Assert.Equal("https://dev.azure.com/fabricated/Fabricated%20App/_workitems/edit/7", posted.Url!.AbsoluteUri);
        Assert.True(posted.MarkerKept);
    }

    [Fact]
    public async Task CommentAsync_ReportsWhenAzureDevOpsDropsTheMarker()
    {
        _client.Store = text => text[..text.IndexOf("<!--", StringComparison.Ordinal)];

        PostedCommentDocument posted = await Commenter().CommentAsync(Fabricated.WorkItemId, Draft("Q"), CancellationToken.None);

        Assert.False(posted.MarkerKept);
    }

    [Fact]
    public async Task CommentAsync_ReturnsTheEarlierCommentWhenItsKeyIsAlreadyPosted()
    {
        _client.Comments.Add(new WorkItemComment(20, "Old question <!-- ado-write:q1 -->", false));

        PostedCommentDocument posted = await Commenter().CommentAsync(Fabricated.WorkItemId, Draft("Q", key: "q1"), CancellationToken.None);

        Assert.Empty(_client.Added);
        Assert.Equal((CommentOutcomes.Duplicate, 20), (posted.Outcome, posted.CommentId));
    }

    [Fact]
    public async Task CommentAsync_DryRunPostsNothing()
    {
        PostedCommentDocument preview = await Commenter().CommentAsync(Fabricated.WorkItemId, Draft("Q", dryRun: true), CancellationToken.None);

        Assert.Empty(_client.Added);
        Assert.Equal(CommentOutcomes.Preview, preview.Outcome);
        Assert.NotNull(preview.Text);
    }

    [Fact]
    public async Task CommentAsync_TreatsAWorkItemWithoutAProjectAsNotFound()
    {
        _client.Project = null;

        WriteException exception = await Assert.ThrowsAsync<WriteException>(
            () => Commenter().CommentAsync(Fabricated.WorkItemId, Draft("Q"), CancellationToken.None));

        Assert.Equal("workItemNotFound", exception.Code);
        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    private WorkItemCommenter Commenter() => new(_client, _people, Fabricated.Organization);

    private static CommentDraft Draft(string body, string[]? mentions = null, string? key = null, bool dryRun = false) =>
        CommentDraft.Create(body, "Claude Opus 5.5", mentions ?? [], key, dryRun);
}
