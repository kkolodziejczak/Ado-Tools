using AdoRead.Output;

namespace AdoRead.Privacy;

/// <summary>
/// The rules that apply to one read: the stored policy restricted by the call, and the aliases for
/// people. Readers and document filters ask this object; they never read the vault themselves.
/// </summary>
public sealed class EffectivePrivacy(PrivacyPolicy policy, PrivacyPolicy storedPolicy, PersonAliases aliases)
{
    public PrivacyPolicy Policy { get; } = policy;

    public bool RestrictedByCall { get; } = !policy.HasSameAllowancesAs(storedPolicy);

    public bool Allows(PrivacyCategory category) => Policy.Allows(category);

    /// <summary>A person's unique name or id: unchanged when people are allowed, otherwise an alias.</summary>
    public string? Person(string? identifier) =>
        Allows(PrivacyCategory.People) ? identifier : aliases.Alias(identifier);

    /// <summary>
    /// An identity with its name, id, descriptor and image links removed when people are withheld.
    /// Identities are aliased under their unique name, then id, then display name.
    /// </summary>
    public IdentityDocument? Person(IdentityDocument? identity)
    {
        if (identity is null || Allows(PrivacyCategory.People))
        {
            return identity;
        }

        string? alias = aliases.Alias(identity.UniqueName ?? identity.Id ?? identity.DisplayName);
        return alias is null
            ? null
            : new IdentityDocument(alias, alias, alias, Descriptor: null, Url: null, ImageUrl: null);
    }

    public PrivacySection Section(IReadOnlyList<PrivacyCategory> relevant)
    {
        string[] withheld = relevant
            .Where(category => category != PrivacyCategory.People && !Allows(category))
            .Select(PrivacyCategories.NameOf)
            .ToArray();
        string[] aliased = relevant.Contains(PrivacyCategory.People) && !Allows(PrivacyCategory.People)
            ? [PrivacyCategories.NameOf(PrivacyCategory.People)]
            : [];
        return new PrivacySection(PrivacyLevels.NameOf(Policy.Level), withheld, aliased, RestrictedByCall);
    }
}
