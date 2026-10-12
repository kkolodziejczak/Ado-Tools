namespace AdoRead.Output;

public sealed record CommandHintDocument(
    string Command,
    IReadOnlyList<string> Arguments);
