namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class MachineOperationLockTests
{
    [Fact]
    public void TryAcquire_ShouldExcludeAnotherThreadUntilLeaseIsReleased()
    {
        var mutexName = $@"Local\OmenGamingHubUnlocker.Tests.{Guid.NewGuid():N}";
        var firstLock = new MachineOperationLock(mutexName);
        var secondLock = new MachineOperationLock(mutexName);

        Assert.True(firstLock.TryAcquire(out var firstLease, out var firstError), firstError);
        Assert.NotNull(firstLease);

        try
        {
            var blockedAttempt = RunOnSeparateThread(secondLock);
            Assert.False(blockedAttempt.Acquired);
            Assert.NotEmpty(blockedAttempt.Error);
        }
        finally
        {
            firstLease.Dispose();
        }

        var successfulAttempt = RunOnSeparateThread(secondLock);
        Assert.True(successfulAttempt.Acquired, successfulAttempt.Error);
    }

    private static (bool Acquired, string Error) RunOnSeparateThread(MachineOperationLock operationLock)
    {
        (bool Acquired, string Error) result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = TryAcquireAndRelease(operationLock);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The mutex test thread did not finish in time.");

        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();

        return result;
    }

    private static (bool Acquired, string Error) TryAcquireAndRelease(MachineOperationLock operationLock)
    {
        var acquired = operationLock.TryAcquire(out var lease, out var error);
        lease?.Dispose();
        return (acquired, error);
    }
}
