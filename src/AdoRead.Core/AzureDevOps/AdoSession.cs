using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.Policy.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.Wiki.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.TestResults.WebApi;
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

    IPipelineSource Pipelines => throw new NotSupportedException();

    /// <summary>The identity ID of the token's owner, which <c>me</c> stands for.</summary>
    /// <exception cref="PullRequestSourceException">Azure DevOps could not be asked.</exception>
    Task<Guid> GetTokenOwnerIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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
    private readonly BuildHttpClient _buildClient;
    private readonly TestManagementHttpClient _testManagementClient;
    private readonly TestResultsHttpClient _testResultsClient;
    private readonly PolicyHttpClient _policyClient;

    public AdoSession(VssConnection connection, Uri organizationUrl, string pat)
    {
        _connection = connection;
        _workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();
        _projectClient = connection.GetClient<ProjectHttpClient>();
        _gitClient = connection.GetClient<GitHttpClient>();
        _workClient = connection.GetClient<WorkHttpClient>();
        _teamClient = connection.GetClient<TeamHttpClient>();
        _wikiClient = connection.GetClient<WikiHttpClient>();
        _buildClient = connection.GetClient<BuildHttpClient>();
        _testManagementClient = connection.GetClient<TestManagementHttpClient>();
        _testResultsClient = connection.GetClient<TestResultsHttpClient>();
        _policyClient = connection.GetClient<PolicyHttpClient>();
        _wikiRestClient = new WikiRestClient(organizationUrl, pat);
        var sdkWorkItemClient = new SdkWorkItemTrackingClient(_workItemClient);
        Projects = new ProjectCatalog(new SdkProjectClient(_projectClient));
        PullRequests = new PullRequestSource(new SdkPullRequestGitClient(_gitClient, _policyClient), organizationUrl);
        WorkItems = new WorkItemSource(sdkWorkItemClient);
        Sprints = new SprintSource(new SdkWorkClient(_workClient), sdkWorkItemClient, organizationUrl);
        Teams = new SdkTeamClient(_teamClient);
        Wikis = new WikiSource(new SdkWikiClient(new WikiPageSdk(_wikiClient)), _wikiRestClient, _wikiRestClient, new WikiGitClient(_gitClient));
        Pipelines = new PipelineSource(new SdkPipelineClient(_buildClient, _testManagementClient, _testResultsClient));
    }

    public IProjectCatalog Projects { get; }

    public IPullRequestSource PullRequests { get; }

    public IWorkItemSource WorkItems { get; }

    public ISprintSource Sprints { get; }

    public ITeamClient Teams { get; }

    public IWikiSource Wikis { get; }

    public IPipelineSource Pipelines { get; }

    public async Task<Guid> GetTokenOwnerIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _connection.ConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            throw AdoReadExceptionTranslator.PullRequestFailure(exception);
        }

        return _connection.AuthorizedIdentity?.Id is { } id && id != Guid.Empty
            ? id
            : throw new PullRequestSourceException("unauthorized", "The PAT was rejected by Azure DevOps.", transient: false);
    }

    public ValueTask DisposeAsync()
    {
        _workItemClient.Dispose();
        _projectClient.Dispose();
        _gitClient.Dispose();
        _workClient.Dispose();
        _teamClient.Dispose();
        _wikiClient.Dispose();
        _buildClient.Dispose();
        _testManagementClient.Dispose();
        _testResultsClient.Dispose();
        _policyClient.Dispose();
        _wikiRestClient.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
