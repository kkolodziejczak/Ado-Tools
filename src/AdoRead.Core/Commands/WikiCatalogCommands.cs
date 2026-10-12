using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.Commands;

public static class WikiCatalogCommands
{
    public static IReadOnlyDictionary<Command, string> Configure(Command target, ToolCommandNames names, CommandServices services)
    {
        var (store, pat, sessions, output, clock, compact) = services;
        var wikis = new Command("list", "List the Wikis in a project.");
        var wikiPages = new Command("pages", "List the pages of one Wiki, with paging.");
        var project = new Option<string?>("--project");
        var config = new Option<string?>("--config");
        wikis.Options.Add(project);
        wikis.Options.Add(config);
        wikis.SetAction((result, token) => RunAsync(
            "wikiCatalog",
            names,
            result.GetValue(project),
            result.GetValue(config),
            result.GetValue(compact),
            store,
            pat,
            sessions,
            output,
            clock,
            token,
            "projectRequired",
            "No project given and none configured.",
            new CommandHintDocument(names.Root.Path("projects"), []),
            (session, configuration, value, cancellation) => new WikiCatalogReader(session.Wikis, configuration.OrganizationUrl, clock, names).ReadWikisAsync(value, cancellation)));
        var pageProject = new Option<string?>("--project");
        var wiki = new Option<string?>("--wiki");
        var version = new Option<string?>("--version");
        var versionType = new Option<string?>("--version-type");
        var top = new Option<int?>("--top");
        var continuation = new Option<string?>("--continuation-token");
        var pageConfig = new Option<string?>("--config");
        foreach (Option option in new Option[] { pageProject, wiki, version, versionType, top, continuation, pageConfig }) wikiPages.Options.Add(option);
        wikiPages.SetAction((result, token) => RunPagesAsync(names, result.GetValue(pageProject), result.GetValue(wiki), result.GetValue(version), result.GetValue(versionType), result.GetValue(top), result.GetValue(continuation), result.GetValue(pageConfig), result.GetValue(compact), store, pat, sessions, output, clock, token));
        target.Subcommands.Add(wikis);
        target.Subcommands.Add(wikiPages);
        return new Dictionary<Command, string> { [wikis] = "wikiCatalog", [wikiPages] = "wikiPageCatalog" };
    }

    private static async Task<int> RunPagesAsync(ToolCommandNames names, string? project, string? wiki, string? version, string? versionType, int? top, string? continuation, string? config, bool compact, IConfigurationStore store, IPatProvider pat, IAdoSessionFactory sessions, IJsonOutput output, TimeProvider clock, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(wiki)) return await Error("wikiPageCatalog", "wikiRequired", "A Wiki is required.", new CommandHintDocument(names.Path("list"), []), output, clock, compact, token, "command");
        wiki = wiki.Trim();
        if (!WikiIdentifier.IsSafeWiki(wiki)) return await Error("wikiPageCatalog", "invalidWikiReference", "The Azure DevOps Wiki identifier is invalid.", null, output, clock, compact, token, "command");
        if (!string.IsNullOrWhiteSpace(versionType) && string.IsNullOrWhiteSpace(version)) return await Error("wikiPageCatalog", "invalidWikiReference", "A version type requires a version.", null, output, clock, compact, token, "command");
        int requestedTop = top ?? 50;
        if (requestedTop is < 1 or > 100) return await Error("wikiPageCatalog", "invalidTop", "The page limit must be between 1 and 100.", null, output, clock, compact, token, "command");
        return await RunAsync("wikiPageCatalog", names, project, config, compact, store, pat, sessions, output, clock, token, "projectRequired", "No project given and none configured.", new CommandHintDocument(names.Root.Path("projects"), []), async (session, configuration, value, cancellation) =>
        {
            WikiVersionDocument? selectedVersion = await ResolveVersionAsync(session.Wikis, value, wiki, version, versionType, cancellation);
            return await new WikiCatalogReader(session.Wikis, configuration.OrganizationUrl, clock, names).ReadPagesAsync(value, wiki, selectedVersion, requestedTop, continuation, cancellation);
        });
    }

    private static async Task<WikiVersionDocument?> ResolveVersionAsync(IWikiSource source, string project, string wiki, string? version, string? versionType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        IReadOnlyList<SourceWiki> wikiMatches = (await source.GetWikisAsync(project, cancellationToken)).Where(candidate => string.Equals(candidate.Id.ToString(), wiki, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.Name, wiki, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (wikiMatches.Count != 1) throw new ConfigurationException("wikiRequired", "The selected Wiki must resolve to exactly one Wiki.");
        return WikiVersionResolver.Resolve(wikiMatches[0].Versions, version, versionType) ?? throw new ConfigurationException("invalidWikiReference", "The Wiki version must match exactly one advertised version.");
    }

    private static async Task<int> RunAsync<T>(string kind, ToolCommandNames names, string? project, string? config, bool compact, IConfigurationStore store, IPatProvider pat, IAdoSessionFactory sessions, IJsonOutput output, TimeProvider clock, CancellationToken token, string missingCode, string missingMessage, CommandHintDocument missingHint, Func<IAdoSession, AdoReadConfiguration, string, CancellationToken, Task<SearchEnvelope<T>>> read)
    {
        string? selectedProject = null;
        if (project is not null && string.IsNullOrWhiteSpace(project)) return await Error(kind, "projectRequired", "The explicit project cannot be blank.", missingHint, output, clock, compact, token, "command");
        project = project?.Trim();
        if (project is not null && !WikiIdentifier.IsSafeProject(project)) return await Error(kind, "invalidProject", "The Azure DevOps project identifier is invalid.", null, output, clock, compact, token, "command");
        try
        {
            AdoReadConfiguration configuration = await store.LoadAsync(config, token);
            selectedProject = project ?? configuration.DefaultProject ?? throw new ConfigurationException(missingCode, missingMessage);
            if (!WikiIdentifier.IsSafeProject(selectedProject)) throw new ConfigurationException("invalidProject", "The configured Azure DevOps project identifier is invalid.");
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            requestTimeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessions.Create(configuration.OrganizationUrl, pat.GetRequiredPat());
            SearchEnvelope<T> envelope = await read(session, configuration, selectedProject, requestTimeout.Token);
            await output.WriteAsync(envelope, compact, token);
            return ExitCodes.FromStatus(envelope.Status);
        }
        catch (ConfigurationException exception)
        {
            CommandHintDocument next = kind == "wikiPageCatalog" && selectedProject is not null && exception.Code is "wikiRequired" or "invalidWikiReference"
                ? new CommandHintDocument(names.Path("list"), ["--project", selectedProject])
                : missingHint;
            return await Error(kind, exception.Code, exception.Message, next, output, clock, compact, token);
        }
        catch (CredentialException exception) { return await WriteError(kind, "authentication", exception.Code, exception.Message, false, ExitCodes.Error, output, clock, compact, token); }
        catch (WikiSourceException exception) { return await WriteError(kind, "azureDevOps", exception.Code, exception.Message, exception.Transient, ExitCodes.Error, output, clock, compact, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return await WriteError(kind, "azureDevOps", "timeout", "The Azure DevOps request timed out.", true, ExitCodes.Error, output, clock, compact, CancellationToken.None); }
    }

    private static async Task<int> Error(string kind, string code, string message, CommandHintDocument? next, IJsonOutput output, TimeProvider clock, bool compact, CancellationToken token, string scope = "configuration")
    {
        await output.WriteAsync(SearchEnvelopeFactory.Error(kind, [new SearchError(scope, null, code, message, false, next)], clock.GetUtcNow()) with { SchemaVersion = "2.0" }, compact, token);
        return ExitCodes.InvalidInputOrConfiguration;
    }

    private static async Task<int> WriteError(string kind, string scope, string code, string message, bool transient, int exitCode, IJsonOutput output, TimeProvider clock, bool compact, CancellationToken token)
    {
        await output.WriteAsync(SearchEnvelopeFactory.Error(kind, [new SearchError(scope, null, code, message, transient)], clock.GetUtcNow()) with { SchemaVersion = "2.0" }, compact, token);
        return exitCode;
    }
}
