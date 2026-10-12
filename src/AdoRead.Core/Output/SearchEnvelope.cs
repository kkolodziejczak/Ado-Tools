namespace AdoRead.Output;

public sealed record SearchEnvelope<T>(
    string SchemaVersion,
    string Kind,
    string Status,
    object? Query,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<T> Results,
    IReadOnlyList<SearchError> Errors,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] PageMetadataDocument? Page = null);

public static class SearchEnvelopeFactory
{
    public static SearchEnvelope<object> Error(
        string kind,
        IEnumerable<SearchError> errors,
        DateTimeOffset generatedAtUtc) => Error<object>(kind, errors, generatedAtUtc);

    public static SearchEnvelope<T> Error<T>(
        string kind,
        IEnumerable<SearchError> errors,
        DateTimeOffset generatedAtUtc) =>
        new(
            "1.0",
            kind,
            "error",
            Query: null,
            generatedAtUtc,
            Results: [],
            SortErrors(errors));

    public static SearchEnvelope<T> Success<T>(
        string kind,
        IEnumerable<T> results,
        DateTimeOffset generatedAtUtc) =>
        new(
            "1.0",
            kind,
            "success",
            Query: null,
            generatedAtUtc,
            results.ToArray(),
            Errors: []);

    private static IReadOnlyList<SearchError> SortErrors(IEnumerable<SearchError> errors) =>
        errors
            .OrderBy(error => error.Scope, StringComparer.Ordinal)
            .ThenBy(error => error.Project, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)
            .ToArray();
}
