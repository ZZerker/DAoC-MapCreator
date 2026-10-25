# Publishes the public map site of the daoc-ui-maps repo to GitHub Pages (branch gh-pages, root) without GitHub Actions.
# The static pages ($Site), the Eden and Blackthorn folders written by MapCreatorNext.exe --site and an empty .nojekyll are
# copied to Output\site_publish, which becomes a throwaway repo with ONE parentless commit that is force-pushed, so the
# images never pile up in git history. With -Assets and -Tag the release packs are uploaded first (created or updated).
# -NoPush builds the commit locally and stops. The working tree and branches of -Repo are never touched (only its origin URL is read).
# Usage: .\publish_site.ps1 -Eden D:\PrivatProjects\DAoC-MapCreator\Output\site_eden -Blackthorn D:\PrivatProjects\DAoC-MapCreator\Output\site_blackthorn [-Assets D:\PrivatProjects\DAoC-MapCreator\Output\packs -Tag v1.0.0] [-NoPush]
param(
    [Parameter(Mandatory)][string]$Eden,
    [Parameter(Mandatory)][string]$Blackthorn,
    [string]$Repo = 'D:\PrivatProjects\daoc-ui-maps',
    [string]$Site,
    [string]$Assets,
    [string]$Tag,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
# robocopy exits 1 on success and gh release view 1 on a missing release; exit codes are checked by hand
$PSNativeCommandUseErrorActionPreference = $false
$MAX_SITE_BYTES = 1073741824
$MAX_ASSET_FILES = 1000
$MAX_ASSET_BYTES = 2GB
$BATCH_SIZE = 50

if (-not $Site) {
    $Site = Join-Path $Repo 'site'
}
if (-not (Test-Path (Join-Path $Site 'index.html'))) {
    throw "No index.html in $Site"
}
foreach ($folder in $Eden, $Blackthorn) {
    if (-not (Test-Path (Join-Path $folder 'zones.json'))) {
        throw "No zones.json in $folder"
    }
}

$files = @()
if ($Assets -or $Tag) {
    if (-not ($Assets -and $Tag)) {
        throw '-Assets and -Tag must be given together'
    }
    if ($Tag -cnotmatch'^v\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
        throw "Tag '$Tag' does not match vX.Y.Z or vX.Y.Z-label"
    }
    if (-not (Test-Path $Assets -PathType Container)) {
        throw "Assets folder not found: $Assets"
    }
    $files = @(Get-ChildItem $Assets -File)
    if ($files.Count -lt 1 -or $files.Count -gt $MAX_ASSET_FILES) {
        throw "Assets folder must hold 1 to $MAX_ASSET_FILES files, found $($files.Count)"
    }
    $tooBig = $files | Where-Object { $_.Length -ge $MAX_ASSET_BYTES }
    if ($tooBig) {
        throw "Release files must be under 2 GiB: $($tooBig.FullName -join ', ')"
    }
}

$remote = (git -C $Repo remote get-url origin).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Cannot read origin of $Repo"
}
if ($remote -notmatch '[:/]([^/:]+)/([^/]+?)(\.git)?/?$') {
    throw "Cannot derive owner/name from $remote"
}
$owner = $Matches[1]
$name = $Matches[2]
$slug = "$owner/$name"

$staging = 'D:\PrivatProjects\DAoC-MapCreator\Output\site_publish'
if (Test-Path $staging) {
    if ((Get-Item $staging -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to delete $staging, it is a link"
    }
    Remove-Item $staging -Recurse -Force
}
New-Item -ItemType Directory -Force $staging | Out-Null

$copies = [ordered]@{ $Site = $staging; $Eden = (Join-Path $staging 'data\eden'); $Blackthorn = (Join-Path $staging 'data\blackthorn') }
foreach ($source in $copies.Keys) {
    robocopy $source $copies[$source] /E /XD .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed ($LASTEXITCODE) for $source"
    }
}
New-Item -ItemType File (Join-Path $staging '.nojekyll') | Out-Null

$bytes = (Get-ChildItem $staging -Recurse -File -Force | Measure-Object Length -Sum).Sum
Write-Host ("Site size: {0:N1} MB" -f ($bytes / 1MB))
if ($bytes -gt $MAX_SITE_BYTES) {
    throw "Site is $bytes bytes, over the 1 GB GitHub Pages limit"
}

git -C $staging init -q -b gh-pages
if ($LASTEXITCODE -ne 0) {
    throw 'git init failed'
}
git -C $staging config core.autocrlf false
if ($LASTEXITCODE -ne 0) {
    throw 'git config failed'
}
git -C $staging add --all
if ($LASTEXITCODE -ne 0) {
    throw 'git add failed'
}
git -C $staging commit -q -m ("Publish site {0:yyyy-MM-dd HH:mm}" -f (Get-Date))
if ($LASTEXITCODE -ne 0) {
    throw 'git commit failed'
}

if ($NoPush) {
    Write-Host "Commit built in $staging, nothing pushed, nothing uploaded."
    exit 0
}

if ($files.Count -gt 0) {
    $paths = @($files | ForEach-Object FullName)
    # @() keeps a single batch as one element instead of unrolling it into its paths
    $batches = @(for ($i = 0; $i -lt $paths.Count; $i += $BATCH_SIZE) {
        , @($paths[$i..([Math]::Min($i + $BATCH_SIZE, $paths.Count) - 1)])
    })
    gh release view $Tag -R $slug *> $null
    if ($LASTEXITCODE -eq 0) {
        foreach ($batch in $batches) {
            gh release upload $Tag @batch --clobber -R $slug
            if ($LASTEXITCODE -ne 0) {
                throw "gh release upload failed for $Tag"
            }
        }
    }
    else {
        gh release create $Tag @($batches[0]) --title "DAoC UI maps $Tag" --notes 'Map packs rendered with DAoC MapCreator.' -R $slug
        if ($LASTEXITCODE -ne 0) {
            throw "gh release create failed for $Tag"
        }
        foreach ($batch in $batches | Select-Object -Skip 1) {
            gh release upload $Tag @batch --clobber -R $slug
            if ($LASTEXITCODE -ne 0) {
                throw "gh release upload failed for $Tag"
            }
        }
    }
    Write-Host ("Release {0}: {1} files" -f $Tag, $files.Count)
}

git -C $staging push --force $remote gh-pages:gh-pages
if ($LASTEXITCODE -ne 0) {
    throw 'git push failed'
}
Write-Host ("Published: https://{0}.github.io/{1}/" -f $owner.ToLowerInvariant(), $name)
