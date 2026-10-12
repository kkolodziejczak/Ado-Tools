using AdoRead.Privacy;

namespace AdoRead.IntegrationTests.Privacy;

/// <summary>The rules of a fully permitted policy, for live tests that exercise the readers directly.</summary>
internal static class FullPrivacy
{
    public static EffectivePrivacy Value { get; } = new(
        new PrivacyPolicy(PrivacyLevel.Full),
        new PrivacyPolicy(PrivacyLevel.Full),
        new PersonAliases(new byte[32]));
}
