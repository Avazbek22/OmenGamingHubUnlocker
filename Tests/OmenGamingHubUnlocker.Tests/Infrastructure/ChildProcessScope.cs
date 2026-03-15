namespace OmenGamingHubUnlocker.Tests.Infrastructure;

/// <summary>
/// Starts a disposable child process for integration tests that need a controllable external process.
/// </summary>
public sealed class ChildProcessScope : IDisposable
{
    private readonly IDisposable? _cleanupScope;

    private ChildProcessScope(Process process, string expectedProcessName, IDisposable? cleanupScope = null)
    {
        Process = process;
        ExpectedProcessName = expectedProcessName;
        _cleanupScope = cleanupScope;
    }

    public Process Process { get; }
    public string ExpectedProcessName { get; }

    public static ChildProcessScope Start(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Failed to start child process.");

        Thread.Sleep(300);
        var expectedProcessName = Path.GetFileNameWithoutExtension(fileName);
        return new ChildProcessScope(process, expectedProcessName);
    }

    /// <summary>
    /// Starts an isolated temporary copy of timeout.exe so process matching tests never touch unrelated user processes.
    /// </summary>
    public static ChildProcessScope StartUniqueNamedWaitProcess()
    {
        var temporaryDirectory = new TemporaryDirectory();
        var uniqueProcessName = $"OghTestProcess_{Guid.NewGuid():N}";
        var (sourceExecutablePath, arguments) = ResolveWaitExecutable();
        var targetExecutablePath = Path.Combine(temporaryDirectory.Path, uniqueProcessName + ".exe");

        File.Copy(sourceExecutablePath, targetExecutablePath, overwrite: true);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(startInfo)
                          ?? throw new InvalidOperationException("Failed to start isolated child process.");

            Thread.Sleep(300);
            return new ChildProcessScope(process, uniqueProcessName, temporaryDirectory);
        }
        catch
        {
            temporaryDirectory.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(3_000);
            }
        }
        catch
        {
            // Cleanup failures should not hide the real test result.
        }
        finally
        {
            Process.Dispose();
            _cleanupScope?.Dispose();
        }
    }

    private static (string path, string arguments) ResolveWaitExecutable()
    {
        var candidates = new[]
        {
            (Path.Combine(Environment.SystemDirectory, "timeout.exe"), "/T 30 /NOBREAK"),
            (Path.Combine(Environment.SystemDirectory, "ping.exe"), "127.0.0.1 -n 30")
        };

        var availableCandidate = candidates.FirstOrDefault(candidate => File.Exists(candidate.Item1));
        if (!string.IsNullOrWhiteSpace(availableCandidate.Item1))
            return availableCandidate;

        throw new FileNotFoundException("No suitable wait executable was found for process integration tests.");
    }
}
