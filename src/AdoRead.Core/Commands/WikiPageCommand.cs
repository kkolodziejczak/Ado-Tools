using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.Commands;

public static class WikiPageCommand
{
    public static IReadOnlyDictionary<Command, string> Configure(Command target, ToolCommandNames names, CommandServices services)
    {
        var (store, pat, sessions, output, clock, compact, _, _) = services;
        var reference = new Argument<string?>("url") { Arity = ArgumentArity.ZeroOrOne };
        var project = new Option<string?>("--project");
        var wiki = new Option<string?>("--wiki");
        var path = new Option<string?>("--path");
        var pageId = new Option<int?>("--page-id");
        var gitPath = new Option<string?>("--git-path");
        var version = new Option<string?>("--version");
        var versionType = new Option<string?>("--version-type");
        var config = new Option<string?>("--config");
        var command = new Command("page", "Read one Azure DevOps Wiki page as Markdown.");
        command.Arguments.Add(reference);
        foreach (Option option in new Option[] { project, wiki, path, pageId, gitPath, version, versionType, config }) command.Options.Add(option);
        command.SetAction((result, token) => ReadAsync(names, result.GetValue(reference), result.GetValue(project), result.GetValue(wiki), result.GetValue(path), result.GetValue(pageId), result.GetValue(gitPath), result.GetValue(version), result.GetValue(versionType), result.GetValue(config), result.GetValue(compact), store, pat, sessions, output, clock, token));
        target.Subcommands.Add(command);
        return new Dictionary<Command, string> { [command] = "wikiPageLookup" };
    }

    private static async Task<int> ReadAsync(ToolCommandNames names, string? url, string? project, string? wiki, string? path, int? pageId, string? gitPath, string? version, string? versionType, string? configPath, bool compact, IConfigurationStore store, IPatProvider pat, IAdoSessionFactory sessions, IJsonOutput output, TimeProvider clock, CancellationToken token)
    {
        bool hasExplicitValues = project is not null || wiki is not null || path is not null || pageId is not null || gitPath is not null || version is not null || versionType is not null;
        bool hasPageSelector = path is not null || pageId is not null || gitPath is not null;
        WikiReference? reference = null;
        if (project is not null && string.IsNullOrWhiteSpace(project)) return await WriteError("command", "projectRequired", "The explicit project cannot be blank.", ExitCodes.InvalidInputOrConfiguration);
        project = project?.Trim();
        wiki = wiki?.Trim();
        if (project is not null && !WikiIdentifier.IsSafeProject(project)) return await WriteError("command", "invalidProject", "The Azure DevOps project identifier is invalid.", ExitCodes.InvalidInputOrConfiguration);
        if (url is not null)
        {
            reference = hasExplicitValues ? null : WikiReferenceParser.ParseUrl(url);
            if (reference is null) return await WriteError("command", "invalidWikiReference", "The Wiki reference must be a supported Wiki URL or one valid selector.", ExitCodes.InvalidInputOrConfiguration);
        }
        else if (hasPageSelector && !WikiReferenceParser.HasValidSelector(wiki, path, pageId, gitPath, version, versionType)) return await WriteError("command", "invalidWikiReference", "The Wiki reference must be a supported Wiki URL or one valid selector.", ExitCodes.InvalidInputOrConfiguration);
        try
        {
            AdoReadConfiguration configuration = await store.LoadAsync(configPath, token);
            if (url is null)
            {
                string? selectedProject = project ?? configuration.DefaultProject;
                if (!hasPageSelector)
                {
                    CommandHintDocument next = string.IsNullOrWhiteSpace(selectedProject) ? new(names.Root.Path("projects"), []) : new(names.Path("list"), ["--project", selectedProject]);
                    return await WriteError("command", "wikiReferenceRequired", "A Wiki page reference is required.", ExitCodes.InvalidInputOrConfiguration, next);
                }
                if (string.IsNullOrWhiteSpace(selectedProject)) return await WriteError("configuration", "projectRequired", "No project given and none configured.", ExitCodes.InvalidInputOrConfiguration, new CommandHintDocument(names.Root.Path("projects"), []));
                if (!WikiIdentifier.IsSafeProject(selectedProject)) return await WriteError("configuration", "invalidProject", "The Azure DevOps project identifier is invalid.", ExitCodes.InvalidInputOrConfiguration);
                reference = WikiReferenceParser.Create(selectedProject, wiki, path, pageId, gitPath, version, versionType);
                if (reference is null) return await WriteError("command", "invalidWikiReference", "The Wiki reference must be a supported Wiki URL or one valid selector.", ExitCodes.InvalidInputOrConfiguration);
            }
            if (reference is null) return await WriteError("command", "invalidWikiReference", "The Wiki reference must be a supported Wiki URL or one valid selector.", ExitCodes.InvalidInputOrConfiguration);
            if (!string.IsNullOrEmpty(reference.Organization) && !reference.IsFor(configuration.OrganizationUrl)) return await WriteError("command", "organizationMismatch", "The Wiki URL belongs to a different organization than the configured organization.", ExitCodes.InvalidInputOrConfiguration);
            string credential = pat.GetRequiredPat(configuration.OrganizationUrl);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessions.Create(configuration.OrganizationUrl, credential);
            if (reference.Selector is WikiPathSelector && reference.Version is { Type: null } selectedVersion) reference = reference with { Version = await ResolveVersionAsync(session.Wikis, reference.Project, reference.Wiki, selectedVersion.Value, timeout.Token) };
            reference = reference with { Organization = Uri.UnescapeDataString(configuration.OrganizationUrl.AbsolutePath.Trim('/')) };
            SearchEnvelope<WikiPageDocument> envelope = await new WikiPageReader(session.Wikis, configuration.OrganizationUrl, clock).ReadAsync(reference, timeout.Token);
            await output.WriteAsync(envelope, compact, token);
            return ExitCodes.FromStatus(envelope.Status);
        }
        catch (ConfigurationException exception) { return await WriteError("configuration", exception.Code, exception.Message, ExitCodes.InvalidInputOrConfiguration); }
        catch (CredentialException exception) { return await WriteError("authentication", exception.Code, exception.Message, ExitCodes.Error); }
        catch (WikiSourceException exception) { return await WriteError("azureDevOps", exception.Code, exception.Message, ExitCodes.Error, transient: exception.Transient); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return await WriteError("azureDevOps", "timeout", "The Azure DevOps request timed out.", ExitCodes.Error, transient: true, cancellation: CancellationToken.None); }
        async Task<int> WriteError(string scope, string code, string message, int exit, CommandHintDocument? next = null, bool transient = false, CancellationToken cancellation = default) { await output.WriteAsync(SearchEnvelopeFactory.Error("wikiPageLookup", [new SearchError(scope, null, code, message, transient, next)], clock.GetUtcNow()) with { SchemaVersion = "2.0" }, compact, cancellation == default ? token : cancellation); return exit; }
    }

    private static async Task<WikiVersionDocument> ResolveVersionAsync(IWikiSource source, string project, string wiki, string version, CancellationToken cancellationToken)
    {
        SourceWiki[] wikis = (await source.GetWikisAsync(project, cancellationToken)).Where(candidate => string.Equals(candidate.Id.ToString(), wiki, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.Name, wiki, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (wikis.Length != 1) throw new ConfigurationException("wikiRequired", "The selected Wiki must resolve to exactly one Wiki.");
        return WikiVersionResolver.Resolve(wikis[0].Versions, version, null) ?? throw new ConfigurationException("invalidWikiReference", "The Wiki version must match exactly one advertised version.");
    }
}
