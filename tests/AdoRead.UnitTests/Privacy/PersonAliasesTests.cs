using AdoRead.Output;
using AdoRead.Privacy;

namespace AdoRead.UnitTests.Privacy;

public sealed class PersonAliasesTests
{
    [Fact]
    public void Alias_IsStableForTheSamePersonAndKeyAndIgnoresCase()
    {
        Assert.Equal(
            new PersonAliases(TestPrivacy.Key).Alias("Engineer@Example.invalid"),
            new PersonAliases(TestPrivacy.Key).Alias("engineer@example.invalid"));
    }

    [Fact]
    public void Alias_DiffersAcrossKeys()
    {
        string first = new PersonAliases(TestPrivacy.Key).Alias("engineer@example.invalid")!;
        string second = new PersonAliases(Enumerable.Repeat((byte)9, 32).ToArray()).Alias("engineer@example.invalid")!;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Alias_HasTheDocumentedShapeAndHidesTheName()
    {
        string alias = new PersonAliases(TestPrivacy.Key).Alias("engineer@example.invalid")!;

        Assert.Matches("^person-[0-9a-f]{8}$", alias);
        Assert.DoesNotContain("engineer", alias, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Alias_LeavesAnAbsentIdentifierNull(string? identifier)
    {
        Assert.Null(new PersonAliases(TestPrivacy.Key).Alias(identifier));
    }

    [Fact]
    public void Person_ReturnsTheIdentityUnchangedWhenPeopleAreAllowed()
    {
        var identity = new IdentityDocument("Example Engineer", "id-1", "engineer@example.invalid", "descriptor", null, null);

        Assert.Same(identity, TestPrivacy.Full.Person(identity));
    }

    [Fact]
    public void Person_RemovesEveryDirectIdentifierWhenPeopleAreWithheld()
    {
        EffectivePrivacy privacy = TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Standard));
        var identity = new IdentityDocument(
            "Example Engineer",
            "00000000-0000-0000-0000-0000000000a1",
            "engineer@example.invalid",
            "descriptor-value",
            new Uri("https://dev.azure.com/fabricated/_apis/identities/1"),
            new Uri("https://dev.azure.com/fabricated/_api/_common/identityImage?id=1"));

        IdentityDocument? aliased = privacy.Person(identity);

        string alias = new PersonAliases(TestPrivacy.Key).Alias("engineer@example.invalid")!;
        Assert.Equal(alias, aliased!.UniqueName);
        Assert.Equal(alias, aliased.DisplayName);
        Assert.Equal(alias, aliased.Id);
        Assert.Null(aliased.Descriptor);
        Assert.Null(aliased.Url);
        Assert.Null(aliased.ImageUrl);
    }

    [Fact]
    public void Person_KeepsNullNull()
    {
        EffectivePrivacy privacy = TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Minimal));

        Assert.Null(privacy.Person((IdentityDocument?)null));
        Assert.Null(privacy.Person((string?)null));
    }

    [Fact]
    public void Section_ListsWithheldCategoriesAndAliasedPeopleOnlyForRelevantCategories()
    {
        EffectivePrivacy privacy = TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Standard));

        PrivacySection section = privacy.Section(
            [PrivacyCategory.Text, PrivacyCategory.Comments, PrivacyCategory.People, PrivacyCategory.CustomFields, PrivacyCategory.Images]);

        Assert.Equal("standard", section.Level);
        Assert.Equal(["customFields", "images"], section.Withheld);
        Assert.Equal(["people"], section.Aliased);
        Assert.False(section.RestrictedByCall);
    }

    [Fact]
    public void Section_ReportsNothingForMetadataOnlyCommands()
    {
        PrivacySection section = TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Minimal)).Section([]);

        Assert.Empty(section.Withheld);
        Assert.Empty(section.Aliased);
    }

    [Fact]
    public void Section_FlagsAPerCallRestriction()
    {
        var stored = new PrivacyPolicy(PrivacyLevel.Full);
        PrivacyPolicy effective = stored.Restrict(new PrivacyPolicy(PrivacyLevel.Minimal));

        PrivacySection section = new EffectivePrivacy(effective, stored, new PersonAliases(TestPrivacy.Key))
            .Section([PrivacyCategory.Text]);

        Assert.True(section.RestrictedByCall);
        Assert.Equal(["text"], section.Withheld);
        Assert.Equal("minimal", section.Level);
    }
}
