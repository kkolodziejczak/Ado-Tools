using AdoRead.Privacy;

namespace AdoRead.Sprints;

/// <summary>Withholds the free-text description of a team. Names and ids stay visible.</summary>
public static class TeamPrivacy
{
    public static IReadOnlyList<PrivacyCategory> Categories { get; } = [PrivacyCategory.Text];

    public static TeamDocument Apply(TeamDocument team, EffectivePrivacy privacy) => team with
    {
        Description = privacy.Allows(PrivacyCategory.Text) ? team.Description : null
    };
}
