#requires -Version 5.1
<#
.SYNOPSIS
    Windows wrapper that runs the POSIX playtest bootstrap inside a user-provided
    MSYS2 UCRT64 (MinGW64) environment.

.DESCRIPTION
    mGBA 0.10.5's Python binding is a GCC/MinGW-only build. Its ``_builder.py``
    hardcodes GCC-style preprocessing (``cc -E -fno-inline -P``) and CMake feeds
    ``-I`` include flags straight into CFFI; upstream issue mgba-emu/mgba#1637
    records that the Windows Python binding does not link under MSVC and was
    closed not-planned (the Python API is deprecated). A Visual Studio ``cl``
    toolchain therefore CANNOT satisfy the one-command Windows contract, so this
    wrapper deliberately does NOT use or claim MSVC support.

    Instead it locates a user-installed MSYS2 root, validates that the UCRT64
    toolchain (Python, GCC, CMake, Ninja/Make, Git, curl, tar) is already
    present, and then invokes the exact same fail-hard POSIX bootstrap
    (``install-mgba-playtest.sh``) under the UCRT64 login shell. All provenance,
    hash-locking, exact-commit, and build logic lives in that single POSIX
    script; this wrapper only supplies the correct GCC environment and a safe
    path translation.

    Policy enforced here:
      * MSYS2 and its toolchain are NEVER downloaded or installed. A missing
        prerequisite fails closed with precise UCRT64 guidance.
      * The UCRT64 environment is applied to the CHILD process only. No global
        or persistent (User/Machine) PATH or environment is mutated.
      * The pinned-commit / SHA-256 / hash-locked-dependency guarantees are
        enforced by the delegated POSIX script, which finishes by running
        ``python -m febuildergba_playtest --check`` (exact version + commit).

    This script is an EXPLICIT user action. The FEBuilderGBA CLI never invokes it
    automatically and never installs anything at command runtime.

    A real Windows MSYS2 CI job is required before this path is considered
    merge-ready.

.PARAMETER Msys2Root
    Path to the MSYS2 installation root (the directory containing
    ``usr\bin\bash.exe``). Overrides the MSYS2_ROOT environment variable and the
    conventional ``C:\msys64`` location.

.PARAMETER Force
    Forwarded to the POSIX bootstrap as ``--force`` (rebuilds from scratch).
#>
[CmdletBinding()]
param(
    [string]$Msys2Root = "",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$MgbaVersion = "0.10.5"

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot    = Split-Path -Parent $ScriptDir
$ToolDir     = Join-Path $RepoRoot "tools\gba-playtest"
$PosixScript = Join-Path $ScriptDir "install-mgba-playtest.sh"

$Msys2Guidance = @"
MSYS2 with the UCRT64 toolchain is required (MSVC 'cl' is NOT supported for the
mGBA 0.10.5 Python binding). Install MSYS2 from https://www.msys2.org and, inside
its UCRT64 shell, install these packages before re-running:
  mingw-w64-ucrt-x86_64-python
  mingw-w64-ucrt-x86_64-gcc
  mingw-w64-ucrt-x86_64-cmake
  mingw-w64-ucrt-x86_64-ninja
  git  curl  tar
Then re-run this script, optionally with -Msys2Root <path> or by setting the
MSYS2_ROOT environment variable.
"@

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

if (-not (Test-Path $PosixScript)) {
    Fail "Cannot find the POSIX bootstrap next to this wrapper: $PosixScript"
}

Write-Host "== FEBuilderGBA playtest bootstrap (Windows / MSYS2 UCRT64) =="

# --- Locate a user-installed MSYS2 root (never installed by this script) ----
$candidates = New-Object System.Collections.Generic.List[string]
if ($Msys2Root) { $candidates.Add($Msys2Root) }
if ($env:MSYS2_ROOT) { $candidates.Add($env:MSYS2_ROOT) }
$candidates.Add("C:\msys64")

$bash = $null
foreach ($c in $candidates) {
    if (-not $c) { continue }
    $candidate = Join-Path $c "usr\bin\bash.exe"
    if (Test-Path $candidate) { $bash = $candidate; break }
}
if (-not $bash) {
    Fail "MSYS2 was not found in any of: $($candidates -join ', ').`n$Msys2Guidance"
}
Write-Host "  using MSYS2 bash -> $bash"

# --- Run everything under the UCRT64 login shell. Apply the environment to    -
# --- the CHILD process only: save and restore so nothing persists globally.   -
$saved = @{}
foreach ($name in @("MSYSTEM", "CHERE_INVOKING", "MSYS2_PATH_TYPE")) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}
try {
    $env:MSYSTEM = "UCRT64"
    $env:CHERE_INVOKING = "1"

    # --- Validate the UCRT64 toolchain is present (never auto-installed) -----
    $probe = @'
missing=""
for t in python gcc cmake git curl tar; do
  command -v "$t" >/dev/null 2>&1 || missing="$missing $t"
done
if ! command -v ninja >/dev/null 2>&1 && ! command -v make >/dev/null 2>&1; then
  missing="$missing ninja-or-make"
fi
if [ -n "$missing" ]; then echo "MISSING:$missing"; exit 3; fi
echo "TOOLCHAIN-OK MSYSTEM=$MSYSTEM"
'@
    Write-Host "Validating the UCRT64 toolchain (nothing is downloaded here)..."
    $probeOut = & $bash -lc $probe 2>&1
    $probeExit = $LASTEXITCODE
    $probeOut | ForEach-Object { Write-Host "  $_" }
    if ($probeExit -ne 0) {
        Fail "The MSYS2 UCRT64 toolchain is incomplete (missing tools reported above).`n$Msys2Guidance"
    }

    # --- Delegate to the POSIX bootstrap under UCRT64. Pass the script path as -
    # --- a positional argument and convert it with cygpath INSIDE the shell so -
    # --- the Windows path is translated structurally (no fragile string        -
    # --- interpolation, spaces/backslashes handled by the shell).              -
    $inner = 'set -e; sh_win="$1"; shift; sh_posix="$(cygpath -u "$sh_win")"; exec bash "$sh_posix" "$@"'
    $forward = @("--python=python")
    if ($Force) { $forward += "--force" }

    Write-Host "Delegating to the POSIX bootstrap under the UCRT64 shell..."
    & $bash -lc $inner "febuildergba-playtest-bootstrap" $PosixScript @forward
    $bootstrapExit = $LASTEXITCODE
}
finally {
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        } else {
            Set-Item "Env:$name" -Value $saved[$name]
        }
    }
}

if ($bootstrapExit -ne 0) {
    Fail "The UCRT64 bootstrap failed (exit $bootstrapExit). Inspect the diagnostics above."
}

# --- Report the exact interpreter path (both venv layouts are supported) -----
$venvDir = Join-Path $ToolDir ".mgba-build\venv"
$venvPy = @(
    (Join-Path $venvDir "bin\python.exe"),
    (Join-Path $venvDir "bin\python"),
    (Join-Path $venvDir "Scripts\python.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

Write-Host "== Bootstrap complete. mGBA $MgbaVersion binding is ready (UCRT64 build). =="
if ($venvPy) {
    Write-Host "Use this interpreter for playtesting: $venvPy"
} else {
    Write-Host "Use the venv interpreter reported above for playtesting."
}
