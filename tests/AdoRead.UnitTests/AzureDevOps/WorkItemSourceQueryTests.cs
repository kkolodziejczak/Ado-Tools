using System.Net;
using AdoRead.AzureDevOps;
using AdoRead.Output;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.UnitTests.AzureDevOps;

public sealed class WorkItemSourceQueryTests
{
    [Fact]
    public async Task QueryAsync_ReturnsTheMatchedIdsInQueryOrder()
    {
        var client = new QueryClient
        {
            Result = new WorkItemQueryResult
            {
                QueryResultType = QueryResultType.WorkItem,
                WorkItems = [new WorkItemReference { Id = 9 }, new WorkItemReference { Id = 2 }]
            }
        };

        IReadOnlyList<int> ids = await new WorkItemSource(client).QueryAsync("SELECT [System.Id] FROM WorkItems", CancellationToken.None);

        Assert.Equal([9, 2], ids);
        Assert.Equal("SELECT [System.Id] FROM WorkItems", client.Wiql);
    }

    [Fact]
    public async Task QueryAsync_RejectsALinkQuery()
    {
        var client = new QueryClient
        {
            Result = new WorkItemQueryResult { QueryResultType = QueryResultType.WorkItemLink, WorkItemRelations = [] }
        };

        WorkItemSourceException exception = await Assert.ThrowsAsync<WorkItemSourceException>(
            () => new WorkItemSource(client).QueryAsync("SELECT [System.Id] FROM WorkItemLinks", CancellationToken.None));

        Assert.Equal("unsupportedWiql", exception.Code);
    }

    [Fact]
    public async Task QueryAsync_ReportsARejectedQueryWithoutEchoingTheServiceMessage()
    {
        var client = new QueryClient
        {
            Failure = new VssServiceResponseException(HttpStatusCode.BadRequest, "TF51005: secret field name", null)
        };

        WorkItemSourceException exception = await Assert.ThrowsAsync<WorkItemSourceException>(
            () => new WorkItemSource(client).QueryAsync("SELECT nonsense", CancellationToken.None));

        Assert.Equal("invalidWiql", exception.Code);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_KeepsTheUsualCodeForARefusedToken()
    {
        var client = new QueryClient
        {
            Failure = new VssServiceResponseException(HttpStatusCode.Unauthorized, "denied", null)
        };

        WorkItemSourceException exception = await Assert.ThrowsAsync<WorkItemSourceException>(
            () => new WorkItemSource(client).QueryAsync("SELECT [System.Id] FROM WorkItems", CancellationToken.None));

        Assert.Equal("unauthorized", exception.Code);
    }

    [Fact]
    public async Task GetManyAsync_ReturnsItemsInTheRequestedOrderWithIdentitiesMapped()
    {
        var client = new QueryClient
        {
            Items =
            [
                new WorkItem { Id = 2, Rev = 1, Fields = new Dictionary<string, object> { ["System.Title"] = "Two" } },
                new WorkItem
                {
                    Id = 9,
                    Rev = 3,
                    Fields = new Dictionary<string, object>
                    {
                        ["System.Title"] = "Nine",
                        ["System.AssignedTo"] = new IdentityRef { DisplayName = "Ana", UniqueName = "ana@example.com" }
                    }
                }
            ]
        };

        IReadOnlyList<SourceWorkItem> items = await new WorkItemSource(client).GetManyAsync([9, 2], CancellationToken.None);

        Assert.Equal([9, 2], items.Select(item => item.Id));
        Assert.Equal("ana@example.com", Assert.IsType<IdentityDocument>(items[0].Fields["System.AssignedTo"]).UniqueName);
        Assert.Contains("System.Tags", client.RequestedFields);
    }

    private sealed class QueryClient : IWorkItemTrackingClient
    {
        public WorkItemQueryResult? Result { get; init; }

        public Exception? Failure { get; init; }

        public IReadOnlyList<WorkItem> Items { get; init; } = [];

        public string? Wiql { get; private set; }

        public IReadOnlyList<string> RequestedFields { get; private set; } = [];

        public Task<WorkItemQueryResult> QueryByWiqlAsync(string wiql, CancellationToken cancellationToken)
        {
            Wiql = wiql;
            return Failure is null ? Task.FromResult(Result!) : throw Failure;
        }

        public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
            IReadOnlyList<int> ids,
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken)
        {
            RequestedFields = fields;
            return Task.FromResult<IReadOnlyList<WorkItem>>(Items.Where(item => ids.Contains(item.Id!.Value)).ToArray());
        }

        public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItemField2>> GetFieldsAsync(string project, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CommentList> GetCommentsAsync(
            string project,
            int workItemId,
            string? continuationToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
