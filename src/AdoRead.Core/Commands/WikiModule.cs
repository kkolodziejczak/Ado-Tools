using System.CommandLine;
using AdoRead.Output;

namespace AdoRead.Commands;

/// <summary>Wiki reading and search: <c>list</c> for the Wikis, and <c>page list|show|search</c> for their pages.</summary>
public static class WikiModule
{
    public static CommandModule Definition { get; } = new(
        "wiki",
        "Read and search Azure DevOps Wiki pages.",
        Configure);

    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services)
    {
        var page = new Command("page", "Wiki pages: list one Wiki's pages, read one as Markdown, or search their text.");
        target.Subcommands.Add(page);
        return WikiCatalogCommands.Configure(target, page, names, services)
            .Concat(WikiPageCommand.Configure(page, names, services))
            .Concat(WikiSearchCommand.Configure(page, names, services))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}
