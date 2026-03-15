using System.Diagnostics;
using System.Text;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Runs external commands with bounded waits and buffered output collection.
/// </summary>
public static class PowerShellRunner
{
    public static (bool ok, string details) CheckAvailability()
    {
        var systemPowerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell\\v1.0\\powershell.exe");

        if (File.Exists(systemPowerShellPath))
            return (true, "powershell.exe found.");

        return (
            TryRun("powershell", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", out var output, out var error, 15_000),
            string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
    }

    public static (bool ok, string details) CheckNetshAvailability()
    {
        var netshPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
        return File.Exists(netshPath) ? (true, "netsh.exe found.") : (false, "netsh.exe not found.");
    }

    /// <summary>
    /// Executes a child process and captures both output streams without risking the usual redirected stream deadlocks.
    /// </summary>
    public static bool TryRun(string fileName, string arguments, out string standardOutput, out string standardError, int timeoutMs = 30_000)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                    outputBuilder.AppendLine(eventArgs.Data);
            };

            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                    errorBuilder.AppendLine(eventArgs.Data);
            };

            if (!process.Start())
            {
                standardOutput = string.Empty;
                standardError = "Failed to start process.";
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                TryKillProcess(process);
                process.WaitForExit();

                standardOutput = outputBuilder.ToString().TrimEnd();
                standardError = $"Process timed out after {timeoutMs} ms.";
                return false;
            }

            process.WaitForExit();

            standardOutput = outputBuilder.ToString().TrimEnd();
            standardError = errorBuilder.ToString().TrimEnd();
            return process.ExitCode == 0;
        }
        catch (Exception exception)
        {
            standardOutput = string.Empty;
            standardError = exception.Message;
            return false;
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The best-effort timeout path should never hide the original timeout reason.
        }
    }
}
