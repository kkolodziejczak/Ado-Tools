namespace AdoRead.IntegrationTests;

/// <summary>A fact that runs only on Windows and is reported as skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows Credential Manager is only available on Windows.";
        }
    }
}
