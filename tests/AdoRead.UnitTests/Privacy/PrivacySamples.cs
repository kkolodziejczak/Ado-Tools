using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.PullRequests;
using AdoRead.Privacy;
using AdoRead.Sprints;
using AdoRead.Text;
using AdoRead.WorkItems;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Privacy;

/// <summary>Fabricated documents with every field that a privacy category can withhold.</summary>
internal static class PrivacySamples
{
    public const string EngineerName = "engineer@example.invalid";

    public const string OwnerName = "owner@example.invalid";

    public static readonly Uri OrganizationUrl = new("https://dev.azure.com/fabricated/");

    public static string EngineerAlias => new PersonAliases(TestPrivacy.Key).Alias(EngineerName)!;

    public static string OwnerAlias => new PersonAliases(TestPrivacy.Key).Alias(OwnerName)!;

    public static PullRequestDocument PullRequest() => new(
        126,
        "Fabricated retry policy",
        "Adds a fabricated retry policy.",
        "active",
        "succeeded",
        false,
        null,
        null,
        OrganizationUrl,
        new PullRequestProjectDocument(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Fabricated.Alpha", null),
        new PullRequestRepositoryDocument(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), "Fabricated.Alpha.Repository", null, null),
        "refs/heads/feature/retry",
        "refs/heads/main",
        null,
        null,
        null,
        EngineerName,
        [new PullRequestReviewerDocument(OwnerName, 10, "approved", false, false, null)],
        [],
        [
            new PullRequestThreadDocument(
                7,
                "active",
                false,
                false,
                "/src/Retry.cs",
                4,
                4,
                null,
                null,
                [new PullRequestCommentDocument(1, 0, "comment", OwnerName, "Fabricated review comment.", null, false)])
        ],
        new PullRequestLinksDocument(null, null));

    /// <summary>The raw work item as the source returns it, before any privacy rule applies.</summary>
    public static SourceWorkItem WorkItemSource() => new(
        42,
        3,
        null,
        new Dictionary<string, object?>
        {
            ["System.TeamProject"] = "Fabricated.Alpha",
            ["System.WorkItemType"] = "User Story",
            ["System.Title"] = "Fabricated checkout story",
            ["System.State"] = "Active",
            ["System.Description"] = "<p>Fabricated description</p>",
            ["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                "<p>Fabricated criteria</p><img src=\"https://dev.azure.com/fabricated/Fabricated.Alpha/_apis/wit/attachments/6d8e1ce4-1172-4d07-a83f-35f8d7f5889a?fileName=screen.png\">",
            ["System.AssignedTo"] = Engineer(),
            ["System.CreatedBy"] = Owner(),
            ["System.ChangedBy"] = Engineer(),
            ["Custom.Owner"] = Owner(),
            ["Custom.Notes"] = "Fabricated note"
        },
        []);

    public static IReadOnlyList<WorkItemCommentDocument> WorkItemComments() =>
    [
        new WorkItemCommentDocument(
            501,
            1,
            OwnerName,
            null,
            new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero),
            null,
            false,
            "html",
            "<p>Fabricated comment</p>",
            "<p>Fabricated comment</p>",
            null,
            [new CommentMentionDocument("00000000-0000-0000-0000-0000000000b2", "Person", "00000000-0000-0000-0000-0000000000b2", new Uri("https://dev.azure.com/fabricated/_apis/identities/2"))],
            [],
            null)
    ];

    public static WorkItemDocument WorkItem(EffectivePrivacy privacy) => WorkItemPrivacy.Apply(
        new WorkItemMapper(new RichTextNormalizer()).Map(
            WorkItemPrivacy.AliasIdentities(WorkItemSource(), privacy),
            WorkItemComments(),
            OrganizationUrl),
        privacy);

    public static WikiPageDocument WikiPage() => new(
        51,
        "/Architecture/Authentication",
        "/Architecture/Authentication.md",
        "wikiApi",
        "markdown",
        "# Authentication\n\nUse the fabricated identity provider.",
        null,
        null);

    public static WikiSearchHitDocument WikiSearchHit() => new(
        "Authentication.md",
        "/Architecture/Authentication.md",
        new WikiSearchProjectDocument(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Fabricated.Alpha"),
        new WikiSearchWikiDocument(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), "Fabricated.wiki", null, "main"),
        [new WikiSearchMatchDocument("content", ["fabricated identity provider"])],
        new CommandHintDocument("wiki page", []));

    public static SprintDocument Sprint() => new(
        OrganizationUrl,
        "Fabricated.Alpha",
        "Fabricated.Alpha Team",
        new SprintIterationDocument(
            Guid.Parse("00000000-0000-0000-0000-000000000a11"),
            "Sprint 11",
            @"Fabricated.Alpha\Sprint 11",
            null,
            null,
            "current"),
        new Dictionary<string, int> { ["Active"] = 1 },
        [
            new SprintWorkItemDocument(
                200,
                "User Story",
                "Fabricated checkout story",
                "Active",
                @"Fabricated.Alpha\Sprint 11",
                "Example Engineer",
                null,
                EngineerName)
        ]);

    public static TeamDocument Team() => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000f6"),
        "Fabricated.Alpha Team",
        "Fabricated team description");

    private static IdentityDocument Engineer() => new(
        "Example Engineer",
        "00000000-0000-0000-0000-0000000000a1",
        EngineerName,
        "aad.descriptor-engineer",
        new Uri("https://dev.azure.com/fabricated/_apis/identities/1"),
        new Uri("https://dev.azure.com/fabricated/_api/_common/identityImage?id=1"));

    private static IdentityDocument Owner() => new(
        "Example Owner",
        "00000000-0000-0000-0000-0000000000b2",
        OwnerName,
        null,
        null,
        null);
}
