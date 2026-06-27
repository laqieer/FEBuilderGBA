<#
.SYNOPSIS
    No-build smoke test for ../release.ps1 (the release-staging script).

.DESCRIPTION
    Builds a throwaway fixture tree that mimics a repo checkout with a Release
    build output (FEBuilderGBA.exe + DLL + json + config), repo-root docs, and
    optional published CLI/Avalonia bundles. A COPY of release.ps1 is dropped at
    the fixture root so the script's $PSScriptRoot resolves to the fixture, then
    it is run and the result asserted:
      * the script parses,
      * a custom -OutputDir is honored (NOT the old hard-coded "r\"),
      * the WinForms exe/dll/json are staged,
      * config is copied from the supplied (Release) build output, not Debug,
      * config/etc and config/log staging folders exist,
      * repo-root docs (*.md) are staged,
      * optional publish/cli-* / publish/avalonia-* bundles are staged,
      * the script is LOCATION-INDEPENDENT (run from an unrelated CWD),
      * a second run (idempotent) does not throw,
      * a missing optional publish output behaves cleanly.

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

# Cross-platform path joiner (avoids embedding a literal '\' in any segment).
# Cast $args to string[] so the params-array overload of Path.Combine is bound
# (passing the bare $args object joins segments with spaces instead).
function J { [System.IO.Path]::Combine([string[]]$args) }

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceScript = Join-Path $repoRoot "release.ps1"

Write-Host "=== release.ps1 smoke test ==="
Write-Host "Repo root:      $repoRoot"
Write-Host "Source script:  $sourceScript"

# --- 0. Parse check --------------------------------------------------------
$parseOk = $true
try {
    [scriptblock]::Create((Get-Content -Raw $sourceScript)) | Out-Null
} catch {
    $parseOk = $false
}
Assert-True $parseOk "release.ps1 parses"

# --- 1. Build a throwaway fixture tree (acts as the 'repo root') -----------
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("febrel_" + [System.Guid]::NewGuid().ToString("N"))
# Drop a copy of the script at the fixture root so $PSScriptRoot == fixture.
$fixtureScript = Join-Path $fixture "release.ps1"
$null = New-Item -ItemType Directory -Path $fixture -Force
Copy-Item -Force $sourceScript $fixtureScript

$binDir = J $fixture "FEBuilderGBA" "bin" "Release"
$cfgDir = Join-Path $binDir "config"
$null = New-Item -ItemType Directory -Path (Join-Path $cfgDir "data") -Force

Set-Content -Path (Join-Path $binDir "FEBuilderGBA.exe")    -Value "fake-exe"
Set-Content -Path (Join-Path $binDir "Some.dll")           -Value "fake-dll"
Set-Content -Path (Join-Path $binDir "app.deps.json")      -Value "{}"
Set-Content -Path (J $cfgDir "data" "marker.txt")          -Value "release-config-marker"
Set-Content -Path (Join-Path $fixture "README.md")         -Value "# fixture readme"
# Extensionless LICENSE (GPLv3) + notices file — both must be staged so every
# release artifact carries the required license text (issue #1633).
Set-Content -Path (Join-Path $fixture "LICENSE")                  -Value "GNU GENERAL PUBLIC LICENSE Version 3"
Set-Content -Path (Join-Path $fixture "THIRD-PARTY-NOTICES.md")   -Value "# Third-Party Notices"

# Optional published bundles (best-effort staging path) under <fixture>/publish.
$cliPub = J $fixture "publish" "cli-linux-x64"
$avaPub = J $fixture "publish" "avalonia-linux-x64"
$null = New-Item -ItemType Directory -Path $cliPub -Force
$null = New-Item -ItemType Directory -Path $avaPub -Force
Set-Content -Path (Join-Path $cliPub "FEBuilderGBA.CLI")        -Value "fake-cli"
Set-Content -Path (Join-Path $avaPub "FEBuilderGBA.Avalonia")   -Value "fake-ava"

$out = Join-Path $fixture "staged-out"

# Run from an UNRELATED working directory to prove location-independence
# (the script must resolve repo-relative paths via $PSScriptRoot, not the CWD).
$unrelatedCwd = Join-Path ([System.IO.Path]::GetTempPath()) ("febrel_cwd_" + [System.Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $unrelatedCwd -Force

Push-Location $unrelatedCwd
try {
    # --- 2. Run with a custom -OutputDir (proves "r\" is gone) -------------
    & $fixtureScript -OutputDir $out -WinFormsBinDir $binDir 2>&1 | Out-Null

    Assert-True (-not (Test-Path (Join-Path $fixture "r"))) "no legacy 'r\' folder created"
    Assert-True (-not (Test-Path (Join-Path $unrelatedCwd "r"))) "no 'r' folder in the CWD either"
    Assert-True (Test-Path $out)                               "custom -OutputDir honored"
    Assert-True (Test-Path (Join-Path $out "FEBuilderGBA.exe")) "WinForms exe staged"
    Assert-True (Test-Path (Join-Path $out "Some.dll"))        "DLL staged"
    Assert-True (Test-Path (Join-Path $out "app.deps.json"))   "json staged"
    Assert-True (Test-Path (J $out "config" "etc"))            "config/etc staging folder exists"
    Assert-True (Test-Path (J $out "config" "log"))            "config/log staging folder exists"

    $markerPath = J $out "config" "data" "marker.txt"
    Assert-True (Test-Path $markerPath) "config copied from supplied (Release) build output"
    if (Test-Path $markerPath) {
        Assert-True ((Get-Content -Raw $markerPath).Trim() -eq "release-config-marker") "config content is the Release marker (not Debug)"
    }

    # Docs + publish bundles are resolved against the script location (fixture),
    # NOT the unrelated CWD — these assertions prove location-independence.
    Assert-True (Test-Path (Join-Path $out "README.md"))      "repo-root docs (*.md) staged from script dir, not CWD"
    # GPLv3 compliance (#1633): the extensionless LICENSE must be staged via the
    # explicit Copy-Item (the "*.md" glob would miss it), and THIRD-PARTY-NOTICES.md
    # via the "*.md" glob.
    Assert-True (Test-Path (Join-Path $out "LICENSE"))                "LICENSE staged (GPLv3 compliance #1633)"
    Assert-True (Test-Path (Join-Path $out "THIRD-PARTY-NOTICES.md")) "THIRD-PARTY-NOTICES.md staged (#1633)"
    Assert-True (Test-Path (J $out "cli-linux-x64" "FEBuilderGBA.CLI"))      "optional CLI publish bundle staged"
    Assert-True (Test-Path (J $out "avalonia-linux-x64" "FEBuilderGBA.Avalonia")) "optional Avalonia publish bundle staged"

    # --- 3. Idempotent re-run --------------------------------------------
    $rerunOk = $true
    try {
        & $fixtureScript -OutputDir $out -WinFormsBinDir $binDir 2>&1 | Out-Null
    } catch {
        $rerunOk = $false
    }
    Assert-True $rerunOk "second (idempotent) run does not throw"

    # --- 4. Missing optional publish output behaves cleanly --------------
    # Use a SECOND fixture whose script root has no publish/ directory.
    $fixture2 = Join-Path ([System.IO.Path]::GetTempPath()) ("febrel_np_" + [System.Guid]::NewGuid().ToString("N"))
    $null = New-Item -ItemType Directory -Path $fixture2 -Force
    $fixture2Script = Join-Path $fixture2 "release.ps1"
    Copy-Item -Force $sourceScript $fixture2Script
    $binDir2 = J $fixture2 "FEBuilderGBA" "bin" "Release"
    $null = New-Item -ItemType Directory -Path $binDir2 -Force
    Set-Content -Path (Join-Path $binDir2 "FEBuilderGBA.exe") -Value "fake-exe"
    $out2 = Join-Path $fixture2 "staged-out-2"

    $noPubOk = $true
    try {
        & $fixture2Script -OutputDir $out2 -WinFormsBinDir $binDir2 2>&1 | Out-Null
    } catch {
        $noPubOk = $false
    }
    Assert-True $noPubOk "missing optional publish output does not throw"
    Assert-True (Test-Path (Join-Path $out2 "FEBuilderGBA.exe")) "WinForms exe staged even with no publish bundles"
}
finally {
    Pop-Location
    Remove-Item -Path $fixture  -Recurse -Force -ErrorAction SilentlyContinue
    if ($fixture2)     { Remove-Item -Path $fixture2     -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item -Path $unrelatedCwd -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "=== ALL CHECKS PASSED ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "=== $($script:failures) CHECK(S) FAILED ===" -ForegroundColor Red
    exit 1
}
