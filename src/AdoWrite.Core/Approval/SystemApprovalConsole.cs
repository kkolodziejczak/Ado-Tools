using System.Text;

namespace AdoWrite.Approval;

/// <summary>The real console. Keys come only from it: with redirected input it is not interactive.</summary>
public sealed class SystemApprovalConsole : IApprovalConsole
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    public void DiscardTypedKeys()
    {
        while (Console.KeyAvailable)
        {
            Console.ReadKey(intercept: true);
        }
    }

    public void Draw(IReadOnlyList<ScreenLine> lines)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "ado-write approval";
        Console.Clear();
        foreach (ScreenLine line in lines)
        {
            if (line.Tone != ScreenTone.Normal)
            {
                Console.ForegroundColor = line.Tone switch
                {
                    ScreenTone.Dim => ConsoleColor.DarkGray,
                    ScreenTone.Warning => ConsoleColor.Yellow,
                    ScreenTone.Posted => ConsoleColor.Green,
                    _ => ConsoleColor.Red
                };
            }

            Console.Write(line.Text);
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
