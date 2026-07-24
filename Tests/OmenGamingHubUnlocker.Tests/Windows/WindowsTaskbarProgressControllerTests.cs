namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class WindowsTaskbarProgressControllerTests
{
    private static readonly nint WindowHandle = 42;

    [Fact]
    public void SetIndeterminate_ShouldRequireAnAttachedWindow()
    {
        var native = new RecordingTaskbarNative();
        using var controller = new WindowsTaskbarProgressController(native);

        var changed = controller.SetIndeterminate();

        Assert.False(changed);
        Assert.Empty(native.States);
    }

    [Fact]
    public void SetIndeterminate_ShouldAvoidDuplicateShellCalls()
    {
        var native = new RecordingTaskbarNative();
        using var controller = new WindowsTaskbarProgressController(native);
        Assert.True(controller.Attach(WindowHandle));

        Assert.True(controller.SetIndeterminate());
        Assert.True(controller.SetIndeterminate());

        Assert.Equal([WindowsTaskbarProgressState.Indeterminate], native.States);
    }

    [Fact]
    public void SetProgress_ShouldClampValuesAndAvoidDuplicates()
    {
        var native = new RecordingTaskbarNative();
        using var controller = new WindowsTaskbarProgressController(native);
        Assert.True(controller.Attach(WindowHandle));

        Assert.True(controller.SetProgress(-10));
        Assert.True(controller.SetProgress(-1));
        Assert.True(controller.SetProgress(150));

        Assert.Equal([WindowsTaskbarProgressState.Normal], native.States);
        Assert.Equal([(0UL, 10_000UL), (10_000UL, 10_000UL)], native.Values);
    }

    [Fact]
    public void Dispose_ShouldClearProgressAndReleaseNativeClientOnce()
    {
        var native = new RecordingTaskbarNative();
        var controller = new WindowsTaskbarProgressController(native);
        Assert.True(controller.Attach(WindowHandle));
        Assert.True(controller.SetIndeterminate());

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(
            [WindowsTaskbarProgressState.Indeterminate, WindowsTaskbarProgressState.NoProgress],
            native.States);
        Assert.Equal(1, native.DisposeCount);
    }

    private sealed class RecordingTaskbarNative : IWindowsTaskbarProgressNative
    {
        public List<WindowsTaskbarProgressState> States { get; } = [];
        public List<(ulong Completed, ulong Total)> Values { get; } = [];
        public int DisposeCount { get; private set; }

        public bool TrySetProgressState(nint windowHandle, WindowsTaskbarProgressState state)
        {
            Assert.Equal(WindowHandle, windowHandle);
            States.Add(state);
            return true;
        }

        public bool TrySetProgressValue(nint windowHandle, ulong completed, ulong total)
        {
            Assert.Equal(WindowHandle, windowHandle);
            Values.Add((completed, total));
            return true;
        }

        public void Dispose() => DisposeCount++;
    }
}
