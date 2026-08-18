namespace OmenGamingHubUnlocker.Tests.Infrastructure;

internal static class E2EHostRunner
{
    private const int DefaultTimeoutMilliseconds = 30_000;

    public static string ExecutablePath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "OmenGamingHubUnlocker.E2EHost.exe");

    public static E2EProcessResult Run(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(arguments)
        };

        if (!process.Start())
            throw new InvalidOperationException("The E2E host process could not be started.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(DefaultTimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The E2E host process did not exit before the test timeout.");
        }

        Task.WaitAll([standardOutput, standardError], DefaultTimeoutMilliseconds);
        return new E2EProcessResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult().TrimEnd(),
            standardError.GetAwaiter().GetResult().TrimEnd());
    }

    private static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }
}

internal sealed record E2EProcessResult(int ExitCode, string StandardOutput, string StandardError);
