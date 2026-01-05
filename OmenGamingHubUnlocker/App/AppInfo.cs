using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmenGamingHubUnlocker.App;

public sealed class AppInfo
{
    public const string AppName = "OmenGamingHubUnlocker";

    public string ExePath { get; }
    public bool IsAdministrator { get; }
    public string OsDisplayName { get; }
    public string FrameworkDescription { get; }
    public string Version { get; }

    private AppInfo(string exePath, bool isAdministrator, string osDisplayName, string frameworkDescription, string version)
    {
        ExePath = exePath;
        IsAdministrator = isAdministrator;
        OsDisplayName = osDisplayName;
        FrameworkDescription = frameworkDescription;
        Version = version;
    }

    public static AppInfo Create()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
                      ?? Environment.ProcessPath
                      ?? $"{AppName}.exe";

        var isAdmin = AdminHelper.IsAdministrator();
        var osDisplayName = BuildWindowsDisplayName();

        var fw = RuntimeInformation.FrameworkDescription.Trim();
        var ver = typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        return new AppInfo(exePath, isAdmin, osDisplayName, fw, ver);
    }

    private static string BuildWindowsDisplayName()
    {
        var (major, minor, build) = TryGetRealNtVersion() ?? (0u, 0u, 0u);

        var (displayVersion, ubrFromReg) = ReadDisplayVersionAndUbr();
        var ubr = ubrFromReg;

        var brandedName = TryGetBrandingString("%WINDOWS_LONG%");
        var regProductName = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");

        var name = FirstNonEmpty(brandedName, regProductName, "Windows");

        if (IsLikelyWindows11(major, minor, build) && name.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            name = ReplaceWindows10With11(name);

        name = name.Trim();

        var buildStr = build > 0
            ? (ubr.HasValue ? $"Build {build}.{ubr.Value}" : $"Build {build}")
            : "Build ?";

        var dv = string.IsNullOrWhiteSpace(displayVersion) ? null : displayVersion.Trim();
        var includeDv = dv is not null && !name.Contains(dv, StringComparison.OrdinalIgnoreCase);

        return includeDv
            ? $"{name} {dv} ({buildStr})"
            : $"{name} ({buildStr})";
    }

    private static bool IsLikelyWindows11(uint major, uint minor, uint build)
        => major == 10 && minor == 0 && build >= 22000;

    private static string ReplaceWindows10With11(string s)
    {
        var idx = s.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return s;

        var before = s[..idx];
        var after = s[(idx + "Windows 10".Length)..];
        return before + "Windows 11" + after;
    }

    private static (string? displayVersion, int? ubr) ReadDisplayVersionAndUbr()
    {
        var displayVersion = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")
                             ?? ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId");

        int? ubr = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var ubrObj = key?.GetValue("UBR");
            if (ubrObj is int ubrInt)
                ubr = ubrInt;
        }
        catch { }

        return (displayVersion, ubr);
    }

    private static string? ReadRegistryString(string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static (uint major, uint minor, uint build)? TryGetRealNtVersion()
    {
        try
        {
            var info = new RTL_OSVERSIONINFOEXW();
            info.dwOSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOEXW>();

            var status = RtlGetVersion(ref info);
            if (status != 0)
                return null;

            return (info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetBrandingString(string token)
    {
        try
        {
            var ptr = BrandingFormatString(token);
            if (ptr == IntPtr.Zero)
                return null;

            var s = Marshal.PtrToStringUni(ptr)?.Trim();
            GlobalFree(ptr);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] items)
        => items.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEXW lpVersionInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RTL_OSVERSIONINFOEXW
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
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
