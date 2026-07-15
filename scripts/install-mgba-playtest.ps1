#requires -Version 5.1
<#
.SYNOPSIS
    Explicit, one-command bootstrap for the FEBuilderGBA headless playtest engine.

.DESCRIPTION
    Builds the pinned mGBA 0.10.5 Python binding from official source into an
    isolated, git-ignored virtual environment, then verifies it with
    `python -m febuildergba_playtest --check`.

    This script is an EXPLICIT user action. The FEBuilderGBA CLI never invokes it
    automatically and never installs anything at command runtime.

    Policy enforced here:
      * Only the official mgba-emu/mgba commit is fetched, as a commit archive,
        and its SHA-256 is verified BEFORE extraction. There is no branch/tag
        fallback and no alternate mirror.
      * Python build dependencies are installed with pip --require-hashes.
      * The local C compiler, CMake, and Python are validated but never
        downloaded; missing toolchains abort with guidance.
      * All build output lives under a dedicated, git-ignored root.

    License note: mGBA is MPL-2.0. This script builds it locally from source and
    bundles nothing into the repository.
#>
[CmdletBinding()]
param(
    [string]$Python = "python",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Pinned provenance -----------------------------------------------------
$MgbaCommit  = "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"
$MgbaVersion = "0.10.5"
$ArchiveSha  = "9475c26e9fa2f4b30c07ab6636e4b0a5b62e4baee2109ede7b2fecc52edae366"
$ArchiveUrl  = "https://codeload.github.com/mgba-emu/mgba/tar.gz/$MgbaCommit"

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot    = Split-Path -Parent $ScriptDir
$ToolDir     = Join-Path $RepoRoot "tools\gba-playtest"
$BuildRoot   = Join-Path $ToolDir ".mgba-build"
$VenvDir     = Join-Path $BuildRoot "venv"
$SrcArchive  = Join-Path $BuildRoot "mgba-$MgbaCommit.tar.gz"
$SrcDir      = Join-Path $BuildRoot "mgba-$MgbaCommit"
$RequirementsBootstrap = Join-Path $ToolDir "requirements-mgba-bootstrap.txt"
$RequirementsBuild     = Join-Path $ToolDir "requirements-mgba-build.txt"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Require-Command([string]$Name, [string]$Hint) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $cmd) { Fail "Required tool '$Name' was not found. $Hint" }
    Write-Host "  found $Name -> $($cmd.Source)"
}

Write-Host "== FEBuilderGBA playtest bootstrap =="
Write-Host "Validating local toolchain (nothing is downloaded here)..."
Require-Command $Python "Install Python 3.10+ and re-run with -Python <path> if needed."
Require-Command "cmake" "Install CMake and ensure it is on PATH."
Require-Command "git" "Install Git so exact source provenance can be stamped."
Require-Command "tar" "Install tar (bsdtar ships with Windows 10+) or add it to PATH."

# A C/C++ compiler is required to build libmgba and the cffi extension.
$hasCompiler = (Get-Command "cl.exe" -ErrorAction SilentlyContinue) -or `
               (Get-Command "gcc" -ErrorAction SilentlyContinue) -or `
               (Get-Command "clang" -ErrorAction SilentlyContinue)
if (-not $hasCompiler) {
    Fail "No C compiler found (cl.exe / gcc / clang). Install Visual Studio Build Tools and run from a Developer prompt."
}

$pyVersion = & $Python -c "import sys; print('%d.%d' % sys.version_info[:2])"
Write-Host "  python version $pyVersion"

if ($Force -and (Test-Path $BuildRoot)) {
    Write-Host "Removing existing build root (-Force)..."
    Remove-Item -Recurse -Force $BuildRoot
}
New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null

# --- Fetch pinned source archive and verify SHA-256 before extraction ------
if (-not (Test-Path $SrcArchive)) {
    Write-Host "Downloading pinned mGBA commit archive..."
    Invoke-WebRequest -Uri $ArchiveUrl -OutFile $SrcArchive -UseBasicParsing
}
Write-Host "Verifying archive SHA-256 before extraction..."
$actual = (Get-FileHash -Algorithm SHA256 -Path $SrcArchive).Hash.ToLower()
if ($actual -ne $ArchiveSha) {
    Remove-Item -Force $SrcArchive
    Fail "Archive SHA-256 mismatch. Expected $ArchiveSha but got $actual. Refusing to extract (no fallback)."
}
Write-Host "  archive verified: $actual"

if (-not (Test-Path $SrcDir)) {
    Write-Host "Extracting source..."
    tar -xzf $SrcArchive -C $BuildRoot
    if ($LASTEXITCODE -ne 0) { Fail "tar extraction failed." }
}

# --- Stamp exact local Git provenance for version.cmake --------------------
# The codeload tarball has no ``.git``. mGBA's version.cmake runs
# ``git describe`` from the source directory; without an inner repository Git
# walks UP into the parent FEBuilderGBA repository and stamps the binding with
# the wrong commit/version (or, if that is blocked, an unknown/non-pinned
# build). Initialize an inner repository pinned to the exact object so
# version.cmake reads correct local provenance and never discovers the parent
# repo. This is a SECOND independent pin on top of the already-verified archive
# SHA-256 (the first source-integrity gate), not a fallback: only the exact
# full commit SHA is fetched, with no branch/tag.
Write-Host "Stamping exact Git provenance inside the extracted source..."
Push-Location $SrcDir
try {
    if (Test-Path ".git") { Remove-Item -Recurse -Force ".git" }
    git init -q
    if ($LASTEXITCODE -ne 0) { Fail "git init failed in the extracted source." }
    git remote add origin "https://github.com/mgba-emu/mgba.git"
    if ($LASTEXITCODE -ne 0) { Fail "git remote add origin failed." }
    git fetch -q --depth 1 origin $MgbaCommit
    if ($LASTEXITCODE -ne 0) { Fail "git fetch of the pinned commit $MgbaCommit failed (no branch/tag fallback)." }
    git reset -q --hard FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { Fail "git reset --hard to the pinned commit failed." }
    $headSha = (& git rev-parse HEAD).Trim()
    if ($headSha -ne $MgbaCommit) {
        Fail "Inner Git HEAD $headSha does not match the pinned commit $MgbaCommit."
    }
    $porcelain = & git status --porcelain
    if ($porcelain) { Fail "Inner Git tree is not clean after reset to the pinned commit." }
    Write-Host "  inner provenance verified: $headSha"
}
finally {
    Pop-Location
}

# --- Isolated virtual environment ------------------------------------------
if (-not (Test-Path (Join-Path $VenvDir "Scripts\python.exe"))) {
    Write-Host "Creating isolated virtual environment..."
    & $Python -m venv $VenvDir
}
$VenvPython = Join-Path $VenvDir "Scripts\python.exe"

Write-Host "Installing hash-locked build prerequisites (stage 1: pinned wheels)..."
& $VenvPython -m pip install --require-hashes --only-binary ":all:" -r $RequirementsBootstrap
if ($LASTEXITCODE -ne 0) { Fail "Hash-locked bootstrap wheel install failed." }

Write-Host "Installing hash-locked Python build dependencies (stage 2: pinned sources)..."
& $VenvPython -m pip install --require-hashes --no-build-isolation --no-binary ":all:" -r $RequirementsBuild
if ($LASTEXITCODE -ne 0) { Fail "Hash-locked dependency install failed." }

# --- Build libmgba + display-free Python binding ---------------------------
$CmakeBuild = Join-Path $SrcDir "build-playtest"
New-Item -ItemType Directory -Force -Path $CmakeBuild | Out-Null

Write-Host "Configuring libmgba (headless, fixed color depth / sync options)..."
Push-Location $CmakeBuild
try {
    cmake .. `
        -DBUILD_PYTHON=ON `
        -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_GL=OFF -DBUILD_GLES2=OFF `
        -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF `
        -DCOLOR_16_BIT=ON -DCOLOR_5_6_5=ON `
        "-DPYTHON_EXECUTABLE=$VenvPython"
    if ($LASTEXITCODE -ne 0) { Fail "CMake configure failed." }

    cmake --build . --config Release
    if ($LASTEXITCODE -ne 0) { Fail "CMake build failed." }
}
finally {
    Pop-Location
}

# mGBA 0.10.5 defines a custom target 'mgba-py-install' (there is no Python
# install *component*). This MUST succeed; there is no fail-open fallback.
Write-Host "Installing the display-free Python binding (mgba-py-install)..."
Push-Location $CmakeBuild
try {
    cmake --build . --target mgba-py-install --config Release
    $installExit = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($installExit -ne 0) { Fail "mgba-py-install target failed (exit $installExit)." }

# --- Verify --------------------------------------------------------------
Write-Host "Verifying the pinned binding with --playtest --check..."
Push-Location $ToolDir
try {
    & $VenvPython -m febuildergba_playtest --check
    $checkExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($checkExit -eq 0) {
    Write-Host "== Bootstrap complete. mGBA $MgbaVersion binding is ready. =="
    Write-Host "Use this interpreter for playtesting: $VenvPython"
} else {
    Fail "Binding built but --check failed (exit $checkExit). Inspect the diagnostics above."
}
