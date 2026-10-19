using AdoRead.Output;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.UnitTests.Comments;

public sealed class PullRequestCommenterTests
{
    private readonly FakePullRequestCommentClient _client = new();
    private readonly FakePeopleDirectory _people = new();

    [Fact]
    public async Task CommentAsync_GeneralCommentOpensAThreadWithMentionsFooterAndMarker()
    {
        CommentDraft draft = Draft("Should the retry be configurable?", mentions: ["reviewer@example.com"]);

        PostedCommentDocument posted = await Commenter().CommentAsync(Fabricated.PullRequestId, null, draft, CancellationToken.None);

        GitPullRequestCommentThread thread = Assert.Single(_client.CreatedThreads);
        Assert.Null(thread.ThreadContext);
        Assert.Null(thread.PullRequestThreadContext);
        Assert.Equal(CommentThreadStatus.Active, thread.Status);
        string key = draft.KeyFor("pullRequest", Fabricated.PullRequestId, null, null, null, null);
        Assert.Equal(
            "@<00000000-0000-0000-0000-00000000B002>\n\n"
            + "Should the retry be configurable?\n\n"
            + "---\n"
            + "🤖 *Posted by AI agent Claude Opus 5.5 on behalf of Example Author*\n\n"
            + $"<!-- ado-write:{key} -->",
            Assert.Single(thread.Comments).Content);
        Assert.Equal(CommentOutcomes.Created, posted.Outcome);
        Assert.Equal("pullRequest", posted.Target);
        Assert.Equal(900, posted.ThreadId);
        Assert.Equal(1, posted.CommentId);
        Assert.Equal(key, posted.Key);
        Assert.True(posted.MarkerKept);
        Assert.Null(posted.Text);
        Assert.Equal(new MentionDocument("reviewer@example.com", Fabricated.Reviewer.Id), Assert.Single(posted.Mentions));
        Assert.Equal(
            "https://dev.azure.com/fabricated/Fabricated%20App/_git/Fabricated.Repo/pullrequest/42?discussionId=900",
            posted.Url!.AbsoluteUri);
    }

    [Fact]
    public async Task CommentAsync_AttachesToNewLinesOfTheLatestIteration()
    {
        var lines = new LineSelection("/src/App.cs", 1, 2, LineSelection.Right);

        PostedCommentDocument posted = await Commenter().CommentAsync(Fabricated.PullRequestId, lines, Draft("Why twice?"), CancellationToken.None);

        GitPullRequestCommentThread thread = Assert.Single(_client.CreatedThreads);
        Assert.Equal("/src/App.cs", thread.ThreadContext.FilePath);
        Assert.Equal((1, 1), (thread.ThreadContext.RightFileStart.Line, thread.ThreadContext.RightFileStart.Offset));
        Assert.Equal((2, "line two is longer".Length + 1), (thread.ThreadContext.RightFileEnd.Line, thread.ThreadContext.RightFileEnd.Offset));
        Assert.Null(thread.ThreadContext.LeftFileStart);
        Assert.Equal(11, thread.PullRequestThreadContext.ChangeTrackingId);
        Assert.Equal(1, thread.PullRequestThreadContext.IterationContext.FirstComparingIteration);
        Assert.Equal(3, thread.PullRequestThreadContext.IterationContext.SecondComparingIteration);
        Assert.Equal([3], _client.ChangeRequests);
        Assert.Equal(new CommentAnchorDocument("/src/App.cs", "right", 1, 2, 3), posted.Anchor);
    }

    [Fact]
    public async Task CommentAsync_AttachesToOldLinesAtTheCommonCommitUnderTheOriginalPath()
    {
        var lines = new LineSelection("/src/Renamed.cs", 2, 2, LineSelection.Left);

        await Commenter().CommentAsync(Fabricated.PullRequestId, lines, Draft("Was this used?"), CancellationToken.None);

        CommentThreadContext context = Assert.Single(_client.CreatedThreads).ThreadContext;
        Assert.Equal("/src/Renamed.cs", context.FilePath);
        Assert.Null(context.RightFileStart);
        Assert.Equal((2, 1), (context.LeftFileStart.Line, context.LeftFileStart.Offset));
        Assert.Equal((2, "original two".Length + 1), (context.LeftFileEnd.Line, context.LeftFileEnd.Offset));
    }

    [Theory]
    [InlineData("/src/Missing.cs", 1, LineSelection.Right, "fileNotInPullRequest")]
    [InlineData("/SRC/App.cs", 1, LineSelection.Right, "fileNotInPullRequest")]
    [InlineData("/src/App.cs", 4, LineSelection.Right, "lineOutOfRange")]
    [InlineData("/src/App.cs", 3, LineSelection.Left, "lineOutOfRange")]
    [InlineData("/src/New.cs", 1, LineSelection.Left, "lineSideUnavailable")]
    public async Task CommentAsync_RejectsLinesThatThePullRequestDoesNotShow(string path, int line, string side, string expectedCode)
    {
        var lines = new LineSelection(path, line, line, side);

        WriteException exception = await Assert.ThrowsAsync<WriteException>(
            () => Commenter().CommentAsync(Fabricated.PullRequestId, lines, Draft("Q"), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exception.ExitCode);
        Assert.Empty(_client.CreatedThreads);
    }

    [Fact]
    public async Task CommentAsync_ReturnsTheEarlierCommentWhenItsKeyIsAlreadyPosted()
    {
        CommentDraft draft = Draft("Should the retry be configurable?");
        PostedCommentDocument first = await Commenter().CommentAsync(Fabricated.PullRequestId, null, draft, CancellationToken.None);
        _client.Threads.Add(new GitPullRequestCommentThread
        {
            Id = 900,
            Comments = [new Comment { Id = 1, Content = _client.CreatedThreads[0].Comments[0].Content }]
        });

        PostedCommentDocument retry = await Commenter().CommentAsync(Fabricated.PullRequestId, null, draft, CancellationToken.None);

        Assert.Single(_client.CreatedThreads);
        Assert.Equal(CommentOutcomes.Duplicate, retry.Outcome);
        Assert.Equal((first.ThreadId, first.CommentId, first.Key), (retry.ThreadId, retry.CommentId, retry.Key));
    }

    [Fact]
    public async Task CommentAsync_IgnoresADeletedEarlierComment()
    {
        CommentDraft draft = Draft("Q", key: "q1");
        _client.Threads.Add(new GitPullRequestCommentThread
        {
            Id = 800,
            Comments = [new Comment { Id = 1, Content = "old <!-- ado-write:q1 -->", IsDeleted = true }]
        });

        PostedCommentDocument posted = await Commenter().CommentAsync(Fabricated.PullRequestId, null, draft, CancellationToken.None);

        Assert.Equal(CommentOutcomes.Created, posted.Outcome);
    }

    [Fact]
    public async Task CommentAsync_DryRunReturnsTheTextAndPostsNothing()
    {
        var lines = new LineSelection("/src/App.cs", 3, 3, LineSelection.Right);

        PostedCommentDocument preview = await Commenter().CommentAsync(Fabricated.PullRequestId, lines, Draft("Q", dryRun: true), CancellationToken.None);

        Assert.Empty(_client.CreatedThreads);
        Assert.Equal(CommentOutcomes.Preview, preview.Outcome);
        Assert.Null(preview.ThreadId);
        Assert.Contains("🤖 *Posted by AI agent Claude Opus 5.5 on behalf of Example Author*", preview.Text, StringComparison.Ordinal);
        Assert.EndsWith($"<!-- ado-write:{preview.Key} -->", preview.Text, StringComparison.Ordinal);
        Assert.Equal(new CommentAnchorDocument("/src/App.cs", "right", 3, 3, 3), preview.Anchor);
    }

    [Fact]
    public async Task CommentAsync_UsesAnIdentityIdMentionWithoutALookup()
    {
        Guid id = Guid.Parse("00000000-0000-0000-0000-00000000c003");

        PostedCommentDocument posted = await Commenter().CommentAsync(
            Fabricated.PullRequestId,
            null,
            Draft("Q", mentions: [id.ToString()]),
            CancellationToken.None);

        Assert.Empty(_people.Searches);
        Assert.StartsWith("@<00000000-0000-0000-0000-00000000C003>\n\n", _client.CreatedThreads[0].Comments[0].Content, StringComparison.Ordinal);
        Assert.Equal(id, Assert.Single(posted.Mentions).Id);
    }

    [Fact]
    public async Task CommentAsync_RejectsAMentionThatMatchesNobodyOrSeveralPeople()
    {
        _people.Matches["team@example.com"] = [Fabricated.Author, Fabricated.Reviewer];

        WriteException missing = await Assert.ThrowsAsync<WriteException>(
            () => Commenter().CommentAsync(Fabricated.PullRequestId, null, Draft("Q", mentions: ["nobody@example.com"]), CancellationToken.None));
        WriteException ambiguous = await Assert.ThrowsAsync<WriteException>(
            () => Commenter().CommentAsync(Fabricated.PullRequestId, null, Draft("Q", mentions: ["team@example.com"]), CancellationToken.None));

        Assert.Equal("mentionNotFound", missing.Code);
        Assert.Equal("mentionAmbiguous", ambiguous.Code);
        Assert.Empty(_client.CreatedThreads);
    }

    [Fact]
    public async Task CommentAsync_MapsAMissingPullRequestToNotFound()
    {
        _client.PullRequestFailure = Fabricated.NotFound();

        WriteException exception = await Assert.ThrowsAsync<WriteException>(
            () => Commenter().CommentAsync(Fabricated.PullRequestId, null, Draft("Q"), CancellationToken.None));

        Assert.Equal("pullRequestNotFound", exception.Code);
        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    [Fact]
    public async Task ReplyAsync_RepliesToTheFirstCommentOfTheThread()
    {
        _client.Threads.Add(new GitPullRequestCommentThread
        {
            Id = 70,
            Comments =
            [
                new Comment { Id = 2, ParentCommentId = 1, Content = "Answer" },
                new Comment { Id = 1, ParentCommentId = 0, Content = "Question" }
            ]
        });

        PostedCommentDocument posted = await Commenter().ReplyAsync(Fabricated.PullRequestId, 70, Draft("Follow-up?"), CancellationToken.None);

        (int threadId, Comment reply) = Assert.Single(_client.CreatedComments);
        Assert.Equal(70, threadId);
        Assert.Equal(1, reply.ParentCommentId);
        Assert.Equal(CommentType.Text, reply.CommentType);
        Assert.Equal((CommentOutcomes.Created, 70, 5), (posted.Outcome, posted.ThreadId, posted.CommentId));
        Assert.EndsWith("pullrequest/42?discussionId=70", posted.Url!.AbsoluteUri, StringComparison.Ordinal);
        Assert.True(posted.MarkerKept);
    }

    [Fact]
    public async Task ReplyAsync_FindsAnEarlierReplyOnlyInTheSameThread()
    {
        CommentDraft draft = Draft("Follow-up?", key: "q2");
        _client.Threads.Add(new GitPullRequestCommentThread { Id = 70, Comments = [new Comment { Id = 1, Content = "Question" }] });
        _client.Threads.Add(new GitPullRequestCommentThread { Id = 71, Comments = [new Comment { Id = 3, Content = "x <!-- ado-write:q2 -->" }] });

        PostedCommentDocument posted = await Commenter().ReplyAsync(Fabricated.PullRequestId, 70, draft, CancellationToken.None);
        _client.Threads[0].Comments.Add(new Comment { Id = 4, Content = "y <!-- ado-write:q2 -->" });
        PostedCommentDocument retry = await Commenter().ReplyAsync(Fabricated.PullRequestId, 70, draft, CancellationToken.None);

        Assert.Equal(CommentOutcomes.Created, posted.Outcome);
        Assert.Equal((CommentOutcomes.Duplicate, 4), (retry.Outcome, retry.CommentId));
        Assert.Single(_client.CreatedComments);
    }

    [Fact]
    public async Task ReplyAsync_RejectsAnUnknownThread()
    {
        WriteException exception = await Assert.ThrowsAsync<WriteException>(
            () => Commenter().ReplyAsync(Fabricated.PullRequestId, 99, Draft("Q"), CancellationToken.None));

        Assert.Equal("threadNotFound", exception.Code);
        Assert.Equal(ExitCodes.NotFound, exception.ExitCode);
    }

    private PullRequestCommenter Commenter() => new(_client, _people, Fabricated.Organization);

    private static CommentDraft Draft(string body, string[]? mentions = null, string? key = null, bool dryRun = false) =>
        CommentDraft.Create(body, "Claude Opus 5.5", mentions ?? [], key, dryRun);
}
