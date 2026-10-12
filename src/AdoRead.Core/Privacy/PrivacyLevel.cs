namespace AdoRead.Privacy;

/// <summary>A preset of content categories. Per-category overrides refine it.</summary>
public enum PrivacyLevel
{
    Minimal,
    Standard,
    Full
}

public static class PrivacyLevels
{
    public static IReadOnlyList<PrivacyLevel> All { get; } = Enum.GetValues<PrivacyLevel>();

    public static string NameOf(PrivacyLevel level) => level switch
    {
        PrivacyLevel.Minimal => "minimal",
        PrivacyLevel.Standard => "standard",
        PrivacyLevel.Full => "full",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };

    public static bool TryParse(string? value, out PrivacyLevel level)
    {
        foreach (PrivacyLevel candidate in All)
        {
            if (string.Equals(NameOf(candidate), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }
}
