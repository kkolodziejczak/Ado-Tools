using System.CommandLine;
using AdoRead.Commands;
using AdoWrite.AzureDevOps;
using AdoWrite.Comments;

namespace AdoWrite.Commands;

/// <summary>Work-item discussion: <c>comment</c> adds one Markdown comment.</summary>
public static class WorkItemCommentModule
{
    public static CommandModule Definition { get; } = Create(new AdoWriteSessionFactory());

    public static CommandModule Create(IAdoWriteSessionFactory sessionFactory) => new(
        "work-item",
        "Comment in a work item's discussion.",
        (target, _, services) => Configure(target, new CommentCommandContext(services, sessionFactory)));

    private static IReadOnlyDictionary<Command, string> Configure(Command target, CommentCommandContext context)
    {
        var reference = new Argument<string>("id-or-url")
        {
            Description = "Positive work-item ID or Azure DevOps work-item URL."
        };
        var options = new CommentOptions();
        var command = new Command("comment", "Add a comment to a work item's discussion.");
        command.Arguments.Add(reference);
        options.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => context.RunAsync(
            CommentKinds.WorkItemComment,
            parseResult,
            options,
            () =>
            {
                AdoReference workItem = AdoReferenceParser.ParseWorkItem(parseResult.GetValue(reference))
                    ?? throw WriteException.Rejected(
                        "invalidWorkItemId",
                        "The work-item reference must be a positive integer or an Azure DevOps work-item URL.");
                return new CommentTarget(
                    workItem,
                    new CommentQuery(workItem.Id),
                    (session, organizationUrl, draft, token) =>
                        new WorkItemCommenter(session.WorkItems, session.People, organizationUrl)
                            .CommentAsync(workItem.Id, draft, token));
            },
            cancellationToken));
        target.Subcommands.Add(command);
        return new Dictionary<Command, string> { [command] = CommentKinds.WorkItemComment };
    }
}
