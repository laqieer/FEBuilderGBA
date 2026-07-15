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

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd -P)"
TOOL_DIR="${REPO_ROOT}/tools/gba-playtest"
if [ -L "${TOOL_DIR}" ]; then
    echo "Refusing symlinked playtest tool directory: ${TOOL_DIR}" >&2
    exit 1
fi
TOOL_DIR_CANON="$(cd "${TOOL_DIR}" && pwd -P)"
EXPECTED_TOOL_DIR="${REPO_ROOT}/tools/gba-playtest"
if [ "${TOOL_DIR_CANON}" != "${EXPECTED_TOOL_DIR}" ]; then
    echo "Refusing playtest tool directory outside the repository: ${TOOL_DIR_CANON}" >&2
    exit 1
fi

BUILD_ROOT="${TOOL_DIR_CANON}/.mgba-build"
VENV_DIR="${BUILD_ROOT}/venv"
SRC_ARCHIVE="${BUILD_ROOT}/mgba-${MGBA_COMMIT}.tar.gz"
SRC_DIR="${BUILD_ROOT}/mgba-${MGBA_COMMIT}"
# The CMake build tree lives OUTSIDE the extracted source so the source can be
# recreated deterministically on every run (setup.py leaves egg-info/build
# artifacts that would otherwise make a rerun without --force fail its inner-git
# cleanliness check) and so a stray build dir never pollutes provenance.
CMAKE_BUILD="${BUILD_ROOT}/build-playtest"
REQUIREMENTS_BOOTSTRAP="${TOOL_DIR_CANON}/requirements-mgba-bootstrap.txt"
REQUIREMENTS_BUILD="${TOOL_DIR_CANON}/requirements-mgba-build.txt"

# Canonical, repository-owned paths that guarded deletion is allowed to touch.
# ``pwd -P`` resolves symlinks physically; ``TOOL_DIR`` always exists.
BUILD_ROOT_CANON="${TOOL_DIR_CANON}/.mgba-build"
SRC_DIR_CANON="${BUILD_ROOT_CANON}/mgba-${MGBA_COMMIT}"
CMAKE_BUILD_CANON="${BUILD_ROOT_CANON}/build-playtest"

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

# Guarded recursive delete. Refuses to remove anything that is not one of the
# three canonical, repository-owned build paths (the build root, the pinned
# source dir, or the out-of-source CMake build dir), rejects symlinked roots,
# and refuses an empty target. A no-op when the target does not exist.
safe_rm_rf() {
    local target="$1"
    [ -n "${target}" ] || fail "refusing to remove an empty path"
    if [ -L "${target}" ]; then
        fail "refusing to remove a symlinked path: ${target}"
    fi
    if [ -e "${BUILD_ROOT}" ] && [ -L "${BUILD_ROOT}" ]; then
        fail "refusing to operate under a symlinked build root."
    fi
    local parent base canon
    parent="$(dirname "${target}")"
    base="$(basename "${target}")"
    [ -d "${parent}" ] || return 0
    canon="$(cd "${parent}" && pwd -P)/${base}" \
        || fail "refusing to remove a path with an unresolved parent: ${target}"
    case "${canon}" in
        "${BUILD_ROOT_CANON}"|"${SRC_DIR_CANON}"|"${CMAKE_BUILD_CANON}") ;;
        *) fail "refusing to remove an unexpected path: ${canon}" ;;
    esac
    rm -rf "${canon}"
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
require_cmd "mktemp" "Install coreutils so temporary files can be created safely."
require_cmd "pkg-config" "Install pkg-config so native build dependencies can be verified."

for dependency in epoxy libffi libpng zlib; do
    if ! pkg-config --exists "${dependency}"; then
        fail "Required native dependency '${dependency}' was not found via pkg-config."
    fi
    echo "  found ${dependency} -> $(pkg-config --modversion "${dependency}")"
done

PY_VERSION="$("${PYTHON_BIN}" -c 'import sys; print("%d.%d" % sys.version_info[:2])')"
echo "  python version ${PY_VERSION}"
if ! "${PYTHON_BIN}" -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)'; then
    fail "Python 3.10 or newer is required (found ${PY_VERSION})."
fi

if [ "${FORCE}" -eq 1 ] && [ -d "${BUILD_ROOT}" ]; then
    echo "Removing existing build root (--force)..."
    safe_rm_rf "${BUILD_ROOT}"
fi
if [ -L "${BUILD_ROOT}" ]; then
    fail "Refusing symlinked build root: ${BUILD_ROOT}"
fi
mkdir -p "${BUILD_ROOT}"
BUILD_ROOT_ACTUAL="$(cd "${BUILD_ROOT}" && pwd -P)"
if [ "${BUILD_ROOT_ACTUAL}" != "${BUILD_ROOT_CANON}" ]; then
    fail "Refusing build root outside the playtest tool directory: ${BUILD_ROOT_ACTUAL}"
fi

# --- Fetch pinned source archive and verify SHA-256 before extraction ------
if [ -L "${SRC_ARCHIVE}" ]; then
    fail "Refusing symlinked source archive: ${SRC_ARCHIVE}"
fi
if [ ! -f "${SRC_ARCHIVE}" ]; then
    echo "Downloading pinned mGBA commit archive..."
    ARCHIVE_TMP="$(mktemp "${BUILD_ROOT}/mgba-source.tar.gz.tmp.XXXXXX")"
    if ! curl -fsSL "${ARCHIVE_URL}" -o "${ARCHIVE_TMP}"; then
        rm -f -- "${ARCHIVE_TMP}"
        fail "Could not download the pinned source archive."
    fi
    mv -f -- "${ARCHIVE_TMP}" "${SRC_ARCHIVE}"
fi

echo "Verifying archive SHA-256 before extraction..."
ACTUAL="$(sha256_of "${SRC_ARCHIVE}")"
if [ "${ACTUAL}" != "${ARCHIVE_SHA}" ]; then
    rm -f "${SRC_ARCHIVE}"
    fail "Archive SHA-256 mismatch. Expected ${ARCHIVE_SHA} but got ${ACTUAL}. Refusing to extract (no fallback)."
fi
echo "  archive verified: ${ACTUAL}"

# Recreate the extracted source deterministically on EVERY run so a rerun
# without --force is retry-safe: a prior build leaves setup.py egg-info/build
# artifacts inside the tree that would otherwise fail the inner-git cleanliness
# check below. The verified archive is cached; only the extracted tree is reset.
echo "Recreating pinned source tree from the verified archive..."
safe_rm_rf "${SRC_DIR}"
tar -xzf "${SRC_ARCHIVE}" -C "${BUILD_ROOT}"

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
    # The source is recreated from the verified archive on every run and the
    # codeload tarball carries no ``.git``; a pre-existing one would be
    # unexpected and is refused rather than blindly removed.
    [ -e .git ] && fail "unexpected .git in the freshly extracted source tree"
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

if [ -L "${VENV_DIR}" ]; then
    fail "Refusing symlinked virtual environment: ${VENV_DIR}"
fi
if ! detect_venv_python >/dev/null 2>&1; then
    echo "Creating isolated virtual environment..."
    "${PYTHON_BIN}" -m venv "${VENV_DIR}"
fi
VENV_DIR_ACTUAL="$(cd "${VENV_DIR}" && pwd -P)"
if [ "${VENV_DIR_ACTUAL}" != "${VENV_DIR}" ]; then
    fail "Refusing virtual environment outside the build root: ${VENV_DIR_ACTUAL}"
fi
VENV_PY="$(detect_venv_python)" \
    || fail "Could not find the venv interpreter under bin/ or Scripts/ in ${VENV_DIR}."

echo "Installing hash-locked build prerequisites (stage 1: pinned wheels)..."
"${VENV_PY}" -m pip install --require-hashes --only-binary ":all:" -r "${REQUIREMENTS_BOOTSTRAP}"

echo "Installing hash-locked Python build dependencies (stage 2: pinned sources)..."
"${VENV_PY}" -m pip install --require-hashes --no-build-isolation --no-binary ":all:" -r "${REQUIREMENTS_BUILD}"

# --- Lock the build/install phase offline ----------------------------------
# Every Python build/setup dependency is now installed with verified hashes.
# setup.py's setup_requires (cffi / pytest-runner) are therefore already
# satisfied, so mgba-py-install must not perform ANY implicit network fetch.
# Enforce it: an internal pip/setuptools fetch would fail closed under
# --no-index rather than silently downloading an unpinned package. The only
# permitted network phases remain the (already-completed) archive download and
# the pinned-commit inner-git fetch above.
export PIP_NO_INDEX=1
export PIP_NO_BUILD_ISOLATION=1

# --- Build libmgba + display-free Python binding ---------------------------
# Recreate the out-of-source build tree deterministically each run.
echo "Preparing out-of-source CMake build tree..."
safe_rm_rf "${CMAKE_BUILD}"
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
cmake -S "${SRC_DIR}" -B "${CMAKE_BUILD}" \
    "${GENERATOR[@]}" \
    -DBUILD_PYTHON=ON \
    -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_GL=OFF -DBUILD_GLES2=OFF \
    -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF \
    -DUSE_PNG=ON -DUSE_ZLIB=ON \
    -DCOLOR_16_BIT=ON -DCOLOR_5_6_5=ON \
    -DPYTHON_EXECUTABLE="${VENV_PY}"
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
DLL_MANIFEST_TMP="$(mktemp "${BUILD_ROOT}/mgba-dll-dirs.txt.tmp.XXXXXX")"
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
} > "${DLL_MANIFEST_TMP}"
mv -f -- "${DLL_MANIFEST_TMP}" "${DLL_MANIFEST}"
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
