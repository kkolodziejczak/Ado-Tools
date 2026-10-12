namespace AdoRead.Privacy;

/// <summary>A privacy rule refused the request. Reported under the "privacy" scope and never returns content.</summary>
public sealed class PrivacyException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
