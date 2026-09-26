# Renders zones (default: all New Frontiers outdoor zones) and converts them to DXT1 DDS maps (zNNN.dds).
# Usage: .\render_nf.ps1 [-Size 2048] [-DdsSize 2048] [-Parallel 4] [-Zones 163,171] [-Areas <UI>\Maps\areas.dat] [-AreaSize 256] [-Png]
# Zones take ids or groups like nf+outdoor. -Areas also writes the zoomed area maps zNNN_AA.dds (New Frontiers mazes).
# -Png writes a PNG next to every DDS for viewing.
param(
    [int]$Size = 2048,
    [int]$DdsSize = 0,
    [int]$Parallel = 4,
    [string[]]$Zones = @('nf+outdoor'),
    [string]$Areas = '',
    [int]$AreaSize = 256,
    [switch]$Png
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'Releases\MapCreator.exe'

# Every exe location (Release, Visual Studio debug) gets its own user.config
$targetMapPath = Get-ChildItem "$env:LOCALAPPDATA\MapCreator" -Recurse -Filter user.config |
    Sort-Object LastWriteTime -Descending |
    ForEach-Object { ([xml](Get-Content $_.FullName -Raw)).SelectSingleNode("//setting[@name='targetMapPath']/value").InnerText } |
    Where-Object { $_ } |
    Select-Object -First 1
if (-not $targetMapPath) {
    $targetMapPath = Split-Path $exe -Parent
}

$renderDir = Join-Path $targetMapPath "nf_$Size"
$ddsDir = Join-Path $targetMapPath "nf_$Size`_dds"

$zoneList = $Zones -join ','
$log = Join-Path $targetMapPath 'render.log'
Write-Host "Rendering $zoneList at $Size px, $Parallel zones at a time"

$started = Get-Date
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -ArgumentList '--render', $zoneList, '--size', $Size, '--dir', "nf_$Size", '--log', 'render.log', '--parallel', $Parallel -Wait
Write-Host ("Rendering took {0:mm\:ss}" -f ((Get-Date) - $started))

Select-String -Path $log -Pattern '^\S+ error' | ForEach-Object { Write-Warning $_.Line }

$pythonArgs = @((Join-Path $PSScriptRoot 'png_to_dds.py'), $renderDir, $ddsDir)
if ($DdsSize -gt 0) {
    $pythonArgs += $DdsSize
}
if ($Areas) {
    $pythonArgs += '--areas', $Areas, '--area-size', $AreaSize
}
if ($Png) {
    $pythonArgs += '--png'
}
python @pythonArgs
Write-Host "DDS maps: $ddsDir"
