using System.Text.Json;
using AdoRead.PullRequests;
using AdoRead.Privacy;
using AdoRead.Sprints;
using AdoRead.WorkItems;
using AdoRead.Wikis;

namespace AdoRead.UnitTests.Privacy;

public sealed class PrivacyDocumentTests
{
    private static EffectivePrivacy Minimal => TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Minimal));

    private static EffectivePrivacy Standard => TestPrivacy.For(new PrivacyPolicy(PrivacyLevel.Standard));

    [Fact]
    public void PullRequest_MinimalWithholdsTextAndCommentsAndAliasesPeople()
    {
        PullRequestDocument pullRequest = PullRequestPrivacy.Apply(PrivacySamples.PullRequest(), Minimal);

        Assert.Equal("Fabricated retry policy", pullRequest.Title);
        Assert.Null(pullRequest.Description);
        Assert.Equal(PrivacySamples.EngineerAlias, pullRequest.CreatorUniqueName);
        Assert.Equal(PrivacySamples.OwnerAlias, Assert.Single(pullRequest.Reviewers).UniqueName);
        PullRequestThreadDocument thread = Assert.Single(pullRequest.Threads);
        Assert.Equal("/src/Retry.cs", thread.FilePath);
        Assert.Equal(4, thread.StartLine);
        PullRequestCommentDocument comment = Assert.Single(thread.Comments);
        Assert.Equal(1, comment.Id);
        Assert.Null(comment.Content);
        Assert.Equal(PrivacySamples.OwnerAlias, comment.AuthorUniqueName);
    }

    [Fact]
    public void PullRequest_StandardKeepsTextAndCommentsAndAliasesPeople()
    {
        PullRequestDocument pullRequest = PullRequestPrivacy.Apply(PrivacySamples.PullRequest(), Standard);

        Assert.Equal("Adds a fabricated retry policy.", pullRequest.Description);
        Assert.Equal("Fabricated review comment.", Assert.Single(Assert.Single(pullRequest.Threads).Comments).Content);
        Assert.Equal(PrivacySamples.EngineerAlias, pullRequest.CreatorUniqueName);
    }

    [Fact]
    public void PullRequest_FullKeepsEverything()
    {
        PullRequestDocument pullRequest = PullRequestPrivacy.Apply(PrivacySamples.PullRequest(), TestPrivacy.Full);

        Assert.Equal("Adds a fabricated retry policy.", pullRequest.Description);
        Assert.Equal(PrivacySamples.EngineerName, pullRequest.CreatorUniqueName);
        Assert.Equal(PrivacySamples.OwnerName, Assert.Single(pullRequest.Reviewers).UniqueName);
    }

    [Fact]
    public void PullRequest_SectionNamesWhatMinimalWithheldAndWhatStandardAliased()
    {
        PrivacySection minimal = Minimal.Section(PullRequestPrivacy.Categories);
        PrivacySection standard = Standard.Section(PullRequestPrivacy.Categories);

        Assert.Equal(["text", "comments"], minimal.Withheld);
        Assert.Equal(["people"], minimal.Aliased);
        Assert.Empty(standard.Withheld);
        Assert.Equal(["people"], standard.Aliased);
    }

    [Fact]
    public void WorkItem_MinimalWithholdsTextCommentsAndCustomFields()
    {
        WorkItemDocument workItem = PrivacySamples.WorkItem(Minimal);

        Assert.Equal("Fabricated checkout story", workItem.Title);
        Assert.Null(workItem.Description.Raw);
        Assert.Null(workItem.Description.PlainText);
        Assert.Null(workItem.AcceptanceCriteria.Raw);
        Assert.Empty(workItem.CustomFields);
        Assert.Equal(PrivacySamples.EngineerAlias, workItem.AssignedToUniqueName);
        Assert.Equal(PrivacySamples.EngineerAlias, workItem.ChangedByUniqueName);
        WorkItemCommentDocument comment = Assert.Single(workItem.Comments);
        Assert.Equal(501, comment.Id);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero), comment.CreatedAtUtc);
        Assert.Null(comment.RawText);
        Assert.Null(comment.RenderedHtml);
        Assert.Null(comment.PlainText);
        Assert.Equal(PrivacySamples.OwnerAlias, comment.CreatedByUniqueName);
        CommentMentionDocument mention = Assert.Single(comment.Mentions);
        Assert.NotEqual("00000000-0000-0000-0000-0000000000b2", mention.TargetId);
        Assert.Null(mention.Url);
    }

    [Fact]
    public void WorkItem_StandardKeepsTextAndCommentsButWithholdsCustomFields()
    {
        WorkItemDocument workItem = PrivacySamples.WorkItem(Standard);

        Assert.Equal("<p>Fabricated description</p>", workItem.Description.Raw);
        Assert.Contains("Fabricated criteria", workItem.AcceptanceCriteria.Raw);
        Assert.Empty(workItem.CustomFields);
        Assert.Equal("<p>Fabricated comment</p>", Assert.Single(workItem.Comments).RawText);
        Assert.Equal(PrivacySamples.EngineerAlias, workItem.AssignedToUniqueName);
    }

    [Fact]
    public void WorkItem_FullKeepsIdentityCustomFieldsAsTheyAre()
    {
        WorkItemDocument workItem = PrivacySamples.WorkItem(TestPrivacy.Full);

        Assert.Equal("Fabricated note", workItem.CustomFields["Custom.Notes"].GetString());
        Assert.Equal(PrivacySamples.OwnerName, workItem.CustomFields["Custom.Owner"].GetProperty("uniqueName").GetString());
        Assert.Equal(PrivacySamples.EngineerName, workItem.AssignedToUniqueName);
    }

    [Fact]
    public void WorkItem_AliasesIdentityCustomFieldsWhenCustomFieldsAreAllowedButPeopleAreNot()
    {
        EffectivePrivacy privacy = TestPrivacy.For(new PrivacyPolicy(
            PrivacyLevel.Standard,
            new Dictionary<PrivacyCategory, bool> { [PrivacyCategory.CustomFields] = true }));

        WorkItemDocument workItem = PrivacySamples.WorkItem(privacy);

        JsonElement owner = workItem.CustomFields["Custom.Owner"];
        Assert.Equal(PrivacySamples.OwnerAlias, owner.GetProperty("uniqueName").GetString());
        Assert.Equal(PrivacySamples.OwnerAlias, owner.GetProperty("displayName").GetString());
        Assert.Equal("Fabricated note", workItem.CustomFields["Custom.Notes"].GetString());
        Assert.DoesNotContain(PrivacySamples.OwnerName, owner.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItem_AliasesTheSamePersonTheSameWayAsOnAPullRequest()
    {
        WorkItemDocument workItem = PrivacySamples.WorkItem(Standard);
        PullRequestDocument pullRequest = PullRequestPrivacy.Apply(PrivacySamples.PullRequest(), Standard);

        Assert.Equal(pullRequest.CreatorUniqueName, workItem.AssignedToUniqueName);
    }

    [Fact]
    public void WorkItem_SectionForStandardNamesCustomFieldsAndImagesAsWithheld()
    {
        PrivacySection section = Standard.Section(WorkItemPrivacy.Categories);

        Assert.Equal(["customFields", "images"], section.Withheld);
        Assert.Equal(["people"], section.Aliased);
    }

    [Fact]
    public void WikiPage_MinimalWithholdsTheBodyButKeepsThePath()
    {
        WikiPageDocument page = WikiPrivacy.Page(PrivacySamples.WikiPage(), Minimal);

        Assert.Null(page.Content);
        Assert.Equal("/Architecture/Authentication", page.Path);
        Assert.Equal(51, page.Id);
        Assert.Equal(["text"], Minimal.Section(WikiPrivacy.PageCategories).Withheld);
    }

    [Fact]
    public void WikiPage_StandardKeepsTheBody()
    {
        Assert.Equal(
            "# Authentication\n\nUse the fabricated identity provider.",
            WikiPrivacy.Page(PrivacySamples.WikiPage(), Standard).Content);
    }

    [Fact]
    public void WikiSearch_MinimalWithholdsHighlightsButKeepsTheHit()
    {
        WikiSearchHitDocument hit = WikiPrivacy.SearchHit(PrivacySamples.WikiSearchHit(), Minimal);

        Assert.Empty(hit.Matches);
        Assert.Equal("/Architecture/Authentication.md", hit.GitPath);
        Assert.Equal("Fabricated.wiki", hit.Wiki.Name);
    }

    [Fact]
    public void WikiSearch_StandardKeepsHighlights()
    {
        Assert.Equal(["fabricated identity provider"], Assert.Single(WikiPrivacy.SearchHit(PrivacySamples.WikiSearchHit(), Standard).Matches).Highlights);
    }

    [Fact]
    public void Sprint_MinimalAliasesTheAssigneeUnderTheSamePersonAsTheWorkItem()
    {
        SprintDocument sprint = SprintPrivacy.Apply(PrivacySamples.Sprint(), Minimal);

        SprintWorkItemDocument item = Assert.Single(sprint.WorkItems);
        Assert.Equal(PrivacySamples.EngineerAlias, item.AssignedTo);
        Assert.Equal("Fabricated checkout story", item.Title);
        Assert.Equal(["people"], Minimal.Section(SprintPrivacy.Categories).Aliased);
    }

    [Fact]
    public void Sprint_FullKeepsTheDisplayName()
    {
        SprintDocument sprint = SprintPrivacy.Apply(PrivacySamples.Sprint(), TestPrivacy.Full);

        Assert.Equal("Example Engineer", Assert.Single(sprint.WorkItems).AssignedTo);
    }

    [Fact]
    public void Team_MinimalWithholdsTheDescriptionOnly()
    {
        TeamDocument team = TeamPrivacy.Apply(PrivacySamples.Team(), Minimal);

        Assert.Null(team.Description);
        Assert.Equal("Fabricated.Alpha Team", team.Name);
        Assert.Equal("Fabricated team description", TeamPrivacy.Apply(PrivacySamples.Team(), Standard).Description);
    }
}
