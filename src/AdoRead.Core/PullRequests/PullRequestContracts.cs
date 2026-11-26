namespace AdoRead.PullRequests;

public sealed record PullRequestDocument(
    int Id,
    string? Title,
    string? Description,
    string? Status,
    string? MergeStatus,
    bool IsDraft,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    Uri OrganizationUrl,
    PullRequestProjectDocument Project,
    PullRequestRepositoryDocument Repository,
    string? SourceRefName,
    string? TargetRefName,
    string? SourceCommitId,
    string? TargetCommitId,
    string? MergeCommitId,
    string? CreatorUniqueName,
    IReadOnlyList<PullRequestReviewerDocument> Reviewers,
    IReadOnlyList<PullRequestWorkItemDocument> WorkItems,
    IReadOnlyList<PullRequestThreadDocument> Threads,
    PullRequestLinksDocument Links);

public sealed record PullRequestProjectDocument(Guid Id, string Name, Uri? Rest);

public sealed record PullRequestRepositoryDocument(Guid Id, string Name, Uri? Rest, Uri? Browser);

public sealed record PullRequestReviewerDocument(
    string? UniqueName,
    int Vote,
    string ReviewStatus,
    bool IsRequired,
    bool IsGroup,
    bool? HasDeclined);

public sealed record PullRequestWorkItemDocument(int? Id, Uri? Url);

public sealed record PullRequestThreadDocument(
    int Id,
    string? Status,
    bool? IsDeleted,
    bool? IsPossiblyOutdated,
    string? FilePath,
    int? StartLine,
    int? EndLine,
    string? ReviewedCommitId,
    string? LatestCommitId,
    IReadOnlyList<PullRequestCommentDocument> Comments);

public sealed record PullRequestCommentDocument(
    int Id,
    int ParentId,
    string Classification,
    string? AuthorUniqueName,
    string? Content,
    DateTimeOffset? PublishedAtUtc,
    bool? IsDeleted);

public sealed record PullRequestLinksDocument(Uri? Rest, Uri? Browser);

public sealed record PullRequestQuery(int Id, Uri OrganizationUrl);

/// <summary>
/// What changed on a pull request since an agent last looked, without any text: its branch-policy
/// checks and, per thread with comments from people, the status, comment count and last update.
/// </summary>
public sealed record PullRequestActivityDocument(
    IReadOnlyList<PullRequestCheckDocument> Checks,
    IReadOnlyList<PullRequestThreadActivityDocument> Threads);

/// <summary>One branch-policy evaluation. <see cref="IsExpired"/> and <see cref="BuildId"/> are false and null when Azure DevOps leaves them out.</summary>
public sealed record PullRequestCheckDocument(
    Guid EvaluationId,
    string Name,
    string Type,
    string Status,
    bool IsExpired,
    bool IsBlocking,
    int? BuildId);

public sealed record PullRequestThreadActivityDocument(int Id, string? Status, int CommentCount, DateTimeOffset? LastUpdatedAtUtc);