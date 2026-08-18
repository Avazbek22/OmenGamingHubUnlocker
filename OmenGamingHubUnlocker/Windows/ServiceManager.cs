namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a Windows service that the unlocker may inspect or modify.
/// </summary>
public sealed record ServiceItem(
    string Name,
    string DisplayName,
    string StartMode,
    string State = "Unknown",
    string PathName = "",
    bool DelayedAutoStart = false);

/// <summary>
/// Describes the desired startup mode for a specific service.
/// </summary>
public sealed record ServiceStartModeTarget(
    string Name,
    string DesiredStartMode,
    bool DelayedAutoStart = false,
    string ExpectedPathName = "");

/// <summary>
/// Identifies the exact service instance whose runtime state may be changed.
/// </summary>
public sealed record ServiceRuntimeTarget(
    string Name,
    string ExpectedPathName = "");

/// <summary>
/// Uses bounded child processes for service discovery and mutation so a damaged WMI provider cannot hang the app.
/// </summary>
public static class ServiceManager
{
    private const int DiscoveryTimeoutMilliseconds = 30_000;
    private const int CommandTimeoutMilliseconds = 20_000;
    private const int StateWaitTimeoutMilliseconds = 20_000;

    public static (bool ok, string details) CheckCapability()
        => TryQueryAllServices(out _, out var error)
            ? (true, Text.Get("manager.services.capabilityOk"))
            : (false, error);

    public static List<ServiceItem> QueryServices(string[] patterns)
    {
        if (!TryQueryAllServices(out var services, out var error))
            throw new InvalidOperationException($"Service discovery failed: {error}");

        if (patterns.Length == 0)
            return services;

        return services
            .Where(service =>
                patterns.Any(pattern =>
                    WildcardMatcher.IsMatch(service.Name, pattern) ||
                    WildcardMatcher.IsMatch(service.DisplayName, pattern) ||
                    WildcardMatcher.IsMatch(service.PathName, pattern)) &&
                OmenIdentity.IsLikelyOmenReference(service.Name, service.DisplayName, service.PathName))
            .ToList();
    }

    public static List<OperationLine> StopServices(
        IEnumerable<ServiceRuntimeTarget> targets,
        bool dryRun)
        => ChangeServiceRunningState(targets, desiredRunning: false, dryRun);

    public static List<OperationLine> StartServices(
        IEnumerable<ServiceRuntimeTarget> targets,
        bool dryRun)
        => ChangeServiceRunningState(targets, desiredRunning: true, dryRun);

    public static List<OperationLine> ApplyStartModeTargets(
        IEnumerable<ServiceStartModeTarget> targets,
        bool dryRun)
    {
        var requestedTargets = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.Name))
            .DistinctBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(target => target.Name, StringComparer.OrdinalIgnoreCase);

        if (requestedTargets.Count == 0)
            return [LocalizedLine.Info("manager.services.nothingToChange")];

        var currentServices = QueryServices([])
            .Where(service => requestedTargets.ContainsKey(service.Name))
            .ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var lines = new List<OperationLine>();

        foreach (var (serviceName, target) in requestedTargets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentServices.TryGetValue(serviceName, out var currentService))
            {
                lines.Add(LocalizedLine.Warn("manager.services.notFound", serviceName));
                continue;
            }

            if (!TargetIdentityMatcher.ServiceMatches(currentService, target.ExpectedPathName))
            {
                lines.Add(LocalizedLine.Err("manager.services.identityChanged", serviceName));
                continue;
            }

            if (StartModeMatches(currentService, target))
            {
                lines.Add(LocalizedLine.Info("manager.services.alreadySet", serviceName, FormatTarget(target)));
                continue;
            }

            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok(
                    "manager.services.wouldSet",
                    serviceName,
                    FormatTarget(target),
                    currentService.StartMode));
                continue;
            }

            if (TryApplyWithSc(target, out var error))
            {
                lines.Add(LocalizedLine.Ok("manager.services.set", serviceName, FormatTarget(target)));
                continue;
            }

            lines.Add(ServiceReachedStartMode(target)
                ? LocalizedLine.Info("manager.services.alreadySet", serviceName, FormatTarget(target))
                : LocalizedLine.Err("manager.services.failedToSet", serviceName, error));
        }

        return lines;
    }

    public static string NormalizeStartMode(string startMode)
        => startMode.Trim() switch
        {
            var mode when mode.Equals("Auto", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Delayed", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Delayed-Auto", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Manual", StringComparison.OrdinalIgnoreCase) => "Manual",
            var mode when mode.Equals("Demand", StringComparison.OrdinalIgnoreCase) => "Manual",
            var mode when mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) => "Disabled",
            _ => startMode.Trim()
        };

    private static List<OperationLine> ChangeServiceRunningState(
        IEnumerable<ServiceRuntimeTarget> targets,
        bool desiredRunning,
        bool dryRun)
    {
        var requestedTargets = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.Name))
            .DistinctBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedTargets.Count == 0)
            return [LocalizedLine.Info("manager.services.nothingToChange")];

        var currentServices = QueryServices([])
            .ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var lines = new List<OperationLine>();

        foreach (var target in requestedTargets)
        {
            if (!currentServices.TryGetValue(target.Name, out var service))
            {
                lines.Add(LocalizedLine.Warn("manager.services.notFound", target.Name));
                continue;
            }

            if (!TargetIdentityMatcher.ServiceMatches(service, target.ExpectedPathName))
            {
                lines.Add(LocalizedLine.Err("manager.services.identityChanged", target.Name));
                continue;
            }

            var desiredState = desiredRunning ? "Running" : "Stopped";
            if (service.State.Equals(desiredState, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(LocalizedLine.Info(
                    desiredRunning ? "manager.services.alreadyRunning" : "manager.services.alreadyStopped",
                    target.Name));
                continue;
            }

            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok(
                    desiredRunning ? "manager.services.wouldStart" : "manager.services.wouldStop",
                    target.Name));
                continue;
            }

            if (!TryChangeRunningStateWithSc(target.Name, desiredRunning, out var commandError))
            {
                var reachedDesiredState = WaitForServiceState(
                                              target.Name,
                                              desiredRunning,
                                              out var commandFailureWaitError) &&
                                          ServiceIdentityStillMatches(target);
                lines.Add(reachedDesiredState
                    ? LocalizedLine.Info(
                        desiredRunning ? "manager.services.alreadyRunning" : "manager.services.alreadyStopped",
                        target.Name)
                    : LocalizedLine.Err(
                        desiredRunning ? "manager.services.failedToStart" : "manager.services.failedToStop",
                        target.Name,
                        CombineErrors(commandError, commandFailureWaitError)));
                continue;
            }

            lines.Add(WaitForServiceState(target.Name, desiredRunning, out var waitError)
                ? LocalizedLine.Ok(
                    desiredRunning ? "manager.services.started" : "manager.services.stopped",
                    target.Name)
                : LocalizedLine.Err(
                    desiredRunning ? "manager.services.startTimeout" : "manager.services.stopTimeout",
                    target.Name,
                    waitError));
        }

        return lines;
    }

    private static bool TryApplyWithSc(ServiceStartModeTarget target, out string error)
    {
        var normalizedMode = NormalizeStartMode(target.DesiredStartMode);
        var scMode = normalizedMode switch
        {
            "Manual" => "demand",
            "Automatic" when target.DelayedAutoStart => "delayed-auto",
            "Automatic" => "auto",
            "Disabled" => "disabled",
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target.DesiredStartMode,
                "Unsupported service mode.")
        };

        return PowerShellRunner.TryRun(
            WindowsPaths.GetSystemExecutable("sc.exe"),
            ["config", target.Name, "start=", scMode],
            out _,
            out error,
            CommandTimeoutMilliseconds);
    }

    private static bool TryChangeRunningStateWithSc(
        string serviceName,
        bool desiredRunning,
        out string error)
        => PowerShellRunner.TryRun(
            WindowsPaths.GetSystemExecutable("sc.exe"),
            [desiredRunning ? "start" : "stop", serviceName],
            out _,
            out error,
            CommandTimeoutMilliseconds);

    private static bool WaitForServiceState(
        string serviceName,
        bool desiredRunning,
        out string error)
    {
        var escapedServiceName = EscapePowerShellLiteral(serviceName);
        var desiredState = desiredRunning ? "Running" : "Stopped";
        var script = $$"""
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name '{{escapedServiceName}}' -ErrorAction Stop
$desired = [System.ServiceProcess.ServiceControllerStatus]::{{desiredState}}
$service.WaitForStatus($desired, [TimeSpan]::FromSeconds(15))
$service.Refresh()
if ($service.Status -ne $desired) {
    throw "Service did not reach {{desiredState}} state."
}
""";

        return PowerShellRunner.TryRunScript(
            script,
            out _,
            out error,
            StateWaitTimeoutMilliseconds);
    }

    private static bool TryQueryAllServices(out List<ServiceItem> services, out string error)
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$result = @(
    Get-CimInstance -ClassName Win32_Service -ErrorAction Stop |
        ForEach-Object {
            [PSCustomObject]@{
                Name = [string]$_.Name
                DisplayName = [string]$_.DisplayName
                StartMode = [string]$_.StartMode
                State = [string]$_.State
                PathName = [string]$_.PathName
                DelayedAutoStart = [bool]$_.DelayedAutoStart
            }
        }
)
ConvertTo-Json -InputObject $result -Compress
""";

        if (!PowerShellRunner.TryRunScript(
                script,
                out var output,
                out error,
                DiscoveryTimeoutMilliseconds))
        {
            services = [];
            return false;
        }

        try
        {
            services = DeserializeServices(output);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            services = [];
            error = exception.Message;
            return false;
        }
    }

    private static bool ServiceReachedStartMode(ServiceStartModeTarget target)
        => TryQueryAllServices(out var services, out _) &&
           services.FirstOrDefault(service =>
               service.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)) is { } service &&
           TargetIdentityMatcher.ServiceMatches(service, target.ExpectedPathName) &&
           StartModeMatches(service, target);

    private static bool ServiceIdentityStillMatches(ServiceRuntimeTarget target)
        => TryQueryAllServices(out var services, out _) &&
           services.FirstOrDefault(service =>
               service.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)) is { } service &&
           TargetIdentityMatcher.ServiceMatches(service, target.ExpectedPathName);

    private static string CombineErrors(string first, string second)
        => string.Join(
            "; ",
            new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static List<ServiceItem> DeserializeServices(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        var elements = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
            JsonValueKind.Object => [document.RootElement],
            _ => []
        };

        return elements.Select(element => new ServiceItem(
            ReadString(element, "Name"),
            ReadString(element, "DisplayName"),
            ReadString(element, "StartMode"),
            ReadString(element, "State"),
            ReadString(element, "PathName"),
            ReadBoolean(element, "DelayedAutoStart"))).ToList();
    }

    private static bool StartModeMatches(ServiceItem currentService, ServiceStartModeTarget target)
    {
        var currentMode = NormalizeStartMode(currentService.StartMode);
        var desiredMode = NormalizeStartMode(target.DesiredStartMode);
        return currentMode.Equals(desiredMode, StringComparison.OrdinalIgnoreCase) &&
               (!desiredMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ||
                currentService.DelayedAutoStart == target.DelayedAutoStart);
    }

    private static string FormatTarget(ServiceStartModeTarget target)
        => NormalizeStartMode(target.DesiredStartMode) == "Automatic" && target.DelayedAutoStart
            ? "Automatic (Delayed Start)"
            : NormalizeStartMode(target.DesiredStartMode);

    private static string EscapePowerShellLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           property.GetBoolean();
}
