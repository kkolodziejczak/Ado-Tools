using AdoRead.Privacy;

namespace AdoRead.Wikis;

/// <summary>Withholds Wiki page bodies and search highlights. Page paths and file names stay visible.</summary>
public static class WikiPrivacy
{
    public static IReadOnlyList<PrivacyCategory> PageCategories { get; } = [PrivacyCategory.Text];

    public static IReadOnlyList<PrivacyCategory> SearchCategories { get; } = [PrivacyCategory.Text];

    public static WikiPageDocument Page(WikiPageDocument page, EffectivePrivacy privacy) => page with
    {
        Content = privacy.Allows(PrivacyCategory.Text) ? page.Content : null
    };

    public static WikiSearchHitDocument SearchHit(WikiSearchHitDocument hit, EffectivePrivacy privacy) => hit with
    {
        Matches = privacy.Allows(PrivacyCategory.Text) ? hit.Matches : []
    };
}
