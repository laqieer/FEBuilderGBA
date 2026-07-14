"""Export pipeline — UPS patches, disassembly, rebuild, image, music, maps."""

import os
from cli_anything.febuildergba.utils.febuildergba_backend import (
    run_cli,
    sanitize_snapshot_path,
    successful_output_size,
)
from cli_anything.febuildergba.core.project import backend_rom_snapshot


def create_ups(rom_path: str, output_path: str,
               from_rom: str = "") -> dict:
    """Create a UPS patch from ROM differences.

    Args:
        rom_path: Path to modified ROM.
        output_path: Output UPS patch file path.
        from_rom: Optional original ROM for diff (if not embedded).

    Returns:
        Dict with patch creation results.
    """
    args = [f"--makeups={output_path}", f"--rom={rom_path}"]
    if from_rom:
        args.append(f"--fromrom={from_rom}")

    result = run_cli(args)

    file_size = successful_output_size(result, output_path)

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def apply_ups(rom_path: str, patch_path: str,
              output_path: str = "") -> dict:
    """Apply a UPS patch to a ROM.

    The backend contract is: --applyups=<output> --rom=<original> --patch=<patch.ups>

    Args:
        rom_path: Path to original ROM file.
        patch_path: Path to UPS patch file.
        output_path: Output ROM path (default: <rom_path>.patched.gba).

    Returns:
        Dict with patch application results.
    """
    if not output_path:
        base, ext = os.path.splitext(rom_path)
        output_path = f"{base}.patched{ext}"

    args = [f"--applyups={output_path}", f"--rom={rom_path}",
            f"--patch={patch_path}"]

    result = run_cli(args)

    return {
        "rom_path": rom_path,
        "patch_path": patch_path,
        "output_path": output_path,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def disassemble(rom_path: str, output_path: str,
                force_version: str = "") -> dict:
    """Disassemble ROM to text file.

    Args:
        rom_path: Path to ROM file.
        output_path: Output disassembly file path.
        force_version: Optional forced version.

    Returns:
        Dict with disassembly results.
    """
    args = [f"--disasm={output_path}", f"--rom={rom_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    file_size = successful_output_size(result, output_path)

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def rebuild(rom_path: str, from_rom: str,
            force_version: str = "") -> dict:
    """Rebuild/defragment a ROM.

    Args:
        rom_path: Path to ROM to rebuild.
        from_rom: Path to original clean ROM.
        force_version: Optional forced version.

    Returns:
        Dict with rebuild results.
    """
    args = ["--rebuild", f"--rom={rom_path}", f"--fromrom={from_rom}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "rom_path": rom_path,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def decrease_color(input_path: str, output_path: str,
                   palette_no: int = 16,
                   no_scale: bool = False,
                   no_reserve_1st: bool = False,
                   ignore_tsa: bool = False) -> dict:
    """Quantize an image palette to a bounded color count for GBA.

    Args:
        input_path: Input image file.
        output_path: Output image file.
        palette_no: Maximum color count (default 16).
        no_scale: Don't scale to GBA 5-bit color.
        no_reserve_1st: Don't reserve palette slot 0 for transparency.
        ignore_tsa: Ignore TSA 8x8 tile constraints.

    Returns:
        Dict with quantization results.
    """
    args = ["--decreasecolor", f"--in={input_path}", f"--out={output_path}"]
    if palette_no:
        args.append(f"--paletteno={palette_no}")
    if no_scale:
        args.append("--noScale")
    if no_reserve_1st:
        args.append("--noReserve1stColor")
    if ignore_tsa:
        args.append("--ignoreTSA")

    result = run_cli(args)

    file_size = successful_output_size(result, output_path)

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def pointer_calc(rom_path: str, target: str, address: str,
                 force_version: str = "") -> dict:
    """Search for pointer references in ROM.

    Args:
        rom_path: Path to ROM file.
        target: Target value to search for.
        address: Address to search at.
        force_version: Optional forced version.

    Returns:
        Dict with pointer search results.
    """
    args = ["--pointercalc", f"--rom={rom_path}",
            f"--target={target}", f"--address={address}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "target": target,
        "address": address,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def song_exchange(rom_path: str, from_rom: str,
                  from_song: str, to_song: str) -> dict:
    """Copy a song from one ROM to another.

    Song IDs are passed as hex strings (e.g., "1A", "0x1A") since
    the backend parses them with NumberStyles.HexNumber.

    Args:
        rom_path: Destination ROM.
        from_rom: Source ROM.
        from_song: Source song ID (hex string, e.g. "1A" or "0x1A").
        to_song: Destination song ID (hex string).

    Returns:
        Dict with song exchange results.
    """
    args = ["--songexchange", f"--rom={rom_path}", f"--fromrom={from_rom}",
            f"--fromsong={from_song}", f"--tosong={to_song}"]

    result = run_cli(args)

    return {
        "from_song": from_song,
        "to_song": to_song,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def convert_map_image(input_path: str, out_img: str, out_tsa: str) -> dict:
    """Convert an image to map tiles.

    Args:
        input_path: Input image file.
        out_img: Output tile image file.
        out_tsa: Output TSA data file.

    Returns:
        Dict with conversion results.
    """
    args = ["--convertmap1picture", f"--in={input_path}",
            f"--outImg={out_img}", f"--outTSA={out_tsa}"]

    result = run_cli(args)

    return {
        "out_img": out_img,
        "out_tsa": out_tsa,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def resolve_names(rom_path: str, kind: str, ids: list[int],
                  force_version: str = "") -> dict:
    """Resolve entity IDs to human-readable names.

    Args:
        rom_path: Path to ROM file.
        kind: Entity type (unit, class, item, song).
        ids: List of entity IDs.
        force_version: Optional forced version.

    Returns:
        Dict with resolved names.
    """
    ids_str = ",".join(str(i) for i in ids)
    with backend_rom_snapshot(rom_path) as snapshot_path:
        args = ["--resolve-names", f"--rom={snapshot_path}",
                f"--kind={kind}", f"--ids={ids_str}"]
        if force_version:
            args.append(f"--force-version={force_version}")

        result = run_cli(args)
        stdout = sanitize_snapshot_path(result.stdout, snapshot_path, rom_path)
        stderr = sanitize_snapshot_path(result.stderr, snapshot_path, rom_path)

    names = {}
    if stdout:
        for line in stdout.strip().splitlines():
            parts = line.split("\t", 1)
            if len(parts) == 2:
                names[parts[0]] = parts[1]

    return {
        "kind": kind,
        "names": names,
        "count": len(names),
        "exit_code": result.returncode,
        "stdout": stdout.strip() if stdout else "",
        "stderr": stderr.strip() if stderr else "",
    }


def render_portrait(rom_path: str, unit_id: int, output_path: str,
                    force_version: str = "") -> dict:
    """Render a unit portrait to PNG.

    Args:
        rom_path: Path to ROM file.
        unit_id: Unit index number.
        output_path: Output PNG file path.
        force_version: Optional forced version.

    Returns:
        Dict with rendering results.
    """
    args = ["--render-portrait", f"--rom={rom_path}",
            f"--unit-id={unit_id}", f"--out={output_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    file_size = successful_output_size(result, output_path)

    return {
        "unit_id": unit_id,
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def export_midi(rom_path: str, song_id: str, output_path: str,
                force_version: str = "") -> dict:
    """Export a GBA song to MIDI file.

    Args:
        rom_path: Path to ROM file.
        song_id: Song ID in hex (e.g., "1A" or "0x1A").
        output_path: Output MIDI file path.
        force_version: Optional forced version.

    Returns:
        Dict with export results.
    """
    args = ["--export-midi", f"--rom={rom_path}",
            f"--song-id={song_id}", f"--out={output_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    file_size = successful_output_size(result, output_path)

    return {
        "song_id": song_id,
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def disasm_event(rom_path: str, addr: str, script_type: str = "event",
                 output_path: str = "", force_version: str = "") -> dict:
    """Disassemble event script bytecode.

    Args:
        rom_path: Path to ROM file.
        addr: Start address in hex (e.g., "0x9A0000").
        script_type: Script type (event, procs, ai).
        output_path: Output file (empty = return in stdout).
        force_version: Optional forced version.

    Returns:
        Dict with disassembly results.
    """
    args = ["--disasm-event", f"--rom={rom_path}", f"--addr={addr}",
            f"--type={script_type}"]
    if output_path:
        args.append(f"--out={output_path}")
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "addr": addr,
        "script_type": script_type,
        "output_path": output_path,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def lint_oam(rom_path: str, addr: str, length: int = 0,
             force_version: str = "") -> dict:
    """Validate battle animation OAM sprite data.

    Args:
        rom_path: Path to ROM file.
        addr: OAM data address in hex.
        length: Bytes to scan (0 = auto).
        force_version: Optional forced version.

    Returns:
        Dict with lint results.
    """
    args = ["--lint-oam", f"--rom={rom_path}", f"--addr={addr}"]
    if length:
        args.append(f"--length={length}")
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    lines = result.stdout.strip().splitlines() if result.stdout else []
    issues = [l.strip() for l in lines if l.strip() and not l.startswith("OAM lint:")]
    clean = result.returncode == 0

    return {
        "addr": addr,
        "clean": clean,
        "issue_count": len(issues),
        "issues": issues,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def apply_patch(rom_path: str, patch_file: str,
                force_version: str = "") -> dict:
    """Apply a BIN patch to a ROM.

    Args:
        rom_path: Path to ROM file.
        patch_file: Path to PATCH_*.txt file.
        force_version: Optional forced version.

    Returns:
        Dict with patch application results.
    """
    args = ["--apply-patch", f"--rom={rom_path}", f"--patch-file={patch_file}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "rom_path": rom_path,
        "patch_file": patch_file,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }
