using System.CommandLine;
using AdoRead.Commands;
using AdoRead.Output;
using AdoWrite.Approval;
using AdoWrite.AzureDevOps;

namespace AdoWrite.Commands;

/// <summary>
/// <c>approval review</c> opens the approval window, where the person the token belongs to posts or
/// rejects the writes agents queued. It runs only in a console a person types into, so an agent that
/// runs it with redirected input cannot approve anything.
/// </summary>
public static class ApprovalModule
{
    public const string ReviewKind = "approvalReview";

    public static CommandModule Definition { get; } = Create(ApprovalOutbox.ForCurrentUser(), new SystemApprovalConsole(), new AdoWriteSessionFactory());

    public static CommandModule Create(ApprovalOutbox outbox, IApprovalConsole console, IAdoWriteSessionFactory sessionFactory) => new(
        "approval",
        "Post or reject the writes agents queued, in a window only you type into.",
        (target, _, services) =>
        {
            var review = new Command("review", "Open the approval window: see each queued write as it will be posted, then post or reject it.");
            review.SetAction(async (parseResult, cancellationToken) =>
            {
                if (!console.IsInteractive)
                {
                    return await RefuseAsync(
                        "consoleRequired",
                        "The approval window needs a console a person types into; its input and output must not be redirected.");
                }

                using FileStream? window = ConsoleApprovalWindow.TryHold(outbox.Directory);
                if (window is null)
                {
                    return await RefuseAsync("approvalWindowOpen", "An approval window is already open.");
                }

                var runner = new ApprovalRunner(services.ConfigurationStore, sessionFactory, services.CredentialVault, services.TokenOwners, services.TimeProvider);
                await new ApprovalReview(outbox, runner, console, TimeZoneInfo.Local).RunAsync(cancellationToken);
                return ExitCodes.Success;

                async Task<int> RefuseAsync(string code, string message)
                {
                    await services.JsonOutput.WriteAsync(
                        SearchEnvelopeFactory.Error(ReviewKind, [new SearchError("command", null, code, message, false)], services.TimeProvider.GetUtcNow()),
                        parseResult.GetValue(services.CompactOption),
                        cancellationToken);
                    return ExitCodes.InvalidInputOrConfiguration;
                }
            });
            target.Subcommands.Add(review);
            return new Dictionary<Command, string> { [review] = ReviewKind };
        });
}
