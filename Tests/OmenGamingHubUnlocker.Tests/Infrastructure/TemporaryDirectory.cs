namespace OmenGamingHubUnlocker.Tests.Infrastructure;

/// <summary>
/// Creates and cleans up a unique directory for tests that need isolated file system state.
/// </summary>
public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "OmenGamingHubUnlocker.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Test cleanup should be best-effort only.
        }
    }
}
