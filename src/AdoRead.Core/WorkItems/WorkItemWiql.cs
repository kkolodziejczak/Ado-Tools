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
}

/// <summary>
/// Turns a <see cref="WorkItemFilter"/> into a flat WIQL query, newest change first. Every value is a
/// quoted WIQL string with its quotes doubled, so a value can never end its string and add clauses.
/// A project filter becomes <c>@project</c>; the caller runs the query in that project.
/// </summary>
public static class WorkItemWiql
{
    public static string Build(WorkItemFilter filter)
    {
        var clauses = new List<string>();
        // The query runs in the project's context, so --project takes a name or an ID like elsewhere.
        if (Clean(filter.Project) is not null)
        {
            clauses.Add("[System.TeamProject] = @project");
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

    /// <summary>True for <c>me</c> and <c>none</c>, which name no person.</summary>
    public static bool IsMacroAssignee(string? assignee) =>
        Clean(assignee)?.ToLowerInvariant() is "me" or "none";

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
