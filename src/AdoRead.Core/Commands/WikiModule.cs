using System.CommandLine;
using AdoRead.Output;

namespace AdoRead.Commands;

/// <summary>Wiki reading and search: list, pages, page and search.</summary>
public static class WikiModule
{
    public static CommandModule Definition { get; } = new(
        "wiki",
        "Read and search Azure DevOps Wiki pages.",
        Configure);

    public static IReadOnlyDictionary<Command, string> Configure(
        Command target,
        ToolCommandNames names,
        CommandServices services) =>
        WikiCatalogCommands.Configure(target, names, services)
            .Concat(WikiPageCommand.Configure(target, names, services))
            .Concat(WikiSearchCommand.Configure(target, names, services))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
}
