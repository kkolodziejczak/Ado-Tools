using System.Net;
using AdoRead.Output;
using AdoRead.WorkItems;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

public sealed class WorkItemSource(IWorkItemTrackingClient client) : IWorkItemSource
{
    public async Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            WorkItem item = await client.GetWorkItemAsync(id, cancellationToken);
            return MapWorkItem(item);
        }
        catch (VssServiceResponseException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.WorkItemFailure(exception);
        }
    }

    public async Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
        string project,
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            var comments = new List<WorkItemCommentDocument>();
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            string? continuationToken = null;
            do
            {
                CommentList page = await client.GetCommentsAsync(
                    project,
                    id,
                    continuationToken,
                    cancellationToken);
                comments.AddRange((page.Comments ?? []).Select(MapComment));
                continuationToken = string.IsNullOrWhiteSpace(page.ContinuationToken)
                    ? null
                    : page.ContinuationToken;
                if (continuationToken is not null && !seenTokens.Add(continuationToken))
                {
                    throw new WorkItemSourceException(
                        "commentPaginationLoop",
                        "Azure DevOps repeated a work-item comment continuation token.",
                        transient: false);
                }
            }
            while (continuationToken is not null);

            return comments
                .OrderBy(comment => comment.Id)
                .ThenBy(comment => comment.Version)
                .ToArray();
        }
        catch (WorkItemSourceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.WorkItemFailure(exception);
        }
    }

    public async Task<Stream> GetAttachmentContentAsync(
        string project,
        Guid attachmentId,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAttachmentContentAsync(project, attachmentId, fileName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.WorkItemFailure(exception);
        }
    }

    private static SourceWorkItem MapWorkItem(WorkItem item) =>
        new(
            item.Id ?? 0,
            item.Rev ?? 0,
            ToUri(item.Url),
            (item.Fields ?? new Dictionary<string, object>())
                .ToDictionary(pair => pair.Key, pair => MapFieldValue(pair.Value), StringComparer.Ordinal),
            (item.Relations ?? [])
                .Select(relation => new SourceWorkItemRelation(
                    relation.Rel,
                    ToUri(relation.Url),
                    (relation.Attributes ?? new Dictionary<string, object>())
                        .ToDictionary(pair => pair.Key, pair => MapFieldValue(pair.Value), StringComparer.Ordinal)))
                .ToArray());

    /// <summary>
    /// Maps everything Azure DevOps supplies. <see cref="WorkItemCommentDocument.PlainText"/> is
    /// derived from the raw text by <see cref="WorkItemMapper"/> and is left null here.
    /// </summary>
    private static WorkItemCommentDocument MapComment(Comment comment) =>
        new(
            comment.Id,
            comment.Version,
            comment.CreatedBy?.UniqueName,
            comment.ModifiedBy?.UniqueName,
            ToUtc(comment.CreatedDate),
            ToUtc(comment.ModifiedDate),
            comment.IsDeleted,
            comment.Format.ToString().ToLowerInvariant(),
            comment.Text,
            comment.RenderedText,
            PlainText: null,
            (comment.Mentions ?? [])
                .Select(mention => new CommentMentionDocument(
                    mention.ArtifactId,
                    mention.ArtifactType,
                    mention.TargetId,
                    ToUri(mention.Url)))
                .ToArray(),
            (comment.Reactions ?? [])
                .Select(reaction => new CommentReactionDocument(
                    reaction.Type.ToString().ToLowerInvariant(),
                    reaction.Count,
                    reaction.IsCurrentUserEngaged))
                .ToArray(),
            ToUri(comment.Url));

    private static object? MapFieldValue(object? value) => value switch
    {
        null => null,
        IdentityRef identity => MapIdentity(identity),
        _ => value
    };

    private static IdentityDocument? MapIdentity(IdentityRef? identity) => identity is null
        ? null
        : new IdentityDocument(
            identity.DisplayName,
            identity.Id,
            identity.UniqueName,
            identity.Descriptor,
            ToUri(identity.Url),
            ToUri(identity.ImageUrl));

    private static DateTimeOffset? ToUtc(DateTime value) => value == default
        ? null
        : new DateTimeOffset(value.ToUniversalTime());

    private static Uri? ToUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
}
