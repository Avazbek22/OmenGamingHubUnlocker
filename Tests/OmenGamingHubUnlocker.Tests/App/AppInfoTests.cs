namespace OmenGamingHubUnlocker.Tests.App;

public sealed class AppInfoTests
{
    [Fact]
    public void Create_ShouldReturnNonEmptyRuntimeMetadata()
    {
        var appInfo = AppInfo.Create();

        Assert.False(string.IsNullOrWhiteSpace(appInfo.ExePath));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.OsDisplayName));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.FrameworkDescription));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.Version));
    }

    [Fact]
    public void Create_ShouldUseExpectedAppNameConstant()
    {
        Assert.Equal("OmenGamingHubUnlocker", AppInfo.AppName);
    }

    [Fact]
    public void AppDisplayName_ShouldIncludeVersionTag()
    {
        Assert.Equal("v3.2", AppInfo.AppVersionTag);
        Assert.Equal("OmenGamingHubUnlocker v3.2", AppInfo.AppDisplayName);
    }
}
