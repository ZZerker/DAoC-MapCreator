# Puts rendered maps into the Eden launcher. Run as admin.
# zNNN.png becomes zoneNNN.jpg; each zNNN.frame.json (cities, dungeons) sets the zone's crop in zones.json,
# without one the launcher places the marker as if the map showed the whole zone, so those are skipped.
# The originals are saved once to Output\launcher_backup_all. Restore: .\deploy_launcher_maps.ps1 -Restore
# Usage: .\deploy_launcher_maps.ps1 -Source D:\PrivatProjects\DAoC-MapCreator\Output\all_maps5 [-Zones <launcher zones folder>]
param(
    [string]$Source,
    [string]$Zones = 'C:\Program Files\Eden Launcher\resources\app.asar.unpacked\out\renderer\zones',
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$backup = Join-Path (Split-Path $PSScriptRoot -Parent) 'Output\launcher_backup_all'
$zonesJson = Join-Path $Zones 'zones.json'
$savedJson = Join-Path $backup 'zones.json'

if ($Restore) {
    Copy-Item (Join-Path $backup '*.jpg') $Zones -Force
    if (Test-Path $savedJson) {
        Copy-Item $savedJson $zonesJson -Force
    }
    Write-Host 'Original launcher maps restored'
    return
}

if (-not $Source) {
    throw 'Give the render folder with -Source'
}

New-Item -ItemType Directory -Force $backup | Out-Null
$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$quality = New-Object System.Drawing.Imaging.EncoderParameters(1)
$quality.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]90)

$table = Get-Content $zonesJson -Raw | ConvertFrom-Json
$ids = [System.Collections.Generic.List[int]]$table.ids
$copied = 0
$framed = 0
$skipped = @()
foreach ($file in Get-ChildItem $Source -File) {
    if ($file.Name -notmatch '^z(\d{3})\.png$') {
        continue
    }
    $zone = $Matches[1]
    $target = Join-Path $Zones "zone$zone.jpg"
    if (-not (Test-Path $target)) {
        continue
    }

    # Outdoor maps show the whole zone (the launcher renders them itself, with a .meta.json)
    $frameFile = Join-Path $Source "z$zone.frame.json"
    $hasFrame = Test-Path $frameFile
    if (-not $hasFrame -and -not (Test-Path "$target.meta.json")) {
        $skipped += $zone
        continue
    }

    $saved = Join-Path $backup "zone$zone.jpg"
    if (-not (Test-Path $saved)) {
        Copy-Item $target $saved
    }

    $image = [System.Drawing.Image]::FromFile($file.FullName)
    try {
        $image.Save($target, $jpeg, $quality)
    }
    finally {
        $image.Dispose()
    }
    $copied++

    $index = $ids.IndexOf([int]$zone)
    if ($hasFrame -and $index -ge 0) {
        $frame = Get-Content $frameFile -Raw | ConvertFrom-Json
        $table.cropOffsetXs[$index] = [int]$frame.offsetX
        $table.cropOffsetYs[$index] = [int]$frame.offsetY
        $table.cropWidths[$index] = [int]$frame.width
        $table.cropHeights[$index] = [int]$frame.width
        $framed++
    }
}

if ($framed -gt 0) {
    if (-not (Test-Path $savedJson)) {
        Copy-Item $zonesJson $savedJson
    }
    [System.IO.File]::WriteAllText($zonesJson, ($table | ConvertTo-Json -Depth 5 -Compress), (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host "$copied maps copied to $Zones, $framed frames set in zones.json"
if ($skipped.Count -gt 0) {
    Write-Host "$($skipped.Count) cities or dungeons skipped, no frame file: $($skipped -join ' ')"
}
