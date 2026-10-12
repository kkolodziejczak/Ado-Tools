namespace AdoRead.Configuration;

public sealed record AdoReadConfiguration
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultRequestTimeoutSeconds = 30;
    public const int DefaultMaxParallelProjects = 4;

    public int SchemaVersion { get; }

    public Uri OrganizationUrl { get; }

    public TimeSpan RequestTimeout { get; }

    public int MaxParallelProjects { get; }

    /// <summary>
    /// Optional default project for the team-scoped sprint commands, so they need no flags after
    /// a one-time setup. Null means the caller must pass --project.
    /// </summary>
    public string? DefaultProject { get; }

    /// <summary>Optional default team, used the same way as <see cref="DefaultProject"/>.</summary>
    public string? DefaultTeam { get; }

    public static AdoReadConfiguration Create(
        Uri organizationUrl,
        int requestTimeoutSeconds = DefaultRequestTimeoutSeconds,
        int maxParallelProjects = DefaultMaxParallelProjects,
        string? defaultProject = null,
        string? defaultTeam = null)
    {
        Uri canonicalOrganizationUrl = ValidateOrganizationUrl(organizationUrl);
        ValidateRequestTimeout(requestTimeoutSeconds);
        ValidateMaxParallelProjects(maxParallelProjects);

        return new AdoReadConfiguration(
            CurrentSchemaVersion,
            canonicalOrganizationUrl,
            TimeSpan.FromSeconds(requestTimeoutSeconds),
            maxParallelProjects,
            Trimmed(defaultProject),
            Trimmed(defaultTeam));
    }

    private AdoReadConfiguration(
        int schemaVersion,
        Uri organizationUrl,
        TimeSpan requestTimeout,
        int maxParallelProjects,
        string? defaultProject,
        string? defaultTeam)
    {
        SchemaVersion = schemaVersion;
        OrganizationUrl = organizationUrl;
        RequestTimeout = requestTimeout;
        MaxParallelProjects = maxParallelProjects;
        DefaultProject = defaultProject;
        DefaultTeam = defaultTeam;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Uri ValidateOrganizationUrl(Uri organizationUrl)
    {
        ArgumentNullException.ThrowIfNull(organizationUrl);

        if (!organizationUrl.IsAbsoluteUri)
        {
            throw InvalidOrganizationUrl();
        }

        string[] segments = organizationUrl.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool isValid = organizationUrl.Scheme == Uri.UriSchemeHttps
            && string.Equals(organizationUrl.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && organizationUrl.IsDefaultPort
            && string.IsNullOrEmpty(organizationUrl.UserInfo)
            && string.IsNullOrEmpty(organizationUrl.Query)
            && string.IsNullOrEmpty(organizationUrl.Fragment)
            && segments.Length == 1;

        if (!isValid)
        {
            throw InvalidOrganizationUrl();
        }

        var builder = new UriBuilder(Uri.UriSchemeHttps, "dev.azure.com")
        {
            Path = $"/{segments[0]}/"
        };
        return builder.Uri;
    }

    private static ConfigurationException InvalidOrganizationUrl() =>
        new(
            "invalidOrganizationUrl",
            "The organization URL must use the form https://dev.azure.com/{organization}.");

    private static void ValidateRequestTimeout(int requestTimeoutSeconds)
    {
        if (requestTimeoutSeconds is < 1 or > 300)
        {
            throw new ConfigurationException(
                "invalidRequestTimeout",
                "Request timeout must be between 1 and 300 seconds.");
        }
    }

    private static void ValidateMaxParallelProjects(int maxParallelProjects)
    {
        if (maxParallelProjects is < 1 or > 16)
        {
            throw new ConfigurationException(
                "invalidMaxParallelProjects",
                "Maximum parallel projects must be between 1 and 16.");
        }
    }
}
