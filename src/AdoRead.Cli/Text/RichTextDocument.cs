namespace AdoRead.Text;

public sealed record RichTextDocument(
    string? Raw,
    string? Format,
    string? PlainText);
