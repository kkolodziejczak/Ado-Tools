using AdoRead.AzureDevOps;
using AdoRead.Output;

namespace AdoRead.PullRequests;

public sealed class PullRequestReader(
    IProjectCatalog projectCatalog,
    IPullRequestSource source,
    Uri organizationUrl,
    int maxParallelProjects,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Resolves a pull request without the caller naming its project. An organization-scoped
    /// lookup answers the common case in one call; anything it cannot answer falls back to
    /// scanning every accessible project, which stays the authoritative path.
    /// </summary>
    public async Task<SearchEnvelope<PullRequestDocument>> ReadAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var query = new PullRequestQuery(id, organizationUrl);
        PullRequestDocument? direct = await source.TryGetByIdAsync(id, cancellationToken);
        if (direct is not null)
        {
            try
            {
                PullRequestDocument document = await source.GetAllDetailsAsync(direct, cancellationToken);
                return CreateEnvelope(query, "success", [document], []);
            }
            catch (PullRequestSourceException exception)
            {
                return CreateEnvelope(
                    query,
                    "error",
                    [],
                    [new SearchError(
                        "azureDevOps",
                        direct.Project.Name,
                        exception.Code,
                        exception.Message,
                        exception.Transient)]);
            }
        }

        return await ScanEveryProjectAsync(query, id, cancellationToken);
    }

    private async Task<SearchEnvelope<PullRequestDocument>> ScanEveryProjectAsync(
        PullRequestQuery query,
        int id,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AdoProject> projects;
        try
        {
            projects = await projectCatalog.GetAllAsync(cancellationToken);
        }
        catch (ProjectCatalogException exception)
        {
            return CreateEnvelope(
                query,
                "error",
                [],
                [new SearchError("azureDevOps", null, exception.Code, exception.Message, exception.Transient)]);
        }

        using var concurrency = new SemaphoreSlim(maxParallelProjects, maxParallelProjects);
        ProjectProbe[] probes = await Task.WhenAll(projects.Select(project =>
            ProbeAsync(project, id, concurrency, cancellationToken)));
        PullRequestDocument[] results = probes
            .Where(probe => probe.Document is not null)
            .Select(probe => probe.Document!)
            .OrderBy(document => document.Project.Name, StringComparer.Ordinal)
            .ThenBy(document => document.Repository.Name, StringComparer.Ordinal)
            .ThenBy(document => document.Id)
            .ToArray();
        SearchError[] errors = probes
            .Where(probe => probe.Error is not null)
            .Select(probe => probe.Error!)
            .OrderBy(error => error.Project, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)
            .ToArray();
        string status = GetStatus(results.Length, errors.Length);
        return CreateEnvelope(query, status, results, errors);
    }

    private async Task<ProjectProbe> ProbeAsync(
        AdoProject project,
        int id,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        bool acquired = false;
        try
        {
            await concurrency.WaitAsync(cancellationToken);
            acquired = true;
            PullRequestDocument? pullRequest = await source.TryGetAsync(project, id, cancellationToken);
            if (pullRequest is null)
            {
                return new ProjectProbe(null, null);
            }

            return new ProjectProbe(await source.GetAllDetailsAsync(pullRequest, cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProjectProbe(
                null,
                new SearchError(
                    "project",
                    project.Name,
                    "cancelled",
                    "The project search was cancelled before it completed.",
                    true));
        }
        catch (PullRequestSourceException exception)
        {
            return new ProjectProbe(
                null,
                new SearchError("project", project.Name, exception.Code, exception.Message, exception.Transient));
        }
        finally
        {
            if (acquired)
            {
                concurrency.Release();
            }
        }
    }

    private SearchEnvelope<PullRequestDocument> CreateEnvelope(
        PullRequestQuery query,
        string status,
        IReadOnlyList<PullRequestDocument> results,
        IReadOnlyList<SearchError> errors) =>
        new(
            "2.0",
            "pullRequestSearch",
            status,
            query,
            timeProvider.GetUtcNow(),
            results,
            errors);

    private static string GetStatus(int resultCount, int errorCount)
    {
        if (resultCount > 0)
        {
            return errorCount > 0 ? "partial" : "success";
        }

        return errorCount > 0 ? "error" : "notFound";
    }

    private sealed record ProjectProbe(PullRequestDocument? Document, SearchError? Error);
}
