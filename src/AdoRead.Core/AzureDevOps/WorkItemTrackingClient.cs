using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace AdoRead.AzureDevOps;

public interface IWorkItemTrackingClient
{
    Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a WIQL query in a project (name or ID), or across the organization when it is null, and
    /// returns the matching references, not their fields.
    /// </summary>
    Task<WorkItemQueryResult> QueryByWiqlAsync(string wiql, string? project, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>
    /// Batched lookup restricted to the named fields. Used for sprint summaries, where one call
    /// per work item would be wasteful and no rich text is needed.
    /// </summary>
    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken);

    /// <summary>The project's field definitions; used to tell rich-text fields from plain ones.</summary>
    Task<IReadOnlyList<WorkItemField2>> GetFieldsAsync(string project, CancellationToken cancellationToken);

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

    /// <summary>
    /// Azure DevOps refuses a query that matches more than this many items unless a top is given, so the
    /// count reported back is "at least" beyond it.
    /// </summary>
    public const int MaxQueryResults = 20000;

    public Task<WorkItemQueryResult> QueryByWiqlAsync(string wiql, string? project, CancellationToken cancellationToken) =>
        project is null
            ? client.QueryByWiqlAsync(
                new Wiql { Query = wiql },
                timePrecision: null,
                top: MaxQueryResults,
                userState: null,
                cancellationToken)
            : client.QueryByWiqlAsync(
                new Wiql { Query = wiql },
                project,
                timePrecision: null,
                top: MaxQueryResults,
                userState: null,
                cancellationToken);

    public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken) =>
        client.GetWorkItemAsync(
            id,
            fields: null,
            asOf: null,
            expand: WorkItemExpand.All,
            userState: null,
            cancellationToken);

    public async Task<IReadOnlyList<WorkItemField2>> GetFieldsAsync(string project, CancellationToken cancellationToken) =>
        await client.GetWorkItemFieldsAsync(project, expand: null, userState: null, cancellationToken);

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
