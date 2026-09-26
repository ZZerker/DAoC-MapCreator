# Copies the rendered outdoor maps into the Eden launcher. Run as admin.
# The originals are saved once to Output\launcher_backup_all. Restore: .\deploy_launcher_maps.ps1 -Restore
param(
    [string]$Source = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Output\launcher_outdoor'),
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
$zones = 'C:\Program Files\Eden Launcher\resources\app.asar.unpacked\out\renderer\zones'
$backup = Join-Path (Split-Path $PSScriptRoot -Parent) 'Output\launcher_backup_all'

if ($Restore) {
    Copy-Item (Join-Path $backup '*.jpg') $zones -Force
    Write-Host 'Original launcher maps restored'
    return
}

New-Item -ItemType Directory -Force $backup | Out-Null
$files = Get-ChildItem $Source -Filter 'zone*.jpg'
foreach ($file in $files) {
    $target = Join-Path $zones $file.Name
    $saved = Join-Path $backup $file.Name
    if ((Test-Path $target) -and -not (Test-Path $saved)) {
        Copy-Item $target $saved
    }
    Copy-Item $file.FullName $target -Force
}
Write-Host "$($files.Count) maps copied to $zones"
