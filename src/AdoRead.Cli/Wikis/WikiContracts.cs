using AdoRead.Output;

namespace AdoRead.Wikis;

public sealed record WikiPageDocument(
    int? Id,
    string? Path,
    string? GitItemPath,
    string Source,
    string Format,
    string? Content,
    WikiVersionDocument? Version,
    WikiPageLinksDocument? Links);

public sealed record WikiVersionDocument(string Value, string? Type);

public sealed record WikiPageLinksDocument(Uri? Rest, Uri? Browser);

public sealed record WikiPageQuery(
    Uri OrganizationUrl,
    string Project,
    string WikiIdentifier,
    WikiSelectorDocument Selector,
    WikiVersionDocument? Version);

public sealed record WikiSelectorDocument(string Kind, string Value);

public sealed record WikiCatalogQuery(Uri OrganizationUrl, string Project);
public sealed record WikiPageCatalogQuery(string Project, string WikiIdentifier, WikiVersionDocument? Version);
public sealed record WikiCatalogDocument(Guid Id, string Name, string Type, Guid ProjectId, string? MappedPath, IReadOnlyList<WikiCatalogVersionDocument> Versions, WikiPageLinksDocument? Links);
public sealed record WikiCatalogVersionDocument(string Value, string? Type, CommandHintDocument Next);
public sealed record WikiPageSummaryDocument(int? Id, string? Path, CommandHintDocument Next);
public sealed record WikiSearchQuery(string Text, string? Project, string? Wiki, int Skip, int Top);
public sealed record WikiSearchProjectDocument(Guid Id, string Name);
public sealed record WikiSearchWikiDocument(Guid Id, string Name, string? MappedPath, string Version);
public sealed record WikiSearchMatchDocument(string Field, IReadOnlyList<string> Highlights);
public sealed record WikiSearchHitDocument(string FileName, string GitPath, WikiSearchProjectDocument Project, WikiSearchWikiDocument Wiki, IReadOnlyList<WikiSearchMatchDocument> Matches, CommandHintDocument Next);
