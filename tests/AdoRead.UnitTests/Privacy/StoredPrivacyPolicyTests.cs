using System.Text.Json;
using AdoRead.Privacy;

namespace AdoRead.UnitTests.Privacy;

public sealed class StoredPrivacyPolicyTests
{
    private const string ValidKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public void ToJson_WritesTheVersionedCompactDocument()
    {
        var stored = new StoredPrivacyPolicy(
            new PrivacyPolicy(PrivacyLevel.Standard, new Dictionary<PrivacyCategory, bool> { [PrivacyCategory.Comments] = false }),
            TestPrivacy.Key);

        using JsonDocument document = JsonDocument.Parse(stored.ToJson());

        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("standard", document.RootElement.GetProperty("level").GetString());
        Assert.Equal("deny", document.RootElement.GetProperty("overrides").GetProperty("comments").GetString());
        Assert.Equal(Convert.ToBase64String(TestPrivacy.Key), document.RootElement.GetProperty("aliasKey").GetString());
    }

    [Fact]
    public void Parse_RoundTripsThePolicyAndTheAliasKey()
    {
        var stored = new StoredPrivacyPolicy(
            new PrivacyPolicy(PrivacyLevel.Full, new Dictionary<PrivacyCategory, bool> { [PrivacyCategory.Images] = false }),
            TestPrivacy.Key);

        StoredPrivacyPolicy parsed = StoredPrivacyPolicy.Parse(stored.ToJson());

        Assert.Equal(PrivacyLevel.Full, parsed.Policy.Level);
        Assert.False(parsed.Policy.Allows(PrivacyCategory.Images));
        Assert.True(parsed.Policy.Allows(PrivacyCategory.Comments));
        Assert.Equal(TestPrivacy.Key, parsed.AliasKey);
    }

    [Fact]
    public void CreateNew_DrawsA32ByteAliasKeyEachTime()
    {
        byte[] first = StoredPrivacyPolicy.CreateNew(new PrivacyPolicy(PrivacyLevel.Minimal)).AliasKey;
        byte[] second = StoredPrivacyPolicy.CreateNew(new PrivacyPolicy(PrivacyLevel.Minimal)).AliasKey;

        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"version\":2,\"level\":\"full\",\"overrides\":{},\"aliasKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\"}")]
    [InlineData("{\"level\":\"full\",\"overrides\":{},\"aliasKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\"}")]
    [InlineData("{\"version\":1,\"level\":\"everything\",\"overrides\":{},\"aliasKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\"}")]
    [InlineData("{\"version\":1,\"level\":\"full\",\"overrides\":{\"comments\":\"maybe\"},\"aliasKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\"}")]
    [InlineData("{\"version\":1,\"level\":\"full\",\"overrides\":{\"passwords\":\"deny\"},\"aliasKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\"}")]
    [InlineData("{\"version\":1,\"level\":\"full\",\"overrides\":{},\"aliasKey\":\"c2hvcnQ=\"}")]
    [InlineData("{\"version\":1,\"level\":\"full\",\"overrides\":{},\"aliasKey\":\"not base64!\"}")]
    public void Parse_RejectsCorruptOrUnknownDocuments(string json)
    {
        Assert.Equal("privacyPolicyInvalid", Assert.Throws<PrivacyException>(() => StoredPrivacyPolicy.Parse(json)).Code);
    }

    [Fact]
    public void Parse_AcceptsAMinimalValidDocument()
    {
        StoredPrivacyPolicy parsed = StoredPrivacyPolicy.Parse(
            "{\"version\":1,\"level\":\"minimal\",\"overrides\":{},\"aliasKey\":\"" + ValidKey + "\"}");

        Assert.Empty(parsed.Policy.AllowedCategories);
        Assert.Equal(32, parsed.AliasKey.Length);
    }
}
