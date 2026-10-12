using AdoRead.Configuration;

namespace AdoRead.Output;

public sealed record SearchError(
    string Scope,
    string? Project,
    string Code,
    string Message,
    bool Transient,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CommandHintDocument? Next = null)
{
    public static SearchError FromException(Exception exception) => exception switch
    {
        ConfigurationException configuration =>
            new("configuration", null, configuration.Code, configuration.Message, false),
        CredentialException credential =>
            new("authentication", null, credential.Code, credential.Message, false),
        OperationCanceledException =>
            new("command", null, "cancelled", "The command was cancelled.", true),
        _ => new(
            "command",
            null,
            "unexpectedError",
            "The command failed unexpectedly.",
            false)
    };
}
