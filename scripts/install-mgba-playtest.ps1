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
    toolchain (Python, GCC, CMake, MSYS /usr/bin/make, Git, curl, tar) is already
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
  make
  mingw-w64-ucrt-x86_64-libepoxy
  mingw-w64-ucrt-x86_64-libffi
  mingw-w64-ucrt-x86_64-libpng
  mingw-w64-ucrt-x86_64-zlib
  mingw-w64-ucrt-x86_64-pkgconf
  mingw-w64-ucrt-x86_64-ffmpeg
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

function Convert-PosixPath([string]$WinPath) {
    # Translate a Windows path to a POSIX path with cygpath under a LOGIN shell
    # (-l), so /etc/profile.d puts /usr/bin (cygpath) and /ucrt64/bin on PATH.
    # ``cygpath`` is REQUIRED: this probes for it and fails closed if it (or the
    # conversion) is unavailable. This replaces the old stdin here-string
    # transport, which Windows PowerShell 5.1 corrupted with a UTF-8 BOM. The
    # path is a plain command argument here (never piped to Bash on stdin), so
    # no byte-order mark can be prepended.
    $converted = $null
    $convExit = 1
    $convPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $converted = & $bash -l -c 'command -v cygpath >/dev/null 2>&1 || exit 127; cygpath -u "$1"' -- $WinPath
        $convExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $convPreference
    }
    if ($convExit -ne 0) {
        Fail "Could not convert a path with cygpath under the UCRT64 login shell (exit $convExit). cygpath must be available.`n$Msys2Guidance"
    }
    $posix = $converted | Where-Object { $_ -and ([string]$_).Trim() } | Select-Object -Last 1
    if (-not $posix) {
        Fail "cygpath returned no POSIX path for a required file.`n$Msys2Guidance"
    }
    return ([string]$posix).Trim()
}

# --- Run everything under the UCRT64 shell. Apply the environment to          -
# --- the CHILD process only: save and restore so nothing persists globally.   -
$saved = @{}
foreach ($name in @("MSYSTEM", "CHERE_INVOKING", "MSYS2_PATH_TYPE")) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}
# Windows PowerShell 5.1 prepends a UTF-8 BOM when a script is piped to Bash on
# stdin, which Bash then reads as literal bytes at the start of the script
# (breaking ``set -e`` and the probe). This wrapper therefore NEVER pipes a
# script to Bash: the probe is materialized to one uniquely named, BOM-free,
# LF-normalized temp file and executed directly (``bash -l <path>``), and the
# checked-in POSIX script is likewise executed directly after a cygpath path
# translation. No console-encoding (OutputEncoding) mutation is needed here.
$bootstrapExit = 1
$probeFile = $null
try {
    $env:MSYSTEM = "UCRT64"
    $env:CHERE_INVOKING = "1"

    # --- Validate the UCRT64 toolchain is present (never auto-installed) -----
    $probe = @'
missing=""
for t in python gcc cmake git curl tar pkg-config cygpath; do
  command -v "$t" >/dev/null 2>&1 || missing="$missing $t"
done
if [ ! -f /usr/bin/make ] || [ ! -x /usr/bin/make ]; then
  missing="$missing /usr/bin/make"
fi
# Native configure/build dependencies are probed through pkg-config rather than
# guessed header paths. PNG + zlib are mandatory for screenshot evidence.
# FFmpeg's dev modules are mandatory too: mGBA's e-Reader API (EReaderScanLoadImageA
# et al.) is only compiled when USE_FFMPEG is on, and this CFFI binding declares
# those symbols unconditionally, so an FFmpeg-less build fails at import time.
if command -v pkg-config >/dev/null 2>&1; then
  for p in epoxy libffi libpng zlib libavcodec libavfilter libavformat libavutil libswscale; do
    pkg-config --exists "$p" || missing="$missing $p"
  done
  if ! pkg-config --exists libswresample && ! pkg-config --exists libavresample; then
    missing="$missing libswresample-or-libavresample"
  fi
fi
# The interpreter that builds and later imports the binding MUST be the UCRT64
# Python (a python.org/MSVC interpreter cannot load the GCC-built binding).
py="$(command -v python 2>/dev/null)"
case "$py" in
  /ucrt64/bin/python|/ucrt64/bin/python.exe) ;;
  *) missing="$missing python-under-ucrt64" ;;
esac
if [ -n "$missing" ]; then echo "MISSING:$missing"; exit 3; fi
echo "TOOLCHAIN-OK MSYSTEM=$MSYSTEM python=$py"
'@
    Write-Host "Validating the UCRT64 toolchain (nothing is downloaded here)..."
    # Windows PowerShell 5.1 prepends a UTF-8 BOM when a script is piped to Bash
    # on stdin, so the probe is instead materialized to one uniquely named,
    # BOM-free, LF-normalized temp file and executed directly as a login-shell
    # script argument (``bash -l <path>``). A LOGIN shell (-l) is required: a
    # non-login MSYS2 Bash does not source /etc/profile.d, so /ucrt64/bin is
    # never added to PATH and every probed tool (and the later delegated build)
    # would silently resolve to nothing or the wrong (non-UCRT64) toolchain.
    $probeFile = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        "febgba-mgba-probe-$([Guid]::NewGuid().ToString('N')).sh"
    )
    # Normalize CRLF/CR to LF, then write with a BOM-free UTF-8 encoding so Bash
    # never sees a carriage return or a leading EF BB BF byte-order mark.
    $probeLf = ($probe -replace "`r`n", "`n") -replace "`r", "`n"
    [System.IO.File]::WriteAllText($probeFile, $probeLf, (New-Object System.Text.UTF8Encoding($false)))
    $probePosix = Convert-PosixPath $probeFile

    # Limit Continue to this captured native invocation so normal bootstrap
    # failures still stop, and read the explicit exit code fail-closed.
    $probeExit = 1
    $probePreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $probeOut = & $bash -l $probePosix 2>&1
        $probeExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $probePreference
    }
    $probeOut | ForEach-Object { Write-Host "  $_" }
    if ($probeExit -ne 0) {
        Fail "The MSYS2 UCRT64 toolchain is incomplete (missing tools reported above).`n$Msys2Guidance"
    }

    # --- Delegate to the POSIX bootstrap under UCRT64. The checked-in script  -
    # --- path is converted with cygpath (structurally, no fragile string      -
    # --- interpolation; spaces/backslashes handled by cygpath) and then        -
    # --- executed DIRECTLY as a login-shell script argument. There is no       -
    # --- stdin here-string and no ``-s`` stdin mode anywhere.                  -
    $scriptPosix = Convert-PosixPath $PosixScript
    $forward = @("--python=python")
    if ($Force) { $forward += "--force" }

    Write-Host "Delegating to the POSIX bootstrap under the UCRT64 shell..."
    & $bash -l $scriptPosix @forward
    $bootstrapExit = $LASTEXITCODE
}
finally {
    # Remove ONLY the exact probe temp file (no recurse, wildcard, or directory
    # removal), then restore the process env vars saved above.
    if ($probeFile -and (Test-Path -LiteralPath $probeFile)) {
        Remove-Item -LiteralPath $probeFile -Force
    }
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
