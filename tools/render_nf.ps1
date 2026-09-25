# Renders zones (default: all New Frontiers outdoor zones) and converts them to DXT1 DDS maps (zNNN.dds).
# Usage: .\render_nf.ps1 [-Size 2048] [-DdsSize 2048] [-Parallel 4] [-Zones 163,171]
param(
    [int]$Size = 2048,
    [int]$DdsSize = 0,
    [int]$Parallel = 4,
    [string[]]$Zones = @('163', '164', '167', '168', '169', '170', '171', '172', '173', '174', '175', '176', '177', '178')
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

# Round-robin so the big ocean zones do not all land in one process
$Parallel = [Math]::Max(1, [Math]::Min($Parallel, $Zones.Count))
$groups = @(for ($i = 0; $i -lt $Parallel; $i++) { , @() })
for ($i = 0; $i -lt $Zones.Count; $i++) {
    $groups[$i % $Parallel] += $Zones[$i]
}

$started = Get-Date
$processes = @()
$logs = @()
for ($i = 0; $i -lt $Parallel; $i++) {
    $zoneList = $groups[$i] -join ','
    $logName = "render_$i.log"
    $logs += Join-Path $targetMapPath $logName
    Write-Host "Process $i renders $zoneList at $Size px"
    $processes += Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -ArgumentList '--render', $zoneList, '--size', $Size, '--dir', "nf_$Size", '--log', $logName -PassThru
}
$processes | ForEach-Object { $_.WaitForExit() }
Write-Host ("Rendering took {0:mm\:ss}" -f ((Get-Date) - $started))

foreach ($log in $logs) {
    Select-String -Path $log -Pattern '^\S+ error' | ForEach-Object { Write-Warning "$(Split-Path $log -Leaf): $($_.Line)" }
}

$pythonArgs = @((Join-Path $PSScriptRoot 'png_to_dds.py'), $renderDir, $ddsDir)
if ($DdsSize -gt 0) {
    $pythonArgs += $DdsSize
}
python @pythonArgs
Write-Host "DDS maps: $ddsDir"
