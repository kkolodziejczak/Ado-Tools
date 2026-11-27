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
        if (await ConfiguredOrganizationAsync(request, cancellationToken) is { } organization && organization != request.Organization)
        {
            return Error(
                "organizationChanged",
                $"The configuration now points to {organization}, but this write was requested for {request.Organization}. Nothing was posted.");
        }

        using var output = new StringWriter();
        CommandApplication application = new ToolComposition(
                configurationStore,
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

    /// <summary>The organization the write's configuration names now; null when it cannot be read, which the command itself reports.</summary>
    private async Task<Uri?> ConfiguredOrganizationAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return (await configurationStore.LoadAsync(request.Option("--config"), cancellationToken)).OrganizationUrl;
        }
        catch (ConfigurationException)
        {
            return null;
        }
    }

    internal static JsonElement Error(string code, string message) =>
        JsonSerializer.SerializeToElement(new { status = "error", errors = new[] { new { code, message } } });
}
