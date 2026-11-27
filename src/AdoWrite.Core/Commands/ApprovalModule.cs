using System.CommandLine;
using AdoRead.Commands;
using AdoRead.Output;
using AdoWrite.Approval;
using AdoWrite.AzureDevOps;

namespace AdoWrite.Commands;

/// <summary>
/// <c>approval open &lt;id&gt;</c> is the approval window of one write: the person the token belongs to
/// reads its dry run and presses Enter to post it. A write opens its window by itself; the command is
/// public so the window can run it. It runs only in a console a person types into, so an agent that runs
/// it with redirected input cannot approve anything.
/// </summary>
public static class ApprovalModule
{
    public const string OpenKind = "approvalOpen";

    public static CommandModule Definition { get; } = Create(PendingWrites.ForCurrentUser(), new SystemApprovalConsole(), new AdoWriteSessionFactory());

    public static CommandModule Create(PendingWrites pending, IApprovalConsole console, IAdoWriteSessionFactory sessionFactory) => new(
        "approval",
        "Approve a write an agent asked for, in a window only you type into.",
        (target, _, services) =>
        {
            var id = new Argument<string>("id") { Description = "The approvalId the write returned." };
            var open = new Command("open", "Open the approval window of one write: read its dry run, then press Enter to post it or any other key to cancel.") { id };
            open.SetAction(async (parseResult, cancellationToken) =>
            {
                if (!console.IsInteractive)
                {
                    await services.JsonOutput.WriteAsync(
                        SearchEnvelopeFactory.Error(
                            OpenKind,
                            [new SearchError("command", null, "consoleRequired", "The approval window needs a console a person types into; its input and output must not be redirected.", false)],
                            services.TimeProvider.GetUtcNow()),
                        parseResult.GetValue(services.CompactOption),
                        cancellationToken);
                    return ExitCodes.InvalidInputOrConfiguration;
                }

                var runner = new ApprovalRunner(services.ConfigurationStore, sessionFactory, services.CredentialVault, services.TokenOwners, services.TimeProvider);
                await new ApprovalPrompt(pending, runner, console).RunAsync(parseResult.GetRequiredValue(id), cancellationToken);
                return ExitCodes.Success;
            });
            target.Subcommands.Add(open);
            return new Dictionary<Command, string> { [open] = OpenKind };
        });
}
