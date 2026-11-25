using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Identity;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

/// <summary>Who a token belongs to. Azure DevOps answers for any valid token, whatever its scopes.</summary>
public interface ITokenOwnerLookup
{
    /// <exception cref="TokenOwnerException">Azure DevOps refused the token, or could not be asked.</exception>
    Task<string> FindDisplayNameAsync(Uri organizationUrl, string pat, CancellationToken cancellationToken);
}

public sealed class TokenOwnerException(string code, string message, bool transient, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Transient { get; } = transient;
}

public sealed class SdkTokenOwnerLookup : ITokenOwnerLookup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<string> FindDisplayNameAsync(Uri organizationUrl, string pat, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var connection = new VssConnection(organizationUrl, new VssBasicCredential(string.Empty, pat));
        try
        {
            await connection.ConnectAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TokenOwnerException("timeout", "The Azure DevOps request timed out.", true, exception);
        }
        catch (Exception exception) when (exception is VssUnauthorizedException || AdoReadExceptionTranslator.IsServiceFailure(exception))
        {
            (string code, string message, bool transient) = AdoReadExceptionTranslator.Describe(exception);
            throw new TokenOwnerException(code, message, transient, exception);
        }

        // An organization open to the public answers a token it does not know as "Anonymous" instead of refusing it.
        return connection.AuthorizedIdentity is { Descriptor: { } descriptor, DisplayName: { Length: > 0 } name }
            && !descriptor.IsUnauthenticatedIdentity()
            && !descriptor.IsSystemPublicAccessType()
                ? name
                : throw new TokenOwnerException("unauthorized", "The PAT was rejected by Azure DevOps.", false);
    }
}
