using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AdoRead.Text;

public sealed partial class RichTextNormalizer
{
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "div", "dl", "dt", "dd", "fieldset", "figcaption",
        "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main",
        "nav", "ol", "p", "pre", "section", "table", "tr", "ul"
    };

    public RichTextDocument Normalize(string? raw, string? format)
    {
        string? normalizedFormat = string.IsNullOrWhiteSpace(format) ? null : format.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(raw))
        {
            return new RichTextDocument(null, normalizedFormat, null);
        }

        string plainText = string.Equals(normalizedFormat, "html", StringComparison.Ordinal)
            ? HtmlToPlainText(raw)
            : NormalizeLineEndings(raw);
        return new RichTextDocument(raw, normalizedFormat, plainText);
    }

    private static string HtmlToPlainText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        RemoveNonContentNodes(document);
        AddSemanticLineBreaks(document);
        string decoded = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
        return NormalizePlainText(decoded);
    }

    private static void RemoveNonContentNodes(HtmlDocument document)
    {
        HtmlNodeCollection? nodes = document.DocumentNode.SelectNodes("//script|//style|//noscript");
        if (nodes is null)
        {
            return;
        }

        foreach (HtmlNode node in nodes.ToArray())
        {
            node.Remove();
        }
    }

    private static void AddSemanticLineBreaks(HtmlDocument document)
    {
        foreach (HtmlNode node in document.DocumentNode.Descendants().ToArray())
        {
            if (string.Equals(node.Name, "br", StringComparison.OrdinalIgnoreCase))
            {
                node.ParentNode.ReplaceChild(document.CreateTextNode("\n"), node);
            }
            else if (BlockElements.Contains(node.Name))
            {
                node.AppendChild(document.CreateTextNode("\n"));
            }
        }
    }

    private static string NormalizePlainText(string value)
    {
        string[] lines = NormalizeLineEndings(value)
            .Replace('\u00A0', ' ')
            .Split('\n');
        return string.Join(
            '\n',
            lines
                .Select(line => HorizontalWhitespace().Replace(line, " ").Trim())
                .Where(line => line.Length > 0));
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    [GeneratedRegex("[\\t ]+")]
    private static partial Regex HorizontalWhitespace();
}
