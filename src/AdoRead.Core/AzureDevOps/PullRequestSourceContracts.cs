using AdoRead.PullRequests;

namespace AdoRead.AzureDevOps;

public sealed record AdoProject(Guid Id, string Name, Uri? Url);

public interface IProjectCatalog
{
    Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads pull requests from Azure DevOps as finished <see cref="PullRequestDocument"/> values.
/// Lookup is two phase so that the reader can probe every project cheaply and only pay for the
/// expensive detail calls on the projects that actually hold the pull request.
/// </summary>
public interface IPullRequestSource
{
    /// <summary>
    /// Best-effort organization-scoped lookup that resolves the project itself. Null means
    /// "could not answer" — the caller falls back to scanning every accessible project.
    /// </summary>
    Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken);

    Task<PullRequestDocument?> TryGetAsync(AdoProject project, int id, CancellationToken cancellationToken);

    Task<PullRequestDocument> GetAllDetailsAsync(
        PullRequestDocument pullRequest,
        CancellationToken cancellationToken);

    /// <summary>One page of a project's pull requests, newest first, filtered by Azure DevOps on <paramref name="search"/>.</summary>
    Task<IReadOnlyList<PullRequestCandidate>> ListAsync(
        AdoProject project,
        PullRequestSearch search,
        int skip,
        int top,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>What Azure DevOps filters on itself: a status, and the identity IDs of a creator or reviewer.</summary>
public sealed record PullRequestSearch(string Status, Guid? CreatorId, Guid? ReviewerId);

/// <summary>A person as Azure DevOps names them; kept beside the document so a filter can match by ID or name.</summary>
public sealed record PullRequestPerson(Guid? Id, string? UniqueName, string? DisplayName);

public sealed record PullRequestReviewerVote(PullRequestPerson Reviewer, int Vote);

/// <summary>A listed pull request: its summary document, and its people with the IDs the document leaves out.</summary>
public sealed record PullRequestCandidate(
    PullRequestDocument Document,
    PullRequestPerson Creator,
    IReadOnlyList<PullRequestReviewerVote> Reviewers);

public class PullRequestSourceException(
    string code,
    string message,
    bool transient,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Transient { get; } = transient;
}

public sealed class ProjectCatalogException(
    string code,
    string message,
    bool transient,
    Exception? innerException = null) : PullRequestSourceException(code, message, transient, innerException);
