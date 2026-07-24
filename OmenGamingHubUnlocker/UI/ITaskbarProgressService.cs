namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Exposes optional taskbar feedback without coupling console workflows to Windows COM.
/// </summary>
public interface ITaskbarProgressService
{
    IDisposable BeginIndeterminate();
}
