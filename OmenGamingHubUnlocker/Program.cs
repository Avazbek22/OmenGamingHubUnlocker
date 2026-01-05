using OmenGamingHubUnlocker.App;
using OmenGamingHubUnlocker.Core;
using OmenGamingHubUnlocker.UI;

try
{
    Console.Title = "OmenGamingHubUnlocker by Avazbek22";

    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("This app supports Windows only.");
        Environment.Exit(2);
    }

    var appInfo = AppInfo.Create();

    // Manifest already requests admin; this is a safety net.
    if (!appInfo.IsAdministrator)
    {
        Console.WriteLine("Administrator rights are required.");
        Environment.Exit(1);
    }

    var engine = new UnlockerEngine();
    var menu = new MenuLoop(appInfo, engine);
    menu.Run();
}
catch (Exception ex)
{
    Console.WriteLine();
    ConsoleHelpers.WriteError("Fatal error.");
    Console.WriteLine(ex);
    ConsoleHelpers.Pause("Press any key to exit...");
}