using AdoWrite.AzureDevOps;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.Comments;

/// <summary>The lines a caller asked to comment on. <see cref="Side"/> is <c>right</c> (new) or <c>left</c> (old).</summary>
public sealed record LineSelection(string FilePath, int StartLine, int EndLine, string Side)
{
    public const string Right = "right";
    public const string Left = "left";
}

/// <summary>
/// Posts pull-request comments: a new thread, general or attached to lines, or a reply in an existing
/// thread. Every post first looks for an earlier comment of the signed-in user with the same key
/// anywhere in the pull request and returns it instead, before the lines are resolved again, so a
/// retry after a later push still finds the comment.
/// </summary>
internal sealed class PullRequestCommenter(IPullRequestCommentClient client, IPeopleDirectory people, Uri organizationUrl)
{
    private const string Target = "pullRequest";

    private readonly CommentComposer _composer = new(people);

    /// <summary>A new thread. With <paramref name="lines"/> it is attached to them in the latest iteration.</summary>
    public async Task<PostedCommentDocument> CommentAsync(
        int pullRequestId,
        LineSelection? lines,
        CommentDraft draft,
        CancellationToken cancellationToken)
    {
        PullRequestLocation location = await LocateAsync(pullRequestId, cancellationToken);
        string key = draft.KeyFor(Target, pullRequestId, lines?.FilePath, lines?.Side, lines?.StartLine, lines?.EndLine);
        Person author = await _composer.GetAuthorAsync(cancellationToken);
        if (FindMarked(await GetThreadsAsync(location, cancellationToken), key, author) is { } earlier)
        {
            return Result(CommentOutcomes.Duplicate, location, earlier.Thread.Id, earlier.CommentId, AnchorOf(earlier.Thread), key);
        }

        LineAnchor? anchor = lines is null ? null : await AnchorAsync(location, lines, cancellationToken);
        ComposedComment comment = await _composer.ComposeAsync(
            draft,
            key,
            author,
            () => UploadAsync(location, draft.Attachments, cancellationToken),
            cancellationToken);
        if (draft.DryRun)
        {
            return Result(CommentOutcomes.Preview, location, null, null, anchor?.Document, key, comment);
        }

        GitPullRequestCommentThread created = await AzureDevOpsCall.RunAsync(
            () => client.CreateThreadAsync(location, NewThread(comment.Text, anchor), cancellationToken),
            cancellationToken);
        Comment? first = created.Comments?.FirstOrDefault();
        return Result(
            CommentOutcomes.Created,
            location,
            created.Id,
            first?.Id,
            anchor?.Document,
            key,
            comment,
            markerKept: CommentMarker.Marks(first?.Content, key));
    }

    /// <summary>A reply to the first comment of an existing thread, so a conversation keeps one thread.</summary>
    public async Task<PostedCommentDocument> ReplyAsync(
        int pullRequestId,
        int threadId,
        CommentDraft draft,
        CancellationToken cancellationToken)
    {
        PullRequestLocation location = await LocateAsync(pullRequestId, cancellationToken);
        IReadOnlyList<GitPullRequestCommentThread> threads = await GetThreadsAsync(location, cancellationToken);
        GitPullRequestCommentThread thread = threads.FirstOrDefault(candidate => candidate.Id == threadId)
            ?? throw WriteException.NotFound(
                "threadNotFound",
                $"Pull request {pullRequestId} has no thread {threadId}. Read the pull request to find its thread IDs.");
        string key = draft.KeyFor("pullRequestReply", pullRequestId, threadId);
        Person author = await _composer.GetAuthorAsync(cancellationToken);
        if (FindMarked(threads, key, author) is { } earlier)
        {
            return Result(CommentOutcomes.Duplicate, location, earlier.Thread.Id, earlier.CommentId, AnchorOf(earlier.Thread), key);
        }

        ComposedComment comment = await _composer.ComposeAsync(
            draft,
            key,
            author,
            () => UploadAsync(location, draft.Attachments, cancellationToken),
            cancellationToken);
        if (draft.DryRun)
        {
            return Result(CommentOutcomes.Preview, location, threadId, null, null, key, comment);
        }

        var reply = new Comment
        {
            ParentCommentId = RootCommentId(thread),
            Content = comment.Text,
            CommentType = CommentType.Text
        };
        Comment created = await AzureDevOpsCall.RunAsync(
            () => client.CreateCommentAsync(location, threadId, reply, cancellationToken),
            cancellationToken);
        return Result(
            CommentOutcomes.Created,
            location,
            threadId,
            created.Id,
            null,
            key,
            comment,
            markerKept: CommentMarker.Marks(created.Content, key));
    }

    /// <summary>
    /// Uploads the files to the pull request under their hash-prefixed names. A file an earlier,
    /// interrupted run already uploaded is reused, so a retry never uploads it twice.
    /// </summary>
    private async Task<IReadOnlyList<AttachmentLink>> UploadAsync(
        PullRequestLocation location,
        IReadOnlyList<CommentAttachment> files,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Attachment> existing = await AzureDevOpsCall.RunAsync(
            () => client.GetAttachmentsAsync(location, cancellationToken),
            cancellationToken);
        var links = new List<AttachmentLink>(files.Count);
        foreach (CommentAttachment file in files)
        {
            Attachment? earlier = existing.FirstOrDefault(attachment =>
                string.Equals(attachment.DisplayName, file.StoredName, StringComparison.Ordinal) && attachment.Url is not null);
            if (earlier is not null)
            {
                links.Add(new AttachmentLink(file, new Uri(earlier.Url), Reused: true));
                continue;
            }

            Attachment uploaded = await AzureDevOpsCall.RunAsync(
                async () =>
                {
                    await using Stream content = file.Open();
                    return await client.UploadAttachmentAsync(location, file.StoredName, content, cancellationToken);
                },
                cancellationToken);
            links.Add(new AttachmentLink(file, new Uri(uploaded.Url), Reused: false));
        }

        return links;
    }

    private Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(PullRequestLocation location, CancellationToken cancellationToken) =>
        AzureDevOpsCall.RunAsync(() => client.GetThreadsAsync(location, cancellationToken), cancellationToken);

    private async Task<PullRequestLocation> LocateAsync(int pullRequestId, CancellationToken cancellationToken)
    {
        GitPullRequest pullRequest = await AzureDevOpsCall.RunAsync(
            () => client.GetPullRequestAsync(pullRequestId, cancellationToken),
            cancellationToken,
            WriteException.NotFound("pullRequestNotFound", $"Pull request {pullRequestId} was not found or the token cannot read it."));
        GitRepository? repository = pullRequest.Repository;
        if (repository?.ProjectReference is null)
        {
            throw WriteException.NotFound("pullRequestNotFound", $"Azure DevOps did not say which repository pull request {pullRequestId} belongs to.");
        }

        return new PullRequestLocation(
            pullRequestId,
            repository.ProjectReference.Id,
            repository.ProjectReference.Name,
            repository.Id,
            repository.Name);
    }

    /// <summary>
    /// Attaches the selection to the latest iteration, compared with the common commit as the pull
    /// request's "all changes" view shows it. The file must be changed by the pull request and have the
    /// selected lines on the chosen side.
    /// </summary>
    private async Task<LineAnchor> AnchorAsync(PullRequestLocation location, LineSelection lines, CancellationToken cancellationToken)
    {
        IReadOnlyList<GitPullRequestIteration> iterations = await AzureDevOpsCall.RunAsync(
            () => client.GetIterationsAsync(location, cancellationToken),
            cancellationToken);
        GitPullRequestIteration latest = iterations.Where(iteration => iteration.Id is not null).MaxBy(iteration => iteration.Id)
            ?? throw WriteException.Rejected("noIterations", $"Pull request {location.PullRequestId} has no iterations to comment on.");
        int iterationId = latest.Id!.Value;
        IReadOnlyList<GitPullRequestChange> changes = await AzureDevOpsCall.RunAsync(
            () => client.GetChangesAsync(location, iterationId, cancellationToken),
            cancellationToken);
        GitPullRequestChange change = changes.FirstOrDefault(candidate => string.Equals(candidate.Item?.Path, lines.FilePath, StringComparison.Ordinal))
            ?? throw WriteException.Rejected(
                "fileNotInPullRequest",
                $"{lines.FilePath} is not changed by pull request {location.PullRequestId}. Attach the comment to a changed file, or leave a general comment without --file.");
        bool right = lines.Side == LineSelection.Right;
        RejectMissingSide(change, lines, right);

        string? commitId = right ? latest.SourceRefCommit?.CommitId : latest.CommonRefCommit?.CommitId;
        if (string.IsNullOrEmpty(commitId))
        {
            throw WriteException.Rejected("iterationUnavailable", $"Azure DevOps did not say which commit iteration {iterationId} compares.");
        }

        string path = right ? lines.FilePath : change.OriginalPath ?? lines.FilePath;
        string text = await AzureDevOpsCall.RunAsync(
            () => client.GetFileTextAsync(location, path, commitId, cancellationToken),
            cancellationToken);
        string[] fileLines = SplitLines(text);
        if (lines.EndLine > fileLines.Length)
        {
            throw WriteException.Rejected(
                "lineOutOfRange",
                $"{path} has {fileLines.Length} lines on the {lines.Side} side, so lines {lines.StartLine}-{lines.EndLine} are outside it.");
        }

        return new LineAnchor(
            new CommentAnchorDocument(lines.FilePath, lines.Side, lines.StartLine, lines.EndLine, iterationId),
            change.ChangeTrackingId,
            fileLines[lines.EndLine - 1].Length + 1);
    }

    private static void RejectMissingSide(GitPullRequestChange change, LineSelection lines, bool right)
    {
        if (right && change.ChangeType.HasFlag(VersionControlChangeType.Delete))
        {
            throw WriteException.Rejected("lineSideUnavailable", $"{lines.FilePath} is deleted by the pull request, so only --side left has lines.");
        }

        if (!right && change.ChangeType.HasFlag(VersionControlChangeType.Add))
        {
            throw WriteException.Rejected("lineSideUnavailable", $"{lines.FilePath} is added by the pull request, so only --side right has lines.");
        }
    }

    /// <summary>
    /// Lines as Azure DevOps numbers them: split at line feeds only, so a form feed or Unicode line
    /// separator inside a line does not shift the numbers. A final line feed ends the last line.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        string[] lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>Only the signed-in user's own comments count, so a person quoting the marker cannot stand in for a post.</summary>
    private static (GitPullRequestCommentThread Thread, int CommentId)? FindMarked(
        IEnumerable<GitPullRequestCommentThread> threads,
        string key,
        Person author)
    {
        foreach (GitPullRequestCommentThread thread in threads.Where(thread => thread.IsDeleted != true))
        {
            Comment? marked = thread.Comments?.FirstOrDefault(comment =>
                comment.IsDeleted != true
                && Guid.TryParse(comment.Author?.Id, out Guid authorId)
                && authorId == author.Id
                && CommentMarker.Marks(comment.Content, key));
            if (marked is not null)
            {
                return (thread, marked.Id);
            }
        }

        return null;
    }

    /// <summary>The lines an earlier thread is attached to, as Azure DevOps stored them.</summary>
    private static CommentAnchorDocument? AnchorOf(GitPullRequestCommentThread thread)
    {
        CommentThreadContext? context = thread.ThreadContext;
        bool right = context?.RightFileStart is not null;
        CommentPosition? start = right ? context!.RightFileStart : context?.LeftFileStart;
        CommentPosition? end = right ? context!.RightFileEnd : context?.LeftFileEnd;
        return context?.FilePath is null || start is null || end is null
            ? null
            : new CommentAnchorDocument(
                context.FilePath,
                right ? LineSelection.Right : LineSelection.Left,
                start.Line,
                end.Line,
                thread.PullRequestThreadContext?.IterationContext?.SecondComparingIteration ?? 0);
    }

    private static GitPullRequestCommentThread NewThread(string text, LineAnchor? anchor)
    {
        var thread = new GitPullRequestCommentThread
        {
            Comments = [new Comment { ParentCommentId = 0, Content = text, CommentType = CommentType.Text }],
            Status = CommentThreadStatus.Active
        };
        if (anchor is null)
        {
            return thread;
        }

        CommentAnchorDocument lines = anchor.Document;
        var start = new CommentPosition { Line = lines.StartLine, Offset = 1 };
        var end = new CommentPosition { Line = lines.EndLine, Offset = anchor.EndOffset };
        thread.ThreadContext = lines.Side == LineSelection.Right
            ? new CommentThreadContext { FilePath = lines.FilePath, RightFileStart = start, RightFileEnd = end }
            : new CommentThreadContext { FilePath = lines.FilePath, LeftFileStart = start, LeftFileEnd = end };
        thread.PullRequestThreadContext = new GitPullRequestCommentThreadContext
        {
            ChangeTrackingId = anchor.ChangeTrackingId,
            // Equal iterations compare the latest iteration with the common commit, which is the
            // version the left-side lines were checked against.
            IterationContext = new CommentIterationContext
            {
                FirstComparingIteration = (short)lines.IterationId,
                SecondComparingIteration = (short)lines.IterationId
            }
        };
        return thread;
    }

    private static short RootCommentId(GitPullRequestCommentThread thread) =>
        thread.Comments?.Where(comment => comment.ParentCommentId == 0).Select(comment => comment.Id).DefaultIfEmpty((short)0).Min() ?? 0;

    private PostedCommentDocument Result(
        string outcome,
        PullRequestLocation location,
        int? threadId,
        int? commentId,
        CommentAnchorDocument? anchor,
        string key,
        ComposedComment? comment = null,
        bool? markerKept = null) =>
        new(
            outcome,
            Target,
            location.PullRequestId,
            threadId,
            commentId,
            BrowserUrl(location, threadId),
            anchor,
            comment?.Mentions ?? [],
            comment?.Attachments ?? [],
            key,
            markerKept,
            outcome == CommentOutcomes.Preview ? comment?.Text : null);

    private Uri BrowserUrl(PullRequestLocation location, int? threadId) =>
        new(
            organizationUrl,
            $"{Uri.EscapeDataString(location.ProjectName)}/_git/{Uri.EscapeDataString(location.RepositoryName)}/pullrequest/{location.PullRequestId}"
            + (threadId is null ? string.Empty : $"?discussionId={threadId}"));

    private sealed record LineAnchor(CommentAnchorDocument Document, int ChangeTrackingId, int EndOffset);
}
