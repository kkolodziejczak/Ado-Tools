using System.CommandLine;
using AdoRead.AzureDevOps;
using AdoRead.Commands.Shared;
using AdoRead.Configuration;
using AdoRead.Privacy;
using AdoRead.Output;
using AdoRead.Wikis;

namespace AdoRead.Commands;

public static class WikiSearchCommand
{
    public static IReadOnlyDictionary<Command, string> Configure(Command target, ToolCommandNames names, CommandServices services)
    {
        var (store, pat, sessions, output, clock, compact, _, _, gate, _) = services;
        var privacyOptions = new PrivacyRequestOptions();
        var text = new Argument<string>("text");
        var project = new Option<string?>("--project");
        var allProjects = new Option<bool>("--all-projects");
        var wiki = new Option<string?>("--wiki");
        var skip = new Option<int?>("--skip");
        var top = new Option<int?>("--top");
        var config = new Option<string?>("--config");
        var command = new Command("search", "Search Azure DevOps Wiki text.");
        command.Arguments.Add(text);
        foreach (Option option in new Option[] { project, allProjects, wiki, skip, top, config }) command.Options.Add(option);
        privacyOptions.AddTo(command);
        command.SetAction((result, token) => RunAsync(names, result.GetValue(text) ?? string.Empty, result.GetValue(project), result.GetValue(allProjects), result.GetValue(wiki), result.GetValue(skip), result.GetValue(top), result.GetValue(config), result.GetValue(compact), privacyOptions.Read(result), gate, store, pat, sessions, output, clock, token));
        target.Subcommands.Add(command);
        return new Dictionary<Command, string> { [command] = "wikiFullTextSearch" };
    }

    private static async Task<int> RunAsync(ToolCommandNames names, string text, string? project, bool allProjects, string? wiki, int? skip, int? top, string? config, bool compact, PrivacyRequest privacyRequest, PrivacyGate gate, IConfigurationStore store, IPatProvider pat, IAdoSessionFactory sessions, IJsonOutput output, TimeProvider clock, CancellationToken token)
    {
        if (allProjects && project is not null) return await WriteError("command", "invalidWikiSearch", "The Wiki search request is invalid.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        if (project is not null && string.IsNullOrWhiteSpace(project)) return await WriteError("command", "projectRequired", "The explicit project cannot be blank.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        project = project?.Trim();
        if (project is not null && !WikiIdentifier.IsSafeProject(project)) return await WriteError("command", "invalidProject", "The Azure DevOps project identifier is invalid.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        if (skip is < 0) return await WriteError("command", "invalidSkip", "The Wiki search offset cannot be negative.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        if ((top ?? 10) is < 1 or > 25) return await WriteError("command", "invalidTop", "The Wiki search result limit must be between 1 and 25.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        if (string.IsNullOrWhiteSpace(text)) return await WriteError("command", "invalidWikiSearch", "The Wiki search request is invalid.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        if (!string.IsNullOrWhiteSpace(wiki)) return await WriteError("command", "unsupportedWikiFilter", "Azure DevOps does not document a Wiki-name search filter.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
        try
        {
            AdoReadConfiguration configuration = await store.LoadAsync(config, token);
            EffectivePrivacy privacy = gate.Require(configuration.OrganizationUrl, privacyRequest);
            string? selectedProject = allProjects ? null : project ?? configuration.DefaultProject;
            if (selectedProject is not null && !WikiIdentifier.IsSafeProject(selectedProject)) return await WriteError("configuration", "invalidProject", "The configured Azure DevOps project identifier is invalid.", false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(configuration.RequestTimeout);
            await using IAdoSession session = sessions.Create(configuration.OrganizationUrl, pat.GetRequiredPat(configuration.OrganizationUrl));
            SearchEnvelope<WikiSearchHitDocument> envelope = await new WikiSearchReader(session.Wikis, clock, names, privacy).ReadAsync(text, selectedProject, null, skip ?? 0, top ?? 10, timeout.Token);
            await output.WriteAsync(envelope, compact, token);
            return ExitCodes.FromStatus(envelope.Status);
        }
        catch (ConfigurationException exception) { return await WriteError("configuration", exception.Code, exception.Message, false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token); }
        catch (PrivacyException exception) { return await WriteError("privacy", exception.Code, exception.Message, false, ExitCodes.InvalidInputOrConfiguration, output, clock, compact, token); }
        catch (CredentialException exception) { return await WriteError("authentication", exception.Code, exception.Message, false, ExitCodes.Error, output, clock, compact, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return await WriteError("azureDevOps", "timeout", "The Azure DevOps request timed out.", true, ExitCodes.Error, output, clock, compact, CancellationToken.None); }
    }

    private static async Task<int> WriteError(string scope, string code, string message, bool transient, int exitCode, IJsonOutput output, TimeProvider clock, bool compact, CancellationToken token)
    {
        await output.WriteAsync(SearchEnvelopeFactory.Error("wikiFullTextSearch", [new SearchError(scope, null, code, message, transient)], clock.GetUtcNow()) with { SchemaVersion = "2.0" }, compact, token);
        return exitCode;
    }
}
