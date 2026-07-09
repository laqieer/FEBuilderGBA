"""Wrappers for additional FEBuilderGBA.CLI verbs (issue #1933).

Each function is a thin subprocess wrapper over the real CLI (via
``run_cli``); it reimplements nothing. Functions return a structured result
dict so the Click layer can honor ``--json``.

Advisory exit codes are handled *structurally* here (not by raising): e.g.
``--checksum`` uses exit 2 to mean "the check ran successfully but the header
checksum is invalid" (advisory, non-fatal), so ``checksum()`` maps it to
``{"valid": false}`` rather than an error — the same non-fatal pattern as
``lint_oam`` (see #1933 review board).
"""

import os
import re

from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


def _base_result(result) -> dict:
    """Common fields shared by every wrapper result."""
    return {
        "exit_code": result.returncode,
        "stdout": result.stdout.strip() if result.stdout else "",
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def checksum(rom_path: str, force_version: str = "") -> dict:
    """Validate the GBA ROM header checksum (``--checksum``).

    Exit codes: 0 = valid, 2 = checked-but-INVALID (advisory, non-fatal),
    1 = file/usage error. This wrapper never raises on exit 2 — it returns
    ``valid: False``.

    Returns dict with ``valid``, ``actual``, ``expected`` (hex strings) plus
    the common fields.
    """
    args = ["--checksum", f"--rom={rom_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)

    actual = expected = None
    m = re.search(r"Header checksum:\s*0x([0-9A-Fa-f]+)\s*\(expected:\s*0x([0-9A-Fa-f]+)\)",
                  out["stdout"])
    if m:
        actual = "0x" + m.group(1).upper()
        expected = "0x" + m.group(2).upper()

    out.update({
        "rom_path": rom_path,
        # exit 0 = valid; exit 2 = checked & invalid; exit 1 = error (valid is None)
        "valid": True if result.returncode == 0 else (False if result.returncode == 2 else None),
        "actual": actual,
        "expected": expected,
    })
    return out


def repair_header(rom_path: str, force_version: str = "") -> dict:
    """Recompute and write the correct GBA header checksum in-place (``--repair-header``).

    The backend exits 0 both when it actually rewrites the byte AND when the
    header was already valid (a no-op). Those two cases are distinguished by
    parsing stdout so consumers (and the session-modified flag) aren't misled:
    ``repaired`` is True only when a byte was actually written.
    """
    args = ["--repair-header", f"--rom={rom_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)
    already_valid = "already valid" in out["stdout"].lower()
    repaired = (result.returncode == 0
                and ("repaired header checksum" in out["stdout"].lower()
                     or (not already_valid and out["stdout"] != "")))
    out.update({
        "rom_path": rom_path,
        "repaired": repaired,          # a byte was actually written
        "already_valid": already_valid,  # no-op (header was already correct)
    })
    return out


def rom_diff(rom_path: str, rom2: str, out_path: str = "") -> dict:
    """Compare two ROMs byte-by-byte (``--diff``).

    The backend exit code is 0 on success regardless of whether the ROMs
    differ (1 = file/usage error), so the identical/differ signal is parsed
    from stdout ("ROMs are identical." vs a "Total: N bytes differ …" line).

    Returns dict with ``identical``, ``bytes_differ``, ``regions`` plus common
    fields.
    """
    args = ["--diff", f"--rom={rom_path}", f"--rom2={rom2}"]
    if out_path:
        args.append(f"--out={out_path}")

    result = run_cli(args)
    out = _base_result(result)

    identical = "ROMs are identical" in out["stdout"]
    bytes_differ = regions = None
    m = re.search(r"Total:\s*(\d+)\s*bytes?\s*differ\s*across\s*(\d+)\s*region", out["stdout"])
    if m:
        bytes_differ = int(m.group(1))
        regions = int(m.group(2))
    elif identical:
        bytes_differ = 0
        regions = 0

    out.update({
        "rom_path": rom_path,
        "rom2": rom2,
        "out_path": out_path,
        # only trustworthy when the command itself succeeded (exit 0)
        "identical": identical if result.returncode == 0 else None,
        "bytes_differ": bytes_differ,
        "regions": regions,
    })
    return out


def export_map_settings_raw(rom_path: str, out_path: str,
                            force_version: str = "") -> dict:
    """Export chapter/map settings as raw struct words to TSV (``--export-map-settings``).

    Note: this is the legacy raw-hex-word dumper. For typed struct fields with
    round-trip import, prefer ``data export map_settings`` (``--export-data
    --table=map_settings``) instead.
    """
    args = ["--export-map-settings", f"--rom={rom_path}", f"--out={out_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)
    out.update({
        "rom_path": rom_path,
        "output_path": out_path,
        "file_size": os.path.getsize(out_path) if os.path.isfile(out_path) else 0,
    })
    return out


def import_midi(rom_path: str, song_id: str, in_path: str,
                force_version: str = "") -> dict:
    """Import a MIDI file into a ROM song slot in-place (``--import-midi``).

    ``song_id`` is a hex string (e.g. "1A" or "0x1A").
    """
    args = ["--import-midi", f"--rom={rom_path}",
            f"--song-id={song_id}", f"--in={in_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)
    out.update({"rom_path": rom_path, "song_id": song_id, "input_path": in_path})
    return out


def export_palette(rom_path: str, addr: str, out_path: str,
                   colors=None, force_version: str = "") -> dict:
    """Export a GBA palette to a file (``--export-palette``).

    The output extension picks the format (.pal/.act/.gpl/.txt/.gbapal).
    ``colors`` (1..256) is optional; when None/0 the ``--colors`` flag is
    omitted and the backend default (16) applies.
    """
    args = ["--export-palette", f"--rom={rom_path}",
            f"--addr={addr}", f"--out={out_path}"]
    if colors:
        args.append(f"--colors={colors}")
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)
    out.update({
        "rom_path": rom_path,
        "addr": addr,
        "output_path": out_path,
        "file_size": os.path.getsize(out_path) if os.path.isfile(out_path) else 0,
    })
    return out


def import_palette(rom_path: str, addr: str, in_path: str,
                   force_version: str = "") -> dict:
    """Import a palette file into the ROM in-place (``--import-palette``).

    The format is auto-detected from the file content/extension.
    """
    args = ["--import-palette", f"--rom={rom_path}",
            f"--addr={addr}", f"--in={in_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)
    out.update({"rom_path": rom_path, "addr": addr, "input_path": in_path})
    return out


def compile_event(rom_path: str, in_path: str, out_path: str = "",
                  force_version: str = "") -> dict:
    """Compile an ``.event`` script with the bundled/configured EA/ColorzCore
    and write the modified ROM (``--compile-event``).

    Requires Event Assembler / ColorzCore to be resolvable; if the tool is
    missing the backend returns exit 1.
    """
    args = ["--compile-event", f"--rom={rom_path}", f"--in={in_path}"]
    if out_path:
        args.append(f"--out={out_path}")
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)
    out = _base_result(result)
    # When --out is omitted the backend overwrites the input ROM, so report the
    # actual destination (rom_path) rather than an empty string.
    out.update({
        "rom_path": rom_path,
        "input_path": in_path,
        "output_path": out_path or rom_path,
    })
    return out
