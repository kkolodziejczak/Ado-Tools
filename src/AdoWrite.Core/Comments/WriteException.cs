using AdoRead.AzureDevOps;
using AdoRead.Output;

namespace AdoWrite.Comments;

/// <summary>An expected reason a write was not made, already shaped as one envelope error and exit code.</summary>
internal sealed class WriteException(
    string scope,
    string code,
    string message,
    int exitCode,
    bool transient = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>A timed-out write may still have reached Azure DevOps; every write command is safe to retry.</summary>
    public const string TimeoutMessage =
        "The Azure DevOps request timed out. The change may still have been made: run the same command again. A comment's key finds the earlier comment instead of posting twice, and a vote or requeue that is already done is skipped.";

    private const string ScopesHint =
        " The write token needs Work Items (Read & write) for work-item comments, Code (Read & write) for pull-request comments, requeues and votes, and Identity (Read) for mentions.";

    public string Scope { get; } = scope;

    public string Code { get; } = code;

    public int ExitCode { get; } = exitCode;

    public bool Transient { get; } = transient;

    public SearchError ToError() => new(Scope, null, Code, Message, Transient);

    /// <summary>The request cannot be posted as given; nothing was sent to Azure DevOps.</summary>
    public static WriteException Rejected(string code, string message) =>
        new("command", code, message, ExitCodes.InvalidInputOrConfiguration);

    public static WriteException NotFound(string code, string message) =>
        new("azureDevOps", code, message, ExitCodes.NotFound);

    /// <summary>An SDK failure, with the stable code and safe message the read tools use.</summary>
    public static WriteException FromService(Exception exception)
    {
        (string code, string message, bool transient) = AdoReadExceptionTranslator.Describe(exception);
        if (code is "unauthorized" or "forbidden")
        {
            message += ScopesHint;
        }
        else if (code == "timeout")
        {
            message = TimeoutMessage;
        }

        return new("azureDevOps", code, message, code == "notFound" ? ExitCodes.NotFound : ExitCodes.Error, transient, exception);
    }
}

/// <summary>Runs one Azure DevOps call and turns its failure into a <see cref="WriteException"/>.</summary>
internal static class AzureDevOpsCall
{
    /// <summary>
    /// A missing resource becomes <paramref name="notFound"/> when given. A cancelled token is left to
    /// the command, which tells a timeout from a cancellation.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        Func<Task<T>> call,
        CancellationToken cancellationToken,
        WriteException? notFound = null)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (
            AdoReadExceptionTranslator.IsServiceFailure(exception) && !cancellationToken.IsCancellationRequested)
        {
            WriteException failure = WriteException.FromService(exception);
            throw failure.Code == "notFound" && notFound is not null ? notFound : failure;
        }
    }

    public static Task RunAsync(Func<Task> call, CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                await call();
                return true;
            },
            cancellationToken);
}
