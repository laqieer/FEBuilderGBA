"""Static safety checks for the bootstrap scripts and runtime package.

These tests read files as text (no execution) to enforce the security policy:
the runtime never downloads/installs, and the bootstrap scripts pin an exact
official commit, verify its SHA-256 before extraction with no fallback, and
install Python build dependencies with hash checking.
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
REQUIREMENTS = os.path.join(TOOL_DIR, "requirements-mgba-build.txt")
REQUIREMENTS_BOOTSTRAP = os.path.join(TOOL_DIR, "requirements-mgba-bootstrap.txt")

# Every package name that must appear, hash-pinned, across the two stages.
REQUIRED_BOOTSTRAP_PACKAGES = ("setuptools", "wheel", "pytest-runner")
REQUIRED_BUILD_PACKAGES = ("cffi", "pycparser", "cached-property")


def _read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def test_bootstrap_scripts_exist():
    assert os.path.isfile(PS1)
    assert os.path.isfile(SH)


def test_scripts_pin_exact_commit_and_archive_hash():
    for path in (PS1, SH):
        text = _read(path)
        assert PINNED_COMMIT in text, f"{path} must pin the exact commit"
        assert ARCHIVE_SHA in text, f"{path} must pin the archive SHA-256"


def test_scripts_only_use_official_mgba_source():
    for path in (PS1, SH):
        text = _read(path)
        assert "mgba-emu/mgba" in text
        # No alternate mirrors / forks.
        assert not re.search(r"github\.com/(?!mgba-emu/mgba)", text)


def test_scripts_verify_before_extraction_with_no_fallback():
    for path in (PS1, SH):
        text = _read(path).lower()
        assert "sha-256 mismatch" in text or "sha256 mismatch" in text
        assert "no fallback" in text
        # No branch/tag fetching as a fallback.
        for ref in ("/archive/refs/heads", "refs/tags", "git checkout main", "git checkout master"):
            assert ref not in text


def test_scripts_require_hashes():
    for path in (PS1, SH):
        assert "--require-hashes" in _read(path)


def test_scripts_do_not_download_toolchains():
    # The scripts validate compilers/cmake but must never fetch them.
    banned = ["apt-get install", "choco install", "brew install", "winget install"]
    for path in (PS1, SH):
        text = _read(path).lower()
        for token in banned:
            assert token not in text, f"{path} must not download toolchains ({token})"


def test_requirements_are_fully_hashed():
    text = _read(REQUIREMENTS)
    # Every pinned requirement line must carry a real 64-hex sha256 hash.
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


def test_scripts_reference_both_requirement_stages():
    for path in (PS1, SH):
        text = _read(path)
        assert "requirements-mgba-bootstrap.txt" in text, f"{path} must install stage-1 wheels"
        assert "requirements-mgba-build.txt" in text, f"{path} must install stage-2 sources"


def test_scripts_use_fail_hard_py_install_target():
    for path in (PS1, SH):
        text = _read(path)
        # The pinned custom target must be invoked...
        assert "mgba-py-install" in text, f"{path} must build the mgba-py-install target"
        # ...and the wrong/fail-open forms must be gone.
        assert "--component python" not in text, f"{path} must not use a Python install component"
        assert "|| true" not in text, f"{path} must not fail-open the install step"


def test_scripts_stamp_inner_git_provenance():
    # The codeload tarball has no .git; version.cmake would run ``git describe``
    # and walk up into the parent FEBuilderGBA repo, mis-stamping the binding.
    # Both scripts must initialize an inner repo pinned to the exact object.
    for path in (PS1, SH):
        text = _read(path)
        assert "git init" in text, f"{path} must initialize an inner git repo"
        assert "https://github.com/mgba-emu/mgba.git" in text, f"{path} must add the official origin"
        assert re.search(r"git\s+remote\s+add\s+origin", text), f"{path} must add origin"
        assert re.search(r"git\s+fetch\b[^\n]*--depth\s+1", text), f"{path} must fetch depth 1"
        assert "FETCH_HEAD" in text, f"{path} must reset to FETCH_HEAD"
        assert re.search(r"git\s+reset\b[^\n]*--hard", text), f"{path} must hard reset to the pin"
        assert "rev-parse HEAD" in text, f"{path} must verify HEAD equals the pin"
        assert "status --porcelain" in text, f"{path} must verify a clean tree"
        # Git must be validated as a local tool, never downloaded.
        assert re.search(r'(Require-Command\s+"git"|require_cmd\s+"git")', text), (
            f"{path} must validate git before use"
        )


def test_git_fetch_targets_exact_pinned_commit_no_fallback():
    ps1 = _read(PS1)
    sh = _read(SH)
    # The exact full commit SHA (via the pinned variable) is fetched directly.
    assert re.search(r"git\s+fetch[^\n]*origin[^\n]*\$MgbaCommit", ps1), (
        "PowerShell must fetch the exact pinned commit"
    )
    assert re.search(r"git\s+fetch[^\n]*origin[^\n]*\$\{MGBA_COMMIT\}", sh), (
        "shell must fetch the exact pinned commit"
    )
    # No branch/tag/HEAD fetch or checkout anywhere (no fallback).
    for path, text in ((PS1, ps1), (SH, sh)):
        assert not re.search(r"git\s+fetch[^\n]*origin\s+(main|master|HEAD)\b", text), (
            f"{path} must not fetch a branch/HEAD"
        )
        assert not re.search(r"git\s+checkout\s+(main|master)", text), f"{path} must not checkout a branch"
        assert "refs/heads" not in text, f"{path} must not reference branch refs"
        assert "refs/tags" not in text, f"{path} must not reference tag refs"


def test_git_provenance_runs_after_verification_before_cmake():
    # Provenance must be stamped only after the archive SHA gate and before the
    # first CMake configure that triggers version.cmake.
    for path in (PS1, SH):
        text = _read(path)
        i_verify = text.find("archive verified")
        i_git = text.find("git init")
        i_cmake = text.find("cmake ..")
        assert -1 < i_verify < i_git < i_cmake, (
            f"{path}: git provenance must sit between archive verification and cmake"
        )


def test_powershell_validates_tar():
    text = _read(PS1)
    assert re.search(r'Require-Command\s+"tar"', text), "PowerShell script must validate tar before use"


def test_runtime_never_installs_or_downloads():
    # Scan every runtime module for network/installer usage.
    banned_tokens = ["pip install", "urllib", "urlopen", "requests.", "http://", "https://",
                     "socket.", "subprocess", "os.system", "curl ", "wget "]
    for name in os.listdir(PKG_DIR):
        if not name.endswith(".py"):
            continue
        text = _read(os.path.join(PKG_DIR, name)).lower()
        for token in banned_tokens:
            assert token not in text, f"runtime module {name} must not use {token!r}"
