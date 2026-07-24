namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Keeps slow Windows inspections visibly responsive without mixing progress output into the engine.
/// </summary>
public static class ConsoleActivityIndicator
{
    private const int FrameDelayMilliseconds = 180;
    private static readonly string[] Frames = [".  ", ".. ", "..."];

    public static T Run<T>(string message, Func<T> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(operation);

        if (Console.IsOutputRedirected)
        {
            ConsoleHelpers.WriteInfo($"{message}...");
            return operation();
        }

        using var cursorVisibility = ConsoleHelpers.HideCursorForAnimation();
        var operationTask = Task.Run(operation);
        var frameIndex = 0;
        var renderedLength = 0;

        try
        {
            do
            {
                renderedLength = RenderFrame(message, Frames[frameIndex], renderedLength);
                frameIndex = (frameIndex + 1) % Frames.Length;
                Thread.Sleep(FrameDelayMilliseconds);
            }
            while (!operationTask.IsCompleted);

            return operationTask.GetAwaiter().GetResult();
        }
        finally
        {
            ClearFrame(renderedLength);
        }
    }

    private static int RenderFrame(string message, string frame, int previousLength)
    {
        var text = message + frame;
        var padding = Math.Max(0, previousLength - text.Length);

        Console.Write('\r');
        ConsoleHelpers.WithColor(ConsoleColor.Cyan, () => Console.Write(text));
        if (padding > 0)
            Console.Write(new string(' ', padding));

        return Math.Max(previousLength, text.Length);
    }

    private static void ClearFrame(int renderedLength)
    {
        if (renderedLength == 0)
            return;

        Console.Write('\r');
        Console.Write(new string(' ', renderedLength));
        Console.Write('\r');
    }

}
