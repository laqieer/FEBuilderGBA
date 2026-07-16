"""Dependency-free contracts for scripts/mgba_cffi_preprocessor.py.

This is a build-time-only helper (see its module docstring for the full
rationale: it works around a CFFI cdef parse failure in mGBA 0.10.5's pinned
``_builder.h`` on newer GCC toolchains, without ever patching the pinned
upstream source). These tests exercise it directly, in-process, with a faked
``subprocess.run`` so no real compiler or mGBA source is required.

``capsysbinary`` is used (not a hand-rolled stream fake) so both this
script's plain-text diagnostics (``sys.stderr.write``) and its binary
passthrough (``sys.stdout.buffer.write`` / ``sys.stderr.buffer.write``) are
captured the same way pytest already captures any other process output,
without needing a custom stream double.
"""

import os
import sys

import pytest

TEST_DIR = os.path.dirname(os.path.abspath(__file__))
TOOL_DIR = os.path.dirname(TEST_DIR)
REPO_ROOT = os.path.dirname(os.path.dirname(TOOL_DIR))
SCRIPTS_DIR = os.path.join(REPO_ROOT, "scripts")
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import mgba_cffi_preprocessor as wrapper  # noqa: E402


class _FakeCompleted:
    def __init__(self, returncode=0, stdout=b"", stderr=b""):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr


@pytest.fixture(autouse=True)
def _no_ambient_cc_override(monkeypatch):
    # Never let the real test-runner environment's CC leak into a test that
    # asserts the plain "cc" default.
    monkeypatch.delenv(wrapper.ENV_CC_OVERRIDE, raising=False)


@pytest.fixture(autouse=True)
def _no_ambient_wrapper_env(monkeypatch):
    # Never let the real test-runner environment's own bootstrap env vars
    # leak into a test that expects them unset/specific.
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    monkeypatch.delenv(wrapper.ENV_SOURCE_ROOT, raising=False)
    monkeypatch.delenv(wrapper.ENV_MINGW_CDEF, raising=False)


# --------------------------------------------------------------------------- #
# Compiler token resolution
# --------------------------------------------------------------------------- #
def test_default_compiler_tokens_are_plain_cc():
    assert wrapper._resolve_compiler_tokens() == ["cc"]


def test_cc_override_is_tokenized_structurally(monkeypatch):
    monkeypatch.setenv(wrapper.ENV_CC_OVERRIDE, "/usr/bin/gcc-13 -pipe")
    assert wrapper._resolve_compiler_tokens() == ["/usr/bin/gcc-13", "-pipe"]


def test_cc_override_with_only_whitespace_fails_closed(monkeypatch):
    monkeypatch.setenv(wrapper.ENV_CC_OVERRIDE, "   ")
    with pytest.raises(wrapper.PreprocessorError, match="empty command"):
        wrapper._resolve_compiler_tokens()


def test_mingw_cdef_flag_accepts_only_zero_or_one(monkeypatch):
    assert wrapper._mingw_cdef_enabled() is False
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "1")
    assert wrapper._mingw_cdef_enabled() is True
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "yes")
    with pytest.raises(wrapper.PreprocessorError, match="must be 0 or 1"):
        wrapper._mingw_cdef_enabled()


# --------------------------------------------------------------------------- #
# Exact single-line rewrite / drift rejection
# --------------------------------------------------------------------------- #
def test_exact_rewrite_replaces_only_the_one_line():
    text = "int a;\n#define va_list void*\nint b;\n"
    rewritten = wrapper._rewrite_builder_h_text(text)
    lines = rewritten.splitlines()
    assert lines == ["int a;", "typedef ... va_list;", "int b;"]
    assert wrapper.OLD_BUILDER_LINE not in rewritten
    assert rewritten.count(wrapper.NEW_BUILDER_LINE) == 1


def test_rewrite_preserves_line_ending_style():
    text = "a;\r\n#define va_list void*\r\nb;\r\n"
    rewritten = wrapper._rewrite_builder_h_text(text)
    assert "typedef ... va_list;\r\n" in rewritten


def test_rewrite_rejects_zero_occurrences():
    with pytest.raises(wrapper.PreprocessorError, match="found 0"):
        wrapper._rewrite_builder_h_text("int a;\nint b;\n")


def test_rewrite_rejects_duplicate_occurrences():
    text = "#define va_list void*\n#define va_list void*\n"
    with pytest.raises(wrapper.PreprocessorError, match="found 2"):
        wrapper._rewrite_builder_h_text(text)


def test_rewrite_rejects_when_upstream_replacement_already_present():
    text = "#define va_list void*\ntypedef ... va_list;\n"
    with pytest.raises(wrapper.PreprocessorError, match="already present"):
        wrapper._rewrite_builder_h_text(text)


def test_mingw_rewrite_scopes_attribute_sanitizer_to_limits_header():
    text = (
        "#define PYCPARSE\n"
        "#define va_list void*\n"
        "#include <limits.h>\n"
        "#include <mgba/flags.h>\n"
    )
    rewritten = wrapper._rewrite_builder_h_text(text, sanitize_mingw=True)
    assert (
        "#define __attribute__(X)\n"
        "#include <limits.h>\n"
        "#undef __attribute__\n"
    ) in rewritten
    assert rewritten.count(wrapper.ATTRIBUTE_DISABLE_LINE) == 1
    assert rewritten.count(wrapper.ATTRIBUTE_RESTORE_LINE) == 1
    assert rewritten.index(wrapper.ATTRIBUTE_DISABLE_LINE) < rewritten.index(
        wrapper.LIMITS_INCLUDE_LINE
    ) < rewritten.index(wrapper.ATTRIBUTE_RESTORE_LINE)
    assert rewritten.index(wrapper.ATTRIBUTE_RESTORE_LINE) < rewritten.index(
        "#include <mgba/flags.h>"
    )


def test_mingw_rewrite_fails_closed_if_limits_include_drifts():
    text = "#define va_list void*\n"
    with pytest.raises(wrapper.PreprocessorError, match="limits.h"):
        wrapper._rewrite_builder_h_text(text, sanitize_mingw=True)


# --------------------------------------------------------------------------- #
# Preprocessed MinGW builtin typedef normalization
# --------------------------------------------------------------------------- #
def test_builtin_va_list_alias_is_normalized_for_cffi():
    data = b"typedef __builtin_va_list __gnuc_va_list;\nint value;\n"
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __gnuc_va_list;\nint value;\n"
    )


def test_multiple_builtin_va_list_aliases_are_normalized_with_line_endings():
    data = (
        b"typedef __builtin_va_list __gnuc_va_list;\r\n"
        b"  typedef   __builtin_va_list   compiler_va_list ;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __gnuc_va_list;\r\n"
        b"typedef ... compiler_va_list;\n"
    )


def test_unrelated_preprocessor_output_is_byte_identical():
    data = b"typedef unsigned long size_t;\n#define VALUE 1\n"
    assert wrapper._normalize_builder_output(data) == data


def test_invalid_builtin_va_list_alias_fails_closed():
    data = b"typedef __builtin_va_list invalid-alias;\n"
    with pytest.raises(wrapper.PreprocessorError, match="invalid"):
        wrapper._normalize_builder_output(data)


def test_builtin_va_list_alias_count_is_bounded():
    data = b"".join(
        f"typedef __builtin_va_list alias_{index};\n".encode("ascii")
        for index in range(wrapper.MAX_BUILTIN_VA_LIST_TYPEDEFS + 1)
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


# --------------------------------------------------------------------------- #
# main(): pass-through for non-_builder.h inputs (notably lib.h)
# --------------------------------------------------------------------------- #
def test_lib_h_passes_through_completely_unchanged(monkeypatch, capsysbinary):
    recorded = {}

    def fake_run(command, stdout, stderr):
        recorded["command"] = command
        return _FakeCompleted(returncode=0, stdout=b"PREPROCESSED", stderr=b"")

    monkeypatch.setattr(wrapper.subprocess, "run", fake_run)
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)

    argv = ["-P", "-fno-inline", "-Iinclude", "lib.h"]
    assert wrapper.main(argv) == 0
    assert recorded["command"] == ["cc", "-E"] + argv
    captured = capsysbinary.readouterr()
    assert captured.out == b"PREPROCESSED"


def test_pass_through_never_reads_the_expected_env_var(monkeypatch):
    # A non-_builder.h invocation must succeed even if the expected canonical
    # path env var is entirely unset -- the env var is only consulted once the
    # final argument is actually named _builder.h.
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    monkeypatch.setattr(
        wrapper.subprocess,
        "run",
        lambda command, stdout, stderr: _FakeCompleted(returncode=0),
    )
    assert wrapper.main(["-Iinclude", "lib.h"]) == 0


# --------------------------------------------------------------------------- #
# main(): the exact pinned _builder.h path
# --------------------------------------------------------------------------- #
def test_wrong_located_builder_h_is_rejected(monkeypatch, tmp_path, capsysbinary):
    real = tmp_path / "real" / "_builder.h"
    real.parent.mkdir()
    real.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")

    decoy = tmp_path / "decoy" / "_builder.h"
    decoy.parent.mkdir()
    decoy.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")

    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(real))

    called = []
    monkeypatch.setattr(
        wrapper.subprocess,
        "run",
        lambda *a, **k: called.append(1) or _FakeCompleted(),
    )

    assert wrapper.main(["-Iinclude", str(decoy)]) == 1
    assert not called, "the real preprocessor must never run on a rejected input"
    captured = capsysbinary.readouterr()
    assert b"does not match the expected" in captured.err


def test_missing_expected_env_var_fails_closed_for_builder_h(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    monkeypatch.setattr(
        wrapper.subprocess, "run", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1
    captured = capsysbinary.readouterr()
    assert wrapper.ENV_EXPECTED_BUILDER_H.encode() in captured.err


def test_no_args_fails_closed(capsysbinary):
    assert wrapper.main([]) == 1
    captured = capsysbinary.readouterr()
    assert b"no preprocessor arguments" in captured.err


def test_source_drift_on_the_real_builder_h_fails_closed(monkeypatch, tmp_path):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text("int a;\n", encoding="utf-8")  # old line missing
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setattr(
        wrapper.subprocess, "run", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1


def test_exact_rewrite_end_to_end_uses_a_temp_copy_outside_the_source_tree_and_cleans_up(
    monkeypatch, tmp_path, capsysbinary
):
    src_dir = tmp_path / "src"
    src_dir.mkdir()
    builder_h = src_dir / "_builder.h"
    builder_h.write_text(f"int a;\n{wrapper.OLD_BUILDER_LINE}\nint b;\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(src_dir))

    seen = {}

    def fake_run(command, stdout, stderr):
        temp_path = command[-1]
        seen["temp_path"] = temp_path
        seen["command"] = command
        with open(temp_path, "r", encoding="utf-8") as handle:
            seen["temp_contents"] = handle.read()
        return _FakeCompleted(returncode=0, stdout=b"OK", stderr=b"")

    monkeypatch.setattr(wrapper.subprocess, "run", fake_run)

    rc = wrapper.main(["-P", "-fno-inline", "-Iinclude", str(builder_h)])
    assert rc == 0

    # The real header path was swapped for a temp copy; every other argument
    # (and their order) is preserved.
    assert seen["command"][:-1] == ["cc", "-E", "-P", "-fno-inline", "-Iinclude"]
    temp_path = seen["temp_path"]
    assert temp_path != str(builder_h)
    assert not str(temp_path).startswith(str(src_dir))
    assert wrapper.NEW_BUILDER_LINE in seen["temp_contents"]
    assert wrapper.OLD_BUILDER_LINE not in seen["temp_contents"]

    # The temp file must be removed after the run (in a `finally`).
    assert not os.path.exists(temp_path)

    captured = capsysbinary.readouterr()
    assert captured.out == b"OK"


def test_temp_file_is_cleaned_up_even_when_the_real_preprocessor_fails(
    monkeypatch, tmp_path
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    seen = {}

    def fake_run(command, stdout, stderr):
        seen["temp_path"] = command[-1]
        return _FakeCompleted(returncode=1, stdout=b"", stderr=b"boom")

    monkeypatch.setattr(wrapper.subprocess, "run", fake_run)

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 1
    assert not os.path.exists(seen["temp_path"])


def test_bounded_read_rejects_oversized_input(monkeypatch, tmp_path, capsysbinary):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_bytes(b"x" * (wrapper.MAX_BUILDER_H_BYTES + 1))
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setattr(
        wrapper.subprocess, "run", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1
    captured = capsysbinary.readouterr()
    assert b"bounded" in captured.err


def test_non_utf8_input_is_rejected(monkeypatch, tmp_path):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_bytes(b"\xff\xfe\x00#define va_list void*")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setattr(
        wrapper.subprocess, "run", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1


# --------------------------------------------------------------------------- #
# main(): source-tree containment proof (hostile/misconfigured TMPDIR)
# --------------------------------------------------------------------------- #
def test_missing_source_root_env_fails_closed(monkeypatch, tmp_path, capsysbinary):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    # ENV_SOURCE_ROOT is deliberately left unset by the autouse fixture.
    monkeypatch.setattr(
        wrapper.subprocess, "run", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1
    captured = capsysbinary.readouterr()
    assert wrapper.ENV_SOURCE_ROOT.encode() in captured.err


def test_hostile_tmpdir_placement_inside_source_root_is_rejected_and_cleaned_up(
    monkeypatch, tmp_path, capsysbinary
):
    src_dir = tmp_path / "src"
    src_dir.mkdir()
    builder_h = src_dir / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(src_dir))

    # Simulate a hostile/misconfigured TMPDIR that lands the "temporary"
    # overlay copy INSIDE the pinned source tree -- this must be refused,
    # even though everything else about the input is otherwise valid.
    hostile_path = str(src_dir / "evil-overlay.h")

    def fake_write_temp_copy(text):
        with open(hostile_path, "w", encoding="utf-8") as handle:
            handle.write(text)
        return hostile_path

    monkeypatch.setattr(wrapper, "_write_temp_copy", fake_write_temp_copy)
    monkeypatch.setattr(
        wrapper.subprocess, "run", lambda *a, **k: pytest.fail("must not run")
    )

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 1
    assert not os.path.exists(hostile_path), (
        "a rejected in-tree overlay copy must still be deleted, not left behind"
    )
    captured = capsysbinary.readouterr()
    assert b"inside the pinned" in captured.err


def test_source_root_itself_as_the_temp_location_is_also_rejected():
    # Edge case: the temp file lands EXACTLY at the source root path (not
    # just somewhere nested under it) -- containment must still hold.
    assert wrapper._path_is_within("/some/src", "/some/src") is True
    assert wrapper._path_is_within("/some/src/nested", "/some/src") is True
    assert wrapper._path_is_within("/some/other", "/some/src") is False


# --------------------------------------------------------------------------- #
# main(): fail-closed temp cleanup (never a silently swallowed OSError)
# --------------------------------------------------------------------------- #
def test_cleanup_failure_after_successful_preprocessing_fails_closed(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    monkeypatch.setattr(
        wrapper.subprocess,
        "run",
        lambda command, stdout, stderr: _FakeCompleted(returncode=0, stdout=b"OK"),
    )
    monkeypatch.setattr(
        wrapper,
        "_remove_temp",
        lambda path: (_ for _ in ()).throw(OSError("boom")),
    )

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 1, "a cleanup failure must fail closed even though the compiler succeeded"
    captured = capsysbinary.readouterr()
    # The compiler's own (successful) output is still relayed exactly.
    assert captured.out == b"OK"
    assert b"failed to remove the temporary" in captured.err


def test_cleanup_failure_preserves_the_original_nonzero_compiler_exit(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    monkeypatch.setattr(
        wrapper.subprocess,
        "run",
        lambda command, stdout, stderr: _FakeCompleted(
            returncode=7, stdout=b"", stderr=b"compiler boom"
        ),
    )
    monkeypatch.setattr(
        wrapper,
        "_remove_temp",
        lambda path: (_ for _ in ()).throw(OSError("cleanup boom")),
    )

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 7, (
        "the original compiler failure code must be preserved, never masked "
        "by an unrelated cleanup failure"
    )
    captured = capsysbinary.readouterr()
    assert b"failed to remove the temporary" in captured.err


def test_subprocess_is_never_invoked_via_a_shell(monkeypatch, tmp_path):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    def fake_run(command, stdout, stderr):
        assert isinstance(command, list)
        assert all(isinstance(part, str) for part in command)
        return _FakeCompleted(returncode=0)

    monkeypatch.setattr(wrapper.subprocess, "run", fake_run)
    assert wrapper.main(["-Iinclude", str(builder_h)]) == 0


def test_nonzero_compiler_exit_is_propagated(monkeypatch):
    monkeypatch.setattr(
        wrapper.subprocess,
        "run",
        lambda command, stdout, stderr: _FakeCompleted(returncode=7),
    )
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    assert wrapper.main(["-Iinclude", "lib.h"]) == 7


def test_builder_preprocessor_output_normalizes_builtin_va_list(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(
        wrapper.OLD_BUILDER_LINE + "\n" + wrapper.LIMITS_INCLUDE_LINE + "\n",
        encoding="utf-8",
    )
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "1")
    monkeypatch.setattr(
        wrapper.subprocess,
        "run",
        lambda command, stdout, stderr: _FakeCompleted(
            returncode=0,
            stdout=b"typedef __builtin_va_list __gnuc_va_list;\n",
        ),
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 0
    assert capsysbinary.readouterr().out == b"typedef ... __gnuc_va_list;\n"


def test_mingw_main_preprocesses_a_scoped_attribute_sanitizer(
    monkeypatch, tmp_path
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(
        wrapper.OLD_BUILDER_LINE + "\n"
        + wrapper.LIMITS_INCLUDE_LINE + "\n"
        + "#include <mgba/flags.h>\n",
        encoding="utf-8",
    )
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "1")
    seen = {}

    def fake_run(command, stdout, stderr):
        with open(command[-1], "r", encoding="utf-8") as handle:
            seen["text"] = handle.read()
        return _FakeCompleted(returncode=0)

    monkeypatch.setattr(wrapper.subprocess, "run", fake_run)
    assert wrapper.main(["-Iinclude", str(builder_h)]) == 0
    assert (
        wrapper.ATTRIBUTE_DISABLE_LINE + "\n"
        + wrapper.LIMITS_INCLUDE_LINE + "\n"
        + wrapper.ATTRIBUTE_RESTORE_LINE + "\n"
    ) in seen["text"]
