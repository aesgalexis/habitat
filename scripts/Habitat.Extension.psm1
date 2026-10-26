Set-StrictMode -Version Latest

function Get-HabitatChromeExtensionFiles {
    @('manifest.json', 'background.js', 'connection.js', 'transport.js', 'content.js',
        'translation.js', 'search-transport.js', 'search.js', 'youtube.js', 'facebook.js', 'facebook-messaging.js', 'popup.html', 'popup.js',
        'welcome.html', 'welcome.css', 'welcome.js', 'privacy.html',
        'icons/icon16.png', 'icons/icon32.png', 'icons/icon48.png', 'icons/icon128.png')
}

function Copy-HabitatChromeExtension([string]$Source, [string]$Destination) {
    foreach ($name in Get-HabitatChromeExtensionFiles) {
        $file = Join-Path $Source $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing extension resource: $name" }
        $target = Join-Path $Destination $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $target
    }
}

Export-ModuleMember -Function Get-HabitatChromeExtensionFiles, Copy-HabitatChromeExtension
