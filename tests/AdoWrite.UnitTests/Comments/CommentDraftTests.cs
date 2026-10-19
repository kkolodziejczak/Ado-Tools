using AdoWrite.Comments;

namespace AdoWrite.UnitTests.Comments;

public sealed class CommentDraftTests
{
    [Theory]
    [InlineData("  ", "Claude Opus 5.5", null, "bodyRequired")]
    [InlineData("Question <!-- ado-write:abc -->", "Claude Opus 5.5", null, "invalidBody")]
    [InlineData("Question", "", null, "invalidAgent")]
    [InlineData("Question", "Claude\nOpus", null, "invalidAgent")]
    [InlineData("Question", "<b>Claude</b>", null, "invalidAgent")]
    [InlineData("Question", "Claude Opus 5.5", "has space", "invalidKey")]
    [InlineData("Question", "Claude Opus 5.5", "--> injected", "invalidKey")]
    public void Create_RejectsInvalidInputBeforeAnyCall(string body, string agent, string? key, string expectedCode)
    {
        WriteException exception = Assert.Throws<WriteException>(() => CommentDraft.Create(body, agent, [], key, dryRun: false));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void Create_RejectsTooManyOrBlankSeparatedMentions()
    {
        string[] many = Enumerable.Range(0, 21).Select(index => $"person{index}@example.com").ToArray();

        Assert.Equal("invalidMention", Assert.Throws<WriteException>(() => CommentDraft.Create("Q", "Agent", many, null, false)).Code);
        Assert.Equal("invalidMention", Assert.Throws<WriteException>(() => CommentDraft.Create("Q", "Agent", ["a b@example.com"], null, false)).Code);
    }

    [Fact]
    public void Create_NormalizesBodyAgentAndMentions()
    {
        CommentDraft draft = CommentDraft.Create("\r\n Question?\r\nSecond line \r\n", " Claude Opus 5.5 ", ["a@example.com", " A@example.com ", ""], null, false);

        Assert.Equal(" Question?\nSecond line", draft.Body);
        Assert.Equal("Claude Opus 5.5", draft.Agent);
        Assert.Equal(["a@example.com"], draft.Mentions);
    }

    [Fact]
    public void KeyFor_IsStableForTheSameTargetAndContentAndChangesWithEither()
    {
        CommentDraft draft = CommentDraft.Create("Question?", "Agent", [], null, false);
        CommentDraft other = CommentDraft.Create("Another question?", "Agent", [], null, false);

        string key = draft.KeyFor("pullRequest", 42);

        Assert.Matches("^[0-9a-f]{16}$", key);
        Assert.Equal(key, CommentDraft.Create("Question?", "Other agent", [], null, true).KeyFor("pullRequest", 42));
        Assert.NotEqual(key, draft.KeyFor("pullRequest", 43));
        Assert.NotEqual(key, other.KeyFor("pullRequest", 42));
    }

    [Fact]
    public void Create_KeepsTheIndentationOfTheFirstLine()
    {
        CommentDraft draft = CommentDraft.Create("\n\n    var retries = 2;\n\nWhy two?  \n", "Agent", [], null, false);

        Assert.Equal("    var retries = 2;\n\nWhy two?", draft.Body);
    }

    [Fact]
    public void KeyFor_KeepsMentionsAndBodyApart()
    {
        CommentDraft mention = CommentDraft.Create("Question?", "Agent", ["a@example.com"], null, false);
        CommentDraft inline = CommentDraft.Create("a@example.com\nQuestion?", "Agent", [], null, false);

        Assert.NotEqual(mention.KeyFor("workItem", 7), inline.KeyFor("workItem", 7));
    }

    [Fact]
    public void KeyFor_PrefersTheCallerKey()
    {
        CommentDraft draft = CommentDraft.Create("Question?", "Agent", [], "refine-it:123:r2", false);

        Assert.Equal("refine-it:123:r2", draft.KeyFor("workItem", 7));
    }
}
