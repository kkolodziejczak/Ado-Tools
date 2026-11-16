using Microsoft.TeamFoundation.Policy.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Identity.Client;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.AzureDevOps;

/// <summary>One connection authenticated with the write token, and the clients the write commands use.</summary>
public interface IAdoWriteSession : IAsyncDisposable
{
    IPullRequestCommentClient PullRequests { get; }

    IWorkItemCommentClient WorkItems { get; }

    IPeopleDirectory People { get; }

    IPullRequestReviewClient Reviews { get; }
}

public interface IAdoWriteSessionFactory
{
    IAdoWriteSession Create(Uri organizationUrl, string pat);
}

public sealed class AdoWriteSessionFactory : IAdoWriteSessionFactory
{
    public IAdoWriteSession Create(Uri organizationUrl, string pat)
    {
        ArgumentNullException.ThrowIfNull(organizationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(pat);

        var connection = new VssConnection(organizationUrl, new VssBasicCredential(string.Empty, pat));
        try
        {
            return new AdoWriteSession(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}

internal sealed class AdoWriteSession : IAdoWriteSession
{
    private readonly VssConnection _connection;
    private readonly GitHttpClient _gitClient;
    private readonly WorkItemTrackingHttpClient _workItemClient;
    private readonly IdentityHttpClient _identityClient;
    private readonly PolicyHttpClient _policyClient;

    public AdoWriteSession(VssConnection connection)
    {
        _connection = connection;
        _gitClient = connection.GetClient<GitHttpClient>();
        _workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();
        _identityClient = connection.GetClient<IdentityHttpClient>();
        _policyClient = connection.GetClient<PolicyHttpClient>();
        PullRequests = new SdkPullRequestCommentClient(_gitClient);
        WorkItems = new SdkWorkItemCommentClient(_workItemClient);
        People = new SdkPeopleDirectory(connection, _identityClient);
        Reviews = new SdkPullRequestReviewClient(_policyClient, _gitClient);
    }

    public IPullRequestCommentClient PullRequests { get; }

    public IWorkItemCommentClient WorkItems { get; }

    public IPeopleDirectory People { get; }

    public IPullRequestReviewClient Reviews { get; }

    public ValueTask DisposeAsync()
    {
        _gitClient.Dispose();
        _workItemClient.Dispose();
        _identityClient.Dispose();
        _policyClient.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
