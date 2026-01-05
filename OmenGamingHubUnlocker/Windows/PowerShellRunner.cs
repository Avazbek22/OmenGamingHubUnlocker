using System.Diagnostics;

namespace OmenGamingHubUnlocker.Windows;

public static class PowerShellRunner
{
    public static (bool ok, string details) CheckAvailability()
    {
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
        if (File.Exists(ps)) return (true, "powershell.exe found.");

        // fallback: try PATH
        return (TryRun("powershell", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", out var output, out var err),
            string.IsNullOrWhiteSpace(err) ? output.Trim() : err.Trim());
    }

    public static (bool ok, string details) CheckNetshAvailability()
    {
        var netsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
        return File.Exists(netsh) ? (true, "netsh.exe found.") : (false, "netsh.exe not found.");
    }

    public static bool TryRun(string fileName, string args, out string stdout, out string stderr)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p is null)
            {
                stdout = "";
                stderr = "Failed to start process.";
                return false;
            }

            stdout = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);

            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            stdout = "";
            stderr = ex.Message;
            return false;
        }
    }
}