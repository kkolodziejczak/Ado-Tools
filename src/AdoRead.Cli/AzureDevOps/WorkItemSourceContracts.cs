using AdoRead.WorkItems;

namespace AdoRead.AzureDevOps;

public interface IWorkItemSource
{
    Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
        string project,
        int id,
        CancellationToken cancellationToken);

    Task<Stream> GetAttachmentContentAsync(
        string project,
        Guid attachmentId,
        string fileName,
        CancellationToken cancellationToken);
}

/// <summary>
/// A work item as Azure DevOps returns it: an untyped field bag that
/// <see cref="WorkItemMapper"/> projects onto <see cref="WorkItemDocument"/>.
/// </summary>
public sealed record SourceWorkItem(
    int Id,
    int Revision,
    Uri? RestUrl,
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<SourceWorkItemRelation> Relations);

public sealed record SourceWorkItemRelation(
    string? RelationType,
    Uri? TargetUrl,
    IReadOnlyDictionary<string, object?> Attributes);

public sealed class WorkItemSourceException(
    string code,
    string message,
    bool transient,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Transient { get; } = transient;
}
