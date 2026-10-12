namespace AdoRead.Privacy;

/// <summary>
/// A kind of content that may leave the tool. Metadata (ids, titles, states, paths, dates, counts,
/// links, votes, names of projects and teams) is always returned and is never a category.
/// </summary>
public enum PrivacyCategory
{
    Text,
    Comments,
    People,
    CustomFields,
    Images,
    TestMessages,
    StackTraces,
    Logs
}

public static class PrivacyCategories
{
    public static IReadOnlyList<PrivacyCategory> All { get; } = Enum.GetValues<PrivacyCategory>();

    public static string NameOf(PrivacyCategory category) => category switch
    {
        PrivacyCategory.Text => "text",
        PrivacyCategory.Comments => "comments",
        PrivacyCategory.People => "people",
        PrivacyCategory.CustomFields => "customFields",
        PrivacyCategory.Images => "images",
        PrivacyCategory.TestMessages => "testMessages",
        PrivacyCategory.StackTraces => "stackTraces",
        PrivacyCategory.Logs => "logs",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };

    public static bool TryParse(string? value, out PrivacyCategory category)
    {
        foreach (PrivacyCategory candidate in All)
        {
            if (string.Equals(NameOf(candidate), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                category = candidate;
                return true;
            }
        }

        category = default;
        return false;
    }
}
