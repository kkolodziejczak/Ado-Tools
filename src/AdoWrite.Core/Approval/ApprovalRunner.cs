using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.AzureDevOps;
using AdoWrite.Commands;

namespace AdoWrite.Approval;

/// <summary>
/// Runs a pending write in this process, through the same commands an agent calls, with approval
/// granted: the approval window's only way to post. Returns the command's JSON envelope. A write runs
/// only against the organization it was saved for.
/// </summary>
public sealed class ApprovalRunner(
    IConfigurationStore configurationStore,
    IAdoWriteSessionFactory sessionFactory,
    ICredentialVault? credentialVault = null,
    ITokenOwnerLookup? tokenOwners = null,
    TimeProvider? timeProvider = null)
{
    public async Task<JsonElement> RunAsync(ApprovalRequest request, bool preview, CancellationToken cancellationToken)
    {
        AdoReadConfiguration configuration;
        try
        {
            configuration = await configurationStore.LoadAsync(request.Option("--config"), cancellationToken);
        }
        catch (ConfigurationException exception)
        {
            return Error(exception.Code, exception.Message);
        }

        if (configuration.OrganizationUrl != request.Organization)
        {
            return Error(
                "organizationChanged",
                $"The configuration now points to {configuration.OrganizationUrl}, but this write was requested for {request.Organization}. Nothing was posted.");
        }

        using var output = new StringWriter();
        CommandApplication application = new ToolComposition(
                // The command uses the configuration checked here, so a change on disk can't redirect it.
                new CheckedConfiguration(configuration, configurationStore.DefaultPath),
                new JsonOutput(output),
                timeProvider ?? TimeProvider.System,
                credentialVault: credentialVault,
                toolKind: ToolKind.Write,
                tokenOwners: tokenOwners)
            .CreateUmbrella(
                "ado-write",
                [
                    PullRequestWriteModule.Create(sessionFactory, WriteApproval.Granted),
                    WorkItemCommentModule.Create(sessionFactory, WriteApproval.Granted)
                ]);
        string[] arguments = [.. request.Command, .. request.Arguments, .. preview ? ["--dry-run"] : Array.Empty<string>(), "--compact"];
        await application.InvokeAsync(arguments, cancellationToken);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        return document.RootElement.Clone();
    }

    internal static JsonElement Error(string code, string message) =>
        JsonSerializer.SerializeToElement(new { status = "error", errors = new[] { new { code, message } } });

    /// <summary>One configuration, already loaded and checked against the write's organization.</summary>
    private sealed class CheckedConfiguration(AdoReadConfiguration configuration, string defaultPath) : IConfigurationStore
    {
        public string DefaultPath => defaultPath;

        public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) => Task.FromResult(configuration);

        public Task SaveAsync(string? overridePath, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The approval window never changes the configuration.");
    }
}
