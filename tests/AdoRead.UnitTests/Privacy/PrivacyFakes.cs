using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.WorkItems;

namespace AdoRead.UnitTests.Privacy;

/// <summary>A configuration for the fabricated organization with no default project or team.</summary>
internal sealed class FabricatedStore : IConfigurationStore
{
    public string DefaultPath => "fabricated://config";

    public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
        Task.FromResult(AdoReadConfiguration.Create(PrivacySamples.OrganizationUrl));

    public Task SaveAsync(string? overridePath, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class StaticPat : IPatProvider
{
    public string GetRequiredPat(Uri organizationUrl) => "fabricated-test-pat";
}

/// <summary>Hands out one session and records whether a read ever opened one.</summary>
internal sealed class FixtureSessionFactory(IAdoSession session) : IAdoSessionFactory
{
    public bool Created { get; private set; }

    public IAdoSession Create(Uri organizationUrl, string pat)
    {
        Created = true;
        return session;
    }
}

internal sealed class FixtureSession(IWorkItemSource workItems) : IAdoSession
{
    public IWorkItemSource WorkItems { get; } = workItems;

    public ITeamClient Teams { get; } = new FixtureTeams();

    public IProjectCatalog Projects => throw new NotSupportedException();

    public IPullRequestSource PullRequests => throw new NotSupportedException();

    public ISprintSource Sprints => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FixtureTeams : ITeamClient
{
    public Task<IReadOnlyList<AdoTeam>> GetTeamsAsync(string project, bool mine, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdoTeam>>(
        [
            new AdoTeam(
                Guid.Parse("00000000-0000-0000-0000-0000000000f6"),
                "Fabricated.Alpha Team",
                "Fabricated team description")
        ]);
}

/// <summary>Serves the fabricated work item and counts every attachment fetch, so a test can prove none happened.</summary>
internal sealed class FixtureWorkItemSource : IWorkItemSource
{
    public int AttachmentCallCount { get; private set; }

    public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult<SourceWorkItem?>(PrivacySamples.WorkItemSource());

    public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(string project, int id, CancellationToken cancellationToken) =>
        Task.FromResult(PrivacySamples.WorkItemComments());

    public Task<Stream> GetAttachmentContentAsync(string project, Guid attachmentId, string fileName, CancellationToken cancellationToken)
    {
        AttachmentCallCount++;
        throw new NotSupportedException("The fabricated attachment must not be fetched in this test.");
    }
}
