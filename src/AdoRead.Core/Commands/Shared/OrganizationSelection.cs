using AdoRead.Configuration;

namespace AdoRead.Commands.Shared;

/// <summary>Picks the organization a command acts on: an explicit URL, or the one in the configuration.</summary>
internal static class OrganizationSelection
{
    public static async Task<Uri> ResolveAsync(
        string? organization,
        string? configPath,
        IConfigurationStore configurationStore,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(organization))
        {
            if (!Uri.TryCreate(organization.Trim(), UriKind.Absolute, out Uri? organizationUrl))
            {
                throw new ConfigurationException(
                    "invalidOrganizationUrl",
                    "The organization URL must use the form https://dev.azure.com/{organization}.");
            }

            return AdoReadConfiguration.Create(organizationUrl).OrganizationUrl;
        }

        AdoReadConfiguration configuration = await configurationStore.LoadAsync(configPath, cancellationToken);
        return configuration.OrganizationUrl;
    }
}
