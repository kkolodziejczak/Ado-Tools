using Microsoft.TeamFoundation.Core.WebApi;

namespace AdoRead.AzureDevOps;

public sealed record AdoTeam(Guid Id, string Name, string? Description);

public interface ITeamClient
{
    /// <summary>
    /// When <paramref name="mine"/> is true only teams the caller belongs to are returned, which
    /// is almost always the useful set when picking a team to read a sprint from.
    /// </summary>
    Task<IReadOnlyList<AdoTeam>> GetTeamsAsync(string project, bool mine, CancellationToken cancellationToken);
}

public sealed class SdkTeamClient(TeamHttpClient client) : ITeamClient
{
    public async Task<IReadOnlyList<AdoTeam>> GetTeamsAsync(
        string project,
        bool mine,
        CancellationToken cancellationToken)
    {
        List<WebApiTeam> teams = await client.GetTeamsAsync(
            project,
            mine,
            top: null,
            skip: null,
            expandIdentity: null,
            userState: null,
            cancellationToken);
        return teams
            .Select(team => new AdoTeam(team.Id, team.Name, team.Description))
            .OrderBy(team => team.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
