using AdoWrite.AzureDevOps;

namespace AdoWrite.Comments;

/// <summary>
/// Posts a Markdown comment to a work item's discussion. Work-item comments are not threaded, so a
/// conversation continues with further comments; each first looks for an earlier one of the signed-in
/// user with the same key. Attached files are uploaded, added to the work item's Attachments with
/// the key as their attachment comment, and linked from the comment.
/// </summary>
internal sealed class WorkItemCommenter(IWorkItemCommentClient client, IPeopleDirectory people, Uri organizationUrl)
{
    private const string Target = "workItem";

    private readonly CommentComposer _composer = new(people);

    public async Task<PostedCommentDocument> CommentAsync(int workItemId, CommentDraft draft, CancellationToken cancellationToken)
    {
        (string project, IReadOnlyList<WorkItemAttachment> attached) = await FindAsync(workItemId, cancellationToken);
        string key = draft.KeyFor(Target, workItemId);
        Person author = await _composer.GetAuthorAsync(cancellationToken);
        IReadOnlyList<WorkItemComment> comments = await AzureDevOpsCall.RunAsync(
            () => client.GetCommentsAsync(project, workItemId, cancellationToken),
            cancellationToken);
        WorkItemComment? earlier = comments.FirstOrDefault(comment =>
            !comment.IsDeleted && comment.AuthorId == author.Id && CommentMarker.Marks(comment.Text, key));
        if (earlier is not null)
        {
            return Result(CommentOutcomes.Duplicate, workItemId, project, earlier.Id, key);
        }

        ComposedComment comment = await _composer.ComposeAsync(
            draft,
            key,
            author,
            () => AttachAsync(workItemId, project, attached, draft.Attachments, key, cancellationToken),
            cancellationToken);
        if (draft.DryRun)
        {
            return Result(CommentOutcomes.Preview, workItemId, project, null, key, comment);
        }

        WorkItemComment created = await AzureDevOpsCall.RunAsync(
            () => client.AddCommentAsync(project, workItemId, comment.Text, cancellationToken),
            cancellationToken);
        return Result(
            CommentOutcomes.Created,
            workItemId,
            project,
            created.Id,
            key,
            comment,
            markerKept: CommentMarker.Marks(created.Text, key));
    }

    private async Task<(string Project, IReadOnlyList<WorkItemAttachment> Attached)> FindAsync(
        int workItemId,
        CancellationToken cancellationToken)
    {
        WriteException notFound = WriteException.NotFound(
            "workItemNotFound",
            $"Work item {workItemId} was not found or the token cannot read it.");
        WorkItemTarget workItem = await AzureDevOpsCall.RunAsync(
            () => client.GetWorkItemAsync(workItemId, cancellationToken),
            cancellationToken,
            notFound);
        return string.IsNullOrWhiteSpace(workItem.Project) ? throw notFound : (workItem.Project, workItem.Attachments);
    }

    /// <summary>
    /// Uploads the files and adds them to the work item in one update before the comment is posted.
    /// A file an earlier, interrupted run already attached under the same key is reused, so a retry
    /// neither uploads nor attaches it twice.
    /// </summary>
    private async Task<IReadOnlyList<AttachmentLink>> AttachAsync(
        int workItemId,
        string project,
        IReadOnlyList<WorkItemAttachment> attached,
        IReadOnlyList<CommentAttachment> files,
        string key,
        CancellationToken cancellationToken)
    {
        string tag = CommentMarker.AttachmentTag(key);
        var links = new List<AttachmentLink>(files.Count);
        var added = new List<WorkItemAttachment>();
        foreach (CommentAttachment file in files)
        {
            WorkItemAttachment? earlier = attached.FirstOrDefault(attachment =>
                attachment.Comment == tag && string.Equals(attachment.Name, file.Name, StringComparison.Ordinal));
            if (earlier is not null)
            {
                links.Add(new AttachmentLink(file, earlier.Url, Reused: true));
                continue;
            }

            Uri url = await AzureDevOpsCall.RunAsync(
                async () =>
                {
                    await using Stream content = file.Open();
                    return await client.UploadAttachmentAsync(project, file.Name, content, cancellationToken);
                },
                cancellationToken);
            added.Add(new WorkItemAttachment(url, file.Name, tag));
            links.Add(new AttachmentLink(file, url, Reused: false));
        }

        if (added.Count > 0)
        {
            await AzureDevOpsCall.RunAsync(() => client.AddAttachmentsAsync(workItemId, added, cancellationToken), cancellationToken);
        }

        return links;
    }

    private PostedCommentDocument Result(
        string outcome,
        int workItemId,
        string project,
        int? commentId,
        string key,
        ComposedComment? comment = null,
        bool? markerKept = null) =>
        new(
            outcome,
            Target,
            workItemId,
            ThreadId: null,
            commentId,
            new Uri(organizationUrl, $"{Uri.EscapeDataString(project)}/_workitems/edit/{workItemId}"),
            Anchor: null,
            comment?.Mentions ?? [],
            comment?.Attachments ?? [],
            key,
            markerKept,
            outcome == CommentOutcomes.Preview ? comment?.Text : null);
}
