"""Text export/import — wraps FEBuilderGBA.CLI --translate commands."""

import csv
import os
import tempfile
from typing import Optional

from cli_anything.febuildergba.utils.febuildergba_backend import (
    run_cli,
    sanitize_snapshot_path,
    successful_output_size,
)
from cli_anything.febuildergba.core.project import backend_rom_snapshot


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

    file_size = successful_output_size(result, output_path)

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
    with backend_rom_snapshot(rom_path) as snapshot_path:
        args = ["--translate-roundtrip", f"--rom={snapshot_path}"]
        if output_prefix:
            args.append(f"--out={output_prefix}")
        if force_version:
            args.append(f"--force-version={force_version}")

        result = run_cli(args)
        stdout = sanitize_snapshot_path(result.stdout, snapshot_path, rom_path)
        stderr = sanitize_snapshot_path(result.stderr, snapshot_path, rom_path)

    return {
        "lossless": result.returncode == 0,
        "exit_code": result.returncode,
        "stdout": stdout.strip() if stdout else "",
        "stderr": stderr.strip() if result.returncode != 0 else "",
    }


def search_text(rom_path: str, query: str, force_version: str = "") -> dict:
    """Search ROM text by substring.

    Exports all text to a temporary TSV, then searches for the query
    string in the text column.

    Args:
        rom_path: Path to ROM file.
        query: Substring to search for (case-insensitive).
        force_version: Optional forced version.

    Returns:
        Dict with matches (list of {id, text} dicts) and match count.
    """
    # Export text to a temp file
    with tempfile.NamedTemporaryFile(suffix=".tsv", delete=False) as tmp:
        tmp_path = tmp.name

    try:
        with backend_rom_snapshot(rom_path) as snapshot_path:
            args = ["--translate", f"--rom={snapshot_path}", f"--out={tmp_path}"]
            if force_version:
                args.append(f"--force-version={force_version}")

            result = run_cli(args)
            stderr = sanitize_snapshot_path(result.stderr, snapshot_path, rom_path)

        if result.returncode != 0:
            return {
                "query": query,
                "matches": [],
                "match_count": 0,
                "exit_code": result.returncode,
                "stderr": stderr.strip() if stderr else "",
            }

        # Read the exported TSV and search
        matches = []
        with open(tmp_path, "r", encoding="utf-8") as f:
            reader = csv.DictReader(f, delimiter="\t")
            columns = reader.fieldnames or []
            # Find the ID column (first) and text column (second or "text")
            id_col = columns[0] if columns else None
            text_col = None
            for col in columns:
                if col.lower() in ("text", "original", "string"):
                    text_col = col
                    break
            if text_col is None and len(columns) >= 2:
                text_col = columns[1]

            if id_col and text_col:
                query_lower = query.lower()
                for row in reader:
                    text_val = row.get(text_col, "")
                    if query_lower in text_val.lower():
                        matches.append({
                            "id": row.get(id_col, ""),
                            "text": text_val,
                        })

        return {
            "query": query,
            "matches": matches,
            "match_count": len(matches),
            "exit_code": 0,
        }
    finally:
        if os.path.isfile(tmp_path):
            os.unlink(tmp_path)


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
