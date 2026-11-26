using AdoRead.Output;
using AdoWrite.AzureDevOps;
using AdoWrite.Commands;
using AdoWrite.Comments;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace AdoWrite.Reviews;

/// <summary>
/// The result of <c>pr resolve</c> or <c>pr unresolve</c>. <see cref="Outcome"/> is one of
/// <see cref="ThreadOutcomes"/>; <see cref="Status"/> is the thread's status after the command, and
/// <see cref="Comment"/> the reply posted first, when <c>--body</c> or <c>--body-file</c> gave one.
/// </summary>
public sealed record ThreadStatusDocument(
    string Outcome,
    int PullRequestId,
    int ThreadId,
    string Status,
    string PreviousStatus,
    Uri Url,
    PostedCommentDocument? Comment);

public static class ThreadOutcomes
{
    public const string Changed = "changed";
    public const string Unchanged = "unchanged";
    public const string Preview = "preview";

    /// <summary>The reply was handled, but Azure DevOps refused the status; the envelope's error says why.</summary>
    public const string Failed = "failed";

    /// <summary>The reply was handled, and the status change timed out, so it may or may not have been made.</summary>
    public const string Uncertain = "uncertain";
}

public static class ThreadKinds
{
    public const string PullRequestResolve = "pullRequestResolve";
    public const string PullRequestUnresolve = "pullRequestUnresolve";
}

/// <summary>
/// Resolves a thread, or makes it active again, after an optional reply in it. The reply goes first
/// and carries its duplicate key, and an unchanged status is not sent again, so running the command
/// again after any failure posts nothing twice and finishes the change.
/// </summary>
internal sealed class ThreadStatusChanger(IPullRequestCommentClient client, IPeopleDirectory people, Uri organizationUrl)
{
    /// <summary>
    /// Azure DevOps shows every status but active and pending as resolved, so resolving such a thread
    /// changes nothing, and only a resolved thread can be made active.
    /// </summary>
    private static bool IsResolved(CommentThreadStatus status) =>
        status is CommentThreadStatus.Fixed or CommentThreadStatus.WontFix or CommentThreadStatus.Closed or CommentThreadStatus.ByDesign;

    public async Task<WriteOutcome<ThreadStatusDocument>> ChangeAsync(
        int pullRequestId,
        int threadId,
        bool resolve,
        CommentDraft? reply,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        (_, PullRequestLocation location) = await PullRequestLookup.FindAsync(client, pullRequestId, cancellationToken);
        // Runs on one thread take turns, so two of them never both decide from the same old status.
        using IDisposable statusLock = await PostLock.AcquireAsync(organizationUrl, $"thread-status:{pullRequestId}:{threadId}", cancellationToken);
        GitPullRequestCommentThread thread = await FindThreadAsync(location, threadId, cancellationToken);
        PostedCommentDocument? comment = null;
        if (reply is not null)
        {
            comment = await new PullRequestCommenter(client, people, organizationUrl).ReplyAsync(pullRequestId, threadId, reply, cancellationToken);
            if (comment.ThreadId != threadId)
            {
                throw WriteException.Rejected(
                    "keyUsedInOtherThread",
                    $"Your reply with this key is in thread {comment.ThreadId}, so nothing was posted and thread {threadId} was left as it is. Pass another --key.");
            }

            // Someone may have changed the status while the reply was posted; decide from what it is now.
            thread = await FindThreadAsync(location, threadId, cancellationToken);
        }

        CommentThreadStatus previous = thread.Status;
        CommentThreadStatus wanted = resolve ? CommentThreadStatus.Fixed : CommentThreadStatus.Active;
        bool unchanged = IsResolved(previous) == resolve;
        Uri url = PullRequestLookup.BrowserUrl(organizationUrl, location, threadId);
        ThreadStatusDocument Document(string outcome, CommentThreadStatus status) =>
            new(outcome, pullRequestId, threadId, NameOf(status), NameOf(previous), url, comment);

        if (unchanged)
        {
            return new([Document(ThreadOutcomes.Unchanged, previous)]);
        }

        if (dryRun)
        {
            return new([Document(ThreadOutcomes.Preview, wanted)]);
        }

        try
        {
            await AzureDevOpsCall.RunAsync(
                () => client.UpdateThreadStatusAsync(location, threadId, wanted, cancellationToken),
                cancellationToken);
        }
        catch (WriteException exception) when (comment is not null)
        {
            // The reply is already handled; say so, with the reason the status may not be set.
            return new([Document(exception.Transient ? ThreadOutcomes.Uncertain : ThreadOutcomes.Failed, previous)], exception.ToError());
        }
        catch (OperationCanceledException) when (comment is not null)
        {
            // The whole command's time ran out during the status change, which may still have reached Azure DevOps.
            return new(
                [Document(ThreadOutcomes.Uncertain, previous)],
                new SearchError("azureDevOps", null, "timeout", WriteException.TimeoutMessage, true));
        }

        return new([Document(ThreadOutcomes.Changed, wanted)]);
    }

    private async Task<GitPullRequestCommentThread> FindThreadAsync(
        PullRequestLocation location,
        int threadId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GitPullRequestCommentThread> threads = await AzureDevOpsCall.RunAsync(
            () => client.GetThreadsAsync(location, cancellationToken),
            cancellationToken);
        return threads.FirstOrDefault(thread => thread.Id == threadId && thread.IsDeleted != true)
            ?? throw WriteException.NotFound(
                "threadNotFound",
                $"Pull request {location.PullRequestId} has no thread {threadId}. Read the pull request to find its thread IDs.");
    }

    /// <summary>The status as the read tool shows it: <c>active</c>, <c>fixed</c>, <c>wontFix</c>, <c>closed</c>, <c>byDesign</c> or <c>pending</c>.</summary>
    private static string NameOf(CommentThreadStatus status)
    {
        string name = status.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
