namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Opens trusted external links without coupling the menu to the Windows shell.
/// </summary>
public interface IExternalLinkLauncher
{
    bool TryOpen(string? url);
}
