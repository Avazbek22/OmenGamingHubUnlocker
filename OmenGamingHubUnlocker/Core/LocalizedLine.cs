namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Creates localized operation lines while keeping the call sites compact and readable.
/// </summary>
public static class LocalizedLine
{
    public static OperationLine Info(string key, params object?[] arguments)
        => Create("INFO", key, arguments);

    public static OperationLine Ok(string key, params object?[] arguments)
        => Create("OK", key, arguments);

    public static OperationLine Warn(string key, params object?[] arguments)
        => Create("WARN", key, arguments);

    public static OperationLine Err(string key, params object?[] arguments)
        => Create("ERR", key, arguments);

    private static OperationLine Create(string level, string key, params object?[] arguments)
    {
        return new OperationLine
        {
            Level = level,
            Text = Text.Format(key, arguments)
        };
    }
}
