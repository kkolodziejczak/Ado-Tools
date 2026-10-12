namespace AdoRead.Configuration;

public interface IConfigurationStore
{
    string DefaultPath { get; }

    Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken);

    Task SaveAsync(
        string? overridePath,
        AdoReadConfiguration configuration,
        bool overwrite,
        CancellationToken cancellationToken);
}
