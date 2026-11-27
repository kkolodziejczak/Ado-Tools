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
/// Runs a queued write in this process, through the same commands an agent calls, with approval
/// granted: the approval window's only way to post. Returns the command's JSON envelope.
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
}
