using AdoWrite.AzureDevOps;

namespace AdoWrite.Comments;

/// <summary>
/// Posts a Markdown comment to a work item's discussion. Work-item comments are not threaded, so a
/// conversation continues with further comments; each first looks for an earlier one of the signed-in
/// user with the same key. Attached files are uploaded, added to the work item's Attachments with
/// the key and content hash as their attachment comment, and linked from the comment.
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
    /// Uploads each file and adds it to the work item straight away, before the comment is posted. A
    /// file already attached under the same key with the same content is reused, so a retry neither
    /// uploads nor attaches it twice. Attaching each file on its own means an interrupted run leaves
    /// at most one uploaded file that is not yet attached.
    /// </summary>
    private async Task<IReadOnlyList<AttachmentLink>> AttachAsync(
        int workItemId,
        string project,
        IReadOnlyList<WorkItemAttachment> attached,
        IReadOnlyList<CommentAttachment> files,
        string key,
        CancellationToken cancellationToken)
    {
        var links = new List<AttachmentLink>(files.Count);
        foreach (CommentAttachment file in files)
        {
            string tag = CommentMarker.AttachmentTag(key, file);
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
                    await using Stream content = await file.OpenVerifiedAsync(cancellationToken);
                    return await client.UploadAttachmentAsync(project, file.Name, content, cancellationToken);
                },
                cancellationToken);
            var attachment = new WorkItemAttachment(url, file.Name, tag);
            await AzureDevOpsCall.RunAsync(() => client.AddAttachmentAsync(workItemId, attachment, cancellationToken), cancellationToken);
            links.Add(new AttachmentLink(file, url, Reused: false));
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
