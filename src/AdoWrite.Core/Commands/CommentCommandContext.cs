using System.CommandLine;
using System.Text.Json.Serialization;
using AdoRead.Commands;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;

namespace AdoWrite.Commands;

/// <summary>
/// Shared plumbing for the comment commands: resolve the target and the draft, load configuration,
/// check the organization, read the write token, apply the whole-command timeout, open one session,
/// and map every expected failure onto a single JSON envelope and exit code. Nothing reaches Azure
/// DevOps until the target, the draft and the configuration are valid.
/// </summary>
internal sealed class CommentCommandContext(CommandServices services, IAdoWriteSessionFactory sessionFactory)
{
    public async Task<int> RunAsync(
        string kind,
        ParseResult parseResult,
        CommentOptions options,
        Func<CommentTarget> resolveTarget,
        CancellationToken cancellationToken)
    {
        bool compact = parseResult.GetValue(services.CompactOption);
        try
        {
            CommentTarget target = resolveTarget();
            CommentDraft draft = await options.ReadDraftAsync(parseResult, cancellationToken);
            AdoReadConfiguration configuration = await services.ConfigurationStore.LoadAsync(
                parseResult.GetValue(options.Config),
                cancellationToken);
            if (!target.Reference.IsFor(configuration.OrganizationUrl))
            {
                throw WriteException.Rejected(
                    "organizationMismatch",
                    "The URL belongs to a different organization than the configured organization.");
            }

            string pat = services.PatProvider.GetRequiredPat(configuration.OrganizationUrl);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoWriteSession session = sessionFactory.Create(configuration.OrganizationUrl, pat);
            PostedCommentDocument posted = await target.PostAsync(session, configuration.OrganizationUrl, draft, requestTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = new SearchEnvelope<PostedCommentDocument>(
                "1.0",
                kind,
                "success",
                target.Query,
                services.TimeProvider.GetUtcNow(),
                [posted],
                []);
            await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return ExitCodes.Success;
        }
        catch (WriteException exception)
        {
            return await WriteErrorAsync(kind, exception.ToError(), exception.ExitCode, compact, cancellationToken);
        }
        catch (ConfigurationException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("configuration", null, exception.Code, exception.Message, false),
                ExitCodes.InvalidInputOrConfiguration,
                compact,
                cancellationToken);
        }
        catch (CredentialException exception)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("authentication", null, exception.Code, exception.Message, false),
                ExitCodes.Error,
                compact,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await WriteErrorAsync(
                kind,
                new SearchError("azureDevOps", null, "timeout", WriteException.TimeoutMessage, true),
                ExitCodes.Error,
                compact,
                CancellationToken.None);
        }
    }

    private async Task<int> WriteErrorAsync(
        string kind,
        SearchError error,
        int exitCode,
        bool compact,
        CancellationToken cancellationToken)
    {
        SearchEnvelope<object> envelope = SearchEnvelopeFactory.Error(kind, [error], services.TimeProvider.GetUtcNow());
        if (exitCode == ExitCodes.NotFound)
        {
            envelope = envelope with { Status = "notFound" };
        }

        await services.JsonOutput.WriteAsync(envelope, compact, cancellationToken);
        return exitCode;
    }
}

/// <summary>What a comment command posts to: the parsed reference, the query echoed in the envelope, and the post itself.</summary>
internal sealed record CommentTarget(
    AdoReference Reference,
    CommentQuery Query,
    Func<IAdoWriteSession, Uri, CommentDraft, CancellationToken, Task<PostedCommentDocument>> PostAsync);

internal sealed record CommentQuery(
    int Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ThreadId = null);
