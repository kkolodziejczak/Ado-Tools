using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace AdoWrite.AzureDevOps;

/// <summary>
/// The work-item calls a comment needs: the owning project and attached files, earlier comments,
/// file uploads, the link that adds an uploaded file to the work item, then the post.
/// </summary>
public interface IWorkItemCommentClient
{
    /// <summary>The work item's project, or null when Azure DevOps does not say, and its attached files.</summary>
    Task<WorkItemTarget> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(
        string project,
        int workItemId,
        CancellationToken cancellationToken);

    /// <summary>Uploads one file and returns its attachment URL. The file is not yet part of any work item.</summary>
    Task<Uri> UploadAttachmentAsync(
        string project,
        string fileName,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>Adds one uploaded file to the work item's Attachments.</summary>
    Task AddAttachmentAsync(
        int workItemId,
        WorkItemAttachment attachment,
        CancellationToken cancellationToken);

    /// <summary>Adds a Markdown comment, so mentions and the duplicate marker match pull-request comments.</summary>
    Task<WorkItemComment> AddCommentAsync(
        string project,
        int workItemId,
        string markdown,
        CancellationToken cancellationToken);
}

public sealed record WorkItemTarget(string? Project, IReadOnlyList<WorkItemAttachment> Attachments);

/// <summary>An attached file. <see cref="Comment"/> is the attachment comment, which carries the post's key.</summary>
public sealed record WorkItemAttachment(Uri Url, string Name, string? Comment);

/// <summary>The parts of a work-item comment that duplicate detection and the result need.</summary>
public sealed record WorkItemComment(int Id, string? Text, bool IsDeleted, Guid? AuthorId);

public sealed class SdkWorkItemCommentClient(WorkItemTrackingHttpClient client) : IWorkItemCommentClient
{
    private const int CommentPageSize = 200;
    private const string ProjectField = "System.TeamProject";
    private const string AttachedFile = "AttachedFile";

    public async Task<WorkItemTarget> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken)
    {
        WorkItem workItem = await client.GetWorkItemAsync(
            workItemId,
            fields: null,
            asOf: null,
            expand: WorkItemExpand.Relations,
            userState: null,
            cancellationToken);
        string? project = workItem.Fields?.TryGetValue(ProjectField, out object? value) == true ? value as string : null;
        WorkItemAttachment[] attachments = (workItem.Relations ?? [])
            .Where(relation => relation.Rel == AttachedFile && Uri.TryCreate(relation.Url, UriKind.Absolute, out _))
            .Select(relation => new WorkItemAttachment(
                new Uri(relation.Url),
                Attribute(relation, "name") ?? string.Empty,
                Attribute(relation, "comment")))
            .ToArray();
        return new WorkItemTarget(project, attachments);
    }

    public async Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(
        string project,
        int workItemId,
        CancellationToken cancellationToken)
    {
        var comments = new List<WorkItemComment>();
        string? continuationToken = null;
        do
        {
            CommentList page = await client.GetCommentsAsync(
                project,
                workItemId,
                top: CommentPageSize,
                continuationToken,
                includeDeleted: false,
                expand: null,
                order: null,
                userState: null,
                cancellationToken);
            comments.AddRange((page.Comments ?? []).Select(Map));
            continuationToken = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return comments;
    }

    public async Task<Uri> UploadAttachmentAsync(
        string project,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        AttachmentReference reference = await client.CreateAttachmentAsync(
            content,
            project,
            fileName,
            uploadType: null,
            areaPath: null,
            userState: null,
            cancellationToken);
        return new Uri(reference.Url);
    }

    public Task AddAttachmentAsync(
        int workItemId,
        WorkItemAttachment attachment,
        CancellationToken cancellationToken)
    {
        var patch = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new WorkItemRelation
                {
                    Rel = AttachedFile,
                    Url = attachment.Url.AbsoluteUri,
                    Attributes = new Dictionary<string, object>
                    {
                        ["name"] = attachment.Name,
                        ["comment"] = attachment.Comment ?? string.Empty
                    }
                }
            }
        };
        return client.UpdateWorkItemAsync(
            patch,
            workItemId,
            validateOnly: null,
            bypassRules: null,
            suppressNotifications: null,
            expand: null,
            userState: null,
            cancellationToken);
    }

    public async Task<WorkItemComment> AddCommentAsync(
        string project,
        int workItemId,
        string markdown,
        CancellationToken cancellationToken) =>
        Map(await client.AddWorkItemCommentAsync(
            new CommentCreate { Text = markdown },
            project,
            workItemId,
            CommentFormat.Markdown,
            userState: null,
            cancellationToken));

    private static string? Attribute(WorkItemRelation relation, string name) =>
        relation.Attributes?.TryGetValue(name, out object? value) == true ? value as string : null;

    private static WorkItemComment Map(Comment comment) =>
        new(
            comment.Id,
            comment.Text,
            comment.IsDeleted,
            Guid.TryParse(comment.CreatedBy?.Id, out Guid authorId) ? authorId : null);
}
