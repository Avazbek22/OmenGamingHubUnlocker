using System.Runtime.InteropServices;

namespace OmenGamingHubUnlocker.App;

/// <summary>
/// Captures immutable runtime metadata that is displayed in the UI and reused across the application.
/// </summary>
public sealed class AppInfo
{
    private const string WindowsVersionRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string Windows10Label = "Windows 10";
    private const string Windows11Label = "Windows 11";

    public const string AppName = "OmenGamingHubUnlocker";

    public string ExePath { get; }
    public bool IsAdministrator { get; }
    public string OsDisplayName { get; }
    public string FrameworkDescription { get; }
    public string Version { get; }

    private AppInfo(
        string exePath,
        bool isAdministrator,
        string osDisplayName,
        string frameworkDescription,
        string version)
    {
        ExePath = exePath;
        IsAdministrator = isAdministrator;
        OsDisplayName = osDisplayName;
        FrameworkDescription = frameworkDescription;
        Version = version;
    }

    /// <summary>
    /// Builds a single immutable snapshot of environment details for the current process.
    /// </summary>
    public static AppInfo Create()
    {
        var executablePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? Environment.ProcessPath
                             ?? $"{AppName}.exe";

        var isAdministrator = AdminHelper.IsAdministrator();
        var operatingSystemName = BuildWindowsDisplayName();
        var frameworkDescription = RuntimeInformation.FrameworkDescription.Trim();
        var version = typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        return new AppInfo(
            executablePath,
            isAdministrator,
            operatingSystemName,
            frameworkDescription,
            version);
    }

    private static string BuildWindowsDisplayName()
    {
        var (majorVersion, minorVersion, buildNumber) = TryGetRealNtVersion() ?? (0u, 0u, 0u);
        var (displayVersion, updateBuildRevision) = ReadDisplayVersionAndBuildRevision();

        var brandedName = TryGetBrandingString("%WINDOWS_LONG%");
        var registryProductName = ReadRegistryString(WindowsVersionRegistryKey, "ProductName");
        var operatingSystemName = FirstNonEmpty(brandedName, registryProductName, "Windows").Trim();

        if (IsLikelyWindows11(majorVersion, minorVersion, buildNumber) &&
            operatingSystemName.Contains(Windows10Label, StringComparison.OrdinalIgnoreCase))
        {
            operatingSystemName = ReplaceWindows10WithWindows11(operatingSystemName);
        }

        var buildLabel = buildNumber > 0
            ? updateBuildRevision.HasValue
                ? $"Build {buildNumber}.{updateBuildRevision.Value}"
                : $"Build {buildNumber}"
            : "Build ?";

        var normalizedDisplayVersion = string.IsNullOrWhiteSpace(displayVersion)
            ? null
            : displayVersion.Trim();

        var shouldAppendDisplayVersion = normalizedDisplayVersion is not null &&
                                         !operatingSystemName.Contains(normalizedDisplayVersion, StringComparison.OrdinalIgnoreCase);

        return shouldAppendDisplayVersion
            ? $"{operatingSystemName} {normalizedDisplayVersion} ({buildLabel})"
            : $"{operatingSystemName} ({buildLabel})";
    }

    private static bool IsLikelyWindows11(uint majorVersion, uint minorVersion, uint buildNumber)
        => majorVersion == 10 && minorVersion == 0 && buildNumber >= 22000;

    private static string ReplaceWindows10WithWindows11(string displayName)
    {
        var windows10Index = displayName.IndexOf(Windows10Label, StringComparison.OrdinalIgnoreCase);
        if (windows10Index < 0)
            return displayName;

        var prefix = displayName[..windows10Index];
        var suffix = displayName[(windows10Index + Windows10Label.Length)..];
        return prefix + Windows11Label + suffix;
    }

    private static (string? displayVersion, int? buildRevision) ReadDisplayVersionAndBuildRevision()
    {
        var displayVersion = ReadRegistryString(WindowsVersionRegistryKey, "DisplayVersion")
                             ?? ReadRegistryString(WindowsVersionRegistryKey, "ReleaseId");

        int? buildRevision = null;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var versionKey = baseKey.OpenSubKey(WindowsVersionRegistryKey, writable: false);
            var buildRevisionValue = versionKey?.GetValue("UBR");

            if (buildRevisionValue is int revision)
                buildRevision = revision;
        }
        catch
        {
            // Display version is still useful even when UBR is unavailable.
        }

        return (displayVersion, buildRevision);
    }

    private static string? ReadRegistryString(string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var registryKey = baseKey.OpenSubKey(subKeyPath, writable: false);
            return registryKey?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static (uint majorVersion, uint minorVersion, uint buildNumber)? TryGetRealNtVersion()
    {
        try
        {
            var versionInfo = new RtlOsVersionInfoExw
            {
                dwOSVersionInfoSize = (uint)Marshal.SizeOf<RtlOsVersionInfoExw>()
            };

            var status = RtlGetVersion(ref versionInfo);
            if (status != 0)
                return null;

            return (versionInfo.dwMajorVersion, versionInfo.dwMinorVersion, versionInfo.dwBuildNumber);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetBrandingString(string token)
    {
        IntPtr brandingPointer = IntPtr.Zero;

        try
        {
            brandingPointer = BrandingFormatString(token);
            if (brandingPointer == IntPtr.Zero)
                return null;

            var brandingValue = Marshal.PtrToStringUni(brandingPointer)?.Trim();
            return string.IsNullOrWhiteSpace(brandingValue) ? null : brandingValue;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (brandingPointer != IntPtr.Zero)
                GlobalFree(brandingPointer);
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlGetVersion(ref RtlOsVersionInfoExw versionInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RtlOsVersionInfoExw
    {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;

        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [DllImport("winbrand.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr BrandingFormatString(string format);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GlobalFree(IntPtr memoryHandle);
}
