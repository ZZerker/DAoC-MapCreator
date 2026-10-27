# Builds every release file of the public map site (daoc-ui-maps repo) for ONE game client, plus the downloads.json the site reads.
# Inputs (all made by MapCreatorNext.exe; add --game <blackthorn client> for Blackthorn):
#   -Labeled / -Unlabeled: UI map sets, --ui-maps <render dir> --dir <set> --ui-sizes 1024,512,256 (Unlabeled: add --no-labels)
#   -Render: the render folder with zNNN.png at 2048 px
#   -LabeledPng: labeled PNGs, --labels <dir> --label-size 2048 --labels-only --dir <render dir>
#   -Site: the client's --site export folder (zones.json lists the zones of all PNG outputs; downloads.json is copied there)
# Writes into <Target> (release files only, plus downloads.json; work folders are under <Target>\work):
#   <prefix>-<size>-<labels|nolabels>.zip       DDS packs (DXT1, no mipmaps), zip root Maps\; the pack's regions.dat stays next to Maps\
#                                                in <Target>\work\<size>-<kind>
#   <prefix>-png-<2048|1024>-<labels|nolabels>.zip   flat z<id>.png (with -Zip and -Render / -LabeledPng)
#   <prefix>-z<id>.png                          single-zone 2048 PNGs (from -Render)
# prefix is daoc-ui-maps for eden (the config tool depends on these names) and blackthorn-ui-maps for blackthorn.
# Usage Eden: .\build_map_packs.ps1 -Client eden -Labeled Output\ui_maps16 -Unlabeled Output\ui_maps16_nolabels -Render Output\all_maps16 -LabeledPng Output\all_maps16_labeled -Site Output\all_maps16_site -Target Output\release_eden -Tag v1.2.0 -Zip
# Usage Blackthorn: .\build_map_packs.ps1 -Client blackthorn -Unlabeled Output\ui_maps_bt_nolabels -Render Output\blackthorn_all -Site Output\blackthorn_all_site -Target Output\release_blackthorn -Tag v1.2.0-blackthorn -Zip
param(
    [Parameter(Mandatory)][string]$Target,
    [string]$Labeled,
    [string]$Unlabeled,
    [int[]]$Sizes = @(256, 512, 1024),
    [switch]$Zip,
    [ValidateSet('eden', 'blackthorn')][string]$Client = 'eden',
    [string]$Render,
    [string]$LabeledPng,
    [string]$Site,
    [string]$Tag,
    [string]$Repo = 'ZZerker/daoc-ui-maps'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$MAX_TOP_LEVEL_FILES = 1000
$MAX_ZIP_BYTES = 2GB
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not ($Labeled -or $Unlabeled -or $Render)) {
    throw 'Give at least one of -Labeled, -Unlabeled, -Render'
}
if (($Render -or $LabeledPng) -and -not $Site) {
    throw '-Render and -LabeledPng need -Site (zone list)'
}
if ($Site -and -not $Tag) {
    throw '-Tag is needed with -Site (downloads.json)'
}
if ($Tag -and $Tag -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?\z') {
    throw "Tag '$Tag' does not match vX.Y.Z or vX.Y.Z-label"
}

$texconv = Join-Path $PSScriptRoot 'bin\texconv.exe'
if (-not (Test-Path -LiteralPath $texconv)) {
    throw "texconv not found: $texconv (DirectXTex release download)"
}

# .NET calls resolve relative paths against the process directory, so inputs are made absolute
foreach ($name in 'Labeled', 'Unlabeled', 'Render', 'LabeledPng', 'Site') {
    $value = Get-Variable $name -ValueOnly
    if ($value) {
        Set-Variable $name (Resolve-Path -LiteralPath $value).Path
    }
}

$prefix = if ($Client -eq 'blackthorn') { 'blackthorn-ui-maps' } else { 'daoc-ui-maps' }
New-Item -ItemType Directory -Force -Path $Target | Out-Null
$Target = (Resolve-Path -LiteralPath $Target).Path
$work = Join-Path $Target 'work'
$written = [System.Collections.Generic.List[string]]::new()

if ($Site) {
    # a failed run must not leave the old list next to new zips
    $oldDownloads = Join-Path $Target 'downloads.json'
    if (Test-Path -LiteralPath $oldDownloads) {
        Remove-Item -LiteralPath $oldDownloads
    }
}

$zoneIds = @()
if ($Site) {
    $zonesFile = Join-Path $Site 'zones.json'
    if (-not (Test-Path -LiteralPath $zonesFile)) {
        throw "No zones.json in $Site"
    }
    $zoneIds = @((Get-Content $zonesFile -Raw | ConvertFrom-Json).zones | ForEach-Object { [string]$_.id })
    if ($zoneIds.Count -lt 1) {
        throw "No zones in $zonesFile"
    }
}

# Source PNGs of the zone list; a missing one throws or is skipped with a warning
function Get-ZonePngs([string]$folder, [bool]$required) {
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        throw "Missing $folder"
    }
    $found = @()
    foreach ($id in $zoneIds) {
        $png = Join-Path $folder "z$id.png"
        if (Test-Path -LiteralPath $png) {
            $found += $png
        }
        elseif ($required) {
            throw "Missing $png"
        }
        else {
            Write-Warning "Missing $png, zone skipped"
        }
    }
    # the comma keeps an empty or one-element array from being unrolled to $null / a string
    return , $found
}

# Checks the finished temp zip, then moves it to its final name; any failure removes the temp file
function Complete-Zip([string]$tmpFile, [string]$zipFile, [scriptblock]$build) {
    try {
        & $build | Out-Null
        $length = (Get-Item -LiteralPath $tmpFile).Length
        if ($length -ge $MAX_ZIP_BYTES) {
            throw "$zipFile is $length bytes, GitHub release files must stay under 2 GiB"
        }
        Move-Item -LiteralPath $tmpFile -Destination $zipFile -Force
    }
    catch {
        if (Test-Path -LiteralPath $tmpFile) {
            Remove-Item -LiteralPath $tmpFile
        }
        throw
    }
    $written.Add($zipFile)
    return $length
}

# Flat zip of z<id>.png files, stored without compression
function Write-PngZip([string]$zipFile, [string[]]$pngs) {
    $tmpFile = "$zipFile.tmp"
    if (Test-Path -LiteralPath $tmpFile) {
        Remove-Item -LiteralPath $tmpFile
    }
    $length = Complete-Zip $tmpFile $zipFile {
        $archive = [System.IO.Compression.ZipFile]::Open($tmpFile, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($png in $pngs) {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $png, (Split-Path $png -Leaf), [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    Write-Host ("{0}: {1} PNGs, {2:N0} MB" -f (Split-Path $zipFile -Leaf), $pngs.Count, ($length / 1MB))
}

# DDS packs
$sets = [ordered]@{}
if ($Labeled) {
    $sets['labels'] = $Labeled
}
if ($Unlabeled) {
    $sets['nolabels'] = $Unlabeled
}
foreach ($kind in $sets.Keys) {
    foreach ($size in $Sizes) {
        $id = "$size-$kind"
        $pngDir = Join-Path $sets[$kind] $size
        if (-not (Test-Path -LiteralPath $pngDir)) {
            throw "Missing $pngDir"
        }

        $packDir = Join-Path $work $id
        $mapsDir = Join-Path $packDir 'Maps'
        if (Test-Path -LiteralPath $packDir) {
            Remove-Item -LiteralPath $packDir -Recurse
        }
        New-Item -ItemType Directory -Force -Path $mapsDir | Out-Null
        $list = Join-Path $packDir 'files.txt'
        $inputs = @(Get-ChildItem -LiteralPath $pngDir -Filter *.png | ForEach-Object FullName)
        Set-Content -LiteralPath $list $inputs
        $texOutput = & $texconv -nologo -y -f BC1_UNORM -m 1 -bc xu -o $mapsDir -flist $list 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "texconv failed for ${pngDir}:`n$($texOutput -join "`n")"
        }
        Remove-Item -LiteralPath $list
        Copy-Item -LiteralPath (Join-Path $pngDir 'regions.dat') $packDir

        $count = @(Get-ChildItem -LiteralPath $mapsDir -Filter *.dds).Count
        if ($count -ne $inputs.Count) {
            throw "texconv wrote $count DDS for $($inputs.Count) PNGs in ${pngDir}:`n$($texOutput -join "`n")"
        }
        if ($Zip) {
            $zipFile = Join-Path $Target "$prefix-$id.zip"
            $tmpFile = "$zipFile.tmp.zip"
            if (Test-Path -LiteralPath $tmpFile) {
                Remove-Item -LiteralPath $tmpFile
            }
            Complete-Zip $tmpFile $zipFile { Compress-Archive -LiteralPath $mapsDir -DestinationPath $tmpFile } | Out-Null
        }
        Write-Host ("{0}: {1} maps" -f $id, $count)
    }
}

# PNG zips and single-zone PNGs
$pngSources = [ordered]@{}
if ($Render) {
    $pngSources['nolabels'] = Get-ZonePngs $Render $true
}
if ($LabeledPng) {
    $pngSources['labels'] = Get-ZonePngs $LabeledPng $false
}

if ($Render) {
    foreach ($png in $pngSources['nolabels']) {
        $zoneName = (Split-Path $png -Leaf) -replace '^z', "$prefix-z"
        Copy-Item -LiteralPath $png (Join-Path $Target $zoneName) -Force
    }
    Write-Host ("Single-zone PNGs: {0}" -f $pngSources['nolabels'].Count)
}

if ($Zip) {
    foreach ($kind in $pngSources.Keys) {
        $pngs = @($pngSources[$kind])
        if ($pngs.Count -lt 1) {
            Write-Warning "No PNGs for $kind, PNG zips skipped"
            continue
        }
        Write-PngZip (Join-Path $Target "$prefix-png-2048-$kind.zip") $pngs

        $smallDir = Join-Path $work "png-1024-$kind"
        if (Test-Path -LiteralPath $smallDir) {
            Remove-Item -LiteralPath $smallDir -Recurse
        }
        New-Item -ItemType Directory -Force -Path $smallDir | Out-Null
        $list = Join-Path $work "png-1024-$kind.txt"
        Set-Content -LiteralPath $list $pngs
        $texOutput = & $texconv -w 1024 -h 1024 -ft png -m 1 -nologo -y -o $smallDir -flist $list 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "texconv resize failed for ${kind}:`n$($texOutput -join "`n")"
        }
        Remove-Item -LiteralPath $list
        $small = @($pngs | ForEach-Object { Join-Path $smallDir (Split-Path $_ -Leaf) })
        $missing = @($small | Where-Object { -not (Test-Path -LiteralPath $_) })
        if ($missing.Count -gt 0) {
            throw "texconv did not write $($missing.Count) of $($small.Count) PNGs, first $($missing[0]):`n$($texOutput -join "`n")"
        }
        Write-PngZip (Join-Path $Target "$prefix-png-1024-$kind.zip") $small
    }
}

$topLevel = @(Get-ChildItem $Target -File | Where-Object Name -ne 'downloads.json')
if ($topLevel.Count -gt $MAX_TOP_LEVEL_FILES) {
    throw "$Target holds $($topLevel.Count) release files, GitHub allows $MAX_TOP_LEVEL_FILES"
}

# downloads.json
if ($Site) {
    $pattern = '^' + [regex]::Escape($prefix) + '-(?<png>png-)?(?<size>[0-9]+)-(?<kind>labels|nolabels)\.zip\z'
    $entries = @()
    foreach ($zipPath in $written) {
        $file = Get-Item -LiteralPath $zipPath
        if ($file.Name -notmatch $pattern) {
            throw "Unexpected zip name $($file.Name)"
        }
        $isPng = [bool]$Matches['png']
        $isLabels = $Matches['kind'] -eq 'labels'
        $entries += [pscustomobject]@{
            name   = $file.Name
            format = if ($isPng) { 'png' } else { 'dds' }
            size   = [int]$Matches['size']
            labels = $isLabels
            bytes  = $file.Length
        }
    }
    $sorted = @($entries | Sort-Object @{ Expression = { $_.format -eq 'png' } }, size, @{ Expression = { -not $_.labels } })

    $downloads = [ordered]@{
        repo     = $Repo
        tag      = $Tag
        released = Get-Date -Format 'yyyy-MM-dd'
        files    = $sorted
    }
    if ($Render) {
        $downloads['zoneFile'] = "$prefix-z{id}.png"
    }

    $json = $downloads | ConvertTo-Json -Depth 5
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $outFile = Join-Path $Target 'downloads.json'
    [System.IO.File]::WriteAllText($outFile, $json, $utf8)
    Copy-Item -LiteralPath $outFile (Join-Path $Site 'downloads.json') -Force
    Write-Host ("downloads.json: {0} files, tag {1}" -f $sorted.Count, $Tag)
}
