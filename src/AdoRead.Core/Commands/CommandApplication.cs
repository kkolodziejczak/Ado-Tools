using System.CommandLine;
using System.CommandLine.Parsing;
using AdoRead.Output;

namespace AdoRead.Commands;

public sealed class CommandApplication(
    RootCommand rootCommand,
    Option<bool> compactOption,
    IReadOnlyDictionary<Command, string> commandKinds,
    IJsonOutput jsonOutput,
    TimeProvider timeProvider)
{
    public RootCommand RootCommand => rootCommand;

    public async Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken)
    {
        ParseResult parseResult = rootCommand.Parse(args);
        string kind = GetEnvelopeKind(parseResult);
        bool mediaRequested = HasParsedImageDownloadOption(parseResult, kind);
        bool compact = parseResult.GetValue(compactOption);
        if (parseResult.Errors.Count > 0)
        {
            SearchEnvelope<object> envelope = CreateErrorEnvelope(
                kind,
                [new SearchError(
                    "command",
                    null,
                    "invalidArguments",
                    "The command arguments are invalid.",
                    false)],
                timeProvider.GetUtcNow(),
                mediaRequested);
            await jsonOutput.WriteAsync(envelope, compact, cancellationToken);
            return ExitCodes.InvalidInputOrConfiguration;
        }

        try
        {
            var configuration = new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            };
            return await parseResult.InvokeAsync(configuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SearchEnvelope<object> envelope = CreateErrorEnvelope(
                kind,
                [SearchError.FromException(new OperationCanceledException())],
                timeProvider.GetUtcNow(),
                mediaRequested);
            await jsonOutput.WriteAsync(envelope, compact, CancellationToken.None);
            return ExitCodes.Cancelled;
        }
        catch (Exception exception)
        {
            SearchEnvelope<object> envelope = CreateErrorEnvelope(
                kind,
                [SearchError.FromException(exception)],
                timeProvider.GetUtcNow(),
                mediaRequested);
            await jsonOutput.WriteAsync(envelope, compact, CancellationToken.None);
            return ExitCodes.Error;
        }
    }

    private static SearchEnvelope<object> CreateErrorEnvelope(
        string kind,
        IEnumerable<SearchError> errors,
        DateTimeOffset generatedAtUtc,
        bool mediaRequested) =>
        kind is "pullRequestSearch" or "workItemSearch" or "wikiCatalog" or "wikiPageCatalog" or "wikiPageLookup" or "wikiFullTextSearch"
            ? SearchEnvelopeFactory.Error(kind, errors, generatedAtUtc) with
            {
                SchemaVersion = mediaRequested && kind == "workItemSearch" ? "2.1" : "2.0"
            }
            : SearchEnvelopeFactory.Error(kind, errors, generatedAtUtc);

    private bool HasParsedImageDownloadOption(ParseResult parseResult, string kind)
    {
        if (kind != "workItemSearch")
        {
            return false;
        }

        SymbolResult? current = parseResult.CommandResult;
        while (current is not null)
        {
            if (current is CommandResult commandResult
                && commandKinds.TryGetValue(commandResult.Command, out string? commandKind)
                && commandKind == "workItemSearch")
            {
                Option<string?>? mediaOption = commandResult.Command.Options
                    .OfType<Option<string?>>()
                    .SingleOrDefault(option =>
                        string.Equals(option.Name.TrimStart('-'), "download-images", StringComparison.Ordinal)
                        || option.Aliases.Any(alias => string.Equals(
                            alias.TrimStart('-'),
                            "download-images",
                            StringComparison.Ordinal)));
                return mediaOption is not null && parseResult.GetValue(mediaOption) is not null;
            }

            current = current.Parent;
        }

        return false;
    }

    private string GetEnvelopeKind(ParseResult parseResult)
    {
        SymbolResult? current = parseResult.CommandResult;
        while (current is not null)
        {
            if (current is CommandResult commandResult
                && commandKinds.TryGetValue(commandResult.Command, out string? kind))
            {
                return kind;
            }

            current = current.Parent;
        }

        return "configuration";
    }
}
