using AdoRead.WorkItems;

namespace AdoRead.AzureDevOps;

public interface IWorkItemSource
{
    Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>The IDs a flat WIQL query matches, in the query's order.</summary>
    Task<IReadOnlyList<int>> QueryAsync(string wiql, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>The fields a work-item list shows, for each ID, in the given order. A deleted or inaccessible ID is left out.</summary>
    Task<IReadOnlyList<SourceWorkItem>> GetManyAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

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
    IReadOnlyList<SourceWorkItemRelation> Relations)
{
    public static IReadOnlySet<string> NoRichTextFields { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Reference names of the fields whose values are HTML, such as Repro Steps or a custom rich-text field.</summary>
    public IReadOnlySet<string> RichTextFields { get; init; } = NoRichTextFields;
}

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
