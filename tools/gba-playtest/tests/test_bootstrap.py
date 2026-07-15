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
