# Builds Release, renders every zone (or -Zones, ids or groups like nf+outdoor), converts the NF zones to DDS, then shuts the computer down.
# Usage: .\render_all_shutdown.ps1 [-Zones all] [-Size 2048] [-Parallel 4] [-Dir all_maps] [-NoShutdown]
# Cancel a pending shutdown with: shutdown /a
param(
    [string]$Zones = 'all',
    [int]$Size = 2048,
    [int]$Parallel = 4,
    [string]$Dir = 'all_maps',
    [int]$ShutdownDelay = 300,
    [switch]$NoShutdown
)

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'Releases\MapCreator.exe'
$output = Join-Path $root 'Output'
$transcript = Join-Path $output "$Dir`_run.log"
Start-Transcript -Path $transcript -Force | Out-Null

try {
    if (Get-Process MapCreator -ErrorAction SilentlyContinue) {
        throw 'MapCreator is running and locks Releases. Close it first.'
    }

    dotnet build (Join-Path $root 'MapCreator.sln') -c Release
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed'
    }

    Write-Host "Rendering $Zones at $Size px, $Parallel at a time"
    $started = Get-Date
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -ArgumentList '--render', $Zones, '--size', $Size, '--dir', $Dir, '--log', "$Dir.log", '--parallel', $Parallel -Wait
    Write-Host ("Rendering took {0:hh\:mm\:ss}" -f ((Get-Date) - $started))
    Select-String -Path (Join-Path $output "$Dir.log") -Pattern '^\S+ error' | ForEach-Object { Write-Warning $_.Line }

    & (Join-Path $PSScriptRoot 'render_nf.ps1') -Size $Size -Parallel $Parallel
}
catch {
    Write-Warning $_
}
finally {
    Stop-Transcript | Out-Null
    if (-not $NoShutdown) {
        shutdown /s /t $ShutdownDelay /c "Map rendering finished, shutting down. Cancel with: shutdown /a"
    }
}
