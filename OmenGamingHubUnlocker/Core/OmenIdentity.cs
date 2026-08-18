namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Rejects accidental substring matches such as "Women" while accepting established HP OMEN naming forms.
/// </summary>
public static class OmenIdentity
{
    public static bool IsLikelyOmenReference(params string?[] values)
        => values.Any(ContainsOmenIdentifier);

    internal static bool ContainsOmenIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var searchIndex = 0;
        while ((searchIndex = value.IndexOf("OMEN", searchIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (searchIndex == 0 || IsBoundary(value[searchIndex - 1]) || HasHpPrefix(value, searchIndex))
                return true;

            searchIndex += "OMEN".Length;
        }

        return false;
    }

    private static bool HasHpPrefix(string value, int omenIndex)
        => omenIndex >= 2 &&
           value.AsSpan(omenIndex - 2, 2).Equals("HP", StringComparison.OrdinalIgnoreCase) &&
           (omenIndex == 2 || IsBoundary(value[omenIndex - 3]));

    private static bool IsBoundary(char character)
        => !char.IsLetterOrDigit(character);
}
