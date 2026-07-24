namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class HostsManagerTests
{
    [Theory]
    [InlineData("127.0.0.1 api.hpbp.io", true)]
    [InlineData("0.0.0.0 api.hpbp.io", true)]
    [InlineData("::1 api.hpbp.io", true)]
    [InlineData("192.168.1.10 api.hpbp.io", false)]
    [InlineData("127.0.0.1 not-api.hpbp.io", false)]
    [InlineData("# 127.0.0.1 api.hpbp.io", false)]
    public void IsBlockedDomainLine_ShouldRequireBlockingAddressAndExactHost(
        string line,
        bool expected)
    {
        Assert.Equal(expected, HostsManager.IsBlockedDomainLine(line, "api.hpbp.io"));
    }

    [Fact]
    public void Activate_ShouldAddAllMissingDomainsInSingleDocumentUpdate()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hosts");
        File.WriteAllText(path, "127.0.0.1 localhost\r\n", new UTF8Encoding(false));

        var lines = HostsManager.ActivateHostsBlockAtPath(
            path,
            ["api.hpbp.io", "hpbp.io"],
            OmenTargets.HostsMarker,
            dryRun: false);

        Assert.DoesNotContain(lines, line => line.Level == "ERR");
        var content = File.ReadAllText(path);
        Assert.Contains("127.0.0.1\tapi.hpbp.io", content);
        Assert.Contains("127.0.0.1\thpbp.io", content);
        Assert.Equal(2, content.Split(OmenTargets.HostsMarker).Length - 1);
    }

    [Fact]
    public void Activate_ShouldNotDuplicateExistingBlockingEntry()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hosts");
        File.WriteAllText(
            path,
            $"0.0.0.0 api.hpbp.io {OmenTargets.HostsMarker}\n",
            new UTF8Encoding(false));

        HostsManager.ActivateHostsBlockAtPath(
            path,
            ["api.hpbp.io"],
            OmenTargets.HostsMarker,
            dryRun: false);

        Assert.Equal(1, File.ReadAllLines(path).Count(line => line.Contains("api.hpbp.io")));
    }

    [Fact]
    public void Disable_ShouldRemoveOnlyManagedLines()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hosts");
        File.WriteAllText(
            path,
            $"127.0.0.1 localhost\r\n127.0.0.1 api.hpbp.io {OmenTargets.HostsMarker}\r\n10.0.0.1 internal\r\n",
            new UTF8Encoding(false));

        var lines = HostsManager.DisableHostsBlockAtPath(path, OmenTargets.HostsMarker, dryRun: false);

        Assert.DoesNotContain(lines, line => line.Level == "ERR");
        var content = File.ReadAllText(path);
        Assert.Contains("127.0.0.1 localhost", content);
        Assert.Contains("10.0.0.1 internal", content);
        Assert.DoesNotContain("api.hpbp.io", content);
    }

    [Fact]
    public void Mutation_ShouldPreserveUtf8BomAndCrLf()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hosts");
        File.WriteAllText(path, "# Примечание\r\n127.0.0.1 localhost\r\n", new UTF8Encoding(true));

        HostsManager.ActivateHostsBlockAtPath(
            path,
            ["api.hpbp.io"],
            OmenTargets.HostsMarker,
            dryRun: false);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var content = File.ReadAllText(path);
        Assert.Contains("# Примечание\r\n", content);
        Assert.DoesNotContain("\n127.0.0.1\tapi.hpbp.io\n", content);
    }

    [Fact]
    public void Mutation_ShouldPreserveUnknownLegacyBytes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hosts");
        byte[] originalBytes = [0x23, 0x20, 0xC0, 0x0D, 0x0A];
        File.WriteAllBytes(path, originalBytes);

        var lines = HostsManager.ActivateHostsBlockAtPath(
            path,
            ["api.hpbp.io"],
            OmenTargets.HostsMarker,
            dryRun: false);

        Assert.DoesNotContain(lines, line => line.Level == "ERR");
        var updatedBytes = File.ReadAllBytes(path);
        Assert.True(updatedBytes.AsSpan().StartsWith(originalBytes));
        Assert.Contains((byte)0xC0, updatedBytes);
    }

    [Fact]
    public void InspectFile_ShouldReportReadFailureInsteadOfUnblockedDomains()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = Path.Combine(directory.Path, "missing-hosts");

        var inspection = HostsManager.InspectFile(
            missingPath,
            ["api.hpbp.io"],
            OmenTargets.HostsMarker);

        Assert.False(inspection.Success);
        Assert.Empty(inspection.Domains);
        Assert.NotEmpty(inspection.Error);
    }
}
