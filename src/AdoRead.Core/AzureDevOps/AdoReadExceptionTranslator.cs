using System.Net;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoRead.AzureDevOps;

/// <summary>
/// Turns Azure DevOps SDK failures into stable codes without leaking service messages.
/// </summary>
internal static class AdoReadExceptionTranslator
{
    public static bool IsServiceFailure(Exception exception) =>
        exception is VssServiceException or HttpRequestException or TaskCanceledException;

    public static ProjectCatalogException ProjectFailure(Exception exception)
    {
        (string code, string message, bool transient) = Values(exception);
        return new ProjectCatalogException(code, message, transient, exception);
    }

    public static PullRequestSourceException PullRequestFailure(Exception exception)
    {
        (string code, string message, bool transient) = Values(exception);
        return new PullRequestSourceException(code, message, transient, exception);
    }

    public static WorkItemSourceException WorkItemFailure(Exception exception)
    {
        (string code, string message, bool transient) = Values(exception);
        return new WorkItemSourceException(code, message, transient, exception);
    }

    public static WikiSourceException WikiFailure(Exception exception)
    {
        (string code, string message, bool transient) = Values(exception);
        return new WikiSourceException(code, message, transient, exception);
    }

    /// <summary>Pipeline reads need two scopes beyond code reading, so authorization failures name them.</summary>
    public static PipelineSourceException PipelineFailure(Exception exception)
    {
        (string code, string message, bool transient) = Values(exception);
        if (code is "unauthorized" or "forbidden")
        {
            message += " Pipeline diagnosis needs Build (Read) and Test Management (Read) scopes on the PAT.";
        }

        return new PipelineSourceException(code, message, transient, exception);
    }

    private static (string Code, string Message, bool Transient) Values(Exception exception)
    {
        if (exception is VssServiceResponseException response)
        {
            return response.HttpStatusCode switch
            {
                HttpStatusCode.Unauthorized => ("unauthorized", "The PAT was rejected by Azure DevOps.", false),
                HttpStatusCode.Forbidden => ("forbidden", "The PAT cannot read this Azure DevOps resource.", false),
                HttpStatusCode.NotFound => ("notFound", "The Azure DevOps resource was not found.", false),
                HttpStatusCode.TooManyRequests => ("throttled", "Azure DevOps throttled the request.", true),
                >= HttpStatusCode.InternalServerError =>
                    ("serviceUnavailable", "Azure DevOps could not complete the request.", true),
                _ => ("azureDevOpsError", "Azure DevOps rejected the request.", false)
            };
        }

        return exception switch
        {
            VssUnauthorizedException => ("unauthorized", "The PAT was rejected by Azure DevOps.", false),
            TaskCanceledException => ("timeout", "The Azure DevOps request timed out.", true),
            HttpRequestException => ("networkError", "Azure DevOps could not be reached.", true),
            _ => ("azureDevOpsError", "Azure DevOps could not complete the request.", false)
        };
    }
}
