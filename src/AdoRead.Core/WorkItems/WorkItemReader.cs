using AdoRead.AzureDevOps;
using AdoRead.Output;

namespace AdoRead.WorkItems;

public sealed class WorkItemReader(
    IWorkItemSource source,
    WorkItemMapper mapper,
    Uri organizationUrl,
    TimeProvider timeProvider)
{
    public Task<SearchEnvelope<WorkItemDocument>> ReadAsync(
        int id,
        CancellationToken cancellationToken) =>
        ReadAsync(id, downloadImagesDirectory: null, cancellationToken);

    public async Task<SearchEnvelope<WorkItemDocument>> ReadAsync(
        int id,
        string? downloadImagesDirectory,
        CancellationToken cancellationToken)
    {
        var query = new WorkItemQuery(id, organizationUrl, downloadImagesDirectory);
        try
        {
            SourceWorkItem? workItem = await source.GetAsync(id, cancellationToken);
            if (workItem is null)
            {
                return CreateEnvelope(query, "notFound", [], []);
            }

            string project = RequireProject(workItem);
            IReadOnlyList<WorkItemCommentDocument> comments = await source.GetAllCommentsAsync(
                project,
                id,
                cancellationToken);
            WorkItemDocument document = mapper.Map(workItem, comments, organizationUrl);
            string status = "success";
            if (downloadImagesDirectory is not null)
            {
                IReadOnlyList<WorkItemImageCandidate> candidates =
                    WorkItemImageCandidateCollector.Collect(workItem, comments, organizationUrl);
                WorkItemMediaDocument media;
                try
                {
                    media = await new WorkItemImageDownloader(source).DownloadAsync(
                        candidates,
                        project,
                        downloadImagesDirectory,
                        cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return CreateEnvelope(
                        query,
                        "error",
                        [],
                        [new SearchError(
                            "command",
                            null,
                            "invalidImageOutputDirectory",
                            "The image output directory could not be used.",
                            false)]);
                }
                document = document with { Media = media };
                status = media.Items.Any(item => item.Status != "downloaded")
                    ? "partial"
                    : "success";

                if (status == "partial")
                {
                    return CreateEnvelope(
                        query,
                        status,
                        [document],
                        [new SearchError(
                            "media",
                            null,
                            "mediaIncomplete",
                            "One or more requested work-item images could not be downloaded.",
                            Transient: false)]);
                }
            }

            return CreateEnvelope(query, status, [document], []);
        }
        catch (WorkItemSourceException exception)
        {
            return CreateEnvelope(
                query,
                "error",
                [],
                [new SearchError("azureDevOps", null, exception.Code, exception.Message, exception.Transient)]);
        }
    }

    private SearchEnvelope<WorkItemDocument> CreateEnvelope(
        WorkItemQuery query,
        string status,
        IReadOnlyList<WorkItemDocument> results,
        IReadOnlyList<SearchError> errors) =>
        new(
            query.DownloadImagesDirectory is null ? "2.0" : "2.1",
            "workItemSearch",
            status,
            query,
            timeProvider.GetUtcNow(),
            results,
            errors);

    private static string RequireProject(SourceWorkItem workItem)
    {
        if (workItem.Fields.TryGetValue("System.TeamProject", out object? value)
            && !string.IsNullOrWhiteSpace(Convert.ToString(value)))
        {
            return Convert.ToString(value)!;
        }

        throw new WorkItemSourceException(
            "missingProject",
            "Azure DevOps returned a work item without System.TeamProject.",
            transient: false);
    }
}
