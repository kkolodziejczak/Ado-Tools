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
}

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
