using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Privacy;
using AdoRead.UnitTests.Credentials;

namespace AdoRead.UnitTests.Privacy;

public sealed class PrivacyPolicyStoreTests
{
    private static readonly Uri Organization = new("https://dev.azure.com/fabricated/");

    [Fact]
    public void PrivacyAccountIsDistinctFromTheTokenAccount()
    {
        Assert.Equal("https://dev.azure.com/fabricated#privacy", CredentialAccount.ForPrivacy(Organization));
        Assert.Equal("https://dev.azure.com/fabricated", CredentialAccount.For(Organization));
    }

    [Fact]
    public void Find_ReturnsNullWhenNothingIsStored()
    {
        Assert.Null(new PrivacyPolicyStore(new MemoryCredentialVault()).Find(Organization));
    }

    [Fact]
    public void SaveThenFind_RoundTripsThePolicyWithoutTouchingTheTokenAccount()
    {
        var vault = new MemoryCredentialVault();
        var store = new PrivacyPolicyStore(vault);

        store.Save(Organization, new StoredPrivacyPolicy(new PrivacyPolicy(PrivacyLevel.Standard), TestPrivacy.Key));

        StoredPrivacyPolicy? found = store.Find(Organization);
        Assert.NotNull(found);
        Assert.Equal(PrivacyLevel.Standard, found.Policy.Level);
        Assert.Equal(TestPrivacy.Key, found.AliasKey);
        Assert.Null(vault.Read(CredentialAccount.For(Organization)));
    }

    [Fact]
    public void Remove_ReportsWhetherAPolicyWasThere()
    {
        var store = new PrivacyPolicyStore(new MemoryCredentialVault());
        store.Save(Organization, StoredPrivacyPolicy.CreateNew(new PrivacyPolicy(PrivacyLevel.Minimal)));

        Assert.True(store.Remove(Organization));
        Assert.False(store.Remove(Organization));
        Assert.Null(store.Find(Organization));
    }

    [Fact]
    public void Find_ReportsAnUnreadableStoredPolicyAsInvalid()
    {
        var vault = new MemoryCredentialVault();
        vault.Write(CredentialAccount.ForPrivacy(Organization), "{broken");

        Assert.Equal(
            "privacyPolicyInvalid",
            Assert.Throws<PrivacyException>(() => new PrivacyPolicyStore(vault).Find(Organization)).Code);
    }

    [Fact]
    public void Find_LetsAnUnavailableVaultFailAsACredentialError()
    {
        Assert.Throws<CredentialException>(() => new PrivacyPolicyStore(new MemoryCredentialVault(available: false)).Find(Organization));
    }
}
