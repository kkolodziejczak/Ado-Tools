using AdoRead.Privacy;

namespace AdoRead.UnitTests.Privacy;

public sealed class PrivacyPolicyTests
{
    public static TheoryData<PrivacyLevel, PrivacyLevel> LevelPairs()
    {
        var pairs = new TheoryData<PrivacyLevel, PrivacyLevel>();
        foreach (PrivacyLevel stored in PrivacyLevels.All)
        {
            foreach (PrivacyLevel call in PrivacyLevels.All)
            {
                pairs.Add(stored, call);
            }
        }

        return pairs;
    }

    [Fact]
    public void Minimal_AllowsNoContentCategory()
    {
        Assert.Empty(new PrivacyPolicy(PrivacyLevel.Minimal).AllowedCategories);
    }

    [Fact]
    public void Standard_AllowsTextAndCommentsOnly()
    {
        Assert.Equal(
            [PrivacyCategory.Text, PrivacyCategory.Comments],
            new PrivacyPolicy(PrivacyLevel.Standard).AllowedCategories);
    }

    [Fact]
    public void Full_AllowsEveryCategory()
    {
        Assert.Equal(PrivacyCategories.All, new PrivacyPolicy(PrivacyLevel.Full).AllowedCategories);
    }

    [Fact]
    public void Overrides_RefineTheLevelPresetInBothDirections()
    {
        var policy = new PrivacyPolicy(
            PrivacyLevel.Standard,
            new Dictionary<PrivacyCategory, bool>
            {
                [PrivacyCategory.Comments] = false,
                [PrivacyCategory.CustomFields] = true
            });

        Assert.True(policy.Allows(PrivacyCategory.Text));
        Assert.False(policy.Allows(PrivacyCategory.Comments));
        Assert.True(policy.Allows(PrivacyCategory.CustomFields));
        Assert.False(policy.Allows(PrivacyCategory.Images));
    }

    [Theory]
    [MemberData(nameof(LevelPairs))]
    public void Restrict_AllowsOnlyWhatBothPoliciesAllow(PrivacyLevel stored, PrivacyLevel call)
    {
        var storedPolicy = new PrivacyPolicy(stored);
        var callPolicy = new PrivacyPolicy(call);

        PrivacyPolicy restricted = storedPolicy.Restrict(callPolicy);

        foreach (PrivacyCategory category in PrivacyCategories.All)
        {
            Assert.Equal(storedPolicy.Allows(category) && callPolicy.Allows(category), restricted.Allows(category));
        }
    }

    [Fact]
    public void Restrict_KeepsAnOverrideDenialFromEitherSide()
    {
        var stored = new PrivacyPolicy(PrivacyLevel.Full, new Dictionary<PrivacyCategory, bool> { [PrivacyCategory.Comments] = false });
        PrivacyPolicy call = PrivacyPolicy.FromNames("full", [], ["images"]);

        PrivacyPolicy restricted = stored.Restrict(call);

        Assert.False(restricted.Allows(PrivacyCategory.Comments));
        Assert.False(restricted.Allows(PrivacyCategory.Images));
        Assert.True(restricted.Allows(PrivacyCategory.Text));
    }

    [Fact]
    public void Restrict_TakesTheStricterLevel()
    {
        Assert.Equal(PrivacyLevel.Minimal, new PrivacyPolicy(PrivacyLevel.Full).Restrict(new PrivacyPolicy(PrivacyLevel.Minimal)).Level);
    }

    [Fact]
    public void IsLooserThan_IsTrueOnlyWhenAGrantIsNew()
    {
        var full = new PrivacyPolicy(PrivacyLevel.Full);
        var standard = new PrivacyPolicy(PrivacyLevel.Standard);

        Assert.True(full.IsLooserThan(standard));
        Assert.False(standard.IsLooserThan(full));
        Assert.False(standard.IsLooserThan(new PrivacyPolicy(PrivacyLevel.Standard)));
        Assert.True(new PrivacyPolicy(PrivacyLevel.Minimal, new Dictionary<PrivacyCategory, bool> { [PrivacyCategory.Images] = true })
            .IsLooserThan(new PrivacyPolicy(PrivacyLevel.Minimal)));
    }

    [Fact]
    public void FromNames_IsCaseInsensitiveForLevelsAndCategories()
    {
        PrivacyPolicy policy = PrivacyPolicy.FromNames("FULL", ["CustomFields"], ["Comments"]);

        Assert.Equal(PrivacyLevel.Full, policy.Level);
        Assert.True(policy.Allows(PrivacyCategory.CustomFields));
        Assert.False(policy.Allows(PrivacyCategory.Comments));
    }

    [Theory]
    [InlineData("everything")]
    [InlineData("")]
    public void FromNames_RejectsAnUnknownLevel(string level)
    {
        Assert.Equal("invalidPrivacyLevel", Assert.Throws<PrivacyException>(() => PrivacyPolicy.FromNames(level, [], [])).Code);
    }

    [Fact]
    public void FromNames_RejectsAnUnknownCategory()
    {
        Assert.Equal(
            "invalidPrivacyCategory",
            Assert.Throws<PrivacyException>(() => PrivacyPolicy.FromNames("full", [], ["passwords"])).Code);
    }

    [Fact]
    public void FromNames_RejectsACategoryThatIsBothAllowedAndDenied()
    {
        Assert.Equal(
            "conflictingPrivacyOverrides",
            Assert.Throws<PrivacyException>(() => PrivacyPolicy.FromNames("standard", ["images"], ["images"])).Code);
    }
}
