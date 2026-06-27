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

.PARAMETER OutputDir
    Destination folder to stage the release into. Created if missing.
    Default: "release".

.PARAMETER WinFormsBinDir
    Folder containing the built WinForms output (FEBuilderGBA.exe + DLLs).
    Default: "FEBuilderGBA\bin\Release".

.PARAMETER ConfigDir
    Folder containing the runtime `config` directory to copy.
    Default: "FEBuilderGBA\bin\Release\config" with a fallback to the
    repo-root "config" if the build output has none.

.PARAMETER Clean
    Remove the output folder before staging (fresh build). Off by default so
    re-runs are idempotent additive copies.

.EXAMPLE
    pwsh ./release.ps1
    # Stages into .\release using the Release build output.

.EXAMPLE
    pwsh ./release.ps1 -OutputDir dist -Clean
    # Fresh staging into .\dist.

.NOTES
    See docs/RELEASE.md for the full-suite release runbook and
    docs/DEPLOYMENT.md for the split-package (.7z) update system.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "release",
    [string]$WinFormsBinDir = "FEBuilderGBA\bin\Release",
    [string]$ConfigDir = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

function New-DirIfMissing([string]$path) {
    if (-not (Test-Path $path)) {
        $null = New-Item -ItemType Directory -Path $path -Force
    }
}

# --- Resolve / prepare the output folder -----------------------------------
if ($Clean -and (Test-Path $OutputDir)) {
    Write-Host "Cleaning existing output folder: $OutputDir"
    Remove-Item -Path $OutputDir -Recurse -Force
}

New-DirIfMissing $OutputDir
New-DirIfMissing (Join-Path $OutputDir "config")
New-DirIfMissing (Join-Path $OutputDir "config\etc")
New-DirIfMissing (Join-Path $OutputDir "config\log")

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
# Prefer the build-output config (Release, not Debug). Fall back to repo-root.
if ([string]::IsNullOrEmpty($ConfigDir)) {
    $candidate = Join-Path $WinFormsBinDir "config"
    if (Test-Path $candidate) {
        $ConfigDir = $candidate
    } elseif (Test-Path "config") {
        $ConfigDir = "config"
    }
}

if ($ConfigDir -and (Test-Path $ConfigDir)) {
    # Copy config contents, leaving the staged etc/ and log/ folders intact.
    Copy-Item -Force -Recurse (Join-Path $ConfigDir "*") (Join-Path $OutputDir "config") -Exclude @("etc", "log")
} else {
    Write-Warning "No config directory found to stage (looked in build output and repo root)."
}

# --- Stage docs ------------------------------------------------------------
Copy-Item -Force *.md $OutputDir -ErrorAction SilentlyContinue

# --- Best-effort: stage CLI / Avalonia publish output if present -----------
# These come from `dotnet publish ... -o publish/cli-{rid}` /
# `publish/avalonia-{rid}` (same layout the crossplatform.yml workflow uses).
# Staged under <OutputDir>\<name> so a single local run can mirror the
# full-suite asset set without overwriting the WinForms payload.
$publishGlobs = @("publish\cli-*", "publish\avalonia-*")
foreach ($glob in $publishGlobs) {
    $matches = Get-ChildItem -Path $glob -Directory -ErrorAction SilentlyContinue
    foreach ($dir in $matches) {
        $dest = Join-Path $OutputDir $dir.Name
        Write-Host "Staging published bundle: $($dir.Name)"
        New-DirIfMissing $dest
        Copy-Item -Force -Recurse (Join-Path $dir.FullName "*") $dest
    }
}

Write-Host "Release staged into '$OutputDir'."
Write-Host "Next: zip per platform and attach to the GitHub release (see docs/RELEASE.md)."
