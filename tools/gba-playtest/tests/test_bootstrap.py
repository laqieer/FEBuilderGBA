"""Static safety checks for the bootstrap scripts and runtime package.

These tests read files as text (no execution) to enforce the security policy.

Architecture note
-----------------
``install-mgba-playtest.sh`` is the single, authoritative fail-hard bootstrap:
it pins the exact official commit, verifies its SHA-256 before extraction (no
fallback), stamps inner Git provenance, installs hash-locked build dependencies,
builds the display-free binding, and runs ``--check`` (exact version + commit).

``install-mgba-playtest.ps1`` is an HONEST Windows wrapper. mGBA 0.10.5's Python
binding is a GCC/MinGW-only build (MSVC is unsupported upstream --
mgba-emu/mgba#1637, closed not-planned), so the wrapper locates a user-installed
MSYS2 UCRT64 environment, validates its toolchain (never installing it), and
delegates to the same POSIX script under the UCRT64 login shell. All build-logic
invariants are therefore asserted against the POSIX script; the PowerShell
wrapper has its own contract (MSYS2 detection, no MSVC, no global env mutation).
"""

import ast
import os
import re
import subprocess
import sys
from urllib.parse import urlsplit

import pytest

import febuildergba_playtest

PKG_DIR = os.path.dirname(os.path.abspath(febuildergba_playtest.__file__))
TOOL_DIR = os.path.dirname(PKG_DIR)
REPO_ROOT = os.path.dirname(os.path.dirname(TOOL_DIR))
SCRIPTS_DIR = os.path.join(REPO_ROOT, "scripts")

PINNED_COMMIT = "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"
ARCHIVE_SHA = "9475c26e9fa2f4b30c07ab6636e4b0a5b62e4baee2109ede7b2fecc52edae366"

PS1 = os.path.join(SCRIPTS_DIR, "install-mgba-playtest.ps1")
SH = os.path.join(SCRIPTS_DIR, "install-mgba-playtest.sh")
CFFI_WRAPPER = os.path.join(SCRIPTS_DIR, "mgba_cffi_preprocessor.py")
SETUPTOOLS_SHIM = os.path.join(
    TOOL_DIR, "setuptools-shim", "sitecustomize.py"
)
README = os.path.join(TOOL_DIR, "README.md")
REQUIREMENTS = os.path.join(TOOL_DIR, "requirements-mgba-build.txt")
REQUIREMENTS_BOOTSTRAP = os.path.join(TOOL_DIR, "requirements-mgba-bootstrap.txt")
REAL_PROOF = os.path.join(os.path.dirname(__file__), "run_real_mgba_proof.py")
REAL_WORKFLOW = os.path.join(REPO_ROOT, ".github", "workflows", "gba-playtest.yml")

# ``install-mgba-playtest.sh`` owns all build logic; ``.ps1`` delegates to it.
BUILD_SCRIPT = SH

# Every package name that must appear, hash-pinned, across the two stages.
REQUIRED_BOOTSTRAP_PACKAGES = ("setuptools", "wheel", "pytest-runner")
REQUIRED_BUILD_PACKAGES = ("cffi", "pycparser", "cached-property")


def _read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def _https_urls(text):
    return [
        urlsplit(match.group(0).rstrip(".,)"))
        for match in re.finditer(r"https://[^\s'\"`]+", text)
    ]


# Matches grep/cat/awk/sed only when actually invoked as a shell command --
# i.e. immediately preceded by a real statement/command boundary: start of
# line, `;`, `&`, `|`, `(` (subshell or `$(...)`/`` `...` `` command
# substitution), or a shell keyword (`if`/`elif`/`then`/`while`/`until`),
# optionally with a `!` pipeline negation in between (e.g. `if ! grep ...`).
# Word boundaries around the command name mean prose suffixes such as
# "clo{sed}", "ba{sed}", "u{sed}" are never treated as the command.
_SHELL_COMMAND_START = re.compile(
    r"(?:^|[;&|(]|\bif\b|\belif\b|\bthen\b|\bwhile\b|\buntil\b)"
    r"\s*(?:!\s*)?"
    r"\b(grep|cat|awk|sed)\b"
)


def _shell_command_reads_cmakecache(text):
    """True if grep/cat/awk/sed is actually INVOKED (on a non-comment line) to
    read CMakeCache.txt -- as opposed to the string merely appearing in prose
    or comments discussing why CMakeCache.txt is not trusted.

    Every command match on a line is checked independently, and for each match
    ``CMakeCache.txt`` is searched for only AFTER that match ends. This means
    an earlier decoy occurrence of the literal string (e.g. in an unrelated
    ``echo`` before a real ``grep`` later on the same line) can never mask a
    real invocation that follows it.
    """
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if "CMakeCache.txt" not in line:
            continue
        for match in _SHELL_COMMAND_START.finditer(line):
            if line.find("CMakeCache.txt", match.end()) != -1:
                return True
    return False


def test_bootstrap_scripts_exist():
    assert os.path.isfile(PS1)
    assert os.path.isfile(SH)
    assert os.path.isfile(REAL_PROOF)
    assert os.path.isfile(REAL_WORKFLOW)


# --------------------------------------------------------------------------- #
# POSIX build-script invariants (authoritative bootstrap)
# --------------------------------------------------------------------------- #
def test_build_script_pins_exact_commit_and_archive_hash():
    text = _read(BUILD_SCRIPT)
    assert PINNED_COMMIT in text, "the build script must pin the exact commit"
    assert ARCHIVE_SHA in text, "the build script must pin the archive SHA-256"


def test_build_script_only_uses_official_mgba_source():
    text = _read(BUILD_SCRIPT)
    assert "mgba-emu/mgba" in text
    # No alternate mirrors / forks.
    assert not re.search(r"github\.com/(?!mgba-emu/mgba)", text)


def test_build_script_verifies_before_extraction_with_no_fallback():
    text = _read(BUILD_SCRIPT).lower()
    assert "sha-256 mismatch" in text or "sha256 mismatch" in text
    assert "no fallback" in text
    for ref in (
        "/archive/refs/heads",
        "/archive/refs/tags",
        "tar.gz/refs/tags",
        "zip/refs/tags",
        "git checkout main",
        "git checkout master",
    ):
        assert ref not in text
    # The downloaded source archive itself must stay keyed by the exact
    # commit, never by the version/tag (the tag is only cross-checked as
    # verified metadata against the already-pinned commit elsewhere).
    assert re.search(
        r'archive_url="https://codeload\.github\.com/mgba-emu/mgba/tar\.gz/\$\{mgba_commit\}"',
        text,
    ), "the source archive URL must be the official codeload commit archive keyed by MGBA_COMMIT"
    assert "codeload.github.com/mgba-emu/mgba/tar.gz/${mgba_version}" not in text, (
        "the source archive URL must never be keyed by MGBA_VERSION/tag"
    )


def test_build_script_requires_hashes():
    assert "--require-hashes" in _read(BUILD_SCRIPT)


def test_build_script_references_both_requirement_stages():
    text = _read(BUILD_SCRIPT)
    assert "requirements-mgba-bootstrap.txt" in text, "must install stage-1 wheels"
    assert "requirements-mgba-build.txt" in text, "must install stage-2 sources"


def test_build_script_uses_fail_hard_bdist_and_local_wheel_contract():
    text = _read(BUILD_SCRIPT)
    assert "mgba-py-bdist" in text, "must build the pinned source's wheel target"
    assert "mgba-py-install" not in text, "must not use the incompatible install target"
    assert "--component python" not in text, "must not use a Python install component"
    assert re.search(r'MGBA_PY_DIST="\$\{SRC_DIR\}/src/platform/python/dist"', text)
    assert re.search(
        r'\[\s+-e\s+"\$\{MGBA_PY_DIST\}"\s+\]\s+\|\|\s+\[\s+-L\s+"\$\{MGBA_PY_DIST\}"\s+\]',
        text,
    ), "must reject a pre-existing source dist directory"
    assert re.search(r'MGBA_WHEELS=\("\$\{MGBA_PY_DIST\}"/\*\.whl\)', text)
    assert re.search(r'MGBA_WHEELS\[@\].*-\s*eq\s+0', text), "must reject zero wheels"
    assert re.search(r'MGBA_WHEELS\[@\].*-\s*ne\s+1', text), "must require exactly one wheel"
    assert 'MGBA_WHEEL="${MGBA_WHEELS[0]}"' in text, "must resolve exactly one wheel"
    assert 'MGBA_WHEEL}" ] || [ ! -f "${MGBA_WHEEL}' in text, (
        "must reject symlinked and non-regular wheels"
    )
    assert '"mgba-${MGBA_VERSION}-"*.whl | "mgba-${MGBA_VERSION}."*.whl' in text, (
        "wheel name must use a version delimiter, not only a broad prefix"
    )
    assert '"mgba-${MGBA_VERSION}"*.whl' not in text, "must reject version-prefix collisions"
    assert "MGBA_WHEEL_SHA_BEFORE" in text and "MGBA_WHEEL_SHA_AFTER" in text
    assert '[ "${MGBA_WHEEL_SHA_AFTER}" != "${MGBA_WHEEL_SHA_BEFORE}" ]' in text, (
        "wheel SHA-256 must be compared before and after installation"
    )
    assert re.search(r'pip install[^\n]*"\$\{MGBA_WHEEL\}"', text), (
        "pip must install the exact resolved wheel variable"
    )
    assert "|| true" not in text, "must not fail-open the wheel build or install"


def test_build_script_stamps_inner_git_provenance():
    # The codeload tarball has no .git; version.cmake would run ``git describe``
    # and walk up into the parent FEBuilderGBA repo, mis-stamping the binding.
    text = _read(BUILD_SCRIPT)
    assert "git init" in text, "must initialize an inner git repo"
    assert any(
        url.scheme == "https"
        and url.hostname == "github.com"
        and url.port is None
        and url.path == "/mgba-emu/mgba.git"
        and not url.query
        and not url.fragment
        for url in _https_urls(text)
    ), "must add the exact official mGBA origin"
    assert re.search(r"git\s+remote\s+add\s+origin", text), "must add origin"
    assert re.search(r"git\s+fetch\b[^\n]*--depth\s+1", text), "must fetch depth 1"
    assert "FETCH_HEAD" in text, "must reset to FETCH_HEAD"
    assert re.search(r"git\s+reset\b[^\n]*--hard", text), "must hard reset to the pin"
    assert "rev-parse HEAD" in text, "must verify HEAD equals the pin"
    assert "status --porcelain" in text, "must verify a clean tree"
    assert re.search(r'require_cmd\s+"git"', text), "must validate git before use"


def test_build_script_git_fetch_targets_exact_pinned_commit_no_fallback():
    text = _read(BUILD_SCRIPT)
    assert re.search(r"git\s+fetch[^\n]*origin[^\n]*\$\{MGBA_COMMIT\}", text), (
        "must fetch the exact pinned commit"
    )
    assert not re.search(r"git\s+fetch[^\n]*origin\s+(main|master|HEAD)\b", text), (
        "must not fetch a branch/HEAD"
    )
    assert not re.search(r"git\s+checkout\s+(main|master)", text), "must not checkout a branch"
    assert "refs/heads" not in text, "must not reference branch refs"
    assert re.search(
        r'git\s+fetch\s+-q\s+origin\s+'
        r'"refs/tags/\$\{MGBA_VERSION\}:refs/tags/\$\{MGBA_VERSION\}"',
        text,
    ), "must fetch ONLY the exact official lightweight tag ref, as a plain ref (no branch/HEAD fallback)"


def test_build_script_tag_provenance_cross_checks_pinned_commit():
    # The tag is verified RELEASE METADATA for the already-pinned commit, not
    # a source selector or fallback: the commit fetch/reset/HEAD check above
    # remains the sole source of truth, and this only cross-checks that the
    # official "0.10.5" tag resolves to that SAME commit.
    text = _read(BUILD_SCRIPT)
    assert re.search(
        r'TAG_COMMIT="\$\(git rev-parse "refs/tags/\$\{MGBA_VERSION\}\^\{commit\}"\)"',
        text,
    ), "must resolve the tag to its target commit via tag^{commit}, not just the ref"
    assert re.search(
        r'if\s+\[\s+"\$\{TAG_COMMIT\}"\s+!=\s+"\$\{MGBA_COMMIT\}"\s+\]', text
    ), "must explicitly compare the resolved tag commit against the pinned commit"
    assert re.search(
        r'fail\s+"Official tag refs/tags/\$\{MGBA_VERSION\} resolves to \$\{TAG_COMMIT\},'
        r' not the pinned commit \$\{MGBA_COMMIT\}',
        text,
    ), "must fail closed with a clear mismatch message naming both commits"
    assert re.search(
        r'git fetch -q origin[\s\S]{0,300}?failed \(no branch/HEAD fallback\)', text
    ), "tag fetch failure must also fail closed with no fallback"
    # Mutation-bindable: the fetch destination refspec must be present, not
    # just the source side, so a mutant dropping the local ref cannot pass.
    assert text.count("refs/tags/${MGBA_VERSION}") >= 4, (
        "tag ref must appear in fetch (src+dst), rev-parse target, and fail message"
    )
    assert "NOT a source selector or fallback" in text, (
        "must document the tag as verified metadata, never a selector/fallback"
    )


def test_build_script_git_provenance_runs_after_verification_before_cmake():
    text = _read(BUILD_SCRIPT)
    i_verify = text.find("archive verified")
    i_git = text.find("git init")
    i_cmake = text.find("cmake -S")
    assert -1 < i_verify < i_git < i_cmake, (
        "git provenance must sit between archive verification and cmake"
    )


def test_build_script_tag_check_runs_after_commit_verification_before_cmake():
    # Mutation-bindable: deleting the tag fetch/compare step, or reordering it
    # before the exact-commit HEAD check or after cmake, collapses this chain
    # (a missing marker returns -1, which cannot satisfy the ordering).
    text = _read(BUILD_SCRIPT)
    i_head_verify = text.find('does not match the pinned commit')
    i_tag_fetch = text.find('refs/tags/${MGBA_VERSION}:refs/tags/${MGBA_VERSION}')
    i_tag_compare = text.find('"${TAG_COMMIT}" != "${MGBA_COMMIT}"')
    i_cmake = text.find("cmake -S")
    assert -1 < i_head_verify < i_tag_fetch < i_tag_compare < i_cmake, (
        "tag provenance must be verified after the exact-commit check and before cmake"
    )


def test_build_script_supports_both_venv_layouts():
    text = _read(BUILD_SCRIPT)
    assert "Scripts/python.exe" in text, "must support the Windows/MSYS2 venv layout"
    assert re.search(r"bin/python\b", text), "must support the POSIX venv layout"


def test_build_script_uses_gcc_compatible_generator_not_msvc():
    text = _read(BUILD_SCRIPT)
    assert re.search(r"-G\s+Ninja", text) or "Unix Makefiles" in text, (
        "must pick a GCC-compatible generator"
    )
    # Must not *select* an MSVC generator (the word may appear only to disclaim it).
    assert not re.search(r'-G\s+"?Visual Studio', text), "must not select an MSVC generator"


def test_build_script_records_dll_manifest():
    text = _read(BUILD_SCRIPT)
    assert "mgba-dll-dirs.txt" in text, "must record the DLL search manifest"
    # Build output dir (libmgba) is recorded, plus UCRT64 bin under MSYS2.
    assert "${CMAKE_BUILD}" in text
    assert "/ucrt64/bin" in text, "must record the UCRT64 bin under MSYS2"
    # Native (Windows) paths so os.add_dll_directory can consume them.
    assert re.search(r"cygpath\s+-w", text), "must convert to native paths with cygpath -w"


def test_build_script_probes_native_import_and_exact_provenance():
    text = _read(BUILD_SCRIPT)
    # A direct import probe runs so loader failures are diagnosed directly...
    assert "prepare_native_library_search" in text, "probe must register DLL dirs"
    assert re.search(r"import\s+mgba\b", text), "probe must import the native binding"
    # ...and it asserts the exact effective version + pinned commit.
    assert re.search(r"__version__[^\n]*0\.10\.5", text) or '== "0.10.5"' in text, (
        "probe must assert exact runtime version"
    )
    assert PINNED_COMMIT in text
    assert "Git" in text and "commit" in text, "probe must read mgba.Git.commit"
    # The direct probe precedes the actual --check invocation.
    i_probe = text.find("prepare_native_library_search")
    i_check = text.rfind("febuildergba_playtest --check")
    assert -1 < i_probe < i_check, "the import probe must run before --check"


def test_build_script_builds_out_of_source():
    text = _read(BUILD_SCRIPT)
    # The CMake build tree must live OUTSIDE the extracted source so the source
    # can be recreated each run and never pollutes version.cmake provenance.
    assert re.search(r"CMAKE_BUILD=\"\$\{BUILD_ROOT\}/", text), (
        "the CMake build dir must be under BUILD_ROOT, outside the source tree"
    )
    assert re.search(r"cmake\s+-S\s+\"\$\{SRC_DIR\}\"\s+-B\s+\"\$\{CMAKE_BUILD\}\"", text), (
        "must configure out-of-source with cmake -S/-B"
    )
    assert "cmake .." not in text, "must not configure in-source with 'cmake ..'"


def test_build_script_recreates_source_each_run_for_retry_safety():
    text = _read(BUILD_SCRIPT)
    # setup.py leaves egg-info/build artifacts inside the source, so a rerun
    # without --force must deterministically recreate the extracted tree (and
    # the out-of-source build dir) instead of reusing a now-dirty one.
    assert re.search(r"safe_rm_rf\s+\"\$\{SRC_DIR\}\"", text), (
        "must remove the stale source tree each run"
    )
    assert re.search(r"safe_rm_rf\s+\"\$\{CMAKE_BUILD\}\"", text), (
        "must remove the stale build tree each run"
    )
    # A leftover ``if [ ! -d SRC_DIR ]`` extraction guard would defeat retry
    # safety: extraction must be unconditional after the guarded removal.
    assert not re.search(r"if\s+\[\s+!\s+-d\s+\"\$\{SRC_DIR\}\"\s+\]", text), (
        "extraction must be unconditional (no stale-source reuse guard)"
    )


def test_build_script_guards_every_recursive_delete():
    text = _read(BUILD_SCRIPT)
    # A guarded deletion helper must exist and constrain removals to the three
    # canonical repository-owned paths, and reject symlinked roots + empty args.
    assert "safe_rm_rf()" in text, "must define a guarded deletion helper"
    assert re.search(r"BUILD_ROOT_CANON|SRC_DIR_CANON|CMAKE_BUILD_CANON", text), (
        "guard must canonicalize the allowed targets"
    )
    assert re.search(r'refusing to remove an empty path', text), "guard must reject empty targets"
    assert re.search(r'refusing to remove a symlinked path', text), "guard must reject symlinks"
    assert re.search(r'refusing to remove an unexpected path', text), (
        "guard must reject non-canonical targets"
    )
    # Every destructive rm -rf must go through the guard: the only bare 'rm -rf'
    # allowed is the one *inside* the helper itself.
    bare = re.findall(r"^\s*rm\s+-rf\b(?!.*safe_rm_rf)", text, re.MULTILINE)
    assert len(bare) == 1, "only the guarded helper may call 'rm -rf' directly"


def test_build_script_contains_all_mutable_roots_before_writing():
    text = _read(BUILD_SCRIPT)
    assert "pwd -P" in text, "repository and tool roots must be resolved physically"
    assert re.search(r'\[\s+-L\s+"\$\{TOOL_DIR\}"\s+\]', text)
    assert re.search(r'\[\s+-L\s+"\$\{BUILD_ROOT\}"\s+\]', text)
    assert re.search(r'\[\s+-L\s+"\$\{VENV_DIR\}"\s+\]', text)
    assert re.search(r'\[\s+-L\s+"\$\{SRC_ARCHIVE\}"\s+\]', text)
    assert "EXPECTED_TOOL_DIR" in text
    assert "BUILD_ROOT_ACTUAL" in text
    assert "VENV_DIR_ACTUAL" in text


def test_build_script_publishes_download_and_manifest_via_temp_files():
    text = _read(BUILD_SCRIPT)
    assert re.search(r'ARCHIVE_TMP=.*mktemp', text)
    assert re.search(
        r'mv\s+-f\s+--\s+"\$\{ARCHIVE_TMP\}"\s+"\$\{SRC_ARCHIVE\}"',
        text,
    )
    assert re.search(r'DLL_MANIFEST_TMP=.*mktemp', text)
    assert re.search(
        r'mv\s+-f\s+--\s+"\$\{DLL_MANIFEST_TMP\}"\s+"\$\{DLL_MANIFEST\}"',
        text,
    )


def test_build_script_requires_python_310_or_newer():
    text = _read(BUILD_SCRIPT)
    assert "sys.version_info >= (3, 10)" in text
    assert "Python 3.10 or newer is required" in text


def test_build_script_locks_build_phase_offline():
    text = _read(BUILD_SCRIPT)
    # After the hash-pinned pip stages, only one local-wheel install is allowed.
    # The lockdown must precede bdist and remain in effect for that install.
    lockdown = re.search(
        r"^export PIP_NO_INDEX=1\nexport PIP_NO_BUILD_ISOLATION=1$",
        text,
        re.MULTILINE,
    )
    assert lockdown, "both offline environment locks must be exported together"
    i_stage2 = text.find("requirements-mgba-build.txt")
    i_offline = lockdown.start()
    i_bdist = text.find("--target mgba-py-bdist")
    assert -1 < i_stage2 < i_offline < i_bdist, (
        "offline lockdown must sit after stage-2 pip and before mgba-py-bdist"
    )
    after = text[lockdown.end():]
    assert not re.search(
        r"^\s*(?:(?:unset|export\s+-n)\s+PIP_NO_(?:INDEX|BUILD_ISOLATION)|"
        r"(?:export\s+)?PIP_NO_(?:INDEX|BUILD_ISOLATION)=)",
        after,
        re.MULTILINE,
    )
    pip_installs = re.findall(r'^\s*"[^"\n]+"\s+-m\s+pip\s+install\s+.*$', after, re.MULTILINE)
    assert len(pip_installs) == 1, "exactly one actual pip install may follow lockdown"
    install = pip_installs[0]
    for flag in ("--no-index", "--no-deps", "--no-cache-dir", "--force-reinstall", '"${MGBA_WHEEL}"'):
        assert flag in install, f"offline wheel install must include {flag}"
    assert not re.search(r"https?://|git\+|(?:^|\s)(?:git|hg|svn|bzr)(?:\s|$)", after), (
        "no post-lockdown URL or VCS install path is allowed"
    )
    assert not re.search(r"(?:^|\s)(?:-r|--requirement)(?:\s|=)", install), (
        "offline wheel install must not use a requirements path"
    )


# --------------------------------------------------------------------------- #
# PowerShell wrapper contract (honest MSYS2 UCRT64 delegation)
# --------------------------------------------------------------------------- #
def test_powershell_is_msys2_ucrt64_wrapper():
    text = _read(PS1)
    low = text.lower()
    assert "msys2" in low, "wrapper must target MSYS2"
    assert "ucrt64" in low, "wrapper must target the UCRT64 environment"
    assert re.search(r"usr\\bin\\bash\.exe", text), "wrapper must locate MSYS2 bash"
    assert "install-mgba-playtest.sh" in text, "wrapper must delegate to the POSIX bootstrap"
    assert "cygpath" in text, "wrapper must convert the path structurally with cygpath"
    # No foreign source references.
    assert not re.search(r"github\.com/(?!mgba-emu/mgba)", text)


def test_powershell_detects_msys2_root_with_override():
    text = _read(PS1)
    assert re.search(r"\[string\]\s*\$Msys2Root", text), "wrapper must accept -Msys2Root"
    assert "MSYS2_ROOT" in text, "wrapper must honor the MSYS2_ROOT env override"
    assert "C:\\msys64" in text, "wrapper must fall back to the conventional root"


def test_powershell_validates_ucrt64_toolchain_via_probe():
    text = _read(PS1)
    for tool in ("python", "gcc", "cmake", "git", "curl", "tar"):
        assert re.search(rf"\b{tool}\b", text), f"wrapper must probe for {tool}"
    assert "/usr/bin/make" in text, "wrapper must require MSYS /usr/bin/make"
    assert "ninja-or-make" not in text, "wrapper must not use an ambiguous generator prerequisite"
    assert "command -v" in text, "wrapper must probe with command -v inside the shell"


def test_powershell_uses_direct_bash_script_execution_no_stdin_transport():
    text = _read(PS1)
    # The probe and the delegated POSIX script must both be executed DIRECTLY as
    # login-shell script arguments (`bash -l <path>`), never piped to Bash on
    # stdin. Windows PowerShell 5.1 prepends a UTF-8 BOM to stdin, which broke
    # `set -e` and the probe, so no stdin-script transport may remain.
    assert re.search(r"&\s*\$bash\s+-l\s+\$probePosix\b", text), (
        "the probe must be executed directly as `bash -l <converted-probe-path>`"
    )
    assert re.search(r"&\s*\$bash\s+-l\s+\$scriptPosix\s+@forward", text), (
        "the bootstrap must run directly as `bash -l <converted-script-path> @forward`"
    )
    # No stdin-script transport anywhere: neither `$probe | ...`, nor a
    # `$delegate` here-string, nor any `-s` stdin mode may survive.
    assert "| & $bash" not in text, "no script may be piped to Bash on stdin"
    assert not re.search(r"\$probe\s*\|\s*&", text), "the probe must not be piped to Bash"
    assert "$delegate" not in text, "the delegate here-string stdin transport must be removed"
    assert not re.search(r"&\s*\$bash\b[^\n]*\s-s\b", text), (
        "Bash must never be invoked in `-s` stdin-script mode"
    )
    assert not re.search(r"&\s*\$bash\s+-s\b", text), (
        "Bash must never be invoked without the -l login-shell flag"
    )
    # cygpath converts BOTH paths structurally under a LOGIN shell (-l), so
    # /etc/profile.d puts /usr/bin (cygpath) and /ucrt64/bin on PATH.
    assert re.search(r'''cygpath\s+-u\s+"\$1"'\s+--\s+\$WinPath''', text), (
        "paths must be passed as a positional argument to cygpath, not interpolated into shell code"
    )
    assert re.search(r"&\s*\$bash\s+-l\s+-c\s+'command -v cygpath", text), (
        "the wrapper must probe/require cygpath under the login shell"
    )
    assert "cygpath -u '$WinPath'" not in text, (
        "a quote-bearing Windows path must not be interpolated into the Bash command"
    )
    assert "Convert-PosixPath $probeFile" in text, "the probe path must be cygpath-converted"
    assert "Convert-PosixPath $PosixScript" in text, "the script path must be cygpath-converted"
    # Scoped probe ErrorActionPreference (Continue only around the captured
    # native invocation) with explicit, fail-closed exit-code handling remains.
    probe_try = text.find("$probePreference = $ErrorActionPreference")
    probe_call = text.find("$probeOut = & $bash -l $probePosix 2>&1", probe_try)
    probe_finally = text.find("$ErrorActionPreference = $probePreference", probe_call)
    assert -1 < probe_try < probe_call < probe_finally, (
        "probe Continue preference must be scoped and restored with try/finally"
    )
    assert "$probeExit = 1" in text, "probe exit must be initialized fail-closed"
    assert re.search(r"\$probeExit\s*=\s*\$LASTEXITCODE", text)
    assert re.search(r"if\s*\(\$probeExit\s+-ne\s+0\)", text), (
        "the probe exit must be checked fail-closed"
    )
    assert "$bootstrapExit = 1" in text, "bootstrap exit must be initialized fail-closed"
    assert re.search(r"\$bootstrapExit\s*=\s*\$LASTEXITCODE", text)
    assert re.search(r"if\s*\(\$bootstrapExit\s+-ne\s+0\)", text), (
        "bootstrap exit must remain explicit and fail-closed"
    )
    # cygpath conversion also fails closed on a nonzero exit / empty result.
    assert re.search(r"if\s*\(\$convExit\s+-ne\s+0\)", text), (
        "path conversion must fail closed on a nonzero cygpath exit"
    )


def test_powershell_materializes_probe_bom_free_lf_temp_file():
    text = _read(PS1)
    # No $OutputEncoding mutation or reliance may survive anywhere: the fix does
    # not depend on console encoding at all.
    assert "$OutputEncoding" not in text, (
        "the wrapper must neither mutate nor rely on $OutputEncoding"
    )
    # The probe is written to exactly one uniquely named temp file with an
    # explicit BOM-free UTF-8 encoding via WriteAllText.
    assert re.search(
        r"\[System\.IO\.File\]::WriteAllText\(\s*\$probeFile\s*,\s*\$probeLf\s*,\s*"
        r"\(New-Object\s+System\.Text\.UTF8Encoding\(\$false\)\)\s*\)",
        text,
    ), "the probe must be written with WriteAllText using UTF8Encoding($false) (no BOM)"
    # CRLF and CR are normalized to LF before the file is written.
    assert re.search(
        r'\$probeLf\s*=\s*\(\$probe\s*-replace\s*"`r`n",\s*"`n"\)\s*-replace\s*"`r",\s*"`n"',
        text,
    ), "the probe text must have CRLF/CR normalized to LF before writing"
    # A uniquely named temp file (GUID) under the OS temp path.
    assert "[System.IO.Path]::GetTempPath()" in text, "probe temp file must live under the temp path"
    assert "[Guid]::NewGuid()" in text, "the probe temp file name must be unique"
    # Exact literal cleanup of only that temp file: no recurse, no wildcard.
    assert re.search(r"Remove-Item\s+-LiteralPath\s+\$probeFile\s+-Force", text), (
        "cleanup must remove exactly the probe temp file by literal path"
    )
    assert "-Recurse" not in text, "cleanup must never recurse"
    assert "$probeFile*" not in text and "$probeFile.*" not in text, (
        "cleanup must not use a wildcard against the probe file"
    )
    assert not re.search(r"Remove-Item[^\n]*\*", text), "cleanup must not use any wildcard removal"
    # The removal lives in the SAME outer finally that restores the saved env.
    i_remove = text.find("Remove-Item -LiteralPath $probeFile -Force")
    assert i_remove != -1
    finally_start = text.rfind("finally", 0, i_remove)
    finally_block = text[finally_start:]
    assert "Remove-Item -LiteralPath $probeFile -Force" in finally_block
    assert ("Set-Item" in finally_block or "Env:" in finally_block), (
        "probe cleanup must share the finally block that restores the saved env"
    )
    # $probeFile is initialized fail-closed so the finally never dereferences an
    # unset variable when construction fails early.
    assert "$probeFile = $null" in text, "the probe temp path must be initialized before the try"


@pytest.mark.skipif(sys.platform != "win32", reason="Windows PowerShell BOM behavior is Windows-only")
def test_powershell_utf8encoding_false_writes_no_bom_on_windows(tmp_path):
    # Real proof (not just a static match): the exact WriteAllText +
    # UTF8Encoding($false) expression the wrapper uses must emit NO UTF-8 BOM
    # (EF BB BF) and must normalize CRLF/CR to LF. Run it in Windows PowerShell
    # against a throwaway temp file, then clean up.
    import shutil

    exe = shutil.which("powershell") or shutil.which("pwsh")
    if not exe:
        pytest.skip("no PowerShell interpreter available")
    target = tmp_path / "bom-probe.sh"
    helper = tmp_path / "write-probe.ps1"
    helper.write_text(
        '$ErrorActionPreference = "Stop"\n'
        f"$p = '{target}'\n"
        '$probe = "set -e`r`necho ok`r`n"\n'
        '$lf = ($probe -replace "`r`n", "`n") -replace "`r", "`n"\n'
        '[System.IO.File]::WriteAllText($p, $lf, (New-Object System.Text.UTF8Encoding($false)))\n',
        encoding="utf-8",
    )
    try:
        subprocess.run(
            [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(helper)],
            check=True,
            capture_output=True,
        )
        data = target.read_bytes()
    finally:
        if target.exists():
            target.unlink()
    assert not data.startswith(b"\xef\xbb\xbf"), "UTF8Encoding($false) must not write a BOM"
    assert b"\r" not in data, "CRLF/CR must be normalized to LF before writing"
    assert data.startswith(b"set -e"), "the script content must be written verbatim after the BOM-free header"


def test_build_script_sets_cmake_policy_minimum_for_cmake4():
    text = _read(BUILD_SCRIPT)
    # CMake 4 rejects mGBA 0.10.5's cmake_minimum_required(VERSION 3.1) and
    # explicitly recommends -DCMAKE_POLICY_VERSION_MINIMUM=3.5. That external
    # policy floor is the source-preserving fix.
    assert "-DCMAKE_POLICY_VERSION_MINIMUM=3.5" in text, (
        "must pass the CMake 4 policy floor as an external configure flag"
    )
    # Exactly one occurrence, on the single configure command, before the build.
    assert text.count("-DCMAKE_POLICY_VERSION_MINIMUM=3.5") == 1, (
        "the policy floor must appear exactly once"
    )
    i_configure = text.find('cmake -S "${SRC_DIR}" -B "${CMAKE_BUILD}"')
    i_flag = text.find("-DCMAKE_POLICY_VERSION_MINIMUM=3.5")
    i_python_exec = text.find('-DPYTHON_EXECUTABLE="${VENV_PY}"')
    i_build = text.find('cmake --build "${CMAKE_BUILD}" --config Release')
    assert -1 < i_configure < i_flag < i_python_exec < i_build, (
        "the policy floor must be part of the single configure command, before the build"
    )
    # The upstream source is NOT patched to raise its cmake_minimum_required.
    assert "cmake_minimum_required" not in text, (
        "must not patch upstream cmake_minimum_required; use the external policy flag"
    )


def test_powershell_probes_configure_time_prerequisites():
    text = _read(PS1)
    # Native build deps are mandatory; PNG/zlib back screenshot evidence.
    assert "pkg-config" in text, "wrapper must probe for pkg-config/pkgconf"
    for dependency in ("epoxy", "libffi", "libpng", "zlib"):
        assert dependency in text, f"wrapper must verify {dependency}"
    # The building interpreter must be the UCRT64 python, not python.org/MSVC.
    assert re.search(r"/ucrt64/bin/python", text), (
        "wrapper must require python under /ucrt64/bin"
    )


def test_powershell_guidance_lists_ucrt64_prerequisite_packages():
    text = _read(PS1)
    for pkg in (
        "mingw-w64-ucrt-x86_64-libepoxy",
        "mingw-w64-ucrt-x86_64-libffi",
        "mingw-w64-ucrt-x86_64-libpng",
        "mingw-w64-ucrt-x86_64-zlib",
        "mingw-w64-ucrt-x86_64-pkgconf",
    ):
        assert pkg in text, f"guidance must list {pkg}"


def test_powershell_guidance_lists_ffmpeg_package():
    text = _read(PS1)
    assert "mingw-w64-ucrt-x86_64-ffmpeg" in text, "guidance must list the new ffmpeg package"
    # pkgconf was already required and must not be dropped by this change.
    assert "mingw-w64-ucrt-x86_64-pkgconf" in text


def test_powershell_probes_ffmpeg_prerequisites_for_use_ffmpeg():
    text = _read(PS1)
    assert (
        "for p in epoxy libffi libpng zlib libavcodec libavfilter libavformat "
        "libavutil libswscale" in text
    ), "wrapper must probe for all six mandatory FFmpeg configure-time modules"
    assert (
        "pkg-config --exists libswresample && ! pkg-config --exists libavresample" in text
    ), "wrapper must require one of libswresample/libavresample, not both"
    assert "EReaderScanLoadImageA" in text, (
        "wrapper must document the e-Reader API symbol that requires USE_FFMPEG"
    )


def test_powershell_does_not_use_or_claim_msvc():
    text = _read(PS1)
    low = text.lower()
    for tok in ("cl.exe", "vcvars", "developer command prompt", "developer prompt", "build tools"):
        assert tok not in low, f"wrapper must not invoke/offer MSVC ({tok})"
    # MSVC is referenced only to disclaim it, never as a supported path.
    assert "msvc" in low
    assert re.search(r"(not\s+supported|unsupported|deprecated|not-planned|cannot|does not use)", low), (
        "wrapper must state MSVC is not supported"
    )


def test_powershell_does_not_install_or_download_toolchain():
    text = _read(PS1)
    low = text.lower()
    for tok in ("invoke-webrequest", "start-bitstransfer", "choco", "winget", "wget"):
        assert tok not in low, f"wrapper must not download the toolchain ({tok})"
    # pacman may only appear as guidance text, never executed.
    for line in text.splitlines():
        if "pacman" in line.lower():
            assert '"' in line or line.strip().startswith("#"), (
                "pacman may only appear as guidance, never executed"
            )
    # Must fail closed with MSYS2 guidance instead of installing.
    assert any(
        url.scheme == "https"
        and url.hostname == "www.msys2.org"
        and url.port is None
        and url.path in ("", "/")
        and not url.query
        and not url.fragment
        for url in _https_urls(text)
    ), "wrapper must point users to the exact official MSYS2 origin"


def test_powershell_does_not_mutate_global_environment():
    text = _read(PS1)
    assert not re.search(r"SetEnvironmentVariable\([^)]*['\"](User|Machine)['\"]", text), (
        "wrapper must not persist environment changes"
    )
    assert not re.search(r"\bsetx\b", text, re.IGNORECASE), "wrapper must not use setx"
    # Any process-scoped env it sets must be restored (save/restore pattern).
    if re.search(r"\$env:MSYSTEM\s*=", text):
        assert "finally" in text and ("Set-Item" in text or "Remove-Item" in text), (
            "wrapper must restore any process env it sets"
        )


def test_powershell_reports_interpreter_and_delegates_check():
    text = _read(PS1)
    assert "venv" in text.lower(), "wrapper must report the venv interpreter path"
    # Both venv layouts are resolved for reporting.
    assert "Scripts\\python.exe" in text
    assert re.search(r"bin\\python", text)


def test_real_workflow_proves_ubuntu_and_windows_ucrt64():
    text = _read(REAL_WORKFLOW)
    lower = text.lower()
    assert "real-mgba-ubuntu:" in text
    assert "real-mgba-windows:" in text
    assert (
        "msys2/setup-msys2@66cd2cce69caa17b53920067426061ca1de3a884"
        in text
    )
    assert "msystem: UCRT64" in text
    assert "install-mgba-playtest.sh" in text
    assert "install-mgba-playtest.ps1" in text
    assert text.count("run_real_mgba_proof.py") == 2
    assert "continue-on-error" not in lower


def test_real_workflow_installs_windows_ucrt64_prerequisites():
    text = _read(REAL_WORKFLOW)
    for package in (
        "python:p",
        "gcc:p",
        "cmake:p",
        "pkgconf:p",
        "libepoxy:p",
        "libffi:p",
        "libpng:p",
        "zlib:p",
    ):
        assert package in text
    assert re.search(r"\bmake\b", text), "UCRT64 job must install plain MSYS make"


def test_real_workflow_windows_ucrt64_adds_ffmpeg_and_keeps_pkgconf():
    text = _read(REAL_WORKFLOW)
    assert "ffmpeg:p" in text, "UCRT64 job must add the new ffmpeg pacboy token"
    assert "pkgconf:p" in text, "UCRT64 job must retain the existing pkgconf pacboy token"


def test_real_workflow_windows_ucrt64_pacboy_packages_are_exact():
    text = _read(REAL_WORKFLOW)
    start = text.index("pacboy: >-")
    end = text.index("\n\n", start)
    block = text[start:end]
    tokens = block.replace("pacboy: >-", "").split()
    expected = {
        "python:p", "python-pip:p", "gcc:p", "cmake:p", "pkgconf:p",
        "libepoxy:p", "libffi:p", "libpng:p", "zlib:p", "ffmpeg:p",
    }
    assert set(tokens) == expected, f"unexpected UCRT64 pacboy package set: {set(tokens)}"


def test_real_workflow_installs_ubuntu_native_prerequisites():
    text = _read(REAL_WORKFLOW)
    for package in ("libepoxy-dev", "libffi-dev", "libpng-dev", "zlib1g-dev"):
        assert package in text


def test_real_workflow_installs_ubuntu_ffmpeg_prerequisites():
    text = _read(REAL_WORKFLOW)
    for package in (
        "libavcodec-dev",
        "libavfilter-dev",
        "libavformat-dev",
        "libavutil-dev",
        "libswscale-dev",
        "libswresample-dev",
    ):
        assert package in text, f"Ubuntu job must install {package}"


def test_real_workflow_ubuntu_apt_packages_are_exact():
    text = _read(REAL_WORKFLOW)
    start = text.index("sudo apt-get install -y")
    end = text.index("\n\n", start)
    block = text[start:end]
    tokens = block.replace("sudo apt-get install -y", "").split()
    expected = {
        "build-essential", "cmake", "ninja-build", "pkg-config",
        "libepoxy-dev", "libffi-dev", "libpng-dev", "zlib1g-dev",
        "libavcodec-dev", "libavfilter-dev", "libavformat-dev", "libavutil-dev",
        "libswscale-dev", "libswresample-dev", "gdb",
    }
    assert set(tokens) == expected, f"unexpected Ubuntu apt package set: {set(tokens)}"


def test_posix_bootstrap_requires_screenshot_and_cffi_native_dependencies():
    text = _read(BUILD_SCRIPT)
    assert re.search(r'require_cmd\s+"pkg-config"', text)
    for dependency in ("epoxy", "libffi", "libpng", "zlib"):
        assert dependency in text
    assert "-DUSE_PNG=ON" in text
    assert "-DUSE_ZLIB=ON" in text


def test_build_script_probes_ffmpeg_prerequisites_for_use_ffmpeg():
    text = _read(BUILD_SCRIPT)
    assert (
        "for dependency in libavcodec libavfilter libavformat libavutil libswscale"
        in text
    ), "build script must probe all five mandatory FFmpeg pkg-config modules"
    assert "pkg-config --exists libswresample" in text
    assert "pkg-config --exists libavresample" in text
    assert "EReaderScanLoadImageA" in text, (
        "build script must document the e-Reader API symbol requiring USE_FFMPEG"
    )
    # Evidence: each FFmpeg module's version must be echoed, like the existing
    # epoxy/libffi/libpng/zlib probes.
    assert re.search(
        r'echo\s+"\s*found\s+\$\{dependency\}\s*->\s*\$\(pkg-config\s+--modversion',
        text,
    )
    assert re.search(r'echo\s+"\s*found\s+libswresample\s*->', text)
    assert re.search(r'echo\s+"\s*found\s+libavresample\s*->', text)


def test_build_script_enables_use_ffmpeg():
    text = _read(BUILD_SCRIPT)
    assert "-DUSE_FFMPEG=ON" in text, (
        "the e-Reader API symbols are only compiled when USE_FFMPEG is enabled"
    )
    assert "-DUSE_FFMPEG=OFF" not in text, "must not silently leave FFmpeg disabled"


def test_build_script_fails_closed_on_generated_flags_header():
    text = _read(BUILD_SCRIPT)
    assert 'FLAGS_HEADER="${CMAKE_BUILD}/include/mgba/flags.h"' in text, (
        "must reference the CMake-generated feature header, not a guessed path"
    )
    i_configure = text.find('-DPYTHON_EXECUTABLE="${VENV_PY}"')
    i_flags_check = text.find("FLAGS_HEADER=")
    i_build = text.find('cmake --build "${CMAKE_BUILD}" --config Release')
    assert -1 < i_configure < i_flags_check < i_build, (
        "the flags.h fail-closed check must run after cmake configure and before the build"
    )
    assert re.search(r'if\s+\[\s+!\s+-f\s+"\$\{FLAGS_HEADER\}"\s+\]', text), (
        "must fail closed if the generated header is missing"
    )
    assert "for feature in USE_FFMPEG USE_PNG USE_ZLIB" in text, (
        "must verify exactly USE_FFMPEG, USE_PNG, and USE_ZLIB"
    )
    assert '#define[[:space:]]+${feature}' in text, (
        "must require an uncommented #define for each feature, not a commented /* #undef */"
    )
    assert "|| true" not in text[i_flags_check:i_build], (
        "the generated-header gate must not fail-open"
    )


def test_build_script_pins_matching_32_bit_python_and_native_color_abi():
    text = _read(BUILD_SCRIPT)
    assert "-DCOLOR_16_BIT=OFF" in text
    assert "-DCOLOR_5_6_5=OFF" in text
    assert "-DCOLOR_16_BIT=ON" not in text
    assert "-DCOLOR_5_6_5=ON" not in text
    assert "for feature in COLOR_16_BIT COLOR_5_6_5" in text
    assert "Python/native color ABI would diverge" in text
    assert "GBAVideoSoftwareRendererInit" in text

    configure = text.index("-DCOLOR_16_BIT=OFF")
    color_gate = text.index("for feature in COLOR_16_BIT COLOR_5_6_5")
    build = text.index('cmake --build "${CMAKE_BUILD}" --config Release')
    assert configure < color_gate < build


def test_build_script_flags_header_check_does_not_trust_cmakecache():
    text = _read(BUILD_SCRIPT)
    low = text.lower()
    assert "cmakecache" in low, (
        "the script must explicitly document that CMakeCache.txt is not authoritative"
    )
    assert re.search(r"not\s+(?:trust\s+)?cmakecache", low), (
        "the script must explicitly disclaim reliance on CMakeCache.txt"
    )
    # No command may parse CMakeCache.txt to gate the feature check itself; the
    # generated flags.h is the only thing read for USE_FFMPEG/USE_PNG/USE_ZLIB.
    # (A raw substring/regex scan for "sed" would false-positive on ordinary
    # prose like "closed"/"based"/"used"; only real command invocations on
    # non-comment lines count.)
    assert not _shell_command_reads_cmakecache(text), (
        "feature verification must read the generated flags.h, not parse CMakeCache.txt"
    )


def test_shell_command_reads_cmakecache_helper_semantics():
    """Dedicated unit coverage for the ``_shell_command_reads_cmakecache``
    helper itself, independent of the current script's exact wording. This
    pins the fail-closed detection semantics so a future edit cannot silently
    weaken (or re-break) the helper without a test failing here directly.
    """
    # A full comment line must never be flagged, even though "sed" is a bare
    # substring of ordinary prose words like "closed"/"based"/"used".
    assert not _shell_command_reads_cmakecache(
        "# --- Fail closed on the GENERATED feature header, never CMakeCache.txt -----"
    )
    # A benign diagnostic string mentioning the filename is not a real read.
    assert not _shell_command_reads_cmakecache(
        'echo "Verifying the generated header (never trust CMakeCache.txt)"'
    )
    # Direct invocation at the start of a line.
    assert _shell_command_reads_cmakecache('grep USE_FFMPEG CMakeCache.txt')
    # Negated conditional invocation ("if ! grep ...").
    assert _shell_command_reads_cmakecache(
        'if ! grep -q "USE_FFMPEG" CMakeCache.txt; then'
    )
    # Command substitution into a variable assignment.
    assert _shell_command_reads_cmakecache(
        'cache="$(grep -q "USE_FFMPEG" CMakeCache.txt)"'
    )
    # Pipeline invocation of sed.
    assert _shell_command_reads_cmakecache(
        'cmake --build . 2>&1 | sed -n "/CMakeCache.txt/p"'
    )
    # An earlier decoy occurrence of the literal filename (in an unrelated
    # echo) must not mask a real grep invocation later on the same line.
    assert _shell_command_reads_cmakecache(
        'echo CMakeCache.txt; grep USE_FFMPEG "$CMAKE_BUILD/CMakeCache.txt"'
    )


def test_real_proof_checks_replay_transitions_screenshots_and_save_isolation():
    text = _read(REAL_PROOF)
    assert "REPLAY_COUNT = 3" in text
    assert '"pressObserved": True' in text
    assert '"releaseObserved": True' in text
    assert "screenshotSha256" in text
    assert "SAVE_SUFFIXES" in text
    assert "process.returncode != result.get(\"exitCode\")" in text


def test_readme_does_not_claim_msvc_support():
    text = _read(README).lower()
    for tok in ("cl.exe", "vcvars", "developer command prompt", "visual studio build tools"):
        assert tok not in text, f"README must not claim MSVC support ({tok})"
    if "msvc" in text:
        assert re.search(r"(unsupported|not\s+supported|not-planned|deprecated)", text), (
            "README may mention MSVC only to disclaim it"
        )
    assert "msys2" in text, "README must document the MSYS2 UCRT64 requirement"


PYPROJECT = os.path.join(TOOL_DIR, "pyproject.toml")


def test_pyproject_uses_repository_gpl_license():
    text = _read(PYPROJECT)
    # The repository-authored package inherits FEBuilderGBA's GPLv3 license; it
    # must NOT be relabelled with mGBA's MPL-2.0.
    assert re.search(r'license\s*=\s*\{\s*text\s*=\s*"GPL-3\.0-or-later"\s*\}', text), (
        "pyproject must license the runner package as GPL-3.0-or-later"
    )
    assert "MPL" not in text, (
        "pyproject must not relicense repository-authored code under mGBA's MPL"
    )


def test_readme_license_boundary_gpl_vs_mpl():
    text = _read(README)
    lower = text.lower()
    # The package itself is GPLv3 (FEBuilderGBA's license)...
    assert "gplv3" in lower or "gpl-3" in lower, (
        "README must state the package is GPLv3"
    )
    # ...while mGBA is separately MPL-2.0 and only fetched/built as a dependency.
    assert "mpl-2.0" in lower, "README must attribute mGBA's MPL-2.0 license"
    assert "separate" in lower, (
        "README must clarify mGBA is a separate dependency, not this package's license"
    )


# --------------------------------------------------------------------------- #
# Cross-script + requirements invariants
# --------------------------------------------------------------------------- #
def test_scripts_do_not_download_toolchains():
    # Neither script may fetch compilers/toolchains.
    banned = ["apt-get install", "choco install", "brew install", "winget install"]
    for path in (PS1, SH):
        text = _read(path).lower()
        for token in banned:
            assert token not in text, f"{path} must not download toolchains ({token})"


def test_requirements_are_fully_hashed():
    text = _read(REQUIREMENTS)
    pins = re.findall(r"^\s*([A-Za-z0-9_.-]+)==", text, re.MULTILINE)
    hashes = re.findall(r"--hash=sha256:([0-9a-f]{64})", text)
    assert len(pins) >= 3
    assert len(hashes) >= len(pins)
    assert "placeholder" not in text.lower()
    assert "0000000000000000000000000000000000000000000000000000000000000000" not in text


def test_bootstrap_requirements_are_fully_hashed():
    assert os.path.isfile(REQUIREMENTS_BOOTSTRAP)
    text = _read(REQUIREMENTS_BOOTSTRAP)
    pins = re.findall(r"^\s*([A-Za-z0-9_.-]+)==", text, re.MULTILINE)
    hashes = re.findall(r"--hash=sha256:([0-9a-f]{64})", text)
    assert len(pins) >= 3
    assert len(hashes) >= len(pins)
    assert "placeholder" not in text.lower()
    assert "0000000000000000000000000000000000000000000000000000000000000000" not in text


def test_all_required_build_packages_are_pinned_with_hashes():
    boot = _read(REQUIREMENTS_BOOTSTRAP)
    build = _read(REQUIREMENTS)
    for pkg in REQUIRED_BOOTSTRAP_PACKAGES:
        assert re.search(rf"(?mi)^\s*{re.escape(pkg)}==", boot), f"{pkg} must be pinned in bootstrap reqs"
    for pkg in REQUIRED_BUILD_PACKAGES:
        assert re.search(rf"(?mi)^\s*{re.escape(pkg)}==", build), f"{pkg} must be pinned in build reqs"


def test_pinned_build_stack_supports_current_msys2_python_314():
    bootstrap = _read(REQUIREMENTS_BOOTSTRAP)
    build = _read(REQUIREMENTS)
    assert re.search(r"(?mi)^\s*setuptools==83\.0\.0\b", bootstrap)
    assert re.search(r"(?mi)^\s*cffi==2\.1\.0\b", build)


def test_cffi_pin_is_the_mingw_atomic_fix_release_with_exact_hash():
    # cffi 2.0.0 supports Python 3.14 but carries a MinGW atomic-store
    # regression that breaks UCRT64 builds; 2.1.0 (upstream PR #198) fixes it
    # via GCC/Clang builtin atomics while keeping 3.14 support. The old pin
    # and its hash must be gone entirely, not merely superseded/left as a
    # stale second pin.
    build = _read(REQUIREMENTS)
    assert re.search(
        r"(?mi)^\s*cffi==2\.1\.0\s*\\\s*--hash=sha256:"
        r"efc1cdd798b1aaf39b4610bba7aad28c9bea9b910f25c784ccf9ec1fa719d1f9\b",
        build,
    ), "cffi must be pinned to 2.1.0 with its exact official sdist hash"
    assert not re.search(r"(?mi)^\s*cffi==2\.0\.0\b", build), (
        "the MinGW-atomic-regressed cffi 2.0.0 pin must be removed, not just superseded"
    )
    assert "44d1b5909021139fe36001ae048dbdde8214afa20200eda0f64c068cac5d5529" not in build, (
        "the old cffi 2.0.0 hash must not remain in the requirements file"
    )


def test_runtime_never_installs_or_downloads():
    banned_tokens = ["pip install", "urllib", "urlopen", "requests.", "http://", "https://",
                     "socket.", "subprocess", "os.system", "curl ", "wget "]
    for name in os.listdir(PKG_DIR):
        if not name.endswith(".py"):
            continue
        text = _read(os.path.join(PKG_DIR, name)).lower()
        for token in banned_tokens:
            assert token not in text, f"runtime module {name} must not use {token!r}"


# --------------------------------------------------------------------------- #
# mGBA CFFI cdef preprocessor overlay (fail-closed, source-preserving)
# --------------------------------------------------------------------------- #
def test_cffi_wrapper_script_exists_next_to_bootstrap():
    assert os.path.isfile(CFFI_WRAPPER)


def test_build_script_requires_and_locates_the_cffi_wrapper():
    text = _read(BUILD_SCRIPT)
    assert 'MGBA_CFFI_WRAPPER="${SCRIPT_DIR}/mgba_cffi_preprocessor.py"' in text
    assert re.search(r'if\s+\[\s+!\s+-f\s+"\$\{MGBA_CFFI_WRAPPER\}"\s+\]', text), (
        "must fail closed if the wrapper is missing"
    )


def test_workflow_triggers_on_cffi_wrapper_changes():
    text = _read(REAL_WORKFLOW)
    assert text.count("- 'scripts/mgba_cffi_preprocessor.py'") == 2


def test_workflow_triggers_on_process_runner_changes():
    text = _read(REAL_WORKFLOW)
    assert text.count("- 'FEBuilderGBA.Core/ProcessRunnerCore.cs'") == 2
    assert (
        text.count("- 'FEBuilderGBA.Core.Tests/ProcessRunnerCoreTests.cs'")
        == 2
    )


def test_workflow_pins_setup_msys2_to_reviewed_commit():
    text = _read(REAL_WORKFLOW)
    assert (
        "msys2/setup-msys2@66cd2cce69caa17b53920067426061ca1de3a884"
        in text
    )
    assert "msys2/setup-msys2@v2" not in text


def test_build_script_rejects_a_symlinked_cffi_wrapper():
    text = _read(BUILD_SCRIPT)
    wrapper_def = text.index('MGBA_CFFI_WRAPPER="${SCRIPT_DIR}/mgba_cffi_preprocessor.py"')
    missing_check = text.index('! -f "${MGBA_CFFI_WRAPPER}"')
    between = text[wrapper_def:missing_check]
    assert re.search(r'if\s+\[\s+-L\s+"\$\{MGBA_CFFI_WRAPPER\}"\s+\]', between), (
        "must fail closed on a symlinked wrapper, checked before the -f check"
    )


def test_setuptools_shim_exists_and_is_scoped_by_bootstrap():
    assert os.path.isfile(SETUPTOOLS_SHIM)
    text = _read(BUILD_SCRIPT)
    assert 'MGBA_SETUPTOOLS_SHIM_DIR="${TOOL_DIR_CANON}/setuptools-shim"' in text
    assert 'MGBA_SETUPTOOLS_SHIM="${MGBA_SETUPTOOLS_SHIM_DIR}/sitecustomize.py"' in text
    assert 'MGBA_SETUPTOOLS_SHIM_ARG="$(to_native_path "${MGBA_SETUPTOOLS_SHIM_DIR}")"' in text
    assert 'MGBA_SETUPTOOLS_TEMP="${CMAKE_BUILD}/setuptools-temp"' in text
    assert 'MGBA_SETUPTOOLS_TEMP_ARG="$(to_native_path "${MGBA_SETUPTOOLS_TEMP}")"' in text
    assert text.count('PYTHONPATH="${MGBA_SETUPTOOLS_SHIM_ARG}"') == 2
    assert text.count(
        'FEBUILDERGBA_MGBA_SETUPTOOLS_TEMP="${MGBA_SETUPTOOLS_TEMP_ARG}"'
    ) == 2
    assert "export PYTHONPATH" not in text


def test_setuptools_shim_is_narrow_and_source_preserving():
    text = _read(SETUPTOOLS_SHIM)
    tree = ast.parse(text)
    imports = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imports.update(alias.name.split(".")[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            imports.add(node.module.split(".")[0])
    assert imports <= {"os", "sys", "setuptools", "distutils"}
    assert "MinGW32Compiler.runtime_library_dir_option" in text
    assert "DistutilsMinGW32Compiler.runtime_library_dir_option" in text
    assert "get_libraries" in text
    assert "python{sys.version_info.major}.{sys.version_info.minor}" in text
    assert "FEBUILDERGBA_MGBA_SETUPTOOLS_TEMP" in text
    assert "self.build_temp = _build_temp" in text
    assert "subprocess" not in imports


def test_build_script_builds_cpp_as_two_shlex_safe_tokens():
    text = _read(BUILD_SCRIPT)
    assert "mgba_cffi_shlex_quote" in text
    assert re.search(
        r'MGBA_CFFI_CPP="\$\(mgba_cffi_shlex_quote "\$\{MGBA_CFFI_PYTHON_ARG\}"\)\s+'
        r'\$\(mgba_cffi_shlex_quote "\$\{MGBA_CFFI_WRAPPER_ARG\}"\)"',
        text,
    ), "CPP must be built from exactly two shlex-safe quoted tokens"
    # printf %q is deliberately avoided (bash %q can emit $'...' ANSI-C
    # quoting that Python's shlex does not parse the same way).
    commands = "\n".join(
        line for line in text.splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    )
    assert not re.search(r"\bprintf(?:\s+-v\s+\w+)?\s+[\"']?%q\b", commands)


def test_build_script_sets_expected_builder_h_path_env():
    text = _read(BUILD_SCRIPT)
    assert 'MGBA_BUILDER_H="${SRC_DIR}/src/platform/python/_builder.h"' in text
    assert 'MGBA_BUILDER_H_ARG="$(to_native_path "${MGBA_BUILDER_H}")"' in text
    assert "FEBUILDERGBA_MGBA_BUILDER_H" in text


def test_build_script_sets_expected_source_root_path_env():
    text = _read(BUILD_SCRIPT)
    assert 'MGBA_SOURCE_ROOT="${SRC_DIR}"' in text
    assert 'MGBA_SOURCE_ROOT_ARG="$(to_native_path "${MGBA_SOURCE_ROOT}")"' in text
    assert "FEBUILDERGBA_MGBA_SOURCE_ROOT" in text


def test_build_script_converts_all_cpp_paths_for_native_msys_python():
    text = _read(BUILD_SCRIPT)
    assert re.search(
        r'to_native_path\(\)\s*\{.*?if \[ -n "\$\{MSYSTEM:-\}" \]; then'
        r'.*?cygpath -w "\$1".*?else.*?printf',
        text,
        re.DOTALL,
    ), "MSYS paths must use cygpath -w while POSIX paths pass through"
    conversions = (
        'MGBA_CFFI_PYTHON_ARG="$(to_native_path "${VENV_PY}")"',
        'MGBA_CFFI_WRAPPER_ARG="$(to_native_path "${MGBA_CFFI_WRAPPER}")"',
        'MGBA_BUILDER_H_ARG="$(to_native_path "${MGBA_BUILDER_H}")"',
        'MGBA_SOURCE_ROOT_ARG="$(to_native_path "${MGBA_SOURCE_ROOT}")"',
        'MGBA_SETUPTOOLS_SHIM_ARG="$(to_native_path "${MGBA_SETUPTOOLS_SHIM_DIR}")"',
        'MGBA_SETUPTOOLS_TEMP_ARG="$(to_native_path "${MGBA_SETUPTOOLS_TEMP}")"',
    )
    cpp_pos = text.index("MGBA_CFFI_CPP=")
    for conversion in conversions:
        assert conversion in text
        assert text.index(conversion) < cpp_pos


def test_build_script_enables_mingw_cdef_sanitization_only_under_msystem():
    text = _read(BUILD_SCRIPT)
    assert "MGBA_CFFI_MINGW_CDEF=0" in text
    assert re.search(
        r'if \[ -n "\$\{MSYSTEM:-\}" \]; then\s+'
        r'MGBA_CFFI_MINGW_CDEF=1\s+fi',
        text,
    )
    assert text.index("MGBA_CFFI_MINGW_CDEF=1") < text.index("MGBA_CFFI_CPP=")


def test_build_script_scopes_cpp_wrapper_to_both_build_invocations_only():
    text = _read(BUILD_SCRIPT)
    # Every CPP=... assignment in the script must be a command-scoped prefix
    # immediately in front of a `cmake --build` invocation -- never exported,
    # never present anywhere else.
    cpp_lines = [
        line for line in text.splitlines()
        if re.match(r'^\s*CPP="\$\{MGBA_CFFI_CPP\}"', line)
    ]
    assert len(cpp_lines) == 2, "CPP must be scoped to exactly two commands"
    for line in cpp_lines:
        assert "FEBUILDERGBA_MGBA_BUILDER_H=" in line
    assert "export CPP" not in text, "CPP must never be exported/persisted"
    assert "export FEBUILDERGBA_MGBA_BUILDER_H" not in text
    assert "export FEBUILDERGBA_MGBA_SOURCE_ROOT" not in text
    assert "export FEBUILDERGBA_MGBA_MINGW_CDEF" not in text

    default_build = text.find('cmake --build "${CMAKE_BUILD}" --config Release')
    bdist_build = text.find('cmake --build "${CMAKE_BUILD}" --target mgba-py-bdist')
    for build_pos in (default_build, bdist_build):
        assert build_pos != -1
        preceding = text[max(0, build_pos - 520):build_pos]
        assert 'CPP="${MGBA_CFFI_CPP}"' in preceding, (
            "each CMake build invocation that can run _builder.py must be "
            "immediately preceded by the scoped CPP assignment"
        )
        assert "FEBUILDERGBA_MGBA_BUILDER_H=" in preceding
        assert "FEBUILDERGBA_MGBA_SOURCE_ROOT=" in preceding, (
            "each CMake build invocation that can run _builder.py must also "
            "carry the scoped source-root env var used to prove the temp "
            "overlay copy lands outside the pinned source tree"
        )
        assert '${MGBA_BUILDER_H_ARG}' in preceding
        assert '${MGBA_SOURCE_ROOT_ARG}' in preceding
        assert "FEBUILDERGBA_MGBA_MINGW_CDEF=" in preceding
        assert '${MGBA_CFFI_MINGW_CDEF}' in preceding
        assert 'PYTHONPATH="${MGBA_SETUPTOOLS_SHIM_ARG}"' in preceding
        assert (
            'FEBUILDERGBA_MGBA_SETUPTOOLS_TEMP="${MGBA_SETUPTOOLS_TEMP_ARG}"'
            in preceding
        )


def test_build_script_cffi_overlay_does_not_touch_cflags_or_source():
    text = _read(BUILD_SCRIPT)
    # The overlay must never be implemented via CFLAGS/CPPFLAGS/CXXFLAGS, and
    # must never patch the pinned source tree (no sed/patch against SRC_DIR).
    for token in ("CFLAGS=", "CPPFLAGS=", "CXXFLAGS="):
        assert token not in text
    assert not re.search(r"patch\s+-p", text)
    assert not re.search(r"sed\s+-i", text), (
        "must not edit files in place (the overlay only ever writes a "
        "temporary copy, never the pinned source)"
    )


def test_build_script_cites_upstream_fix_commit():
    text = _read(BUILD_SCRIPT)
    assert "36f321f84889bc69b48541e0519401c091eeaeca" in text
    assert "va_list" in text
    assert "typedef ... va_list;" in text


def test_cffi_wrapper_is_dependency_free_and_never_uses_a_shell():
    text = _read(CFFI_WRAPPER)
    tree = ast.parse(text)
    imports = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imports.update(alias.name.split(".")[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            imports.add(node.module.split(".")[0])
    allowed = {"__future__", "os", "shlex", "subprocess", "sys", "tempfile", "typing"}
    assert imports <= allowed, f"unexpected imports: {imports - allowed}"
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call) or not isinstance(node.func, ast.Attribute):
            continue
        owner = node.func.value
        if not isinstance(owner, ast.Name):
            continue
        if owner.id == "subprocess" and node.func.attr == "run":
            assert all(keyword.arg != "shell" for keyword in node.keywords), (
                "subprocess.run must use structural argv without a shell"
            )
        assert not (owner.id == "os" and node.func.attr == "system"), (
            "the wrapper must never invoke os.system"
        )


def test_cffi_wrapper_is_never_imported_by_the_runtime_package():
    for name in os.listdir(PKG_DIR):
        if not name.endswith(".py"):
            continue
        text = _read(os.path.join(PKG_DIR, name))
        assert "mgba_cffi_preprocessor" not in text
