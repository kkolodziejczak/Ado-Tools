namespace AdoRead.Sprints;

/// <summary>
/// Sprint overview. Deliberately shallow: enough to see what a sprint contains and to link out,
/// with no rich text. Use <c>ado-read work-item &lt;id&gt;</c> for a single item's full detail.
/// </summary>
public sealed record SprintDocument(
    Uri OrganizationUrl,
    string Project,
    string Team,
    SprintIterationDocument Iteration,
    IReadOnlyDictionary<string, int> CountsByState,
    IReadOnlyList<SprintWorkItemDocument> WorkItems);

public sealed record SprintIterationDocument(
    Guid Id,
    string Name,
    string? Path,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? FinishesAtUtc,
    string? Timeframe);

public sealed record SprintWorkItemDocument(
    int Id,
    string? Type,
    string? Title,
    string? State,
    string? IterationPath,
    string? AssignedTo,
    Uri? Browser);

public sealed record SprintQuery(
    int? Id,
    Uri OrganizationUrl,
    string Project,
    string Team,
    string? Sprint,
    IReadOnlyList<string> States);

public sealed record IterationQuery(
    Uri OrganizationUrl,
    string Project,
    string Team,
    string? Timeframe);

public sealed record TeamQuery(Uri OrganizationUrl, string Project, bool MineOnly);

public sealed record TeamDocument(Guid Id, string Name, string? Description);

public sealed record ProjectDocument(Guid Id, string Name, Uri? Rest);
