using System.Globalization;

namespace AdoRead.Commands;

internal sealed record AdoReference(int Id, string? Organization, string? Project = null)
{
    public bool IsFor(Uri organizationUrl)
    {
        if (Organization is null)
        {
            return true;
        }

        string configuredOrganization = Uri.UnescapeDataString(organizationUrl.AbsolutePath.Trim('/'));
        return string.Equals(Organization, configuredOrganization, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class AdoReferenceParser
{
    public static AdoReference? ParseWorkItem(string? value) => Parse(value, (_, segments) => FindWorkItemId(segments));

    public static AdoReference? ParsePullRequest(string? value) => Parse(value, (_, segments) => FindPullRequestId(segments));

    /// <summary>A build reference is a positive build id or a results URL such as .../{project}/_build/results?buildId=123.</summary>
    public static AdoReference? ParseBuild(string? value) =>
        Parse(value, (uri, segments) => segments.Contains("_build", StringComparer.OrdinalIgnoreCase) ? FindBuildId(uri) : null);

    private static AdoReference? Parse(string? value, Func<Uri, IReadOnlyList<string>, int?> findId)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericId)
            && numericId > 0)
        {
            return new AdoReference(numericId, null);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !TryGetOrganization(uri, out string? organization))
        {
            return null;
        }

        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        int? id = findId(uri, segments);
        return id is > 0 ? new AdoReference(id.Value, organization, FindProject(uri, segments)) : null;
    }

    /// <summary>The project segment follows the organization on dev.azure.com and is the first segment on legacy hosts.</summary>
    private static string? FindProject(Uri uri, IReadOnlyList<string> segments)
    {
        int projectIndex = string.Equals(uri.IdnHost, "dev.azure.com", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return segments.Count > projectIndex && !segments[projectIndex].StartsWith('_')
            ? segments[projectIndex]
            : null;
    }

    private static int? FindBuildId(Uri uri)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0
                || !string.Equals(Uri.UnescapeDataString(pair[..separator]), "buildId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : null;
        }

        return null;
    }

    private static bool TryGetOrganization(Uri uri, out string? organization)
    {
        if (string.Equals(uri.IdnHost, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            organization = segments.Length > 0 ? Uri.UnescapeDataString(segments[0]) : null;
            return !string.IsNullOrWhiteSpace(organization);
        }

        const string legacySuffix = ".visualstudio.com";
        if (uri.IdnHost.EndsWith(legacySuffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = uri.IdnHost[..^legacySuffix.Length];
            return !string.IsNullOrWhiteSpace(organization) && !organization.Contains('.');
        }

        organization = null;
        return false;
    }

    private static int? FindWorkItemId(IReadOnlyList<string> segments)
    {
        for (int index = 1; index < segments.Count; index++)
        {
            if (Matches(segments, index, "_workitems", "edit"))
            {
                return ParsePositiveId(segments, index + 2);
            }

            if (Matches(segments, index, "_apis", "wit", "workitems"))
            {
                return ParsePositiveId(segments, index + 3);
            }
        }

        return null;
    }

    private static int? FindPullRequestId(IReadOnlyList<string> segments)
    {
        for (int index = 1; index < segments.Count; index++)
        {
            if (string.Equals(segments[index], "pullrequest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[index], "pullrequests", StringComparison.OrdinalIgnoreCase))
            {
                return ParsePositiveId(segments, index + 1);
            }
        }

        return null;
    }

    private static bool Matches(IReadOnlyList<string> segments, int start, params string[] expected)
    {
        if (start + expected.Length > segments.Count)
        {
            return false;
        }

        for (int offset = 0; offset < expected.Length; offset++)
        {
            if (!string.Equals(segments[start + offset], expected[offset], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int? ParsePositiveId(IReadOnlyList<string> segments, int index) =>
        index < segments.Count
        && int.TryParse(segments[index], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
        && id > 0
            ? id
            : null;
}
