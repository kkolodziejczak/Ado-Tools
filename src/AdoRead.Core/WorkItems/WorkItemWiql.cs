namespace AdoRead.WorkItems;

/// <summary>The filters of <c>work-item list</c>. Every filter given must match; values within one filter are alternatives, except tags, which must all be present.</summary>
public sealed record WorkItemFilter(
    string? Project,
    IReadOnlyList<string> Tags,
    string? Assignee,
    IReadOnlyList<string> States,
    IReadOnlyList<string> Areas,
    IReadOnlyList<string> Types)
{
    public static WorkItemFilter None { get; } = new(null, [], null, [], [], []);

    public bool IsEmpty => WorkItemWiql.Clean(Project) is null
        && WorkItemWiql.Clean(Assignee) is null
        && !WorkItemWiql.Clean(Tags).Any()
        && !WorkItemWiql.Clean(States).Any()
        && !WorkItemWiql.Clean(Areas).Any()
        && !WorkItemWiql.Clean(Types).Any();
}

/// <summary>
/// Turns a <see cref="WorkItemFilter"/> into a flat WIQL query, newest change first. Every value is a
/// quoted WIQL string with its quotes doubled, so a value can never end its string and add clauses.
/// </summary>
public static class WorkItemWiql
{
    public static string Build(WorkItemFilter filter)
    {
        var clauses = new List<string>();
        if (Clean(filter.Project) is { } project)
        {
            clauses.Add($"[System.TeamProject] = {Quote(project)}");
        }

        clauses.AddRange(Clean(filter.Tags).Select(tag => $"[System.Tags] CONTAINS {Quote(tag)}"));
        if (Clean(filter.Assignee) is { } assignee)
        {
            clauses.Add($"[System.AssignedTo] = {AssigneeValue(assignee)}");
        }

        AddIn(clauses, "System.State", filter.States);
        string[] areas = Clean(filter.Areas).ToArray();
        if (areas.Length > 0)
        {
            clauses.Add($"({string.Join(" OR ", areas.Select(area => $"[System.AreaPath] UNDER {Quote(area)}"))})");
        }

        AddIn(clauses, "System.WorkItemType", filter.Types);
        string where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
        return $"SELECT [System.Id] FROM WorkItems{where} ORDER BY [System.ChangedDate] DESC";
    }

    /// <summary><c>me</c> is the token owner and <c>none</c> an unassigned item; anything else is a name or e-mail.</summary>
    private static string AssigneeValue(string assignee) => assignee.ToLowerInvariant() switch
    {
        "me" => "@Me",
        "none" => "''",
        _ => Quote(assignee)
    };

    private static void AddIn(List<string> clauses, string field, IReadOnlyList<string> values)
    {
        string[] cleaned = Clean(values).ToArray();
        if (cleaned.Length > 0)
        {
            clauses.Add($"[{field}] IN ({string.Join(", ", cleaned.Select(Quote))})");
        }
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IEnumerable<string> Clean(IReadOnlyList<string> values) =>
        values.Select(Clean).OfType<string>();
}
