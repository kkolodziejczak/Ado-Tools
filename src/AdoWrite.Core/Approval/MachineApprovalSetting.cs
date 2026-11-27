namespace AdoWrite.Approval;

/// <summary>
/// The machine-wide switch that turns approval off. Only an administrator can change it: on Windows
/// the machine-level environment variable, read from the machine scope so a value set for one process
/// or for the user is ignored; elsewhere a file under /etc.
/// </summary>
public static class MachineApprovalSetting
{
    public const string VariableName = "ADO_WRITE_APPROVAL";
    public const string UnixFilePath = "/etc/ado-tools/approval";

    public static bool IsOff(string? value) => string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    public static string? Read()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable(VariableName, EnvironmentVariableTarget.Machine);
        }

        try
        {
            return File.Exists(UnixFilePath) ? File.ReadAllText(UnixFilePath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
