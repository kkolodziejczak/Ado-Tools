using System.Globalization;

namespace AdoRead.Wikis;

public abstract record WikiSelector;
public sealed record WikiPathSelector(string Value) : WikiSelector;
public sealed record WikiPageIdSelector(int Value) : WikiSelector;
public sealed record WikiGitPathSelector(string Value) : WikiSelector;
public sealed record WikiReference(string Organization, string Project, string Wiki, WikiSelector Selector, WikiVersionDocument? Version = null)
{
    public bool IsFor(Uri organizationUrl) => string.Equals(
        Organization,
        Uri.UnescapeDataString(organizationUrl.AbsolutePath.Trim('/')),
        StringComparison.OrdinalIgnoreCase);
}

public static class WikiReferenceParser
{
    public static WikiReference? Create(string? project, string? wiki, string? path, int? pageId, string? version, string? versionType) =>
        Create(project, wiki, path, pageId, null, version, versionType);

    public static WikiReference? ParseUrl(string? value)
    {
        if (HasMalformedPercent(value) || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? organization; int offset;
        if (string.Equals(uri.IdnHost, "dev.azure.com", StringComparison.OrdinalIgnoreCase)) { if (segments.Length < 1) return null; organization = Decode(segments[0]); offset = 1; }
        else if (uri.IdnHost.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)) { organization = uri.IdnHost[..^".visualstudio.com".Length]; offset = 0; }
        else return null;
        if (organization is null || segments.Length < offset + 4) return null;
        string? project = Decode(segments[offset]);
        if (!Matches(segments, offset + 1, "_wiki", "wikis") && !Matches(segments, offset + 1, "_apis", "wiki", "wikis")) return null;
        int wikiIndex = offset + (Matches(segments, offset + 1, "_wiki", "wikis") ? 3 : 4);
        if (wikiIndex >= segments.Length) return null;
        string? wiki = Decode(segments[wikiIndex]);
        if (!WikiIdentifier.IsSafeProject(project) || !WikiIdentifier.IsSafeWiki(wiki)) return null;
        string safeProject = project!;
        string safeWiki = wiki!;
        if (Matches(segments, offset + 1, "_wiki", "wikis"))
        {
            if (wikiIndex + 1 < segments.Length && TryPositive(Decode(segments[wikiIndex + 1]), out int id)) return !HasSelectorQuery(uri) ? new(organization, safeProject, safeWiki, new WikiPageIdSelector(id)) : null;
            return TryPath(uri, out string? path) ? new(organization, safeProject, safeWiki, new WikiPathSelector(path!)) : null;
        }
        if (wikiIndex + 2 < segments.Length && string.Equals(Decode(segments[wikiIndex + 1]), "pages", StringComparison.OrdinalIgnoreCase) && TryPositive(Decode(segments[wikiIndex + 2]), out int pageId)) return !HasSelectorQuery(uri) ? new(organization, safeProject, safeWiki, new WikiPageIdSelector(pageId)) : null;
        return wikiIndex + 1 < segments.Length && string.Equals(Decode(segments[wikiIndex + 1]), "pages", StringComparison.OrdinalIgnoreCase) && TryPath(uri, out string? restPath) ? new(organization, safeProject, safeWiki, new WikiPathSelector(restPath!)) : null;
    }

    public static WikiReference? Create(string? project, string? wiki, string? path, int? pageId, string? gitPath, string? version, string? versionType)
    {
        if (!WikiIdentifier.IsSafeProject(project) || !HasValidSelector(wiki, path, pageId, gitPath, version, versionType)) return null;
        WikiSelector selector = path is not null ? new WikiPathSelector(path) : pageId is not null ? new WikiPageIdSelector(pageId.Value) : new WikiGitPathSelector(gitPath!);
        return new(string.Empty, project!, wiki!, selector, version is null ? null : new WikiVersionDocument(version, versionType));
    }

    internal static bool HasValidSelector(string? wiki, string? path, int? pageId, string? gitPath, string? version, string? versionType)
    {
        int selectors = (path is null ? 0 : 1) + (pageId is null ? 0 : 1) + (gitPath is null ? 0 : 1);
        return WikiIdentifier.IsSafeWiki(wiki)
            && selectors == 1
            && (pageId is null || pageId > 0 && version is null && versionType is null)
            && (gitPath is null || !string.IsNullOrWhiteSpace(version))
            && (versionType is null || version is not null && new[] { "branch", "tag", "commit" }.Contains(versionType, StringComparer.OrdinalIgnoreCase))
            && (path is null || path.StartsWith('/') && !string.IsNullOrWhiteSpace(path))
            && (gitPath is null || gitPath.StartsWith('/') && !string.IsNullOrWhiteSpace(gitPath));
    }

    private static bool TryPath(Uri uri, out string? value)
    {
        value = null; string[] pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries); string? encoded = null;
        foreach (string pair in pairs) { int equals = pair.IndexOf('='); string? key = Decode(equals < 0 ? pair : pair[..equals]); string? queryValue = Decode(equals < 0 ? "" : pair[(equals + 1)..]); if (key is null || queryValue is null) return false; if (!string.Equals(key, "pagePath", StringComparison.OrdinalIgnoreCase) && !string.Equals(key, "path", StringComparison.OrdinalIgnoreCase)) continue; if (encoded is not null) return false; encoded = equals < 0 ? "" : pair[(equals + 1)..]; }
        value = Decode(encoded); return value is not null && value.StartsWith('/');
    }
    private static bool HasSelectorQuery(Uri uri) => uri.Query.Contains("pagePath=", StringComparison.OrdinalIgnoreCase) || uri.Query.Contains("path=", StringComparison.OrdinalIgnoreCase);
    private static string? Decode(string? value)
    {
        if (value is null || value.Select((character, index) => character != '%' || index + 2 < value.Length && Uri.IsHexDigit(value[index + 1]) && Uri.IsHexDigit(value[index + 2])).Any(valid => !valid)) return null;
        return Uri.UnescapeDataString(value);
    }
    private static bool HasMalformedPercent(string? value) => value is null || value.Select((character, index) => character == '%' && (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2]))).Any(invalid => invalid);
    private static bool TryPositive(string? value, out int id) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    private static bool Matches(IReadOnlyList<string> segments, int index, params string[] values) => index + values.Length <= segments.Count && values.Select((value, offset) => string.Equals(Decode(segments[index + offset]), value, StringComparison.OrdinalIgnoreCase)).All(match => match);
}
