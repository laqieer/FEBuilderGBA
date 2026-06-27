<#
.SYNOPSIS
    No-build smoke test for ../release.ps1 (the release-staging script).

.DESCRIPTION
    Builds a throwaway fixture tree that mimics a Release build output
    (FEBuilderGBA.exe + DLL + json + config) plus an optional published
    CLI/Avalonia bundle, then runs release.ps1 against it and asserts:
      * the script parses,
      * a custom -OutputDir is honored (NOT the old hard-coded "r\"),
      * the WinForms exe/dll/json are staged,
      * config is copied from the supplied (Release) build output, not Debug,
      * config\etc and config\log staging folders exist,
      * optional publish/cli-* / publish/avalonia-* bundles are staged when present,
      * a second run (idempotent) does not throw.

    Pure PowerShell — no .NET build required. Exit 0 = all pass, 1 = any failure.

.EXAMPLE
    pwsh -NoProfile -File scripts/release.test.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$script:failures = 0

function Assert-True([bool]$cond, [string]$msg) {
    if ($cond) {
        Write-Host "  [PASS] $msg"
    } else {
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $script:failures++
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseScript = Join-Path $repoRoot "release.ps1"

Write-Host "=== release.ps1 smoke test ==="
Write-Host "Repo root:      $repoRoot"
Write-Host "Release script: $releaseScript"

# --- 0. Parse check --------------------------------------------------------
$parseOk = $true
try {
    [scriptblock]::Create((Get-Content -Raw $releaseScript)) | Out-Null
} catch {
    $parseOk = $false
}
Assert-True $parseOk "release.ps1 parses"

# --- 1. Build a throwaway fixture tree -------------------------------------
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("febrel_" + [System.Guid]::NewGuid().ToString("N"))
$binDir = Join-Path $fixture "FEBuilderGBA\bin\Release"
$cfgDir = Join-Path $binDir "config"
$null = New-Item -ItemType Directory -Path $cfgDir -Force
$null = New-Item -ItemType Directory -Path (Join-Path $cfgDir "data") -Force

Set-Content -Path (Join-Path $binDir "FEBuilderGBA.exe") -Value "fake-exe"
Set-Content -Path (Join-Path $binDir "Some.dll")          -Value "fake-dll"
Set-Content -Path (Join-Path $binDir "app.deps.json")     -Value "{}"
Set-Content -Path (Join-Path $cfgDir "data\marker.txt")   -Value "release-config-marker"
Set-Content -Path (Join-Path $fixture "README.md")        -Value "# fixture readme"

# Optional published bundles (best-effort staging path).
$cliPub = Join-Path $fixture "publish\cli-linux-x64"
$avaPub = Join-Path $fixture "publish\avalonia-linux-x64"
$null = New-Item -ItemType Directory -Path $cliPub -Force
$null = New-Item -ItemType Directory -Path $avaPub -Force
Set-Content -Path (Join-Path $cliPub "FEBuilderGBA.CLI") -Value "fake-cli"
Set-Content -Path (Join-Path $avaPub "FEBuilderGBA.Avalonia") -Value "fake-ava"

$out = Join-Path $fixture "staged-out"

Push-Location $fixture
try {
    # --- 2. Run with a custom -OutputDir (proves "r\" is gone) -------------
    & $releaseScript -OutputDir $out -WinFormsBinDir $binDir 2>&1 | Out-Null

    Assert-True (-not (Test-Path (Join-Path $fixture "r"))) "no legacy 'r\' folder created"
    Assert-True (Test-Path $out)                            "custom -OutputDir honored"
    Assert-True (Test-Path (Join-Path $out "FEBuilderGBA.exe")) "WinForms exe staged"
    Assert-True (Test-Path (Join-Path $out "Some.dll"))    "DLL staged"
    Assert-True (Test-Path (Join-Path $out "app.deps.json")) "json staged"
    Assert-True (Test-Path (Join-Path $out "config\etc"))  "config\etc staging folder exists"
    Assert-True (Test-Path (Join-Path $out "config\log"))  "config\log staging folder exists"

    $markerPath = Join-Path $out "config\data\marker.txt"
    Assert-True (Test-Path $markerPath) "config copied from supplied (Release) build output"
    if (Test-Path $markerPath) {
        Assert-True ((Get-Content -Raw $markerPath).Trim() -eq "release-config-marker") "config content is the Release marker (not Debug)"
    }

    Assert-True (Test-Path (Join-Path $out "README.md"))   "docs (*.md) staged"
    Assert-True (Test-Path (Join-Path $out "cli-linux-x64\FEBuilderGBA.CLI")) "optional CLI publish bundle staged"
    Assert-True (Test-Path (Join-Path $out "avalonia-linux-x64\FEBuilderGBA.Avalonia")) "optional Avalonia publish bundle staged"

    # --- 3. Idempotent re-run --------------------------------------------
    $rerunOk = $true
    try {
        & $releaseScript -OutputDir $out -WinFormsBinDir $binDir 2>&1 | Out-Null
    } catch {
        $rerunOk = $false
    }
    Assert-True $rerunOk "second (idempotent) run does not throw"

    # --- 4. Missing optional publish output behaves cleanly --------------
    $out2 = Join-Path $fixture "staged-out-2"
    $binDir2 = Join-Path $fixture "no-publish\FEBuilderGBA\bin\Release"
    $null = New-Item -ItemType Directory -Path $binDir2 -Force
    Set-Content -Path (Join-Path $binDir2 "FEBuilderGBA.exe") -Value "fake-exe"
    $noPubOk = $true
    try {
        Push-Location (Join-Path $fixture "no-publish")
        & $releaseScript -OutputDir $out2 -WinFormsBinDir $binDir2 2>&1 | Out-Null
        Pop-Location
    } catch {
        $noPubOk = $false
    }
    Assert-True $noPubOk "missing optional publish output does not throw"
    Assert-True (Test-Path (Join-Path $out2 "FEBuilderGBA.exe")) "WinForms exe staged even with no publish bundles"
}
finally {
    Pop-Location
    Remove-Item -Path $fixture -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "=== ALL CHECKS PASSED ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "=== $($script:failures) CHECK(S) FAILED ===" -ForegroundColor Red
    exit 1
}
