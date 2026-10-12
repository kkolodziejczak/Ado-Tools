namespace AdoRead.Terminal;

/// <summary>Reads a secret typed by a person at an interactive terminal.</summary>
public interface ITerminalInput
{
    bool IsInputRedirected { get; }

    /// <summary>Writes <paramref name="prompt"/> to the error stream, then reads one line without echoing it.</summary>
    string ReadMaskedLine(string prompt);
}
