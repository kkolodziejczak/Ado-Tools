namespace AdoRead.Wikis;

internal static class WikiIdentifier
{
    public static bool IsSafeProject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string normalized = value.Trim();
        return normalized is not "." and not ".."
            && !normalized.Contains('/')
            && !normalized.Contains('\\');
    }

    public static bool IsSafeWiki(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return value.Trim()
            .Split('/', '\\')
            .All(segment => segment is not "." and not "..");
    }
}
