using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdoRead.Privacy;

/// <summary>
/// What the credential vault keeps for one organization: the policy and the key that makes person
/// aliases stable. The JSON form is versioned; an unknown version or a malformed value is rejected.
/// </summary>
public sealed record StoredPrivacyPolicy(PrivacyPolicy Policy, byte[] AliasKey)
{
    public const int CurrentVersion = 1;
    private const int AliasKeyLength = 32;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static StoredPrivacyPolicy CreateNew(PrivacyPolicy policy) =>
        new(policy, RandomNumberGenerator.GetBytes(AliasKeyLength));

    public static StoredPrivacyPolicy Parse(string json)
    {
        PolicyDocument? document = ReadDocument(json);
        if (document is null || document.Version != CurrentVersion || !PrivacyLevels.TryParse(document.Level, out PrivacyLevel level))
        {
            throw InvalidPolicy();
        }

        var overrides = new Dictionary<PrivacyCategory, bool>();
        foreach ((string name, string decision) in document.Overrides ?? new())
        {
            if (!PrivacyCategories.TryParse(name, out PrivacyCategory category) || decision is not ("allow" or "deny"))
            {
                throw InvalidPolicy();
            }

            overrides[category] = decision == "allow";
        }

        return new StoredPrivacyPolicy(new PrivacyPolicy(level, overrides), DecodeAliasKey(document.AliasKey));
    }

    public string ToJson()
    {
        var overrides = PrivacyCategories.All
            .Where(Policy.Overrides.ContainsKey)
            .ToDictionary(
                PrivacyCategories.NameOf,
                category => Policy.Overrides[category] ? "allow" : "deny");
        var document = new PolicyDocument(
            CurrentVersion,
            PrivacyLevels.NameOf(Policy.Level),
            overrides,
            Convert.ToBase64String(AliasKey));
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static PolicyDocument? ReadDocument(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PolicyDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw InvalidPolicy();
        }
    }

    private static byte[] DecodeAliasKey(string? value)
    {
        try
        {
            byte[] key = Convert.FromBase64String(value ?? string.Empty);
            return key.Length == AliasKeyLength ? key : throw InvalidPolicy();
        }
        catch (FormatException)
        {
            throw InvalidPolicy();
        }
    }

    private static PrivacyException InvalidPolicy() => new(
        "privacyPolicyInvalid",
        "The stored privacy policy is unreadable or uses an unknown version. Run 'privacy reset', then 'privacy set' in an interactive terminal.");

    private sealed record PolicyDocument(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("level")] string? Level,
        [property: JsonPropertyName("overrides")] Dictionary<string, string>? Overrides,
        [property: JsonPropertyName("aliasKey")] string? AliasKey);
}
