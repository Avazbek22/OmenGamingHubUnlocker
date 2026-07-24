using System.Text.RegularExpressions;

namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Provides one consistent, culture-independent wildcard implementation for target discovery.
/// </summary>
public static partial class WildcardMatcher
{
    public static bool IsMatch(string? value, string? pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern ?? string.Empty)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(
            value ?? string.Empty,
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
    }

    public static bool MatchesAny(string? value, IEnumerable<string> patterns)
        => patterns.Any(pattern => IsMatch(value, pattern));

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
}
