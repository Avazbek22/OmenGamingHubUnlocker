namespace OmenGamingHubUnlocker.Tests.UI;

public sealed class ConsoleActivityIndicatorTests
{
    [Fact]
    public void Run_ShouldReturnTheOperationResultAndShowActivity()
    {
        using var capture = new ConsoleOutputCapture();
        var taskbarProgress = new RecordingTaskbarProgressService();

        var result = ConsoleActivityIndicator.Run("Inspecting", () => 42, taskbarProgress);

        Assert.Equal(42, result);
        Assert.Contains("Inspecting", capture.GetOutput());
        Assert.Equal(1, taskbarProgress.BeginCount);
        Assert.Equal(1, taskbarProgress.DisposeCount);
    }

    [Fact]
    public void Run_ShouldPropagateTheOriginalException()
    {
        using var capture = new ConsoleOutputCapture();
        var taskbarProgress = new RecordingTaskbarProgressService();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConsoleActivityIndicator.Run<int>(
                "Inspecting",
                () => throw new InvalidOperationException("inspection failed"),
                taskbarProgress));

        Assert.Equal("inspection failed", exception.Message);
        Assert.Equal(1, taskbarProgress.BeginCount);
        Assert.Equal(1, taskbarProgress.DisposeCount);
    }

    private sealed class RecordingTaskbarProgressService : ITaskbarProgressService
    {
        public int BeginCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IDisposable BeginIndeterminate()
        {
            BeginCount++;
            return new CallbackScope(() => DisposeCount++);
        }
    }

    private sealed class CallbackScope(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose()
        {
            var currentCallback = Interlocked.Exchange(ref _callback, null);
            currentCallback?.Invoke();
        }
    }
}
