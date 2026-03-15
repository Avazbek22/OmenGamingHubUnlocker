namespace OmenGamingHubUnlocker.Tests.Integration;

public sealed class SystemCapabilitySmokeTests
{
    [Fact]
    public void HostsManager_GetDomainsStatus_ShouldReturnOneEntryPerDomain()
    {
        var domains = new[] { "example.com", "localhost" };

        var result = HostsManager.GetDomainsStatus(domains, OmenTargets.HostsMarker);

        Assert.Equal(domains.Length, result.Count);
    }

    [Fact]
    public void RegistryRunManager_QueryRunEntries_ShouldReturnDistinctEntries()
    {
        var entries = RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns);

        Assert.Equal(entries.Count, entries.DistinctBy(entry => $"{entry.Hive}|{entry.View}|{entry.Name}", StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ServiceManager_QueryServices_ShouldReturnDistinctServiceNames()
    {
        var services = ServiceManager.QueryServices(OmenTargets.ServicePatterns);

        Assert.Equal(services.Count, services.DistinctBy(service => service.Name, StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TaskSchedulerManager_QueryTasks_ShouldReturnDistinctTaskPaths()
    {
        var tasks = TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns);

        Assert.Equal(tasks.Count, tasks.DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FirewallManager_DiscoverCandidateExecutables_ShouldReturnRootedPathsWhenPresent()
    {
        var paths = FirewallManager.DiscoverCandidateExecutables();

        Assert.All(paths, path => Assert.True(System.IO.Path.IsPathRooted(path)));
    }

    [Fact]
    public void CapabilityChecks_ShouldReturnDetails()
    {
        var checks = new[]
        {
            TaskSchedulerManager.CheckCapability(),
            FirewallManager.CheckCapability(),
            ServiceManager.CheckCapability(),
            HostsManager.CheckWriteAccess(OmenTargets.HostsMarker),
            PowerShellRunner.CheckAvailability(),
            PowerShellRunner.CheckNetshAvailability(),
            AppxPackageManager.CheckResetCapability()
        };

        Assert.All(checks, check => Assert.False(string.IsNullOrWhiteSpace(check.details)));
    }
}
