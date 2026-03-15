namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Provides the few Windows-specific file system locations used across the application.
/// </summary>
public static class WindowsPaths
{
    public static string HostsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\drivers\etc\hosts");

    public static string ProgramFiles =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string ProgramFilesX86 =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
}
