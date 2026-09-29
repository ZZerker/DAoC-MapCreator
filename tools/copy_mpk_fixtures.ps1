param([string]$GamePath = 'C:\Spiele\Eden DAoC')

$ErrorActionPreference = 'Stop'

Add-Type -Path (Join-Path $PSScriptRoot '..\libs\MPKLib.dll')

$testDataDir = Join-Path $PSScriptRoot '..\Mpk.Tests\TestData'
New-Item -ItemType Directory -Force -Path $testDataDir | Out-Null

$relativePaths = @(
    'frontiers\zones\zone163\csv163.mpk',
    'frontiers\zones\zone163\dat163.mpk',
    'frontiers\zones\zone163\tex163.mpk',
    'frontiers\zones\zone163\lod163.mpk',
    'frontiers\zones\zone163\ter163.mpk',
    'zones\zone060\dat060.mpk',
    'zones\zones.mpk',
    'zones\trees\treemap.mpk',
    'zones\trees\tree_clusters.mpk',
    'frontiers\NIFS\frontiers.mpk',
    'data\loginc.mpk'
)

$archiveManifests = @()

foreach ($relativePath in $relativePaths) {
    $sourcePath = Join-Path $GamePath $relativePath
    $destPath = Join-Path $testDataDir $relativePath
    $destDir = Split-Path $destPath -Parent
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    Copy-Item -Path $sourcePath -Destination $destPath -Force

    $mpak = New-Object MPKLib.MPAK
    $stream = New-Object System.IO.FileStream($destPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $loadResult = $mpak.Load($stream)
    }
    finally {
        $stream.Dispose()
    }

    if ($loadResult -ne [MPKLib.eMPAKError]::None) {
        throw "Load of $destPath returned $loadResult"
    }

    $entryManifests = @()
    foreach ($entry in $mpak.Files) {
        $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($entry.Data)).ToLowerInvariant()
        $entryManifests += [PSCustomObject]@{
            name       = $entry.Name
            timestamp  = $entry.Timestamp
            size       = $entry.FileSize
            dataLength = $entry.Data.Length
            sha256     = $hash
        }
    }

    $archiveManifests += [PSCustomObject]@{
        path        = $relativePath
        archiveName = $mpak.ArchiveName
        entries     = [object[]]$entryManifests
    }

    Write-Host "$relativePath : $($mpak.ArchiveName), $($entryManifests.Count) entries"
}

$manifest = [PSCustomObject]@{
    archives = [object[]]$archiveManifests
}

$manifestPath = Join-Path $testDataDir 'manifest.json'
$manifestJson = ConvertTo-Json -InputObject $manifest -Depth 6
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Manifest written to $manifestPath"
