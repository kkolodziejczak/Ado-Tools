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

/// <summary>What to list: the filters, or null when the caller wrote the WIQL, and the query that runs.</summary>
public sealed record WorkItemListRequest(WorkItemFilter? Filter, string Wiql, int Top);

/// <summary>
/// The request as the output echoes it. When people are withheld, a named assignee is aliased and the
/// WIQL is left out, because either can carry a person's name.
/// </summary>
public sealed record WorkItemListQuery(Uri OrganizationUrl, WorkItemFilter? Filter, string? Wiql, int Top, bool Full);

/// <summary>
/// Runs a WIQL query, then reads the first <see cref="WorkItemListRequest.Top"/> matches: as short
/// entries in one batched call, or as the same documents <c>work-item show</c> returns, one read each.
/// </summary>
public sealed class WorkItemListReader(
    IWorkItemSource source,
    Uri organizationUrl,
    TimeProvider timeProvider,
    EffectivePrivacy privacy,
    int maxParallelReads)
{
    public const string ListKind = "workItemList";
    public const string FullKind = "workItemSearch";

    private static readonly IReadOnlyList<PrivacyCategory> ListCategories = [PrivacyCategory.People];

    private readonly WorkItemMapper _mapper = new(new RichTextNormalizer());

    public async Task<SearchEnvelope<WorkItemListEntry>> ListAsync(WorkItemListRequest request, CancellationToken cancellationToken)
    {
        WorkItemListQuery query = Echo(request, full: false);
        try
        {
            IReadOnlyList<int> matched = await QueryAsync(request, cancellationToken);
            int[] selected = matched.Take(request.Top).ToArray();
            IReadOnlyList<SourceWorkItem> items = await source.GetManyAsync(selected, cancellationToken);
            WorkItemListEntry[] entries = items.Select(ToEntry).ToArray();
            SearchError[] errors = selected
                .Except(entries.Select(entry => entry.Id))
                .Select(Unreadable)
                .ToArray();
            return PrivacyEnvelopes.Describe(
                Envelope(ListKind, "1.0", query, entries, errors, matched),
                privacy,
                ListCategories);
        }
        catch (WorkItemSourceException exception)
        {
            return Envelope<WorkItemListEntry>(ListKind, "1.0", query, [], [Error(exception)], matched: null);
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

    public async Task<SearchEnvelope<WorkItemDocument>> ListFullAsync(WorkItemListRequest request, CancellationToken cancellationToken)
    {
        WorkItemListQuery query = Echo(request, full: true);
        IReadOnlyList<int> matched;
        try
        {
            matched = await QueryAsync(request, cancellationToken);
        }
        catch (WorkItemSourceException exception)
        {
            return Envelope<WorkItemDocument>(FullKind, "2.0", query, [], [Error(exception)], matched: null);
        }

        // Each read is several calls and the whole command shares one timeout, so reads overlap. The
        // results keep the query's order.
        var reader = new WorkItemReader(source, _mapper, organizationUrl, timeProvider, privacy);
        int[] selected = matched.Take(request.Top).ToArray();
        var reads = new SearchEnvelope<WorkItemDocument>[selected.Length];
        await Parallel.ForAsync(
            0,
            selected.Length,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelReads, CancellationToken = cancellationToken },
            async (index, token) => reads[index] = await reader.ReadAsync(selected[index], token));

        var documents = new List<WorkItemDocument>();
        var errors = new List<SearchError>();
        foreach ((int id, SearchEnvelope<WorkItemDocument> read) in selected.Zip(reads))
        {
            documents.AddRange(read.Results);
            errors.AddRange(read.Status == "notFound"
                ? [Unreadable(id)]
                : read.Errors.Select(error => error with { Message = $"Work item {id}: {error.Message}" }));
        }

        return PrivacyEnvelopes.Describe(
            Envelope(FullKind, "2.0", query, documents, errors, matched),
            privacy,
            WorkItemPrivacy.Categories);
    }

    private Task<IReadOnlyList<int>> QueryAsync(WorkItemListRequest request, CancellationToken cancellationToken) =>
        source.QueryAsync(request.Wiql, WorkItemWiql.Clean(request.Filter?.Project), cancellationToken);

    private WorkItemListQuery Echo(WorkItemListRequest request, bool full)
    {
        if (privacy.Allows(PrivacyCategory.People))
        {
            return new WorkItemListQuery(organizationUrl, request.Filter, request.Wiql, request.Top, full);
        }

        WorkItemFilter? filter = request.Filter is null
            ? null
            : request.Filter with { Assignee = WorkItemWiql.IsMacroAssignee(request.Filter.Assignee) ? request.Filter.Assignee : privacy.Person(request.Filter.Assignee) };
        return new WorkItemListQuery(organizationUrl, filter, Wiql: null, request.Top, full);
    }

    /// <summary>
    /// The page counts what matched, except at the service's cap, where the count is only a lower bound
    /// and is left out.
    /// </summary>
    private SearchEnvelope<T> Envelope<T>(
        string kind,
        string schemaVersion,
        WorkItemListQuery query,
        IReadOnlyList<T> results,
        IReadOnlyList<SearchError> errors,
        IReadOnlyList<int>? matched) =>
        new(
            schemaVersion,
            kind,
            errors.Count == 0 ? "success" : results.Count > 0 ? "partial" : "error",
            query,
            timeProvider.GetUtcNow(),
            results,
            errors,
            matched is null
                ? null
                : new PageMetadataDocument(
                    null,
                    query.Top,
                    results.Count,
                    matched.Count < SdkWorkItemTrackingClient.MaxQueryResults ? matched.Count : null,
                    null,
                    null));

    private static SearchError Unreadable(int id) =>
        new("azureDevOps", null, "workItemNotFound", $"Work item {id} matched the query but could not be read.", false);

    private static SearchError Error(WorkItemSourceException exception) =>
        new("azureDevOps", null, exception.Code, exception.Message, exception.Transient);
}
