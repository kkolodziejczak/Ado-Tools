using AdoRead.AzureDevOps;
using AdoRead.Output;

namespace AdoRead.Sprints;

public sealed class SprintReader(
    ISprintSource source,
    Uri organizationUrl,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Reads one sprint as a shallow overview. <paramref name="sprint"/> is null for the team's
    /// current iteration, otherwise it matches an iteration by name or path.
    /// </summary>
    public async Task<SearchEnvelope<SprintDocument>> ReadAsync(
        string project,
        string team,
        string? sprint,
        IReadOnlyList<string> states,
        CancellationToken cancellationToken)
    {
        var query = new SprintQuery(null, organizationUrl, project, team, sprint, states);
        try
        {
            SprintIterationDocument? iteration = await ResolveIterationAsync(
                project,
                team,
                sprint,
                cancellationToken);
            if (iteration is null)
            {
                return CreateEnvelope(query, "notFound", [], []);
            }

            IReadOnlyList<SprintWorkItemDocument> workItems = await source.GetWorkItemsAsync(
                project,
                team,
                iteration.Id,
                cancellationToken);
            IReadOnlyList<SprintWorkItemDocument> matched = Filter(workItems, states);
            var document = new SprintDocument(
                organizationUrl,
                project,
                team,
                iteration,
                CountByState(matched),
                matched);
            return CreateEnvelope(query, "success", [document], []);
        }
        catch (WorkItemSourceException exception)
        {
            return CreateEnvelope(
                query,
                "error",
                [],
                [new SearchError("azureDevOps", project, exception.Code, exception.Message, exception.Transient)]);
        }
    }

    /// <summary>
    /// Lists a team's iterations so a caller can discover sprint names before reading one.
    /// </summary>
    public async Task<SearchEnvelope<SprintIterationDocument>> ListIterationsAsync(
        string project,
        string team,
        string? timeframe,
        CancellationToken cancellationToken)
    {
        var query = new IterationQuery(organizationUrl, project, team, timeframe);
        try
        {
            IReadOnlyList<SprintIterationDocument> iterations = await source.GetIterationsAsync(
                project,
                team,
                timeframe,
                cancellationToken);
            return new SearchEnvelope<SprintIterationDocument>(
                "1.0",
                "iterationSearch",
                iterations.Count > 0 ? "success" : "notFound",
                query,
                timeProvider.GetUtcNow(),
                iterations,
                []);
        }
        catch (WorkItemSourceException exception)
        {
            return new SearchEnvelope<SprintIterationDocument>(
                "1.0",
                "iterationSearch",
                "error",
                query,
                timeProvider.GetUtcNow(),
                [],
                [new SearchError("azureDevOps", project, exception.Code, exception.Message, exception.Transient)]);
        }
    }

    private async Task<SprintIterationDocument?> ResolveIterationAsync(
        string project,
        string team,
        string? sprint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sprint))
        {
            IReadOnlyList<SprintIterationDocument> current = await source.GetIterationsAsync(
                project,
                team,
                "current",
                cancellationToken);
            return current.FirstOrDefault();
        }

        IReadOnlyList<SprintIterationDocument> all = await source.GetIterationsAsync(
            project,
            team,
            timeframe: null,
            cancellationToken);
        return all.FirstOrDefault(iteration =>
                   string.Equals(iteration.Name, sprint, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(iteration =>
                   string.Equals(iteration.Path, sprint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// State names are matched case-insensitively so "done" finds "Done". An empty filter keeps
    /// every item, which is what the plain overview wants.
    /// </summary>
    private static IReadOnlyList<SprintWorkItemDocument> Filter(
        IReadOnlyList<SprintWorkItemDocument> workItems,
        IReadOnlyList<string> states)
    {
        if (states.Count == 0)
        {
            return workItems;
        }

        var wanted = new HashSet<string>(states, StringComparer.OrdinalIgnoreCase);
        return workItems.Where(item => item.State is not null && wanted.Contains(item.State)).ToArray();
    }

    private static IReadOnlyDictionary<string, int> CountByState(
        IReadOnlyList<SprintWorkItemDocument> workItems) =>
        workItems
            .GroupBy(item => item.State ?? "unknown", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private SearchEnvelope<SprintDocument> CreateEnvelope(
        SprintQuery query,
        string status,
        IReadOnlyList<SprintDocument> results,
        IReadOnlyList<SearchError> errors) =>
        new(
            "1.0",
            "sprintSearch",
            status,
            query,
            timeProvider.GetUtcNow(),
            results,
            errors);
}
