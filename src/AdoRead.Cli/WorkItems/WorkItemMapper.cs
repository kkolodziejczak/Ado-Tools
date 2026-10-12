using System.Globalization;
using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Text;

namespace AdoRead.WorkItems;

public sealed class WorkItemMapper(RichTextNormalizer richTextNormalizer)
{
    private static readonly HashSet<string> CoreFields = new(StringComparer.Ordinal)
    {
        "System.WorkItemType", "System.Title", "System.State", "System.Reason", "System.TeamProject",
        "System.AreaPath", "System.IterationPath", "System.Description", "System.AssignedTo", "System.CreatedBy",
        "System.ChangedBy", "System.CreatedDate", "System.ChangedDate", "System.ClosedDate", "System.ResolvedDate",
        "System.Tags", "Microsoft.VSTS.Common.AcceptanceCriteria", "Microsoft.VSTS.Common.Priority",
        "Microsoft.VSTS.Common.Severity", "Microsoft.VSTS.Common.ResolvedDate", "Microsoft.VSTS.Common.ClosedDate"
    };

    public WorkItemDocument Map(
        SourceWorkItem source,
        IReadOnlyList<WorkItemCommentDocument> comments,
        Uri organizationUrl)
    {
        string? project = GetString(source.Fields, "System.TeamProject");
        return new WorkItemDocument(
            source.Id,
            source.Revision,
            GetString(source.Fields, "System.WorkItemType"),
            GetString(source.Fields, "System.Title"),
            GetString(source.Fields, "System.State"),
            GetString(source.Fields, "System.Reason"),
            GetDecimal(source.Fields, "Microsoft.VSTS.Common.Priority"),
            GetString(source.Fields, "Microsoft.VSTS.Common.Severity"),
            organizationUrl,
            project,
            GetString(source.Fields, "System.AreaPath"),
            GetString(source.Fields, "System.IterationPath"),
            richTextNormalizer.Normalize(GetString(source.Fields, "System.Description"), "html"),
            richTextNormalizer.Normalize(GetString(source.Fields, "Microsoft.VSTS.Common.AcceptanceCriteria"), "html"),
            GetUniqueName(source.Fields, "System.AssignedTo"),
            GetUniqueName(source.Fields, "System.CreatedBy"),
            GetUniqueName(source.Fields, "System.ChangedBy"),
            GetDate(source.Fields, "System.CreatedDate"),
            GetDate(source.Fields, "System.ChangedDate"),
            GetFirstDate(source.Fields, "System.ClosedDate", "Microsoft.VSTS.Common.ClosedDate"),
            GetFirstDate(source.Fields, "System.ResolvedDate", "Microsoft.VSTS.Common.ResolvedDate"),
            MapTags(GetString(source.Fields, "System.Tags")),
            source.Relations.Select(MapRelation).ToArray(),
            comments.Select(MapComment).ToArray(),
            MapCustomFields(source.Fields),
            new WorkItemLinksDocument(source.RestUrl, CreateBrowserUrl(organizationUrl, project, source.Id)));
    }

    private WorkItemCommentDocument MapComment(WorkItemCommentDocument source)
    {
        RichTextDocument text = string.IsNullOrWhiteSpace(source.RenderedHtml)
            ? richTextNormalizer.Normalize(source.RawText, source.SourceFormat)
            : richTextNormalizer.Normalize(source.RenderedHtml, "html");
        return source with { PlainText = text.PlainText };
    }

    private static WorkItemRelationDocument MapRelation(SourceWorkItemRelation source) =>
        new(
            source.RelationType,
            ClassifyRelation(source),
            source.TargetUrl,
            ParseWorkItemId(source.TargetUrl),
            source.Attributes.ToDictionary(
                pair => pair.Key,
                pair => AdoValueConverter.ToJsonElement(pair.Value),
                StringComparer.Ordinal));

    private static string ClassifyRelation(SourceWorkItemRelation source)
    {
        string? name = source.Attributes.TryGetValue("name", out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
        return source.RelationType switch
        {
            "System.LinkTypes.Hierarchy-Reverse" => "parent",
            "System.LinkTypes.Hierarchy-Forward" => "child",
            "System.LinkTypes.Related" => "related",
            "AttachedFile" => "attachment",
            "Hyperlink" => "hyperlink",
            "ArtifactLink" when Contains(name, "pull request") => "pullRequest",
            "ArtifactLink" when Contains(name, "commit") => "commit",
            "ArtifactLink" when Contains(name, "branch") => "branch",
            _ => "unknown"
        };
    }

    private static bool Contains(string? value, string expected) =>
        value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static int? ParseWorkItemId(Uri? url)
    {
        if (url is null)
        {
            return null;
        }

        string segment = url.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
        return int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;
    }

    private static IReadOnlyDictionary<string, JsonElement> MapCustomFields(
        IReadOnlyDictionary<string, object?> fields) =>
        fields
            .Where(pair => !CoreFields.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key,
                pair => AdoValueConverter.ToJsonElement(pair.Value),
                StringComparer.Ordinal);

    private static string? GetUniqueName(
        IReadOnlyDictionary<string, object?> fields,
        string name) => fields.TryGetValue(name, out object? value) ? MapUniqueName(value) : null;

    private static string? MapUniqueName(object? value) => value switch
    {
        IdentityDocument identity => identity.UniqueName,
        IReadOnlyDictionary<string, object?> dictionary => DictionaryString(dictionary, "uniqueName"),
        IDictionary<string, object?> dictionary => MapUniqueName(
            new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase)),
        _ => null
    };

    private static string? DictionaryString(IReadOnlyDictionary<string, object?> dictionary, string name)
    {
        if (dictionary.TryGetValue(name, out object? direct))
        {
            return Convert.ToString(direct, CultureInfo.InvariantCulture);
        }

        KeyValuePair<string, object?> fallback = dictionary
            .FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        return fallback.Key is null ? null : Convert.ToString(fallback.Value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> MapTags(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string name) =>
        fields.TryGetValue(name, out object? value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static decimal? GetDecimal(IReadOnlyDictionary<string, object?> fields, string name)
    {
        if (!fields.TryGetValue(name, out object? value) || value is null)
        {
            return null;
        }

        return value is decimal number
            ? number
            : decimal.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static DateTimeOffset? GetFirstDate(
        IReadOnlyDictionary<string, object?> fields,
        params string[] names)
    {
        foreach (string name in names)
        {
            DateTimeOffset? value = GetDate(fields, name);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDate(IReadOnlyDictionary<string, object?> fields, string name)
    {
        if (!fields.TryGetValue(name, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset date => date.ToUniversalTime(),
            DateTime date => new DateTimeOffset(date.ToUniversalTime()),
            _ when DateTimeOffset.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed) => parsed,
            _ => null
        };
    }

    private static Uri? CreateBrowserUrl(Uri organizationUrl, string? project, int id) =>
        string.IsNullOrWhiteSpace(project)
            ? null
            : new Uri(organizationUrl, $"{Uri.EscapeDataString(project)}/_workitems/edit/{id}");
}
