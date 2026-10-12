namespace AdoRead.Privacy;

/// <summary>
/// The per-call tightening passed with --privacy and --withhold. It can only restrict the stored
/// policy; <see cref="PrivacyGate"/> intersects it with the stored policy.
/// </summary>
public sealed record PrivacyRequest(string? Level, IReadOnlyList<string> Withheld)
{
    public PrivacyPolicy ToPolicy() =>
        PrivacyPolicy.FromNames(
            Level ?? PrivacyLevels.NameOf(PrivacyLevel.Full),
            [],
            Withheld);
}
