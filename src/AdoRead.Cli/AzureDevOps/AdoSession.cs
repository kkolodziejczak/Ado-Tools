using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.Wiki.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

/// <summary>
/// One authenticated Azure DevOps connection and the readers built on top of it.
/// </summary>
public interface IAdoSession : IAsyncDisposable
{
    IProjectCatalog Projects { get; }

    IPullRequestSource PullRequests { get; }

    IWorkItemSource WorkItems { get; }

    ISprintSource Sprints { get; }

    ITeamClient Teams { get; }

    IWikiSource Wikis => throw new NotSupportedException();
}

public interface IAdoSessionFactory
{
    IAdoSession Create(Uri organizationUrl, string pat);
}

public sealed class AdoSessionFactory : IAdoSessionFactory
{
    public IAdoSession Create(Uri organizationUrl, string pat)
    {
        ArgumentNullException.ThrowIfNull(organizationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(pat);

        var connection = new VssConnection(organizationUrl, new VssBasicCredential(string.Empty, pat));
        try
        {
            return new AdoSession(connection, organizationUrl, pat);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}

internal sealed class AdoSession : IAdoSession
{
    private readonly VssConnection _connection;
    private readonly WorkItemTrackingHttpClient _workItemClient;
    private readonly ProjectHttpClient _projectClient;
    private readonly GitHttpClient _gitClient;
    private readonly WorkHttpClient _workClient;
    private readonly TeamHttpClient _teamClient;
    private readonly WikiHttpClient _wikiClient;
    private readonly WikiRestClient _wikiRestClient;

    public AdoSession(VssConnection connection, Uri organizationUrl, string pat)
    {
        _connection = connection;
        _workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();
        _projectClient = connection.GetClient<ProjectHttpClient>();
        _gitClient = connection.GetClient<GitHttpClient>();
        _workClient = connection.GetClient<WorkHttpClient>();
        _teamClient = connection.GetClient<TeamHttpClient>();
        _wikiClient = connection.GetClient<WikiHttpClient>();
        _wikiRestClient = new WikiRestClient(organizationUrl, pat);
        var sdkWorkItemClient = new SdkWorkItemTrackingClient(_workItemClient);
        Projects = new ProjectCatalog(new SdkProjectClient(_projectClient));
        PullRequests = new PullRequestSource(new SdkPullRequestGitClient(_gitClient), organizationUrl);
        WorkItems = new WorkItemSource(sdkWorkItemClient);
        Sprints = new SprintSource(new SdkWorkClient(_workClient), sdkWorkItemClient, organizationUrl);
        Teams = new SdkTeamClient(_teamClient);
        Wikis = new WikiSource(new SdkWikiClient(new WikiPageSdk(_wikiClient)), _wikiRestClient, _wikiRestClient, new WikiGitClient(_gitClient));
    }

    public IProjectCatalog Projects { get; }

    public IPullRequestSource PullRequests { get; }

    public IWorkItemSource WorkItems { get; }

    public ISprintSource Sprints { get; }

    public ITeamClient Teams { get; }

    public IWikiSource Wikis { get; }

    public ValueTask DisposeAsync()
    {
        _workItemClient.Dispose();
        _projectClient.Dispose();
        _gitClient.Dispose();
        _workClient.Dispose();
        _teamClient.Dispose();
        _wikiClient.Dispose();
        _wikiRestClient.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
