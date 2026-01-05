using System.Text;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.UI;

public static class ConsoleHelpers
{
    // Small "terminal feel" delay before each printed line (ms).
    public static int LineDelayMs { get; set; } = 25;

    // Adds a bit of randomness so it feels less robotic.
    public static int LineJitterMs { get; set; } = 20;

    private static readonly Random Rng = new();

    public static void WriteHeader(string text)
    {
        Console.OutputEncoding = Encoding.UTF8;

        WithColor(ConsoleColor.Cyan, () =>
        {
            Console.WriteLine(text);
            Console.WriteLine();
            Console.WriteLine("Developed by Avazbek22");
            Console.WriteLine(new string('=', Math.Max(10, text.Length)));
        });

        Console.WriteLine();
    }

    public static void WriteMiniHeader(string text)
    {
        Console.OutputEncoding = Encoding.UTF8;

        WithColor(ConsoleColor.Cyan, () =>
        {
            Console.WriteLine(text);
            Console.WriteLine(new string('=', Math.Max(10, text.Length)));
        });

        Console.WriteLine();
    }

    public static void WriteSuccess(string text) => WithColor(ConsoleColor.Green, () => Console.WriteLine(text));
    public static void WriteWarning(string text) => WithColor(ConsoleColor.Yellow, () => Console.WriteLine(text));
    public static void WriteError(string text) => WithColor(ConsoleColor.Red, () => Console.WriteLine(text));
    public static void WriteInfo(string text) => WithColor(ConsoleColor.Gray, () => Console.WriteLine(text));
    public static void WriteHint(string text) => WithColor(ConsoleColor.DarkGray, () => Console.WriteLine(text));

    public static void WithColor(ConsoleColor color, Action action)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try { action(); }
        finally { Console.ForegroundColor = old; }
    }

    public static void WriteSection(string title)
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine($"=== {title} ==="));
        Console.WriteLine();
    }

    public static void WriteBullets(string title, IEnumerable<string> bullets)
    {
        WriteSection(title);
        foreach (var b in bullets)
            Console.WriteLine($" - {b}");
        Console.WriteLine();
    }

    public static void Pause(string message = "Press any key to continue...")
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine(message));
        Console.ReadKey(true);
    }

    public static string ReadMenuChoice()
    {
        WithColor(ConsoleColor.Gray, () => Console.Write("Select: "));
        return (Console.ReadLine() ?? "").Trim();
    }

    /// <summary>
    /// Confirm screen: Enter -> continue, Esc -> cancel.
    /// </summary>
    public static bool ConfirmEnterOrEscape(string message = "Press Enter to continue or Esc to cancel...")
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine(message));

        while (true)
        {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Enter) return true;
            if (key == ConsoleKey.Escape) return false;
        }
    }

    public static void PrintOperationLinesAnimated(IEnumerable<OperationLine> lines)
    {
        foreach (var line in lines)
        {
            DelayLine();

            var color = LevelToColor(line.Level);
            WithColor(color, () => Console.WriteLine(line.Text));
        }
    }

    public static void PrintLinesAnimated(IEnumerable<string> lines, ConsoleColor color = ConsoleColor.Gray)
    {
        foreach (var line in lines)
        {
            DelayLine();
            WithColor(color, () => Console.WriteLine(line));
        }
    }

    // IMPORTANT: default valueColor is WHITE (as you asked)
    public static void PrintKeyValue(string key, string value, ConsoleColor keyColor = ConsoleColor.DarkGray, ConsoleColor valueColor = ConsoleColor.White)
    {
        WithColor(keyColor, () => Console.Write($"{key}: "));
        WithColor(valueColor, () => Console.WriteLine(value));
    }

    private static ConsoleColor LevelToColor(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return ConsoleColor.Gray;

        return level.Trim().ToUpperInvariant() switch
        {
            "OK" => ConsoleColor.Green,
            "INFO" => ConsoleColor.Gray,
            "WARN" => ConsoleColor.Yellow,
            "ERR" => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }

    private static void DelayLine()
    {
        var baseDelay = LineDelayMs;
        var jitter = LineJitterMs <= 0 ? 0 : Rng.Next(0, LineJitterMs + 1);
        var total = Math.Clamp(baseDelay + jitter, 0, 250);

        if (total > 0)
            Thread.Sleep(total);
    }
}
