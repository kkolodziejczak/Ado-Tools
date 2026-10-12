using AdoRead.Text;

namespace AdoRead.UnitTests.Text;

public sealed class RichTextNormalizerTests
{
    private readonly RichTextNormalizer _normalizer = new();

    [Fact]
    public void Normalize_HtmlPreservesRawAndProducesReadablePlainText()
    {
        const string html = "<p>First <strong>line</strong></p><p>Second &amp; final<br>third</p><script>secret()</script>";

        RichTextDocument result = _normalizer.Normalize(html, "html");

        Assert.Equal(html, result.Raw);
        Assert.Equal("html", result.Format);
        Assert.Equal("First line\nSecond & final\nthird", result.PlainText);
    }

    [Fact]
    public void Normalize_MarkdownPreservesReadableSourceText()
    {
        const string markdown = "## Goal\n\n- first\n- second";

        RichTextDocument result = _normalizer.Normalize(markdown, "markdown");

        Assert.Equal(markdown, result.Raw);
        Assert.Equal("markdown", result.Format);
        Assert.Equal(markdown, result.PlainText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_EmptyInputHasStableNullContent(string? value)
    {
        RichTextDocument result = _normalizer.Normalize(value, "html");

        Assert.Null(result.Raw);
        Assert.Equal("html", result.Format);
        Assert.Null(result.PlainText);
    }
}
