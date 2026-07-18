"""Dependency-free tests for the scenario runner using a pure-Python backend."""

import json
import os

import pytest

from febuildergba_playtest import canonical_json
from febuildergba_playtest.model import DOMAIN_SIZES, load_scenario
from febuildergba_playtest.runner import BackendError, Runner

import synthetic_gba


# --- Fake backend ----------------------------------------------------------


class FakeBackend:
    """Deterministic in-memory backend implementing the Backend protocol.

    ``program`` is an optional callable ``(backend, frame_index)`` invoked once
    per ``run_frame`` so tests can script memory changes, crashes, and stalls.
    """

    def __init__(self, program=None, screenshot_bytes=b"\x89PNG\r\n\x1a\nFAKE"):
        self.memory = {name: bytearray(size) for name, size in DOMAIN_SIZES.items()}
        self.keys = 0
        self.frame = 0
        self.program = program
        self._crash = None
        self._screenshot = screenshot_bytes
        self.loaded_rom = None
        self.reset_count = 0

    # Backend protocol -----------------------------------------------------

    def load_rom(self, rom_bytes):
        self.loaded_rom = bytes(rom_bytes)

    def reset(self):
        self.reset_count += 1
        self.frame = 0

    def set_keys(self, mask):
        self.keys = mask

    def run_frame(self):
        if self.program is not None:
            self.program(self, self.frame)
        self.frame += 1

    def read(self, domain, address, width):
        buf = self.memory[domain]
        byte_width = width // 8
        return int.from_bytes(buf[address:address + byte_width], "little")

    def write(self, domain, address, width, value):
        buf = self.memory[domain]
        byte_width = width // 8
        buf[address:address + byte_width] = int(value).to_bytes(byte_width, "little")

    def crash_message(self):
        return self._crash

    def screenshot_png(self):
        return self._screenshot

    def version(self):
        return "0.10.5"

    def commit(self):
        return "26b7884bc25a5933960f3cdcd98bac1ae14d42e2"

    def effective_config(self):
        return {"audioSync": False, "videoSync": False, "frameskip": 0, "mute": True, "biosHle": True}


def scenario(doc):
    return load_scenario(json.dumps(doc))


ROM = synthetic_gba.build_synthetic_rom()


# --- Exact frame semantics -------------------------------------------------


def test_runs_exact_frames_and_passes():
    def program(be, frame):
        if frame == 4:
            be.write("wram", 0, 8, 0x2A)
    sc = scenario({
        "schemaVersion": 1, "runFrames": 10,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 0x2A}],
    })
    result, code = Runner(sc, FakeBackend(program)).run(ROM)
    assert result["status"] == "pass"
    assert code == 0
    assert result["framesExecuted"] == 10
    assert result["assertions"][0]["passed"] is True


def test_key_applied_at_exact_frame():
    seen = {}

    def program(be, frame):
        seen[frame] = be.keys
        if be.keys & 1:  # A held
            be.write("iwram", 0, 8, 0x99)

    sc = scenario({
        "schemaVersion": 1, "runFrames": 6,
        "keys": [{"frame": 2, "keys": ["A"]}, {"frame": 4, "keys": []}],
        "assertions": [{"domain": "iwram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, _ = Runner(sc, FakeBackend(program)).run(ROM)
    # Keys persist from frame 2 until cleared at frame 4.
    assert seen[0] == 0 and seen[1] == 0
    assert seen[2] == 1 and seen[3] == 1
    assert seen[4] == 0 and seen[5] == 0
    assert result["status"] == "pass"


def test_fixed_seed_write_applied():
    captured = {}

    def program(be, frame):
        if frame == 1:
            captured["seed"] = be.read("wram", 0x100, 32)

    sc = scenario({
        "schemaVersion": 1, "runFrames": 3,
        "writes": [{"frame": 0, "domain": "wram", "address": "0x100", "width": 32, "value": "0xDEADBEEF"}],
        "assertions": [{"domain": "wram", "address": 0x100, "width": 32, "op": "equals", "value": "0xDEADBEEF"}],
    })
    result, code = Runner(sc, FakeBackend(program)).run(ROM)
    assert captured["seed"] == 0xDEADBEEF
    assert result["status"] == "pass"
    assert code == 0


# --- Assertion operators ---------------------------------------------------


def test_equals_failure_exit_two():
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 7}],
    })
    result, code = Runner(sc, FakeBackend()).run(ROM)
    assert result["status"] == "assertion_failed"
    assert code == 2
    assert result["assertions"][0]["passed"] is False
    assert result["assertions"][0]["actual"] == 0


def test_not_equals_and_range_and_changed():
    def program(be, frame):
        be.write("wram", 0, 8, 50)
        be.write("wram", 4, 16, 123)

    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [
            {"domain": "wram", "address": 0, "width": 8, "op": "notEquals", "value": 0},
            {"domain": "wram", "address": 0, "width": 8, "op": "inclusiveRange", "min": 40, "max": 60},
            {"domain": "wram", "address": 4, "width": 16, "op": "changed"},
        ],
    })
    result, code = Runner(sc, FakeBackend(program)).run(ROM)
    assert result["status"] == "pass"
    assert code == 0
    assert all(a["passed"] for a in result["assertions"])


def test_changed_fails_when_unchanged():
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, FakeBackend()).run(ROM)
    assert result["status"] == "assertion_failed"
    assert result["assertions"][0]["initial"] == 0
    assert result["assertions"][0]["actual"] == 0


# --- Crash and softlock ----------------------------------------------------


def test_crash_classified_exit_two():
    def program(be, frame):
        if frame == 3:
            be._crash = "core signalled crash"

    sc = scenario({
        "schemaVersion": 1, "runFrames": 10,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, FakeBackend(program)).run(ROM)
    assert result["status"] == "crash"
    assert code == 2
    assert result["framesExecuted"] == 4
    assert "crash" in result["note"]


def test_watchdog_softlock():
    sc = scenario({
        "schemaVersion": 1, "runFrames": 100,
        "watchdogs": [{"domain": "wram", "address": 0, "width": 8, "maxStallFrames": 5, "label": "hp"}],
    })
    result, code = Runner(sc, FakeBackend()).run(ROM)
    assert result["status"] == "softlock"
    assert code == 2
    assert result["framesExecuted"] == 5
    assert "hp" in result["note"]


def test_watchdog_softlock_samples_all_evidence_on_terminal_frame():
    sc = scenario({
        "schemaVersion": 1,
        "runFrames": 100,
        "watchdogs": [
            {
                "domain": "wram",
                "address": 0,
                "width": 8,
                "maxStallFrames": 2,
                "label": "first",
            },
            {
                "domain": "wram",
                "address": 1,
                "width": 8,
                "maxStallFrames": 10,
                "label": "later",
            },
        ],
    })
    result, code = Runner(sc, FakeBackend()).run(ROM)
    assert result["status"] == "softlock"
    assert code == 2
    assert result["framesExecuted"] == 2
    assert "first" in result["note"]
    assert [entry["stalledFrames"] for entry in result["watchdogs"]] == [2, 2]


def test_watchdog_resets_on_change():
    def program(be, frame):
        be.write("wram", 0, 8, frame & 0xFF)  # always changing

    sc = scenario({
        "schemaVersion": 1, "runFrames": 20,
        "watchdogs": [{"domain": "wram", "address": 0, "width": 8, "maxStallFrames": 5}],
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, FakeBackend(program)).run(ROM)
    assert result["status"] == "pass"
    assert result["framesExecuted"] == 20


# --- ROM guards ------------------------------------------------------------


def test_rom_sha_mismatch_fails_before_emulation():
    backend = FakeBackend()
    sc = scenario({
        "schemaVersion": 1, "runFrames": 5,
        "expectedRomSha256": "f" * 64,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, backend).run(ROM)
    assert result["status"] == "rom_mismatch"
    assert code == 2
    assert backend.reset_count == 0  # never started emulation
    assert result["romGuard"]["romSha256"]["matched"] is False


def test_rom_sha_match_passes_guard():
    sha = synthetic_gba.sha256_hex(ROM)
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "expectedRomSha256": sha,
        "expectedGameCode": synthetic_gba.header_game_code(ROM),
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 0}],
    })
    result, code = Runner(sc, FakeBackend()).run(ROM)
    assert result["status"] == "pass"
    assert result["romGuard"]["gameCode"]["matched"] is True


def test_game_code_mismatch():
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "expectedGameCode": "ZZZZ",
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, FakeBackend()).run(ROM)
    assert result["status"] == "rom_mismatch"
    assert code == 2


# --- Deterministic JSON ----------------------------------------------------


def test_result_is_deterministic():
    def program(be, frame):
        be.write("wram", 0, 8, 0x2A)

    doc = {
        "schemaVersion": 1, "runFrames": 8, "name": "det",
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 0x2A}],
    }
    r1, _ = Runner(scenario(doc), FakeBackend(program)).run(ROM)
    r2, _ = Runner(scenario(doc), FakeBackend(program)).run(ROM)
    assert canonical_json(r1) == canonical_json(r2)


def test_result_has_no_volatile_fields():
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, _ = Runner(sc, FakeBackend()).run(ROM)
    text = canonical_json(result).lower()
    for banned in ("timestamp", "duration", "elapsed", "seconds", "/", "\\", "traceback"):
        assert banned not in text


# --- Screenshot handling ---------------------------------------------------


def test_screenshot_written_and_hashed(tmp_path):
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "screenshot": {"basename": "final.png"},
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, _ = Runner(sc, FakeBackend(), artifact_dir=str(tmp_path)).run(ROM)
    written = tmp_path / "final.png"
    assert written.exists()
    assert result["artifact"]["written"] is True
    assert result["artifact"]["sha256"] == synthetic_gba.sha256_hex(FakeBackend()._screenshot)


def test_screenshot_expected_hash_mismatch_fails(tmp_path):
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "screenshot": {"basename": "final.png", "expectedSha256": "0" * 64},
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 0}],
    })
    result, code = Runner(sc, FakeBackend(), artifact_dir=str(tmp_path)).run(ROM)
    assert result["artifact"]["shaMatched"] is False
    assert result["status"] == "assertion_failed"
    assert code == 2


def test_screenshot_rejects_directory_target(tmp_path):
    (tmp_path / "final.png").mkdir()
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "screenshot": {"basename": "final.png"},
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, FakeBackend(), artifact_dir=str(tmp_path)).run(ROM)
    assert result["status"] == "harness_error"
    assert code == 1
    assert "regular file" in result["note"]


def test_screenshot_without_artifact_dir_still_hashes():
    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "screenshot": {"basename": "final.png"},
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, _ = Runner(sc, FakeBackend()).run(ROM)
    assert result["artifact"]["written"] is False
    assert "sha256" in result["artifact"]


# --- Backend faults --------------------------------------------------------


def test_backend_load_failure_is_harness_error():
    class BadBackend(FakeBackend):
        def load_rom(self, rom_bytes):
            raise BackendError("bad rom")

    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, BadBackend()).run(ROM)
    assert result["status"] == "harness_error"
    assert code == 1


# --- Backend seam exception wrapping ---------------------------------------


def test_unexpected_seam_exception_becomes_sanitized_harness_error():
    class ExplodingBackend(FakeBackend):
        def run_frame(self):
            raise RuntimeError(r"boom at C:\secret\path\core.dll")

    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, ExplodingBackend()).run(ROM)
    assert result["status"] == "harness_error"
    assert code == 1
    # The generic exception is caught and sanitized into a single JSON result.
    assert "run_frame failed: RuntimeError" in result["note"]
    assert "secret" not in result["note"]
    assert "C:\\" not in result["note"]
    # The result must still be serializable to exactly one JSON object.
    json.loads(canonical_json(result))


def test_unexpected_read_exception_becomes_harness_error():
    class BadReadBackend(FakeBackend):
        def read(self, domain, address, width):
            raise ValueError("nope")

    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    result, code = Runner(sc, BadReadBackend()).run(ROM)
    assert result["status"] == "harness_error"
    assert code == 1
    assert "read failed: ValueError" in result["note"]


def test_metadata_exception_never_breaks_result_emission():
    class BadMetaBackend(FakeBackend):
        def version(self):
            raise RuntimeError("no version")

        def commit(self):
            raise RuntimeError("no commit")

        def effective_config(self):
            raise RuntimeError("no config")

    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "equals", "value": 0}],
    })
    result, code = Runner(sc, BadMetaBackend()).run(ROM)
    # A pass is still emitted; metadata degrades gracefully to null/empty.
    assert result["status"] == "pass"
    assert result["mgba"] == {"version": None, "commit": None}
    assert result["startupConfig"] == {}


# --- Atomic screenshot publish ---------------------------------------------


def test_screenshot_publish_replaces_late_substituted_target(tmp_path):
    import febuildergba_playtest.runner as runner_mod

    calls = {"n": 0}

    def hook(final_path):
        # Simulate a late substitution: a competing regular file appears at the
        # destination between the safety check and the atomic replace. The
        # temporary file must already exist and must NOT be the final path.
        calls["n"] += 1
        assert os.path.basename(final_path) == "final.png"
        assert not final_path.endswith(".tmp")
        with open(final_path, "wb") as handle:
            handle.write(b"ATTACKER-CONTENT")

    sc = scenario({
        "schemaVersion": 1, "runFrames": 2,
        "screenshot": {"basename": "final.png"},
        "assertions": [{"domain": "wram", "address": 0, "width": 8, "op": "changed"}],
    })
    original = runner_mod._PRE_REPLACE_HOOK
    runner_mod._PRE_REPLACE_HOOK = hook
    try:
        result, _ = Runner(sc, FakeBackend(), artifact_dir=str(tmp_path)).run(ROM)
    finally:
        runner_mod._PRE_REPLACE_HOOK = original

    assert calls["n"] == 1
    written = tmp_path / "final.png"
    # os.replace overwrote the substituted file with our bytes; it was not
    # followed. No temporary artifact remains behind.
    assert written.read_bytes() == FakeBackend()._screenshot
    leftovers = [p.name for p in tmp_path.iterdir() if p.name != "final.png"]
    assert leftovers == []
    assert result["artifact"]["written"] is True
