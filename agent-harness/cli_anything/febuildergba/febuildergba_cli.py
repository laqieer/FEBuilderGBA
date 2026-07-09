"""FEBuilderGBA CLI Harness — Click-based CLI with REPL support.

Usage:
    cli-anything-febuildergba                     # Enter REPL
    cli-anything-febuildergba rom info rom.gba     # One-shot command
    cli-anything-febuildergba --json data export   # JSON output mode
"""

import json
import os
import shlex
import sys

import click

from cli_anything.febuildergba import __version__
from cli_anything.febuildergba.core.session import Session


# ── Global state ──────────────────────────────────────────────────────

_session: Session | None = None
_json_mode: bool = False


def _output(data: dict, human_message: str = ""):
    """Output data in JSON or human-readable format."""
    if _json_mode:
        click.echo(json.dumps(data, indent=2, default=str))
    else:
        if human_message:
            click.echo(human_message)
        elif "error" in data:
            click.echo(f"Error: {data['error']}", err=True)


def _check_exit_code(result: dict, context: str = "Command"):
    """Raise ClickException if backend returned non-zero exit code."""
    if result.get("exit_code", 0) != 0:
        stderr = result.get("stderr", "") or result.get("stdout", "")
        raise click.ClickException(
            f"{context} failed (exit {result['exit_code']}): {stderr}"
        )


def _get_rom_path(ctx_rom: str = "") -> str:
    """Get ROM path from argument or session."""
    if ctx_rom:
        return ctx_rom
    if _session and _session.is_open():
        return _session.state.rom_path
    raise click.UsageError(
        "No ROM specified. Use --rom or open a session first with: session open <rom>"
    )


def _get_force_version() -> str:
    """Get force-version from session if available."""
    if _session and _session.is_open():
        return _session.state.force_version
    return ""


# ── Main CLI group ────────────────────────────────────────────────────

@click.group(invoke_without_command=True)
@click.option("--json", "json_output", is_flag=True, help="Output in JSON format")
@click.option("--rom", "rom_path", default="", help="ROM file path")
@click.option("--session-file", default="", help="Session file path")
@click.version_option(__version__, prog_name="cli-anything-febuildergba")
@click.pass_context
def cli(ctx, json_output, rom_path, session_file):
    """FEBuilderGBA CLI Harness — Fire Emblem GBA ROM hacking from the command line."""
    global _session, _json_mode
    _json_mode = json_output
    _session = Session(session_file if session_file else None)

    ctx.ensure_object(dict)
    ctx.obj["rom_path"] = rom_path

    if ctx.invoked_subcommand is None:
        ctx.invoke(repl)


# ── ROM commands ──────────────────────────────────────────────────────

@cli.group()
def rom():
    """ROM file operations — info, validate, version."""
    pass


@rom.command("info")
@click.argument("rom_file", required=False)
@click.option("--force-version", default="", help="Force version (FE6/FE7J/FE7U/FE8J/FE8U)")
@click.pass_context
def rom_info_cmd(ctx, rom_file, force_version):
    """Show ROM information and metadata."""
    from cli_anything.febuildergba.core.project import rom_info
    path = rom_file or _get_rom_path(ctx.obj.get("rom_path", ""))
    result = rom_info(path, force_version)
    _output(result, (
        f"ROM: {result['rom_path']}\n"
        f"Size: {result['rom_size_mb']} MB ({result['rom_size_hex']})\n"
        f"Version: {result['detected_version']}"
    ))


@rom.command("validate")
@click.argument("rom_file")
def rom_validate_cmd(rom_file):
    """Check if a file is a valid GBA ROM."""
    from cli_anything.febuildergba.core.project import validate_rom
    valid = validate_rom(rom_file)
    result = {"rom_path": rom_file, "valid": valid}
    _output(result, f"{'Valid' if valid else 'Invalid'} GBA ROM: {rom_file}")


@rom.command("tables")
def rom_tables_cmd():
    """List all supported data tables."""
    from cli_anything.febuildergba.core.project import list_tables
    tables = list_tables()
    _output({"tables": tables, "count": len(tables)},
            "\n".join(f"  {t}" for t in tables))


@rom.command("header")
@click.argument("rom_file", required=False)
@click.pass_context
def rom_header_cmd(ctx, rom_file):
    """Dump raw GBA header fields (title, game_code, maker_code, etc.)."""
    from cli_anything.febuildergba.core.project import rom_header
    path = rom_file or _get_rom_path(ctx.obj.get("rom_path", ""))
    result = rom_header(path)
    _output(result, (
        f"Title:            {result['title']}\n"
        f"Game Code:        {result['game_code']}\n"
        f"Maker Code:       {result['maker_code']}\n"
        f"Unit Code:        0x{result['unit_code']:02X}\n"
        f"Device Type:      0x{result['device_type']:02X}\n"
        f"Software Version: 0x{result['software_version']:02X}\n"
        f"Header Checksum:  0x{result['header_checksum']:02X}"
    ))


@rom.command("save")
@click.option("-o", "--out", required=True, help="Output ROM file path")
@click.pass_context
def rom_save_cmd(ctx, out):
    """Copy ROM file to a new location."""
    from cli_anything.febuildergba.core.project import save_rom
    path = _get_rom_path(ctx.obj.get("rom_path", ""))
    result = save_rom(path, out)
    _output(result, f"Saved ROM to {result['output_path']} ({result['file_size']} bytes)")


@rom.command("checksum")
@click.argument("rom_file", required=False)
@click.option("--force-version", default="")
@click.pass_context
def rom_checksum_cmd(ctx, rom_file, force_version):
    """Validate the GBA header checksum (an INVALID header is reported, not an error)."""
    from cli_anything.febuildergba.core.verbs import checksum
    path = rom_file or _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = checksum(path, fv)
    # Do NOT _check_exit_code blindly: exit 2 = "checked, header INVALID" (advisory,
    # non-fatal). Only exit 1 is a real file/usage error.
    if result["exit_code"] == 1:
        _check_exit_code(result, "Checksum")
    if _json_mode:
        _output(result)
    elif result["valid"]:
        click.echo(f"Header checksum VALID ({result['actual']})")
    else:
        click.echo(f"Header checksum INVALID (actual {result['actual']}, "
                   f"expected {result['expected']}) — use 'rom repair-header' to fix")


@rom.command("repair-header")
@click.argument("rom_file", required=False)
@click.option("--force-version", default="")
@click.pass_context
def rom_repair_header_cmd(ctx, rom_file, force_version):
    """Recompute and write the correct GBA header checksum in-place."""
    from cli_anything.febuildergba.core.verbs import repair_header
    path = rom_file or _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = repair_header(path, fv)
    _check_exit_code(result, "Repair header")
    if _session:
        _session.record_operation("repair_header", {"rom": path})
        _session.mark_modified()
    _output(result, f"Header checksum repaired: {path}")


@rom.command("diff")
@click.argument("rom_file")
@click.argument("rom2")
@click.option("-o", "--out", default="", help="Write a TSV of the differing ranges")
def rom_diff_cmd(rom_file, rom2, out):
    """Compare two ROMs byte-by-byte (distinct from `data diff`, which compares TSVs)."""
    from cli_anything.febuildergba.core.verbs import rom_diff
    result = rom_diff(rom_file, rom2, out)
    _check_exit_code(result, "ROM diff")  # exit 1 = file/usage error only
    if _json_mode:
        _output(result)
    elif result["identical"]:
        click.echo("ROMs are identical.")
    else:
        click.echo(f"ROMs differ: {result['bytes_differ']} bytes across "
                   f"{result['regions']} region(s)"
                   + (f" (TSV: {out})" if out else ""))


# ── Data commands ─────────────────────────────────────────────────────

@cli.group()
def data():
    """Struct data export/import (units, classes, items, etc.)."""
    pass


@data.command("export")
@click.argument("table")
@click.option("-o", "--out", required=True, help="Output TSV file path")
@click.option("--force-version", default="")
@click.pass_context
def data_export_cmd(ctx, table, out, force_version):
    """Export a data table to TSV. Use 'all' for all tables."""
    from cli_anything.febuildergba.core.data import export_table
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = export_table(rom, table, out, fv)
    _check_exit_code(result, f"Data export ({table})")
    if _session:
        _session.record_operation("data_export", {"table": table, "out": out})
    _output(result, f"Exported {table} to {out}")


@data.command("import")
@click.argument("table")
@click.option("-i", "--input-file", "in_file", required=True, help="Input TSV file path")
@click.option("--force-version", default="")
@click.pass_context
def data_import_cmd(ctx, table, in_file, force_version):
    """Import data from TSV into ROM."""
    from cli_anything.febuildergba.core.data import import_table
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = import_table(rom, table, in_file, fv)
    _check_exit_code(result, f"Data import ({table})")
    if _session:
        _session.record_operation("data_import", {"table": table, "in": in_file})
        _session.mark_modified()
    _output(result, f"Imported {table} from {in_file}")


@data.command("roundtrip")
@click.option("--table", default="all", help="Table to validate (default: all)")
@click.option("--force-version", default="")
@click.pass_context
def data_roundtrip_cmd(ctx, table, force_version):
    """Validate struct data round-trip (export/import/compare)."""
    from cli_anything.febuildergba.core.data import roundtrip_table
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = roundtrip_table(rom, table, fv)
    _check_exit_code(result, f"Data roundtrip ({table})")
    status = "PASS (lossless)" if result["lossless"] else "FAIL (mismatches)"
    _output(result, f"Roundtrip {table}: {status}")


@data.command("inspect")
@click.argument("tsv_file")
def data_inspect_cmd(tsv_file):
    """Inspect a TSV export file (preview rows, column info)."""
    from cli_anything.febuildergba.core.data import tsv_summary
    result = tsv_summary(tsv_file)
    if _json_mode:
        _output(result)
    else:
        click.echo(f"File: {result['path']}")
        click.echo(f"Rows: {result['row_count']}")
        click.echo(f"Columns: {', '.join(result['columns'])}")
        if result['preview']:
            click.echo("\nPreview:")
            for row in result['preview']:
                click.echo(f"  {row}")


@data.command("diff")
@click.argument("file_a")
@click.argument("file_b")
def data_diff_cmd(file_a, file_b):
    """Compare two TSV exports and report differences."""
    from cli_anything.febuildergba.core.data import diff_tsv
    result = diff_tsv(file_a, file_b)
    if _json_mode:
        _output(result)
    else:
        added = len(result["added_rows"])
        removed = len(result["removed_rows"])
        changed = len(result["changed_rows"])
        unchanged = result["unchanged_count"]
        click.echo(f"Added:     {added}")
        click.echo(f"Removed:   {removed}")
        click.echo(f"Changed:   {changed}")
        click.echo(f"Unchanged: {unchanged}")
        for ch in result["changed_rows"]:
            click.echo(f"  [{ch['id']}]")
            for field, vals in ch["fields"].items():
                click.echo(f"    {field}: {vals['old']} -> {vals['new']}")


@data.command("lookup")
@click.argument("tsv_file")
@click.argument("entry_id")
def data_lookup_cmd(tsv_file, entry_id):
    """Look up a single entry by ID from an exported TSV."""
    from cli_anything.febuildergba.core.data import lookup_entry
    result = lookup_entry(tsv_file, entry_id)
    if _json_mode:
        _output(result)
    else:
        if result["found"]:
            click.echo(f"Entry {entry_id}:")
            for k, v in result["row"].items():
                click.echo(f"  {k}: {v}")
        else:
            click.echo(f"Entry {entry_id} not found")


# ── Text commands ─────────────────────────────────────────────────────

@cli.group()
def text():
    """Text export/import and translation tools."""
    pass


@text.command("export")
@click.option("-o", "--out", required=True, help="Output TSV file path")
@click.option("--force-version", default="")
@click.pass_context
def text_export_cmd(ctx, out, force_version):
    """Export all ROM text to TSV."""
    from cli_anything.febuildergba.core.text import export_text
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = export_text(rom, out, fv)
    _check_exit_code(result, "Text export")
    _output(result, f"Exported text to {out} ({result['file_size']} bytes)")


@text.command("import")
@click.option("-i", "--input-file", "in_file", required=True, help="Input TSV file path")
@click.option("--force-version", default="")
@click.pass_context
def text_import_cmd(ctx, in_file, force_version):
    """Import text from TSV into ROM (Huffman encode + write)."""
    from cli_anything.febuildergba.core.text import import_text
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = import_text(rom, in_file, fv)
    _check_exit_code(result, "Text import")
    if _session:
        _session.record_operation("text_import", {"in": in_file})
        _session.mark_modified()
    _output(result, f"Imported text from {in_file}")


@text.command("roundtrip")
@click.option("--out-prefix", default="", help="Output prefix for diff files")
@click.option("--force-version", default="")
@click.pass_context
def text_roundtrip_cmd(ctx, out_prefix, force_version):
    """Validate text export/import round-trip."""
    from cli_anything.febuildergba.core.text import roundtrip_text
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = roundtrip_text(rom, out_prefix, fv)
    _check_exit_code(result, "Text roundtrip")
    status = "PASS (lossless)" if result["lossless"] else "FAIL (mismatches)"
    _output(result, f"Text roundtrip: {status}")


@text.command("search")
@click.argument("query")
@click.option("--force-version", default="")
@click.pass_context
def text_search_cmd(ctx, query, force_version):
    """Search ROM text by substring."""
    from cli_anything.febuildergba.core.text import search_text
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = search_text(rom, query, fv)
    _check_exit_code(result, "Text search")
    if _json_mode:
        _output(result)
    else:
        click.echo(f"Search: '{query}' — {result['match_count']} matches")
        for m in result["matches"]:
            click.echo(f"  [{m['id']}] {m['text']}")


# ── Lint commands ─────────────────────────────────────────────────────

@cli.command("lint")
@click.option("--force-version", default="")
@click.pass_context
def lint_cmd(ctx, force_version):
    """Run integrity checks on the ROM."""
    from cli_anything.febuildergba.core.lint import lint_rom
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = lint_rom(rom, fv)
    _check_exit_code(result, "Lint")
    if _json_mode:
        _output(result)
    else:
        status = "CLEAN" if result["clean"] else "ISSUES FOUND"
        click.echo(f"Lint: {status}")
        click.echo(f"  Errors: {result['error_count']}")
        click.echo(f"  Warnings: {result['warning_count']}")
        for e in result["errors"]:
            click.echo(f"  [ERROR] {e}")
        for w in result["warnings"]:
            click.echo(f"  [WARN]  {w}")


# ── Patch commands ────────────────────────────────────────────────────

@cli.group()
def patch():
    """Patch operations — list, create UPS, apply UPS."""
    pass


@patch.command("create")
@click.option("-o", "--out", required=True, help="Output UPS file")
@click.option("--from-rom", default="", help="Original ROM for diff")
@click.pass_context
def patch_create_cmd(ctx, out, from_rom):
    """Create a UPS patch from ROM differences."""
    from cli_anything.febuildergba.core.export import create_ups
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    result = create_ups(rom, out, from_rom)
    _check_exit_code(result, "Patch create")
    _output(result, f"Created UPS patch: {out} ({result['file_size']} bytes)")


@patch.command("list")
@click.option("--config-dir", default="", help="Path to config/ directory")
@click.option("--force-version", default="")
@click.pass_context
def patch_list_cmd(ctx, config_dir, force_version):
    """List available patches for the ROM version."""
    from cli_anything.febuildergba.core.patches import list_patches
    from cli_anything.febuildergba.core.project import _detect_version
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    if not fv:
        fv = _detect_version(rom)
    if not config_dir:
        # Default: look for config/ relative to the ROM or current directory
        config_dir = os.path.join(os.path.dirname(os.path.abspath(rom)), "config")
        if not os.path.isdir(config_dir):
            config_dir = os.path.join(os.getcwd(), "config")
    result = list_patches(config_dir, fv)
    if _json_mode:
        _output(result)
    else:
        click.echo(f"Patches for {result['version']}: {result['count']}")
        for p in result["patches"]:
            name = p["name"]
            comment = f" — {p['info']}" if p.get("info") else ""
            click.echo(f"  {name}{comment}")


@patch.command("apply")
@click.argument("patch_file")
@click.option("-o", "--out", default="", help="Output ROM path (default: <rom>.patched.gba)")
@click.pass_context
def patch_apply_cmd(ctx, patch_file, out):
    """Apply a UPS patch to a ROM.

    Backend contract: --applyups=<output> --rom=<original> --patch=<patch.ups>
    """
    from cli_anything.febuildergba.core.export import apply_ups
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    result = apply_ups(rom, patch_file, out)
    _check_exit_code(result, "Patch apply")
    if _session:
        _session.record_operation("patch_apply", {"patch": patch_file})
        _session.mark_modified()
    _output(result, f"Applied patch: {patch_file} -> {result.get('output_path', '')}")


# ── ASM commands ──────────────────────────────────────────────────────

@cli.command("disasm")
@click.option("-o", "--out", required=True, help="Output disassembly file")
@click.option("--force-version", default="")
@click.pass_context
def disasm_cmd(ctx, out, force_version):
    """Disassemble ROM to text file."""
    from cli_anything.febuildergba.core.export import disassemble
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = disassemble(rom, out, fv)
    _check_exit_code(result, "Disassembly")
    _output(result, f"Disassembled to {out} ({result['file_size']} bytes)")


# ── Image commands ────────────────────────────────────────────────────

@cli.group()
def image():
    """Image operations — palette quantization, map tile conversion."""
    pass


@image.command("quantize")
@click.option("-i", "--input-file", "in_file", required=True, help="Input image")
@click.option("-o", "--out", required=True, help="Output image")
@click.option("--palette-no", default=0, type=int, help="Palette number")
@click.option("--no-scale", is_flag=True, help="Don't scale to GBA 5-bit")
@click.option("--no-reserve-1st", is_flag=True, help="Don't reserve slot 0")
@click.option("--ignore-tsa", is_flag=True, help="Ignore TSA constraints")
def image_quantize_cmd(in_file, out, palette_no, no_scale, no_reserve_1st, ignore_tsa):
    """Quantize image palette to 16 colors for GBA."""
    from cli_anything.febuildergba.core.export import decrease_color
    result = decrease_color(in_file, out, palette_no, no_scale, no_reserve_1st, ignore_tsa)
    _check_exit_code(result, "Image quantize")
    _output(result, f"Quantized to {out} ({result['file_size']} bytes)")


@image.command("convert-map")
@click.option("-i", "--input-file", "in_file", required=True, help="Input image")
@click.option("--out-img", required=True, help="Output tile image")
@click.option("--out-tsa", required=True, help="Output TSA data")
def image_convert_map_cmd(in_file, out_img, out_tsa):
    """Convert image to map tiles + TSA."""
    from cli_anything.febuildergba.core.export import convert_map_image
    result = convert_map_image(in_file, out_img, out_tsa)
    _check_exit_code(result, "Map conversion")
    _output(result, f"Converted map: {out_img}, {out_tsa}")


# ── Music commands ────────────────────────────────────────────────────

@cli.command("songexchange")
@click.option("--from-rom", required=True, help="Source ROM")
@click.option("--from-song", required=True, type=str, help="Source song ID (hex, e.g. 1A or 0x1A)")
@click.option("--to-song", required=True, type=str, help="Destination song ID (hex)")
@click.pass_context
def songexchange_cmd(ctx, from_rom, from_song, to_song):
    """Copy a song from one ROM to another. Song IDs are hex (e.g. 1A, 0x1A)."""
    from cli_anything.febuildergba.core.export import song_exchange
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    result = song_exchange(rom, from_rom, from_song, to_song)
    _check_exit_code(result, "Song exchange")
    _output(result, f"Exchanged song {from_song} -> {to_song}")


# ── Name resolution command ───────────────────────────────────────────

@cli.command("names")
@click.argument("kind", type=click.Choice(["unit", "class", "item", "song"]))
@click.argument("ids")
@click.option("--force-version", default="")
@click.pass_context
def names_cmd(ctx, kind, ids, force_version):
    """Resolve entity IDs to names. IDs are comma-separated (e.g. 0,1,2,3)."""
    from cli_anything.febuildergba.core.export import resolve_names
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    try:
        id_list = [int(x.strip()) for x in ids.split(",") if x.strip()]
    except ValueError:
        raise click.UsageError(f"Invalid IDs '{ids}'. Use comma-separated integers (e.g. 0,1,2,3)")
    result = resolve_names(rom, kind, id_list, fv)
    _check_exit_code(result, "Name resolution")
    if _json_mode:
        _output(result)
    else:
        for id_str, name in result.get("names", {}).items():
            click.echo(f"  {id_str}\t{name}")


# ── Portrait rendering command ────────────────────────────────────────

@cli.command("portrait")
@click.argument("unit_id", type=int)
@click.option("-o", "--out", required=True, help="Output PNG file path")
@click.option("--force-version", default="")
@click.pass_context
def portrait_cmd(ctx, unit_id, out, force_version):
    """Render a unit portrait to PNG."""
    from cli_anything.febuildergba.core.export import render_portrait
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = render_portrait(rom, unit_id, out, fv)
    _check_exit_code(result, "Portrait render")
    _output(result, f"Portrait rendered: {out} ({result['file_size']} bytes)")


# ── MIDI export command ───────────────────────────────────────────────

@cli.command("export-midi")
@click.argument("song_id", type=str)
@click.option("-o", "--out", required=True, help="Output MIDI file path")
@click.option("--force-version", default="")
@click.pass_context
def export_midi_cmd(ctx, song_id, out, force_version):
    """Export a GBA song to MIDI file. Song ID is hex (e.g. 1A, 0x1A)."""
    from cli_anything.febuildergba.core.export import export_midi
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = export_midi(rom, song_id, out, fv)
    _check_exit_code(result, "MIDI export")
    _output(result, f"MIDI exported: {out} ({result['file_size']} bytes)")


@cli.command("import-midi")
@click.argument("song_id", type=str)
@click.option("-i", "--in", "in_path", required=True, help="Input MIDI file path")
@click.option("--force-version", default="")
@click.pass_context
def import_midi_cmd(ctx, song_id, in_path, force_version):
    """Import a MIDI file into a ROM song slot. Song ID is hex (e.g. 1A, 0x1A)."""
    from cli_anything.febuildergba.core.verbs import import_midi
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = import_midi(rom, song_id, in_path, fv)
    _check_exit_code(result, "MIDI import")
    if _session:
        _session.record_operation("import_midi", {"song_id": song_id})
        _session.mark_modified()
    _output(result, f"MIDI imported into song {song_id}: {in_path}")


# ── Event script disassembly command ──────────────────────────────────

@cli.command("disasm-event")
@click.argument("addr")
@click.option("--type", "script_type", default="event",
              type=click.Choice(["event", "procs", "ai"]),
              help="Script type (default: event)")
@click.option("-o", "--out", default="", help="Output file (default: stdout)")
@click.option("--force-version", default="")
@click.pass_context
def disasm_event_cmd(ctx, addr, script_type, out, force_version):
    """Disassemble event script at ROM address. Address is hex (e.g. 0x9A0000)."""
    from cli_anything.febuildergba.core.export import disasm_event
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = disasm_event(rom, addr, script_type, out, fv)
    _check_exit_code(result, "Event disassembly")
    if _json_mode:
        _output(result)
    elif out:
        click.echo(result.get("stderr", result.get("stdout", "")).split("\n")[-1])
    else:
        click.echo(result.get("stdout", ""))


@cli.command("compile-event")
@click.option("-i", "--in", "in_path", required=True, help="Input .event script")
@click.option("-o", "--out", default="", help="Output ROM (default: overwrite input ROM)")
@click.option("--force-version", default="")
@click.pass_context
def compile_event_cmd(ctx, in_path, out, force_version):
    """Compile an .event script with the bundled/configured EA/ColorzCore and write the ROM."""
    from cli_anything.febuildergba.core.verbs import compile_event
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = compile_event(rom, in_path, out, fv)
    _check_exit_code(result, "Event compile")  # exit 1 = tool missing or compile failure
    if _session:
        _session.record_operation("compile_event", {"in": in_path})
        _session.mark_modified()
    _output(result, result.get("stdout", "") or f"Compiled {in_path}")


# ── OAM lint command ─────────────────────────────────────────────────

@cli.command("lint-oam")
@click.argument("addr")
@click.option("--length", default=0, type=int, help="Bytes to scan (0=auto)")
@click.option("--force-version", default="")
@click.pass_context
def lint_oam_cmd(ctx, addr, length, force_version):
    """Validate battle animation OAM data at ROM address."""
    from cli_anything.febuildergba.core.export import lint_oam
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = lint_oam(rom, addr, length, fv)
    # Don't _check_exit_code — exit 1 means lint found issues (not fatal)
    if _json_mode:
        _output(result)
    else:
        if result["clean"]:
            click.echo(f"OAM lint: CLEAN (no issues at {addr})")
        else:
            click.echo(f"OAM lint: {result['issue_count']} issue(s)")
            for issue in result["issues"]:
                click.echo(f"  {issue}")


@cli.command("export-map-settings-raw")
@click.option("-o", "--out", required=True, help="Output TSV path")
@click.option("--force-version", default="")
@click.pass_context
def export_map_settings_raw_cmd(ctx, out, force_version):
    """Export chapter/map settings as raw struct words to TSV.

    Legacy raw-hex dumper. For typed, round-trippable fields prefer:
    `data export map_settings` (--export-data --table=map_settings).
    """
    from cli_anything.febuildergba.core.verbs import export_map_settings_raw
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = export_map_settings_raw(rom, out, fv)
    _check_exit_code(result, "Export map settings")
    _output(result, f"Map settings exported: {out} ({result['file_size']} bytes)")


# ── Palette commands ─────────────────────────────────────────────────

@cli.group()
def palette():
    """Palette operations — export/import GBA palettes."""
    pass


@palette.command("export")
@click.option("--addr", required=True, help="Palette address in hex (e.g. 0x5524)")
@click.option("-o", "--out", required=True,
              help="Output file; extension picks format (.pal/.act/.gpl/.txt/.gbapal)")
@click.option("--colors", default=0, type=int, help="Color count 1..256 (default 16)")
@click.option("--force-version", default="")
@click.pass_context
def palette_export_cmd(ctx, addr, out, colors, force_version):
    """Export a GBA palette to a file."""
    from cli_anything.febuildergba.core.verbs import export_palette
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = export_palette(rom, addr, out, colors, fv)
    _check_exit_code(result, "Palette export")
    _output(result, f"Palette exported: {out} ({result['file_size']} bytes)")


@palette.command("import")
@click.option("--addr", required=True, help="Palette address in hex (e.g. 0x5524)")
@click.option("-i", "--in", "in_path", required=True, help="Input palette file")
@click.option("--force-version", default="")
@click.pass_context
def palette_import_cmd(ctx, addr, in_path, force_version):
    """Import a palette file into the ROM (format auto-detected)."""
    from cli_anything.febuildergba.core.verbs import import_palette
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = import_palette(rom, addr, in_path, fv)
    _check_exit_code(result, "Palette import")
    if _session:
        _session.record_operation("import_palette", {"addr": addr})
        _session.mark_modified()
    _output(result, f"Palette imported at {addr}: {in_path}")


# ── Patch apply command ──────────────────────────────────────────────

@patch.command("apply-bin")
@click.argument("patch_file")
@click.option("--force-version", default="")
@click.pass_context
def patch_apply_bin_cmd(ctx, patch_file, force_version):
    """Apply a BIN patch from config/patch2/. Creates backup automatically."""
    from cli_anything.febuildergba.core.export import apply_patch
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = apply_patch(rom, patch_file, fv)
    _check_exit_code(result, "Patch apply")
    if _session:
        _session.record_operation("patch_apply_bin", {"patch": patch_file})
        _session.mark_modified()
    _output(result, result.get("stdout", ""))


# ── Rebuild command ───────────────────────────────────────────────────

@cli.command("rebuild")
@click.option("--from-rom", required=True, help="Original clean ROM")
@click.option("--force-version", default="")
@click.pass_context
def rebuild_cmd(ctx, from_rom, force_version):
    """Rebuild/defragment the ROM."""
    from cli_anything.febuildergba.core.export import rebuild
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = rebuild(rom, from_rom, fv)
    _check_exit_code(result, "Rebuild")
    if _session:
        _session.record_operation("rebuild", {"from_rom": from_rom})
    _output(result, f"Rebuilt ROM successfully")


# ── Pointer calc command ──────────────────────────────────────────────

@cli.command("pointercalc")
@click.option("--target", required=True, help="Target value")
@click.option("--address", required=True, help="Address to search")
@click.option("--force-version", default="")
@click.pass_context
def pointercalc_cmd(ctx, target, address, force_version):
    """Search for pointer references in ROM."""
    from cli_anything.febuildergba.core.export import pointer_calc
    rom = _get_rom_path(ctx.obj.get("rom_path", ""))
    fv = force_version or _get_force_version()
    result = pointer_calc(rom, target, address, fv)
    _check_exit_code(result, "Pointer calc")
    _output(result, result.get("stdout", ""))


# ── Session commands ──────────────────────────────────────────────────

@cli.group()
def session():
    """Session management — open ROM, track state, view history."""
    pass


@session.command("open")
@click.argument("rom_file")
@click.option("--force-version", default="", help="Force version detection")
def session_open_cmd(rom_file, force_version):
    """Open a ROM and start a persistent session."""
    from cli_anything.febuildergba.core.project import rom_info
    info = rom_info(rom_file, force_version)
    _session.open_rom(rom_file, info["detected_version"],
                      info["rom_size"], force_version)
    _output(
        {"status": "opened", **info},
        f"Session opened: {info['detected_version']} ({info['rom_size_mb']} MB)"
    )


@session.command("close")
def session_close_cmd():
    """Close the current session."""
    if not _session.is_open():
        _output({"status": "no_session"}, "No active session")
        return
    _session.close()
    _output({"status": "closed"}, "Session closed")


@session.command("status")
def session_status_cmd():
    """Show current session status."""
    info = _session.info()
    if _json_mode:
        _output(info)
    else:
        if not _session.is_open():
            click.echo("No active session. Use: session open <rom>")
        else:
            click.echo(f"ROM: {info['rom_path']}")
            click.echo(f"Version: {info['rom_version']}")
            click.echo(f"Size: {info['rom_size']} bytes")
            click.echo(f"Modified: {info['modified']}")
            click.echo(f"Operations: {info['history_count']}")


@session.command("history")
@click.option("-n", "--count", default=10, help="Number of entries")
def session_history_cmd(count):
    """Show session operation history."""
    if not _session.is_open():
        _output({"error": "no_session"}, "No active session")
        return
    entries = _session.state.history[-count:]
    if _json_mode:
        _output({"history": entries})
    else:
        for entry in entries:
            click.echo(f"  [{entry.get('op', '?')}] {entry}")


# ── Backend check ─────────────────────────────────────────────────────

@cli.command("check")
def check_cmd():
    """Check if the FEBuilderGBA.CLI backend is available."""
    from cli_anything.febuildergba.utils.febuildergba_backend import check_backend
    result = check_backend()
    if _json_mode:
        _output(result)
    else:
        if result["available"]:
            click.echo(f"Backend: OK ({result['version']})")
            click.echo(f"Command: {result['command']}")
        else:
            click.echo(f"Backend: NOT FOUND", err=True)
            click.echo(f"Error: {result['error']}", err=True)


# ── REPL ──────────────────────────────────────────────────────────────

@cli.command("repl", hidden=True)
@click.option("--project-path", default=None, hidden=True)
def repl(project_path):
    """Enter interactive REPL mode."""
    from cli_anything.febuildergba.utils.repl_skin import ReplSkin

    skin = ReplSkin("febuildergba", version=__version__)
    skin.print_banner()

    pt_session = skin.create_prompt_session()

    commands_help = {
        "rom info <file>": "Show ROM information",
        "rom validate <file>": "Validate GBA ROM",
        "rom header <file>": "Dump raw GBA header fields",
        "rom tables": "List supported data tables",
        "rom save -o <file>": "Copy ROM to new location",
        "session open <rom>": "Open ROM session",
        "session status": "Show session status",
        "session close": "Close session",
        "data export <table> -o <file>": "Export table to TSV",
        "data import <table> -i <file>": "Import table from TSV",
        "data roundtrip": "Validate data round-trip",
        "data diff <file_a> <file_b>": "Compare two TSV exports",
        "data lookup <tsv> <id>": "Look up entry by ID in TSV",
        "text export -o <file>": "Export ROM text",
        "text import -i <file>": "Import text into ROM",
        "text search <query>": "Search ROM text by substring",
        "lint": "Run ROM integrity checks",
        "patch create -o <file>": "Create UPS patch",
        "patch apply <file>": "Apply UPS patch",
        "patch list": "List available patches",
        "names <kind> <ids>": "Resolve IDs to names (unit/class/item/song)",
        "portrait <unit_id> -o <file>": "Render unit portrait to PNG",
        "export-midi <song_id> -o <file>": "Export song to MIDI",
        "disasm-event <addr>": "Disassemble event script",
        "lint-oam <addr>": "Validate OAM sprite data",
        "patch apply-bin <file>": "Apply BIN patch",
        "disasm -o <file>": "Disassemble ROM",
        "check": "Check backend availability",
        "help": "Show this help",
        "quit/exit": "Exit REPL",
    }

    while True:
        try:
            project_name = ""
            modified = False
            if _session and _session.is_open():
                project_name = os.path.basename(_session.state.rom_path)
                modified = _session.state.modified

            line = skin.get_input(pt_session, project_name=project_name,
                                  modified=modified)

            if not line:
                continue

            if line.lower() in ("quit", "exit", "q"):
                skin.print_goodbye()
                break

            if line.lower() in ("help", "h", "?"):
                skin.help(commands_help)
                continue

            # Parse and dispatch to Click commands
            try:
                args = shlex.split(line, posix=(os.name != "nt"))
                # Prepend global options from session
                global_args = []
                if _json_mode:
                    global_args.append("--json")
                if _session and _session.path:
                    global_args += ["--session-file", str(_session.path)]
                if _session and _session.is_open():
                    global_args += ["--rom", _session.state.rom_path]

                cli.main(global_args + args, standalone_mode=False)
            except SystemExit:
                pass
            except click.UsageError as e:
                skin.error(str(e))
            except Exception as e:
                skin.error(str(e))

        except (EOFError, KeyboardInterrupt):
            skin.print_goodbye()
            break


# ── Entry point ───────────────────────────────────────────────────────

def main():
    cli(obj={})


if __name__ == "__main__":
    main()
