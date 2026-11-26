using System.Text.Json.Serialization;
using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Privacy;

namespace AdoRead.PullRequests;

/// <summary>
/// The filters of <c>pr list</c>, already checked: <see cref="Status"/> is one of <see cref="Statuses"/>,
/// <see cref="Vote"/> one of <see cref="Votes"/> and only set with a <see cref="Reviewer"/>.
/// <see cref="Author"/> and <see cref="Reviewer"/> are <c>me</c> or a unique or display name.
/// </summary>
public sealed record PullRequestFilter(
    string? Project,
    string? Repository,
    string? Author,
    string? Reviewer,
    string? Vote,
    string Status)
{
    public static IReadOnlyList<string> Statuses { get; } = ["active", "completed", "abandoned", "all"];

    /// <summary>The values <c>ado-write pr vote --vote</c> takes, and the vote Azure DevOps stores for each.</summary>
    public static IReadOnlyDictionary<string, int> Votes { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["approve"] = 10,
        ["approve-with-suggestions"] = 5,
        ["none"] = 0,
        ["wait-for-author"] = -5,
        ["reject"] = -10
    };

    public bool UsesMe => IsMe(Author) || IsMe(Reviewer);

    /// <summary>What Azure DevOps can filter itself: the status, and <c>me</c> by identity ID.</summary>
    public PullRequestSearch ToSearch(Guid? tokenOwnerId) =>
        new(Status, IsMe(Author) ? tokenOwnerId : null, IsMe(Reviewer) ? tokenOwnerId : null);

    /// <summary>Everything, checked again here, because names, repository and vote are not filtered by Azure DevOps.</summary>
    public bool Matches(PullRequestCandidate candidate, Guid? tokenOwnerId) =>
        IsRepository(candidate.Document.Repository)
        && (Author is null || IsPerson(candidate.Creator, Author, tokenOwnerId))
        && (Reviewer is null || candidate.Reviewers.Any(reviewer =>
            IsPerson(reviewer.Reviewer, Reviewer, tokenOwnerId) && (Vote is null || reviewer.Vote == Votes[Vote])));

    public static bool IsMe(string? who) => string.Equals(who, "me", StringComparison.OrdinalIgnoreCase);

    private bool IsRepository(PullRequestRepositoryDocument repository) =>
        Repository is null
        || string.Equals(repository.Name, Repository, StringComparison.OrdinalIgnoreCase)
        || (Guid.TryParse(Repository, out Guid id) && repository.Id == id);

    private static bool IsPerson(PullRequestPerson person, string who, Guid? tokenOwnerId) =>
        IsMe(who)
            ? person.Id is not null && person.Id == tokenOwnerId
            : string.Equals(person.UniqueName, who, StringComparison.OrdinalIgnoreCase)
              || string.Equals(person.DisplayName, who, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One row of <c>pr list</c>: enough to choose which pull requests to read with <c>pr show</c>. With
/// <c>--activity</c> it also carries <see cref="Checks"/> and <see cref="Threads"/>, so an agent that
/// polls can tell what changed since it last looked.
/// </summary>
public sealed record PullRequestListEntry(
    int Id,
    string? Title,
    string? Status,
    string? MergeStatus,
    bool IsDraft,
    DateTimeOffset? CreatedAtUtc,
    string? CreatorUniqueName,
    string Project,
    string Repository,
    string? SourceRefName,
    string? TargetRefName,
    IReadOnlyList<PullRequestReviewerDocument> Reviewers,
    Uri? Browser,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PullRequestCheckDocument>? Checks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PullRequestThreadActivityDocument>? Threads = null);

/// <summary>The request as the output echoes it; a named author or reviewer is aliased when people are withheld.</summary>
public sealed record PullRequestListQuery(
    Uri OrganizationUrl,
    string? Project,
    string? Repository,
    string? Author,
    string? Reviewer,
    string? Vote,
    string Status,
    int Top,
    bool Full);

/// <summary>
/// Lists pull requests across every accessible project, or one. Each project is read page by page,
/// newest first, until it has enough matches, and at most <see cref="MaxScannedPerProject"/> deep;
/// the matches of all projects are then merged newest first.
/// </summary>
public sealed class PullRequestListReader(
    IProjectCatalog projectCatalog,
    IPullRequestSource source,
    Func<CancellationToken, Task<Guid>> tokenOwnerId,
    Uri organizationUrl,
    int maxParallelProjects,
    TimeProvider timeProvider,
    EffectivePrivacy privacy)
{
    public const string ListKind = "pullRequestList";
    public const string FullKind = "pullRequestSearch";
    public const int PageSize = 100;
    public const int MaxScannedPerProject = 1000;

    public async Task<SearchEnvelope<PullRequestListEntry>> ListAsync(
        PullRequestFilter filter,
        int top,
        bool activity,
        CancellationToken cancellationToken)
    {
        PullRequestListQuery query = Echo(filter, top, full: false);
        Found found = await FindAsync(filter, top, cancellationToken);
        PullRequestListEntry[] entries = found.Documents
            .Select(document => ToEntry(PullRequestPrivacy.Apply(document, privacy)))
            .ToArray();
        if (!activity)
        {
            return Envelope(ListKind, "1.0", query, entries, found.Errors, [PrivacyCategory.People]);
        }

        // Two calls per pull request, read up to maxParallelProjects at a time. Checks and threads carry no
        // text and no people, so every privacy level shows them.
        var activityErrors = new SearchError?[entries.Length];
        await Parallel.ForAsync(
            0,
            entries.Length,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelProjects, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                PullRequestDocument document = found.Documents[index];
                try
                {
                    PullRequestActivityDocument read = await source.GetActivityAsync(document, token);
                    entries[index] = entries[index] with { Checks = read.Checks, Threads = read.Threads };
                }
                catch (PullRequestSourceException exception)
                {
                    activityErrors[index] = new SearchError(
                        "azureDevOps",
                        document.Project.Name,
                        exception.Code,
                        $"Pull request {document.Id}: its checks and threads could not be read. {exception.Message}",
                        exception.Transient);
                }
            });
        return Envelope(ListKind, "1.0", query, entries, [.. found.Errors, .. activityErrors.OfType<SearchError>()], [PrivacyCategory.People]);
    }

    private static PullRequestListEntry ToEntry(PullRequestDocument document) =>
        new(
            document.Id,
            document.Title,
            document.Status,
            document.MergeStatus,
            document.IsDraft,
            document.CreatedAtUtc,
            document.CreatorUniqueName,
            document.Project.Name,
            document.Repository.Name,
            document.SourceRefName,
            document.TargetRefName,
            document.Reviewers,
            document.Links.Browser);

    public async Task<SearchEnvelope<PullRequestDocument>> ListFullAsync(
        PullRequestFilter filter,
        int top,
        CancellationToken cancellationToken)
    {
        PullRequestListQuery query = Echo(filter, top, full: true);
        Found found = await FindAsync(filter, top, cancellationToken);
        var details = new PullRequestDocument?[found.Documents.Count];
        var detailErrors = new SearchError?[found.Documents.Count];
        await Parallel.ForAsync(
            0,
            found.Documents.Count,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelProjects, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                PullRequestDocument summary = found.Documents[index];
                try
                {
                    // A listed page shortens the description, so each match is read again on its own, as pr show does.
                    var project = new AdoProject(summary.Project.Id, summary.Project.Name, summary.Project.Rest);
                    PullRequestDocument whole = await source.TryGetAsync(project, summary.Id, token)
                        ?? throw new PullRequestSourceException("pullRequestNotFound", "It matched the list but could not be read.", false);
                    details[index] = PullRequestPrivacy.Apply(await source.GetAllDetailsAsync(whole, token), privacy);
                }
                catch (PullRequestSourceException exception)
                {
                    detailErrors[index] = new SearchError(
                        "azureDevOps",
                        summary.Project.Name,
                        exception.Code,
                        $"Pull request {summary.Id}: {exception.Message}",
                        exception.Transient);
                }
            });

        return Envelope(
            FullKind,
            "2.0",
            query,
            details.OfType<PullRequestDocument>().ToArray(),
            [.. found.Errors, .. detailErrors.OfType<SearchError>()],
            PullRequestPrivacy.Categories);
    }

    private async Task<Found> FindAsync(PullRequestFilter filter, int top, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdoProject> projects;
        Guid? owner = null;
        try
        {
            projects = await projectCatalog.GetAllAsync(cancellationToken);
            if (filter.UsesMe)
            {
                owner = await tokenOwnerId(cancellationToken);
            }
        }
        catch (PullRequestSourceException exception)
        {
            return Found.Failed(new SearchError("azureDevOps", null, exception.Code, exception.Message, exception.Transient));
        }

        if (filter.Project is not null)
        {
            projects = projects
                .Where(project => string.Equals(project.Name, filter.Project, StringComparison.OrdinalIgnoreCase)
                    || (Guid.TryParse(filter.Project, out Guid id) && project.Id == id))
                .ToArray();
            if (projects.Count == 0)
            {
                return Found.Failed(new SearchError(
                    "command",
                    filter.Project,
                    "projectNotFound",
                    "No accessible project has this name or ID.",
                    false));
            }
        }

        using var concurrency = new SemaphoreSlim(maxParallelProjects, maxParallelProjects);
        ProjectScan[] scans = await Task.WhenAll(projects.Select(project =>
            ScanAsync(project, filter, owner, top, concurrency, cancellationToken)));
        PullRequestDocument[] documents = scans
            .SelectMany(scan => scan.Matches)
            .OrderByDescending(document => document.CreatedAtUtc)
            .ThenByDescending(document => document.Id)
            .Take(top)
            .ToArray();
        SearchError[] errors = scans
            .Select(scan => scan.Error)
            .OfType<SearchError>()
            .OrderBy(error => error.Project, StringComparer.Ordinal)
            .ToArray();
        return new Found(documents, errors);
    }

    private async Task<ProjectScan> ScanAsync(
        AdoProject project,
        PullRequestFilter filter,
        Guid? owner,
        int top,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);
        var matches = new List<PullRequestDocument>();
        var seen = new HashSet<int>();
        try
        {
            PullRequestSearch search = filter.ToSearch(owner);
            for (int skip = 0; ; skip += PageSize)
            {
                IReadOnlyList<PullRequestCandidate> page = await source.ListAsync(project, search, skip, PageSize, cancellationToken);
                // Pages are offsets, so a pull request opened meanwhile pushes one already read onto the next page.
                matches.AddRange(page
                    .Where(candidate => seen.Add(candidate.Document.Id) && filter.Matches(candidate, owner))
                    .Select(candidate => candidate.Document));
                if (page.Count < PageSize || matches.Count >= top)
                {
                    return new ProjectScan(matches, null);
                }

                if (skip + PageSize >= MaxScannedPerProject)
                {
                    return new ProjectScan(matches, new SearchError(
                        "project",
                        project.Name,
                        "scanLimitReached",
                        $"Only the newest {MaxScannedPerProject} pull requests of this project were checked. "
                        + "Narrow the list with --status, or --author me / --reviewer me, which Azure DevOps filters itself.",
                        false));
                }
            }
        }
        catch (PullRequestSourceException exception)
        {
            return new ProjectScan(matches, new SearchError("project", project.Name, exception.Code, exception.Message, exception.Transient));
        }
        finally
        {
            concurrency.Release();
        }
    }

    private PullRequestListQuery Echo(PullRequestFilter filter, int top, bool full) =>
        new(
            organizationUrl,
            filter.Project,
            filter.Repository,
            Person(filter.Author),
            Person(filter.Reviewer),
            filter.Vote,
            filter.Status,
            top,
            full);

    private string? Person(string? who) => PullRequestFilter.IsMe(who) ? who : privacy.Person(who);

    private SearchEnvelope<T> Envelope<T>(
        string kind,
        string schemaVersion,
        PullRequestListQuery query,
        IReadOnlyList<T> results,
        IReadOnlyList<SearchError> errors,
        IReadOnlyList<PrivacyCategory> categories) =>
        PrivacyEnvelopes.Describe(
            new SearchEnvelope<T>(
                schemaVersion,
                kind,
                Status(results.Count, errors),
                query,
                timeProvider.GetUtcNow(),
                results,
                errors,
                new PageMetadataDocument(null, query.Top, results.Count, null, null, null)),
            privacy,
            categories);

    /// <summary>A scan cut short read everything it was asked to, so it is partial, not an error, even without a match.</summary>
    private static string Status(int resultCount, IReadOnlyList<SearchError> errors) =>
        errors.Count == 0 ? "success"
        : resultCount > 0 || errors.All(error => error.Code == "scanLimitReached") ? "partial"
        : "error";

    private sealed record ProjectScan(IReadOnlyList<PullRequestDocument> Matches, SearchError? Error);

    private sealed record Found(IReadOnlyList<PullRequestDocument> Documents, IReadOnlyList<SearchError> Errors)
    {
        public static Found Failed(SearchError error) => new([], [error]);
    }
}
