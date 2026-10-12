using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using AdoRead.Wikis;

namespace AdoRead.AzureDevOps;

public sealed class WikiRestClient : IWikiPageCatalogClient, IWikiSearchClient, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public WikiRestClient(Uri organizationUrl, string pat)
    {
        ArgumentNullException.ThrowIfNull(organizationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(pat);
        _client = CreateAuthenticatedClient(organizationUrl, pat, CreateDefaultHandler());
        _ownsClient = true;
    }

    public WikiRestClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    internal WikiRestClient(Uri organizationUrl, string pat, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(organizationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(pat);
        ArgumentNullException.ThrowIfNull(handler);
        _client = CreateAuthenticatedClient(organizationUrl, pat, handler);
        _ownsClient = true;
    }

    private static HttpClient CreateAuthenticatedClient(Uri organizationUrl, string pat, HttpMessageHandler handler)
    {
        var client = new HttpClient(handler, disposeHandler: true) { BaseAddress = organizationUrl };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}")));
        return client;
    }

    internal static HttpClientHandler CreateDefaultHandler() => new() { AllowAutoRedirect = false };

    public async Task<SourceWikiPageCatalog> GetPagesAsync(string project, string wiki, WikiVersionDocument? version, int top, string? continuationToken, CancellationToken cancellationToken)
    {
        project = ValidateProject(project);
        wiki = ValidateWiki(wiki);
        string path = $"{Uri.EscapeDataString(project)}/_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pagesbatch?api-version=7.1";
        if (version is not null) path += $"&versionDescriptor.version={Uri.EscapeDataString(version.Value)}&versionDescriptor.versionType={Uri.EscapeDataString(version.Type ?? "branch")}";
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { top, continuationToken }) };
        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw HttpFailure(response.StatusCode, "Azure DevOps Wiki page discovery failed.");
        try
        {
            using JsonDocument body = await ReadBodyAsync(response, cancellationToken);
            SourceWikiPageSummary[] pages = RequiredArray(body.RootElement, "value")
                .EnumerateArray()
                .Select(page => new SourceWikiPageSummary(OptionalInt32(page, "id"), RequiredString(page, "path")))
                .OrderBy(page => page.Path, StringComparer.Ordinal)
                .ThenBy(page => page.Id)
                .ToArray();
            return new SourceWikiPageCatalog(pages, ContinuationToken(response));
        }
        catch (Exception exception) when (IsInvalidResponse(exception))
        {
            throw InvalidResponse(exception);
        }
    }

    public async Task<SourceWikiSearchResult> SearchAsync(string text, string? project, string? wiki, int skip, int top, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(wiki)) throw new WikiSourceException("unsupportedWikiFilter", "Azure DevOps does not document a Wiki-name search filter.", false);
        Uri? baseAddress = _client.BaseAddress;
        if (baseAddress is null || !string.Equals(baseAddress.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Wiki search requires a dev.azure.com organization URL.");
        string organization = baseAddress.AbsolutePath.Trim('/');
        string projectSegment = project is null ? string.Empty : $"/{Uri.EscapeDataString(ValidateProject(project))}";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://almsearch.dev.azure.com/{Uri.EscapeDataString(organization)}{projectSegment}/_apis/search/wikisearchresults?api-version=7.1", UriKind.Absolute)) { Content = JsonContent.Create(new Dictionary<string, object> { ["searchText"] = text, ["$skip"] = skip, ["$top"] = top, ["includeFacets"] = false }) };
        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw HttpFailure(response.StatusCode, "Azure DevOps Wiki search failed.");
        try
        {
            using JsonDocument body = await ReadBodyAsync(response, cancellationToken);
            JsonElement root = body.RootElement;
            SourceWikiSearchHit[] hits = RequiredArray(root, "results").EnumerateArray().Select(MapSearchHit).ToArray();
            return new SourceWikiSearchResult(hits, RequiredInt32(root, "count"), RequiredInt32(root, "infoCode"));
        }
        catch (Exception exception) when (IsInvalidResponse(exception))
        {
            throw InvalidResponse(exception);
        }
    }

    private static SourceWikiSearchHit MapSearchHit(JsonElement hit)
    {
        JsonElement project = RequiredObject(hit, "project");
        JsonElement wiki = RequiredObject(hit, "wiki");
        SourceWikiSearchMatch[] matches = RequiredArray(hit, "hits").EnumerateArray()
            .Select(match => new SourceWikiSearchMatch(RequiredSearchFieldName(match), RequiredArray(match, "highlights").EnumerateArray().Select(value => value.GetString() ?? throw new JsonException()).ToArray()))
            .ToArray();
        return new SourceWikiSearchHit(RequiredString(hit, "fileName"), RequiredString(hit, "path"), RequiredGuid(project, "id"), RequiredString(project, "name"), RequiredGuid(wiki, "id"), RequiredString(wiki, "name"), OptionalString(wiki, "mappedPath"), RequiredString(wiki, "version"), matches);
    }

    private static string ValidateProject(string value)
    {
        if (!WikiIdentifier.IsSafeProject(value)) throw new WikiSourceException("invalidProject", "The Azure DevOps project identifier is invalid.", false);
        return value;
    }

    private static string ValidateWiki(string value)
    {
        if (!WikiIdentifier.IsSafeWiki(value)) throw new WikiSourceException("invalidWikiReference", "The Azure DevOps Wiki identifier is invalid.", false);
        return value;
    }

    private static string RequiredSearchFieldName(JsonElement match) =>
        match.TryGetProperty("fieldReferenceName", out JsonElement fieldReferenceName) && fieldReferenceName.ValueKind == JsonValueKind.String
            ? fieldReferenceName.GetString() ?? throw new JsonException()
            : RequiredString(match, "fieldName");

    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken) ?? throw new JsonException();

    private static JsonElement RequiredArray(JsonElement parent, string name) => Required(parent, name, JsonValueKind.Array);
    private static JsonElement RequiredObject(JsonElement parent, string name) => Required(parent, name, JsonValueKind.Object);
    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind) => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == kind ? value : throw new JsonException();
    private static string RequiredString(JsonElement parent, string name) => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? throw new JsonException() : throw new JsonException();
    private static string? OptionalString(JsonElement parent, string name) => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int RequiredInt32(JsonElement parent, string name) => parent.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : throw new JsonException();
    private static int? OptionalInt32(JsonElement parent, string name) => parent.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : null;
    private static Guid RequiredGuid(JsonElement parent, string name) => parent.TryGetProperty(name, out JsonElement value) && value.TryGetGuid(out Guid result) ? result : throw new JsonException();

    private static string? ContinuationToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("x-ms-continuationtoken", out IEnumerable<string>? values)) return null;
        string[] tokens = values.ToArray();
        return tokens.Length == 1 ? tokens[0] : throw new JsonException();
    }

    private static bool IsInvalidResponse(Exception exception) => exception is JsonException or InvalidOperationException or FormatException;
    private static WikiSourceException InvalidResponse(Exception exception) => new("invalidAzureDevOpsResponse", "Azure DevOps returned an invalid Wiki response.", false, exception);
    private static WikiSourceException HttpFailure(HttpStatusCode status, string message) => new(StatusCode(status), message, status == HttpStatusCode.TooManyRequests || status >= HttpStatusCode.InternalServerError);
    private static string StatusCode(HttpStatusCode code) => code switch { HttpStatusCode.Unauthorized => "unauthorized", HttpStatusCode.Forbidden => "forbidden", HttpStatusCode.NotFound => "notFound", HttpStatusCode.TooManyRequests => "throttled", >= HttpStatusCode.InternalServerError => "serviceUnavailable", _ => "azureDevOpsError" };

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
