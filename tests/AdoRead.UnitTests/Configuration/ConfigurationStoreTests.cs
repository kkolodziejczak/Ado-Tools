using AdoRead.Configuration;

namespace AdoRead.UnitTests.Configuration;

public sealed class ConfigurationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ado-read-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultPath_UsesUserProfileDirectory()
    {
        var store = new ConfigurationStore(_directory);

        Assert.Equal(Path.Combine(_directory, ".ado-read", "config.json"), store.DefaultPath);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsCanonicalConfigurationWithoutSecrets()
    {
        string path = Path.Combine(_directory, "nested", "settings.json");
        var store = new ConfigurationStore(_directory);
        var configuration = AdoReadConfiguration.Create(new Uri("https://dev.azure.com/example"), 45, 7);

        await store.SaveAsync(path, configuration, overwrite: false, CancellationToken.None);
        AdoReadConfiguration loaded = await store.LoadAsync(path, CancellationToken.None);

        Assert.Equal(new Uri("https://dev.azure.com/example/"), loaded.OrganizationUrl);
        Assert.Equal(TimeSpan.FromSeconds(45), loaded.RequestTimeout);
        Assert.Equal(7, loaded.MaxParallelProjects);
        string persisted = await File.ReadAllTextAsync(path, CancellationToken.None);
        Assert.DoesNotContain("token", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Save_RefusesToOverwriteUnlessExplicitlyRequested()
    {
        string path = Path.Combine(_directory, "config.json");
        var store = new ConfigurationStore(_directory);
        var original = AdoReadConfiguration.Create(new Uri("https://dev.azure.com/original"), 30, 4);
        var replacement = AdoReadConfiguration.Create(new Uri("https://dev.azure.com/replacement"), 60, 5);
        await store.SaveAsync(path, original, overwrite: false, CancellationToken.None);

        ConfigurationException exception = await Assert.ThrowsAsync<ConfigurationException>(
            () => store.SaveAsync(path, replacement, overwrite: false, CancellationToken.None));

        Assert.Equal("configurationExists", exception.Code);
        Assert.Equal(original, await store.LoadAsync(path, CancellationToken.None));

        await store.SaveAsync(path, replacement, overwrite: true, CancellationToken.None);
        Assert.Equal(replacement, await store.LoadAsync(path, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentSaveWithoutOverwrite_LeavesOneCompleteConfigurationAndNoTemporaryFiles()
    {
        string path = Path.Combine(_directory, "race", "config.json");
        var store = new ConfigurationStore(_directory);
        var first = AdoReadConfiguration.Create(new Uri("https://dev.azure.com/first"), 30, 4);
        var second = AdoReadConfiguration.Create(new Uri("https://dev.azure.com/second"), 60, 8);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<Exception?> firstSave = SaveAfterGateAsync(first);
        Task<Exception?> secondSave = SaveAfterGateAsync(second);
        gate.SetResult();
        Exception?[] outcomes = await Task.WhenAll(firstSave, secondSave);

        Assert.Single(outcomes, outcome => outcome is null);
        ConfigurationException failure = Assert.IsType<ConfigurationException>(Assert.Single(outcomes, outcome => outcome is not null));
        Assert.Equal("configurationExists", failure.Code);
        AdoReadConfiguration persisted = await store.LoadAsync(path, CancellationToken.None);
        Assert.True(persisted == first || persisted == second);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
        return;

        async Task<Exception?> SaveAfterGateAsync(AdoReadConfiguration configuration)
        {
            await gate.Task;
            try
            {
                await store.SaveAsync(path, configuration, overwrite: false, CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    [Fact]
    public void DefaultPath_FailsOnlyWhenResolvedSoCommandBoundaryCanReportIt()
    {
        var store = new ConfigurationStore(string.Empty);

        ConfigurationException exception = Assert.Throws<ConfigurationException>(() => _ = store.DefaultPath);

        Assert.Equal("userProfileUnavailable", exception.Code);
    }

    [Theory]
    [InlineData("http://dev.azure.com/example")]
    [InlineData("https://example.com/example")]
    [InlineData("https://dev.azure.com/example/project")]
    [InlineData("https://dev.azure.com/")]
    public void Create_RejectsInvalidOrganizationUrl(string url)
    {
        ConfigurationException exception = Assert.Throws<ConfigurationException>(
            () => AdoReadConfiguration.Create(new Uri(url), 30, 4));

        Assert.Equal("invalidOrganizationUrl", exception.Code);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(301, 4)]
    [InlineData(30, 0)]
    [InlineData(30, 17)]
    public void Create_RejectsOutOfRangeSettings(int timeoutSeconds, int maxParallelProjects)
    {
        Assert.Throws<ConfigurationException>(
            () => AdoReadConfiguration.Create(
                new Uri("https://dev.azure.com/example"),
                timeoutSeconds,
                maxParallelProjects));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
