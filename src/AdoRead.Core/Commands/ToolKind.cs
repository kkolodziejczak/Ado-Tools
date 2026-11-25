namespace AdoRead.Commands;

/// <summary>
/// Which commands a tool shares. A write tool returns no Azure DevOps content, so it has no privacy or
/// discovery commands and asks for no privacy level at login. Both kinds use the same token.
/// </summary>
public enum ToolKind
{
    Read,
    Write
}
