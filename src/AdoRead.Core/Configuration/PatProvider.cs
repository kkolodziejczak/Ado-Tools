namespace AdoRead.Configuration;

public sealed class PatProvider : IPatProvider
{
    public const string EnvironmentVariableName = "ADO_READ_PAT";

    private readonly Func<string, string?> _getEnvironmentVariable;

    public PatProvider(Func<string, string?>? getEnvironmentVariable = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string GetRequiredPat()
    {
        string? pat = _getEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new CredentialException(
                "missingPat",
                $"Environment variable {EnvironmentVariableName} is required.");
        }

        return pat;
    }
}
