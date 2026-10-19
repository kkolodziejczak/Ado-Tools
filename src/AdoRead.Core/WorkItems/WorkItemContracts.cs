using System.Text.Json;
using System.Text.Json.Serialization;
using AdoRead.Output;
using AdoRead.Text;

namespace AdoRead.WorkItems;

public sealed record WorkItemDocument(
    int Id,
    int Revision,
    string? Type,
    string? Title,
    string? State,
    string? Reason,
    decimal? Priority,
    string? Severity,
    Uri OrganizationUrl,
    string? Project,
    string? AreaPath,
    string? IterationPath,
    RichTextDocument Description,
    RichTextDocument AcceptanceCriteria,
    string? AssignedToUniqueName,
    string? CreatedByUniqueName,
    string? ChangedByUniqueName,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ChangedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    IReadOnlyList<string> Tags,
    IReadOnlyList<WorkItemRelationDocument> Relations,
    IReadOnlyList<WorkItemCommentDocument> Comments,
    IReadOnlyDictionary<string, JsonElement> CustomFields,
    WorkItemLinksDocument Links,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkItemMediaDocument? Media = null);

public sealed record WorkItemRelationDocument(
    string? RelationType,
    string Category,
    Uri? TargetUrl,
    int? TargetId,
    IReadOnlyDictionary<string, JsonElement> Attributes);

public sealed record WorkItemCommentDocument(
    int Id,
    int Version,
    string? CreatedByUniqueName,
    string? ModifiedByUniqueName,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    bool? IsDeleted,
    string? SourceFormat,
    string? RawText,
    string? RenderedHtml,
    string? PlainText,
    IReadOnlyList<CommentMentionDocument> Mentions,
    IReadOnlyList<CommentReactionDocument> Reactions,
    Uri? Url);

public sealed record CommentMentionDocument(
    string? ArtifactId,
    string? ArtifactType,
    string? TargetId,
    Uri? Url);

public sealed record CommentReactionDocument(
    string? Type,
    int Count,
    bool? IsCurrentUserEngaged);

public sealed record WorkItemLinksDocument(
    Uri? Rest,
    Uri? Browser);

public sealed record WorkItemQuery(
    int Id,
    Uri OrganizationUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DownloadImagesDirectory = null);

public sealed record WorkItemMediaDocument(
    string OutputDirectory,
    WorkItemMediaLimits Limits,
    IReadOnlyList<WorkItemMediaItem> Items);

public sealed record WorkItemMediaLimits(
    int MaximumCandidates,
    long MaximumBytesPerImage,
    long MaximumTotalBytes);

public sealed record WorkItemMediaItem(
    string? AttachmentId,
    string? OriginalFileName,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Format,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContentType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ByteLength,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocalPath,
    IReadOnlyList<WorkItemMediaSource> Sources,
    WorkItemMediaError? Error);

public sealed record WorkItemMediaSource(
    string Kind,
    int? CommentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null);

public sealed record WorkItemMediaError(
    string Code,
    string Message,
    bool Transient,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OmittedCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OmittedSourceCount = null);
