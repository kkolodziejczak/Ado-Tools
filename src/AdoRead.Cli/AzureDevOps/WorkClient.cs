using Microsoft.TeamFoundation.Core.WebApi.Types;
using Microsoft.TeamFoundation.Work.WebApi;

namespace AdoRead.AzureDevOps;

/// <summary>
/// Iterations are team scoped in Azure DevOps: there is no organization-wide "current sprint",
/// so every call here needs a project and a team.
/// </summary>
public interface IWorkClient
{
    Task<IReadOnlyList<TeamSettingsIteration>> GetIterationsAsync(
        string project,
        string team,
        string? timeframe,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> GetIterationWorkItemIdsAsync(
        string project,
        string team,
        Guid iterationId,
        CancellationToken cancellationToken);
}

public sealed class SdkWorkClient(WorkHttpClient client) : IWorkClient
{
    public async Task<IReadOnlyList<TeamSettingsIteration>> GetIterationsAsync(
        string project,
        string team,
        string? timeframe,
        CancellationToken cancellationToken) =>
        await client.GetTeamIterationsAsync(
            new TeamContext(project, team),
            timeframe,
            userState: null,
            cancellationToken);

    /// <summary>
    /// The iteration backlog is returned as parent/child relations. Only the distinct targets
    /// matter here; the hierarchy is not part of the summary view.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetIterationWorkItemIdsAsync(
        string project,
        string team,
        Guid iterationId,
        CancellationToken cancellationToken)
    {
        IterationWorkItems items = await client.GetIterationWorkItemsAsync(
            new TeamContext(project, team),
            iterationId,
            userState: null,
            cancellationToken);
        return (items.WorkItemRelations ?? [])
            .Select(relation => relation.Target?.Id)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
    }
}
