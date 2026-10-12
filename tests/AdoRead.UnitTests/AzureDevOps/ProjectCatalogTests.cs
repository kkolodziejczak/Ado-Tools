using AdoRead.AzureDevOps;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class ProjectCatalogTests
{
    [Fact]
    public async Task GetAllAsync_FollowsContinuationTokensAndSortsProjects()
    {
        var client = new FakeProjectClient(
            new ProjectPage([new AdoProject(Guid.NewGuid(), "Zulu", null)], "next"),
            new ProjectPage([new AdoProject(Guid.NewGuid(), "Alpha", null)], null));
        var catalog = new ProjectCatalog(client);

        IReadOnlyList<AdoProject> projects = await catalog.GetAllAsync(CancellationToken.None);

        Assert.Equal([null, "next"], client.Tokens);
        Assert.Equal(["Alpha", "Zulu"], projects.Select(project => project.Name));
    }

    [Fact]
    public async Task GetAllAsync_RejectsRepeatedContinuationToken()
    {
        var client = new FakeProjectClient(
            new ProjectPage([], "repeat"),
            new ProjectPage([], "repeat"));
        var catalog = new ProjectCatalog(client);

        ProjectCatalogException exception = await Assert.ThrowsAsync<ProjectCatalogException>(() =>
            catalog.GetAllAsync(CancellationToken.None));

        Assert.Equal("projectPaginationLoop", exception.Code);
    }

    private sealed class FakeProjectClient(params ProjectPage[] pages) : IProjectClient
    {
        private int _index;

        public List<string?> Tokens { get; } = [];

        public Task<ProjectPage> GetProjectsAsync(
            string? continuationToken,
            CancellationToken cancellationToken)
        {
            Tokens.Add(continuationToken);
            return Task.FromResult(pages[_index++]);
        }
    }
}
