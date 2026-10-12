using System.Globalization;
using AdoRead.Sprints;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public interface ISprintSource
{
    Task<IReadOnlyList<SprintIterationDocument>> GetIterationsAsync(
        string project,
        string team,
        string? timeframe,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SprintWorkItemDocument>> GetWorkItemsAsync(
        string project,
        string team,
        Guid iterationId,
        CancellationToken cancellationToken);
}

public sealed class SprintSource(
    IWorkClient workClient,
    IWorkItemTrackingClient workItemClient,
    Uri organizationUrl) : ISprintSource
{
    /// <summary>Only what the summary view exposes; no rich text is requested.</summary>
    private static readonly string[] SummaryFields =
    [
        "System.WorkItemType",
        "System.Title",
        "System.State",
        "System.IterationPath",
        "System.AssignedTo",
        "System.TeamProject"
    ];

    public async Task<IReadOnlyList<SprintIterationDocument>> GetIterationsAsync(
        string project,
        string team,
        string? timeframe,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<TeamSettingsIteration> iterations = await workClient.GetIterationsAsync(
                project,
                team,
                timeframe,
                cancellationToken);
            return iterations
                .Select(MapIteration)
                .OrderBy(iteration => iteration.StartsAtUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(iteration => iteration.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.WorkItemFailure(exception);
        }
    }

    public async Task<IReadOnlyList<SprintWorkItemDocument>> GetWorkItemsAsync(
        string project,
        string team,
        Guid iterationId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<int> ids = await workClient.GetIterationWorkItemIdsAsync(
                project,
                team,
                iterationId,
                cancellationToken);
            if (ids.Count == 0)
            {
                return [];
            }

            IReadOnlyList<WorkItem> items = await workItemClient.GetWorkItemsAsync(
                ids,
                SummaryFields,
                cancellationToken);
            return items
                .Select(MapWorkItem)
                .OrderBy(item => item.Type, StringComparer.Ordinal)
                .ThenBy(item => item.Id)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.WorkItemFailure(exception);
        }
    }

    private static SprintIterationDocument MapIteration(TeamSettingsIteration source) =>
        new(
            source.Id,
            source.Name,
            source.Path,
            ToUtc(source.Attributes?.StartDate),
            ToUtc(source.Attributes?.FinishDate),
            source.Attributes?.TimeFrame?.ToString().ToLowerInvariant());

    private SprintWorkItemDocument MapWorkItem(WorkItem item)
    {
        IReadOnlyDictionary<string, object> fields =
            (IReadOnlyDictionary<string, object>?)item.Fields ?? new Dictionary<string, object>();
        int id = item.Id ?? 0;
        return new SprintWorkItemDocument(
            id,
            GetString(fields, "System.WorkItemType"),
            GetString(fields, "System.Title"),
            GetString(fields, "System.State"),
            GetString(fields, "System.IterationPath"),
            GetIdentity(fields, "System.AssignedTo"),
            CreateBrowserUrl(GetString(fields, "System.TeamProject"), id),
            GetUniqueName(fields, "System.AssignedTo"));
    }

    private Uri? CreateBrowserUrl(string? project, int id) => string.IsNullOrWhiteSpace(project) || id <= 0
        ? null
        : new Uri(organizationUrl, $"{Uri.EscapeDataString(project)}/_workitems/edit/{id}");

    private static string? GetString(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private static string? GetUniqueName(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out object? value) && value is IdentityRef identity
            ? identity.UniqueName
            : null;

    private static string? GetIdentity(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out object? value) && value is IdentityRef identity
            ? identity.DisplayName
            : GetString(fields, name);

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(value.Value.ToUniversalTime());
}
