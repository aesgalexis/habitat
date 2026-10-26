param([Parameter(Mandatory = $true)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $manifestEntry = $archive.GetEntry('manifest.json')
    if ($null -eq $manifestEntry) { throw 'The Chrome manifest must be at the ZIP root.' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.manifest_version -ne 3 -or $manifest.description.Length -gt 132 -or
        $manifest.permissions -contains 'activeTab' -or $manifest.host_permissions -contains '<all_urls>') {
        throw 'The store package must use MV3 and scoped permissions.'
    }
    foreach ($required in @($manifest.background.service_worker, $manifest.action.default_popup,
        'connection.js', 'transport.js', 'translation.js', 'search-transport.js', 'search.js', 'youtube.js', 'facebook.js', 'facebook-messaging.js',
        'welcome.html', 'welcome.css', 'welcome.js', 'privacy.html', 'LICENSE.txt') + @($manifest.content_scripts.js)) {
        if ($null -eq $archive.GetEntry($required)) { throw "Missing runtime resource: $required" }
    }
    foreach ($icon in $manifest.icons.PSObject.Properties) {
        $entry = $archive.GetEntry($icon.Value)
        if ($null -eq $entry) { throw "Missing icon: $($icon.Value)" }
        $stream = $entry.Open()
        try {
            $bytes = [byte[]]::new(24)
            if ($stream.Read($bytes, 0, 24) -ne 24 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80) { throw 'Invalid PNG icon.' }
            $width = 0; $height = 0
            foreach ($index in 16..19) { $width = $width * 256 + $bytes[$index] }
            foreach ($index in 20..23) { $height = $height * 256 + $bytes[$index] }
            if ($width -ne [int]$icon.Name -or $height -ne [int]$icon.Name) { throw 'Incorrect icon dimensions.' }
        } finally { $stream.Dispose() }
    }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(^/|\.\.|\\|\.env|secret|credential|(^|/)(tests|artifacts)/)' -or
            $entry.Name -in 'connect.html','connect.js','reload.html','reload.js','finish.html','finish.js') {
            throw "Unexpected store file: $($entry.FullName)"
        }
    }
    $policyReader = [IO.StreamReader]::new($archive.GetEntry('privacy.html').Open())
    try { $policy = $policyReader.ReadToEnd() } finally { $policyReader.Dispose() }
    if (-not $policy.Contains('127.0.0.1:8765') -or -not $policy.Contains('Limited Use') -or
        -not $policy.Contains('Enable connection')) { throw 'The packaged privacy notice is incomplete.' }
    Write-Host "Chrome package checks passed: MV3, runtime resources, all icon sizes, scoped permissions, privacy and source-only payload ($($archive.Entries.Count) files)."
} finally { $archive.Dispose() }
