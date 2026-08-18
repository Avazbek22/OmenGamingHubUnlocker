namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Keeps slow Windows inspections visibly responsive without mixing progress output into the engine.
/// </summary>
public static class ConsoleActivityIndicator
{
    private const int FrameDelayMilliseconds = 180;
    private static readonly string[] Frames = [".  ", ".. ", "..."];

    public static T Run<T>(
        string message,
        Func<T> operation,
        ITaskbarProgressService? taskbarProgress = null)
        => Run(message, _ => operation(), taskbarProgress);

    public static T Run<T>(
        string message,
        Func<IProgress<string>, T> operation,
        ITaskbarProgressService? taskbarProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(operation);

        using var taskbarProgressScope = TryBeginTaskbarProgress(taskbarProgress);

        if (Console.IsOutputRedirected)
        {
            ConsoleHelpers.WriteInfo($"{message}...");
            var lastMessage = message;
            var redirectedProgress = new InlineProgress(stage =>
            {
                if (string.IsNullOrWhiteSpace(stage) ||
                    stage.Equals(lastMessage, StringComparison.Ordinal))
                {
                    return;
                }

                lastMessage = stage;
                ConsoleHelpers.WriteInfo($"{stage}...");
            });
            return operation(redirectedProgress);
        }

        using var cursorVisibility = ConsoleHelpers.HideCursorForAnimation();
        var currentMessage = message;
        var progress = new InlineProgress(stage =>
        {
            if (!string.IsNullOrWhiteSpace(stage))
                Volatile.Write(ref currentMessage, stage);
        });
        var operationTask = Task.Run(() => operation(progress));
        var stopwatch = Stopwatch.StartNew();
        var frameIndex = 0;
        var renderedLength = 0;

        try
        {
            do
            {
                renderedLength = RenderFrame(
                    Volatile.Read(ref currentMessage),
                    Frames[frameIndex],
                    stopwatch.Elapsed,
                    renderedLength);
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

    private static int RenderFrame(
        string message,
        string frame,
        TimeSpan elapsed,
        int previousLength)
    {
        var text = $"{message}{frame} [{elapsed:mm\\:ss}]";
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

    private static IDisposable? TryBeginTaskbarProgress(ITaskbarProgressService? taskbarProgress)
    {
        try
        {
            return taskbarProgress?.BeginIndeterminate();
        }
        catch
        {
            // Optional taskbar feedback must not affect the underlying operation.
            return null;
        }
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
