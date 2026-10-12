using System.Security.Cryptography;
using System.Text;

namespace AdoRead.Privacy;

/// <summary>
/// Replaces a person identifier with a stable, non-reversible alias. The same person gets the same
/// alias in every call for one organization, because the alias is a keyed hash of the lowercase
/// unique name or identity id. Without the vault key the alias cannot be matched back to a name.
/// </summary>
public sealed class PersonAliases(byte[] key)
{
    public string? Alias(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        byte[] digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(identifier.Trim().ToLowerInvariant()));
        return $"person-{Convert.ToHexString(digest, 0, 4).ToLowerInvariant()}";
    }
}
