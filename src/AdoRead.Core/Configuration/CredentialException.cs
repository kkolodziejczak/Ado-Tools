namespace AdoRead.Configuration;

public sealed class CredentialException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
