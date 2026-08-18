namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class RegistryRunManagerTests
{
    [Fact]
    public void RemoveEntries_ShouldDeleteMatchingValue()
    {
        using var registryScope = TemporaryRegistryScope.Create();
        registryScope.Key.SetValue("OmenBackground", "Omen.exe", RegistryValueKind.String);
        var entry = new RunEntry(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            "OmenBackground",
            "Omen.exe");

        var lines = RegistryRunManager.RemoveEntriesAtSubKey(
            [entry],
            dryRun: false,
            registryScope.SubKeyPath);

        Assert.Null(registryScope.Key.GetValue("OmenBackground"));
        Assert.DoesNotContain(lines, line => line.Level == "ERR");
    }

    [Fact]
    public void RestoreEntries_ShouldRestoreMissingValue()
    {
        using var registryScope = TemporaryRegistryScope.Create();
        var backup = CreateBackup("Original.exe");

        var lines = RegistryRunManager.RestoreEntriesAtSubKey(
            [backup],
            dryRun: false,
            registryScope.SubKeyPath);

        Assert.Equal("Original.exe", registryScope.Key.GetValue("OmenBackground"));
        Assert.DoesNotContain(lines, line => line.Level == "ERR");
    }

    [Fact]
    public void RestoreEntries_ShouldNotOverwriteValueChangedAfterActivation()
    {
        using var registryScope = TemporaryRegistryScope.Create();
        registryScope.Key.SetValue("OmenBackground", "Changed.exe", RegistryValueKind.String);

        var lines = RegistryRunManager.RestoreEntriesAtSubKey(
            [CreateBackup("Original.exe")],
            dryRun: false,
            registryScope.SubKeyPath);

        Assert.Equal("Changed.exe", registryScope.Key.GetValue("OmenBackground"));
        Assert.Contains(lines, line => line.Level == "ERR");
    }

    [Fact]
    public void RestoreEntries_WithDryRun_ShouldNotCreateMissingRegistryKey()
    {
        var subKeyPath = $@"Software\OmenGamingHubUnlocker.Tests.{Guid.NewGuid():N}";

        var lines = RegistryRunManager.RestoreEntriesAtSubKey(
            [CreateBackup("Original.exe")],
            dryRun: true,
            subKeyPath);

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var createdKey = baseKey.OpenSubKey(subKeyPath, writable: false);
        Assert.Null(createdKey);
        Assert.DoesNotContain(lines, line => line.Level == "ERR");
    }

    private static RunEntryBackup CreateBackup(string value)
        => new(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            "OmenBackground",
            value,
            RegistryValueKind.String);

    private sealed class TemporaryRegistryScope : IDisposable
    {
        private TemporaryRegistryScope(string subKeyPath, RegistryKey key)
        {
            SubKeyPath = subKeyPath;
            Key = key;
        }

        public string SubKeyPath { get; }
        public RegistryKey Key { get; }

        public static TemporaryRegistryScope Create()
        {
            var subKeyPath = $@"Software\OmenGamingHubUnlocker.Tests.{Guid.NewGuid():N}";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            var key = baseKey.CreateSubKey(subKeyPath, writable: true)
                      ?? throw new InvalidOperationException("Could not create the isolated test registry key.");
            return new TemporaryRegistryScope(subKeyPath, key);
        }

        public void Dispose()
        {
            Key.Dispose();
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(SubKeyPath, throwOnMissingSubKey: false);
        }
    }
}
