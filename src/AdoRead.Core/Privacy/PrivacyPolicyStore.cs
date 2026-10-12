using AdoRead.Credentials;

namespace AdoRead.Privacy;

/// <summary>Keeps one privacy policy per organization in the OS credential vault, never in a file.</summary>
public sealed class PrivacyPolicyStore(ICredentialVault vault)
{
    public string VaultKind => vault.Kind;

    public StoredPrivacyPolicy? Find(Uri organizationUrl)
    {
        string? json = vault.Read(CredentialAccount.ForPrivacy(organizationUrl));
        return string.IsNullOrEmpty(json) ? null : StoredPrivacyPolicy.Parse(json);
    }

    public void Save(Uri organizationUrl, StoredPrivacyPolicy policy) =>
        vault.Write(CredentialAccount.ForPrivacy(organizationUrl), policy.ToJson());

    public bool Remove(Uri organizationUrl) => vault.Remove(CredentialAccount.ForPrivacy(organizationUrl));
}
