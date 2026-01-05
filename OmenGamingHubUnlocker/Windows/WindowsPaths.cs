namespace OmenGamingHubUnlocker.Windows;

public static class WindowsPaths
{
    public static string HostsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\drivers\etc\hosts");

    public static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
}