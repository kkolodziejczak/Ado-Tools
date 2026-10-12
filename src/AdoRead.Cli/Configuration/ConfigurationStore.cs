using System.Text.Json;

namespace AdoRead.Configuration;

public sealed class ConfigurationStore : IConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _userProfileDirectory;

    public ConfigurationStore()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public ConfigurationStore(string userProfileDirectory)
    {
        _userProfileDirectory = userProfileDirectory;
    }

    /// <summary>
    /// Resolved lazily so an unavailable profile is reported by the command boundary as
    /// structured JSON instead of throwing during composition.
    /// </summary>
    public string DefaultPath => string.IsNullOrWhiteSpace(_userProfileDirectory)
        ? throw new ConfigurationException(
            "userProfileUnavailable",
            "The user profile directory could not be resolved. Use --config to provide a path.")
        : Path.Combine(_userProfileDirectory, ".ado-read", "config.json");

    public async Task<AdoReadConfiguration> LoadAsync(
        string? overridePath,
        CancellationToken cancellationToken)
    {
        string path = ResolvePath(overridePath);
        if (!File.Exists(path))
        {
            throw new ConfigurationException(
                "configurationNotFound",
                $"Configuration was not found at '{path}'. Run 'ado-read config init' first.");
        }

        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ConfigurationFile? file = await JsonSerializer.DeserializeAsync<ConfigurationFile>(
                stream,
                SerializerOptions,
                cancellationToken);
            return ToDomain(file);
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException(
                "invalidConfiguration",
                "The configuration file is not valid JSON.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ConfigurationException(
                "configurationReadFailed",
                "The configuration file could not be read.",
                exception);
        }
    }

    public async Task SaveAsync(
        string? overridePath,
        AdoReadConfiguration configuration,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        AdoReadConfiguration validated = AdoReadConfiguration.Create(
            configuration.OrganizationUrl,
            checked((int)configuration.RequestTimeout.TotalSeconds),
            configuration.MaxParallelProjects,
            configuration.DefaultProject,
            configuration.DefaultTeam);
        string path = ResolvePath(overridePath);
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ConfigurationException(
                "invalidConfigurationPath",
                "The configuration path must include a directory.");
        }

        if (File.Exists(path) && !overwrite)
        {
            throw ConfigurationAlreadyExists(path);
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteTemporaryFileAsync(temporaryPath, ConfigurationFile.From(validated), cancellationToken);
            try
            {
                File.Move(temporaryPath, path, overwrite);
            }
            catch (IOException exception) when (!overwrite && File.Exists(path))
            {
                throw ConfigurationAlreadyExists(path, exception);
            }
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new ConfigurationException(
                "configurationWriteFailed",
                "The configuration file could not be written.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string ResolvePath(string? overridePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            return DefaultPath;
        }

        return Path.GetFullPath(overridePath);
    }

    private static AdoReadConfiguration ToDomain(ConfigurationFile? file)
    {
        if (file is null)
        {
            throw new ConfigurationException("invalidConfiguration", "The configuration file is empty.");
        }

        if (file.SchemaVersion != AdoReadConfiguration.CurrentSchemaVersion)
        {
            throw new ConfigurationException(
                "unsupportedConfigurationVersion",
                $"Configuration schema version '{file.SchemaVersion}' is not supported.");
        }

        if (!Uri.TryCreate(file.OrganizationUrl, UriKind.Absolute, out Uri? organizationUrl))
        {
            throw new ConfigurationException(
                "invalidOrganizationUrl",
                "The configured organization URL is invalid.");
        }

        return AdoReadConfiguration.Create(
            organizationUrl,
            file.RequestTimeoutSeconds,
            file.MaxParallelProjects,
            file.DefaultProject,
            file.DefaultTeam);
    }

    private static async Task WriteTemporaryFileAsync(
        string path,
        ConfigurationFile file,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, file, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static ConfigurationException ConfigurationAlreadyExists(
        string path,
        Exception? innerException = null) =>
        new(
            "configurationExists",
            $"Configuration already exists at '{path}'. Use --force to replace it.",
            innerException);

    /// <summary>
    /// The default project and team were added after the first release. They are optional, so a
    /// schema version 1 file written without them still loads.
    /// </summary>
    private sealed record ConfigurationFile(
        int SchemaVersion,
        string? OrganizationUrl,
        int RequestTimeoutSeconds,
        int MaxParallelProjects,
        string? DefaultProject = null,
        string? DefaultTeam = null)
    {
        public static ConfigurationFile From(AdoReadConfiguration configuration) =>
            new(
                configuration.SchemaVersion,
                configuration.OrganizationUrl.AbsoluteUri,
                checked((int)configuration.RequestTimeout.TotalSeconds),
                configuration.MaxParallelProjects,
                configuration.DefaultProject,
                configuration.DefaultTeam);
    }
}
