using AdoRead.Privacy;

namespace AdoRead.PullRequests;

/// <summary>Withholds pull-request text and comment bodies, and aliases every person.</summary>
public static class PullRequestPrivacy
{
    public static IReadOnlyList<PrivacyCategory> Categories { get; } =
        [PrivacyCategory.Text, PrivacyCategory.Comments, PrivacyCategory.People];

    public static PullRequestDocument Apply(PullRequestDocument pullRequest, EffectivePrivacy privacy) => pullRequest with
    {
        Description = privacy.Allows(PrivacyCategory.Text) ? pullRequest.Description : null,
        CreatorUniqueName = privacy.Person(pullRequest.CreatorUniqueName),
        Reviewers = pullRequest.Reviewers.Select(reviewer => Reviewer(reviewer, privacy)).ToArray(),
        Threads = pullRequest.Threads.Select(thread => ReviewThread(thread, privacy)).ToArray()
    };

    private static PullRequestReviewerDocument Reviewer(PullRequestReviewerDocument reviewer, EffectivePrivacy privacy) =>
        reviewer with { UniqueName = privacy.Person(reviewer.UniqueName) };

    private static PullRequestThreadDocument ReviewThread(PullRequestThreadDocument thread, EffectivePrivacy privacy) => thread with
    {
        Comments = thread.Comments.Select(comment => Comment(comment, privacy)).ToArray()
    };

    private static PullRequestCommentDocument Comment(PullRequestCommentDocument comment, EffectivePrivacy privacy) => comment with
    {
        AuthorUniqueName = privacy.Person(comment.AuthorUniqueName),
        Content = privacy.Allows(PrivacyCategory.Comments) ? comment.Content : null
    };
}
