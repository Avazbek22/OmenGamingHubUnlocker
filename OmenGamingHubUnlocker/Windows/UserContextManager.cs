namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Identifies whether elevation kept the same user that owns the interactive desktop.
/// </summary>
public sealed record UserContextStatus(
    bool InspectionSucceeded,
    string ProcessIdentity,
    string InteractiveIdentity,
    string Error)
{
    public bool IsSafe =>
        InspectionSucceeded &&
        !string.IsNullOrWhiteSpace(ProcessIdentity) &&
        ProcessIdentity.Equals(InteractiveIdentity, StringComparison.OrdinalIgnoreCase);
}

public static class UserContextManager
{
    public static UserContextStatus Inspect()
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$sessionId = (Get-Process -Id $PID).SessionId
$explorer = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" |
    Where-Object { $_.SessionId -eq $sessionId } |
    Select-Object -First 1

$interactiveIdentity = $null
if ($null -ne $explorer) {
    $owner = Invoke-CimMethod -InputObject $explorer -MethodName GetOwner
    if ($owner.ReturnValue -eq 0 -and -not [string]::IsNullOrWhiteSpace($owner.User)) {
        $interactiveIdentity = if ([string]::IsNullOrWhiteSpace($owner.Domain)) {
            $owner.User
        } else {
            "$($owner.Domain)\$($owner.User)"
        }
    }
}

if ([string]::IsNullOrWhiteSpace($interactiveIdentity)) {
    $interactiveIdentity = (Get-CimInstance Win32_ComputerSystem).UserName
}

[PSCustomObject]@{
    ProcessIdentity = $currentIdentity
    InteractiveIdentity = $interactiveIdentity
} | ConvertTo-Json -Compress
""";

        if (!PowerShellRunner.TryRunScript(script, out var output, out var error, 20_000))
            return new UserContextStatus(false, string.Empty, string.Empty, error);

        try
        {
            using var document = JsonDocument.Parse(output);
            var processIdentity = GetString(document.RootElement, "ProcessIdentity");
            var interactiveIdentity = GetString(document.RootElement, "InteractiveIdentity");

            if (string.IsNullOrWhiteSpace(interactiveIdentity))
            {
                return new UserContextStatus(
                    false,
                    processIdentity,
                    string.Empty,
                    "The interactive desktop user could not be identified.");
            }

            return new UserContextStatus(true, processIdentity, interactiveIdentity, string.Empty);
        }
        catch (Exception exception)
        {
            return new UserContextStatus(false, string.Empty, string.Empty, exception.Message);
        }
    }

    private static string GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
}
