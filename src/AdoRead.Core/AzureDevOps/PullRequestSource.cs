using System.Globalization;
using AdoRead.PullRequests;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public sealed class PullRequestSource(IPullRequestGitClient client, Uri organizationUrl) : IPullRequestSource
{
    public async Task<PullRequestDocument?> TryGetByIdAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            GitPullRequest pullRequest = await client.GetByIdAsync(id, cancellationToken);
            AdoProject? project = TryReadProject(pullRequest);
            return project is null ? null : MapPullRequest(project, pullRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            // Deliberately swallowed. This lookup is an optimisation; the per-project scan is
            // the authoritative path and reports the real error if one exists.
            return null;
        }
    }

    /// <summary>
    /// The organization-scoped response carries its own project. A pull request without one is
    /// unusable here, so the caller falls back to the scan rather than guessing.
    /// </summary>
    private static AdoProject? TryReadProject(GitPullRequest pullRequest)
    {
        TeamProjectReference? project = pullRequest.Repository?.ProjectReference;
        return project is null || string.IsNullOrEmpty(project.Name)
            ? null
            : new AdoProject(project.Id, project.Name, ToUri(project.Url));
    }

    public async Task<PullRequestDocument?> TryGetAsync(
        AdoProject project,
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            GitPullRequest pullRequest = await client.GetPullRequestAsync(project.Id, id, cancellationToken);
            return MapPullRequest(project, pullRequest);
        }
        catch (VssServiceResponseException exception)
            when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PullRequestFailure(exception);
        }
    }

    public async Task<PullRequestDocument> GetAllDetailsAsync(
        PullRequestDocument pullRequest,
        CancellationToken cancellationToken)
    {
        Guid projectId = pullRequest.Project.Id;
        Guid repositoryId = pullRequest.Repository.Id;
        int id = pullRequest.Id;
        try
        {
            IReadOnlyList<ResourceRef> workItems = await client.GetWorkItemsAsync(
                projectId,
                repositoryId,
                id,
                cancellationToken);
            IReadOnlyList<GitPullRequestCommentThread> threads = await client.GetThreadsAsync(
                projectId,
                repositoryId,
                id,
                cancellationToken);
            IReadOnlyList<GitPullRequestIteration> iterations = await client.GetIterationsAsync(
                projectId,
                repositoryId,
                id,
                cancellationToken);
            IReadOnlyDictionary<int, string?> iterationSourceCommits = MapIterationSourceCommits(iterations);
            int? latestIterationId = iterationSourceCommits.Count == 0
                ? null
                : iterationSourceCommits.Keys.Max();
            return pullRequest with
            {
                WorkItems = workItems.Select(MapWorkItem)
                    .OrderBy(item => item.Id)
                    .ThenBy(item => item.Url?.AbsoluteUri, StringComparer.Ordinal)
                    .ToArray(),
                Threads = threads.Select(thread => MapThread(thread, iterationSourceCommits, latestIterationId))
                    .OrderBy(thread => thread.Id)
                    .ToArray()
            };
        }
        catch (PullRequestSourceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PullRequestFailure(exception);
        }
    }

    private PullRequestDocument MapPullRequest(AdoProject project, GitPullRequest source)
    {
        GitRepository repository = source.Repository
            ?? throw new PullRequestSourceException(
                "missingRepository",
                "Azure DevOps returned a pull request without a repository.",
                transient: false);
        return new PullRequestDocument(
            source.PullRequestId,
            source.Title,
            source.Description,
            StableEnum(source.Status),
            StableEnum(source.MergeStatus),
            source.IsDraft ?? false,
            ToUtc(source.CreationDate),
            ToUtc(source.ClosedDate),
            organizationUrl,
            new PullRequestProjectDocument(project.Id, project.Name, project.Url),
            new PullRequestRepositoryDocument(
                repository.Id,
                repository.Name,
                ToUri(repository.Url),
                ToUri(repository.RemoteUrl)),
            source.SourceRefName,
            source.TargetRefName,
            source.LastMergeSourceCommit?.CommitId,
            source.LastMergeTargetCommit?.CommitId,
            source.LastMergeCommit?.CommitId,
            source.CreatedBy?.UniqueName,
            (source.Reviewers ?? []).Select(MapReviewer)
                .OrderBy(reviewer => reviewer.UniqueName, StringComparer.Ordinal)
                .ToArray(),
            WorkItems: [],
            Threads: [],
            new PullRequestLinksDocument(ToUri(source.Url), ToUri(source.RemoteUrl)));
    }

    private static PullRequestReviewerDocument MapReviewer(IdentityRefWithVote source) =>
        new(
            source.UniqueName,
            source.Vote,
            ReviewStatus(source.Vote),
            source.IsRequired,
            source.IsContainer,
            source.HasDeclined);

    private static string ReviewStatus(int vote) => vote switch
    {
        10 => "approved",
        5 => "approvedWithSuggestions",
        0 => "noVote",
        -5 => "waitingForAuthor",
        -10 => "rejected",
        _ => "unknown"
    };

    private static PullRequestWorkItemDocument MapWorkItem(ResourceRef source) =>
        new(
            int.TryParse(source.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null,
            ToUri(source.Url));

    private static IReadOnlyDictionary<int, string?> MapIterationSourceCommits(
        IReadOnlyList<GitPullRequestIteration> iterations) =>
        iterations
            .Where(iteration => iteration.Id is > 0)
            .OrderBy(iteration => iteration.Id)
            .GroupBy(iteration => iteration.Id!.Value)
            .ToDictionary(
                group => group.Key,
                group => NormalizeCommitId(group.Last().SourceRefCommit?.CommitId));

    private static PullRequestThreadDocument MapThread(
        GitPullRequestCommentThread source,
        IReadOnlyDictionary<int, string?> iterationSourceCommits,
        int? latestIterationId)
    {
        int? reviewedIterationId = source.PullRequestThreadContext?.IterationContext?.SecondComparingIteration;
        string? reviewedCommitId = reviewedIterationId is > 0
            && iterationSourceCommits.TryGetValue(reviewedIterationId.Value, out string? commitId)
            ? commitId
            : null;
        string? latestCommitId = latestIterationId is not null
            && iterationSourceCommits.TryGetValue(latestIterationId.Value, out string? latestCommit)
            ? latestCommit
            : null;
        CommentThreadContext? context = source.ThreadContext;
        return new PullRequestThreadDocument(
            source.Id,
            StableEnum(source.Status),
            source.IsDeleted,
            IsPossiblyOutdated(reviewedCommitId, latestCommitId, reviewedIterationId, latestIterationId),
            context?.FilePath,
            context?.RightFileStart?.Line,
            context?.RightFileEnd?.Line,
            reviewedCommitId,
            latestCommitId,
            (source.Comments ?? []).Select(MapComment)
                .OrderBy(comment => comment.Id)
                .ToArray());
    }

    private static bool? IsPossiblyOutdated(
        string? reviewedCommitId,
        string? latestCommitId,
        int? reviewedIterationId,
        int? latestIterationId) =>
        string.IsNullOrWhiteSpace(reviewedCommitId)
            || string.IsNullOrWhiteSpace(latestCommitId)
            || reviewedIterationId is not > 0
            || latestIterationId is null
            ? null
            : reviewedIterationId < latestIterationId
                ? true
                : reviewedIterationId == latestIterationId
                    ? false
                    : null;

    private static string? NormalizeCommitId(string? commitId) =>
        string.IsNullOrWhiteSpace(commitId) ? null : commitId;

    private static PullRequestCommentDocument MapComment(Comment source) =>
        new(
            source.Id,
            source.ParentCommentId,
            ClassifyComment(StableEnum(source.CommentType)),
            source.Author?.UniqueName,
            source.Content,
            ToUtc(source.PublishedDate),
            source.IsDeleted);

    private static string ClassifyComment(string? type) => type switch
    {
        "text" => "human",
        "system" => "system",
        _ => "unknown"
    };

    private static DateTimeOffset? ToUtc(DateTime value) => value == default
        ? null
        : new DateTimeOffset(value.ToUniversalTime());

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null
        ? null
        : ToUtc(value.Value);

    private static Uri? ToUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;

    private static string? StableEnum<T>(T value) where T : struct, Enum
    {
        string text = value.ToString();
        return string.IsNullOrEmpty(text)
            ? null
            : char.ToLowerInvariant(text[0]) + text[1..];
    }
}
