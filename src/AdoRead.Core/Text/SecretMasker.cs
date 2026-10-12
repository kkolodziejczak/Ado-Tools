using System.Text.RegularExpressions;

namespace AdoRead.Text;

/// <summary>
/// Replaces credential-looking values with "***" before they leave the tool. Each replacement is
/// counted, so one run can report how many values were masked. The patterns are deliberately narrow:
/// ordinary assertion messages and 40-character git SHAs must pass through unchanged.
/// </summary>
public sealed class SecretMasker
{
    private const string Redacted = "***";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private static readonly Rule[] Rules =
    [
        new(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----", Redacted),
        new(@"(?i)(\b[a-z][a-z0-9+.\-]*://)[^/\s@]+:[^/\s@]+@", "$1" + Redacted + "@"),
        new(@"(?i)(authorization\s*:\s*(?:bearer|basic)\s+)[^\s""',;]+", "$1" + Redacted),
        new(@"(?i)((?:password|pwd|secret|token|apikey|api_key|accountkey|sharedaccesskey)\s*=\s*[""']?)[^;\s&""']+", "$1" + Redacted),
        new(@"(?i)(\bbearer\s+)[A-Za-z0-9\-._~+/]{12,}=*", "$1" + Redacted),
        new(@"\beyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*", Redacted),
        new(@"(?<![A-Za-z0-9])[A-Za-z0-9]{84}(?![A-Za-z0-9])", Redacted),
        new(@"(?<![A-Za-z0-9])[a-z2-7]{52}(?![A-Za-z0-9])", Redacted),
        new(@"(?<![A-Za-z0-9])AKIA[0-9A-Z]{16}(?![A-Za-z0-9])", Redacted)
    ];

    public int MaskedCount { get; private set; }

    /// <summary>Returns the text with every credential-looking value masked, and adds the replacements to <see cref="MaskedCount"/>.</summary>
    public string? Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string masked = text;
        foreach (Rule rule in Rules)
        {
            masked = rule.Regex.Replace(masked, match =>
            {
                MaskedCount++;
                return match.Result(rule.Replacement);
            });
        }

        return masked;
    }

    private sealed class Rule
    {
        public Rule(string expression, string replacement)
        {
            Regex = new Regex(expression, RegexOptions.CultureInvariant, MatchTimeout);
            Replacement = replacement;
        }

        public Regex Regex { get; }

        public string Replacement { get; }
    }
}
