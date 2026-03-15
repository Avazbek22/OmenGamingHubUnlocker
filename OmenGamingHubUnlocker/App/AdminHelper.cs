using System.Security.Principal;

namespace OmenGamingHubUnlocker.App;

/// <summary>
/// Encapsulates elevation checks and relaunch helpers so the rest of the app can stay platform-agnostic.
/// </summary>
public static class AdminHelper
{
    /// <summary>
    /// Returns <c>true</c> when the current process token belongs to the local Administrators group.
    /// </summary>
    public static bool IsAdministrator()
    {
        try
        {
            using var currentIdentity = WindowsIdentity.GetCurrent();
            var currentPrincipal = new WindowsPrincipal(currentIdentity);
            return currentPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tries to relaunch the current executable through the UAC prompt.
    /// </summary>
    public static bool TryRelaunchAsAdministrator(string executablePath, string[] arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = BuildArgumentString(arguments),
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(processStartInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user cancelled the UAC prompt.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildArgumentString(string[] arguments)
        => arguments.Length == 0
            ? string.Empty
            : string.Join(" ", arguments.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "\"\"";

        if (argument.Any(char.IsWhiteSpace) || argument.Contains('"'))
            return "\"" + argument.Replace("\"", "\\\"") + "\"";

        return argument;
    }
}
