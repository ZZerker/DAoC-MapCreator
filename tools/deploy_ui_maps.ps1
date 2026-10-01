# Puts a UI map set (MapCreatorNext.exe --ui-maps <render dir> --dir <set>) into the TokaZerk UI repo.
# <set>\512 goes to Maps and Maps_large, <set>\256 to Maps_small, as DXT1 DDS without mipmaps (texconv). Every zNNN*.dds and rNNN.dds
# in those folders is replaced, so only our maps remain; areas.dat and regions.dat come from the set. UI assets (map_gitter.dds,
# no_map.dds, Warmap\) stay. Nothing is committed.
# Usage: .\deploy_ui_maps.ps1 -Source D:\PrivatProjects\DAoC-MapCreator\Output\ui_maps8 [-Repo D:\PrivatProjects\tokajerui.git]
param(
    [Parameter(Mandatory)][string]$Source,
    [string]$Repo = 'D:\PrivatProjects\tokajerui.git'
)

$ErrorActionPreference = 'Stop'
$texconv = Join-Path $PSScriptRoot 'bin\texconv.exe'
if (-not (Test-Path $texconv)) {
    throw "texconv not found: $texconv (DirectXTex release download)"
}

$folders = [ordered]@{ 'Maps' = 512; 'Maps_large' = 512; 'Maps_small' = 256 }
$converted = @{}
foreach ($size in ($folders.Values | Sort-Object -Unique)) {
    $pngDir = Join-Path $Source $size
    $ddsDir = Join-Path $Source "dds_$size"
    if (-not (Test-Path $pngDir)) {
        throw "Missing $pngDir"
    }
    New-Item -ItemType Directory -Force $ddsDir | Out-Null
    Get-ChildItem $ddsDir -Filter *.dds | Remove-Item
    $list = Join-Path $ddsDir 'files.txt'
    Get-ChildItem $pngDir -Filter *.png | ForEach-Object FullName | Set-Content $list
    & $texconv -nologo -y -f BC1_UNORM -m 1 -bc xu -o $ddsDir -flist $list | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "texconv failed for $pngDir"
    }
    $converted[$size] = $ddsDir
    Write-Host ("{0} px: {1} DDS" -f $size, (Get-ChildItem $ddsDir -Filter *.dds).Count)
}

foreach ($folder in $folders.Keys) {
    $target = Join-Path $Repo $folder
    $size = $folders[$folder]
    $old = Get-ChildItem $target -File | Where-Object { $_.Name -match '^(z\d+.*|r\d{3})\.dds$' }
    $old | Remove-Item
    Copy-Item (Join-Path $converted[$size] '*.dds') $target
    Copy-Item (Join-Path $Source "$size\areas.dat"), (Join-Path $Source "$size\regions.dat") $target -Force
    Write-Host ("{0}: {1} old maps removed, {2} written" -f $folder, $old.Count, (Get-ChildItem $converted[$size] -Filter *.dds).Count)
}
