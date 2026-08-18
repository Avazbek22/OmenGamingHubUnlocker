namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class UserContextStatusTests
{
    [Fact]
    public void IsSafe_ShouldUseMatchingSidsWhenAvailable()
    {
        var status = new UserContextStatus(
            true,
            @"DOMAIN\SameName",
            @"OTHER\SameName",
            string.Empty,
            "S-1-5-21-1000",
            "S-1-5-21-1000");

        Assert.True(status.IsSafe);
    }

    [Fact]
    public void IsSafe_ShouldRejectDifferentSidsDespiteMatchingNames()
    {
        var status = new UserContextStatus(
            true,
            @"PC\User",
            @"PC\User",
            string.Empty,
            "S-1-5-21-1000",
            "S-1-5-21-2000");

        Assert.False(status.IsSafe);
    }
}
