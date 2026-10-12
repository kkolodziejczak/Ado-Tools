using System.Text;

namespace AdoRead.Terminal;

public sealed class ConsoleTerminalInput : ITerminalInput
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public string ReadLine(string prompt)
    {
        Console.Error.Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }

    public string ReadMaskedLine(string prompt)
    {
        Console.Error.Write(prompt);
        var line = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return line.ToString();
            }

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
            {
                Console.Error.WriteLine();
                throw new OperationCanceledException();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (line.Length > 0)
                {
                    line.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                line.Append(key.KeyChar);
            }
        }
    }
}
