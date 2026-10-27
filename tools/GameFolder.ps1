# Finds DAoC client folders like the TokaZerk UI Manager (PlatformInstallLocator): the Eden launcher's gameDir, the Blackthorn
# launcher's gamePath, then the default EA install folders. A folder counts when it holds camelot.exe. Dot-source this file.
# Server: the launcher that named the folder, else btui_game_bridge.dll (Blackthorn, checked first: a Blackthorn folder is an
# Eden client plus the bridge) or eden.dll (Eden).

function Read-LauncherKey([string]$ConfigPath, [string]$Key) {
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        return $null
    }
    try {
        $value = (Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json).$Key
        if ($value -is [string] -and $value) { return $value }
    }
    catch {
    }
    return $null
}

function Find-GameFolder {
    $candidates = @(
        @{ Path = Read-LauncherKey (Join-Path $env:APPDATA 'eden-launcher\config.json') 'gameDir'; Source = 'EdenLauncher' }
        @{ Path = Read-LauncherKey (Join-Path $env:APPDATA 'bt-launcher\config.json') 'gamePath'; Source = 'BlackthornLauncher' }
        @{ Path = Join-Path ${env:ProgramFiles(x86)} 'Electronic Arts\Dark Age of Camelot'; Source = 'WellKnown' }
        @{ Path = Join-Path $env:ProgramFiles 'Electronic Arts\Dark Age of Camelot'; Source = 'WellKnown' }
    )
    $seen = @{}
    foreach ($candidate in $candidates) {
        if (-not $candidate.Path) { continue }
        # Launchers may mix separators (C:/Games/Blackthorn DAoC)
        $root = [IO.Path]::GetFullPath($candidate.Path).TrimEnd('\')
        if ($seen.ContainsKey($root.ToLowerInvariant()) -or -not (Test-Path -LiteralPath (Join-Path $root 'camelot.exe'))) {
            continue
        }
        $seen[$root.ToLowerInvariant()] = $true
        $server = switch ($candidate.Source) {
            'EdenLauncher' { 'Eden' }
            'BlackthornLauncher' { 'Blackthorn' }
            default {
                if (Test-Path -LiteralPath (Join-Path $root 'btui_game_bridge.dll')) { 'Blackthorn' }
                elseif (Test-Path -LiteralPath (Join-Path $root 'eden.dll')) { 'Eden' }
                else { 'Unknown' }
            }
        }
        [pscustomobject]@{ Path = $root; Server = $server; Source = $candidate.Source }
    }
}

# The first folder of that server, or the first folder at all without -Server; throws when none is found
function Resolve-GameFolder([string]$Server) {
    $found = @(Find-GameFolder | Where-Object { -not $Server -or $_.Server -eq $Server })
    if ($found.Count -eq 0) {
        throw "No $Server DAoC client found (launcher configs, Electronic Arts install folders); pass the game folder explicitly"
    }
    return $found[0].Path
}
