namespace OmenGamingHubUnlocker.Tests.Infrastructure;

/// <summary>
/// Redirects console output so rendering code can be asserted without a real interactive console.
/// </summary>
public sealed class ConsoleOutputCapture : IDisposable
{
    private readonly TextWriter _originalWriter;
    private readonly StringWriter _captureWriter;

    public ConsoleOutputCapture()
    {
        _originalWriter = Console.Out;
        _captureWriter = new StringWriter(new StringBuilder());
        Console.SetOut(_captureWriter);
    }

    public string GetOutput() => _captureWriter.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalWriter);
        _captureWriter.Dispose();
    }
}
