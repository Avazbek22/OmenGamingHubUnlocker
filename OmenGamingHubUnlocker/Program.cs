WindowsConsoleShellIntegration? shellIntegration = null;

try
{
    var languagePreferenceStore = new FileLanguagePreferenceStore();
    var startupLanguage = AppLanguageResolver.ResolveStartupLanguage(languagePreferenceStore);
    Text.Initialize(new LocalizationService(languagePreferenceStore, startupLanguage));

    Console.Title = "OmenGamingHubUnlocker by Avazbek22";

    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine(Text.Get("program.windowsOnly"));
        Environment.Exit(2);
    }

    var applicationInfo = AppInfo.Create();

    if (!applicationInfo.IsAdministrator)
    {
        if (AdminHelper.TryRelaunchAsAdministrator(applicationInfo.ExePath, args))
            return;

        Console.WriteLine(Text.Get("program.adminRequired"));
        Environment.ExitCode = 1;
        return;
    }

    shellIntegration = WindowsConsoleShellIntegration.Create();
    var unlockerEngine = new UnlockerEngine();
    var menuLoop = new MenuLoop(applicationInfo, unlockerEngine, shellIntegration);
    menuLoop.Run();
}
catch (Exception exception)
{
    Console.WriteLine();
    ConsoleHelpers.WriteError(Text.Get("program.fatalError"));
    Console.WriteLine(exception);
    ConsoleHelpers.Pause(Text.Get("program.pressAnyKeyToExit"));
}
finally
{
    shellIntegration?.Dispose();
}
