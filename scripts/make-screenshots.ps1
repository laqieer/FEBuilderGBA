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
# `net9.0-windows`. On non-Windows hosts the WinForms step is skipped with a
# warning and the gallery is generated against the Avalonia captures only. The
# Avalonia runner is cross-platform (net9.0) so the AV-only path still works.

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

# Derive ROM tag from filename if not provided. Mirrors the runner naming:
# both --screenshot-all flows use Program.ROM.RomInfo.VersionToFilename, which
# is typically "FE6JP", "FE7U", "FE8U", etc. When the user passes a custom ROM
# filename we still let them override via -RomTag.
if (-not $RomTag) {
    $RomTag = [System.IO.Path]::GetFileNameWithoutExtension($romPath)
    Write-Host "Derived ROM tag from filename: $RomTag"
}

# Derive output directory if not provided.
if (-not $Out) {
    $today = Get-Date -Format "yyyy-MM-dd"
    $Out = Join-Path $repoRoot "docs" | Join-Path -ChildPath "avalonia-gaps" `
        | Join-Path -ChildPath "$today-screenshots" | Join-Path -ChildPath $RomTag
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
# Step 1: WinForms --screenshot-all (Windows only; the project targets net9.0-windows)
# ---------------------------------------------------------------------------
$IsWindowsHost = $IsWindows -or ($env:OS -eq "Windows_NT")
$wfSkipped = $false
if (-not $IsWindowsHost) {
    Write-Warning "Non-Windows host detected — skipping WinForms --screenshot-all (net9.0-windows target). The gallery will surface AV-only captures."
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
        # We avoid `dotnet build` for the WinForms project here because the
        # project targets net9.0-windows and may need msbuild on Windows for
        # the WinForms designer. `dotnet build` works fine in practice; if it
        # fails the user gets a clear message.
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
$avPngCount = (Get-ChildItem -Path $avDir -Filter "*.png" -File -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host "Avalonia captured: $avPngCount PNGs"

# ---------------------------------------------------------------------------
# Step 3: --gap-sweep-gallery
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 3/3: gallery build ==="
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
