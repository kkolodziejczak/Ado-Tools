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
