using AdoRead.Credentials;
using AdoRead.Privacy;
using AdoRead.UnitTests.Credentials;

namespace AdoRead.UnitTests.Privacy;

/// <summary>Builds the privacy rules a test reads under, with a fixed alias key so aliases are predictable within a test run.</summary>
internal static class TestPrivacy
{
    public static byte[] Key { get; } = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    public static EffectivePrivacy Full => For(new PrivacyPolicy(PrivacyLevel.Full));

    public static EffectivePrivacy For(PrivacyPolicy policy) =>
        new(policy, policy, new PersonAliases(Key));

    /// <summary>A vault whose organizations used by the command tests already have a full policy, so reads reach their content.</summary>
    public static MemoryCredentialVault ConsentedVault()
    {
        var vault = new MemoryCredentialVault();
        foreach (string organization in new[] { "https://dev.azure.com/example", "https://dev.azure.com/fabricated" })
        {
            vault.Write(
                CredentialAccount.ForPrivacy(new Uri(organization)),
                new StoredPrivacyPolicy(new PrivacyPolicy(PrivacyLevel.Full), Key).ToJson());
        }

        return vault;
    }
}
