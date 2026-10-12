namespace AdoRead.Configuration;

public sealed class ConfigurationException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
