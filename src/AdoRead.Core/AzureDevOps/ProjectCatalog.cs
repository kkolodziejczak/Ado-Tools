using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public sealed record ProjectPage(
    IReadOnlyList<AdoProject> Projects,
    string? ContinuationToken);

public interface IProjectClient
{
    Task<ProjectPage> GetProjectsAsync(string? continuationToken, CancellationToken cancellationToken);
}

public sealed class ProjectCatalog(IProjectClient client) : IProjectCatalog
{
    public async Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var projects = new List<AdoProject>();
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            string? continuationToken = null;
            do
            {
                ProjectPage page = await client.GetProjectsAsync(continuationToken, cancellationToken);
                projects.AddRange(page.Projects);
                continuationToken = string.IsNullOrWhiteSpace(page.ContinuationToken)
                    ? null
                    : page.ContinuationToken;
                if (continuationToken is not null && !seenTokens.Add(continuationToken))
                {
                    throw new ProjectCatalogException(
                        "projectPaginationLoop",
                        "Azure DevOps repeated a project continuation token.",
                        transient: false);
                }
            }
            while (continuationToken is not null);

            return projects
                .OrderBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.Id)
                .ToArray();
        }
        catch (ProjectCatalogException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.ProjectFailure(exception);
        }
    }
}

public sealed class SdkProjectClient(ProjectHttpClient client) : IProjectClient
{
    private const int PageSize = 100;

    public async Task<ProjectPage> GetProjectsAsync(
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<IPagedList<TeamProjectReference>> request = client.GetProjects(
            stateFilter: null,
            top: PageSize,
            skip: null,
            userState: null,
            continuationToken);
        IPagedList<TeamProjectReference> page = await request.WaitAsync(cancellationToken);
        return new ProjectPage(
            page.Select(project => new AdoProject(
                project.Id,
                project.Name,
                ToUri(project.Url)))
                .ToArray(),
            page.ContinuationToken);
    }

    private static Uri? ToUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
}
