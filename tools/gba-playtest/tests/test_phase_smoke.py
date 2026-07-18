"""Dependency-free contracts for the native phase smoke."""

import ast
import io
import os
import re

import pytest

import run_real_mgba_phase_smoke as smoke


TEST_DIR = os.path.dirname(os.path.abspath(__file__))
TOOL_DIR = os.path.dirname(TEST_DIR)
ROOT = os.path.dirname(os.path.dirname(TOOL_DIR))
WORKFLOW = os.path.join(ROOT, ".github", "workflows", "gba-playtest.yml")
SH = os.path.join(ROOT, "scripts", "install-mgba-playtest.sh")
PS1 = os.path.join(ROOT, "scripts", "install-mgba-playtest.ps1")
PROOF = os.path.join(os.path.dirname(__file__), "run_real_mgba_proof.py")


def _read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


EXPECTED_PHASES = (
    "construct", "load", "reset", "read", "set-keys", "frame",
    "crash-query", "frame", "crash-query", "read", "screenshot", "close",
)


def test_phase_marker_sequence_is_exact_and_ordered():
    names = [
        marker.split(":")[1]
        for marker in smoke.PHASE_MARKERS
        if marker.endswith(":begin")
    ]
    assert tuple(names) == EXPECTED_PHASES
    assert smoke.PHASE_MARKERS[-1] == "phase:done"
    assert smoke.EXPECTED_STDERR.endswith(b"phase:done\n")


def test_emit_writes_canonical_lf_bytes(monkeypatch):
    class CapturedStderr:
        def __init__(self):
            self.buffer = io.BytesIO()

    captured = CapturedStderr()
    monkeypatch.setattr(smoke.sys, "stderr", captured)
    smoke._emit("phase:test")
    assert captured.buffer.getvalue() == b"phase:test\n"


def test_parent_accepts_only_the_exact_marker_stream(capsys):
    smoke._validate_child(0, b"", smoke.EXPECTED_STDERR)
    capsys.readouterr()

    with pytest.raises(smoke.SmokeFailure, match="missing, extra"):
        smoke._validate_child(
            0, b"", smoke.EXPECTED_STDERR.replace(b"phase:done", b"", 1)
        )
    capsys.readouterr()

    with pytest.raises(smoke.SmokeFailure, match="missing, extra"):
        smoke._validate_child(0, b"", smoke.EXPECTED_STDERR + b"extra\n")
    capsys.readouterr()


def test_parent_rejects_nonzero_and_signal_like_children(capsys):
    with pytest.raises(smoke.SmokeFailure, match="status 7"):
        smoke._validate_child(7, b"", b"")
    with pytest.raises(smoke.SmokeFailure, match="signal 11"):
        smoke._validate_child(-11, b"", b"")
    with pytest.raises(smoke.SmokeFailure, match="signal-like status"):
        smoke._validate_child(0xC0000005, b"", b"")
    capsys.readouterr()


def test_parent_rejects_any_child_stdout(capsys):
    with pytest.raises(smoke.SmokeFailure, match="stdout"):
        smoke._validate_child(0, b"unexpected\n", smoke.EXPECTED_STDERR)
    capsys.readouterr()


def test_smoke_is_data_free_and_child_isolated():
    text = _read(os.path.join(os.path.dirname(__file__), "run_real_mgba_phase_smoke.py"))
    tree = ast.parse(text)
    called = {
        node.func.attr
        for node in ast.walk(tree)
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
    }
    builtin_calls = {
        node.func.id
        for node in ast.walk(tree)
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
    }
    assert "write_text" not in called
    assert "write_bytes" not in called
    assert "open" not in called
    assert "open" not in builtin_calls
    assert "faulthandler" in text
    assert "run_bounded" in text
    assert "stdout_limit=MAX_CHILD_OUTPUT" in text
    assert "stderr_limit=MAX_CHILD_OUTPUT" in text
    assert "build_synthetic_rom()" in text
    assert "want_screenshot=True" in text
    assert 'env["PYTHONNOUSERSITE"] = "1"' in text


def test_main_does_not_echo_launch_oserror(monkeypatch, capsys):
    sensitive_path = r"C:\sensitive\executable\phase-smoke.py"

    def fail_to_launch():
        raise OSError(f"unable to execute {sensitive_path}")

    monkeypatch.setattr(smoke, "_run_child", fail_to_launch)
    assert smoke.main([]) == 1
    output = capsys.readouterr().err
    assert "child launch failure" in output
    assert sensitive_path not in output


def test_workflow_smoke_precedes_unchanged_proof_on_both_platforms():
    text = _read(WORKFLOW)
    ubuntu_start = text.find("real-mgba-ubuntu:")
    windows_start = text.find("real-mgba-windows:")
    assert -1 < ubuntu_start < windows_start

    ubuntu_block = text[ubuntu_start:windows_start]
    windows_block = text[windows_start:]

    for block in (ubuntu_block, windows_block):
        smoke_step = block.find("Run native phase smoke (no artifacts)")
        proof_step = block.find("Run repeated synthetic replay proof")
        assert -1 < smoke_step < proof_step, (
            "each real-mGBA job must run the phase smoke once, before its "
            "unchanged proof step"
        )

    # The smoke script is referenced by the Ubuntu smoke step, the Ubuntu
    # gdb-on-failure diagnostic (`--child`), and the Windows smoke step --
    # three references total, never inside a third real job.
    assert text.count("run_real_mgba_phase_smoke.py") == 3
    assert ubuntu_block.count("run_real_mgba_phase_smoke.py") == 2
    assert windows_block.count("run_real_mgba_phase_smoke.py") == 1
    assert text.count("run_real_mgba_proof.py") == 2


def test_workflow_ubuntu_gdb_diagnostic_is_conditional_on_smoke_failure_only():
    text = _read(WORKFLOW)
    ubuntu_start = text.find("real-mgba-ubuntu:")
    windows_start = text.find("real-mgba-windows:")
    ubuntu_block = text[ubuntu_start:windows_start]

    assert 'id: native_smoke' in ubuntu_block
    diag = ubuntu_block.find("Diagnose native phase smoke crash")
    assert diag != -1, "Ubuntu job must have a gdb-on-failure diagnostic step"
    smoke_step = ubuntu_block.find("Run native phase smoke (no artifacts)")
    assert smoke_step < diag, "the diagnostic step must follow the smoke step"

    diag_block = ubuntu_block[diag:]
    assert "if: failure() && steps.native_smoke.outcome == 'failure'" in diag_block
    assert "continue-on-error" not in diag_block.lower()
    assert "timeout-minutes: 5" in diag_block
    assert "gdb --batch" in diag_block
    assert "thread apply all bt full" in diag_block
    assert "--child" in diag_block
    # The diagnostic step must not upload any artifact or write anywhere.
    assert "upload-artifact" not in diag_block.split("Run repeated synthetic replay proof")[0]


def test_workflow_windows_phase_smoke_has_robust_interpreter_discovery():
    text = _read(WORKFLOW)
    windows_start = text.find("real-mgba-windows:")
    windows_block = text[windows_start:]
    smoke_step = windows_block.find("Run native phase smoke (no artifacts)")
    proof_step = windows_block.find("Run repeated synthetic replay proof")
    assert -1 < smoke_step < proof_step
    smoke_block = windows_block[smoke_step:proof_step]
    assert 'id: native_smoke' in smoke_block
    assert "Scripts\\python.exe" in smoke_block
    assert "bin\\python.exe" in smoke_block
    assert "bootstrap venv interpreter was not found" in smoke_block
    assert "$LASTEXITCODE -ne 0" in smoke_block
    assert "exit $LASTEXITCODE" in smoke_block


def test_msystem_generator_branch_requires_usr_bin_make_before_ninja():
    text = _read(SH)
    msystem = text.find('if [ -n "${MSYSTEM:-}" ]')
    ninja = text.find("command -v ninja")
    assert -1 < msystem < ninja
    assert 'GENERATOR=(-G "MSYS Makefiles")' in text
    assert "[ ! -f /usr/bin/make ]" in text
    assert "[ ! -x /usr/bin/make ]" in text
    assert "MSYSTEM=" in text and "/usr/bin/make" in text
    assert re.search(r"elif command -v ninja\b", text)
    assert 'GENERATOR=(-G "Unix Makefiles")' in text


def test_windows_probe_and_guidance_require_msys_make_not_ninja_or_make():
    text = _read(PS1)
    assert "/usr/bin/make" in text
    assert "ninja-or-make" not in text
    assert "command -v ninja" not in text
    assert "mingw-w64-ucrt-x86_64-ninja" not in text
    assert re.search(r"if \[ ! -f /usr/bin/make \].*\n.*missing=.*\/usr/bin/make", text)
    assert "  make" in text


def test_windows_workflow_installs_plain_make_and_not_pacboy_ninja():
    text = _read(WORKFLOW)
    install_start = text.index("        install: >-")
    pacboy_start = text.index("        pacboy: >-", install_start)
    install = text[install_start:pacboy_start]
    pacboy_end = text.index("\n\n    - name:", pacboy_start)
    pacboy = text[pacboy_start:pacboy_end]
    assert re.search(r"\n          make\s*$", install)
    assert "ninja:p" not in pacboy


def test_protected_six_replay_oracle_content_is_still_present():
    text = _read(PROOF)
    assert "REPLAY_COUNT = 3" in text
    assert "replayCount" in text
    assert "REPLAY_COUNT * 2" in text
    assert "build_synthetic_rom()" in text
    assert "proof-summary.json" in text
    assert "run_real_mgba_phase_smoke" not in text
