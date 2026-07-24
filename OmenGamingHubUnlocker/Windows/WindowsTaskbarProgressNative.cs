using System.Runtime.InteropServices;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Wraps the Windows taskbar COM API and converts shell failures into optional no-op behavior.
/// </summary>
internal sealed class WindowsTaskbarProgressNative : IWindowsTaskbarProgressNative
{
    private ITaskbarList3? _taskbarList;

    public WindowsTaskbarProgressNative()
    {
        _taskbarList = (ITaskbarList3)(object)new CTaskbarList();
        var result = _taskbarList.HrInit();
        if (result < 0)
        {
            Dispose();
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public bool TrySetProgressState(nint windowHandle, WindowsTaskbarProgressState state)
        => TryInvoke(taskbar => taskbar.SetProgressState(windowHandle, state));

    public bool TrySetProgressValue(nint windowHandle, ulong completed, ulong total)
        => TryInvoke(taskbar => taskbar.SetProgressValue(windowHandle, completed, total));

    private bool TryInvoke(Func<ITaskbarList3, int> operation)
    {
        var taskbar = _taskbarList;
        if (taskbar is null)
            return false;

        try
        {
            return operation(taskbar) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        var taskbar = _taskbarList;
        _taskbarList = null;

        if (taskbar is not null && Marshal.IsComObject(taskbar))
            Marshal.FinalReleaseComObject(taskbar);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class CTaskbarList
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig]
        int HrInit();

        [PreserveSig]
        int AddTab(nint windowHandle);

        [PreserveSig]
        int DeleteTab(nint windowHandle);

        [PreserveSig]
        int ActivateTab(nint windowHandle);

        [PreserveSig]
        int SetActiveAlt(nint windowHandle);

        [PreserveSig]
        int MarkFullscreenWindow(nint windowHandle, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        [PreserveSig]
        int SetProgressValue(nint windowHandle, ulong completed, ulong total);

        [PreserveSig]
        int SetProgressState(nint windowHandle, WindowsTaskbarProgressState state);

        [PreserveSig]
        int RegisterTab(nint tabWindowHandle, nint parentWindowHandle);

        [PreserveSig]
        int UnregisterTab(nint tabWindowHandle);

        [PreserveSig]
        int SetTabOrder(nint tabWindowHandle, nint insertBeforeWindowHandle);

        [PreserveSig]
        int SetTabActive(nint tabWindowHandle, nint parentWindowHandle, uint reserved);

        [PreserveSig]
        int ThumbBarAddButtons(nint windowHandle, uint buttonCount, nint buttons);

        [PreserveSig]
        int ThumbBarUpdateButtons(nint windowHandle, uint buttonCount, nint buttons);

        [PreserveSig]
        int ThumbBarSetImageList(nint windowHandle, nint imageList);

        [PreserveSig]
        int SetOverlayIcon(
            nint windowHandle,
            nint icon,
            [MarshalAs(UnmanagedType.LPWStr)] string? description);

        [PreserveSig]
        int SetThumbnailTooltip(
            nint windowHandle,
            [MarshalAs(UnmanagedType.LPWStr)] string? tooltip);

        [PreserveSig]
        int SetThumbnailClip(nint windowHandle, nint rectangle);
    }
}
