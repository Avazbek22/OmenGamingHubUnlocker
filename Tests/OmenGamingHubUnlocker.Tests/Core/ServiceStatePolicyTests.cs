namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class ServiceStatePolicyTests
{
    [Theory]
    [InlineData("Stopped", true)]
    [InlineData("Running", false)]
    [InlineData("Stop Pending", false)]
    [InlineData("Paused", false)]
    [InlineData("Unknown", false)]
    public void IsStopped_ShouldRequireTheExactTerminalState(string state, bool expected)
    {
        var service = new ServiceItem("Service", "Service", "Manual", state);

        Assert.Equal(expected, ServiceStatePolicy.IsStopped(service));
    }

    [Theory]
    [InlineData(true, "Running", true)]
    [InlineData(true, "Start Pending", false)]
    [InlineData(false, "Stopped", true)]
    [InlineData(false, "Stop Pending", false)]
    [InlineData(false, "Paused", false)]
    public void MatchesOriginalRunningState_ShouldRejectIntermediateStates(
        bool originallyRunning,
        string currentState,
        bool expected)
    {
        var service = new ServiceItem("Service", "Service", "Automatic", currentState);
        var backup = new ServiceBackup("Service", "Automatic", originallyRunning);

        Assert.Equal(expected, ServiceStatePolicy.MatchesOriginalRunningState(service, backup));
    }
}
