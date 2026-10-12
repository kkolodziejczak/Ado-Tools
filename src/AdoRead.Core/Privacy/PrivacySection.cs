namespace AdoRead.Privacy;

/// <summary>
/// Describes what a read command withheld. Lists only the categories relevant to that command's data.
/// </summary>
public sealed record PrivacySection(
    string Level,
    IReadOnlyList<string> Withheld,
    IReadOnlyList<string> Aliased,
    bool RestrictedByCall);
