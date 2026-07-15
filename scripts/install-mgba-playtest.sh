#!/usr/bin/env bash
#
# Explicit, one-command bootstrap for the FEBuilderGBA headless playtest engine
# on POSIX hosts. Builds the pinned mGBA 0.10.5 Python binding from official
# source into an isolated, git-ignored virtual environment, then verifies it
# with `python -m febuildergba_playtest --check`.
#
# This script is an EXPLICIT user action. The FEBuilderGBA CLI never invokes it
# automatically and never installs anything at command runtime.
#
# Policy enforced here:
#   * Only the official mgba-emu/mgba commit is fetched, as a commit archive,
#     and its SHA-256 is verified BEFORE extraction. No branch/tag fallback,
#     no alternate mirror.
#   * Python build dependencies are installed with pip --require-hashes.
#   * The local C compiler, CMake, and Python are validated but never
#     downloaded; missing toolchains abort with guidance.
#   * All build output lives under a dedicated, git-ignored root.
#
# License note: mGBA is MPL-2.0. This script builds it locally from source and
# bundles nothing into the repository.

set -euo pipefail

# --- Pinned provenance -----------------------------------------------------
MGBA_COMMIT="26b7884bc25a5933960f3cdcd98bac1ae14d42e2"
MGBA_VERSION="0.10.5"
ARCHIVE_SHA="9475c26e9fa2f4b30c07ab6636e4b0a5b62e4baee2109ede7b2fecc52edae366"
ARCHIVE_URL="https://codeload.github.com/mgba-emu/mgba/tar.gz/${MGBA_COMMIT}"

PYTHON_BIN="${PYTHON:-python3}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TOOL_DIR="${REPO_ROOT}/tools/gba-playtest"
BUILD_ROOT="${TOOL_DIR}/.mgba-build"
VENV_DIR="${BUILD_ROOT}/venv"
SRC_ARCHIVE="${BUILD_ROOT}/mgba-${MGBA_COMMIT}.tar.gz"
SRC_DIR="${BUILD_ROOT}/mgba-${MGBA_COMMIT}"
REQUIREMENTS_BOOTSTRAP="${TOOL_DIR}/requirements-mgba-bootstrap.txt"
REQUIREMENTS_BUILD="${TOOL_DIR}/requirements-mgba-build.txt"

FORCE=0
for arg in "$@"; do
    case "$arg" in
        --force) FORCE=1 ;;
        --python=*) PYTHON_BIN="${arg#*=}" ;;
        *) echo "Unknown argument: $arg" >&2; exit 1 ;;
    esac
done

fail() { echo "ERROR: $*" >&2; exit 1; }

require_cmd() {
    command -v "$1" >/dev/null 2>&1 || fail "Required tool '$1' was not found. $2"
    echo "  found $1 -> $(command -v "$1")"
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    else
        shasum -a 256 "$1" | awk '{print $1}'
    fi
}

echo "== FEBuilderGBA playtest bootstrap =="
echo "Validating local toolchain (nothing is downloaded here)..."
require_cmd "${PYTHON_BIN}" "Install Python 3.10+ or set PYTHON=<path>."
require_cmd "cmake" "Install CMake and ensure it is on PATH."

if ! command -v cc >/dev/null 2>&1 && ! command -v gcc >/dev/null 2>&1 && ! command -v clang >/dev/null 2>&1; then
    fail "No C compiler found (cc / gcc / clang). Install build-essential, Xcode command-line tools, or the MSYS2 UCRT64 GCC toolchain."
fi
require_cmd "curl" "Install curl to fetch the pinned source archive."
require_cmd "tar" "Install tar to extract the source archive."
require_cmd "git" "Install git so exact source provenance can be stamped."

PY_VERSION="$("${PYTHON_BIN}" -c 'import sys; print("%d.%d" % sys.version_info[:2])')"
echo "  python version ${PY_VERSION}"

if [ "${FORCE}" -eq 1 ] && [ -d "${BUILD_ROOT}" ]; then
    echo "Removing existing build root (--force)..."
    rm -rf "${BUILD_ROOT}"
fi
mkdir -p "${BUILD_ROOT}"

# --- Fetch pinned source archive and verify SHA-256 before extraction ------
if [ ! -f "${SRC_ARCHIVE}" ]; then
    echo "Downloading pinned mGBA commit archive..."
    curl -fsSL "${ARCHIVE_URL}" -o "${SRC_ARCHIVE}"
fi

echo "Verifying archive SHA-256 before extraction..."
ACTUAL="$(sha256_of "${SRC_ARCHIVE}")"
if [ "${ACTUAL}" != "${ARCHIVE_SHA}" ]; then
    rm -f "${SRC_ARCHIVE}"
    fail "Archive SHA-256 mismatch. Expected ${ARCHIVE_SHA} but got ${ACTUAL}. Refusing to extract (no fallback)."
fi
echo "  archive verified: ${ACTUAL}"

if [ ! -d "${SRC_DIR}" ]; then
    echo "Extracting source..."
    tar -xzf "${SRC_ARCHIVE}" -C "${BUILD_ROOT}"
fi

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
echo "Stamping exact Git provenance inside the extracted source..."
(
    cd "${SRC_DIR}"
    rm -rf .git
    git init -q
    git remote add origin "https://github.com/mgba-emu/mgba.git"
    git fetch -q --depth 1 origin "${MGBA_COMMIT}" \
        || fail "git fetch of the pinned commit ${MGBA_COMMIT} failed (no branch/tag fallback)."
    git reset -q --hard FETCH_HEAD
    HEAD_SHA="$(git rev-parse HEAD)"
    if [ "${HEAD_SHA}" != "${MGBA_COMMIT}" ]; then
        fail "Inner Git HEAD ${HEAD_SHA} does not match the pinned commit ${MGBA_COMMIT}."
    fi
    if [ -n "$(git status --porcelain)" ]; then
        fail "Inner Git tree is not clean after reset to the pinned commit."
    fi
    echo "  inner provenance verified: ${HEAD_SHA}"
)

# --- Isolated virtual environment ------------------------------------------
# The venv layout differs by platform: POSIX Pythons use ``bin/python`` while a
# Windows/MSYS2 UCRT64 Python uses ``Scripts/python.exe``. Detect either.
detect_venv_python() {
    local p
    for p in "${VENV_DIR}/bin/python" "${VENV_DIR}/bin/python3" \
             "${VENV_DIR}/Scripts/python.exe" "${VENV_DIR}/Scripts/python"; do
        if [ -x "$p" ]; then
            printf '%s\n' "$p"
            return 0
        fi
    done
    return 1
}

if ! detect_venv_python >/dev/null 2>&1; then
    echo "Creating isolated virtual environment..."
    "${PYTHON_BIN}" -m venv "${VENV_DIR}"
fi
VENV_PY="$(detect_venv_python)" \
    || fail "Could not find the venv interpreter under bin/ or Scripts/ in ${VENV_DIR}."

echo "Installing hash-locked build prerequisites (stage 1: pinned wheels)..."
"${VENV_PY}" -m pip install --require-hashes --only-binary ":all:" -r "${REQUIREMENTS_BOOTSTRAP}"

echo "Installing hash-locked Python build dependencies (stage 2: pinned sources)..."
"${VENV_PY}" -m pip install --require-hashes --no-build-isolation --no-binary ":all:" -r "${REQUIREMENTS_BUILD}"

# --- Build libmgba + display-free Python binding ---------------------------
CMAKE_BUILD="${SRC_DIR}/build-playtest"
mkdir -p "${CMAKE_BUILD}"

echo "Configuring libmgba (headless, fixed color depth / sync options)..."
# Select a GCC-compatible generator (Ninja preferred, else Unix Makefiles).
# This deliberately avoids any Visual Studio / MSVC generator: mGBA 0.10.5's
# Python binding is a GCC/MinGW-only build.
if command -v ninja >/dev/null 2>&1; then
    GENERATOR=(-G Ninja)
elif command -v make >/dev/null 2>&1 || command -v mingw32-make >/dev/null 2>&1; then
    GENERATOR=(-G "Unix Makefiles")
else
    fail "No GCC-compatible CMake generator found. Install Ninja or Make."
fi
(
    cd "${CMAKE_BUILD}"
    cmake .. \
        "${GENERATOR[@]}" \
        -DBUILD_PYTHON=ON \
        -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_GL=OFF -DBUILD_GLES2=OFF \
        -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF \
        -DCOLOR_16_BIT=ON -DCOLOR_5_6_5=ON \
        -DPYTHON_EXECUTABLE="${VENV_PY}"
)
echo "Building libmgba..."
cmake --build "${CMAKE_BUILD}" --config Release
echo "Installing the display-free Python binding (mgba-py-install)..."
# mGBA 0.10.5 defines a custom target 'mgba-py-install' (there is no Python
# install *component*). This MUST succeed; no fail-open fallback.
cmake --build "${CMAKE_BUILD}" --target mgba-py-install --config Release

# --- Record the native DLL search directories (Windows loader strategy) -----
# A UCRT64 Python launched outside the MSYS2 shell (e.g. from PowerShell/.NET)
# does not resolve the binding's dependent DLLs via ``runtime_library_dirs``.
# Record the build output dir (libmgba) and, under MSYS2, the UCRT64 ``bin``
# (libgcc/libwinpthread) as NATIVE paths; the runtime adapter registers them
# with ``os.add_dll_directory`` before importing mgba. Harmless on POSIX (the
# adapter no-ops there). This is the single strategy shared with the adapter.
DLL_MANIFEST="${BUILD_ROOT}/mgba-dll-dirs.txt"
to_native_path() {
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$1"
    else
        printf '%s\n' "$1"
    fi
}
{
    to_native_path "${CMAKE_BUILD}"
    if [ -n "${MSYSTEM:-}" ] && [ -d "/ucrt64/bin" ]; then
        to_native_path "/ucrt64/bin"
    fi
} > "${DLL_MANIFEST}"
echo "  recorded DLL search dirs -> ${DLL_MANIFEST}"

# --- Direct native import + provenance probe (diagnoses loader failures) ----
# Runs BEFORE --check so a DLL/loader failure is surfaced directly (with the
# real error) instead of folded into the sanitized runtime result. Asserts the
# effective binding is exactly 0.10.5 at the pinned commit -- never
# None/unknown/short SHA.
echo "Probing the native binding import and exact provenance..."
(
    cd "${TOOL_DIR}"
    "${VENV_PY}" -c 'import febuildergba_playtest.mgba_backend as b; b.prepare_native_library_search(); import mgba; v = mgba.__version__; c = getattr(getattr(mgba, "Git", None), "commit", None); print("mgba", v, c); assert v == "0.10.5", ("unexpected version", v); assert c == "26b7884bc25a5933960f3cdcd98bac1ae14d42e2", ("unexpected commit", c)'
) || fail "Native import/provenance probe failed (see the loader error above)."

# --- Verify ----------------------------------------------------------------
echo "Verifying the pinned binding with --playtest --check..."
if ( cd "${TOOL_DIR}" && "${VENV_PY}" -m febuildergba_playtest --check ); then
    echo "== Bootstrap complete. mGBA ${MGBA_VERSION} binding is ready. =="
    echo "Use this interpreter for playtesting: ${VENV_PY}"
else
    fail "Binding built but --check failed. Inspect the diagnostics above."
fi
