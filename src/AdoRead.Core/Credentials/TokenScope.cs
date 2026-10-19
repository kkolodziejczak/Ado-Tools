namespace AdoRead.Credentials;

/// <summary>
/// Which token a tool uses. Read tools and write tools keep separate tokens in the vault, so a token
/// stored for reading never gains write scopes and a read tool never sees the write token.
/// </summary>
public enum TokenScope
{
    Read,
    Write
}
