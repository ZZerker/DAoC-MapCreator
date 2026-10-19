# Builds the TokaZerk map packs of the config tool (TokaZerkUIConfig PLANS.md "Map packs as downloads") from two UI map sets
# (MapCreatorNext.exe --ui-maps <render dir> --dir <set> --ui-sizes 1024,512,256, once more with --no-labels --dir <set>_nolabels).
# Writes <Target>\<size>-<labels|nolabels>\Maps\*.dds (DXT1, no mipmaps, map files only) and next to Maps\ the pack's regions.dat,
# which the contract leaves out of the pack although its pixel values depend on the size. -Zip also writes daoc-ui-maps-<id>.zip
# with the root Maps\.
# Usage: .\build_map_packs.ps1 -Labeled <Output\ui_maps11> -Unlabeled <Output\ui_maps11_nolabels> -Target <folder> [-Sizes 256,512,1024] [-Zip]
param(
    [Parameter(Mandatory)][string]$Labeled,
    [Parameter(Mandatory)][string]$Unlabeled,
    [Parameter(Mandatory)][string]$Target,
    [int[]]$Sizes = @(256, 512, 1024),
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$texconv = Join-Path $PSScriptRoot 'bin\texconv.exe'
if (-not (Test-Path $texconv)) {
    throw "texconv not found: $texconv (DirectXTex release download)"
}

$sets = [ordered]@{ 'labels' = $Labeled; 'nolabels' = $Unlabeled }
foreach ($kind in $sets.Keys) {
    foreach ($size in $Sizes) {
        $id = "$size-$kind"
        $pngDir = Join-Path $sets[$kind] $size
        if (-not (Test-Path $pngDir)) {
            throw "Missing $pngDir"
        }

        $packDir = Join-Path $Target $id
        $mapsDir = Join-Path $packDir 'Maps'
        if (Test-Path $packDir) {
            Remove-Item $packDir -Recurse
        }
        New-Item -ItemType Directory -Force $mapsDir | Out-Null
        $list = Join-Path $packDir 'files.txt'
        Get-ChildItem $pngDir -Filter *.png | ForEach-Object FullName | Set-Content $list
        & $texconv -nologo -y -f BC1_UNORM -m 1 -bc xu -o $mapsDir -flist $list | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "texconv failed for $pngDir"
        }
        Remove-Item $list
        Copy-Item (Join-Path $pngDir 'regions.dat') $packDir

        $count = (Get-ChildItem $mapsDir -Filter *.dds).Count
        if ($Zip) {
            $zipFile = Join-Path $Target "daoc-ui-maps-$id.zip"
            Compress-Archive -Path $mapsDir -DestinationPath $zipFile -Force
        }
        Write-Host ("{0}: {1} maps" -f $id, $count)
    }
}
