using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Text;

namespace AdoRead.WorkItems;

/// <summary>
/// One row of <c>work-item list</c>: enough to choose which items to read in full with <c>work-item show</c>.
/// </summary>
public sealed record WorkItemListEntry(
    int Id,
    string? Type,
    string? Title,
    string? State,
    string? AssignedToUniqueName,
    IReadOnlyList<string> Tags,
    string? Project,
    string? AreaPath,
    string? IterationPath,
    DateTimeOffset? ChangedAtUtc,
    Uri? Browser);

/// <summary>
/// What was asked for. <see cref="Filter"/> is null when the caller gave the WIQL; <see cref="Wiql"/>
/// is always the query that ran.
/// </summary>
public sealed record WorkItemListQuery(Uri OrganizationUrl, WorkItemFilter? Filter, string Wiql, int Top, bool Full);

/// <summary>
/// Runs a WIQL query, then reads the first <see cref="WorkItemListQuery.Top"/> matches: as short entries
/// in one batched call, or with <see cref="WorkItemListQuery.Full"/> as the same documents
/// <c>work-item show</c> returns, one read each.
/// </summary>
public sealed class WorkItemListReader(
    IWorkItemSource source,
    Uri organizationUrl,
    TimeProvider timeProvider,
    EffectivePrivacy privacy)
{
    public const string ListKind = "workItemList";
    public const string FullKind = "workItemSearch";

    private static readonly IReadOnlyList<PrivacyCategory> ListCategories = [PrivacyCategory.People];

    private readonly WorkItemMapper _mapper = new(new RichTextNormalizer());

    public async Task<SearchEnvelope<WorkItemListEntry>> ListAsync(WorkItemListQuery query, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<int> matched = await source.QueryAsync(query.Wiql, cancellationToken);
            int[] selected = matched.Take(query.Top).ToArray();
            IReadOnlyList<SourceWorkItem> items = await source.GetManyAsync(selected, cancellationToken);
            WorkItemListEntry[] entries = items.Select(ToEntry).ToArray();
            return PrivacyEnvelopes.Describe(
                Envelope(ListKind, "1.0", query, "success", entries, [], matched.Count),
                privacy,
                ListCategories);
        }
        catch (WorkItemSourceException exception)
        {
            return Envelope<WorkItemListEntry>(ListKind, "1.0", query, "error", [], [Error(exception)], totalCount: null);
        }
    }

    private WorkItemListEntry ToEntry(SourceWorkItem item)
    {
        WorkItemDocument document = _mapper.Map(WorkItemPrivacy.AliasIdentities(item, privacy), [], organizationUrl);
        return new WorkItemListEntry(
            document.Id,
            document.Type,
            document.Title,
            document.State,
            document.AssignedToUniqueName,
            document.Tags,
            document.Project,
            document.AreaPath,
            document.IterationPath,
            document.ChangedAtUtc,
            document.Links.Browser);
    }

    public async Task<SearchEnvelope<WorkItemDocument>> ListFullAsync(WorkItemListQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> matched;
        try
        {
            matched = await source.QueryAsync(query.Wiql, cancellationToken);
        }
        catch (WorkItemSourceException exception)
        {
            return Envelope<WorkItemDocument>(FullKind, "2.0", query, "error", [], [Error(exception)], totalCount: null);
        }

        var reader = new WorkItemReader(source, _mapper, organizationUrl, timeProvider, privacy);
        var documents = new List<WorkItemDocument>();
        var errors = new List<SearchError>();
        foreach (int id in matched.Take(query.Top))
        {
            SearchEnvelope<WorkItemDocument> read = await reader.ReadAsync(id, cancellationToken);
            documents.AddRange(read.Results);
            errors.AddRange(read.Status == "notFound"
                ? [new SearchError("azureDevOps", null, "workItemNotFound", $"Work item {id} matched the query but could not be read.", false)]
                : read.Errors.Select(error => error with { Message = $"Work item {id}: {error.Message}" }));
        }

        string status = errors.Count == 0 ? "success" : documents.Count > 0 ? "partial" : "error";
        return PrivacyEnvelopes.Describe(
            Envelope(FullKind, "2.0", query, status, documents, errors, matched.Count),
            privacy,
            WorkItemPrivacy.Categories);
    }

    private SearchEnvelope<T> Envelope<T>(
        string kind,
        string schemaVersion,
        WorkItemListQuery query,
        string status,
        IReadOnlyList<T> results,
        IReadOnlyList<SearchError> errors,
        int? totalCount) =>
        new(
            schemaVersion,
            kind,
            status,
            query,
            timeProvider.GetUtcNow(),
            results,
            errors,
            totalCount is null ? null : new PageMetadataDocument(null, query.Top, results.Count, totalCount, null, null));

    private static SearchError Error(WorkItemSourceException exception) =>
        new("azureDevOps", null, exception.Code, exception.Message, exception.Transient);
}
