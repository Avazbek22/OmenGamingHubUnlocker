using System.Diagnostics;
using System.Text;

namespace OmenGamingHubUnlocker.Windows;

public static class PowerShellRunner
{
    public static (bool ok, string details) CheckAvailability()
    {
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
        if (File.Exists(ps))
            return (true, "powershell.exe found.");

        return (TryRun("powershell", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", out var output, out var err, 15_000),
            string.IsNullOrWhiteSpace(err) ? output.Trim() : err.Trim());
    }

    public static (bool ok, string details) CheckNetshAvailability()
    {
        var netsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
        return File.Exists(netsh) ? (true, "netsh.exe found.") : (false, "netsh.exe not found.");
    }

    public static bool TryRun(string fileName, string args, out string stdout, out string stderr, int timeoutMs = 30_000)
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

            using var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stdoutBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stderrBuilder.AppendLine(e.Data);
            };

            if (!process.Start())
            {
                stdout = string.Empty;
                stderr = "Failed to start process.";
                return false;
            }

            // Read both streams asynchronously to avoid deadlocks on full buffers.
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                TryKill(process);
                process.WaitForExit();

                stdout = stdoutBuilder.ToString().TrimEnd();
                stderr = $"Process timed out after {timeoutMs} ms.";
                return false;
            }

            process.WaitForExit();

            stdout = stdoutBuilder.ToString().TrimEnd();
            stderr = stderrBuilder.ToString().TrimEnd();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            stdout = string.Empty;
            stderr = ex.Message;
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
