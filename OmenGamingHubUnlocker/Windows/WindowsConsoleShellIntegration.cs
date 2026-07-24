using System.Runtime.InteropServices;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Applies the executable icon to the console window and exposes taskbar progress scopes.
/// </summary>
public sealed class WindowsConsoleShellIntegration : ITaskbarProgressService, IDisposable
{
    private const uint WmSetIcon = 0x0080;
    private static readonly nint IconSmall = 0;
    private static readonly nint IconBig = 1;

    private readonly object _syncRoot = new();
    private readonly nint _windowHandle;

    private WindowsTaskbarProgressController? _progressController;
    private nint _largeIcon;
    private nint _smallIcon;
    private nint _previousLargeIcon;
    private nint _previousSmallIcon;
    private int _activeProgressScopes;
    private bool _disposed;

    private WindowsConsoleShellIntegration()
    {
        if (!OperatingSystem.IsWindows())
            return;

        _windowHandle = NativeMethods.GetConsoleWindow();
        if (_windowHandle == 0)
            return;

        TryApplyWindowIcon();
        TryInitializeTaskbarProgress();
    }

    public static WindowsConsoleShellIntegration Create() => new();

    public IDisposable BeginIndeterminate()
    {
        lock (_syncRoot)
        {
            if (_disposed || _progressController is null)
                return EmptyScope.Instance;

            if (!_progressController.SetIndeterminate())
                return EmptyScope.Instance;

            _activeProgressScopes++;
            return new ProgressScope(this);
        }
    }

    private void EndProgress()
    {
        lock (_syncRoot)
        {
            if (_disposed || _activeProgressScopes == 0)
                return;

            _activeProgressScopes--;
            if (_activeProgressScopes == 0)
                _progressController?.Clear();
        }
    }

    private void TryInitializeTaskbarProgress()
    {
        try
        {
            var controller = new WindowsTaskbarProgressController(new WindowsTaskbarProgressNative());
            if (controller.Attach(_windowHandle))
            {
                _progressController = controller;
                return;
            }

            controller.Dispose();
        }
        catch
        {
            // Windows shell integration is optional and must never block application startup.
            _progressController = null;
        }
    }

    private void TryApplyWindowIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        try
        {
            var iconCount = NativeMethods.ExtractIconEx(
                executablePath,
                0,
                out _largeIcon,
                out _smallIcon,
                1);

            if (iconCount == 0)
                return;

            if (_largeIcon != 0)
            {
                _previousLargeIcon = NativeMethods.SendMessage(
                    _windowHandle,
                    WmSetIcon,
                    IconBig,
                    _largeIcon);
            }

            if (_smallIcon != 0)
            {
                _previousSmallIcon = NativeMethods.SendMessage(
                    _windowHandle,
                    WmSetIcon,
                    IconSmall,
                    _smallIcon);
            }
        }
        catch
        {
            // The embedded PE icon remains available when the console host rejects WM_SETICON.
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            _activeProgressScopes = 0;

            _progressController?.Dispose();
            _progressController = null;

            RestoreWindowIcons();
        }
    }

    private void RestoreWindowIcons()
    {
        if (_windowHandle != 0)
        {
            if (_largeIcon != 0)
            {
                NativeMethods.SendMessage(
                    _windowHandle,
                    WmSetIcon,
                    IconBig,
                    _previousLargeIcon);
            }

            if (_smallIcon != 0)
            {
                NativeMethods.SendMessage(
                    _windowHandle,
                    WmSetIcon,
                    IconSmall,
                    _previousSmallIcon);
            }
        }

        if (_largeIcon != 0)
            NativeMethods.DestroyIcon(_largeIcon);

        if (_smallIcon != 0)
            NativeMethods.DestroyIcon(_smallIcon);

        _largeIcon = 0;
        _smallIcon = 0;
    }

    private sealed class ProgressScope(WindowsConsoleShellIntegration owner) : IDisposable
    {
        private WindowsConsoleShellIntegration? _owner = owner;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref _owner, null);
            currentOwner?.EndProgress();
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint GetConsoleWindow();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint ExtractIconEx(
            string fileName,
            int iconIndex,
            out nint largeIcon,
            out nint smallIcon,
            uint iconCount);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint SendMessage(
            nint windowHandle,
            uint message,
            nint wordParameter,
            nint longParameter);

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(nint icon);
    }
}
