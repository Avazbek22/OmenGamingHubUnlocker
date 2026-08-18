using OmenGamingHubUnlocker.Core;
using OmenGamingHubUnlocker.Windows;
using Microsoft.Win32;

return args.FirstOrDefault() switch
{
    "journal-crash" => WriteJournalAndCrash(args),
    "state-merge" => MergeState(args),
    "echo-argument" => EchoArgument(args),
    _ => 64
};

static int WriteJournalAndCrash(string[] commandArguments)
{
    if (commandArguments.Length != 4 ||
        !Enum.TryParse<UnlockerOperationPhase>(commandArguments[3], ignoreCase: false, out var phase))
    {
        return 65;
    }

    var store = new FileOperationJournalStore(commandArguments[1], commandArguments[2]);
    var entry = store.Begin(
        UnlockerOperationKind.ResetAndReapply,
        manageFirewall: true,
        manageHosts: true);
    store.Advance(entry.OperationId, phase);

    Console.WriteLine(entry.OperationId);
    Console.Out.Flush();
    Environment.Exit(137);
    return 137;
}

static int MergeState(string[] commandArguments)
{
    if (commandArguments.Length != 4)
        return 66;

    var statePath = commandArguments[1];
    var userSid = commandArguments[2];
    var identity = commandArguments[3];
    var store = new UnlockerStateStore(statePath, userSid);
    store.PersistBackups(
        [new ServiceBackup($"service-{identity}", "Automatic", true, PathName: $@"C:\HP\{identity}\service.exe")],
        [new TaskBackup($@"\task-{identity}", true, true, ScheduledTaskRuntimeState.Running, [$@"C:\HP\{identity}\task.exe"])],
        [new RunEntryBackup(RegistryHive.CurrentUser, RegistryView.Registry64, $"run-{identity}", $@"C:\HP\{identity}\run.exe")]);

    Console.WriteLine(identity);
    return 0;
}

static int EchoArgument(string[] commandArguments)
{
    if (commandArguments.Length != 2)
        return 67;

    Console.Write(commandArguments[1]);
    return 0;
}
