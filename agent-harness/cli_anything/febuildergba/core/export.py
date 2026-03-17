"""Export pipeline — UPS patches, disassembly, rebuild, image, music, maps."""

import os
from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


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

    file_size = os.path.getsize(output_path) if os.path.isfile(output_path) else 0

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
    }


def apply_ups(rom_path: str, patch_path: str) -> dict:
    """Apply a UPS patch to a ROM.

    Args:
        rom_path: Path to ROM file.
        patch_path: Path to UPS patch file.

    Returns:
        Dict with patch application results.
    """
    args = [f"--applyups={patch_path}", f"--rom={rom_path}"]

    result = run_cli(args)

    return {
        "rom_path": rom_path,
        "patch_path": patch_path,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
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

    file_size = os.path.getsize(output_path) if os.path.isfile(output_path) else 0

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
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
    }


def decrease_color(input_path: str, output_path: str,
                   palette_no: int = 0,
                   no_scale: bool = False,
                   no_reserve_1st: bool = False,
                   ignore_tsa: bool = False) -> dict:
    """Quantize an image palette for GBA (16 colors).

    Args:
        input_path: Input image file.
        output_path: Output image file.
        palette_no: Palette number (default 0).
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

    file_size = os.path.getsize(output_path) if os.path.isfile(output_path) else 0

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
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
    }


def song_exchange(rom_path: str, from_rom: str,
                  from_song: int, to_song: int) -> dict:
    """Copy a song from one ROM to another.

    Args:
        rom_path: Destination ROM.
        from_rom: Source ROM.
        from_song: Source song ID.
        to_song: Destination song ID.

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
    }
