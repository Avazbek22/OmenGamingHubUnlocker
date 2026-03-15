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

    // The manifest already asks for elevation, but the runtime check keeps the failure explicit.
    if (!applicationInfo.IsAdministrator)
    {
        Console.WriteLine(Text.Get("program.adminRequired"));
        Environment.Exit(1);
    }

    var unlockerEngine = new UnlockerEngine();
    var menuLoop = new MenuLoop(applicationInfo, unlockerEngine);
    menuLoop.Run();
}
catch (Exception exception)
{
    Console.WriteLine();
    ConsoleHelpers.WriteError(Text.Get("program.fatalError"));
    Console.WriteLine(exception);
    ConsoleHelpers.Pause(Text.Get("program.pressAnyKeyToExit"));
}
