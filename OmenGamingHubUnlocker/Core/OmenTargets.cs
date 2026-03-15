namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Centralizes every pattern and identifier that the unlocker treats as part of the OMEN ecosystem.
/// </summary>
public static class OmenTargets
{
    public const string Developer = "Avazbek22";
    public const string FirewallRulePrefix = "Tame-OMEN";
    public const string HostsMarker = "# OmenGamingHubUnlocker";
    public const string PrimaryAppxPackageName = "AD2F1837.OMENCommandCenter";

    /// <summary>
    /// Known OMEN endpoints that should resolve locally while the app is tamed.
    /// </summary>
    public static readonly string[] HostsDomains =
    [
        "hpbp.io",
        "api.hpbp.io",
        "hpgamestream.com",
        "content.hpgamestream.com"
    ];

    /// <summary>
    /// Service display names and service names that should be switched to manual mode.
    /// </summary>
    public static readonly string[] ServicePatterns =
    [
        "*OMEN*",
        "*Omen*",
        "*HP OMEN*",
        "*HPGaming*",
        "*HP Gaming*",
        "*HPGame*",
        "*HPSupportAssistant*"
    ];

    /// <summary>
    /// Scheduled tasks that are disabled in tame mode.
    /// </summary>
    public static readonly string[] TaskPatterns =
    [
        "*Omen*",
        "*OMEN*",
        "*HP.OMEN*",
        "*OMEN Gaming*",
        "*HP Support Assistant*",
        "*HPSupportAssistant*"
    ];

    /// <summary>
    /// Run key entries that are removed while tame mode is active.
    /// </summary>
    public static readonly string[] RunEntryPatterns =
    [
        "*OMEN*",
        "*Omen*",
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*HP Support Assistant*",
        "*HPSupportAssistant*"
    ];

    /// <summary>
    /// Process names that are safe to terminate before reset or re-apply operations.
    /// </summary>
    public static readonly string[] ProcessNamePatterns =
    [
        "*Omen*",
        "*OMEN*",
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*HPSupportAssistant*"
    ];

    /// <summary>
    /// Package filters used to find the installed OMEN AppX package and its binaries.
    /// </summary>
    public static readonly string[] AppxFilters =
    [
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*HPInc.OMEN*",
        "*HPOMEN*"
    ];

    /// <summary>
    /// Classic install locations that are scanned when AppX discovery is not enough.
    /// </summary>
    public static readonly string[] ExtraExeDirsRelative =
    [
        @"HP\OMEN Gaming Hub",
        @"HP Inc\OMEN Gaming Hub",
        @"HP\OMENCommandCenter"
    ];
}
