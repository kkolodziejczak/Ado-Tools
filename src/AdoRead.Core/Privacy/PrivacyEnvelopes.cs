using AdoRead.Output;

namespace AdoRead.Privacy;

/// <summary>Adds the privacy section to a read envelope and filters its results. Error envelopes stay as they are.</summary>
public static class PrivacyEnvelopes
{
    public static SearchEnvelope<T> Filter<T>(
        SearchEnvelope<T> envelope,
        EffectivePrivacy privacy,
        IReadOnlyList<PrivacyCategory> relevant,
        Func<T, T> filter) =>
        envelope.Status == "error"
            ? envelope
            : Describe(envelope with { Results = envelope.Results.Select(filter).ToArray() }, privacy, relevant);

    public static SearchEnvelope<T> Describe<T>(
        SearchEnvelope<T> envelope,
        EffectivePrivacy privacy,
        IReadOnlyList<PrivacyCategory> relevant) =>
        envelope.Status == "error"
            ? envelope
            : envelope with { Privacy = privacy.Section(relevant) };
}
