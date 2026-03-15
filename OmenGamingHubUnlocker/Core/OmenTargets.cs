namespace OmenGamingHubUnlocker.Core;

public static class OmenTargets
{
    public const string Developer = "Avazbek22";

    public const string FirewallRulePrefix = "Tame-OMEN";
    public const string HostsMarker = "# OmenGamingHubUnlocker";
    public const string PrimaryAppxPackageName = "AD2F1837.OMENCommandCenter";

    // Domains blocked via hosts (when Activate)
    public static readonly string[] HostsDomains =
    [
        "hpbp.io",
        "api.hpbp.io",
        "hpgamestream.com",
        "content.hpgamestream.com"
    ];

    // Services & tasks patterns (aggressive but reasonable)
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

    public static readonly string[] TaskPatterns =
    [
        "*Omen*",
        "*OMEN*",
        "*HP.OMEN*",
        "*OMEN Gaming*",
        "*HP Support Assistant*",
        "*HPSupportAssistant*"
    ];

    // Run entries patterns to remove (Activate)
    public static readonly string[] RunEntryPatterns =
    [
        "*OMEN*",
        "*Omen*",
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*HP Support Assistant*",
        "*HPSupportAssistant*"
    ];

    // Process names to kill (optional, aggressive)
    public static readonly string[] ProcessNamePatterns =
    [
        "*Omen*",
        "*OMEN*",
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*HPSupportAssistant*"
    ];

    // AppX filters for exe discovery (Firewall)
    public static readonly string[] AppxFilters =
    [
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*HPInc.OMEN*",
        "*HPOMEN*"
    ];

    // Classic dirs for exe discovery (Firewall fallback)
    public static readonly string[] ExtraExeDirsRelative =
    [
        @"HP\OMEN Gaming Hub",
        @"HP Inc\OMEN Gaming Hub",
        @"HP\OMENCommandCenter"
    ];
}
