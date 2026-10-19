using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace AdoWrite.AzureDevOps;

/// <summary>The work-item calls a comment needs: the owning project, earlier comments, then the post.</summary>
public interface IWorkItemCommentClient
{
    /// <summary>The project the work item belongs to, or null when Azure DevOps does not say.</summary>
    Task<string?> GetProjectAsync(int workItemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(
        string project,
        int workItemId,
        CancellationToken cancellationToken);

    /// <summary>Adds a Markdown comment, so mentions and the duplicate marker match pull-request comments.</summary>
    Task<WorkItemComment> AddCommentAsync(
        string project,
        int workItemId,
        string markdown,
        CancellationToken cancellationToken);
}

/// <summary>The parts of a work-item comment that duplicate detection and the result need.</summary>
public sealed record WorkItemComment(int Id, string? Text, bool IsDeleted);

public sealed class SdkWorkItemCommentClient(WorkItemTrackingHttpClient client) : IWorkItemCommentClient
{
    private const int CommentPageSize = 200;
    private const string ProjectField = "System.TeamProject";

    public async Task<string?> GetProjectAsync(int workItemId, CancellationToken cancellationToken)
    {
        WorkItem workItem = await client.GetWorkItemAsync(
            workItemId,
            fields: [ProjectField],
            asOf: null,
            expand: null,
            userState: null,
            cancellationToken);
        return workItem.Fields?.TryGetValue(ProjectField, out object? project) == true ? project as string : null;
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

    private static WorkItemComment Map(Comment comment) => new(comment.Id, comment.Text, comment.IsDeleted);
}
