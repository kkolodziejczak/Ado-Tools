using System.Text.Json.Serialization;

namespace AdoWrite.Comments;

/// <summary>
/// The result of one comment command. <see cref="Outcome"/> is <c>created</c>, <c>duplicate</c> (an
/// earlier comment carries the same key, so nothing was posted) or <c>preview</c> (a dry run). Only a
/// preview carries <see cref="Text"/>. <see cref="Attachments"/> lists the uploaded files; a reused one
/// was already uploaded with the same name and content, for example by a run that did not finish.
/// <see cref="MarkerKept"/> is false when Azure DevOps dropped the
/// hidden marker, so a retry could not detect this comment.
/// </summary>
public sealed record PostedCommentDocument(
    string Outcome,
    string Target,
    int TargetId,
    int? ThreadId,
    int? CommentId,
    Uri? Url,
    CommentAnchorDocument? Anchor,
    IReadOnlyList<MentionDocument> Mentions,
    IReadOnlyList<AttachmentDocument> Attachments,
    string Key,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MarkerKept = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null);

/// <summary>The lines a pull-request comment is attached to. <see cref="Side"/> is <c>right</c> (new) or <c>left</c> (old).</summary>
public sealed record CommentAnchorDocument(string FilePath, string Side, int StartLine, int EndLine, int IterationId);

public sealed record MentionDocument(string Input, Guid Id);

public static class CommentOutcomes
{
    public const string Created = "created";
    public const string Duplicate = "duplicate";
    public const string Preview = "preview";
}

public static class CommentKinds
{
    public const string PullRequestComment = "pullRequestComment";
    public const string PullRequestReply = "pullRequestReply";
    public const string WorkItemComment = "workItemComment";
}
