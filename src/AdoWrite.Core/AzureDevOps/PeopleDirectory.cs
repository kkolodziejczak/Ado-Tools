using Microsoft.VisualStudio.Services.Identity;
using Microsoft.VisualStudio.Services.Identity.Client;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>Who the token belongs to, and who a mention names.</summary>
public interface IPeopleDirectory
{
    Task<Person> GetSignedInUserAsync(CancellationToken cancellationToken);

    /// <summary>Active people whose mail address or account name matches <paramref name="value"/>.</summary>
    Task<IReadOnlyList<Person>> FindAsync(string value, CancellationToken cancellationToken);
}

public sealed record Person(Guid Id, string DisplayName);

public sealed class SdkPeopleDirectory(VssConnection connection, IdentityHttpClient identities) : IPeopleDirectory
{
    public async Task<Person> GetSignedInUserAsync(CancellationToken cancellationToken)
    {
        await connection.ConnectAsync(cancellationToken);
        Identity identity = connection.AuthorizedIdentity;
        return new Person(identity.Id, identity.DisplayName);
    }

    public async Task<IReadOnlyList<Person>> FindAsync(string value, CancellationToken cancellationToken)
    {
        IdentitiesCollection matches = await identities.ReadIdentitiesAsync(
            IdentitySearchFilter.General,
            value,
            QueryMembership.None,
            propertyNameFilters: null,
            userState: null,
            cancellationToken);
        return matches
            .Where(identity => identity is { IsActive: true })
            .Select(identity => new Person(identity.Id, identity.DisplayName))
            .ToArray();
    }
}
