using System.Collections;
using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace AdoRead.Output;

public static class AdoValueConverter
{
    public static JsonElement ToJsonElement(object? value)
    {
        object? safe = ToSafeValue(value);
        return JsonSerializer.SerializeToElement(safe, safe?.GetType() ?? typeof(object));
    }

    private static object? ToSafeValue(object? value)
    {
        if (value is null or string or bool
            or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or DateTime or DateTimeOffset or Guid)
        {
            return value;
        }

        return value switch
        {
            Uri uri => uri.AbsoluteUri,
            JsonElement json => json.Clone(),
            IdentityDocument identity => IdentityValue(identity),
            JToken token => TokenValue(token),
            IDictionary dictionary => DictionaryValue(dictionary),
            IEnumerable enumerable => enumerable.Cast<object?>().Select(ToSafeValue).ToArray(),
            _ => UnsupportedValue(value)
        };
    }

    private static IReadOnlyDictionary<string, object?> IdentityValue(IdentityDocument identity) =>
        new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["descriptor"] = identity.Descriptor,
            ["displayName"] = identity.DisplayName,
            ["id"] = identity.Id,
            ["imageUrl"] = identity.ImageUrl?.AbsoluteUri,
            ["uniqueName"] = identity.UniqueName,
            ["url"] = identity.Url?.AbsoluteUri
        };

    private static object? TokenValue(JToken token) => token switch
    {
        JValue value => ToSafeValue(value.Value),
        JObject value => value.Properties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(
                property => property.Name,
                property => TokenValue(property.Value),
                StringComparer.Ordinal),
        JArray value => value.Select(TokenValue).ToArray(),
        _ when token.Type is JTokenType.Null or JTokenType.Undefined => null,
        _ => UnsupportedValue(token)
    };

    private static IReadOnlyDictionary<string, object?> DictionaryValue(IDictionary dictionary)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
            result[key] = ToSafeValue(entry.Value);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, object?> UnsupportedValue(object value) =>
        new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sourceType"] = value.GetType().FullName ?? value.GetType().Name,
            ["value"] = Convert.ToString(value, CultureInfo.InvariantCulture)
        };
}
