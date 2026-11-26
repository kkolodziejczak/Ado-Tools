using AdoRead.WorkItems;

namespace AdoRead.UnitTests.WorkItems;

public sealed class WorkItemWiqlTests
{
    private const string Select = "SELECT [System.Id] FROM WorkItems";
    private const string Order = " ORDER BY [System.ChangedDate] DESC";

    [Fact]
    public void Build_WithoutFiltersSelectsEveryWorkItemNewestChangeFirst()
    {
        Assert.Equal(Select + Order, WorkItemWiql.Build(WorkItemFilter.None));
    }

    [Fact]
    public void Build_JoinsEveryFilterWithAnd()
    {
        var filter = WorkItemFilter.None with
        {
            Project = "Payments",
            Tags = ["needs-refinement", "backend"],
            Assignee = "me",
            States = ["New", "Active"],
            Areas = [@"Payments\Checkout"],
            Types = ["Bug", "User Story"]
        };

        Assert.Equal(
            Select
            + " WHERE [System.TeamProject] = 'Payments'"
            + " AND [System.Tags] CONTAINS 'needs-refinement'"
            + " AND [System.Tags] CONTAINS 'backend'"
            + " AND [System.AssignedTo] = @Me"
            + " AND [System.State] IN ('New', 'Active')"
            + @" AND ([System.AreaPath] UNDER 'Payments\Checkout')"
            + " AND [System.WorkItemType] IN ('Bug', 'User Story')"
            + Order,
            WorkItemWiql.Build(filter));
    }

    [Theory]
    [InlineData("me", "[System.AssignedTo] = @Me")]
    [InlineData("ME", "[System.AssignedTo] = @Me")]
    [InlineData("none", "[System.AssignedTo] = ''")]
    [InlineData("ana@example.com", "[System.AssignedTo] = 'ana@example.com'")]
    public void Build_AssigneeTakesMeNoneOrAName(string assignee, string clause)
    {
        Assert.Equal(
            Select + " WHERE " + clause + Order,
            WorkItemWiql.Build(WorkItemFilter.None with { Assignee = assignee }));
    }

    [Fact]
    public void Build_MatchesAnyOfSeveralAreas()
    {
        Assert.Equal(
            Select + @" WHERE ([System.AreaPath] UNDER 'A\One' OR [System.AreaPath] UNDER 'B')" + Order,
            WorkItemWiql.Build(WorkItemFilter.None with { Areas = [@"A\One", "B"] }));
    }

    [Fact]
    public void Build_DoublesQuotesSoAValueCannotEndItsString()
    {
        Assert.Equal(
            Select + " WHERE [System.Tags] CONTAINS 'it''s'' OR 1=1 --'" + Order,
            WorkItemWiql.Build(WorkItemFilter.None with { Tags = ["it's' OR 1=1 --"] }));
    }

    [Fact]
    public void Build_TrimsValuesAndDropsEmptyOnes()
    {
        Assert.Equal(
            Select + " WHERE [System.Tags] CONTAINS 'a' AND [System.State] IN ('New')" + Order,
            WorkItemWiql.Build(WorkItemFilter.None with { Tags = [" a ", " "], States = ["", " New "], Assignee = " " }));
    }
}
