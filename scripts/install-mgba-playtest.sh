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
REQUIREMENTS="${TOOL_DIR}/requirements-mgba-build.txt"

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
    fail "No C compiler found (cc / gcc / clang). Install build-essential or Xcode command-line tools."
fi
require_cmd "curl" "Install curl to fetch the pinned source archive."
require_cmd "tar" "Install tar to extract the source archive."

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

# --- Isolated virtual environment ------------------------------------------
if [ ! -x "${VENV_DIR}/bin/python" ]; then
    echo "Creating isolated virtual environment..."
    "${PYTHON_BIN}" -m venv "${VENV_DIR}"
fi
VENV_PY="${VENV_DIR}/bin/python"

echo "Installing hash-locked Python build dependencies..."
"${VENV_PY}" -m pip install --require-hashes --no-build-isolation --no-binary ":all:" -r "${REQUIREMENTS}"

# --- Build libmgba + display-free Python binding ---------------------------
CMAKE_BUILD="${SRC_DIR}/build-playtest"
mkdir -p "${CMAKE_BUILD}"

echo "Configuring libmgba (headless, fixed color depth / sync options)..."
(
    cd "${CMAKE_BUILD}"
    cmake .. \
        -DBUILD_PYTHON=ON \
        -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_GL=OFF -DBUILD_GLES2=OFF \
        -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF \
        -DCOLOR_16_BIT=ON -DCOLOR_5_6_5=ON \
        -DPYTHON_EXECUTABLE="${VENV_PY}"
    cmake --build . --config Release
    cmake --install . --component python 2>/dev/null || true
)

# --- Verify ----------------------------------------------------------------
echo "Verifying the pinned binding with --playtest --check..."
if ( cd "${TOOL_DIR}" && "${VENV_PY}" -m febuildergba_playtest --check ); then
    echo "== Bootstrap complete. mGBA ${MGBA_VERSION} binding is ready. =="
    echo "Use this interpreter for playtesting: ${VENV_PY}"
else
    fail "Binding built but --check failed. Inspect the diagnostics above."
fi
