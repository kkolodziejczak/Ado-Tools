namespace AdoRead.AzureDevOps;

/// <summary>
/// What to tell a person when Azure DevOps refuses the token. Azure DevOps does not say which scope is
/// missing, so the hint names the scopes the failing command needs and how to replace the token that
/// every Ado-Tools tool shares.
/// </summary>
internal static class TokenScopeHint
{
    public static string For(string scopes, string loginCommand = "auth login") =>
        $" This command needs {scopes} on the token. Every Ado-Tools tool uses the same token per organization: if it lacks a scope or has expired, create one that includes these and store it with `{loginCommand}`.";

    /// <summary>Appends the hint to an unauthorized or forbidden failure and leaves every other failure as it is.</summary>
    public static string AppendTo(string code, string message, string scopes, string loginCommand = "auth login") =>
        code is "unauthorized" or "forbidden" ? message + For(scopes, loginCommand) : message;
}
