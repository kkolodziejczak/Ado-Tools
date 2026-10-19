using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.PullRequests;
using AdoRead.Sprints;
using AdoRead.WorkItems;

var application = new ToolComposition(
    new FixtureConfigurationStore(),
    new JsonOutput(Console.Out),
    new FixtureTimeProvider(),
    new FixturePatProvider(),
    new FixtureAdoSessionFactory(args.Contains("--download-images", StringComparer.OrdinalIgnoreCase)),
    new FixtureCredentialVault()).CreateUmbrella(
    "ado-read",
    [PullRequestModule.Definition, WorkItemModule.Definition, WikiModule.Definition, SprintModule.Definition, PipelineModule.Definition]);

return await application.InvokeAsync(args, CancellationToken.None);

internal sealed class FixtureConfigurationStore : IConfigurationStore
{
    public string DefaultPath => "fixture://config";

    public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
        Task.FromResult(AdoReadConfiguration.Create(
            new Uri("https://dev.azure.com/fabricated/"),
            AdoReadConfiguration.DefaultRequestTimeoutSeconds,
            AdoReadConfiguration.DefaultMaxParallelProjects,
            defaultProject: "FabricatedApp",
            defaultTeam: "Fabricated.Alpha Team"));

    public Task SaveAsync(
        string? overridePath,
        AdoReadConfiguration configuration,
        bool overwrite,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FixturePatProvider : IPatProvider
{
    public string GetRequiredPat(Uri organizationUrl) => "fabricated-test-pat";
}

internal sealed class FixtureAdoSessionFactory(bool includeImages) : IAdoSessionFactory
{
    public IAdoSession Create(Uri organizationUrl, string pat) => new FixtureAdoSession(includeImages);
}

internal sealed class FixtureAdoSession(bool includeImages) : IAdoSession
{
    public IProjectCatalog Projects { get; } = new FixtureProjectCatalog();

    public IPullRequestSource PullRequests { get; } = new FixturePullRequestSource();

    public IWorkItemSource WorkItems { get; } = new FixtureWorkItemSource(includeImages);

    public ISprintSource Sprints { get; } = new FixtureSprintSource();

    public ITeamClient Teams { get; } = new FixtureTeamClient();

    public IWikiSource Wikis { get; } = new FixtureWikiSource();

    public IPipelineSource Pipelines { get; } = new FixturePipelineSource();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FixtureTeamClient : ITeamClient
{
    public Task<IReadOnlyList<AdoTeam>> GetTeamsAsync(
        string project,
        bool mine,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdoTeam>>(
        [
            new AdoTeam(
                Guid.Parse("00000000-0000-0000-0000-0000000000f6"),
                "Fabricated.Alpha Team",
                "Fabricated team")
        ]);
}

internal sealed class FixtureSprintSource : ISprintSource
{
    private static readonly Guid CurrentIterationId = Guid.Parse("00000000-0000-0000-0000-000000000a11");
    private static readonly Guid PastIterationId = Guid.Parse("00000000-0000-0000-0000-000000000a10");

    public Task<IReadOnlyList<SprintIterationDocument>> GetIterationsAsync(
        string project,
        string team,
        string? timeframe,
        CancellationToken cancellationToken)
    {
        SprintIterationDocument past = new(
            PastIterationId,
            "Sprint 10",
            @"Fabricated.Alpha\Sprint 10",
            new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero),
            "past");
        SprintIterationDocument current = new(
            CurrentIterationId,
            "Sprint 11",
            @"Fabricated.Alpha\Sprint 11",
            new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero),
            "current");
        IReadOnlyList<SprintIterationDocument> iterations = timeframe switch
        {
            "current" => [current],
            "past" => [past],
            "future" => [],
            _ => [past, current]
        };
        return Task.FromResult(iterations);
    }

    public Task<IReadOnlyList<SprintWorkItemDocument>> GetWorkItemsAsync(
        string project,
        string team,
        Guid iterationId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SprintWorkItemDocument>>(
        [
            new SprintWorkItemDocument(
                201,
                "Bug",
                "Declined payments are retried twice",
                "Closed",
                @"Fabricated.Alpha\Sprint 11",
                "Example Engineer",
                new Uri("https://dev.azure.com/fabricated/FabricatedApp/_workitems/edit/201")),
            new SprintWorkItemDocument(
                200,
                "User Story",
                "Fabricated checkout story",
                "Active",
                @"Fabricated.Alpha\Sprint 11",
                "Example Engineer",
                new Uri("https://dev.azure.com/fabricated/FabricatedApp/_workitems/edit/200")),
            new SprintWorkItemDocument(
                202,
                "User Story",
                "Retry declined payments",
                "Closed",
                @"Fabricated.Alpha\Sprint 11",
                null,
                new Uri("https://dev.azure.com/fabricated/FabricatedApp/_workitems/edit/202"))
        ]);
}

internal sealed class FixtureWorkItemSource(bool includeImages) : IWorkItemSource
{
    private static readonly Guid AttachmentId = Guid.Parse("6d8e1ce4-1172-4d07-a83f-35f8d7f5889a");
    private static readonly Guid SharedAttachmentId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid RelationOnlyAttachmentId = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid InvalidAttachmentId = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] WebpBytes =
    [
        0x52, 0x49, 0x46, 0x46, 0x34, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50,
        0x56, 0x50, 0x38, 0x58, 0x0A, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x56, 0x50, 0x38, 0x20, 0x16, 0x00, 0x00, 0x00,
        0x30, 0x01, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00,
        0x0E, 0xC0, 0xFE, 0x25, 0xA4, 0x00, 0x03, 0x70, 0x00, 0x00, 0x00, 0x00
    ];
    private static readonly byte[] InvalidBytes = "<html>not-an-image</html>"u8.ToArray();
    private int _requestedWorkItemId;
    private readonly HashSet<Guid> _downloadedAttachmentIds = [];

    public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        _requestedWorkItemId = id;
        if (id == 404)
        {
            return Task.FromResult<SourceWorkItem?>(null);
        }

        var fields = new Dictionary<string, object?>
        {
            ["System.WorkItemType"] = "User Story",
            ["System.Title"] = "Fabricated checkout story",
            ["System.State"] = "Active",
            ["System.TeamProject"] = "FabricatedApp",
            ["System.Description"] = "<p>As a shopper, I can <strong>check out</strong>.</p>",
            ["System.AssignedTo"] = new IdentityDocument(
                "Example Engineer",
                "00000000-0000-0000-0000-000000000001",
                "engineer@example.invalid",
                "aad.fabricated",
                null,
                null),
            ["System.Tags"] = "agent-ready; sample",
            ["Custom.Risk"] = "low",
            ["Custom.Notes"] = "<div>Ship behind a <b>flag</b>.</div>"
        };
        if ((includeImages && id != 126) || id == 125)
        {
            if (id == 124)
            {
                fields["System.Description"] =
                    $"<DIV><IMG SRC=\"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{SharedAttachmentId:D}?fileName=description.png\"><IMG SRC=\"https://evil.invalid/image.png\"></DIV>";
                fields["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                    $"<p>Shared evidence:</p><IMG SRC=\"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{SharedAttachmentId:D}?fileName=acceptance.png\">";
            }
            else
            {
                fields["Microsoft.VSTS.Common.AcceptanceCriteria"] =
                    $"<p>The checkout screenshot is attached.</p><img src=\"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{AttachmentId:D}?fileName=checkout-layout.png\" />";
            }
        }

        SourceWorkItem item = new(
            id,
            6,
            new Uri($"https://dev.azure.com/fabricated/_apis/wit/workItems/{id}"),
            fields,
            BuildRelations(id))
        {
            RichTextFields = new HashSet<string>(StringComparer.Ordinal) { "System.Description", "Custom.Notes" }
        };
        return Task.FromResult<SourceWorkItem?>(item);
    }

    private IReadOnlyList<SourceWorkItemRelation> BuildRelations(int id)
    {
        var relations = new List<SourceWorkItemRelation>
        {
            new(
                "System.LinkTypes.Hierarchy-Reverse",
                new Uri("https://dev.azure.com/fabricated/_apis/wit/workItems/100"),
                new Dictionary<string, object?> { ["name"] = "Parent" })
        };
        if ((includeImages && id != 126) || id == 125)
        {
            if (id == 124)
            {
                relations.AddRange(
                [
                    new SourceWorkItemRelation(
                        "AttachedFile",
                        new Uri($"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{SharedAttachmentId:D}?fileName=relation.png"),
                        new Dictionary<string, object?> { ["name"] = "relation.png" }),
                    new SourceWorkItemRelation(
                        "AttachedFile",
                        new Uri($"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{RelationOnlyAttachmentId:D}?fileName=relation-only.webp"),
                        new Dictionary<string, object?> { ["name"] = "relation-only.webp" }),
                    new SourceWorkItemRelation(
                        "AttachedFile",
                        new Uri($"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{InvalidAttachmentId:D}?fileName=bad.png"),
                        new Dictionary<string, object?> { ["name"] = "bad.png" }),
                    new SourceWorkItemRelation(
                        "AttachedFile",
                        new Uri($"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{Guid.Empty:D}?fileName=requirements.pdf"),
                        new Dictionary<string, object?> { ["name"] = "requirements.pdf" })
                ]);
            }
            else
            {
                relations.Add(new SourceWorkItemRelation(
                    "AttachedFile",
                    new Uri($"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{AttachmentId:D}?fileName=checkout-layout.png"),
                    new Dictionary<string, object?> { ["name"] = "checkout-layout.png" }));
            }
        }

        return relations;
    }

    public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
        string project,
        int id,
        CancellationToken cancellationToken)
    {
        if (includeImages && id == 124)
        {
            return Task.FromResult<IReadOnlyList<WorkItemCommentDocument>>(
            [
                new WorkItemCommentDocument(
                    10,
                    1,
                    "reviewer@example.invalid",
                    ModifiedByUniqueName: null,
                    CreatedAtUtc: null,
                    ModifiedAtUtc: null,
                    IsDeleted: false,
                    SourceFormat: "html",
                    RawText: null,
                    RenderedHtml: $"<p><img src=\"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{SharedAttachmentId:D}?fileName=comment.png\"></p>",
                    PlainText: null,
                    Mentions: [],
                    Reactions: [],
                    Url: null),
                new WorkItemCommentDocument(
                    11,
                    1,
                    "reviewer@example.invalid",
                    ModifiedByUniqueName: null,
                    CreatedAtUtc: null,
                    ModifiedAtUtc: null,
                    IsDeleted: false,
                    SourceFormat: "html",
                    RawText: $"<img src=\"https://dev.azure.com/fabricated/FabricatedApp/_apis/wit/attachments/{SharedAttachmentId:D}?fileName=raw-comment.png\">",
                    RenderedHtml: null,
                    PlainText: null,
                    Mentions: [],
                    Reactions: [],
                    Url: null)
            ]);
        }

        return Task.FromResult<IReadOnlyList<WorkItemCommentDocument>>(
        [
            new WorkItemCommentDocument(
                9,
                2,
                "reviewer@example.invalid",
                ModifiedByUniqueName: null,
                new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero),
                ModifiedAtUtc: null,
                IsDeleted: false,
                SourceFormat: "markdown",
                RawText: "Please cover the declined-payment case.",
                RenderedHtml: "<p>Please cover the declined-payment case.</p>",
                PlainText: null,
                Mentions: [],
                Reactions: [new CommentReactionDocument("like", 1, false)],
                new Uri($"https://dev.azure.com/fabricated/_apis/wit/workItems/{id}/comments/9"))
        ]);
    }

    public Task<Stream> GetAttachmentContentAsync(
        string project,
        Guid attachmentId,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(project, "FabricatedApp", StringComparison.Ordinal)
            || !cancellationToken.CanBeCanceled)
        {
            return Task.FromException<Stream>(new WorkItemSourceException(
                "fixtureDownloadContract",
                "The fixture received an unexpected image download request.",
                transient: false));
        }

        if (_requestedWorkItemId == 124)
        {
            if (attachmentId == SharedAttachmentId)
            {
                if (!_downloadedAttachmentIds.Add(attachmentId))
                {
                    return Task.FromException<Stream>(new WorkItemSourceException(
                        "duplicateAttachmentRequest",
                        "The fixture received a duplicate image download request.",
                        transient: false));
                }

                return Task.FromResult<Stream>(new MemoryStream(PngBytes, writable: false));
            }

            if (attachmentId == RelationOnlyAttachmentId)
            {
                return Task.FromResult<Stream>(new MemoryStream(WebpBytes, writable: false));
            }

            if (attachmentId == InvalidAttachmentId)
            {
                return Task.FromResult<Stream>(new MemoryStream(InvalidBytes, writable: false));
            }
        }

        if (_requestedWorkItemId == 123
            && attachmentId == AttachmentId
            && string.Equals(fileName, "checkout-layout.png", StringComparison.Ordinal))
        {
            return Task.FromResult<Stream>(new MemoryStream(PngBytes, writable: false));
        }

        return Task.FromException<Stream>(new WorkItemSourceException(
            "fixtureDownloadContract",
            "The fixture received an unexpected image download request.",
            transient: false));
    }
}

internal sealed class FixtureTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() =>
        new(2026, 7, 10, 8, 30, 0, TimeSpan.Zero);
}

internal sealed class FixtureProjectCatalog : IProjectCatalog
{
    public Task<IReadOnlyList<AdoProject>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdoProject>>(
        [
            new AdoProject(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Fabricated.Alpha", null),
            new AdoProject(Guid.Parse("00000000-0000-0000-0000-0000000000b2"), "Fabricated.Beta", null),
            new AdoProject(Guid.Parse("00000000-0000-0000-0000-0000000000c3"), "Fabricated.Restricted", null)
        ]);
}

internal sealed class FixturePullRequestSource : IPullRequestSource
{
    private static readonly Uri OrganizationUrl = new("https://dev.azure.com/fabricated/");

    /// <summary>
    /// Returns null so the process tests keep covering the project-scan fallback, including the
    /// multi-project and partial-result cases the organization-scoped lookup cannot produce.
    /// </summary>
    public Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult<PullRequestDocument?>(null);

    public Task<PullRequestDocument?> TryGetAsync(
        AdoProject project,
        int id,
        CancellationToken cancellationToken)
    {
        if (id == 125 || (id == 124 && project.Name == "Fabricated.Restricted"))
        {
            return Task.FromException<PullRequestDocument?>(new PullRequestSourceException(
                "forbidden",
                "The PAT cannot read this project.",
                transient: false));
        }

        bool isMatch = id switch
        {
            123 => project.Name is "Fabricated.Alpha" or "Fabricated.Beta",
            124 => project.Name == "Fabricated.Alpha",
            404 => false,
            _ => project.Name == "Fabricated.Alpha"
        };
        return Task.FromResult(isMatch ? CreatePullRequest(project, id) : null);
    }

    public Task<PullRequestDocument> GetAllDetailsAsync(
        PullRequestDocument pullRequest,
        CancellationToken cancellationToken)
        => Task.FromResult(pullRequest with
        {
            WorkItems =
            [
                new PullRequestWorkItemDocument(
                    321,
                    new Uri("https://dev.azure.com/fabricated/_apis/wit/workItems/321"))
            ],
            Threads =
            [
                new PullRequestThreadDocument(
                    7,
                    "active",
                    false,
                    true,
                    "/src/RetryPolicy.cs",
                    12,
                    12,
                    "fabricated-source-1",
                    "fabricated-source-2",
                    [
                        new PullRequestCommentDocument(
                            1,
                            0,
                            "human",
                            "reviewer@example.invalid",
                            "Please cover the final retry attempt.",
                            new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero),
                            false),
                        new PullRequestCommentDocument(
                            2,
                            1,
                            "system",
                            "reviewer@example.invalid",
                            "Thread state changed.",
                            new DateTimeOffset(2026, 7, 9, 10, 10, 0, TimeSpan.Zero),
                            false)
                    ])
            ]
        });

    private static PullRequestDocument CreatePullRequest(AdoProject project, int id)
    {
        bool isBeta = project.Name.EndsWith("Beta", StringComparison.Ordinal);
        string repositoryName = isBeta ? "Fabricated.Beta.Repository" : "Fabricated.Alpha.Repository";
        Guid repositoryId = isBeta
            ? Guid.Parse("00000000-0000-0000-0000-0000000000d4")
            : Guid.Parse("00000000-0000-0000-0000-0000000000e5");
        return new PullRequestDocument(
            id,
            "Fabricated retry policy",
            "Adds a fabricated retry policy for process-level tests.",
            "active",
            "succeeded",
            IsDraft: false,
            new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero),
            ClosedAtUtc: null,
            OrganizationUrl,
            new PullRequestProjectDocument(project.Id, project.Name, project.Url),
            new PullRequestRepositoryDocument(repositoryId, repositoryName, null, null),
            "refs/heads/retry-policy",
            "refs/heads/main",
            "fabricated-source-1",
            "fabricated-target-1",
            "fabricated-merge-1",
            "creator@example.invalid",
            [new PullRequestReviewerDocument("creator@example.invalid", 10, "approved", true, false, false)],
            WorkItems: [],
            Threads: [],
            new PullRequestLinksDocument(
                new Uri($"https://dev.azure.com/fabricated/{project.Name}/_apis/git/pullRequests/{id}"),
                new Uri($"https://dev.azure.com/fabricated/{project.Name}/_git/{repositoryName}/pullrequest/{id}")));
    }
}

/// <summary>
/// Holds the fabricated organization's privacy policy as a full policy, so the fixture commands return
/// their content. Other vault accounts are empty, as on a machine where no token or policy was stored.
/// </summary>
internal sealed class FixtureCredentialVault : ICredentialVault
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal)
    {
        [CredentialAccount.ForPrivacy(new Uri("https://dev.azure.com/fabricated/"))] =
            new StoredPrivacyPolicy(new PrivacyPolicy(PrivacyLevel.Full), new byte[32]).ToJson()
    };

    public string Kind => "fixture";

    public string? Read(string account) => _secrets.GetValueOrDefault(account);

    public void Write(string account, string secret) => _secrets[account] = secret;

    public bool Remove(string account) => _secrets.Remove(account);
}
