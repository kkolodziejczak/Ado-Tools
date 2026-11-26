using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.WorkItems;

namespace AdoRead.UnitTests.Commands;

public sealed class WorkItemListCommandTests
{
    [Fact]
    public async Task List_BuildsTheQueryFromFiltersAndReturnsShortEntries()
    {
        var source = new FakeWorkItemListSource { QueryIds = [7, 3] };
        source.Add(Item(3, "Fix login", "Bug", "Active", "ana@example.com", "needs-refinement; backend"));
        source.Add(Item(7, "Pay an invoice", "User Story", "New", assignee: null, tags: null));

        (int exitCode, JsonElement root) = await RunAsync(
            source,
            "work-item", "list", "--tag", "needs-refinement", "--assignee", "me", "--state", "New", "--state", "Active",
            "--type", "Bug", "--area", @"Payments\Checkout", "--project", "Payments");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Payments'"
            + " AND [System.Tags] CONTAINS 'needs-refinement' AND [System.AssignedTo] = @Me"
            + " AND [System.State] IN ('New', 'Active') AND ([System.AreaPath] UNDER 'Payments\\Checkout')"
            + " AND [System.WorkItemType] IN ('Bug') ORDER BY [System.ChangedDate] DESC",
            source.Wiql);
        Assert.Equal("workItemList", root.GetProperty("kind").GetString());
        Assert.Equal("success", root.GetProperty("status").GetString());
        Assert.Equal(source.Wiql, root.GetProperty("query").GetProperty("wiql").GetString());
        JsonElement results = root.GetProperty("results");
        Assert.Equal([7, 3], results.EnumerateArray().Select(entry => entry.GetProperty("id").GetInt32()));
        JsonElement bug = results[1];
        Assert.Equal("Fix login", bug.GetProperty("title").GetString());
        Assert.Equal("Bug", bug.GetProperty("type").GetString());
        Assert.Equal("Active", bug.GetProperty("state").GetString());
        Assert.Equal("ana@example.com", bug.GetProperty("assignedToUniqueName").GetString());
        Assert.Equal(["backend", "needs-refinement"], bug.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal("https://dev.azure.com/example/Payments/_workitems/edit/3", bug.GetProperty("browser").GetString());
        Assert.False(bug.TryGetProperty("description", out _));
        Assert.Equal(2, root.GetProperty("page").GetProperty("returnedCount").GetInt32());
        Assert.Equal(2, root.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_ReturnsTheFirstTopEntriesAndTheTotalThatMatched()
    {
        var source = new FakeWorkItemListSource { QueryIds = [5, 4, 3, 2, 1] };
        foreach (int id in source.QueryIds)
        {
            source.Add(Item(id, $"Item {id}"));
        }

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--top", "2");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([5, 4], root.GetProperty("results").EnumerateArray().Select(entry => entry.GetProperty("id").GetInt32()));
        Assert.Equal([5, 4], source.FetchedIds);
        JsonElement page = root.GetProperty("page");
        Assert.Equal(2, page.GetProperty("requestedTop").GetInt32());
        Assert.Equal(2, page.GetProperty("returnedCount").GetInt32());
        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_RunsAGivenWiqlQueryAsIs()
    {
        const string wiql = "SELECT [System.Id] FROM WorkItems WHERE [System.Tags] CONTAINS 'x'";
        var source = new FakeWorkItemListSource { QueryIds = [9] };
        source.Add(Item(9, "Nine"));

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--wiql", wiql);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(wiql, source.Wiql);
        Assert.Equal(9, root.GetProperty("results")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task List_RejectsWiqlCombinedWithFiltersBeforeCallingAzureDevOps()
    {
        var source = new FakeWorkItemListSource();

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--wiql", "SELECT [System.Id] FROM WorkItems", "--tag", "x");

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("workItemList", root.GetProperty("kind").GetString());
        Assert.Equal("conflictingOptions", root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(source.Wiql);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("201")]
    public async Task List_RejectsTopOutsideOneTo200(string top)
    {
        var source = new FakeWorkItemListSource();

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--top", top);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        Assert.Equal("invalidTop", root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Null(source.Wiql);
    }

    [Fact]
    public async Task List_WithNoMatchesSucceedsWithAnEmptyList()
    {
        var source = new FakeWorkItemListSource { QueryIds = [] };

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--tag", "nothing");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(0, root.GetProperty("results").GetArrayLength());
        Assert.Empty(source.FetchedIds);
    }

    [Fact]
    public async Task List_ReportsAQueryAzureDevOpsRejected()
    {
        var source = new FakeWorkItemListSource
        {
            QueryFailure = new WorkItemSourceException("invalidWiql", "Azure DevOps rejected the WIQL query.", transient: false)
        };

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--wiql", "SELECT nonsense");

        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Equal("error", root.GetProperty("status").GetString());
        Assert.Equal("invalidWiql", root.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_AliasesAssigneesWhenPeopleAreWithheld()
    {
        var source = new FakeWorkItemListSource { QueryIds = [3] };
        source.Add(Item(3, "Fix login", assignee: "ana@example.com"));

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--withhold", "people");

        Assert.Equal(ExitCodes.Success, exitCode);
        string? assignee = root.GetProperty("results")[0].GetProperty("assignedToUniqueName").GetString();
        Assert.NotNull(assignee);
        Assert.DoesNotContain("ana", assignee, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("people", root.GetProperty("privacy").GetProperty("aliased")[0].GetString());
    }

    [Fact]
    public async Task List_FullReturnsTheShowDocumentOfEveryMatch()
    {
        var source = new FakeWorkItemListSource { QueryIds = [7, 3] };
        source.Add(Item(3, "Fix login"));
        source.Add(Item(7, "Pay an invoice"));

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--tag", "x", "--full");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("workItemSearch", root.GetProperty("kind").GetString());
        JsonElement results = root.GetProperty("results");
        Assert.Equal([7, 3], results.EnumerateArray().Select(item => item.GetProperty("id").GetInt32()));
        Assert.True(results[0].TryGetProperty("comments", out _));
        Assert.True(results[0].TryGetProperty("description", out _));
        Assert.Equal([3, 7], source.ReadIds.Order());
        Assert.Empty(source.FetchedIds);
    }

    [Fact]
    public async Task List_FullReadsSeveralItemsAtOnceUpToTheConfiguredLimitAndKeepsTheQueryOrder()
    {
        int[] ids = [6, 5, 4, 3, 2, 1];
        var source = new FakeWorkItemListSource { QueryIds = ids, ReadDelay = TimeSpan.FromMilliseconds(50) };
        foreach (int id in ids)
        {
            source.Add(Item(id, $"Item {id}"));
        }

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--full");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(ids, root.GetProperty("results").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()));
        Assert.InRange(source.MaxConcurrentReads, 2, AdoReadConfiguration.DefaultMaxParallelProjects);
    }

    [Fact]
    public async Task List_FullIsPartialWhenOneMatchCannotBeRead()
    {
        var source = new FakeWorkItemListSource { QueryIds = [7, 3] };
        source.Add(Item(7, "Pay an invoice"));

        (int exitCode, JsonElement root) = await RunAsync(source, "work-item", "list", "--full");

        Assert.Equal(ExitCodes.Partial, exitCode);
        Assert.Equal("partial", root.GetProperty("status").GetString());
        Assert.Equal(7, root.GetProperty("results")[0].GetProperty("id").GetInt32());
        JsonElement error = root.GetProperty("errors")[0];
        Assert.Equal("workItemNotFound", error.GetProperty("code").GetString());
        Assert.Contains("3", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, JsonElement Root)> RunAsync(FakeWorkItemListSource source, params string[] args)
    {
        var writer = new StringWriter();
        CommandApplication application = TestTool.Umbrella(
            new ConfiguredStore(),
            new JsonOutput(writer),
            TimeProvider.System,
            new FakePatProvider(),
            new FakeSessionFactory(source));
        int exitCode = await application.InvokeAsync([.. args, "--compact"], CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
    }

    private static SourceWorkItem Item(
        int id,
        string title,
        string type = "Task",
        string state = "New",
        string? assignee = null,
        string? tags = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["System.TeamProject"] = "Payments",
            ["System.WorkItemType"] = type,
            ["System.Title"] = title,
            ["System.State"] = state,
            ["System.ChangedDate"] = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc)
        };
        if (assignee is not null)
        {
            fields["System.AssignedTo"] = new IdentityDocument("Ana", "id-ana", assignee, null, null, null);
        }

        if (tags is not null)
        {
            fields["System.Tags"] = tags;
        }

        return new SourceWorkItem(id, 1, null, fields, []);
    }

    private sealed class FakeWorkItemListSource : IWorkItemSource
    {
        private readonly Dictionary<int, SourceWorkItem> _items = [];

        public IReadOnlyList<int> QueryIds { get; init; } = [];

        public WorkItemSourceException? QueryFailure { get; init; }

        public string? Wiql { get; private set; }

        public List<int> FetchedIds { get; } = [];

        public List<int> ReadIds { get; } = [];

        public void Add(SourceWorkItem item) => _items[item.Id] = item;

        public Task<IReadOnlyList<int>> QueryAsync(string wiql, CancellationToken cancellationToken)
        {
            Wiql = wiql;
            return QueryFailure is null ? Task.FromResult(QueryIds) : throw QueryFailure;
        }

        public Task<IReadOnlyList<SourceWorkItem>> GetManyAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
        {
            FetchedIds.AddRange(ids);
            return Task.FromResult<IReadOnlyList<SourceWorkItem>>(ids.Where(_items.ContainsKey).Select(id => _items[id]).ToArray());
        }

        public TimeSpan ReadDelay { get; init; }

        public int MaxConcurrentReads { get; private set; }

        private int _concurrentReads;

        public async Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken)
        {
            lock (ReadIds)
            {
                ReadIds.Add(id);
                MaxConcurrentReads = Math.Max(MaxConcurrentReads, ++_concurrentReads);
            }

            try
            {
                await Task.Delay(ReadDelay, cancellationToken);
                return _items.GetValueOrDefault(id);
            }
            finally
            {
                lock (ReadIds)
                {
                    _concurrentReads--;
                }
            }
        }

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkItemCommentDocument>>([]);

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ConfiguredStore : IConfigurationStore
    {
        public string DefaultPath => "unused";

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
            Task.FromResult(AdoReadConfiguration.Create(new Uri("https://dev.azure.com/example")));

        public Task SaveAsync(
            string? overridePath,
            AdoReadConfiguration configuration,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePatProvider : IPatProvider
    {
        public string GetRequiredPat(Uri organizationUrl) => "test-pat";
    }

    private sealed class FakeSessionFactory(IWorkItemSource workItems) : IAdoSessionFactory
    {
        public IAdoSession Create(Uri organizationUrl, string pat) => new Session(workItems);

        private sealed class Session(IWorkItemSource workItems) : IAdoSession
        {
            public IWorkItemSource WorkItems { get; } = workItems;

            public IProjectCatalog Projects => throw new NotSupportedException();

            public IPullRequestSource PullRequests => throw new NotSupportedException();

            public ISprintSource Sprints => throw new NotSupportedException();

            public ITeamClient Teams => throw new NotSupportedException();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
