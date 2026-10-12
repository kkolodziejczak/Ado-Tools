using System.Text.Json;
using AdoRead.Output;

namespace AdoRead.UnitTests.Output;

public sealed class JsonOutputTests
{
    [Fact]
    public async Task WriteAsync_UsesStableCamelCaseShapeAndEmptyArrays()
    {
        var writer = new StringWriter();
        var output = new JsonOutput(writer);
        var envelope = new SearchEnvelope<object>(
            "1.0",
            "configuration",
            "success",
            Query: null,
            new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero),
            Results: [],
            Errors: []);

        await output.WriteAsync(envelope, compact: true, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("configuration", root.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("query").ValueKind);
        Assert.Equal(0, root.GetProperty("results").GetArrayLength());
        Assert.Equal(0, root.GetProperty("errors").GetArrayLength());
        Assert.False(root.TryGetProperty("page", out _));
        Assert.EndsWith("Z", root.GetProperty("generatedAtUtc").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_OmitsUnusedNextHints()
    {
        var writer = new StringWriter();
        await new JsonOutput(writer).WriteAsync(SearchEnvelopeFactory.Error("configuration", [new SearchError("command", null, "x", "x", false)], TimeProvider.System.GetUtcNow()), true, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("errors")[0].TryGetProperty("next", out _));
    }

    [Fact]
    public async Task WriteAsync_OmitsNullPageMetadataMembers()
    {
        var writer = new StringWriter();
        var envelope = new SearchEnvelope<object>("2.0", "wikiPageCatalog", "success", null, TimeProvider.System.GetUtcNow(), [], [], new PageMetadataDocument(null, 50, 1, null, null, null));

        await new JsonOutput(writer).WriteAsync(envelope, true, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        JsonElement page = document.RootElement.GetProperty("page");
        Assert.Equal(50, page.GetProperty("requestedTop").GetInt32());
        Assert.Equal(1, page.GetProperty("returnedCount").GetInt32());
        Assert.False(page.TryGetProperty("skip", out _));
        Assert.False(page.TryGetProperty("totalCount", out _));
        Assert.False(page.TryGetProperty("continuationToken", out _));
        Assert.False(page.TryGetProperty("serviceInfoCode", out _));
    }

    [Fact]
    public async Task WriteAsync_SerializesEnumsAsLowerCamelCaseStrings()
    {
        var writer = new StringWriter();
        var output = new JsonOutput(writer);

        await output.WriteAsync(new { State = SampleState.InProgress }, compact: true, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("inProgress", document.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void CreateError_SortsErrorsDeterministicallyAndRedactsUnknownExceptionMessages()
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(
            "configuration",
            [
                new SearchError("project", "Zulu", "forbidden", "Denied", false),
                new SearchError("project", "Alpha", "timeout", "Timed out", true)
            ],
            new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal("Alpha", envelope.Errors[0].Project);
        Assert.Equal("Zulu", envelope.Errors[1].Project);

        SearchError safe = SearchError.FromException(new InvalidOperationException("secret PAT abc"));
        Assert.Equal("unexpectedError", safe.Code);
        Assert.DoesNotContain("secret PAT abc", safe.Message, StringComparison.Ordinal);
    }

    private enum SampleState
    {
        InProgress
    }
}
