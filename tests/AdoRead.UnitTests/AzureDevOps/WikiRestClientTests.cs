using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class WikiRestClientTests
{
    [Theory]
    [InlineData(null, "https://almsearch.dev.azure.com/fabricated/_apis/search/wikisearchresults?api-version=7.1")]
    [InlineData("Project Space", "https://almsearch.dev.azure.com/fabricated/Project%20Space/_apis/search/wikisearchresults?api-version=7.1")]
    public async Task SearchAsync_UsesDocumentedProjectRouteAndRequestBody(string? project, string expectedUri)
    {
        var handler = new SearchResponseHandler("""{"count":0,"results":[],"infoCode":0}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        await reader.SearchAsync("hello", project, null, 3, 10, CancellationToken.None);

        Assert.Equal(expectedUri, handler.Uri);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("hello", body.RootElement.GetProperty("searchText").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("$skip").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("$top").GetInt32());
        Assert.False(body.RootElement.GetProperty("includeFacets").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("project", out _));
        Assert.False(body.RootElement.TryGetProperty("wiki", out _));
    }

    [Fact]
    public async Task SearchAsync_RejectsUndocumentedWikiFilterBeforeSendingARequest()
    {
        var handler = new SearchResponseHandler("""{"count":0,"results":[],"infoCode":0}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.SearchAsync("hello", "Project", "Wiki", 0, 10, CancellationToken.None));

        Assert.Equal("unsupportedWikiFilter", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Project/Child")]
    [InlineData("Project\\Child")]
    public async Task SearchAsync_RejectsUnsafeProjectSegmentsBeforeSendingARequest(string project)
    {
        var handler = new SearchResponseHandler("""{"count":0,"results":[],"infoCode":0}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.SearchAsync("hello", project, null, 0, 10, CancellationToken.None));

        Assert.Equal("invalidProject", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(" .. ")]
    [InlineData("../..")]
    [InlineData("a/../../b")]
    public async Task GetPagesAsync_RejectsUnsafeWikiSegmentsBeforeSendingARequest(string wiki)
    {
        var handler = new RecordingHandler("""{"value":[]}""", null);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.GetPagesAsync("Project", wiki, null, 50, null, CancellationToken.None));

        Assert.Equal("invalidWikiReference", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SearchAsync_MapsDocumentedResultsAndHits()
    {
        const string response = """
            {"count":1,"results":[{"fileName":"Hello-world.md","path":"/Hello-world.md","contentId":"ignored-content-id","project":{"id":"9811862e-ec6f-4e94-93bd-91555b51eedd","name":"Release"},"wiki":{"id":"9c2aeff2-bab0-43e9-9eaa-bf8283be064b","mappedPath":"/","name":"Release.wiki","version":"wikiMaster"},"hits":[{"fieldReferenceName":"fileNames","highlights":["<highlighthit>Hello</highlighthit>-world.md"]},{"fieldReferenceName":"content","highlights":["<highlighthit>Hello</highlighthit> world"]}]}],"facets":{"Project":[]},"infoCode":0}
            """;
        using var client = new HttpClient(new SearchResponseHandler(response)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        SourceWikiSearchResult result = await reader.SearchAsync("Hello", null, null, 0, 2, CancellationToken.None);

        SourceWikiSearchHit hit = Assert.Single(result.Hits);
        Assert.Equal("Hello-world.md", hit.FileName);
        Assert.Equal("/Hello-world.md", hit.GitPath);
        Assert.Collection(hit.Matches,
            match => { Assert.Equal("fileNames", match.Field); Assert.Single(match.Highlights); },
            match => { Assert.Equal("content", match.Field); Assert.Single(match.Highlights); });
        string serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("contentId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("facets", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_MapsFieldNameFallbackWhenReferenceNameIsAbsent()
    {
        const string response = """{"count":1,"results":[{"fileName":"Page.md","path":"/Page.md","project":{"id":"9811862e-ec6f-4e94-93bd-91555b51eedd","name":"P"},"wiki":{"id":"9c2aeff2-bab0-43e9-9eaa-bf8283be064b","name":"W","version":"main"},"hits":[{"fieldName":"content","highlights":["match"]}]}],"infoCode":0}""";
        using var client = new HttpClient(new SearchResponseHandler(response)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        SourceWikiSearchResult result = await reader.SearchAsync("match", null, null, 0, 10, CancellationToken.None);

        Assert.Equal("content", Assert.Single(Assert.Single(result.Hits).Matches).Field);
    }

    [Fact]
    public async Task Constructor_ConfiguresThePatOnTheOutboundSearchRequest()
    {
        var handler = new SearchResponseHandler("""{"count":0,"results":[],"infoCode":0}""");
        using var reader = new WikiRestClient(new Uri("https://dev.azure.com/fabricated/"), "dummy-pat", handler);

        await reader.SearchAsync("hello", null, null, 0, 10, CancellationToken.None);

        Assert.Equal("Basic", handler.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(":dummy-pat")), handler.Authorization.Parameter);
    }

    [Fact]
    public void ProductionHandler_DoesNotFollowRedirects()
    {
        using HttpClientHandler handler = WikiRestClient.CreateDefaultHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void ProductionConstructor_RejectsBlankPat()
    {
        Assert.Throws<ArgumentException>(() => new WikiRestClient(new Uri("https://dev.azure.com/fabricated/"), ""));
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeCallerOwnedHttpClient()
    {
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.OK)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        var reader = new WikiRestClient(client);

        reader.Dispose();
        using HttpResponseMessage response = await client.GetAsync("health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Dispose_DisposesOwnedHandler()
    {
        var handler = new DisposalTrackingHandler();
        var reader = new WikiRestClient(new Uri("https://dev.azure.com/fabricated/"), "dummy-pat", handler);

        reader.Dispose();

        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task SearchAsync_RefusesRedirectWithoutASecondRequest()
    {
        var handler = new RedirectHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        await Assert.ThrowsAsync<WikiSourceException>(() => reader.SearchAsync("x", null, null, 0, 10, CancellationToken.None));

        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task SearchAsync_UsesOneFixedSearchPostAndDoesNotExposeFailureBody()
    {
        var handler = new SearchHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.SearchAsync("secret", "Project", null, 0, 10, CancellationToken.None));

        Assert.Equal(1, handler.Calls); Assert.Equal("https://almsearch.dev.azure.com/fabricated/Project/_apis/search/wikisearchresults?api-version=7.1", handler.Request!.RequestUri!.AbsoluteUri); Assert.Equal(HttpMethod.Post, handler.Request.Method); Assert.DoesNotContain("dummy-pat", error.Message, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("{\"value\":[{\"id\":1,\"path\":\"/First\"}]}", "middle-token", 1)]
    [InlineData("{\"value\":[{\"id\":2,\"path\":\"/Middle\"}]}", "final-token", 1)]
    [InlineData("{\"value\":[]}", null, 0)]
    public async Task GetPagesAsync_PreservesEachOpaqueContinuationWithoutPaging(string json, string? continuation, int expectedCount)
    {
        var handler = new RecordingHandler(json, continuation);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        SourceWikiPageCatalog page = await reader.GetPagesAsync("Project", "Wiki", null, 50, "repeated-token", CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(expectedCount, page.Pages.Count);
        Assert.Equal(continuation, page.ContinuationToken);
    }

    [Fact]
    public async Task GetPagesAsync_UsesOnlyTheBoundedPagesBatchRouteAndMapsSummaries()
    {
        var handler = new RecordingHandler("""{"value":[{"id":2,"path":"/Zulu","content":"must not escape","viewStats":[{}]},{"id":1,"path":"/Alpha"}]}""", "opaque-out");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        SourceWikiPageCatalog page = await reader.GetPagesAsync("Project Space", "Wiki/Name", new WikiVersionDocument("release/1", "tag"), 50, "opaque-in", CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://dev.azure.com/fabricated/Project%20Space/_apis/wiki/wikis/Wiki%2FName/pagesbatch?api-version=7.1&versionDescriptor.version=release%2F1&versionDescriptor.versionType=tag", handler.Request.RequestUri!.AbsoluteUri);
        using JsonDocument request = JsonDocument.Parse(handler.Body!);
        Assert.Equal(50, request.RootElement.GetProperty("top").GetInt32());
        Assert.Equal("opaque-in", request.RootElement.GetProperty("continuationToken").GetString());
        Assert.Equal("opaque-out", page.ContinuationToken);
        Assert.Collection(page.Pages, first => { Assert.Equal(1, first.Id); Assert.Equal("/Alpha", first.Path); }, second => { Assert.Equal(2, second.Id); Assert.Equal("/Zulu", second.Path); });
        string serialized = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("content", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("viewStats", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", false)]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", false)]
    [InlineData(HttpStatusCode.NotFound, "notFound", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "throttled", true)]
    [InlineData(HttpStatusCode.InternalServerError, "serviceUnavailable", true)]
    [InlineData(HttpStatusCode.BadGateway, "serviceUnavailable", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "serviceUnavailable", true)]
    public async Task GetPagesAsync_MapsHttpFailuresToStableErrors(HttpStatusCode status, string code, bool transient)
    {
        using var client = new HttpClient(new StatusHandler(status)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.GetPagesAsync("Project", "Wiki", null, 50, null, CancellationToken.None));

        Assert.Equal(code, error.Code);
        Assert.Equal(transient, error.Transient);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", false)]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", false)]
    [InlineData(HttpStatusCode.NotFound, "notFound", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "throttled", true)]
    [InlineData(HttpStatusCode.InternalServerError, "serviceUnavailable", true)]
    [InlineData(HttpStatusCode.BadGateway, "serviceUnavailable", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "serviceUnavailable", true)]
    public async Task SearchAsync_MapsHttpFailuresToStableErrors(HttpStatusCode status, string code, bool transient)
    {
        using var client = new HttpClient(new StatusHandler(status)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.SearchAsync("hello", "Project", null, 0, 10, CancellationToken.None));

        Assert.Equal(code, error.Code);
        Assert.Equal(transient, error.Transient);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Project/Child")]
    [InlineData("Project\\Child")]
    public async Task GetPagesAsync_RejectsUnsafeProjectSegmentsBeforeSendingARequest(string project)
    {
        var handler = new RecordingHandler("""{"value":[]}""", null);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.GetPagesAsync(project, "Wiki", null, 50, null, CancellationToken.None));

        Assert.Equal("invalidProject", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetPagesAsync_RejectsMissingPagePathAsAStableResponseError()
    {
        using var client = new HttpClient(new RecordingHandler("""{"value":[{"id":1}]}""", null)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.GetPagesAsync("Project", "Wiki", null, 50, null, CancellationToken.None));

        Assert.Equal("invalidAzureDevOpsResponse", error.Code);
    }

    [Fact]
    public async Task GetPagesAsync_RejectsDuplicateContinuationHeadersAsAStableResponseError()
    {
        using var client = new HttpClient(new DuplicateContinuationHandler()) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.GetPagesAsync("Project", "Wiki", null, 50, null, CancellationToken.None));

        Assert.Equal("invalidAzureDevOpsResponse", error.Code);
    }

    [Fact]
    public async Task SearchAsync_RejectsMissingHitContainerAsAStableResponseError()
    {
        const string response = """{"count":1,"results":[{"fileName":"Page.md","path":"/Page.md","project":{"id":"9811862e-ec6f-4e94-93bd-91555b51eedd","name":"P"},"wiki":{"id":"9c2aeff2-bab0-43e9-9eaa-bf8283be064b","name":"W","version":"main"}}],"infoCode":0}""";
        using var client = new HttpClient(new SearchResponseHandler(response)) { BaseAddress = new Uri("https://dev.azure.com/fabricated/") };
        using var reader = new WikiRestClient(client);

        WikiSourceException error = await Assert.ThrowsAsync<WikiSourceException>(() => reader.SearchAsync("x", null, null, 0, 10, CancellationToken.None));

        Assert.Equal("invalidAzureDevOpsResponse", error.Code);
    }

    private sealed class RecordingHandler(string json, string? continuationToken) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Request = request; Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (continuationToken is not null) response.Headers.Add("x-ms-continuationtoken", continuationToken);
            return response;
        }
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("dummy-pat response body", Encoding.UTF8, "text/plain") });
        }
    }

    private sealed class SearchResponseHandler(string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Uri { get; private set; }
        public string? Body { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class DisposalTrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class DuplicateContinuationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("x-ms-continuationtoken", ["one", "two"]);
            return Task.FromResult(response);
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://untrusted.invalid/") } });
        }
    }
}
