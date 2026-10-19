using AdoWrite.AzureDevOps;

namespace AdoWrite.Comments;

/// <summary>
/// Posts a Markdown comment to a work item's discussion. Work-item comments are not threaded, so a
/// conversation continues with further comments; each first looks for an earlier one of the signed-in
/// user with the same key.
/// </summary>
internal sealed class WorkItemCommenter(IWorkItemCommentClient client, IPeopleDirectory people, Uri organizationUrl)
{
    private const string Target = "workItem";

    private readonly CommentComposer _composer = new(people);

    public async Task<PostedCommentDocument> CommentAsync(int workItemId, CommentDraft draft, CancellationToken cancellationToken)
    {
        string project = await FindProjectAsync(workItemId, cancellationToken);
        string key = draft.KeyFor(Target, workItemId);
        Person author = await _composer.GetAuthorAsync(cancellationToken);
        IReadOnlyList<WorkItemComment> comments = await AzureDevOpsCall.RunAsync(
            () => client.GetCommentsAsync(project, workItemId, cancellationToken),
            cancellationToken);
        WorkItemComment? earlier = comments.FirstOrDefault(comment =>
            !comment.IsDeleted && comment.AuthorId == author.Id && CommentMarker.Marks(comment.Text, key));
        if (earlier is not null)
        {
            return Result(CommentOutcomes.Duplicate, workItemId, project, earlier.Id, [], key);
        }

        ComposedComment comment = await _composer.ComposeAsync(draft, key, author, cancellationToken);
        if (draft.DryRun)
        {
            return Result(CommentOutcomes.Preview, workItemId, project, null, comment.Mentions, key, text: comment.Text);
        }

        WorkItemComment created = await AzureDevOpsCall.RunAsync(
            () => client.AddCommentAsync(project, workItemId, comment.Text, cancellationToken),
            cancellationToken);
        return Result(
            CommentOutcomes.Created,
            workItemId,
            project,
            created.Id,
            comment.Mentions,
            key,
            markerKept: CommentMarker.Marks(created.Text, key));
    }

    private async Task<string> FindProjectAsync(int workItemId, CancellationToken cancellationToken)
    {
        WriteException notFound = WriteException.NotFound(
            "workItemNotFound",
            $"Work item {workItemId} was not found or the token cannot read it.");
        string? project = await AzureDevOpsCall.RunAsync(
            () => client.GetProjectAsync(workItemId, cancellationToken),
            cancellationToken,
            notFound);
        return string.IsNullOrWhiteSpace(project) ? throw notFound : project;
    }

    private PostedCommentDocument Result(
        string outcome,
        int workItemId,
        string project,
        int? commentId,
        IReadOnlyList<MentionDocument> mentions,
        string key,
        bool? markerKept = null,
        string? text = null) =>
        new(
            outcome,
            Target,
            workItemId,
            ThreadId: null,
            commentId,
            new Uri(organizationUrl, $"{Uri.EscapeDataString(project)}/_workitems/edit/{workItemId}"),
            Anchor: null,
            mentions,
            key,
            markerKept,
            text);
}
