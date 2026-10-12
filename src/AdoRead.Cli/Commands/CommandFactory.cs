using System.CommandLine;
using System.CommandLine.Parsing;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.Commands;

public sealed class CommandFactory(
    IConfigurationStore configurationStore,
    IJsonOutput jsonOutput,
    TimeProvider timeProvider,
    IPatProvider? patProvider = null,
    IAdoSessionFactory? sessionFactory = null)
{
    private readonly IConfigurationStore _configurationStore = configurationStore;
    private readonly IJsonOutput _jsonOutput = jsonOutput;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IPatProvider _patProvider = patProvider ?? new PatProvider();
    private readonly IAdoSessionFactory _sessionFactory = sessionFactory ?? new AdoSessionFactory();

    public CommandApplication Create()
    {
        var rootCommand = new RootCommand("Read Azure DevOps context as structured JSON.");
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Emit compact JSON.",
            Recursive = true
        };
        rootCommand.Options.Add(compactOption);
        Command configCommand = ConfigCommands.AddTo(
            rootCommand,
            compactOption,
            _configurationStore,
            _jsonOutput,
            _timeProvider);
        Command workItemCommand = WorkItemCommand.AddTo(
            rootCommand,
            compactOption,
            _configurationStore,
            _patProvider,
            _sessionFactory,
            _jsonOutput,
            _timeProvider);
        Command pullRequestCommand = PullRequestCommand.AddTo(
            rootCommand,
            compactOption,
            _configurationStore,
            _patProvider,
            _sessionFactory,
            _jsonOutput,
            _timeProvider);
        Command wikiPageCommand = WikiPageCommand.AddTo(
            rootCommand, compactOption, _configurationStore, _patProvider, _sessionFactory, _jsonOutput, _timeProvider);
        Command wikiSearchCommand = WikiSearchCommand.AddTo(rootCommand, compactOption, _configurationStore, _patProvider, _sessionFactory, _jsonOutput, _timeProvider);
        IReadOnlyDictionary<Command, string> wikiKinds = WikiDiscoveryCommands.AddTo(rootCommand, compactOption, _configurationStore, _patProvider, _sessionFactory, _jsonOutput, _timeProvider);
        IReadOnlyDictionary<Command, string> sprintKinds = SprintCommands.AddTo(
            rootCommand,
            compactOption,
            _configurationStore,
            _patProvider,
            _sessionFactory,
            _jsonOutput,
            _timeProvider);
        var kinds = new Dictionary<Command, string>
        {
            [configCommand] = "configuration",
            [workItemCommand] = "workItemSearch",
            [pullRequestCommand] = "pullRequestSearch",
            [wikiPageCommand] = "wikiPageLookup",
            [wikiSearchCommand] = "wikiFullTextSearch"
        };
        foreach ((Command command, string kind) in sprintKinds)
        {
            kinds[command] = kind;
        }
        foreach ((Command command, string kind) in wikiKinds) kinds[command] = kind;
        return new CommandApplication(rootCommand, compactOption, kinds, _jsonOutput, _timeProvider);
    }
}

public sealed class CommandApplication(
    RootCommand rootCommand,
    Option<bool> compactOption,
    IReadOnlyDictionary<Command, string> commandKinds,
    IJsonOutput jsonOutput,
    TimeProvider timeProvider)
{
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
