namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Provides the few Windows-specific file system locations used across the application.
/// </summary>
public static class WindowsPaths
{
    public static string SystemDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.System);

    public static string WindowsPowerShell =>
        Path.Combine(SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    public static string HostsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\drivers\etc\hosts");

    public static string ProgramFiles =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string ProgramFilesX86 =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public static string GetSystemExecutable(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!Path.GetFileName(fileName).Equals(fileName, StringComparison.Ordinal))
            throw new ArgumentException("A system executable name must not contain a path.", nameof(fileName));

        return Path.Combine(SystemDirectory, fileName);
    }
}
