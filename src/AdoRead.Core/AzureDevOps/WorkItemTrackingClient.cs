using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace AdoRead.AzureDevOps;

public interface IWorkItemTrackingClient
{
    Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Batched lookup restricted to the named fields. Used for sprint summaries, where one call
    /// per work item would be wasteful and no rich text is needed.
    /// </summary>
    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken);

    Task<CommentList> GetCommentsAsync(
        string project,
        int workItemId,
        string? continuationToken,
        CancellationToken cancellationToken);

    Task<Stream> GetAttachmentContentAsync(
        string project,
        Guid attachmentId,
        string fileName,
        CancellationToken cancellationToken);
}

public sealed class SdkWorkItemTrackingClient(WorkItemTrackingHttpClient client) : IWorkItemTrackingClient
{
    private const int CommentPageSize = 200;

    /// <summary>Azure DevOps rejects batches larger than this.</summary>
    private const int WorkItemBatchSize = 200;

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        var items = new List<WorkItem>(ids.Count);
        foreach (int[] batch in ids.Chunk(WorkItemBatchSize))
        {
            items.AddRange(await client.GetWorkItemsAsync(
                batch,
                fields,
                asOf: null,
                expand: null,
                // A deleted or inaccessible ID should thin the sprint, not fail the whole read.
                errorPolicy: WorkItemErrorPolicy.Omit,
                userState: null,
                cancellationToken));
        }

        return items;
    }

    public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken) =>
        client.GetWorkItemAsync(
            id,
            fields: null,
            asOf: null,
            expand: WorkItemExpand.All,
            userState: null,
            cancellationToken);

    public Task<CommentList> GetCommentsAsync(
        string project,
        int workItemId,
        string? continuationToken,
        CancellationToken cancellationToken) =>
        client.GetCommentsAsync(
            project,
            workItemId,
            top: CommentPageSize,
            continuationToken,
            includeDeleted: true,
            expand: CommentExpandOptions.Reactions | CommentExpandOptions.RenderedText,
            order: CommentSortOrder.Asc,
            userState: null,
            cancellationToken);

    public Task<Stream> GetAttachmentContentAsync(
        string project,
        Guid attachmentId,
        string fileName,
        CancellationToken cancellationToken) =>
        client.GetAttachmentContentAsync(
            project,
            attachmentId,
            fileName,
            download: true,
            userState: null,
            cancellationToken);
}
