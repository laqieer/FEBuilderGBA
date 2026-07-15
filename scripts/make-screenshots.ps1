#!/usr/bin/env pwsh
# SPDX-License-Identifier: GPL-3.0-or-later
# FEBuilderGBA gap-sweep tooling (#374) — Phase 3 helper.
#
# Drives both `--screenshot-all` runners (WinForms + Avalonia) against the same
# ROM, then calls `--gap-sweep-gallery` to emit a side-by-side Markdown
# manifest. PNGs are gitignored; only the index.md is committed.
#
# Usage:
#   pwsh ./scripts/make-screenshots.ps1 -Rom roms/FE8U.gba
#   pwsh ./scripts/make-screenshots.ps1 -Rom roms/FE8U.gba -Out docs/avalonia-gaps/2026-05-22-screenshots/FE8U
#   pwsh ./scripts/make-screenshots.ps1 -Rom roms/FE8U.gba -RomTag FE8U
#
# Non-Windows note: The WinForms `--screenshot-all` runner targets
# `net10.0-windows`. On non-Windows hosts the WinForms step is skipped with a
# warning and the gallery is generated against the Avalonia captures only. The
# Avalonia runner is cross-platform (net10.0) so the AV-only path still works.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, HelpMessage = "Path to the ROM file (e.g. roms/FE8U.gba)")]
    [string]$Rom,

    [Parameter(HelpMessage = "Output directory for the gallery (default: docs/avalonia-gaps/<today>-screenshots/<rom-tag>)")]
    [string]$Out,

    [Parameter(HelpMessage = "ROM tag suffix used in screenshot filenames (default: derived from ROM filename)")]
    [string]$RomTag,

    [Parameter(HelpMessage = "Build configuration (default: Release)")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Resolve paths
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "FEBuilderGBA.sln"))) {
    Write-Error "Could not find FEBuilderGBA.sln at expected repo root: $repoRoot"
    exit 1
}

$romPath = Resolve-Path $Rom -ErrorAction SilentlyContinue
if (-not $romPath) {
    Write-Error "ROM file not found: $Rom"
    exit 1
}
$romPath = $romPath.Path

# NOTE: ROM tag derivation. Both --screenshot-all runners suffix screenshots
# with `Program.ROM.RomInfo.VersionToFilename` (e.g. "FE6JP", "FE7U", "FE8U"),
# which is the binary-signature-detected version — NOT the filename. We
# defer the derivation until AFTER the captures land so we can infer the
# real tag from the produced PNG names. The -RomTag override stays available
# for users who know it up front.
if ($RomTag) {
    Write-Host "Using explicit ROM tag: $RomTag"
}

# Derive output directory if not provided. When -RomTag isn't given we use
# the ROM filename for the directory name (not for the gallery's --rom-tag);
# the actual --rom-tag is inferred from the PNG filenames after the captures
# land.
if (-not $Out) {
    $today = Get-Date -Format "yyyy-MM-dd"
    $outRomTag = if ($RomTag) { $RomTag } else { [System.IO.Path]::GetFileNameWithoutExtension($romPath) }
    $Out = Join-Path $repoRoot "docs" | Join-Path -ChildPath "avalonia-gaps" `
        | Join-Path -ChildPath "$today-screenshots" | Join-Path -ChildPath $outRomTag
    Write-Host "Derived output directory: $Out"
}

# Resolve $Out to an absolute path even if it doesn't exist yet. We use
# [Path]::GetFullPath which respects absolute paths verbatim (passing an
# already-absolute path through `Join-Path (Get-Location) ...` would
# duplicate the drive letter on Windows).
$Out = [System.IO.Path]::GetFullPath($Out)

$wfDir = Join-Path $Out "wf"
$avDir = Join-Path $Out "av"
$indexMd = Join-Path $Out "index.md"

New-Item -ItemType Directory -Path $wfDir -Force | Out-Null
New-Item -ItemType Directory -Path $avDir -Force | Out-Null

# ---------------------------------------------------------------------------
# Step 1: WinForms --screenshot-all (Windows only; the project targets net10.0-windows)
# ---------------------------------------------------------------------------
$IsWindowsHost = $IsWindows -or ($env:OS -eq "Windows_NT")
$wfSkipped = $false
if (-not $IsWindowsHost) {
    Write-Warning "Non-Windows host detected — skipping WinForms --screenshot-all (net10.0-windows target). The gallery will surface AV-only captures."
    $wfSkipped = $true
}
else {
    Write-Host ""
    Write-Host "=== Step 1/3: WinForms --screenshot-all ==="
    # FEBuilderGBA.csproj sets AppendTargetFrameworkToOutputPath=false so the
    # binary sits directly under bin/$Configuration, not bin/$Configuration/<tfm>.
    $wfExe = Join-Path $repoRoot "FEBuilderGBA" | Join-Path -ChildPath "bin" `
        | Join-Path -ChildPath $Configuration | Join-Path -ChildPath "FEBuilderGBA.exe"
    if (-not (Test-Path $wfExe)) {
        Write-Warning "WinForms binary not found at $wfExe — building first..."
        # The .NET 10 SDK provides the required WindowsDesktop build targets;
        # if the build fails the user gets a clear message.
        & dotnet build (Join-Path $repoRoot "FEBuilderGBA" | Join-Path -ChildPath "FEBuilderGBA.csproj") `
            -c $Configuration | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "WinForms build failed; skipping the WinForms capture step."
            $wfSkipped = $true
        }
    }

    if (-not $wfSkipped) {
        if (-not (Test-Path $wfExe)) {
            Write-Warning "WinForms binary still not found at $wfExe; skipping the WinForms capture step."
            $wfSkipped = $true
        }
        else {
            & $wfExe --rom $romPath --screenshot-all --screenshot-dir=$wfDir
            # WinForms --screenshot-all currently always returns 0 from
            # Program.cs even when the runner sets a non-zero ExitCode (Copilot
            # flagged this during Phase 3 plan review). We rely on the file
            # count instead.
            $wfPngCount = (Get-ChildItem -Path $wfDir -Filter "*.png" -File -ErrorAction SilentlyContinue | Measure-Object).Count
            Write-Host "WinForms captured: $wfPngCount PNGs"
        }
    }
}

# ---------------------------------------------------------------------------
# Step 2: Avalonia --screenshot-all (cross-platform)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 2/3: Avalonia --screenshot-all ==="
& dotnet run --project (Join-Path $repoRoot "FEBuilderGBA.Avalonia" | Join-Path -ChildPath "FEBuilderGBA.Avalonia.csproj") `
    -c $Configuration -- --rom $romPath --screenshot-all --screenshot-dir=$avDir
# $ErrorActionPreference="Stop" does NOT halt on native-process nonzero exits
# (Copilot review point); we must check $LASTEXITCODE explicitly. We treat a
# nonzero exit as fatal because the gallery against a partially-captured AV
# directory would silently mis-classify rows as "missing".
$avExitCode = $LASTEXITCODE
if ($avExitCode -ne 0) {
    Write-Error "Avalonia --screenshot-all exited with code $avExitCode — gallery build aborted to avoid masking the capture failure."
    exit $avExitCode
}
$avPngCount = (Get-ChildItem -Path $avDir -Filter "*.png" -File -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host "Avalonia captured: $avPngCount PNGs"

# ---------------------------------------------------------------------------
# Derive the real ROM tag from the captured PNGs if the caller didn't pass
# one explicitly. The runners emit `{Prefix}{ViewName}_{RomTag}.png`, so
# stripping the prefix + view-name leaves `_<RomTag>.png` — the segment
# AFTER the LAST underscore. View names may contain underscores (e.g.
# `ToolWorkSupport_SelectUPSView`), so we must rsplit, not lsplit.
if (-not $RomTag) {
    # Prefer Avalonia PNGs (always present); fall back to WinForms PNGs when
    # the AV step somehow produced nothing.
    $sampleDir = if ((Get-ChildItem -Path $avDir -Filter "*.png" -File -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) { $avDir } else { $wfDir }
    $sampleFile = Get-ChildItem -Path $sampleDir -Filter "*.png" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($sampleFile) {
        $bareName = [System.IO.Path]::GetFileNameWithoutExtension($sampleFile.Name)
        # Last underscore-separated segment is the ROM tag the runner used.
        $lastUnderscore = $bareName.LastIndexOf('_')
        if ($lastUnderscore -gt 0 -and $lastUnderscore -lt ($bareName.Length - 1)) {
            $RomTag = $bareName.Substring($lastUnderscore + 1)
            Write-Host "Inferred ROM tag from captured PNG filename: $RomTag"
        }
    }
    if (-not $RomTag) {
        Write-Warning "Could not infer ROM tag from captured PNGs; falling back to ROM filename."
        $RomTag = [System.IO.Path]::GetFileNameWithoutExtension($romPath)
    }
}

# ---------------------------------------------------------------------------
# Step 3: --gap-sweep-gallery
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 3/3: gallery build (rom-tag=$RomTag) ==="
& dotnet run --project (Join-Path $repoRoot "FEBuilderGBA.Avalonia" | Join-Path -ChildPath "FEBuilderGBA.Avalonia.csproj") `
    -c $Configuration -- --gap-sweep-gallery `
        --wf-dir=$wfDir --av-dir=$avDir --rom-tag=$RomTag --out=$indexMd

if ($LASTEXITCODE -ne 0) {
    Write-Error "Gallery build failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== Done ==="
Write-Host "Gallery manifest: $indexMd"
if ($wfSkipped) {
    Write-Host "  (WinForms captures skipped — gallery is AV-only on this host.)"
}
Write-Host "  WinForms PNGs:  $wfDir"
Write-Host "  Avalonia PNGs:  $avDir"
