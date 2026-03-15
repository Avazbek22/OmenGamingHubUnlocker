namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Contains all console-specific rendering helpers so the rest of the code can focus on business logic.
/// </summary>
public static class ConsoleHelpers
{
    /// <summary>
    /// Adds a small pause before animated lines to make long reports easier to scan.
    /// </summary>
    public static int LineDelayMs { get; set; } = 25;

    /// <summary>
    /// Adds a bit of random jitter so repeated logs feel less mechanical.
    /// </summary>
    public static int LineJitterMs { get; set; } = 20;

    private static readonly Random RandomGenerator = new();

    public static void WriteHeader(string text)
    {
        EnsureUtf8OutputEncoding();

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
        EnsureUtf8OutputEncoding();

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
        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;

        try
        {
            action();
        }
        finally
        {
            Console.ForegroundColor = previousColor;
        }
    }

    public static void TryClearScreen()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            Console.WriteLine();
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine();
        }
    }

    public static void WriteSection(string title)
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine($"=== {title} ==="));
        Console.WriteLine();
    }

    public static void WriteBullets(string title, IEnumerable<string> bulletLines)
    {
        WriteSection(title);

        foreach (var bulletLine in bulletLines)
            Console.WriteLine($" - {bulletLine}");

        Console.WriteLine();
    }

    public static void Pause(string message = "Press any key to continue...")
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine(message));

        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            Console.ReadLine();
        }
    }

    public static string ReadMenuChoice()
    {
        WithColor(ConsoleColor.Gray, () => Console.Write("Select: "));

        try
        {
            return (Console.ReadLine() ?? string.Empty).Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Lets the user continue with Enter or cancel with Escape.
    /// </summary>
    public static bool ConfirmEnterOrEscape(string message = "Press Enter to continue or Esc to cancel...")
    {
        Console.WriteLine();
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine(message));

        while (true)
        {
            try
            {
                var pressedKey = Console.ReadKey(intercept: true).Key;
                if (pressedKey == ConsoleKey.Enter)
                    return true;

                if (pressedKey == ConsoleKey.Escape)
                    return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public static void PrintOperationLinesAnimated(IEnumerable<OperationLine> lines)
    {
        foreach (var line in lines)
        {
            DelayBeforeNextLine();
            var lineColor = LevelToColor(line.Level);
            WithColor(lineColor, () => Console.WriteLine(line.Text));
        }
    }

    public static void PrintLinesAnimated(IEnumerable<string> lines, ConsoleColor color = ConsoleColor.Gray)
    {
        foreach (var line in lines)
        {
            DelayBeforeNextLine();
            WithColor(color, () => Console.WriteLine(line));
        }
    }

    /// <summary>
    /// Prints a single key/value pair with separate colors for the label and the value.
    /// </summary>
    public static void PrintKeyValue(
        string key,
        string value,
        ConsoleColor keyColor = ConsoleColor.DarkGray,
        ConsoleColor valueColor = ConsoleColor.White)
    {
        WithColor(keyColor, () => Console.Write($"{key}: "));
        WithColor(valueColor, () => Console.WriteLine(value));
    }

    private static void EnsureUtf8OutputEncoding()
    {
        try
        {
            if (Console.OutputEncoding != Encoding.UTF8)
                Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Encoding setup is cosmetic; logging should continue even when the console host refuses it.
        }
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

    private static void DelayBeforeNextLine()
    {
        var baseDelay = LineDelayMs;
        var randomJitter = LineJitterMs <= 0 ? 0 : RandomGenerator.Next(0, LineJitterMs + 1);
        var totalDelay = Math.Clamp(baseDelay + randomJitter, 0, 250);

        if (totalDelay > 0)
            Thread.Sleep(totalDelay);
    }
}
