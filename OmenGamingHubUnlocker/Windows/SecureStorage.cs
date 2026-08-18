using System.Security.AccessControl;
using System.Security.Principal;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Provides durable atomic writes and rejects reparse-point redirection for elevated state files.
/// </summary>
internal static class SecureStorage
{
    public static void EnsureDirectory(string directoryPath, bool hardenAcl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        Directory.CreateDirectory(directoryPath);
        EnsureNotReparsePoint(directoryPath);

        if (hardenAcl)
            ApplyMachineStateAcl(directoryPath);
    }

    public static string ReadAllText(string path)
    {
        EnsureNotReparsePoint(path);
        return File.ReadAllText(path);
    }

    public static FileStream OpenExclusiveLock(string path)
    {
        if (File.Exists(path))
            EnsureNotReparsePoint(path);

        return new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.WriteThrough);
    }

    public static void WriteAllTextAtomically(
        string path,
        string content,
        Encoding encoding,
        bool hardenDirectoryAcl)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new InvalidOperationException("The secure storage directory path is invalid.");

        EnsureDirectory(directoryPath, hardenDirectoryAcl);
        if (File.Exists(path))
            EnsureNotReparsePoint(path);

        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, encoding))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                EnsureNotReparsePoint(path);
                File.Replace(
                    temporaryPath,
                    path,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public static void DeleteFile(string path)
    {
        if (!File.Exists(path))
            return;

        EnsureNotReparsePoint(path);
        File.Delete(path);
    }

    public static void EnsureNotReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Secure storage path cannot be a reparse point: {path}");
    }

    public static string? TryGetOwnerSid(string path)
    {
        try
        {
            EnsureNotReparsePoint(path);
            var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner);
            return security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
                ? owner.Value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyMachineStateAcl(string directoryPath)
    {
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(CreateFullControlRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
            inheritance));
        security.AddAccessRule(CreateFullControlRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
            inheritance));

        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    private static FileSystemAccessRule CreateFullControlRule(
        IdentityReference identity,
        InheritanceFlags inheritance)
        => new(
            identity,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow);

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch
        {
            // A committed state remains valid even if temporary cleanup is delayed by security software.
        }
    }
}
