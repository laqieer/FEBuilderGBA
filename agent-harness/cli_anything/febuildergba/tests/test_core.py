"""Unit tests for cli-anything-febuildergba core modules.

All tests use synthetic data — no external dependencies required.
"""

import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import pytest


def _write_valid_test_rom(path, game_code):
    rom = bytearray(0x100000)
    rom[0xAC:0xB0] = game_code
    rom[0xB2] = 0x96
    rom[0xBD] = (-sum(rom[0xA0:0xBD]) - 0x19) & 0xFF
    path.write_bytes(rom)


# ── Backend availability tests ────────────────────────────────────────

class TestBackend:
    def test_get_version_success(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        result = subprocess.CompletedProcess(["cli", "--version"], 0,
                                             stdout="FEBuilderGBA 1.2.3\n", stderr="")
        monkeypatch.setattr(backend, "run_cli", lambda args: result)
        assert backend.get_version() == "FEBuilderGBA 1.2.3"

    def test_get_version_rejects_failed_probe(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        result = subprocess.CompletedProcess(["cli", "--version"], 7,
                                             stdout="", stderr="SDK unavailable")
        monkeypatch.setattr(backend, "run_cli", lambda args: result)
        with pytest.raises(RuntimeError, match="exit code 7"):
            backend.get_version()

    def test_get_version_rejects_empty_probe(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        result = subprocess.CompletedProcess(["cli", "--version"], 0,
                                             stdout=" \n", stderr="")
        monkeypatch.setattr(backend, "run_cli", lambda args: result)
        with pytest.raises(RuntimeError, match="no version text"):
            backend.get_version()

    def test_get_version_accepts_exact_output_limit(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        text = "v" * backend.MAX_VERSION_TEXT_LEN
        result = subprocess.CompletedProcess(
            ["cli", "--version"], 0, stdout=text, stderr="",
        )
        monkeypatch.setattr(backend, "run_cli", lambda args: result)

        assert backend.get_version() == text

    def test_get_version_rejects_output_over_limit(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        text = "v" * (backend.MAX_VERSION_TEXT_LEN + 1)
        result = subprocess.CompletedProcess(
            ["cli", "--version"], 0, stdout=text, stderr="",
        )
        monkeypatch.setattr(backend, "run_cli", lambda args: result)

        with pytest.raises(RuntimeError, match="output exceeded 4096 characters"):
            backend.get_version()

    def test_check_backend_reports_probe_failure(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["cli"])

        def fail_version():
            raise RuntimeError("probe failed")

        monkeypatch.setattr(backend, "get_version", fail_version)
        result = backend.check_backend()
        assert result["available"] is False
        assert result["error"] == "probe failed"

    def test_run_cli_wraps_os_launch_failure(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["blocked-cli"])

        def fail_launch(*args, **kwargs):
            raise PermissionError("execution denied")

        monkeypatch.setattr(backend.subprocess, "run", fail_launch)

        with pytest.raises(RuntimeError, match="execution denied"):
            backend.run_cli(["--version"])

    def test_run_cli_outside_bounded_context_keeps_subprocess_run_seam(
            self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        calls = []
        expected = subprocess.CompletedProcess(
            ["legacy-cli", "--version"], 0, stdout="full output", stderr="",
        )
        monkeypatch.setattr(
            backend, "find_febuildergba_cli", lambda: ["legacy-cli"],
        )

        def fake_run(*args, **kwargs):
            calls.append((args, kwargs))
            return expected

        def should_not_use_popen(*args, **kwargs):
            raise AssertionError("outside bounded capture must use subprocess.run")

        monkeypatch.setattr(backend.subprocess, "run", fake_run)
        monkeypatch.setattr(backend.subprocess, "Popen", should_not_use_popen)

        assert backend.run_cli(["--version"], timeout=17) is expected
        assert backend.run_cli(["--no-capture"], capture=False, timeout=19) is expected
        assert calls == [
            (
                (["legacy-cli", "--version"],),
                {"capture_output": True, "text": True, "timeout": 17},
            ),
            (
                (["legacy-cli", "--no-capture"],),
                {"capture_output": False, "text": True, "timeout": 19},
            ),
        ]

    def test_bounded_capture_concurrently_drains_both_pipes(
            self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        limit = 257
        chars_per_stream = 2_097_152
        script = (
            "import sys\n"
            "for _ in range(1024):\n"
            "    sys.stdout.write('o' * 2048)\n"
            "    sys.stdout.flush()\n"
            "    sys.stderr.write('e' * 2048)\n"
            "    sys.stderr.flush()\n"
        )
        monkeypatch.setattr(
            backend, "find_febuildergba_cli",
            lambda: [sys.executable, "-c", script],
        )

        with backend.bounded_capture(limit):
            result = backend.run_cli([], timeout=15)

        assert isinstance(result.stdout, str)
        assert isinstance(result.stderr, str)
        assert result.stdout == "o" * limit
        assert result.stderr == "e" * limit
        assert len(result.stdout) == limit
        assert len(result.stderr) == limit
        assert result.stdout.truncated is True
        assert result.stderr.truncated is True
        assert result.stdout.original_length == chars_per_stream
        assert result.stderr.original_length == chars_per_stream

    def test_bounded_capture_strip_preserves_source_metadata(
            self, monkeypatch):
        from cli_anything.febuildergba.core import verbs
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        stdout_source = " \tWXYZ \n"
        stderr_source = "\n EEEE \t"
        script = (
            "import sys\n"
            f"sys.stdout.write({stdout_source!r})\n"
            f"sys.stderr.write({stderr_source!r})\n"
        )
        monkeypatch.setattr(
            backend, "find_febuildergba_cli",
            lambda: [sys.executable, "-c", script],
        )

        with backend.bounded_capture(6):
            result = backend.run_cli([])

        stdout = result.stdout.strip()
        stderr = result.stderr.strip()
        assert stdout == "WXYZ"
        assert stderr == "EEEE"
        assert stdout.truncated is True
        assert stderr.truncated is True
        assert stdout.original_length == len(stdout_source)
        assert stderr.original_length == len(stderr_source)

        # _base_result is an existing no-argument .strip() core wrapper.
        wrapped = verbs._base_result(result)
        assert wrapped["stdout"].original_length == len(stdout_source)
        assert wrapped["stderr"].original_length == len(stderr_source)
        assert wrapped["stdout"].truncated is True
        assert wrapped["stderr"].truncated is True

    def test_bounded_capture_decodes_multibyte_output_across_chunks(
            self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        # Write every UTF-8 byte separately so TextIOWrapper must carry
        # incomplete code points across underlying pipe reads.
        script = (
            "import os, sys, time\n"
            "data = b'\\xe2\\x82\\xac\\xe6\\xbc\\xa2\\xf0\\x9f\\x99\\x82'\n"
            "for fd in (sys.stdout.fileno(), sys.stderr.fileno()):\n"
            "    for byte in data:\n"
            "        os.write(fd, bytes((byte,)))\n"
            "        time.sleep(0.001)\n"
        )
        monkeypatch.setattr(
            backend, "find_febuildergba_cli",
            lambda: [sys.executable, "-c", script],
        )
        monkeypatch.setattr(backend, "_OUTPUT_READ_CHARS", 1)

        with backend.bounded_capture(10):
            result = backend.run_cli([])

        assert result.stdout == "€漢🙂"
        assert result.stderr == "€漢🙂"
        assert result.stdout.original_length == 3
        assert result.stderr.original_length == 3
        assert result.stdout.truncated is False
        assert result.stderr.truncated is False

    def test_bounded_capture_timeout_uses_existing_runtime_error(
            self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        monkeypatch.setattr(
            backend, "find_febuildergba_cli",
            lambda: [sys.executable, "-c", "import time; time.sleep(60)"],
        )
        started = time.monotonic()
        with backend.bounded_capture(16):
            with pytest.raises(
                    RuntimeError,
                    match=r"Command timed out after 0.1s: .*",
            ):
                backend.run_cli([], timeout=0.1)
        assert time.monotonic() - started < 3

    def test_bounded_capture_detaches_backend_stdin_from_pending_frames(self):
        """A bounded backend must not consume the MCP server's next request."""
        harness_root = Path(__file__).resolve().parents[3]
        backend_script = (
            "import sys\n"
            "sys.stdout.write('child-read=' + sys.stdin.read())\n"
        )
        outer_script = (
            "import json\n"
            "import sys\n"
            "from cli_anything.febuildergba.utils import febuildergba_backend as backend\n"
            f"backend.find_febuildergba_cli = lambda: [sys.executable, '-c', {backend_script!r}]\n"
            "current_frame = sys.stdin.readline()\n"
            "with backend.bounded_capture(64):\n"
            "    result = backend.run_cli([])\n"
            "print(json.dumps({\n"
            "    'backend_output': str(result.stdout),\n"
            "    'current_frame': current_frame,\n"
            "    'next_frame': sys.stdin.readline(),\n"
            "}))\n"
        )
        current_frame = '{"jsonrpc":"2.0","id":2,"method":"tools/call"}\n'
        next_frame = '{"jsonrpc":"2.0","id":3,"method":"ping"}\n'
        pending_input = current_frame + next_frame
        env = os.environ.copy()
        env["PYTHONPATH"] = str(harness_root) + os.pathsep + env.get("PYTHONPATH", "")

        outer = subprocess.run(
            [sys.executable, "-c", outer_script],
            input=pending_input,
            capture_output=True,
            text=True,
            timeout=10,
            env=env,
        )
        assert outer.returncode == 0, outer.stderr
        observed = json.loads(outer.stdout)

        # Before stdin=DEVNULL, the fake backend read the pending ping and the
        # outer parent observed EOF.  This proves the real pipe boundary, not
        # merely a mocked Popen keyword.
        assert observed["backend_output"] == "child-read="
        assert observed["current_frame"] == current_frame
        assert observed["next_frame"] == next_frame

    def test_check_backend_normalizes_os_probe_failure(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["blocked-cli"])

        def fail_version():
            raise PermissionError("execution denied")

        monkeypatch.setattr(backend, "get_version", fail_version)
        result = backend.check_backend()

        assert result == {"available": False, "error": "execution denied"}


class TestLint:
    def test_clean_summary_is_not_an_error(self, monkeypatch):
        from cli_anything.febuildergba.core import lint
        result = subprocess.CompletedProcess(
            ["cli", "--lint"], 0, stdout="Lint: No errors found.\n", stderr="",
        )
        monkeypatch.setattr(lint, "run_cli", lambda args: result)

        parsed = lint.lint_rom("r.gba")

        assert parsed["clean"] is True
        assert parsed["error_count"] == 0
        assert parsed["warning_count"] == 0
        assert parsed["errors"] == []
        assert parsed["warnings"] == []
        assert parsed["info"] == ["Lint: No errors found."]

    def test_only_severity_markers_create_findings(self, monkeypatch):
        from cli_anything.febuildergba.core import lint
        stdout = "\n".join([
            "Lint: 2 issue(s) found:",
            "  [ERROR] 0x08000000: broken pointer",
            "  [WARNING] 0x08000004: suspicious value",
            "Informational warning and error words",
        ])
        result = subprocess.CompletedProcess(
            ["cli", "--lint"], 1, stdout=stdout, stderr="",
        )
        monkeypatch.setattr(lint, "run_cli", lambda args: result)

        parsed = lint.lint_rom("r.gba")

        assert parsed["clean"] is False
        assert parsed["error_count"] == 1
        assert parsed["warning_count"] == 1
        assert parsed["errors"] == ["  [ERROR] 0x08000000: broken pointer"]
        assert parsed["warnings"] == [
            "  [WARNING] 0x08000004: suspicious value",
        ]
        assert "Informational warning and error words" in parsed["info"]


class TestOutputFileReporting:
    @pytest.mark.parametrize(
        ("module_name", "invoke"),
        [
            ("cli_anything.febuildergba.core.export",
             lambda module, out: module.create_ups("r.gba", out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out: module.disassemble("r.gba", out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out: module.decrease_color("in.png", out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out: module.render_portrait("r.gba", 1, out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out: module.export_midi("r.gba", "1A", out)),
            ("cli_anything.febuildergba.core.text",
             lambda module, out: module.export_text("r.gba", out)),
            ("cli_anything.febuildergba.core.verbs",
             lambda module, out: module.export_map_settings_raw("r.gba", out)),
            ("cli_anything.febuildergba.core.verbs",
             lambda module, out: module.export_palette("r.gba", "0x5524", out)),
            ("cli_anything.febuildergba.core.verbs",
             lambda module, out: module.lz77_file("compress", "in.bin", out)),
        ],
    )
    def test_failed_wrappers_do_not_report_stale_output(
            self, module_name, invoke, monkeypatch, tmp_path):
        import importlib

        module = importlib.import_module(module_name)
        output = tmp_path / "stale.bin"
        output.write_bytes(b"stale")
        monkeypatch.setattr(
            module,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                args, 1, stdout="", stderr="backend failed",
            ),
        )

        result = invoke(module, str(output))

        assert result["exit_code"] == 1
        assert result["file_size"] == 0


class TestImageQuantizeContract:
    def test_core_wrapper_uses_backend_color_count_semantics(self, monkeypatch):
        from cli_anything.febuildergba.core import export
        calls = []

        def fake_run_cli(args):
            calls.append(args)
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(export, "run_cli", fake_run_cli)

        export.decrease_color("in.png", "out.png")
        export.decrease_color("in.png", "out.png", 256)

        assert "--paletteno=16" in calls[0]
        assert "--paletteno=256" in calls[1]

    @pytest.mark.parametrize(
        ("args", "expected_colors"),
        [
            ([], 16),
            (["--palette-no", "256"], 256),
            (["--palette-no", "1", "--no-reserve-1st"], 1),
        ],
    )
    def test_click_wrapper_accepts_backend_color_ranges(
            self, monkeypatch, args, expected_colors):
        from click.testing import CliRunner
        from cli_anything.febuildergba import febuildergba_cli
        from cli_anything.febuildergba.core import export
        calls = []

        def fake_decrease_color(
                in_path, out_path, palette_no, no_scale,
                no_reserve_1st, ignore_tsa):
            calls.append({
                "in_path": in_path,
                "out_path": out_path,
                "palette_no": palette_no,
                "no_reserve_1st": no_reserve_1st,
            })
            return {
                "output_path": out_path,
                "file_size": 0,
                "exit_code": 0,
                "stdout": "",
                "stderr": "",
            }

        monkeypatch.setattr(export, "decrease_color", fake_decrease_color)
        result = CliRunner().invoke(
            febuildergba_cli.cli,
            [
                "--session-file", "session.json",
                "image", "quantize",
                "--input-file", "in.png",
                "--out", "out.png",
                *args,
            ],
        )

        assert result.exit_code == 0, result.output
        assert calls[0]["palette_no"] == expected_colors

    def test_click_wrapper_rejects_one_color_when_slot_zero_is_reserved(
            self, monkeypatch):
        from click.testing import CliRunner
        from cli_anything.febuildergba import febuildergba_cli
        from cli_anything.febuildergba.core import export
        calls = []
        monkeypatch.setattr(
            export,
            "decrease_color",
            lambda *args: calls.append(args),
        )

        result = CliRunner().invoke(
            febuildergba_cli.cli,
            [
                "--session-file", "session.json",
                "image", "quantize",
                "--input-file", "in.png",
                "--out", "out.png",
                "--palette-no", "1",
            ],
        )

        assert result.exit_code == 2
        assert "must be at least 2 unless --no-reserve-1st is set" in result.output
        assert calls == []


# ── Session tests ─────────────────────────────────────────────────────

class TestSession:
    """Tests for core/session.py."""

    def test_create_default_session(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        assert not sess.is_open()

    def test_missing_session_does_not_create_lock(self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "missing" / "test_session.json"

        def fail_if_called(_self):
            raise AssertionError("missing session must not acquire a lock")

        monkeypatch.setattr(Session, "_acquire_lock", fail_if_called)

        sess = Session(str(path))

        assert not sess.is_open()
        assert not path.parent.exists()
        assert not (tmp_path / "missing" / "test_session.json.lock").exists()

    @pytest.mark.parametrize("command", ["check", "lz77"])
    def test_stateless_click_command_does_not_lock_missing_session(
            self, tmp_path, monkeypatch, command):
        from click.testing import CliRunner
        from cli_anything.febuildergba import febuildergba_cli
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core import verbs
        from cli_anything.febuildergba.utils import febuildergba_backend

        session_dir = tmp_path / "unavailable-session-dir"
        monkeypatch.setattr(
            session_module, "_default_session_dir", lambda: session_dir,
        )

        def fail_if_called(_self):
            raise AssertionError("stateless command must not acquire a session lock")

        monkeypatch.setattr(
            session_module.Session, "_acquire_lock", fail_if_called,
        )
        if command == "check":
            monkeypatch.setattr(
                febuildergba_backend,
                "check_backend",
                lambda: {
                    "available": True,
                    "version": "test",
                    "command": "test-cli",
                },
            )
            args = ["check"]
        else:
            monkeypatch.setattr(
                verbs,
                "lz77_file",
                lambda mode, in_file, out: {
                    "exit_code": 0,
                    "stdout": "",
                    "stderr": "",
                    "file_size": 1,
                },
            )
            args = [
                "lz77", "-i", "in.bin", "-o", "out.bin", "--compress",
            ]

        result = CliRunner().invoke(febuildergba_cli.cli, args)

        assert result.exit_code == 0, result.output
        assert not session_dir.exists()

    def test_open_rom(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U", 16777216)
        assert sess.is_open()
        assert sess.state.rom_path.endswith("rom.gba")
        assert sess.state.rom_version == "FE8U"
        assert sess.state.rom_size == 16777216

    def test_close_session(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        assert sess.close() is True
        assert not sess.is_open()
        assert not (tmp_path / "test_session.json").exists()

    def test_click_close_reports_stale_session(self, monkeypatch):
        from click.testing import CliRunner
        from cli_anything.febuildergba import febuildergba_cli

        class StaleSession:
            def is_open(self):
                return True

            def close(self):
                return False

        stale_session = StaleSession()
        monkeypatch.setattr(
            febuildergba_cli,
            "Session",
            lambda session_file: stale_session,
        )

        result = CliRunner().invoke(
            febuildergba_cli.cli,
            ["--json", "--session-file", "session.json", "session", "close"],
        )
        assert result.exit_code == 0, result.output
        assert json.loads(result.output) == {"status": "stale_session"}

    def test_record_operation(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        sess.record_operation("data_export", {"table": "units"})
        assert len(sess.state.history) == 2  # open + data_export
        assert sess.state.history[-1]["op"] == "data_export"

    def test_mark_modified(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        assert not sess.state.modified
        sess.mark_modified()
        assert sess.state.modified

    def test_session_persistence(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")

        sess1 = Session(path)
        sess1.open_rom("/fake/rom.gba", "FE8U", 16777216)
        sess1.record_operation("lint", {})

        # Reload from disk
        sess2 = Session(path)
        assert sess2.is_open()
        assert sess2.state.rom_version == "FE8U"
        assert sess2.state.rom_size == 16777216
        assert len(sess2.state.history) == 2

    def test_history_limit(self, tmp_path):
        from cli_anything.febuildergba.core.session import (
            MAX_HISTORY_ENTRIES,
            Session,
        )
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")
        for i in range(150):
            sess.record_operation(f"op_{i}", {})
        assert len(sess.state.history) == MAX_HISTORY_ENTRIES

    def test_loaded_history_is_clamped(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        history = [{"op": f"op_{i}"} for i in range(150)]
        path.write_text(json.dumps({
            "rom_path": "/fake/rom.gba",
            "history": history,
        }))

        sess = Session(str(path))

        assert len(sess.state.history) == 100
        assert sess.state.history[0]["op"] == "op_50"

    def test_loaded_non_list_history_is_discarded(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_text(json.dumps({
            "rom_path": "/fake/rom.gba",
            "history": "not-a-list",
        }))

        sess = Session(str(path))

        assert sess.state.history == []

    def test_legacy_session_file_loads_without_session_id(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_text(json.dumps({
            "rom_path": "/fake/rom.gba",
            "created_at": 1.0,
            "history": [{"op": "open"}],
        }))

        sess = Session(str(path))

        assert sess.is_open()
        assert sess.state.session_id == ""

    def test_info_output(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U", 16777216, "FE8U")
        info = sess.info()
        assert "rom_path" in info
        assert "rom_version" in info
        assert info["rom_version"] == "FE8U"
        assert info["rom_size"] == 16777216
        assert "session_file" in info

    def test_empty_session_info(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        info = sess.info()
        assert info["rom_path"] == ""
        assert info["history_count"] == 0

    def test_corrupt_json_recovery(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_text("not valid json {{{")
        sess = Session(str(path))
        assert not sess.is_open()

    def test_invalid_utf8_session_loads_closed(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_bytes(b'{"rom_path":"/fake/rom.gba\xff"}')

        sess = Session(str(path))

        assert not sess.is_open()

    def test_excessive_integer_digits_session_loads_closed(self, tmp_path):
        from cli_anything.febuildergba.core.session import (
            MAX_SESSION_INTEGER_DIGITS,
            Session,
        )
        path = tmp_path / "test_session.json"
        path.write_bytes(
            b'{"rom_path":"/fake/rom.gba","rom_size":'
            + b"9" * (MAX_SESSION_INTEGER_DIGITS + 1)
            + b"}"
        )

        sess = Session(str(path))

        assert not sess.is_open()

    def test_nonstandard_constant_session_loads_closed(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_bytes(
            b'{"rom_path":"/fake/rom.gba","history":[{"value":NaN}]}'
        )

        sess = Session(str(path))

        assert not sess.is_open()

    def test_float_overflow_session_loads_closed(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        path.write_bytes(
            b'{"rom_path":"/fake/rom.gba","history":[{"value":1e1000000}]}'
        )

        sess = Session(str(path))

        assert not sess.is_open()

    def test_excessive_nesting_session_loads_closed(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        depth = sys.getrecursionlimit() + 1
        path.write_bytes(b"[" * depth + b"0" + b"]" * depth)

        sess = Session(str(path))

        assert not sess.is_open()

    def test_oversized_session_file_loads_closed(self, tmp_path):
        from cli_anything.febuildergba.core.session import (
            MAX_SESSION_FILE_BYTES,
            Session,
        )
        path = tmp_path / "test_session.json"
        content = b'{"rom_path":"/fake/rom.gba"}'
        path.write_bytes(
            content + b" " * (MAX_SESSION_FILE_BYTES + 1 - len(content))
        )

        sess = Session(str(path))

        assert not sess.is_open()

    def test_force_version_stored(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U", force_version="FE8U")
        assert sess.state.force_version == "FE8U"

    def test_stale_sessions_merge_same_generation_history(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")
        first = Session(path)
        first.open_rom("/fake/rom.gba", "FE8U")
        second = Session(path)

        assert first.record_operation("first")
        assert second.record_operation("second")

        reloaded = Session(path)
        assert [entry["op"] for entry in reloaded.state.history][-2:] == [
            "first", "second",
        ]

    def test_stale_operation_is_skipped_after_reopen(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")
        stale = Session(path)
        stale.open_rom("/fake/rom.gba", "FE8U")
        old_id = stale.state.session_id
        current = Session(path)
        current.open_rom("/fake/rom.gba", "FE8U")

        assert current.state.session_id != old_id
        assert stale.record_operation("stale", modified=True) is False
        assert stale.state.session_id == current.state.session_id
        assert [entry["op"] for entry in stale.state.history] == ["open"]
        assert stale.state.modified is False

    def test_stale_close_is_skipped_after_reopen(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")
        stale = Session(path)
        stale.open_rom("/fake/a.gba", "FE8U")
        current = Session(path)
        current.open_rom("/fake/b.gba", "FE8U")

        assert stale.close() is False
        assert stale.state.session_id == current.state.session_id
        assert stale.state.rom_path.endswith("b.gba")

        persisted = Session(path)
        assert persisted.state.session_id == current.state.session_id
        assert persisted.state.rom_path.endswith("b.gba")
        assert [entry["op"] for entry in persisted.state.history] == ["open"]

    def test_stale_operation_is_skipped_after_close(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")
        stale = Session(path)
        stale.open_rom("/fake/rom.gba", "FE8U")
        current = Session(path)
        assert current.close() is True

        assert stale.record_operation("stale") is False
        assert not (tmp_path / "test_session.json").exists()
        assert not stale.is_open()

    def test_record_operation_can_atomically_mark_modified(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")

        assert sess.record_operation("import", modified=True)

        assert sess.state.history[-1]["op"] == "import"
        assert sess.state.modified is True

    def test_stale_session_cannot_overwrite_reopened_rom(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = str(tmp_path / "test_session.json")
        server_session = Session(path)
        server_session.open_rom("/fake/a.gba", "FE8U")
        external_session = Session(path)
        external_session.open_rom("/fake/b.gba", "FE8U")

        assert server_session.record_operation("data_export") is False

        persisted = Session(path)
        assert persisted.state.rom_path.endswith("b.gba")
        assert [entry["op"] for entry in persisted.state.history] == ["open"]

    def test_lock_timeout_does_not_change_disk(self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U")
        before = path.read_bytes()
        monkeypatch.setattr(session_module, "SESSION_LOCK_TIMEOUT_SECONDS", 0)
        monkeypatch.setattr(sess, "_try_acquire_lock", lambda lock_file: False)

        with pytest.raises(TimeoutError, match="Timed out waiting for session lock"):
            sess.record_operation("blocked")

        assert path.read_bytes() == before

    def test_transaction_reentrancy_is_rejected_and_releases_lock(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        sess = Session(str(tmp_path / "test_session.json"))
        sess.open_rom("/fake/rom.gba", "FE8U")

        with sess._transaction():
            with pytest.raises(RuntimeError, match="Nested session transaction"):
                with sess._transaction():
                    pass

        sess.refresh()
        assert sess.record_operation("after_nested_transaction") is True

    def test_lock_sidecar_persists_after_close_and_save_is_not_public(self, tmp_path):
        from cli_anything.febuildergba.core.session import Session
        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U")
        sess.close()

        assert not path.exists()
        assert (tmp_path / "test_session.json.lock").read_bytes()[:1] == b"\0"
        assert not hasattr(sess, "save")


_CLICK_SESSION_COMMAND_CASES = (
    (
        "cli_anything.febuildergba.core.verbs.repair_header",
        ("rom", "repair-header"),
        "repair_header",
        True,
    ),
    (
        "cli_anything.febuildergba.core.data.export_table",
        ("data", "export", "units", "--out", "out.tsv"),
        "data_export",
        False,
    ),
    (
        "cli_anything.febuildergba.core.data.import_table",
        ("data", "import", "units", "--input-file", "in.tsv"),
        "data_import",
        True,
    ),
    (
        "cli_anything.febuildergba.core.text.import_text",
        ("text", "import", "--input-file", "text.tsv"),
        "text_import",
        True,
    ),
    (
        "cli_anything.febuildergba.core.export.apply_ups",
        ("patch", "apply", "patch.ups"),
        "patch_apply",
        False,
    ),
    (
        "cli_anything.febuildergba.core.verbs.import_midi",
        ("import-midi", "1A", "--in", "song.mid"),
        "import_midi",
        True,
    ),
    (
        "cli_anything.febuildergba.core.verbs.compile_event",
        ("compile-event", "--in", "script.event", "--out", "out.gba"),
        "compile_event",
        False,
    ),
    (
        "cli_anything.febuildergba.core.verbs.import_palette",
        ("palette", "import", "--addr", "0x5524", "--in", "palette.pal"),
        "import_palette",
        True,
    ),
    (
        "cli_anything.febuildergba.core.export.apply_patch",
        ("patch", "apply-bin", "patch.txt"),
        "patch_apply_bin",
        True,
    ),
    (
        "cli_anything.febuildergba.core.export.rebuild",
        ("rebuild", "--from-rom", "clean.gba"),
        "rebuild",
        False,
    ),
)


class TestClickSessionOwnership:
    @staticmethod
    def _result(output_path):
        return {
            "exit_code": 0,
            "repaired": True,
            "stdout": "",
            "stderr": "",
            "output_path": output_path,
            "file_size": 1,
        }

    @staticmethod
    def _open_session(tmp_path, rom_path):
        from cli_anything.febuildergba.core.session import Session
        session_path = tmp_path / "session.json"
        session = Session(str(session_path))
        session.open_rom(str(rom_path), "FE8U", rom_path.stat().st_size)
        return session_path

    @pytest.mark.parametrize(
        ("backend_target", "command_args", "expected_op", "expected_modified"),
        _CLICK_SESSION_COMMAND_CASES,
    )
    def test_explicit_other_rom_is_not_attributed(
        self,
        tmp_path,
        monkeypatch,
        backend_target,
        command_args,
        expected_op,
        expected_modified,
    ):
        from click.testing import CliRunner
        from cli_anything.febuildergba.core.session import Session
        from cli_anything.febuildergba.febuildergba_cli import cli

        active_rom = tmp_path / "active.gba"
        other_rom = tmp_path / "other.gba"
        active_rom.write_bytes(b"active")
        other_rom.write_bytes(b"other")
        session_path = self._open_session(tmp_path, active_rom)
        output_path = str(tmp_path / "out.gba")
        monkeypatch.setattr(
            backend_target,
            lambda *args, **kwargs: self._result(output_path),
        )

        result = CliRunner().invoke(
            cli,
            [
                "--json",
                "--rom",
                str(other_rom),
                "--session-file",
                str(session_path),
                *command_args,
            ],
        )

        assert result.exit_code == 0, result.output
        persisted = Session(str(session_path))
        assert [entry["op"] for entry in persisted.state.history] == ["open"]
        assert persisted.state.modified is False

    @pytest.mark.parametrize(
        ("backend_target", "command_args", "expected_op", "expected_modified"),
        _CLICK_SESSION_COMMAND_CASES,
    )
    def test_hardlink_alias_is_attributed(
        self,
        tmp_path,
        monkeypatch,
        backend_target,
        command_args,
        expected_op,
        expected_modified,
    ):
        from click.testing import CliRunner
        from cli_anything.febuildergba.core.session import Session
        from cli_anything.febuildergba.febuildergba_cli import cli

        active_rom = tmp_path / "active.gba"
        alias_rom = tmp_path / "alias.gba"
        active_rom.write_bytes(b"active")
        os.link(active_rom, alias_rom)
        session_path = self._open_session(tmp_path, active_rom)
        output_path = str(tmp_path / "out.gba")
        monkeypatch.setattr(
            backend_target,
            lambda *args, **kwargs: self._result(output_path),
        )

        result = CliRunner().invoke(
            cli,
            [
                "--json",
                "--rom",
                str(alias_rom),
                "--session-file",
                str(session_path),
                *command_args,
            ],
        )

        assert result.exit_code == 0, result.output
        persisted = Session(str(session_path))
        assert [entry["op"] for entry in persisted.state.history] == [
            "open",
            expected_op,
        ]
        assert persisted.state.modified is expected_modified

    def test_compile_event_in_place_marks_session_modified(
            self, tmp_path, monkeypatch):
        from click.testing import CliRunner
        from cli_anything.febuildergba.core.session import Session
        from cli_anything.febuildergba.febuildergba_cli import cli

        active_rom = tmp_path / "active.gba"
        alias_rom = tmp_path / "alias.gba"
        active_rom.write_bytes(b"active")
        os.link(active_rom, alias_rom)
        session_path = self._open_session(tmp_path, active_rom)
        monkeypatch.setattr(
            "cli_anything.febuildergba.core.verbs.compile_event",
            lambda *args, **kwargs: self._result(str(alias_rom)),
        )

        result = CliRunner().invoke(
            cli,
            [
                "--json",
                "--rom",
                str(alias_rom),
                "--session-file",
                str(session_path),
                "compile-event",
                "--in",
                "script.event",
            ],
        )

        assert result.exit_code == 0, result.output
        persisted = Session(str(session_path))
        assert [entry["op"] for entry in persisted.state.history] == [
            "open",
            "compile_event",
        ]
        assert persisted.state.modified is True


# ── Project tests ─────────────────────────────────────────────────────

class TestProject:
    """Tests for core/project.py."""

    def test_list_tables(self):
        from cli_anything.febuildergba.core.project import list_tables
        tables = list_tables()
        assert len(tables) == 40
        assert "units" in tables
        assert "classes" in tables
        assert "items" in tables
        assert "portraits" in tables
        assert "map_settings" in tables

    def test_validate_rom_missing(self):
        from cli_anything.febuildergba.core.project import validate_rom
        assert not validate_rom("/nonexistent/rom.gba")

    def test_validate_rom_small_file(self, tmp_path):
        from cli_anything.febuildergba.core.project import validate_rom
        small = tmp_path / "small.gba"
        small.write_bytes(b"\x00" * 100)
        assert not validate_rom(str(small))

    def test_read_validated_header_checks_opened_descriptor(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "fe8u.gba"
        _write_valid_test_rom(path, b"BE8E")
        real_open = project.os.open
        monkeypatch.setattr(
            project.os,
            "open",
            lambda open_path, flags: real_open(os.devnull, flags),
        )

        with pytest.raises(ValueError, match="not a regular file"):
            project._read_validated_header(str(path))

    def test_read_validated_header_uses_safe_descriptor_flags(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "fe8u.gba"
        _write_valid_test_rom(path, b"BE8E")
        calls = []
        real_open = project.os.open

        def tracking_open(open_path, flags):
            calls.append((open_path, flags))
            return real_open(open_path, flags)

        monkeypatch.setattr(project.os, "open", tracking_open)
        project._read_validated_header(str(path))

        assert len(calls) == 1
        assert calls[0][1] == project._rom_open_flags()
        if hasattr(project.os, "O_NONBLOCK"):
            assert calls[0][1] & project.os.O_NONBLOCK

    def test_read_validated_header_accepts_exact_32_mib(self, tmp_path):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "max-size.gba"
        _write_valid_test_rom(path, b"BE8E")
        with path.open("r+b") as stream:
            stream.truncate(project._MAX_ROM_SIZE)

        header, size = project._read_validated_header(str(path))

        assert size == project._MAX_ROM_SIZE
        assert header[0xAC:0xB0] == b"BE8E"

    def test_rom_info_rejects_over_32_mib_before_backend(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "oversized.gba"
        _write_valid_test_rom(path, b"BE8E")
        with path.open("r+b") as stream:
            stream.truncate(project._MAX_ROM_SIZE + 1)
        backend_calls = []
        fdopen_calls = []
        real_fdopen = project.os.fdopen
        monkeypatch.setattr(
            project,
            "run_cli",
            lambda args: backend_calls.append(args),
        )
        monkeypatch.setattr(
            project.os,
            "fdopen",
            lambda *args, **kwargs: (
                fdopen_calls.append(args),
                real_fdopen(*args, **kwargs),
            )[1],
        )

        with pytest.raises(ValueError, match="larger than 32 MiB"):
            project.rom_info(str(path))

        assert backend_calls == []
        assert fdopen_calls == []
        assert project.validate_rom(str(path)) is False

    def test_checksum_target_allows_bad_header_checksum(self, tmp_path):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "bad-checksum.gba"
        _write_valid_test_rom(path, b"BE8E")
        content = bytearray(path.read_bytes())
        content[0xBD] ^= 0xFF
        path.write_bytes(content)

        project.validate_checksum_target(str(path))

    def test_checksum_target_rejects_missing_fixed_header_byte(self, tmp_path):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "not-rom.bin"
        path.write_bytes(b"\x00" * 0x100000)

        with pytest.raises(ValueError, match="missing fixed header byte"):
            project.validate_checksum_target(str(path))

    def test_checksum_header_reports_valid_and_invalid(self, tmp_path):
        from cli_anything.febuildergba.core import project
        path = tmp_path / "checksum.gba"
        _write_valid_test_rom(path, b"BE8E")

        valid = project.checksum_header(str(path))
        assert valid["exit_code"] == 0
        assert valid["valid"] is True
        assert valid["actual"] == valid["expected"]

        content = bytearray(path.read_bytes())
        content[0xBD] ^= 0xFF
        path.write_bytes(content)
        invalid = project.checksum_header(str(path))
        assert invalid["exit_code"] == 2
        assert invalid["valid"] is False
        assert invalid["actual"] != invalid["expected"]

    @pytest.mark.parametrize("bad_path", [None, [], {}])
    def test_validate_rom_non_path_returns_false(self, bad_path):
        from cli_anything.febuildergba.core.project import validate_rom
        assert validate_rom(bad_path) is False

    def test_rom_info_rejects_existing_non_rom_before_backend(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project
        not_rom = tmp_path / "document.bin"
        data = bytearray(0x100000)
        data[0xA0:0xAC] = b"LOCAL SECRET"
        data[0xAC:0xB0] = b"LEAK"
        data[0xB2] = 0x96
        data[0xBD] = 0x00  # Correct value would be 0xE3.
        not_rom.write_bytes(data)
        backend_calls = []
        monkeypatch.setattr(
            project, "run_cli", lambda args: backend_calls.append(args),
        )

        with pytest.raises(ValueError, match="header checksum mismatch"):
            project.rom_info(str(not_rom))

        assert backend_calls == []

    def test_rom_info_accepts_valid_header_without_backend(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project
        rom = bytearray(0x100000)
        rom[0xA0:0xAC] = b"FIRE EMBLEM\x00"
        rom[0xAC:0xB0] = b"BE8E"
        rom[0xB0:0xB2] = b"01"
        rom[0xB2] = 0x96
        rom[0xBC] = 0x01
        rom[0xBD] = 0xF3
        path = tmp_path / "fe8u.gba"
        path.write_bytes(rom)

        def unavailable_backend(args):
            raise RuntimeError("backend unavailable")

        monkeypatch.setattr(project, "run_cli", unavailable_backend)
        result = project.rom_info(str(path))

        assert result["detected_version"] == "FE8U"
        assert result["rom_size"] == len(rom)
        assert result["lint_exit_code"] == -1

    def test_detect_version_fe8u(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        path = tmp_path / "fe8u.gba"
        _write_valid_test_rom(path, b"BE8E")
        assert _detect_version(str(path)) == "FE8U"

    def test_detect_version_fe7u(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        path = tmp_path / "fe7u.gba"
        _write_valid_test_rom(path, b"AE7E")
        assert _detect_version(str(path)) == "FE7U"

    def test_detect_version_fe6(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        path = tmp_path / "fe6.gba"
        _write_valid_test_rom(path, b"AFEJ")
        assert _detect_version(str(path)) == "FE6"

    def test_detect_version_forced(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        assert _detect_version("any_path", "FE8U") == "FE8U"

    @pytest.mark.parametrize("bad_path", [None, [], {}])
    def test_detect_version_non_path_returns_unknown(self, bad_path):
        from cli_anything.febuildergba.core.project import _detect_version
        assert _detect_version(bad_path) == "unknown"

    def test_detect_version_unknown(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        path = tmp_path / "unknown.gba"
        _write_valid_test_rom(path, b"XXXX")
        result = _detect_version(str(path))
        assert "unknown" in result

    def test_detect_version_invalid_file_does_not_decode_game_code(self, tmp_path):
        from cli_anything.febuildergba.core.project import _detect_version
        not_rom = tmp_path / "document.bin"
        data = bytearray(0x100000)
        data[0xAC:0xB0] = b"LEAK"
        data[0xB2] = 0x96
        data[0xBD] = 0x00
        not_rom.write_bytes(data)

        result = _detect_version(str(not_rom))
        assert result == "unknown"
        assert "LEAK" not in result


# ── Data tests ────────────────────────────────────────────────────────

class TestData:
    """Tests for core/data.py."""

    def test_read_tsv(self, tmp_path):
        from cli_anything.febuildergba.core.data import read_tsv
        tsv = tmp_path / "test.tsv"
        tsv.write_text("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n")
        rows = read_tsv(str(tsv))
        assert len(rows) == 2
        assert rows[0]["Name"] == "Eirika"
        assert rows[1]["HP"] == "30"

    def test_tsv_summary(self, tmp_path):
        from cli_anything.febuildergba.core.data import tsv_summary
        tsv = tmp_path / "test.tsv"
        tsv.write_text("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n2\tFranz\t20\n")
        summary = tsv_summary(str(tsv))
        assert summary["row_count"] == 3
        assert "ID" in summary["columns"]
        assert "Name" in summary["columns"]
        assert len(summary["preview"]) == 3

    def test_unknown_table_raises(self):
        from cli_anything.febuildergba.core.data import export_table
        with pytest.raises(ValueError, match="Unknown table"):
            export_table("fake.gba", "nonexistent_table", "out.tsv")

    def test_failed_export_does_not_report_stale_output(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import data
        output = tmp_path / "units.tsv"
        output.write_text("stale")
        monkeypatch.setattr(
            data,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                args, 1, stdout="", stderr="backend failed",
            ),
        )

        result = data.export_table("fake.gba", "units", str(output))

        assert result["exit_code"] == 1
        assert result["output_files"] == []

    def test_all_export_reports_only_declared_table_outputs(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import data
        output = tmp_path / "tables"
        expected = tmp_path / "tables.units.tsv"
        stale = tmp_path / "tables.stale.tsv"
        expected.write_text("new")
        stale.write_text("old")
        monkeypatch.setattr(data, "list_tables", lambda: ["units", "items"])
        monkeypatch.setattr(
            data,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                args, 0, stdout="exported", stderr="",
            ),
        )

        result = data.export_table("fake.gba", "all", str(output))

        assert result["output_files"] == [str(expected.resolve())]
        assert str(stale.resolve()) not in result["output_files"]

    def test_export_rejects_empty_output_path(self):
        from cli_anything.febuildergba.core.data import export_table
        with pytest.raises(ValueError, match="must not be empty"):
            export_table("fake.gba", "units", "")

    def test_empty_tsv(self, tmp_path):
        from cli_anything.febuildergba.core.data import read_tsv
        tsv = tmp_path / "empty.tsv"
        tsv.write_text("ID\tName\n")
        rows = read_tsv(str(tsv))
        assert len(rows) == 0


# ── SessionState serialization tests ──────────────────────────────────

class TestSessionState:
    """Tests for SessionState dataclass."""

    def test_to_dict(self):
        from cli_anything.febuildergba.core.session import SessionState
        state = SessionState(rom_path="/test.gba", rom_version="FE8U")
        d = state.to_dict()
        assert d["rom_path"] == "/test.gba"
        assert d["rom_version"] == "FE8U"

    def test_from_dict(self):
        from cli_anything.febuildergba.core.session import SessionState
        d = {"rom_path": "/test.gba", "rom_version": "FE8U", "rom_size": 1024,
             "force_version": "", "created_at": 0.0, "updated_at": 0.0,
             "history": [], "modified": False}
        state = SessionState.from_dict(d)
        assert state.rom_path == "/test.gba"
        assert state.rom_version == "FE8U"

    def test_from_dict_extra_keys(self):
        from cli_anything.febuildergba.core.session import SessionState
        d = {"rom_path": "/test.gba", "rom_version": "FE8U", "rom_size": 0,
             "force_version": "", "created_at": 0.0, "updated_at": 0.0,
             "history": [], "modified": False, "extra_key": "ignored"}
        state = SessionState.from_dict(d)
        assert state.rom_path == "/test.gba"

    def test_from_dict_non_object_returns_default(self):
        from cli_anything.febuildergba.core.session import SessionState
        state = SessionState.from_dict(["not", "an", "object"])
        assert state == SessionState()

    def test_from_dict_sanitizes_invalid_persisted_fields(self):
        from cli_anything.febuildergba.core.session import (
            MAX_SESSION_PATH_LEN,
            SessionState,
        )
        state = SessionState.from_dict({
            "rom_path": "x" * (MAX_SESSION_PATH_LEN + 1),
            "rom_version": ["FE8U"],
            "rom_size": 1 << 100,
            "force_version": {"version": "FE8U"},
            "created_at": float("nan"),
            "updated_at": -1,
            "history": ["bad", {"op": "kept"}],
            "modified": "yes",
        })

        assert state.rom_path == ""
        assert state.rom_version == ""
        assert state.rom_size == 0
        assert state.force_version == ""
        assert state.created_at == 0.0
        assert state.updated_at == 0.0
        assert state.history == [{"op": "kept"}]
        assert state.modified is False

    def test_from_dict_sanitizes_invalid_session_id(self):
        from cli_anything.febuildergba.core.session import (
            MAX_SESSION_ID_LEN,
            SessionState,
        )

        wrong_type = SessionState.from_dict({"session_id": []})
        overlong = SessionState.from_dict({
            "session_id": "x" * (MAX_SESSION_ID_LEN + 1),
        })
        legacy = SessionState.from_dict({"rom_path": "/test.gba"})

        assert wrong_type.session_id == ""
        assert overlong.session_id == ""
        assert legacy.session_id == ""
        assert legacy.rom_path == "/test.gba"


# ── ROM header tests ──────────────────────────────────────────────────

class TestRomHeader:
    """Tests for core/project.py rom_header."""

    def test_header_fe8u(self, tmp_path):
        from cli_anything.febuildergba.core.project import rom_header, validate_rom
        rom = bytearray(0x100000)  # 1 MB min
        rom[0xA0:0xAC] = b"FIRE EMBLEM\x00"
        rom[0xAC:0xB0] = b"BE8E"
        rom[0xB0:0xB2] = b"01"
        rom[0xB2] = 0x96
        rom[0xB3] = 0x00
        rom[0xB4] = 0x00
        rom[0xBC] = 0x01
        rom[0xBD] = 0xF3
        path = tmp_path / "fe8u.gba"
        path.write_bytes(bytes(rom))
        assert validate_rom(str(path)) is True
        result = rom_header(str(path))
        assert result["game_code"] == "BE8E"
        assert result["title"].startswith("FIRE EMBLEM")
        assert result["maker_code"] == "01"
        assert result["software_version"] == 1
        assert result["header_checksum"] == 0xF3

    def test_header_missing_file(self):
        from cli_anything.febuildergba.core.project import rom_header
        with pytest.raises(FileNotFoundError):
            rom_header("/nonexistent.gba")

    def test_header_rejects_existing_non_rom(self, tmp_path):
        from cli_anything.febuildergba.core.project import rom_header
        not_rom = tmp_path / "document.bin"
        data = bytearray(0x100000)
        data[0xA0:0xAC] = b"LOCAL SECRET"
        data[0xAC:0xB0] = b"LEAK"
        data[0xB2] = 0x96
        data[0xBD] = 0x00  # Correct value would be 0xE3.
        not_rom.write_bytes(data)

        with pytest.raises(ValueError, match="header checksum mismatch"):
            rom_header(str(not_rom))


# ── ROM save tests ────────────────────────────────────────────────────

class TestRomSave:
    """Tests for core/project.py save_rom."""

    def test_save_rom(self, tmp_path):
        from cli_anything.febuildergba.core.project import save_rom
        src = tmp_path / "input.gba"
        src.write_bytes(b"\x00" * 1024)
        dst = str(tmp_path / "output.gba")
        result = save_rom(str(src), dst)
        assert result["output_path"] == dst
        assert result["file_size"] == 1024
        assert os.path.isfile(dst)

    def test_save_rom_missing(self):
        from cli_anything.febuildergba.core.project import save_rom
        with pytest.raises(FileNotFoundError):
            save_rom("/nonexistent.gba", "/out.gba")


# ── Data diff tests ───────────────────────────────────────────────────

class TestDataDiff:
    """Tests for core/data.py diff_tsv."""

    def test_identical_files(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        tsv = tmp_path / "a.tsv"
        tsv.write_text("ID\tName\n0\tEirika\n1\tSeth\n")
        result = diff_tsv(str(tsv), str(tsv))
        assert result["unchanged_count"] == 2
        assert len(result["added_rows"]) == 0
        assert len(result["removed_rows"]) == 0
        assert len(result["changed_rows"]) == 0

    def test_added_row(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        a = tmp_path / "a.tsv"
        b = tmp_path / "b.tsv"
        a.write_text("ID\tName\n0\tEirika\n")
        b.write_text("ID\tName\n0\tEirika\n1\tSeth\n")
        result = diff_tsv(str(a), str(b))
        assert len(result["added_rows"]) == 1
        assert result["added_rows"][0]["ID"] == "1"

    def test_removed_row(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        a = tmp_path / "a.tsv"
        b = tmp_path / "b.tsv"
        a.write_text("ID\tName\n0\tEirika\n1\tSeth\n")
        b.write_text("ID\tName\n0\tEirika\n")
        result = diff_tsv(str(a), str(b))
        assert len(result["removed_rows"]) == 1

    def test_changed_row(self, tmp_path):
        from cli_anything.febuildergba.core.data import diff_tsv
        a = tmp_path / "a.tsv"
        b = tmp_path / "b.tsv"
        a.write_text("ID\tName\tHP\n0\tEirika\t16\n")
        b.write_text("ID\tName\tHP\n0\tEirika\t20\n")
        result = diff_tsv(str(a), str(b))
        assert len(result["changed_rows"]) == 1


# ── Data lookup tests ─────────────────────────────────────────────────

class TestDataLookup:
    """Tests for core/data.py lookup_entry."""

    def test_lookup_found(self, tmp_path):
        from cli_anything.febuildergba.core.data import lookup_entry
        tsv = tmp_path / "units.tsv"
        tsv.write_text("ID\tName\tHP\n0\tEirika\t16\n1\tSeth\t30\n")
        result = lookup_entry(str(tsv), "1")
        assert result["found"]
        assert result["row"]["Name"] == "Seth"

    def test_lookup_not_found(self, tmp_path):
        from cli_anything.febuildergba.core.data import lookup_entry
        tsv = tmp_path / "units.tsv"
        tsv.write_text("ID\tName\n0\tEirika\n")
        result = lookup_entry(str(tsv), "99")
        assert not result["found"]

    def test_lookup_missing_file(self):
        from cli_anything.febuildergba.core.data import lookup_entry
        with pytest.raises(FileNotFoundError):
            lookup_entry("/nonexistent.tsv", "0")


# ── Patch list tests ──────────────────────────────────────────────────

class TestPatchList:
    """Tests for core/patches.py list_patches."""

    def test_list_patches(self, tmp_path):
        from cli_anything.febuildergba.core.patches import list_patches
        ver_dir = tmp_path / "config" / "patch2" / "FE8U"
        ver_dir.mkdir(parents=True)
        (ver_dir / "PATCH_Test.txt").write_text(
            "NAME=Test Patch\nINFO=A test patch\n"
        )
        result = list_patches(str(tmp_path / "config"), "FE8U")
        assert result["count"] >= 1
        assert any(p["name"] == "Test Patch" for p in result["patches"])
        assert any(p["info"] == "A test patch" for p in result["patches"])

    def test_list_patches_empty(self, tmp_path):
        from cli_anything.febuildergba.core.patches import list_patches
        ver_dir = tmp_path / "config" / "patch2" / "FE8U"
        ver_dir.mkdir(parents=True)
        result = list_patches(str(tmp_path / "config"), "FE8U")
        assert result["count"] == 0

    def test_list_patches_missing_dir(self, tmp_path):
        from cli_anything.febuildergba.core.patches import list_patches
        result = list_patches(str(tmp_path / "nonexistent"), "FE8U")
        assert result["count"] == 0
