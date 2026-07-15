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

import os
import re

import febuildergba_playtest

PKG_DIR = os.path.dirname(os.path.abspath(febuildergba_playtest.__file__))
TOOL_DIR = os.path.dirname(PKG_DIR)
REPO_ROOT = os.path.dirname(os.path.dirname(TOOL_DIR))
SCRIPTS_DIR = os.path.join(REPO_ROOT, "scripts")

PINNED_COMMIT = "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"
ARCHIVE_SHA = "9475c26e9fa2f4b30c07ab6636e4b0a5b62e4baee2109ede7b2fecc52edae366"

PS1 = os.path.join(SCRIPTS_DIR, "install-mgba-playtest.ps1")
SH = os.path.join(SCRIPTS_DIR, "install-mgba-playtest.sh")
README = os.path.join(TOOL_DIR, "README.md")
REQUIREMENTS = os.path.join(TOOL_DIR, "requirements-mgba-build.txt")
REQUIREMENTS_BOOTSTRAP = os.path.join(TOOL_DIR, "requirements-mgba-bootstrap.txt")

# ``install-mgba-playtest.sh`` owns all build logic; ``.ps1`` delegates to it.
BUILD_SCRIPT = SH

# Every package name that must appear, hash-pinned, across the two stages.
REQUIRED_BOOTSTRAP_PACKAGES = ("setuptools", "wheel", "pytest-runner")
REQUIRED_BUILD_PACKAGES = ("cffi", "pycparser", "cached-property")


def _read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def test_bootstrap_scripts_exist():
    assert os.path.isfile(PS1)
    assert os.path.isfile(SH)


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
    for ref in ("/archive/refs/heads", "refs/tags", "git checkout main", "git checkout master"):
        assert ref not in text


def test_build_script_requires_hashes():
    assert "--require-hashes" in _read(BUILD_SCRIPT)


def test_build_script_references_both_requirement_stages():
    text = _read(BUILD_SCRIPT)
    assert "requirements-mgba-bootstrap.txt" in text, "must install stage-1 wheels"
    assert "requirements-mgba-build.txt" in text, "must install stage-2 sources"


def test_build_script_uses_fail_hard_py_install_target():
    text = _read(BUILD_SCRIPT)
    assert "mgba-py-install" in text, "must build the mgba-py-install target"
    assert "--component python" not in text, "must not use a Python install component"
    assert "|| true" not in text, "must not fail-open the install step"


def test_build_script_stamps_inner_git_provenance():
    # The codeload tarball has no .git; version.cmake would run ``git describe``
    # and walk up into the parent FEBuilderGBA repo, mis-stamping the binding.
    text = _read(BUILD_SCRIPT)
    assert "git init" in text, "must initialize an inner git repo"
    assert "https://github.com/mgba-emu/mgba.git" in text, "must add the official origin"
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
    assert "refs/tags" not in text, "must not reference tag refs"


def test_build_script_git_provenance_runs_after_verification_before_cmake():
    text = _read(BUILD_SCRIPT)
    i_verify = text.find("archive verified")
    i_git = text.find("git init")
    i_cmake = text.find("cmake -S")
    assert -1 < i_verify < i_git < i_cmake, (
        "git provenance must sit between archive verification and cmake"
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


def test_build_script_locks_build_phase_offline():
    text = _read(BUILD_SCRIPT)
    # After the hash-pinned pip stages, the CMake/setup.py install must not be
    # able to silently fetch an unpinned package: enforce an offline pip env.
    assert "PIP_NO_INDEX" in text, "must disable the package index during the build"
    assert "PIP_NO_BUILD_ISOLATION" in text, "must disable build isolation during the build"
    i_stage2 = text.find("requirements-mgba-build.txt")
    i_offline = text.find("PIP_NO_INDEX")
    i_install = text.find("--target mgba-py-install")
    assert -1 < i_stage2 < i_offline < i_install, (
        "offline lockdown must sit after stage-2 pip and before mgba-py-install"
    )
    # No pip install may run after the lockdown is engaged.
    after = text[i_offline:]
    assert "pip install" not in after, "no pip install may run after the offline lockdown"


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
    assert "ninja" in text and "make" in text, "wrapper must require ninja or make"
    assert "command -v" in text, "wrapper must probe with command -v inside the shell"


def test_powershell_probes_configure_time_prerequisites():
    text = _read(PS1)
    # libepoxy + pkgconf are mandatory configure-time deps of the mGBA build.
    assert "pkg-config" in text, "wrapper must probe for pkg-config/pkgconf"
    assert re.search(r"pkg-config\s+--exists\s+epoxy", text), (
        "wrapper must verify libepoxy via pkg-config"
    )
    # The building interpreter must be the UCRT64 python, not python.org/MSVC.
    assert re.search(r"/ucrt64/bin/python", text), (
        "wrapper must require python under /ucrt64/bin"
    )


def test_powershell_guidance_lists_ucrt64_prerequisite_packages():
    text = _read(PS1)
    for pkg in (
        "mingw-w64-ucrt-x86_64-libepoxy",
        "mingw-w64-ucrt-x86_64-pkgconf",
    ):
        assert pkg in text, f"guidance must list {pkg}"


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
    assert "www.msys2.org" in low, "wrapper must point users to install MSYS2 themselves"


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


def test_readme_does_not_claim_msvc_support():
    text = _read(README).lower()
    for tok in ("cl.exe", "vcvars", "developer command prompt", "visual studio build tools"):
        assert tok not in text, f"README must not claim MSVC support ({tok})"
    if "msvc" in text:
        assert re.search(r"(unsupported|not\s+supported|not-planned|deprecated)", text), (
            "README may mention MSVC only to disclaim it"
        )
    assert "msys2" in text, "README must document the MSYS2 UCRT64 requirement"


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


def test_runtime_never_installs_or_downloads():
    banned_tokens = ["pip install", "urllib", "urlopen", "requests.", "http://", "https://",
                     "socket.", "subprocess", "os.system", "curl ", "wget "]
    for name in os.listdir(PKG_DIR):
        if not name.endswith(".py"):
            continue
        text = _read(os.path.join(PKG_DIR, name)).lower()
        for token in banned_tokens:
            assert token not in text, f"runtime module {name} must not use {token!r}"
