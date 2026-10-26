param(
    [switch]$Send,
    [switch]$Inspect,
    [string]$ContactName,
    [string]$Message,
    [switch]$Follow,
    [string]$LogPath = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HABITAT\facebook-messaging.jsonl'),
    [string]$Address = 'http://127.0.0.1:8765'
)
$ErrorActionPreference = 'Stop'
$diagnosticUri = [Uri]$Address
if ($diagnosticUri.Scheme -ne 'http' -or $diagnosticUri.Host -ne '127.0.0.1' -or
    $diagnosticUri.UserInfo -or $diagnosticUri.Query -or $diagnosticUri.Fragment -or $diagnosticUri.AbsolutePath -ne '/') {
    throw 'The diagnostic address must be the local HABITAT bridge.'
}
$diagnosticPair = Invoke-RestMethod -Uri ($Address.TrimEnd('/') + '/pair') -Headers @{ 'X-Habitat-Pair' = '1' } -TimeoutSec 3
$diagnosticHeaders = @{ 'X-Habitat-Key' = $diagnosticPair.key }
if ($Send -and $Inspect) { throw 'Choose either -Send or -Inspect.' }
if ($Send -or $Inspect) {
    if ([string]::IsNullOrWhiteSpace($ContactName) -or ($Send -and [string]::IsNullOrWhiteSpace($Message))) {
        throw 'Sending requires an explicit -ContactName and -Message. No message was queued.'
    }
    $diagnosticHeaders['X-Habitat-Action'] = 'send-facebook-message'
    $diagnosticBody = @{ contactName = $ContactName; text = $Message; inspectOnly = [bool]$Inspect } | ConvertTo-Json -Compress
    Invoke-RestMethod -Uri ($Address.TrimEnd('/') + '/facebook/message') -Method Post -Headers $diagnosticHeaders `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($diagnosticBody)) -TimeoutSec 75 | ConvertTo-Json -Depth 5
    # Deliberately no retries: an uncertain result must be reviewed first.
}
$diagnosticStatus = Invoke-RestMethod -Uri ($Address.TrimEnd('/') + '/status') -Headers $diagnosticHeaders -TimeoutSec 3
[pscustomobject]@{ extensionVersion = $diagnosticStatus.facebook.extensionVersion;
    connected = $diagnosticStatus.facebookConnected; inFlight = $diagnosticStatus.hasMessageInFlight;
    lastAttempt = $diagnosticStatus.facebookMessaging; log = $LogPath } | ConvertTo-Json -Depth 6
if (Test-Path -LiteralPath $LogPath) {
    if ($Follow) { Get-Content -LiteralPath $LogPath -Tail 60 -Wait }
    else { Get-Content -LiteralPath $LogPath -Tail 60 }
} else { Write-Output 'No Messenger attempt has been recorded yet.' }
