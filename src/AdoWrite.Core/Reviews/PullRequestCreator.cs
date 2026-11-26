using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.Reviews;

/// <summary>
/// The result of <c>pr create</c>. <see cref="Outcome"/> is <c>created</c>, <c>existing</c> (an active
/// pull request for the same branches already exists and is returned as it is, with a
/// <see cref="Notice"/>) or <c>preview</c>. Only a preview carries <see cref="Description"/>.
/// </summary>
public sealed record CreatedPullRequestDocument(
    string Outcome,
    int? PullRequestId,
    string? Title,
    string SourceRefName,
    string TargetRefName,
    bool IsDraft,
    string Project,
    string Repository,
    Uri? Url,
    IReadOnlyList<MentionDocument> Reviewers,
    IReadOnlyList<int> WorkItems,
    string ActingAs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notice = null);

public static class CreateOutcomes
{
    public const string Created = "created";
    public const string Existing = "existing";
    public const string Preview = "preview";
}

public static class CreateKinds
{
    public const string PullRequestCreate = "pullRequestCreate";
}

/// <summary>
/// What to create, already checked: <see cref="Repository"/> is an ID, or a name with its
/// <see cref="Project"/>; branches are full ref names, and a null <see cref="TargetRefName"/> means the
/// repository's default branch.
/// </summary>
internal sealed record PullRequestRequest(
    string? Project,
    string Repository,
    string SourceRefName,
    string? TargetRefName,
    string Title,
    string? Body,
    string? Agent,
    IReadOnlyList<string> Reviewers,
    IReadOnlyList<int> WorkItems,
    bool IsDraft,
    bool DryRun)
{
    /// <summary>Azure DevOps keeps at most this many characters of a pull request's description.</summary>
    public const int MaxDescriptionLength = 4000;

    /// <summary><c>feature/x</c> and <c>refs/heads/feature/x</c> name the same branch.</summary>
    public static string RefName(string branch)
    {
        string trimmed = branch.Trim();
        return trimmed.StartsWith("refs/", StringComparison.Ordinal) ? trimmed : "refs/heads/" + trimmed.TrimStart('/');
    }
}

/// <summary>
/// Opens a pull request. An active pull request from the same source into the same target is returned
/// instead, both when it is found first and when Azure DevOps refuses the create because another run
/// opened it meanwhile, so running the command again never opens a second one.
/// </summary>
internal sealed class PullRequestCreator(IPullRequestCreateClient client, IPeopleDirectory people, Uri organizationUrl)
{
    public async Task<CreatedPullRequestDocument> CreateAsync(PullRequestRequest request, CancellationToken cancellationToken)
    {
        GitRepository repository = await AzureDevOpsCall.RunAsync(
            () => client.GetRepositoryAsync(request.Project, request.Repository, cancellationToken),
            cancellationToken,
            WriteException.NotFound("repositoryNotFound", $"No repository '{request.Repository}' was found, or the token cannot read it."));
        string target = request.TargetRefName
            ?? repository.DefaultBranch
            ?? throw WriteException.Rejected("targetRequired", $"Repository {repository.Name} has no default branch. Name one with --target.");
        if (string.Equals(request.SourceRefName, target, StringComparison.Ordinal))
        {
            throw WriteException.Rejected("sameBranch", $"--source and --target are both {target}; a pull request needs two branches.");
        }

        Person author = await AzureDevOpsCall.RunAsync(() => people.GetSignedInUserAsync(cancellationToken), cancellationToken);
        if (await FindActiveAsync(repository, request.SourceRefName, target, cancellationToken) is { } open)
        {
            return Existing(open, repository, author);
        }

        IReadOnlyList<MentionDocument> reviewers = await CommentComposer.ResolvePeopleAsync(people, request.Reviewers, "reviewer", cancellationToken);
        string? description = Describe(request, author);
        if (request.DryRun)
        {
            return Document(CreateOutcomes.Preview, null, request.Title, request.SourceRefName, target, request.IsDraft, repository, reviewers, request.WorkItems, author) with
            {
                Description = description ?? string.Empty
            };
        }

        GitPullRequest created;
        try
        {
            created = await AzureDevOpsCall.RunAsync(
                () => client.CreatePullRequestAsync(repository, NewPullRequest(request, target, description, reviewers), cancellationToken),
                cancellationToken);
        }
        catch (WriteException exception) when (exception.InnerException is VssServiceResponseException { HttpStatusCode: HttpStatusCode.Conflict })
        {
            if (await FindActiveAsync(repository, request.SourceRefName, target, cancellationToken) is { } opened)
            {
                return Existing(opened, repository, author);
            }

            throw;
        }

        return Document(
            CreateOutcomes.Created,
            created.PullRequestId,
            created.Title,
            request.SourceRefName,
            target,
            created.IsDraft ?? request.IsDraft,
            repository,
            reviewers,
            request.WorkItems,
            author);
    }

    private async Task<GitPullRequest?> FindActiveAsync(GitRepository repository, string source, string target, CancellationToken cancellationToken)
    {
        IReadOnlyList<GitPullRequest> active = await AzureDevOpsCall.RunAsync(
            () => client.GetActivePullRequestsAsync(repository, source, target, cancellationToken),
            cancellationToken);
        return active.OrderBy(pullRequest => pullRequest.PullRequestId).FirstOrDefault();
    }

    /// <summary>The body, and the footer naming the agent and the person it creates for when an agent is named.</summary>
    private static string? Describe(PullRequestRequest request, Person author)
    {
        if (request.Agent is null)
        {
            return request.Body;
        }

        string footer = $"🤖 *Created by {CommentComposer.EscapeEmphasis(request.Agent)} on behalf of {CommentComposer.EscapeEmphasis(author.DisplayName)}*";
        string description = string.IsNullOrEmpty(request.Body) ? footer : $"{request.Body}\n\n---\n{footer}";
        return description.Length > PullRequestRequest.MaxDescriptionLength
            ? throw WriteException.Rejected(
                "descriptionTooLong",
                $"With the --agent footer the description has {description.Length} characters; Azure DevOps keeps {PullRequestRequest.MaxDescriptionLength}.")
            : description;
    }

    private static GitPullRequest NewPullRequest(
        PullRequestRequest request,
        string target,
        string? description,
        IReadOnlyList<MentionDocument> reviewers) =>
        new()
        {
            SourceRefName = request.SourceRefName,
            TargetRefName = target,
            Title = request.Title,
            Description = description,
            IsDraft = request.IsDraft,
            Reviewers = reviewers.Select(reviewer => new IdentityRefWithVote { Id = reviewer.Id.ToString() }).ToArray(),
            WorkItemRefs = request.WorkItems
                .Select(id => new ResourceRef { Id = id.ToString(CultureInfo.InvariantCulture) })
                .ToArray()
        };

    private CreatedPullRequestDocument Existing(GitPullRequest open, GitRepository repository, Person author) =>
        Document(
            CreateOutcomes.Existing,
            open.PullRequestId,
            open.Title,
            open.SourceRefName,
            open.TargetRefName,
            open.IsDraft ?? false,
            repository,
            [],
            [],
            author) with
        {
            Notice = "An active pull request from this source into this target already exists, so none was created. "
                + "Its title, description, reviewers and work items were left as they are."
        };

    private CreatedPullRequestDocument Document(
        string outcome,
        int? id,
        string? title,
        string source,
        string target,
        bool isDraft,
        GitRepository repository,
        IReadOnlyList<MentionDocument> reviewers,
        IReadOnlyList<int> workItems,
        Person author)
    {
        string project = repository.ProjectReference?.Name ?? string.Empty;
        Uri? url = id is null
            ? null
            : PullRequestLookup.BrowserUrl(
                organizationUrl,
                new PullRequestLocation(id.Value, repository.ProjectReference?.Id ?? Guid.Empty, project, repository.Id, repository.Name));
        return new CreatedPullRequestDocument(
            outcome,
            id,
            title,
            source,
            target,
            isDraft,
            project,
            repository.Name,
            url,
            reviewers,
            workItems,
            author.DisplayName);
    }
}
