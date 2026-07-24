using System.Net;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Represents the block state of a single hosts entry.
/// </summary>
public sealed record HostsDomainState(string Domain, bool Blocked);

/// <summary>
/// Distinguishes an unblocked domain from a hosts file that could not be inspected.
/// </summary>
public sealed record HostsInspection(
    bool Success,
    IReadOnlyList<HostsDomainState> Domains,
    int ManagedLineCount,
    string Error)
{
    public bool AllBlocked => Success && Domains.Count > 0 && Domains.All(domain => domain.Blocked);
}

/// <summary>
/// Encapsulates atomic, encoding-preserving hosts-file inspection and mutation.
/// </summary>
public static class HostsManager
{
    public static (bool ok, string details) CheckWriteAccess(string marker)
    {
        _ = marker;

        try
        {
            var hostsFilePath = WindowsPaths.HostsPath;
            if (!File.Exists(hostsFilePath))
                return (false, Text.Format("manager.hosts.fileNotFound", hostsFilePath));

            using var stream = new FileStream(hostsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return (true, Text.Get("manager.hosts.accessibleForReadWrite"));
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    public static HostsInspection Inspect(IEnumerable<string> domains, string marker)
        => InspectFile(WindowsPaths.HostsPath, domains, marker);

    public static HostsInspection InspectFile(string hostsFilePath, IEnumerable<string> domains, string marker)
    {
        try
        {
            if (!File.Exists(hostsFilePath))
            {
                return new HostsInspection(
                    false,
                    [],
                    0,
                    Text.Format("manager.hosts.fileNotFound", hostsFilePath));
            }

            var document = ReadDocument(hostsFilePath);
            var domainStates = domains
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(domain => new HostsDomainState(
                    domain,
                    document.Lines.Any(line => IsBlockedDomainLine(line, domain))))
                .ToList();
            var managedLineCount = document.Lines.Count(line =>
                line.Contains(marker, StringComparison.OrdinalIgnoreCase));

            return new HostsInspection(true, domainStates, managedLineCount, string.Empty);
        }
        catch (Exception exception)
        {
            return new HostsInspection(false, [], 0, exception.Message);
        }
    }

    public static List<HostsDomainState> GetDomainsStatus(IEnumerable<string> domains, string marker)
        => Inspect(domains, marker).Domains.ToList();

    public static List<OperationLine> ActivateHostsBlock(string[] domains, string marker, bool dryRun)
        => ActivateHostsBlockAtPath(WindowsPaths.HostsPath, domains, marker, dryRun);

    public static List<OperationLine> ActivateHostsBlockAtPath(
        string hostsFilePath,
        IEnumerable<string> domains,
        string marker,
        bool dryRun)
    {
        if (!File.Exists(hostsFilePath))
            return [LocalizedLine.Err("manager.hosts.fileNotFound", hostsFilePath)];

        HostsDocument document;
        try
        {
            document = ReadDocument(hostsFilePath);
        }
        catch (Exception exception)
        {
            return [LocalizedLine.Err("manager.hosts.cannotRead", exception.Message)];
        }

        var normalizedDomains = domains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missingDomains = normalizedDomains
            .Where(domain => !document.Lines.Any(line => IsBlockedDomainLine(line, domain)))
            .ToList();

        if (missingDomains.Count == 0)
            return [LocalizedLine.Info("manager.hosts.nothingToAdd")];

        var lines = missingDomains
            .Select(domain => $"127.0.0.1\t{domain}\t{marker}")
            .ToList();

        if (dryRun)
            return lines.Select(line => LocalizedLine.Ok("manager.hosts.wouldAdd", line)).ToList();

        var updatedLines = document.Lines.Concat(lines).ToList();
        if (!TryWriteDocumentAtomically(hostsFilePath, document, updatedLines, out var writeError))
            return [LocalizedLine.Err("manager.hosts.failedToWrite", writeError)];

        return lines.Select(line => LocalizedLine.Ok("manager.hosts.added", line)).ToList();
    }

    public static List<OperationLine> DisableHostsBlock(string marker, bool dryRun)
        => DisableHostsBlockAtPath(WindowsPaths.HostsPath, marker, dryRun);

    public static List<OperationLine> DisableHostsBlockAtPath(string hostsFilePath, string marker, bool dryRun)
    {
        if (!File.Exists(hostsFilePath))
            return [LocalizedLine.Err("manager.hosts.fileNotFound", hostsFilePath)];

        HostsDocument document;
        try
        {
            document = ReadDocument(hostsFilePath);
        }
        catch (Exception exception)
        {
            return [LocalizedLine.Err("manager.hosts.cannotRead", exception.Message)];
        }

        var remainingLines = document.Lines
            .Where(line => !line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var removedLineCount = document.Lines.Count - remainingLines.Count;

        if (removedLineCount == 0)
            return [LocalizedLine.Info("manager.hosts.noMarkerLines")];

        if (dryRun)
            return [LocalizedLine.Ok("manager.hosts.wouldRemove", removedLineCount, marker)];

        if (!TryWriteDocumentAtomically(hostsFilePath, document, remainingLines, out var writeError))
            return [LocalizedLine.Err("manager.hosts.failedToWrite", writeError)];

        return [LocalizedLine.Ok("manager.hosts.removed", removedLineCount)];
    }

    public static bool IsBlockedDomainLine(string? line, string domain)
    {
        var content = (line ?? string.Empty).Split('#', 2)[0].Trim();
        if (content.Length == 0)
            return false;

        var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out var address) || !IsBlockingAddress(address))
            return false;

        return parts.Skip(1).Any(host => host.Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlockingAddress(IPAddress address)
        => IPAddress.IsLoopback(address) ||
           address.Equals(IPAddress.Any) ||
           address.Equals(IPAddress.IPv6Any);

    private static HostsDocument ReadDocument(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var content = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var newLine = DetectNewLine(content);
        var endsWithNewLine = content.EndsWith("\r\n", StringComparison.Ordinal) ||
                              content.EndsWith('\n') ||
                              content.EndsWith('\r');
        var lines = content.Length == 0
            ? []
            : content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .ToList();

        if (endsWithNewLine && lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return new HostsDocument(lines, encoding, newLine, endsWithNewLine);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Encoding.UTF8.Preamble.Length);

        if (bytes.AsSpan().StartsWith(Encoding.UTF32.Preamble))
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), Encoding.UTF32.Preamble.Length);

        var bigEndianUtf32 = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        if (bytes.AsSpan().StartsWith(bigEndianUtf32.Preamble))
            return (bigEndianUtf32, bigEndianUtf32.Preamble.Length);

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), Encoding.Unicode.Preamble.Length);

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return (
                new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
                Encoding.BigEndianUnicode.Preamble.Length);
        }

        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        try
        {
            _ = strictUtf8.GetCharCount(bytes);
            return (strictUtf8, 0);
        }
        catch (DecoderFallbackException)
        {
            // Latin-1 preserves every unknown legacy byte while managed ASCII entries are added.
            return (Encoding.Latin1, 0);
        }
    }

    private static bool TryWriteDocumentAtomically(
        string path,
        HostsDocument original,
        List<string> lines,
        out string error)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "The hosts directory path is invalid.";
            return false;
        }

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var content = string.Join(original.NewLine, lines);
            if (original.EndsWithNewLine && lines.Count > 0)
                content += original.NewLine;

            File.WriteAllText(temporaryPath, content, original.Encoding);
            File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
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
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // The primary write error is more useful than temporary-file cleanup failures.
            }
        }
    }

    private static string DetectNewLine(string content)
    {
        var lineFeedIndex = content.IndexOf('\n');
        if (lineFeedIndex > 0 && content[lineFeedIndex - 1] == '\r')
            return "\r\n";

        if (lineFeedIndex >= 0)
            return "\n";

        return content.Contains('\r') ? "\r" : Environment.NewLine;
    }

    private sealed record HostsDocument(
        List<string> Lines,
        Encoding Encoding,
        string NewLine,
        bool EndsWithNewLine);
}
