using System.Runtime.InteropServices;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Resolves the stable AppContainer SID used by Windows Firewall for a Store package family.
/// </summary>
public static class AppContainerSidResolver
{
    public static bool TryResolve(string packageFamilyName, out string sid, out string error)
    {
        sid = string.Empty;

        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            error = "The package family name is empty.";
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            error = "AppContainer SIDs are available only on Windows.";
            return false;
        }

        nint nativeSid = 0;
        nint sidString = 0;

        try
        {
            var result = DeriveAppContainerSidFromAppContainerName(packageFamilyName, out nativeSid);
            if (result != 0 || nativeSid == 0)
            {
                error = $"DeriveAppContainerSidFromAppContainerName failed with HRESULT 0x{result:X8}.";
                return false;
            }

            if (!ConvertSidToStringSid(nativeSid, out sidString) || sidString == 0)
            {
                error = $"ConvertSidToStringSid failed with Win32 error {Marshal.GetLastWin32Error()}.";
                return false;
            }

            sid = Marshal.PtrToStringUni(sidString) ?? string.Empty;
            if (sid.Length == 0)
            {
                error = "Windows returned an empty AppContainer SID.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            if (sidString != 0)
                _ = LocalFree(sidString);

            if (nativeSid != 0)
                FreeSid(nativeSid);
        }
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(
        string appContainerName,
        out nint appContainerSid);

    [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSid(nint sid, out nint stringSid);

    [DllImport("advapi32.dll")]
    private static extern nint FreeSid(nint sid);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
