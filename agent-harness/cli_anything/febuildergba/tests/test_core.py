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


def _touch(path) -> str:
    """Create an empty file at *path* (a ``Path``) and return it as ``str``."""
    Path(path).write_bytes(b"")
    return str(path)


# Every MCP ROM-touching wrapper (issue #1942 / PR #1971): module import
# path, function name (for readable parametrize ids), whether it mutates the
# ROM, and a small adapter invoking it with the minimum valid arguments given
# ``(module, rom_path_str, tmp_path)``. Shared by the class-wide trust
# boundary tests below so every wrapper is provably covered from one table.
_ROM_SNAPSHOT_WRAPPERS = [
    ("cli_anything.febuildergba.core.data", "export_table", False,
     lambda module, rom, tmp_path: module.export_table(
         rom, "units", str(tmp_path / "out.tsv"))),
    ("cli_anything.febuildergba.core.data", "import_table", True,
     lambda module, rom, tmp_path: module.import_table(
         rom, "units", _touch(tmp_path / "in.tsv"))),
    ("cli_anything.febuildergba.core.data", "roundtrip_table", False,
     lambda module, rom, tmp_path: module.roundtrip_table(rom, "units")),
    ("cli_anything.febuildergba.core.export", "resolve_names", False,
     lambda module, rom, tmp_path: module.resolve_names(rom, "unit", [1, 2])),
    ("cli_anything.febuildergba.core.text", "search_text", False,
     lambda module, rom, tmp_path: module.search_text(rom, "Eirika")),
    ("cli_anything.febuildergba.core.text", "roundtrip_text", False,
     lambda module, rom, tmp_path: module.roundtrip_text(rom)),
    ("cli_anything.febuildergba.core.verbs", "export_palette", False,
     lambda module, rom, tmp_path: module.export_palette(
         rom, "0x5524", str(tmp_path / "p.pal"))),
    ("cli_anything.febuildergba.core.verbs", "import_palette", True,
     lambda module, rom, tmp_path: module.import_palette(
         rom, "0x5524", _touch(tmp_path / "p.pal"))),
    ("cli_anything.febuildergba.core.lint", "lint_rom", False,
     lambda module, rom, tmp_path: module.lint_rom(rom)),
]
_ROM_SNAPSHOT_WRAPPER_IDS = [w[1] for w in _ROM_SNAPSHOT_WRAPPERS]
_READ_ONLY_ROM_WRAPPERS = [w for w in _ROM_SNAPSHOT_WRAPPERS if not w[2]]
_READ_ONLY_ROM_WRAPPER_IDS = [w[1] for w in _READ_ONLY_ROM_WRAPPERS]
_MUTATING_ROM_WRAPPERS = [w for w in _ROM_SNAPSHOT_WRAPPERS if w[2]]
_MUTATING_ROM_WRAPPER_IDS = [w[1] for w in _MUTATING_ROM_WRAPPERS]

# ``lint_rom`` predates issue #1942 / PR #1971 and always snapshotted, in
# Click and MCP alike, so it is excluded here: this table is only the 8
# wrappers whose *historical* (pre-fix) behavior was a raw, unvalidated,
# direct ``--rom=<path>`` passthrough — the behavior that must remain
# byte-for-byte outside MCP's ``prebuilt_backend_only`` scope.
_LEGACY_PASSTHROUGH_WRAPPERS = [
    w for w in _ROM_SNAPSHOT_WRAPPERS if w[1] != "lint_rom"]
_LEGACY_PASSTHROUGH_WRAPPER_IDS = [w[1] for w in _LEGACY_PASSTHROUGH_WRAPPERS]


def _configure_temporary_backend_root(tmp_path, monkeypatch, backend):
    """Point backend discovery at an isolated tree rooted at ``tmp_path``."""
    fake_module = tmp_path / "pkg" / "utils" / "febuildergba_backend.py"
    fake_module.parent.mkdir(parents=True)
    fake_module.write_text("# discovery test placeholder\n")
    monkeypatch.setattr(backend, "__file__", str(fake_module))
    monkeypatch.delenv("FEBUILDERGBA_CLI_EXE", raising=False)
    monkeypatch.delenv("FEBUILDERGBA_CLI", raising=False)
    return tmp_path


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

    def test_bounded_capture_rejects_capture_false_before_resolution(
            self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        resolver_calls = []

        def resolver():
            resolver_calls.append(True)
            raise AssertionError("resolver must not be called")

        def subprocess_must_not_run(*args, **kwargs):
            raise AssertionError("subprocess must not be called")

        monkeypatch.setattr(backend, "find_febuildergba_cli", resolver)
        monkeypatch.setattr(backend.subprocess, "run", subprocess_must_not_run)
        monkeypatch.setattr(backend.subprocess, "Popen", subprocess_must_not_run)

        with backend.bounded_capture(16):
            with pytest.raises(RuntimeError, match="capture=False is not allowed"):
                backend.run_cli(["--version"], capture=False)

        assert resolver_calls == []

    def test_legacy_resolver_keeps_dotnet_run_fallback(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        root = _configure_temporary_backend_root(tmp_path, monkeypatch, backend)
        csproj = root / "FEBuilderGBA.CLI" / "FEBuilderGBA.CLI.csproj"
        csproj.parent.mkdir()
        csproj.write_text("<Project />\n")
        monkeypatch.setattr(backend.shutil, "which", lambda name: "dotnet-host")

        assert backend.find_febuildergba_cli() == [
            "dotnet-host", "run", "--project", str(csproj), "--",
        ]

    def test_prebuilt_only_rejects_dotnet_run_fallback(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        root = _configure_temporary_backend_root(tmp_path, monkeypatch, backend)
        csproj = root / "FEBuilderGBA.CLI" / "FEBuilderGBA.CLI.csproj"
        csproj.parent.mkdir()
        csproj.write_text("<Project />\n")
        monkeypatch.setattr(backend.shutil, "which", lambda name: "dotnet-host")

        with backend.prebuilt_backend_only():
            with pytest.raises(RuntimeError, match="prebuilt.*dotnet run fallback is disabled"):
                backend.find_febuildergba_cli()

    def test_resolver_accepts_prebuilt_dll(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        root = _configure_temporary_backend_root(tmp_path, monkeypatch, backend)
        dll = (
            root / "FEBuilderGBA.CLI" / "bin" / "Release" / "net9.0"
            / "FEBuilderGBA.CLI.dll"
        )
        dll.parent.mkdir(parents=True)
        dll.write_bytes(b"prebuilt dll placeholder")
        monkeypatch.setattr(backend.shutil, "which", lambda name: "dotnet-host")

        with backend.prebuilt_backend_only():
            assert backend.find_febuildergba_cli() == ["dotnet-host", str(dll)]

    def test_prebuilt_backend_only_context_is_nested_and_resets(self):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        assert backend._prebuilt_backend_only.get() is False
        with backend.prebuilt_backend_only():
            assert backend._prebuilt_backend_only.get() is True
            with backend.prebuilt_backend_only():
                assert backend._prebuilt_backend_only.get() is True
            assert backend._prebuilt_backend_only.get() is True
        assert backend._prebuilt_backend_only.get() is False

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
            "import os\n"
            "import sys\n"
            "from cli_anything.febuildergba.utils import febuildergba_backend as backend\n"
            f"backend.find_febuildergba_cli = lambda: [sys.executable, '-c', {backend_script!r}]\n"
            "def read_frame():\n"
            "    chunks = []\n"
            "    while True:\n"
            "        byte = os.read(0, 1)\n"
            "        if not byte:\n"
            "            return b''.join(chunks).decode('utf-8')\n"
            "        chunks.append(byte)\n"
            "        if byte == b'\\n':\n"
            "            return b''.join(chunks).decode('utf-8')\n"
            "current_frame = read_frame()\n"
            "with backend.bounded_capture(64):\n"
            "    result = backend.run_cli([])\n"
            "print(json.dumps({\n"
            "    'backend_output': str(result.stdout),\n"
            "    'current_frame': current_frame,\n"
            "    'next_frame': read_frame(),\n"
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

        # os.read(0, 1) leaves the next frame in the kernel pipe. Before
        # stdin=DEVNULL, the fake backend consumed it and the outer parent
        # observed EOF. This proves the real pipe boundary, not a mock.
        assert observed["backend_output"] == "child-read="
        assert json.loads(observed["current_frame"]) == json.loads(current_frame)
        assert json.loads(observed["next_frame"]) == json.loads(next_frame)

    def test_check_backend_normalizes_os_probe_failure(self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["blocked-cli"])

        def fail_version():
            raise PermissionError("execution denied")

        monkeypatch.setattr(backend, "get_version", fail_version)
        result = backend.check_backend()

        assert result == {"available": False, "error": "execution denied"}


class TestRomSnapshotGate:
    """run_cli's MCP-only trust-boundary seam guard (issue #1942 / PR #1971).

    The guard runs before backend resolution/subprocess and is a no-op
    outside MCP's ``prebuilt_backend_only`` dynamic scope, so Click's
    historic direct-path behavior is unaffected.
    """

    def test_rejects_unregistered_rom_path_before_resolver_and_subprocess(
            self, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        def resolver_must_not_run():
            raise AssertionError("resolver must not be called")

        def subprocess_must_not_run(*args, **kwargs):
            raise AssertionError("subprocess must not be called")

        monkeypatch.setattr(backend, "find_febuildergba_cli", resolver_must_not_run)
        monkeypatch.setattr(backend.subprocess, "run", subprocess_must_not_run)
        monkeypatch.setattr(backend.subprocess, "Popen", subprocess_must_not_run)

        with backend.prebuilt_backend_only():
            with pytest.raises(RuntimeError, match="registered private ROM snapshot"):
                backend.run_cli(["--lint", "--rom=/some/unregistered/rom.gba"])

    def test_allows_registered_rom_path_under_mcp_scope(self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom = tmp_path / "snap.gba"
        rom.write_bytes(b"x")
        expected = subprocess.CompletedProcess(["cli"], 0, stdout="ok", stderr="")
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["cli"])
        monkeypatch.setattr(backend.subprocess, "run", lambda *a, **k: expected)

        with backend.prebuilt_backend_only():
            with backend.register_rom_snapshot(str(rom)):
                assert backend.run_cli(["--lint", f"--rom={rom}"]) is expected

    def test_allows_registered_rom_path_two_token_form_under_mcp_scope(
            self, tmp_path, monkeypatch):
        """The two-token ``["--rom", "<path>"]`` form (issue #1942 / PR
        #1971 follow-up) is validated identically to ``--rom=<path>``: a
        registered snapshot path is accepted."""
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom = tmp_path / "snap.gba"
        rom.write_bytes(b"x")
        expected = subprocess.CompletedProcess(["cli"], 0, stdout="ok", stderr="")
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["cli"])
        monkeypatch.setattr(backend.subprocess, "run", lambda *a, **k: expected)

        with backend.prebuilt_backend_only():
            with backend.register_rom_snapshot(str(rom)):
                assert backend.run_cli(["--lint", "--rom", str(rom)]) is expected

    def test_rejects_unregistered_rom_path_two_token_form_before_resolver_and_subprocess(
            self, monkeypatch):
        """Two-token counterpart of
        ``test_rejects_unregistered_rom_path_before_resolver_and_subprocess``:
        an unregistered path must fail before the resolver or subprocess
        runs regardless of which ``--rom`` form carries it."""
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        def resolver_must_not_run():
            raise AssertionError("resolver must not be called")

        def subprocess_must_not_run(*args, **kwargs):
            raise AssertionError("subprocess must not be called")

        monkeypatch.setattr(backend, "find_febuildergba_cli", resolver_must_not_run)
        monkeypatch.setattr(backend.subprocess, "run", subprocess_must_not_run)
        monkeypatch.setattr(backend.subprocess, "Popen", subprocess_must_not_run)

        with backend.prebuilt_backend_only():
            with pytest.raises(RuntimeError, match="registered private ROM snapshot"):
                backend.run_cli(["--lint", "--rom", "/some/unregistered/rom.gba"])

    @pytest.mark.parametrize(
        "trailing_args", [[], [""]], ids=["no-value", "empty-value"])
    def test_rejects_missing_or_empty_two_token_value_before_resolver_and_subprocess(
            self, monkeypatch, trailing_args):
        """A two-token ``--rom`` with no following element, or an empty
        following element, must be rejected as a missing value before the
        resolver or subprocess runs — never falling through to the
        registered-path check with an accidental empty/cwd-relative path."""
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        def resolver_must_not_run():
            raise AssertionError("resolver must not be called")

        def subprocess_must_not_run(*args, **kwargs):
            raise AssertionError("subprocess must not be called")

        monkeypatch.setattr(backend, "find_febuildergba_cli", resolver_must_not_run)
        monkeypatch.setattr(backend.subprocess, "run", subprocess_must_not_run)
        monkeypatch.setattr(backend.subprocess, "Popen", subprocess_must_not_run)

        with backend.prebuilt_backend_only():
            with pytest.raises(RuntimeError, match="requires a non-empty value"):
                backend.run_cli(["--lint", "--rom", *trailing_args])

    def test_gate_is_inert_outside_mcp_scope(self, monkeypatch):
        """Legacy Click behavior: an unregistered --rom path is never
        rejected by the gate outside MCP's prebuilt-backend dynamic scope."""
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        expected = subprocess.CompletedProcess(["cli"], 0, stdout="ok", stderr="")
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["cli"])
        monkeypatch.setattr(backend.subprocess, "run", lambda *a, **k: expected)

        assert backend._prebuilt_backend_only.get() is False
        assert backend.run_cli(["--lint", "--rom=/anything/at/all.gba"]) is expected

    def test_is_prebuilt_backend_only_accessor_mirrors_the_contextvar(self):
        """``is_prebuilt_backend_only()`` is the public read accessor
        ``core.project``'s conditional wrappers (``backend_rom_snapshot`` /
        ``backend_mutating_rom_snapshot``) use to pick MCP-scoped private
        snapshot vs. legacy direct-path behavior — it must always mirror
        ``_prebuilt_backend_only`` exactly."""
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        assert backend.is_prebuilt_backend_only() is False
        with backend.prebuilt_backend_only():
            assert backend.is_prebuilt_backend_only() is True
        assert backend.is_prebuilt_backend_only() is False

    def test_registration_is_scoped_and_resets(self):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        assert backend._registered_rom_snapshots.get() == frozenset()
        with backend.register_rom_snapshot("/a/b.gba"):
            assert (
                os.path.abspath("/a/b.gba") in backend._registered_rom_snapshots.get()
            )
        assert backend._registered_rom_snapshots.get() == frozenset()

    def test_gate_only_inspects_rom_flagged_args(self, monkeypatch):
        """Only ``--rom=`` is gated. Click-only verbs whose args never appear
        in an MCP tool's argv (e.g. ``--rom2=`` for ``rom diff``) are out of
        scope for this seam by design (issue #1942 / PR #1971 review)."""
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        expected = subprocess.CompletedProcess(["cli"], 0, stdout="ok", stderr="")
        monkeypatch.setattr(backend, "find_febuildergba_cli", lambda: ["cli"])
        monkeypatch.setattr(backend.subprocess, "run", lambda *a, **k: expected)

        with backend.prebuilt_backend_only():
            assert backend.run_cli(
                ["--diff", "--rom2=/unregistered/other.gba"],
            ) is expected

    def test_metadata_only_mcp_tools_never_reach_the_gate(self, tmp_path):
        """``rom_checksum``/``rom_info``/``rom_validate`` (issue #1942 / PR
        #1971 invariant #2: "metadata/checksum remain local") are backed by
        ``core.project`` functions that read the header locally and never
        call ``run_cli`` at all — so they must keep working under MCP's
        ``prebuilt_backend_only`` scope with *no* snapshot registered,
        exactly as they did before this fix. This is what stands between
        "every --rom path must be a registered snapshot" (invariant #1) and
        these three real MCP tools, which intentionally carry no ``--rom=``
        backend argument whatsoever.
        """
        from cli_anything.febuildergba.core.project import (
            checksum_header, rom_info, validate_rom,
        )
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with backend.prebuilt_backend_only():
            assert backend._registered_rom_snapshots.get() == frozenset()
            assert validate_rom(str(rom_path)) is True
            assert rom_info(str(rom_path))["rom_size"] == rom_path.stat().st_size
            assert checksum_header(str(rom_path))["exit_code"] in (0, 2)


class TestSanitizeSnapshotPath:
    """utils.febuildergba_backend.sanitize_snapshot_path (issue #1942 / PR
    #1971) — internal snapshot paths must never leak through backend output,
    while ``_BoundedOutput`` metadata (original_length/truncated) survives.
    """

    def test_noop_when_snapshot_path_absent(self):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        text = "Lint: No errors found."
        assert backend.sanitize_snapshot_path(
            text, "/tmp/snap123.gba", "/rom/real.gba") is text

    def test_replaces_every_occurrence(self):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        text = "Processing /tmp/snap123.gba... done with /tmp/snap123.gba"
        result = backend.sanitize_snapshot_path(
            text, "/tmp/snap123.gba", "/rom/real.gba")
        assert result == "Processing /rom/real.gba... done with /rom/real.gba"
        assert "/tmp/snap123.gba" not in result

    def test_noop_when_snapshot_equals_real_path(self):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        text = "Processing /rom/real.gba"
        assert backend.sanitize_snapshot_path(
            text, "/rom/real.gba", "/rom/real.gba") is text

    def test_preserves_bounded_output_metadata(self):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        source = backend._BoundedOutput(
            "leak: /tmp/snap123.gba", original_length=9000, truncated=True)
        result = backend.sanitize_snapshot_path(
            source, "/tmp/snap123.gba", "/rom/real.gba")
        assert isinstance(result, backend._BoundedOutput)
        assert result == "leak: /rom/real.gba"
        assert result.original_length == 9000
        assert result.truncated is True

    @pytest.mark.parametrize("bad_text", [None, 123, [], b"bytes-not-str"])
    def test_noop_for_non_string_input(self, bad_text):
        from cli_anything.febuildergba.utils import febuildergba_backend as backend
        assert backend.sanitize_snapshot_path(
            bad_text, "/tmp/snap.gba", "/rom/real.gba") is bad_text


class TestLint:
    def test_clean_summary_is_not_an_error(self, monkeypatch, tmp_path):
        from cli_anything.febuildergba.core import lint
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        result = subprocess.CompletedProcess(
            ["cli", "--lint"], 0, stdout="Lint: No errors found.\n", stderr="",
        )
        monkeypatch.setattr(lint, "run_cli", lambda args: result)

        parsed = lint.lint_rom(str(rom_path))

        assert parsed["clean"] is True
        assert parsed["error_count"] == 0
        assert parsed["warning_count"] == 0
        assert parsed["errors"] == []
        assert parsed["warnings"] == []
        assert parsed["info"] == ["Lint: No errors found."]

    def test_only_severity_markers_create_findings(self, monkeypatch, tmp_path):
        from cli_anything.febuildergba.core import lint
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
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

        parsed = lint.lint_rom(str(rom_path))

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
             lambda module, out, rom: module.create_ups("r.gba", out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out, rom: module.disassemble("r.gba", out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out, rom: module.decrease_color("in.png", out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out, rom: module.render_portrait("r.gba", 1, out)),
            ("cli_anything.febuildergba.core.export",
             lambda module, out, rom: module.export_midi("r.gba", "1A", out)),
            ("cli_anything.febuildergba.core.text",
             lambda module, out, rom: module.export_text("r.gba", out)),
            ("cli_anything.febuildergba.core.verbs",
             lambda module, out, rom: module.export_map_settings_raw("r.gba", out)),
            ("cli_anything.febuildergba.core.verbs",
             lambda module, out, rom: module.export_palette(rom, "0x5524", out)),
            ("cli_anything.febuildergba.core.verbs",
             lambda module, out, rom: module.lz77_file("compress", "in.bin", out)),
        ],
    )
    def test_failed_wrappers_do_not_report_stale_output(
            self, module_name, invoke, monkeypatch, tmp_path):
        import importlib

        module = importlib.import_module(module_name)
        output = tmp_path / "stale.bin"
        output.write_bytes(b"stale")
        rom_path = tmp_path / "valid.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        monkeypatch.setattr(
            module,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                args, 1, stdout="", stderr="backend failed",
            ),
        )

        result = invoke(module, str(output), str(rom_path))

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

    def test_open_rom_replace_failure_rolls_back_memory_and_disk(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session

        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/old.gba", "FE8U", 1)
        before_state = sess.state.to_dict()
        before_disk = path.read_bytes()

        def fail_replace(source, destination):
            raise PermissionError("replace denied")

        monkeypatch.setattr(session_module.os, "replace", fail_replace)

        with pytest.raises(PermissionError, match="replace denied"):
            sess.open_rom("/fake/new.gba", "FE7U", 2)

        assert sess.state.to_dict() == before_state
        assert path.read_bytes() == before_disk
        assert list(tmp_path.glob(".test_session.json.*.tmp")) == []

    def test_record_operation_write_failure_rolls_back_memory_and_disk(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session

        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U", 1)
        before_state = sess.state.to_dict()
        before_disk = path.read_bytes()

        def fail_replace(source, destination):
            raise PermissionError("replace denied")

        monkeypatch.setattr(session_module.os, "replace", fail_replace)

        with pytest.raises(PermissionError, match="replace denied"):
            sess.record_operation("import", {"source": "units.tsv"}, modified=True)

        assert sess.state.to_dict() == before_state
        assert path.read_bytes() == before_disk
        assert list(tmp_path.glob(".test_session.json.*.tmp")) == []

    def test_record_operation_with_effect_rolls_back_effect_on_write_failure(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session

        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U", 1)
        before_state = sess.state.to_dict()
        before_disk = path.read_bytes()
        effects = []

        def fail_replace(source, destination):
            raise PermissionError("replace denied")

        monkeypatch.setattr(session_module.os, "replace", fail_replace)

        with pytest.raises(PermissionError, match="replace denied"):
            sess.record_operation_with_effect(
                "import",
                {"source": "units.tsv"},
                lambda: effects.append("apply"),
                lambda: effects.append("rollback"),
                modified=True,
            )

        assert effects == ["apply", "rollback"]
        assert sess.state.to_dict() == before_state
        assert path.read_bytes() == before_disk
        assert list(tmp_path.glob(".test_session.json.*.tmp")) == []

    def test_record_operation_with_effect_surfaces_double_failure(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session

        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U", 1)
        before_state = sess.state.to_dict()
        before_disk = path.read_bytes()

        def fail_replace(source, destination):
            raise PermissionError("replace denied")

        def fail_rollback():
            raise OSError("rollback denied")

        monkeypatch.setattr(session_module.os, "replace", fail_replace)

        with pytest.raises(
                RuntimeError, match="external state may remain changed") as exc:
            sess.record_operation_with_effect(
                "import",
                {},
                lambda: None,
                fail_rollback,
                modified=True,
            )

        assert isinstance(exc.value.__cause__, OSError)
        assert sess.state.to_dict() == before_state
        assert path.read_bytes() == before_disk
        assert list(tmp_path.glob(".test_session.json.*.tmp")) == []

    def test_mark_modified_write_failure_rolls_back_memory_and_disk(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session

        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U", 1)
        before_state = sess.state.to_dict()
        before_disk = path.read_bytes()

        def fail_replace(source, destination):
            raise PermissionError("replace denied")

        monkeypatch.setattr(session_module.os, "replace", fail_replace)

        with pytest.raises(PermissionError, match="replace denied"):
            sess.mark_modified()

        assert sess.state.to_dict() == before_state
        assert path.read_bytes() == before_disk
        assert list(tmp_path.glob(".test_session.json.*.tmp")) == []

    def test_close_unlink_failure_rolls_back_memory_and_keeps_disk(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import session as session_module
        from cli_anything.febuildergba.core.session import Session

        path = tmp_path / "test_session.json"
        sess = Session(str(path))
        sess.open_rom("/fake/rom.gba", "FE8U", 1)
        before_state = sess.state.to_dict()
        before_disk = path.read_bytes()
        real_unlink = session_module.Path.unlink

        def fail_session_unlink(self, *args, **kwargs):
            if self == path:
                raise PermissionError("unlink denied")
            return real_unlink(self, *args, **kwargs)

        monkeypatch.setattr(session_module.Path, "unlink", fail_session_unlink)

        with pytest.raises(PermissionError, match="unlink denied"):
            sess.close()

        assert sess.state.to_dict() == before_state
        assert sess.is_open()
        assert path.read_bytes() == before_disk

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

    def test_stale_record_operation_with_effect_never_applies_effect(
            self, tmp_path):
        from cli_anything.febuildergba.core.session import Session

        path = str(tmp_path / "test_session.json")
        stale = Session(path)
        stale.open_rom("/fake/a.gba", "FE8U")
        current = Session(path)
        current.open_rom("/fake/b.gba", "FE8U")
        effects = []

        assert stale.record_operation_with_effect(
            "import",
            {},
            lambda: effects.append("apply"),
            lambda: effects.append("rollback"),
            modified=True,
        ) is False

        assert effects == []
        assert stale.state.session_id == current.state.session_id
        assert stale.state.rom_path.endswith("b.gba")

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
            raising=False,
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
            raising=False,
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

        monkeypatch.setattr(
            project, "run_cli", unavailable_backend, raising=False,
        )
        result = project.rom_info(str(path))

        assert result["detected_version"] == "FE8U"
        assert result["rom_size"] == len(rom)
        assert result["lint_output"] == ""
        assert result["lint_exit_code"] == -1

    def test_rom_info_keeps_validated_descriptor_metadata_after_path_swap(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import project

        rom_path = tmp_path / "original.gba"
        replacement = tmp_path / "replacement.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        _write_valid_test_rom(replacement, b"AE7E")
        real_read = project._read_validated_header

        def read_then_swap(path, require_checksum=True):
            result = real_read(path, require_checksum)
            os.replace(replacement, path)
            return result

        def reopened_replacement(args):
            backend_rom = next(arg[6:] for arg in args if arg.startswith("--rom="))
            assert Path(backend_rom).read_bytes()[0xAC:0xB0] == b"AE7E"
            raise AssertionError("rom_info must not reopen the replaced path")

        monkeypatch.setattr(project, "_read_validated_header", read_then_swap)
        monkeypatch.setattr(
            project, "run_cli", reopened_replacement, raising=False,
        )

        result = project.rom_info(str(rom_path))

        assert result["detected_version"] == "FE8U"
        assert result["lint_output"] == ""
        assert result["lint_exit_code"] == -1

    def test_lint_uses_validated_snapshot_and_removes_it(
            self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import lint

        rom_path = tmp_path / "original.gba"
        replacement = tmp_path / "replacement.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        _write_valid_test_rom(replacement, b"AE7E")
        original_bytes = rom_path.read_bytes()
        replacement_bytes = replacement.read_bytes()
        snapshots = []

        def inspect_snapshot(args):
            snapshot = Path(next(arg[6:] for arg in args if arg.startswith("--rom=")))
            snapshots.append(snapshot)
            assert snapshot != rom_path
            assert snapshot.suffix == ".gba"
            os.replace(replacement, rom_path)
            assert snapshot.read_bytes() == original_bytes
            assert snapshot.stat().st_size == len(original_bytes)
            assert rom_path.read_bytes() == replacement_bytes
            return subprocess.CompletedProcess(
                args, 0, stdout="Lint: No errors found.\n", stderr="",
            )

        monkeypatch.setattr(lint, "run_cli", inspect_snapshot)
        result = lint.lint_rom(str(rom_path))

        assert result["rom_path"] == str(rom_path)
        assert result["clean"] is True
        assert snapshots and all(not path.exists() for path in snapshots)

    def test_lint_snapshot_pins_the_validated_header(
            self, tmp_path, monkeypatch):
        from contextlib import contextmanager
        from cli_anything.febuildergba.core import lint, project

        rom_path = tmp_path / "mutable.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        validated_header = rom_path.read_bytes()[:project._GBA_HEADER_SIZE]
        real_open_validated_rom = project._open_validated_rom

        @contextmanager
        def mutate_after_validation(path, require_checksum=True):
            with real_open_validated_rom(path, require_checksum) as opened:
                with Path(path).open("r+b") as mutable:
                    mutable.seek(project._GBA_FIXED_VALUE_OFFSET)
                    mutable.write(b"\x00")
                    mutable.flush()
                yield opened

        def inspect_snapshot(args):
            snapshot = Path(next(arg[6:] for arg in args if arg.startswith("--rom=")))
            assert snapshot.read_bytes()[:project._GBA_HEADER_SIZE] == validated_header
            return subprocess.CompletedProcess(
                args, 0, stdout="Lint: No errors found.\n", stderr="",
            )

        monkeypatch.setattr(project, "_open_validated_rom", mutate_after_validation)
        monkeypatch.setattr(lint, "run_cli", inspect_snapshot)

        result = lint.lint_rom(str(rom_path))

        assert result["clean"] is True
        assert rom_path.read_bytes()[project._GBA_FIXED_VALUE_OFFSET] == 0

    @pytest.mark.parametrize(
        "failure",
        [
            RuntimeError("backend failed"),
            subprocess.TimeoutExpired(["cli"], 1),
            OSError("backend exception"),
        ],
    )
    def test_lint_snapshot_is_removed_when_backend_raises(
            self, tmp_path, monkeypatch, failure):
        from cli_anything.febuildergba.core import lint

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        snapshots = []

        def fail_after_snapshot(args):
            snapshots.append(Path(next(arg[6:] for arg in args if arg.startswith("--rom="))))
            raise failure

        monkeypatch.setattr(lint, "run_cli", fail_after_snapshot)
        with pytest.raises(type(failure)):
            lint.lint_rom(str(rom_path))

        assert snapshots and all(not path.exists() for path in snapshots)

    @pytest.mark.parametrize("kind", ["oversized", "non_rom"])
    def test_lint_rejects_invalid_inputs_before_backend(
            self, tmp_path, monkeypatch, kind):
        from cli_anything.febuildergba.core import lint, project

        rom_path = tmp_path / f"{kind}.gba"
        if kind == "oversized":
            _write_valid_test_rom(rom_path, b"BE8E")
            with rom_path.open("r+b") as stream:
                stream.truncate(project._MAX_ROM_SIZE + 1)
        else:
            rom_path.write_bytes(b"\x00" * project._MIN_ROM_SIZE)
        backend_calls = []
        monkeypatch.setattr(
            lint, "run_cli", lambda args: backend_calls.append(args),
        )

        with pytest.raises(ValueError):
            lint.lint_rom(str(rom_path))

        assert backend_calls == []

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


class TestMutatingRomSnapshot:
    """core.project.mutating_rom_snapshot / MutatingRomSnapshot.commit()
    (issue #1942 / PR #1971) — the shared write-back protocol used by both
    mutating wrappers (data.import_table, verbs.import_palette). Simulates
    "the backend mutated the snapshot" by writing into ``mutator.path``
    directly, since that is exactly what a real backend process does.
    """

    @staticmethod
    def _mutate(path, marker=b"MUTATED!", offset=0x1000):
        with open(path, "r+b") as f:
            f.seek(offset)
            f.write(marker)

    def test_commit_writes_mutation_back_through_original_descriptor(
            self, tmp_path):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            assert mutator.path != str(rom_path)
            assert os.path.isfile(mutator.path)
            self._mutate(mutator.path)
            mutator.commit()
            assert mutator.committed is True
            snapshot_path = mutator.path

        assert rom_path.read_bytes()[0x1000:0x1000 + 8] == b"MUTATED!"
        assert not os.path.isfile(snapshot_path)

    def test_rollback_restores_exact_precommit_bytes(self, tmp_path):
        from cli_anything.febuildergba.core import project

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        original_bytes = rom_path.read_bytes()

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            self._mutate(mutator.path)
            mutator.commit()
            assert rom_path.read_bytes() != original_bytes
            assert mutator.rollback() is True
            assert mutator.committed is False
            assert mutator.rollback() is False

        assert rom_path.read_bytes() == original_bytes

    def test_rollback_refuses_postcommit_content_drift(self, tmp_path):
        from cli_anything.febuildergba.core import project

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            self._mutate(mutator.path)
            mutator.commit()
            mutator._original_stream.seek(0x2000)
            mutator._original_stream.write(b"DRIFT!!!")
            mutator._original_stream.flush()

            with pytest.raises(ValueError, match="committed ROM content changed"):
                mutator.rollback()

            assert mutator.committed is True

    def test_uncommitted_mutation_never_touches_original(self, tmp_path):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        original_bytes = rom_path.read_bytes()

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            self._mutate(mutator.path)
            snapshot_path = mutator.path
            # Simulated backend failure: caller deliberately skips commit().

        assert rom_path.read_bytes() == original_bytes
        assert not os.path.isfile(snapshot_path)

    def test_commit_rejects_replaced_original_path(self, tmp_path):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        replacement = tmp_path / "replacement.bin"
        _write_valid_test_rom(rom_path, b"BE8E")
        replacement.write_bytes(b"NOT-A-ROM" * 20000)
        replacement_bytes = replacement.read_bytes()

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            self._mutate(mutator.path)
            try:
                os.replace(str(replacement), str(rom_path))
            except OSError:
                # Windows blocked the replace outright (open descriptor) —
                # the legitimate mutation just proceeds normally against the
                # (never actually replaced) original.
                mutator.commit()
                assert mutator.committed is True
                assert rom_path.read_bytes()[0x1000:0x1000 + 8] == b"MUTATED!"
                assert replacement.read_bytes() == replacement_bytes
                return
            with pytest.raises(ValueError, match="no longer identifies"):
                mutator.commit()
            assert mutator.committed is False

        # The OS allowed the replace: the attacker's file must be left
        # exactly as written — never overwritten by our write-back.
        assert rom_path.read_bytes() == replacement_bytes

    def test_commit_rejects_same_length_content_drift(self, tmp_path):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            self._mutate(mutator.path)
            # Drift the original content through the very descriptor
            # commit() will re-read from — simulating a second writer that
            # touched the file while "the backend ran", without depending on
            # OS-specific concurrent-handle sharing semantics.
            stream = mutator._original_stream
            stream.seek(0x2000)
            stream.write(b"DRIFT!!!")
            stream.flush()

            with pytest.raises(ValueError, match="content changed"):
                mutator.commit()
            assert mutator.committed is False

    def test_commit_rejects_oversized_mutated_result(self, tmp_path):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            with open(mutator.path, "r+b") as f:
                f.seek(project._MAX_ROM_SIZE)
                f.write(b"\x00")
            with pytest.raises(ValueError, match="larger than 32 MiB"):
                mutator.commit()
            assert mutator.committed is False

    def test_commit_rejects_invalid_header_mutated_result(self, tmp_path):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            with open(mutator.path, "r+b") as f:
                f.seek(project._GBA_FIXED_VALUE_OFFSET)
                f.write(b"\x00")
            with pytest.raises(ValueError, match="missing fixed header byte"):
                mutator.commit()
            assert mutator.committed is False

    @pytest.mark.parametrize(
        "failure",
        [RuntimeError("simulated backend crash"),
         ValueError("simulated bad result"),
         OSError("simulated backend exception")],
    )
    def test_snapshot_removed_when_body_raises_without_committing(
            self, tmp_path, failure):
        from cli_anything.febuildergba.core import project
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        original_bytes = rom_path.read_bytes()
        holder = {}

        with pytest.raises(type(failure)):
            with project.mutating_rom_snapshot(str(rom_path)) as mutator:
                holder["path"] = mutator.path
                raise failure

        assert not os.path.isfile(holder["path"])
        assert rom_path.read_bytes() == original_bytes

    def test_snapshot_registered_during_body_and_deregistered_after(
            self, tmp_path):
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        assert backend._registered_rom_snapshots.get() == frozenset()
        with project.mutating_rom_snapshot(str(rom_path)) as mutator:
            registered = backend._registered_rom_snapshots.get()
            assert os.path.abspath(mutator.path) in registered
            assert len(registered) == 1
        assert backend._registered_rom_snapshots.get() == frozenset()


class TestBackendRomSnapshotDispatch:
    """core.project.backend_rom_snapshot / backend_mutating_rom_snapshot
    (issue #1942 / PR #1971 follow-up fix): the shared MCP-conditional
    dispatch every non-lint ROM-touching wrapper calls into instead of the
    always-on primitives directly. Wrapper-level end-to-end coverage lives
    in ``TestRomSnapshotAcrossAllWrappers`` /
    ``TestLegacyClickPassthroughOutsideMcp`` /
    ``TestMutatingWrappersCommitProtocol`` below; this pins the two
    dispatch functions' own contract directly, in isolation from any single
    wrapper's business logic.
    """

    def test_read_only_yields_the_original_path_unchanged_outside_mcp(
            self, tmp_path):
        from cli_anything.febuildergba.core import project

        # Not even a valid ROM — outside MCP scope this must never be
        # opened, copied, or validated at all.
        missing_path = str(tmp_path / "does-not-exist.gba")
        with project.backend_rom_snapshot(missing_path) as yielded:
            assert yielded == missing_path

    def test_read_only_delegates_to_validated_snapshot_inside_mcp(
            self, tmp_path):
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with backend.prebuilt_backend_only():
            with project.backend_rom_snapshot(str(rom_path)) as snapshot_path:
                assert snapshot_path != str(rom_path)
                assert os.path.isfile(snapshot_path)
            assert not os.path.isfile(snapshot_path)

    def test_mutating_yields_a_pure_no_op_handle_outside_mcp(self, tmp_path):
        from cli_anything.febuildergba.core import project

        missing_path = str(tmp_path / "does-not-exist.gba")
        with project.backend_mutating_rom_snapshot(missing_path) as mutator:
            assert isinstance(mutator, project._DirectRomHandle)
            assert mutator.path == missing_path
            assert mutator.committed is False
            mutator.commit()
            assert mutator.committed is True
        # commit() above never touched the filesystem at all.
        assert not os.path.exists(missing_path)

    def test_mutating_delegates_to_mutating_snapshot_inside_mcp(
            self, tmp_path):
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        with backend.prebuilt_backend_only():
            with project.backend_mutating_rom_snapshot(str(rom_path)) as mutator:
                assert isinstance(mutator, project.MutatingRomSnapshot)
                assert mutator.path != str(rom_path)
                assert os.path.isfile(mutator.path)
            assert not os.path.isfile(mutator.path)


class TestRomSnapshotAcrossAllWrappers:
    """Class-wide trust-boundary coverage for every MCP ROM-touching wrapper
    (issue #1942 / PR #1971): inside MCP's ``prebuilt_backend_only`` dynamic
    scope, each must hand the backend a private snapshot — never the
    caller's original path — and remove it once the call returns. Also
    proves lint does not double-snapshot (exactly one registration per call
    — shared with the other 8 wrappers via the same
    ``validated_rom_snapshot`` helper). The 8 non-lint wrappers' legacy
    Click passthrough (no snapshot, no local validation, unmodified caller
    path) outside MCP scope is covered separately by
    ``TestLegacyClickPassthroughOutsideMcp`` below.
    """

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _ROM_SNAPSHOT_WRAPPERS, ids=_ROM_SNAPSHOT_WRAPPER_IDS,
    )
    def test_backend_receives_a_removed_snapshot_registered_exactly_once(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            seen["existed"] = os.path.isfile(rom_arg)
            seen["registered"] = set(backend._registered_rom_snapshots.get())
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        with backend.prebuilt_backend_only():
            invoke(module, str(rom_path), tmp_path)

        assert seen.get("rom_arg") is not None, f"{func_name} never called run_cli"
        assert seen["rom_arg"] != str(rom_path), (
            f"{func_name} passed the caller's original path to the backend")
        assert seen["existed"] is True
        assert os.path.abspath(seen["rom_arg"]) in seen["registered"]
        assert len(seen["registered"]) == 1, (
            f"{func_name} registered {len(seen['registered'])} snapshots at "
            "once (expected exactly one — no double-snapshotting)")
        assert not os.path.isfile(seen["rom_arg"])
        assert backend._registered_rom_snapshots.get() == frozenset()

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _ROM_SNAPSHOT_WRAPPERS, ids=_ROM_SNAPSHOT_WRAPPER_IDS,
    )
    @pytest.mark.parametrize("kind", ["oversized", "non_rom"])
    def test_invalid_rom_rejected_before_backend(
            self, module_name, func_name, is_mutating, invoke, kind,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / f"{kind}.gba"
        if kind == "oversized":
            _write_valid_test_rom(rom_path, b"BE8E")
            with rom_path.open("r+b") as stream:
                stream.seek(project._MAX_ROM_SIZE)
                stream.write(b"\x00")
        else:
            rom_path.write_bytes(b"\x00" * project._MIN_ROM_SIZE)

        backend_calls = []
        monkeypatch.setattr(
            module, "run_cli",
            lambda args, **kw: backend_calls.append(args))

        with backend.prebuilt_backend_only(), pytest.raises(ValueError):
            invoke(module, str(rom_path), tmp_path)

        assert backend_calls == []

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _READ_ONLY_ROM_WRAPPERS, ids=_READ_ONLY_ROM_WRAPPER_IDS,
    )
    def test_read_only_snapshot_pins_bytes_despite_path_replacement(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        replacement = tmp_path / "replacement.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        _write_valid_test_rom(replacement, b"AE7E")
        original_bytes = rom_path.read_bytes()
        replacement_bytes = replacement.read_bytes()

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            assert Path(rom_arg).read_bytes() == original_bytes
            # Simulate a compromised backend replacing the caller's path
            # mid-call.
            os.replace(str(replacement), str(rom_path))
            assert Path(rom_arg).read_bytes() == original_bytes  # untouched
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        with backend.prebuilt_backend_only():
            invoke(module, str(rom_path), tmp_path)

        # The replacement content the "backend" wrote must survive untouched.
        assert rom_path.read_bytes() == replacement_bytes


class TestLegacyClickPassthroughOutsideMcp:
    """The 8 non-lint wrappers must retain their pre-#1942/#1971 historical
    Click behavior outside MCP's ``prebuilt_backend_only`` dynamic scope: no
    snapshot, no local size/header validation, and the backend receives the
    caller's own path completely unchanged. This is the direct counterpart
    to ``TestRomSnapshotAcrossAllWrappers`` (which covers the same 8
    wrappers, plus lint, *inside* MCP scope) and to
    ``TestRomSnapshotGate`` (which covers the separate ``run_cli`` seam
    guard, also MCP-only). ``lint_rom`` is intentionally excluded: its
    pre-existing historical behavior already always snapshotted, in Click
    and MCP alike (see ``TestProject``'s lint-specific tests).
    """

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _LEGACY_PASSTHROUGH_WRAPPERS, ids=_LEGACY_PASSTHROUGH_WRAPPER_IDS,
    )
    def test_backend_receives_the_original_path_unchanged(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        def _forbid_mkstemp(*args, **kwargs):
            raise AssertionError(
                f"{func_name} must not create any snapshot outside MCP scope")

        monkeypatch.setattr(project.tempfile, "mkstemp", _forbid_mkstemp)

        assert backend.is_prebuilt_backend_only() is False, (
            "test must run outside MCP's prebuilt_backend_only() scope")
        result = invoke(module, str(rom_path), tmp_path)

        assert seen.get("rom_arg") is not None, f"{func_name} never called run_cli"
        assert seen["rom_arg"] == str(rom_path), (
            f"{func_name} must pass the caller's original path unchanged "
            "outside MCP scope (legacy Click behavior)")
        assert result["exit_code"] == 0
        # The wrapper must not have removed/replaced the original file.
        assert rom_path.is_file()

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _LEGACY_PASSTHROUGH_WRAPPERS, ids=_LEGACY_PASSTHROUGH_WRAPPER_IDS,
    )
    @pytest.mark.parametrize("kind", ["oversized", "non_rom"])
    def test_oversized_and_non_rom_input_reach_the_backend_unvalidated(
            self, module_name, func_name, is_mutating, invoke, kind,
            monkeypatch, tmp_path):
        """Mirror image of ``TestRomSnapshotAcrossAllWrappers.
        test_invalid_rom_rejected_before_backend``: the same inputs that
        MCP scope must reject before the backend/resolver must, outside MCP
        scope, reach the backend exactly as historical Click always let
        them — no local validation is layered on top of legacy behavior."""
        import importlib
        from cli_anything.febuildergba.core import project

        module = importlib.import_module(module_name)
        rom_path = tmp_path / f"{kind}.gba"
        if kind == "oversized":
            _write_valid_test_rom(rom_path, b"BE8E")
            with rom_path.open("r+b") as stream:
                stream.seek(project._MAX_ROM_SIZE)
                stream.write(b"\x00")
        else:
            rom_path.write_bytes(b"\x00" * 16)

        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        result = invoke(module, str(rom_path), tmp_path)

        assert seen.get("rom_arg") == str(rom_path)
        assert result["exit_code"] == 0

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        # ``_MUTATING_ROM_WRAPPERS`` is already lint-free (lint is
        # read-only), so it is exactly the mutating subset of
        # ``_LEGACY_PASSTHROUGH_WRAPPERS``.
        _MUTATING_ROM_WRAPPERS, ids=_MUTATING_ROM_WRAPPER_IDS,
    )
    def test_mutating_commit_is_a_pure_no_op(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        """Outside MCP scope, ``data.import_table`` / ``verbs.import_palette``
        must not re-validate or re-copy anything on "commit": the backend
        already wrote the caller's real file directly, exactly like every
        historical Click mutating command, so there is nothing left to do."""
        import importlib

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            with open(rom_arg, "r+b") as f:
                f.seek(0x1000)
                f.write(b"MUTATED!")
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        result = invoke(module, str(rom_path), tmp_path)

        assert result["exit_code"] == 0
        # The backend wrote straight through the real path — no snapshot,
        # no revalidation gate to pass, no write-back indirection.
        assert rom_path.read_bytes()[0x1000:0x1000 + 8] == b"MUTATED!"


class TestWrapperSanitizesLeakedSnapshotPath:
    """Representative end-to-end proof that wrappers actually apply
    ``sanitize_snapshot_path`` to backend output before returning it (issue
    #1942 / PR #1971) — pure-function coverage lives in
    ``TestSanitizeSnapshotPath``; this proves it is really wired in for a
    read-only wrapper (``lint_rom``'s ``raw_output``), a second read-only
    wrapper with independent stdout/stderr fields (``export_table``), and a
    mutating wrapper on its success/commit path (``import_palette``).
    """

    def test_lint_sanitizes_raw_output(self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import lint

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        leaked = {}

        def inspect(args):
            rom_arg = next(a[6:] for a in args if a.startswith("--rom="))
            leaked["path"] = rom_arg
            return subprocess.CompletedProcess(
                args, 0, stdout=f"Lint: clean ({rom_arg})", stderr="")

        monkeypatch.setattr(lint, "run_cli", inspect)
        result = lint.lint_rom(str(rom_path))

        assert leaked["path"] not in result["raw_output"]
        assert str(rom_path) in result["raw_output"]

    def test_export_table_sanitizes_stdout_and_stderr(self, tmp_path, monkeypatch):
        from cli_anything.febuildergba.core import data
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        leaked = {}

        def inspect(args):
            rom_arg = next(a[6:] for a in args if a.startswith("--rom="))
            leaked["path"] = rom_arg
            return subprocess.CompletedProcess(
                args, 0,
                stdout=f"Exported via {rom_arg}",
                stderr=f"note: {rom_arg} temp")

        monkeypatch.setattr(data, "run_cli", inspect)
        # Sanitization is only meaningful where a private snapshot exists
        # (MCP scope); outside it snapshot_path == rom_path and there is
        # nothing to sanitize (see TestLegacyClickPassthroughOutsideMcp).
        with backend.prebuilt_backend_only():
            result = data.export_table(str(rom_path), "units", str(tmp_path / "out.tsv"))

        assert leaked["path"] not in result["stdout"]
        assert leaked["path"] not in result["stderr"]
        assert str(rom_path) in result["stdout"]
        assert str(rom_path) in result["stderr"]

    def test_mutating_wrapper_sanitizes_stdout_and_stderr_on_success(
            self, tmp_path, monkeypatch):
        """The mutation-success path (issue #1942 / PR #1971 acceptance:
        "internal path sanitized") must sanitize output exactly like the
        read-only wrappers above — proven here through a real ``commit()``,
        not just the pure helper."""
        from cli_anything.febuildergba.core import verbs
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        infile = _touch(tmp_path / "p.pal")
        leaked = {}

        def inspect(args):
            rom_arg = next(a[6:] for a in args if a.startswith("--rom="))
            leaked["path"] = rom_arg
            # Leave the snapshot's bytes untouched (still a valid, unchanged
            # copy) so commit() succeeds without conflating this with the
            # separate mutated-content checks covered elsewhere.
            return subprocess.CompletedProcess(
                args, 0,
                stdout=f"Imported via {rom_arg}",
                stderr=f"note: {rom_arg} temp")

        monkeypatch.setattr(verbs, "run_cli", inspect)
        # Same rationale as test_export_table_sanitizes_stdout_and_stderr:
        # sanitization only has anything to sanitize inside MCP scope.
        with backend.prebuilt_backend_only():
            result = verbs.import_palette(str(rom_path), "0x5524", infile)

        assert result["exit_code"] == 0
        assert leaked["path"] not in result["stdout"]
        assert leaked["path"] not in result["stderr"]
        assert str(rom_path) in result["stdout"]
        assert str(rom_path) in result["stderr"]
        assert not os.path.isfile(leaked["path"])


class TestMutatingWrappersCommitProtocol:
    """data.import_table / verbs.import_palette (issue #1942 / PR #1971):
    the two mutating wrappers only commit a backend-mutated snapshot back
    through the original descriptor after the backend exits 0 — never on
    failure or on a replaced original path. Same-length source drift and
    invalid/oversized mutated-result rejection are the shared
    ``MutatingRomSnapshot.commit()`` protocol, covered directly and more
    robustly by ``TestMutatingRomSnapshot``.
    """

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _MUTATING_ROM_WRAPPERS, ids=_MUTATING_ROM_WRAPPER_IDS,
    )
    def test_success_commits_mutation_and_removes_snapshot(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            with open(rom_arg, "r+b") as f:
                f.seek(0x1000)
                f.write(b"MUTATED!")
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)
        with backend.prebuilt_backend_only():
            result = invoke(module, str(rom_path), tmp_path)

        assert result["exit_code"] == 0
        assert rom_path.read_bytes()[0x1000:0x1000 + 8] == b"MUTATED!"
        assert not os.path.isfile(seen["rom_arg"])

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _MUTATING_ROM_WRAPPERS, ids=_MUTATING_ROM_WRAPPER_IDS,
    )
    def test_backend_failure_leaves_original_untouched_and_cleans_up(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        original_bytes = rom_path.read_bytes()
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            with open(rom_arg, "r+b") as f:
                f.seek(0x1000)
                f.write(b"MUTATED!")
            return subprocess.CompletedProcess(
                args, 1, stdout="", stderr="backend failed")

        monkeypatch.setattr(module, "run_cli", inspect)
        with backend.prebuilt_backend_only():
            result = invoke(module, str(rom_path), tmp_path)

        assert result["exit_code"] == 1
        assert rom_path.read_bytes() == original_bytes
        assert not os.path.isfile(seen["rom_arg"])

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _MUTATING_ROM_WRAPPERS, ids=_MUTATING_ROM_WRAPPER_IDS,
    )
    def test_oversized_mutated_result_aborts_with_no_commit(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        original_bytes = rom_path.read_bytes()
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            with open(rom_arg, "r+b") as f:
                f.seek(project._MAX_ROM_SIZE)
                f.write(b"\x00")
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        with backend.prebuilt_backend_only(), pytest.raises(Exception):
            invoke(module, str(rom_path), tmp_path)

        assert rom_path.read_bytes() == original_bytes
        assert not os.path.isfile(seen["rom_arg"])

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _MUTATING_ROM_WRAPPERS, ids=_MUTATING_ROM_WRAPPER_IDS,
    )
    def test_invalid_header_mutated_result_aborts_with_no_commit(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.core import project
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        original_bytes = rom_path.read_bytes()
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            with open(rom_arg, "r+b") as f:
                f.seek(project._GBA_FIXED_VALUE_OFFSET)
                f.write(b"\x00")
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        with backend.prebuilt_backend_only(), pytest.raises(Exception):
            invoke(module, str(rom_path), tmp_path)

        assert rom_path.read_bytes() == original_bytes
        assert not os.path.isfile(seen["rom_arg"])

    @pytest.mark.parametrize(
        ("module_name", "func_name", "is_mutating", "invoke"),
        _MUTATING_ROM_WRAPPERS, ids=_MUTATING_ROM_WRAPPER_IDS,
    )
    def test_path_replacement_during_mutation_fails_closed_or_is_os_blocked(
            self, module_name, func_name, is_mutating, invoke,
            monkeypatch, tmp_path):
        import importlib
        from cli_anything.febuildergba.utils import febuildergba_backend as backend

        module = importlib.import_module(module_name)
        rom_path = tmp_path / "rom.gba"
        replacement = tmp_path / "replacement.bin"
        _write_valid_test_rom(rom_path, b"BE8E")
        replacement.write_bytes(b"NOT-A-ROM" * 20000)
        replacement_bytes = replacement.read_bytes()
        replace_error = {}
        seen = {}

        def inspect(args, **kwargs):
            rom_arg = next(
                a[len("--rom="):] for a in args if a.startswith("--rom="))
            seen["rom_arg"] = rom_arg
            with open(rom_arg, "r+b") as f:
                f.seek(0x1000)
                f.write(b"MUTATED!")
            try:
                os.replace(str(replacement), str(rom_path))
            except OSError as exc:
                replace_error["exc"] = exc
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        monkeypatch.setattr(module, "run_cli", inspect)

        raised = None
        result = None
        with backend.prebuilt_backend_only():
            try:
                result = invoke(module, str(rom_path), tmp_path)
            except Exception as exc:  # capturing to branch on OS-dependent semantics
                raised = exc

        if "exc" in replace_error:
            # Windows blocked replacing a file with an open descriptor: the
            # legitimate mutation proceeds normally against the (never
            # actually replaced) original.
            assert raised is None, f"unexpected failure: {raised!r}"
            assert result["exit_code"] == 0
            assert rom_path.read_bytes()[0x1000:0x1000 + 8] == b"MUTATED!"
            assert replacement.read_bytes() == replacement_bytes
        else:
            # The OS allowed the replace: our identity check must fail
            # closed, and the attacker's replacement file must be left
            # exactly as written — never overwritten by our write-back.
            assert raised is not None, (
                "path replacement during mutation must fail closed")
            assert rom_path.read_bytes() == replacement_bytes

        assert "rom_arg" in seen and not os.path.isfile(seen["rom_arg"])


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
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        monkeypatch.setattr(
            data,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                args, 1, stdout="", stderr="backend failed",
            ),
        )

        result = data.export_table(str(rom_path), "units", str(output))

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
        rom_path = tmp_path / "rom.gba"
        _write_valid_test_rom(rom_path, b"BE8E")
        monkeypatch.setattr(data, "list_tables", lambda: ["units", "items"])
        monkeypatch.setattr(
            data,
            "run_cli",
            lambda args: subprocess.CompletedProcess(
                args, 0, stdout="exported", stderr="",
            ),
        )

        result = data.export_table(str(rom_path), "all", str(output))

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
