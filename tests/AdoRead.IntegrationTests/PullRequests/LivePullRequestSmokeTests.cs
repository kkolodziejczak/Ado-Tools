using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.PullRequests;

namespace AdoRead.IntegrationTests.PullRequests;

public sealed class LivePullRequestSmokeTests
{
    [Fact]
    public async Task ReadConfiguredPullRequest_WhenLiveTestsAreExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ADO_READ_LIVE_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        string idValue = Environment.GetEnvironmentVariable("ADO_READ_PR_ID")
            ?? throw new InvalidOperationException("ADO_READ_PR_ID is required for the live smoke test.");
        Assert.True(int.TryParse(idValue, out int id) && id > 0, "ADO_READ_PR_ID must be a positive integer.");
        AdoReadConfiguration configuration = await new ConfigurationStore().LoadAsync(null, CancellationToken.None);
        string pat = LiveTestCredentials.TokenFor(configuration);
        await using IAdoSession session = new AdoSessionFactory()
            .Create(configuration.OrganizationUrl, pat);
        var reader = new PullRequestReader(
            session.Projects,
            session.PullRequests,
            configuration.OrganizationUrl,
            configuration.MaxParallelProjects,
            TimeProvider.System);
        using var timeout = new CancellationTokenSource(configuration.RequestTimeout);

        var result = await reader.ReadAsync(id, timeout.Token);

        Assert.Contains(result.Results, pullRequest => pullRequest.Id == id);
    }
}
