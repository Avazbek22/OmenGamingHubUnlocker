namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Prevents concurrent machine mutations from interleaving across processes or user sessions.
/// </summary>
public sealed class MachineOperationLock : IOperationLock
{
    private const string DefaultMutexName = @"Global\OmenGamingHubUnlocker.MachineOperation";
    private readonly string _mutexName;

    public MachineOperationLock()
        : this(DefaultMutexName)
    {
    }

    internal MachineOperationLock(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        _mutexName = mutexName;
    }

    public bool TryAcquire(out IDisposable? lease, out string failureDetails)
    {
        Mutex? mutex = null;

        try
        {
            mutex = new Mutex(initiallyOwned: false, _mutexName);
            var acquired = false;

            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // The previous process exited mid-operation; ownership is transferred to this process.
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                lease = null;
                failureDetails = Text.Get("engine.operationAlreadyRunning");
                return false;
            }

            lease = new MutexLease(mutex);
            failureDetails = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            mutex?.Dispose();
            lease = null;
            failureDetails = exception.Message;
            return false;
        }
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var ownedMutex = Interlocked.Exchange(ref _mutex, null);
            if (ownedMutex is null)
                return;

            try
            {
                ownedMutex.ReleaseMutex();
            }
            finally
            {
                ownedMutex.Dispose();
            }
        }
    }
}
