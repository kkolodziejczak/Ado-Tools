using AdoRead.Output;

namespace AdoRead.Privacy;

/// <summary>
/// The only way a read command obtains content rules. Without a stored policy it refuses, so nothing
/// is read or returned until a person has set one.
/// </summary>
public sealed class PrivacyGate(PrivacyPolicyStore store, ToolCommandNames names)
{
    public EffectivePrivacy Require(Uri organizationUrl, PrivacyRequest request)
    {
        PrivacyPolicy call = request.ToPolicy();
        StoredPrivacyPolicy stored = store.Find(organizationUrl)
            ?? throw new PrivacyException(
                "privacyPolicyRequired",
                $"No privacy policy is set for {organizationUrl}. A person must run '{names.Invocation("privacy set <minimal|standard|full>")}' "
                + "in an interactive terminal before content can be read. No content was returned.");
        return new EffectivePrivacy(stored.Policy.Restrict(call), stored.Policy, new PersonAliases(stored.AliasKey));
    }
}
