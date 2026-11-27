using System.Text;

namespace AdoWrite.Approval;

/// <summary>The real console. Keys come only from it: with redirected input it is not interactive.</summary>
public sealed class SystemApprovalConsole : IApprovalConsole
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public ConsoleKeyInfo? ReadKey(TimeSpan wait)
    {
        DateTime deadline = DateTime.UtcNow + wait;
        while (!Console.KeyAvailable)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            Thread.Sleep(PollInterval);
        }

        return Console.ReadKey(intercept: true);
    }

    public void Draw(IReadOnlyList<ScreenLine> lines)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "ado-write approval";
        Console.Clear();
        foreach (ScreenLine line in lines)
        {
            (Console.ForegroundColor, Console.BackgroundColor) = line.Tone switch
            {
                ScreenTone.Dim => (ConsoleColor.DarkGray, ConsoleColor.Black),
                ScreenTone.Current => (ConsoleColor.Black, ConsoleColor.DarkYellow),
                ScreenTone.Posted => (ConsoleColor.Green, ConsoleColor.Black),
                ScreenTone.Failed => (ConsoleColor.Red, ConsoleColor.Black),
                _ => (ConsoleColor.Gray, ConsoleColor.Black)
            };
            Console.Write(line.Text);
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
