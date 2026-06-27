# SPDX-License-Identifier: GPL-3.0-or-later
#
# fetch-fe-repo.ps1 — populate the FE-Repo resource folders for the in-app
# Resource Browser (#1644). PowerShell counterpart of scripts/fetch-fe-repo.sh.
#
# The FE-Repo (graphics) and FE-Repo-Music-No-Preview (music) repositories are
# wired into FEBuilderGBA as git submodules but are intentionally NOT bundled
# into released artifacts (their payload is too large to attach to every
# release). This helper fetches them on demand into the `resources/` folder the
# Resource Browser searches, so the browser is no longer empty.
#
# It works in two situations:
#   * Inside a source clone — initializes the submodules in place.
#   * Next to an extracted release .zip (no git repo / no submodule) — shallow
#     `git clone`s the public repos into `resources/` next to the executable.
#
# Idempotent: a folder that is already populated is left untouched.
#
# Usage:
#   pwsh scripts/fetch-fe-repo.ps1 [-GraphicsOnly] [-MusicOnly] [-Dest <dir>]
#
# Requires: git on PATH.
[CmdletBinding()]
param(
    [switch]$GraphicsOnly,
    [switch]$MusicOnly,
    [string]$Dest = ""
)

$ErrorActionPreference = 'Stop'

$GraphicsUrl  = 'https://github.com/Klokinator/FE-Repo'
$MusicUrl     = 'https://github.com/laqieer/FE-Repo-Music-No-Preview'
$GraphicsPath = 'resources/FE-Repo'
$MusicPath    = 'resources/FE-Repo-Music-No-Preview'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error 'fetch-fe-repo: git is required but was not found on PATH.'
    exit 1
}

# Resolve the destination root: explicit -Dest, else the enclosing git working
# tree (a source clone), else the current directory (a release zip).
if ([string]::IsNullOrEmpty($Dest)) {
    $top = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($top)) {
        $Dest = $top.Trim()
    } else {
        $Dest = (Get-Location).Path
    }
}

function Test-Populated([string]$path) {
    # Populated == directory exists and has at least one child entry (matches
    # FERepoResourceBrowser's empty-placeholder-as-not-found rule).
    return (Test-Path -LiteralPath $path) -and `
        ((Get-ChildItem -LiteralPath $path -Force -ErrorAction SilentlyContinue | Select-Object -First 1) -ne $null)
}

function Invoke-FetchOne([string]$name, [string]$url, [string]$rel) {
    $target = Join-Path $Dest $rel
    if (Test-Populated $target) {
        Write-Host "fetch-fe-repo: $name already populated at $target - skipping."
        return
    }
    Write-Host "fetch-fe-repo: fetching $name into $target ..."

    $isGitTree = $false
    & git -C $Dest rev-parse --git-dir > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        & git -C $Dest config --file .gitmodules --get "submodule.$rel.url" > $null 2>&1
        if ($LASTEXITCODE -eq 0) { $isGitTree = $true }
    }

    if ($isGitTree) {
        # Source clone: init the registered submodule in place.
        & git -C $Dest submodule update --init --depth 1 $rel
    } else {
        # Released zip / non-git tree: shallow clone the public repo directly.
        $parent = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        & git clone --depth 1 $url $target
    }
    if ($LASTEXITCODE -ne 0) { Write-Error "fetch-fe-repo: failed to fetch $name."; exit 1 }
    Write-Host "fetch-fe-repo: $name ready."
}

$doGraphics = -not $MusicOnly
$doMusic    = -not $GraphicsOnly

if ($doGraphics) { Invoke-FetchOne 'FE-Repo (graphics)' $GraphicsUrl $GraphicsPath }
if ($doMusic)    { Invoke-FetchOne 'FE-Repo-Music (music)' $MusicUrl $MusicPath }

Write-Host 'fetch-fe-repo: done. Open the FE-Repo Resource Browser to browse the assets.'
