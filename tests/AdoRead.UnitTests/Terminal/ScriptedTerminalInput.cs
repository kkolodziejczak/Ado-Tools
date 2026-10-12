using AdoRead.Terminal;

namespace AdoRead.UnitTests.Terminal;

/// <summary>
/// Records every prompt and answers with scripted input, so no test touches a real console. The masked
/// answer serves <c>ReadMaskedLine</c>; the queued lines serve <c>ReadLine</c> in order.
/// </summary>
internal sealed class ScriptedTerminalInput(bool isInputRedirected, string? answer, params string[] lines) : ITerminalInput
{
    private readonly Queue<string> _lines = new(lines);

    public List<string> Prompts { get; } = [];

    public List<string> LinePrompts { get; } = [];

    public bool IsInputRedirected => isInputRedirected;

    public string ReadMaskedLine(string prompt)
    {
        Prompts.Add(prompt);
        return answer ?? throw new InvalidOperationException("No answer was scripted for the prompt.");
    }

    public string ReadLine(string prompt)
    {
        LinePrompts.Add(prompt);
        return _lines.Count > 0 ? _lines.Dequeue() : throw new InvalidOperationException("No line was scripted for the prompt.");
    }
}
