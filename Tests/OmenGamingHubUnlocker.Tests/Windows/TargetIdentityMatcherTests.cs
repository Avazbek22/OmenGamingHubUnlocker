namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class TargetIdentityMatcherTests
{
    [Fact]
    public void ServiceMatches_ShouldAcceptTheSameExecutableWithDifferentArguments()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "OmenService.exe");
        File.WriteAllText(executable, string.Empty);
        var service = new ServiceItem(
            "HPOmenCap",
            "OMEN service",
            "Auto",
            PathName: $"\"{executable}\" --current");

        var matches = TargetIdentityMatcher.ServiceMatches(
            service,
            $"\"{executable}\" --original");

        Assert.True(matches);
    }

    [Fact]
    public void ServiceMatches_ShouldRejectAReplacementExecutable()
    {
        var service = new ServiceItem(
            "HPOmenCap",
            "OMEN service",
            "Auto",
            PathName: @"C:\HP\New\OmenService.exe");

        Assert.False(TargetIdentityMatcher.ServiceMatches(
            service,
            @"C:\HP\Old\OmenService.exe"));
    }

    [Fact]
    public void TaskMatches_ShouldIgnoreActionOrderingButRejectReplacementActions()
    {
        var task = new TaskItem(
            @"\OmenTask",
            true,
            "Ready",
            [@"C:\HP\OmenA.exe", @"C:\HP\OmenB.exe"]);

        Assert.True(TargetIdentityMatcher.TaskMatches(
            task,
            [@"C:\HP\OmenB.exe", @"C:\HP\OmenA.exe"]));
        Assert.False(TargetIdentityMatcher.TaskMatches(
            task,
            [@"C:\HP\Replacement.exe"]));
    }

    [Fact]
    public void TaskMatches_ShouldTreatNullAsLegacyIdentityButNotAsAnEmptySnapshot()
    {
        var task = new TaskItem(@"\OmenTask", true, "Ready", [@"C:\HP\Omen.exe"]);

        Assert.True(TargetIdentityMatcher.TaskMatches(task, expectedActions: null));
        Assert.False(TargetIdentityMatcher.TaskMatches(task, []));
    }
}
