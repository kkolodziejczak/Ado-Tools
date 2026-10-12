using AdoRead.AzureDevOps;
using AdoRead.PullRequests;

namespace AdoRead.UnitTests;

/// <summary>
/// Builds the shallow <see cref="PullRequestDocument"/> that IPullRequestSource.TryGetAsync
/// returns: identity and review metadata only, with every detail collection still empty.
/// </summary>
internal static class TestPullRequests
{
    public static readonly Uri OrganizationUrl = new("https://dev.azure.com/example/");

    public static PullRequestDocument Create(
        AdoProject project,
        int id = 42,
        string repositoryName = "Repo") =>
        new(
            id,
            "PR",
            Description: null,
            "active",
            "succeeded",
            IsDraft: false,
            CreatedAtUtc: null,
            ClosedAtUtc: null,
            OrganizationUrl,
            new PullRequestProjectDocument(project.Id, project.Name, project.Url),
            new PullRequestRepositoryDocument(Guid.NewGuid(), repositoryName, null, null),
            "refs/heads/feature",
            "refs/heads/main",
            SourceCommitId: null,
            TargetCommitId: null,
            MergeCommitId: null,
            CreatorUniqueName: null,
            Reviewers: [],
            WorkItems: [],
            Threads: [],
            new PullRequestLinksDocument(null, null));
}
