<#
.SYNOPSIS
    Stage a local FEBuilderGBA release folder from already-built outputs.

.DESCRIPTION
    Collects the WinForms application (FEBuilderGBA.exe + DLLs + config + docs)
    into a single output folder so it can be zipped and attached to a GitHub
    release. When CLI / Avalonia publish output is present it is staged too
    (best-effort), so a single local run can mirror the full-suite asset set
    that the planned tag-triggered release workflow (#1629) would produce.

    This script is part of the CURRENT live release flow: the release process
    is manual today (build -> stage with this script -> `gh release create`),
    as documented in docs/RELEASE.md. A tag-triggered CI workflow that would
    build and attach every platform artifact on a `ver_YYYYMMDD.NN` tag push is
    PLANNED but NOT yet merged (tracked by issue #1629); until it lands, the
    manual/script path here is the source of truth.

    Repo-relative paths are resolved against the script's own location
    ($PSScriptRoot), so the script works the same regardless of the current
    working directory, and all paths are built with Join-Path so it runs on
    Windows, Linux and macOS under `pwsh`.

.PARAMETER OutputDir
    Destination folder to stage the release into. Created if missing.
    Relative paths are resolved against the current working directory.
    Default: "release" (under the repo root).

.PARAMETER WinFormsBinDir
    Folder containing the built WinForms output (FEBuilderGBA.exe + DLLs).
    Relative paths are resolved against the repo root.
    Default: FEBuilderGBA/bin/Release.

.PARAMETER ConfigDir
    Folder containing the runtime `config` directory to copy.
    Default: the WinForms build output's `config`, falling back to the
    repo-root `config` if the build output has none.

.PARAMETER Clean
    Remove the output folder before staging (fresh build). Off by default so
    re-runs are idempotent additive copies.

.EXAMPLE
    pwsh ./release.ps1
    # Stages into ./release using the Release build output.

.EXAMPLE
    pwsh ./release.ps1 -OutputDir dist -Clean
    # Fresh staging into ./dist.

.NOTES
    See docs/RELEASE.md for the full-suite release runbook and
    docs/DEPLOYMENT.md for the split-package (.7z) update system.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "release",
    [string]$WinFormsBinDir = "",
    [string]$ConfigDir = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

# Resolve the repo root from the script location so paths are independent of
# the current working directory.
$repoRoot = $PSScriptRoot
if ([string]::IsNullOrEmpty($repoRoot)) { $repoRoot = (Get-Location).Path }

function New-DirIfMissing([string]$path) {
    if (-not (Test-Path $path)) {
        $null = New-Item -ItemType Directory -Path $path -Force
    }
}

# Default the WinForms bin dir to <repo>/FEBuilderGBA/bin/Release, built with
# Join-Path so it is correct on every OS.
if ([string]::IsNullOrEmpty($WinFormsBinDir)) {
    $WinFormsBinDir = Join-Path (Join-Path (Join-Path $repoRoot "FEBuilderGBA") "bin") "Release"
}

# --- Resolve / prepare the output folder -----------------------------------
if ($Clean -and (Test-Path $OutputDir)) {
    Write-Host "Cleaning existing output folder: $OutputDir"
    Remove-Item -Path $OutputDir -Recurse -Force
}

$outConfig = Join-Path $OutputDir "config"
New-DirIfMissing $OutputDir
New-DirIfMissing $outConfig
New-DirIfMissing (Join-Path $outConfig "etc")
New-DirIfMissing (Join-Path $outConfig "log")

# --- Stage the WinForms application ----------------------------------------
if (-not (Test-Path $WinFormsBinDir)) {
    throw "WinForms build output not found at '$WinFormsBinDir'. Build the solution first (Release/x86) — see docs/RELEASE.md."
}

$exe = Join-Path $WinFormsBinDir "FEBuilderGBA.exe"
if (Test-Path $exe) {
    Copy-Item -Force $exe $OutputDir
} else {
    Write-Warning "FEBuilderGBA.exe not found in '$WinFormsBinDir' — skipping exe."
}

foreach ($pattern in @("*.dll", "*.json")) {
    $files = Get-ChildItem -Path $WinFormsBinDir -Filter $pattern -ErrorAction SilentlyContinue
    if ($files) {
        Copy-Item -Force (Join-Path $WinFormsBinDir $pattern) $OutputDir
    }
}

# --- Stage the config directory --------------------------------------------
# Prefer the build-output config (Release, not Debug). Fall back to the
# repo-root config (resolved against the script location, not the CWD).
if ([string]::IsNullOrEmpty($ConfigDir)) {
    $candidate = Join-Path $WinFormsBinDir "config"
    $repoConfig = Join-Path $repoRoot "config"
    if (Test-Path $candidate) {
        $ConfigDir = $candidate
    } elseif (Test-Path $repoConfig) {
        $ConfigDir = $repoConfig
    }
}

if ($ConfigDir -and (Test-Path $ConfigDir)) {
    # Copy config contents, leaving the staged etc/ and log/ folders intact.
    Copy-Item -Force -Recurse (Join-Path $ConfigDir "*") $outConfig -Exclude @("etc", "log")
} else {
    Write-Warning "No config directory found to stage (looked in build output and repo root)."
}

# --- Stage docs + license --------------------------------------------------
# Copy the repo-root docs (resolved against the script location).
Copy-Item -Force (Join-Path $repoRoot "*.md") $OutputDir -ErrorAction SilentlyContinue

# GPLv3 (sections 4-6) requires the license text to accompany every binary
# distribution. The LICENSE file is extensionless, so the "*.md" glob above
# does NOT catch it -- copy it explicitly. THIRD-PARTY-NOTICES.md is already
# matched by the "*.md" glob.
$licenseFile = Join-Path $repoRoot "LICENSE"
if (Test-Path $licenseFile) {
    Copy-Item -Force $licenseFile $OutputDir
} else {
    Write-Warning "LICENSE file not found at repo root -- release artifact will be missing required GPLv3 license text."
}

# --- Best-effort: stage CLI / Avalonia publish output if present -----------
# These come from `dotnet publish ... -o publish/cli-{rid}` /
# `publish/avalonia-{rid}` (same layout the crossplatform.yml workflow and
# scripts/publish-all.sh use). Staged under <OutputDir>/<name> so a single
# local run can mirror the full-suite asset set without overwriting the
# WinForms payload. Globs are rooted at the repo so they resolve on any OS.
$publishRoot = Join-Path $repoRoot "publish"
foreach ($prefix in @("cli-", "avalonia-")) {
    $glob = Join-Path $publishRoot ($prefix + "*")
    $bundles = Get-ChildItem -Path $glob -Directory -ErrorAction SilentlyContinue
    foreach ($dir in $bundles) {
        $dest = Join-Path $OutputDir $dir.Name
        Write-Host "Staging published bundle: $($dir.Name)"
        New-DirIfMissing $dest
        Copy-Item -Force -Recurse (Join-Path $dir.FullName "*") $dest
    }
}

Write-Host "Release staged into '$OutputDir'."
Write-Host "Next: zip per platform and attach to the GitHub release (see docs/RELEASE.md)."
