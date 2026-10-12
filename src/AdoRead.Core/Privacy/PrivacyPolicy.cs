namespace AdoRead.Privacy;

/// <summary>
/// The content one organization allows to leave the tool: a level preset plus explicit per-category
/// overrides. Metadata is always allowed and is not represented here.
/// </summary>
public sealed class PrivacyPolicy
{
    public PrivacyPolicy(PrivacyLevel level, IReadOnlyDictionary<PrivacyCategory, bool>? overrides = null)
    {
        Level = level;
        Overrides = overrides is null
            ? new Dictionary<PrivacyCategory, bool>()
            : new Dictionary<PrivacyCategory, bool>(overrides);
    }

    public PrivacyLevel Level { get; }

    /// <summary>Explicit decisions on top of the level preset: true allows the category, false denies it.</summary>
    public IReadOnlyDictionary<PrivacyCategory, bool> Overrides { get; }

    /// <summary>
    /// Builds a policy from command-line names. Names are case-insensitive. A category cannot be both
    /// allowed and denied.
    /// </summary>
    public static PrivacyPolicy FromNames(string level, IEnumerable<string> allowed, IEnumerable<string> denied)
    {
        if (!PrivacyLevels.TryParse(level, out PrivacyLevel parsedLevel))
        {
            throw new PrivacyException(
                "invalidPrivacyLevel",
                "The privacy level must be one of: minimal, standard, full.");
        }

        var overrides = new Dictionary<PrivacyCategory, bool>();
        foreach (string name in allowed)
        {
            overrides[ParseCategory(name)] = true;
        }

        foreach (string name in denied)
        {
            PrivacyCategory category = ParseCategory(name);
            if (overrides.GetValueOrDefault(category))
            {
                throw new PrivacyException(
                    "conflictingPrivacyOverrides",
                    $"The category '{PrivacyCategories.NameOf(category)}' cannot be both allowed and denied.");
            }

            overrides[category] = false;
        }

        return new PrivacyPolicy(parsedLevel, overrides);
    }

    public bool Allows(PrivacyCategory category) =>
        Overrides.TryGetValue(category, out bool allowed) ? allowed : PresetAllows(Level, category);

    public IReadOnlyList<PrivacyCategory> AllowedCategories =>
        PrivacyCategories.All.Where(Allows).ToArray();

    /// <summary>The intersection of two policies. It can only deny more than either input, never allow more.</summary>
    public PrivacyPolicy Restrict(PrivacyPolicy other)
    {
        PrivacyLevel level = (PrivacyLevel)Math.Min((int)Level, (int)other.Level);
        var overrides = new Dictionary<PrivacyCategory, bool>();
        foreach (PrivacyCategory category in PrivacyCategories.All)
        {
            bool allowed = Allows(category) && other.Allows(category);
            if (allowed != PresetAllows(level, category))
            {
                overrides[category] = allowed;
            }
        }

        return new PrivacyPolicy(level, overrides);
    }

    /// <summary>True when this policy grants at least one category that <paramref name="other"/> does not.</summary>
    public bool IsLooserThan(PrivacyPolicy other) =>
        PrivacyCategories.All.Any(category => Allows(category) && !other.Allows(category));

    public bool HasSameAllowancesAs(PrivacyPolicy other) =>
        PrivacyCategories.All.All(category => Allows(category) == other.Allows(category));

    private static PrivacyCategory ParseCategory(string name) =>
        PrivacyCategories.TryParse(name, out PrivacyCategory category)
            ? category
            : throw new PrivacyException(
                "invalidPrivacyCategory",
                $"The privacy category must be one of: {string.Join(", ", PrivacyCategories.All.Select(PrivacyCategories.NameOf))}.");

    private static bool PresetAllows(PrivacyLevel level, PrivacyCategory category) => level switch
    {
        PrivacyLevel.Minimal => false,
        PrivacyLevel.Standard => category is PrivacyCategory.Text or PrivacyCategory.Comments
            or PrivacyCategory.TestMessages or PrivacyCategory.StackTraces,
        PrivacyLevel.Full => true,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };
}
