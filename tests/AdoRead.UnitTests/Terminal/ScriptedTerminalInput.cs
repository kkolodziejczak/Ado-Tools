using AdoRead.Terminal;

namespace AdoRead.UnitTests.Terminal;

/// <summary>Records every prompt and answers with a scripted line, so no test touches a real console.</summary>
internal sealed class ScriptedTerminalInput(bool isInputRedirected, string? answer) : ITerminalInput
{
    public List<string> Prompts { get; } = [];

    public bool IsInputRedirected => isInputRedirected;

    public string ReadMaskedLine(string prompt)
    {
        Prompts.Add(prompt);
        return answer ?? throw new InvalidOperationException("No answer was scripted for the prompt.");
    }
}
