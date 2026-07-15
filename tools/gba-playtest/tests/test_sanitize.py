"""Tests for the shared note redactor and reparse-point rejection helper."""

import os
import stat
import types

import pytest

from febuildergba_playtest.runner import (
    BackendError,
    _safe_artifact_path,
    is_reparse_point,
    redact_message,
)


def test_redacts_windows_drive_path():
    note = redact_message(r"failed to open C:\Users\alice\secret\rom.gba while loading")
    assert "C:\\Users" not in note
    assert "alice" not in note
    assert "<path>" in note


def test_redacts_windows_extended_length_path():
    note = redact_message(r"open failed \\?\C:\temp\build\x.pyd")
    assert "\\\\?\\" not in note
    assert "<path>" in note


def test_redacts_unc_path():
    note = redact_message(r"module at \\server\share\pkg\mod.py raised")
    assert "server" not in note
    assert "share" not in note
    assert "<path>" in note


def test_redacts_unix_absolute_path():
    note = redact_message("ImportError from /home/alice/.venv/lib/python3.12/site-packages/mgba")
    assert "/home/alice" not in note
    assert "site-packages" not in note
    assert "<path>" in note


def test_preserves_non_path_slashes():
    note = redact_message("performed a read/write check and/or reset")
    assert note == "performed a read/write check and/or reset"


def test_redacts_windows_drive_path_with_spaces():
    note = redact_message(r"failed to open C:\Users\Alice B\My Docs\rom.gba now")
    assert "Alice B" not in note
    assert "My Docs" not in note
    assert "rom.gba" not in note
    assert "<path>" in note
    assert note.endswith("now")


def test_redacts_quoted_windows_path_with_spaces():
    note = redact_message('open "C:\\Program Files\\mgba\\_pylib.pyd" failed')
    assert "Program Files" not in note
    assert "<path>" in note
    assert note.endswith("failed")


def test_redacts_quoted_windows_path_with_spaces_in_final_component():
    note = redact_message('open "C:\\temp\\private rom.gba" failed')
    assert "private" not in note
    assert "rom.gba" not in note
    assert note == "open <path> failed"


def test_redacts_unc_path_with_spaces():
    note = redact_message(r"module at \\host\team share\pkg dir\mod.py raised")
    assert "team share" not in note
    assert "pkg dir" not in note
    assert "<path>" in note


def test_redacts_unix_absolute_path_with_spaces():
    note = redact_message("ImportError from /home/alice b/My Venv/lib/mgba done")
    assert "alice b" not in note
    assert "My Venv" not in note
    assert "<path>" in note
    assert note.endswith("done")


def test_redacts_quoted_unix_path_with_spaces_in_final_component():
    note = redact_message("open '/tmp/private rom.gba' failed")
    assert "private" not in note
    assert "rom.gba" not in note
    assert note == "open <path> failed"


def test_redacts_extended_length_path_with_spaces():
    note = redact_message(r"open failed \\?\C:\temp dir\build out\x.pyd here")
    assert "temp dir" not in note
    assert "build out" not in note
    assert "<path>" in note
    assert note.endswith("here")


def test_collapses_whitespace_and_caps_length():
    note = redact_message("a\n\n  b\t c" + " x" * 300)
    assert "\n" not in note
    assert "  " not in note
    assert len(note) <= 200


def test_redact_message_accepts_non_str():
    assert redact_message(ValueError("boom")) == "boom"


def test_redact_message_survives_baseexception_from_str():
    class Unprintable:
        def __str__(self):
            raise KeyboardInterrupt()

    assert redact_message(Unprintable()) == "KeyboardInterrupt: <unprintable>"


def test_is_reparse_point_regular_file(tmp_path):
    target = tmp_path / "plain.txt"
    target.write_text("hi", encoding="utf-8")
    assert is_reparse_point(str(target)) is False


def test_is_reparse_point_detects_symlink(monkeypatch, tmp_path):
    target = tmp_path / "plain.txt"
    target.write_text("hi", encoding="utf-8")
    monkeypatch.setattr(os.path, "islink", lambda p: True)
    assert is_reparse_point(str(target)) is True


def test_is_reparse_point_detects_junction(monkeypatch, tmp_path):
    target = tmp_path / "plain.txt"
    target.write_text("hi", encoding="utf-8")
    monkeypatch.setattr(os.path, "islink", lambda p: False)
    monkeypatch.setattr(os.path, "isjunction", lambda p: True, raising=False)
    assert is_reparse_point(str(target)) is True


def test_is_reparse_point_detects_reparse_attribute(monkeypatch, tmp_path):
    target = tmp_path / "plain.txt"
    target.write_text("hi", encoding="utf-8")
    monkeypatch.setattr(os.path, "islink", lambda p: False)
    monkeypatch.setattr(os.path, "isjunction", lambda p: False, raising=False)
    reparse = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    monkeypatch.setattr(
        os, "lstat", lambda p: types.SimpleNamespace(st_file_attributes=reparse)
    )
    assert is_reparse_point(str(target)) is True


def test_is_reparse_point_plain_attribute_is_false(monkeypatch, tmp_path):
    target = tmp_path / "plain.txt"
    target.write_text("hi", encoding="utf-8")
    monkeypatch.setattr(os.path, "islink", lambda p: False)
    monkeypatch.setattr(os.path, "isjunction", lambda p: False, raising=False)
    monkeypatch.setattr(
        os, "lstat", lambda p: types.SimpleNamespace(st_file_attributes=0)
    )
    assert is_reparse_point(str(target)) is False


def test_safe_artifact_path_rejects_reparse_target(monkeypatch, tmp_path):
    existing = tmp_path / "shot.png"
    existing.write_bytes(b"x")
    monkeypatch.setattr(
        "febuildergba_playtest.runner.is_reparse_point", lambda p: True
    )
    with pytest.raises(BackendError):
        _safe_artifact_path(str(tmp_path), "shot.png")


def test_safe_artifact_path_allows_new_basename(tmp_path):
    path = _safe_artifact_path(str(tmp_path), "fresh.png")
    assert os.path.dirname(path) == os.path.realpath(str(tmp_path))
