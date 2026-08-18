namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Compares the immutable parts of discovered targets before any privileged mutation is attempted.
/// </summary>
internal static class TargetIdentityMatcher
{
    public static bool ServiceMatches(ServiceItem service, string? expectedPathName)
        => string.IsNullOrWhiteSpace(expectedPathName) ||
           ServiceIdentityEquals(service.PathName, expectedPathName);

    public static bool ServiceIdentityEquals(string? leftPathName, string? rightPathName)
        => NormalizeCommandIdentity(leftPathName)
            .Equals(NormalizeCommandIdentity(rightPathName), StringComparison.OrdinalIgnoreCase);

    public static bool TaskMatches(TaskItem task, IReadOnlyList<string>? expectedActions)
        => expectedActions is null || TaskIdentityEquals(task.ActionPaths, expectedActions);

    public static bool TaskIdentityEquals(
        IReadOnlyList<string>? leftActions,
        IReadOnlyList<string>? rightActions)
    {
        if (leftActions is null || rightActions is null)
            return leftActions is null && rightActions is null;

        var left = NormalizeActions(leftActions);
        var right = NormalizeActions(rightActions);
        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeActions(IEnumerable<string> actions)
        => actions
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Select(NormalizeCommandIdentity)
            .OrderBy(action => action, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeCommandIdentity(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return string.Empty;

        if (ExecutablePathResolver.TryResolveExistingExecutable(commandLine, out var executablePath))
            return Path.GetFullPath(executablePath).TrimEnd(Path.DirectorySeparatorChar);

        return commandLine.Trim();
    }
}
