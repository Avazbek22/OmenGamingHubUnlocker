try
{
    Console.Title = "OmenGamingHubUnlocker by Avazbek22";

    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("This app supports Windows only.");
        Environment.Exit(2);
    }

    var applicationInfo = AppInfo.Create();

    // The manifest already asks for elevation, but the runtime check keeps the failure explicit.
    if (!applicationInfo.IsAdministrator)
    {
        Console.WriteLine("Administrator rights are required.");
        Environment.Exit(1);
    }

    var unlockerEngine = new UnlockerEngine();
    var menuLoop = new MenuLoop(applicationInfo, unlockerEngine);
    menuLoop.Run();
}
catch (Exception exception)
{
    Console.WriteLine();
    ConsoleHelpers.WriteError("Fatal error.");
    Console.WriteLine(exception);
    ConsoleHelpers.Pause("Press any key to exit...");
}
