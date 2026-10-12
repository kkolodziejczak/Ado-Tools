using AdoRead.IntegrationTests.Privacy;
using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Text;
using AdoRead.WorkItems;

namespace AdoRead.IntegrationTests.WorkItems;

public sealed class WorkItemReaderIntegrationTests
{
    [Theory]
    [InlineData(true, "success", 0)]
    [InlineData(false, "notFound", 3)]
    public async Task OrganizationScopedFakeSource_ProducesExpectedStatusAndExitCode(
        bool found,
        string expectedStatus,
        int expectedExitCode)
    {
        var source = new OrganizationScopedFakeSource(found);
        var reader = new WorkItemReader(
            source,
            new WorkItemMapper(new RichTextNormalizer()),
            new Uri("https://dev.azure.com/example/"),
            TimeProvider.System, FullPrivacy.Value);

        var result = await reader.ReadAsync(77, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedExitCode, ExitCodes.FromStatus(result.Status));
        Assert.Equal(1, source.OrganizationLookupCount);
        Assert.Equal(found ? 1 : 0, source.CommentLookupCount);
    }

    private sealed class OrganizationScopedFakeSource(bool found) : IWorkItemSource
    {
        public int OrganizationLookupCount { get; private set; }

        public int CommentLookupCount { get; private set; }

        public Task<SourceWorkItem?> GetAsync(int id, CancellationToken cancellationToken)
        {
            OrganizationLookupCount++;
            SourceWorkItem? item = found
                ? new SourceWorkItem(
                    id,
                    1,
                    null,
                    new Dictionary<string, object?>
                    {
                        ["System.TeamProject"] = "Project",
                        ["System.Title"] = "Fixture story"
                    },
                    [])
                : null;
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<WorkItemCommentDocument>> GetAllCommentsAsync(
            string project,
            int id,
            CancellationToken cancellationToken)
        {
            CommentLookupCount++;
            return Task.FromResult<IReadOnlyList<WorkItemCommentDocument>>([]);
        }

        public Task<Stream> GetAttachmentContentAsync(
            string project,
            Guid attachmentId,
            string fileName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
