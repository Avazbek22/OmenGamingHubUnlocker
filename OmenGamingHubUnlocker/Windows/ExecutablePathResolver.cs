namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Extracts an executable path from quoted and unquoted Windows command lines without executing them.
/// </summary>
public static class ExecutablePathResolver
{
    public static bool TryResolveExistingExecutable(string? commandLine, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(commandLine.Trim());
            var candidate = ExtractCandidate(expanded);
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            candidate = candidate.Trim().Trim('"');
            if (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return false;

            var normalizedPath = Path.GetFullPath(candidate);
            if (!File.Exists(normalizedPath))
                return false;

            executablePath = normalizedPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractCandidate(string commandLine)
    {
        if (commandLine[0] == '"')
        {
            var closingQuote = commandLine.IndexOf('"', 1);
            return closingQuote > 1 ? commandLine[1..closingQuote] : string.Empty;
        }

        var extensionIndex = commandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return extensionIndex < 0
            ? string.Empty
            : commandLine[..(extensionIndex + ".exe".Length)];
    }
}
