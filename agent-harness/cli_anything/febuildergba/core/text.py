"""Text export/import — wraps FEBuilderGBA.CLI --translate commands."""

import os
from typing import Optional

from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


def export_text(rom_path: str, output_path: str,
                force_version: str = "") -> dict:
    """Export all ROM text to TSV.

    Args:
        rom_path: Path to ROM file.
        output_path: Output TSV file path.
        force_version: Optional forced version.

    Returns:
        Dict with export results.
    """
    args = ["--translate", f"--rom={rom_path}", f"--out={output_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    file_size = os.path.getsize(output_path) if os.path.isfile(output_path) else 0

    return {
        "output_path": output_path,
        "file_size": file_size,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }


def import_text(rom_path: str, input_path: str,
                force_version: str = "") -> dict:
    """Import text from TSV into ROM (Huffman encode + write-back).

    Args:
        rom_path: Path to ROM file.
        input_path: Input TSV file path.
        force_version: Optional forced version.

    Returns:
        Dict with import results.
    """
    if not os.path.isfile(input_path):
        raise FileNotFoundError(f"Input file not found: {input_path}")

    args = ["--translate", f"--rom={rom_path}", f"--in={input_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "input_path": input_path,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.returncode != 0 else "",
    }


def roundtrip_text(rom_path: str, output_prefix: str = "",
                   force_version: str = "") -> dict:
    """Validate text export/import round-trip.

    Args:
        rom_path: Path to ROM file.
        output_prefix: Optional output prefix for diff files.
        force_version: Optional forced version.

    Returns:
        Dict with roundtrip results (lossless=True means exit 0).
    """
    args = ["--translate-roundtrip", f"--rom={rom_path}"]
    if output_prefix:
        args.append(f"--out={output_prefix}")
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "lossless": result.returncode == 0,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.returncode != 0 else "",
    }


def batch_translate(rom_path: str, export_path: str, import_path: str,
                    force_version: str = "") -> dict:
    """Batch text export + import in one operation.

    Args:
        rom_path: Path to ROM file.
        export_path: Output TSV for export.
        import_path: Input TSV for import.
        force_version: Optional forced version.

    Returns:
        Dict with batch operation results.
    """
    args = ["--translate_batch", f"--rom={rom_path}",
            f"--out={export_path}", f"--in={import_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    return {
        "export_path": export_path,
        "import_path": import_path,
        "exit_code": result.returncode,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip() if result.stderr else "",
    }
