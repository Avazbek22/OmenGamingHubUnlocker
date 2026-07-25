using System.ComponentModel;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Opens HTTPS links through the user's default Windows browser.
/// </summary>
public sealed class ShellExternalLinkLauncher : IExternalLinkLauncher
{
    private readonly Func<ProcessStartInfo, bool> _startProcess;

    public ShellExternalLinkLauncher()
        : this(StartProcess)
    {
    }

    internal ShellExternalLinkLauncher(Func<ProcessStartInfo, bool> startProcess)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        _startProcess = startProcess;
    }

    public bool TryOpen(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return _startProcess(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is
                                             Win32Exception or
                                             InvalidOperationException or
                                             NotSupportedException or
                                             PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool StartProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}
