namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class ReportsTests
{
    [Fact]
    public void OperationReportOk_ShouldCreateSuccessfulReport()
    {
        var report = OperationReport.Ok("done");

        Assert.True(report.Success);
        Assert.Equal("done", report.Title);
        Assert.Empty(report.Lines);
    }

    [Fact]
    public void OperationReportFail_ShouldCreateFailedReport()
    {
        var report = OperationReport.Fail("failed");

        Assert.False(report.Success);
        Assert.Equal("failed", report.Title);
    }

    [Fact]
    public void StatusSnapshot_DefaultConstructor_ShouldUseEmptyStrings()
    {
        var snapshot = new StatusSnapshot();

        Assert.Equal(string.Empty, snapshot.Area);
        Assert.Equal(string.Empty, snapshot.Item);
        Assert.Equal(string.Empty, snapshot.Current);
        Assert.Equal(string.Empty, snapshot.Expected);
        Assert.Equal(string.Empty, snapshot.Result);
    }

    [Fact]
    public void StatusReport_ToPrettyText_ShouldIncludeAllCounters()
    {
        var report = new StatusReport
        {
            ServicesMatched = 1,
            TasksMatched = 2,
            RunEntriesMatched = 3,
            FirewallRulesFound = 4
        };
        report.RunningProcesses.Add("proc");

        var text = report.ToPrettyText();

        Assert.Contains("Services matched: 1", text);
        Assert.Contains("Tasks matched: 2", text);
        Assert.Contains("Run entries matched: 3", text);
        Assert.Contains("Firewall rules (Tame-OMEN): 4", text);
        Assert.Contains("Running OMEN-related processes: 1", text);
    }
}
