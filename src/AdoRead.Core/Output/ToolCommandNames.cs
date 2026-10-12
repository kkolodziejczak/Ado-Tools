namespace AdoRead.Output;

/// <summary>
/// Spells command paths the way a person or agent types them. <see cref="Group"/> is the module
/// group ("wiki", "sprints") in the umbrella tool and null in a slice tool, where module commands
/// sit directly under the executable.
/// </summary>
public sealed record ToolCommandNames(string Executable, string? Group)
{
    public ToolCommandNames Root => this with { Group = null };

    /// <summary>Path relative to the executable, as hint documents carry it: "wiki page" or "page".</summary>
    public string Path(string command) => Group is null ? command : $"{Group} {command}";

    /// <summary>Full invocation for help text: "ado-read wiki page" or "ado-read-wiki page".</summary>
    public string Invocation(string command) => $"{Executable} {Path(command)}";
}
