"""Struct data export/import — wraps FEBuilderGBA.CLI --export-data/--import-data."""

import os
import csv
from typing import Optional

from cli_anything.febuildergba.utils.febuildergba_backend import (
    run_cli,
    sanitize_snapshot_path,
)
from cli_anything.febuildergba.core.project import (
    backend_mutating_rom_snapshot,
    backend_rom_snapshot,
    list_tables,
)


def export_table(rom_path: str, table: str, output_path: str,
                 force_version: str = "") -> dict:
    """Export a struct data table to TSV.

    Args:
        rom_path: Path to ROM file.
        table: Table name (e.g., "units", "classes", "items", or "all").
        output_path: Output TSV file path (or directory prefix for "all").
        force_version: Optional forced version.

    Returns:
        Dict with export results.
    """
    table_names = list_tables()
    if table != "all" and table not in table_names:
        raise ValueError(f"Unknown table: {table}. Use 'all' or one of: {', '.join(table_names)}")
    if not isinstance(output_path, str) or not output_path:
        raise ValueError("Output path must not be empty")

    # The backend must never reopen the caller's mutable path after local
    # validation; it receives a header-pinned, length-checked snapshot
    # instead — but only inside MCP's dynamic scope. Outside MCP this is a
    # no-op passthrough to `rom_path` (see `backend_rom_snapshot`).
    with backend_rom_snapshot(rom_path) as snapshot_path:
        args = ["--export-data", f"--rom={snapshot_path}", f"--table={table}",
                f"--out={output_path}"]
        if force_version:
            args.append(f"--force-version={force_version}")

        result = run_cli(args)
        stdout = sanitize_snapshot_path(result.stdout, snapshot_path, rom_path)
        stderr = sanitize_snapshot_path(result.stderr, snapshot_path, rom_path)

    files = []
    if result.returncode == 0:
        # Determine output files (return full paths) only after success so a
        # failed backend cannot make stale pre-existing files look produced.
        if table == "all":
            files = [
                os.path.abspath(f"{output_path}.{table_name}.tsv")
                for table_name in table_names
                if os.path.isfile(f"{output_path}.{table_name}.tsv")
            ]
        elif os.path.isfile(output_path):
            files = [os.path.abspath(output_path)]

    return {
        "table": table,
        "output_files": files,
        "output_path": output_path,
        "exit_code": result.returncode,
        "stdout": stdout.strip() if stdout else "",
        "stderr": stderr.strip() if stderr else "",
    }


def import_table(rom_path: str, table: str, input_path: str,
                 force_version: str = "") -> dict:
    """Import struct data from TSV into ROM.

    Args:
        rom_path: Path to ROM file.
        table: Table name.
        input_path: Input TSV file path.
        force_version: Optional forced version.

    Returns:
        Dict with import results.
    """
    if table not in list_tables():
        raise ValueError(f"Unknown table: {table}")

    if not os.path.isfile(input_path):
        raise FileNotFoundError(f"Input file not found: {input_path}")

    # Mutate a private snapshot; only commit back through the originally
    # opened writable descriptor after the backend reports success — but
    # only inside MCP's dynamic scope. Outside MCP this is a no-op
    # passthrough to `rom_path` (see `backend_mutating_rom_snapshot`).
    with backend_mutating_rom_snapshot(rom_path) as mutator:
        args = ["--import-data", f"--rom={mutator.path}", f"--table={table}",
                f"--in={input_path}"]
        if force_version:
            args.append(f"--force-version={force_version}")

        result = run_cli(args)
        stdout = sanitize_snapshot_path(result.stdout, mutator.path, rom_path)
        stderr = sanitize_snapshot_path(result.stderr, mutator.path, rom_path)
        if result.returncode == 0:
            mutator.commit()

    return {
        "table": table,
        "input_path": input_path,
        "exit_code": result.returncode,
        "stdout": stdout.strip() if stdout else "",
        "stderr": stderr.strip() if result.returncode != 0 else "",
    }


def roundtrip_table(rom_path: str, table: str = "all",
                    force_version: str = "") -> dict:
    """Validate struct data round-trip (export → import → export, compare).

    Args:
        rom_path: Path to ROM file.
        table: Table name or "all".
        force_version: Optional forced version.

    Returns:
        Dict with roundtrip validation results.
    """
    with backend_rom_snapshot(rom_path) as snapshot_path:
        args = ["--data-roundtrip", f"--rom={snapshot_path}", f"--table={table}"]
        if force_version:
            args.append(f"--force-version={force_version}")

        result = run_cli(args)
        stdout = sanitize_snapshot_path(result.stdout, snapshot_path, rom_path)
        stderr = sanitize_snapshot_path(result.stderr, snapshot_path, rom_path)

    return {
        "table": table,
        "lossless": result.returncode == 0,
        "exit_code": result.returncode,
        "stdout": stdout.strip() if stdout else "",
        "stderr": stderr.strip() if result.returncode != 0 else "",
    }


def diff_tsv(path_a: str, path_b: str) -> dict:
    """Compare two TSV exports and report differences.

    Pure Python — reads two TSV files and compares by first column (ID).

    Args:
        path_a: Path to the first TSV file.
        path_b: Path to the second TSV file.

    Returns:
        Dict with added_rows, removed_rows, changed_rows, unchanged_count.
    """
    if not os.path.isfile(path_a):
        raise FileNotFoundError(f"File not found: {path_a}")
    if not os.path.isfile(path_b):
        raise FileNotFoundError(f"File not found: {path_b}")

    rows_a = read_tsv(path_a)
    rows_b = read_tsv(path_b)

    if not rows_a and not rows_b:
        return {
            "added_rows": [],
            "removed_rows": [],
            "changed_rows": [],
            "unchanged_count": 0,
        }

    # Use first column as the key
    key_col_a = list(rows_a[0].keys())[0] if rows_a else None
    key_col_b = list(rows_b[0].keys())[0] if rows_b else None

    dict_a = {row[key_col_a]: row for row in rows_a} if key_col_a else {}
    dict_b = {row[key_col_b]: row for row in rows_b} if key_col_b else {}

    keys_a = set(dict_a.keys())
    keys_b = set(dict_b.keys())

    added_keys = sorted(keys_b - keys_a)
    removed_keys = sorted(keys_a - keys_b)
    common_keys = keys_a & keys_b

    added_rows = [dict_b[k] for k in added_keys]
    removed_rows = [dict_a[k] for k in removed_keys]

    changed_rows = []
    unchanged_count = 0

    for k in sorted(common_keys):
        row_a = dict_a[k]
        row_b = dict_b[k]
        if row_a != row_b:
            diffs = {}
            all_fields = set(row_a.keys()) | set(row_b.keys())
            for field in sorted(all_fields):
                val_a = row_a.get(field, "")
                val_b = row_b.get(field, "")
                if val_a != val_b:
                    diffs[field] = {"old": val_a, "new": val_b}
            changed_rows.append({"id": k, "fields": diffs})
        else:
            unchanged_count += 1

    return {
        "added_rows": added_rows,
        "removed_rows": removed_rows,
        "changed_rows": changed_rows,
        "unchanged_count": unchanged_count,
    }


def lookup_entry(tsv_path: str, entry_id: str) -> dict:
    """Look up a single entry by ID from an exported TSV.

    Pure Python — reads TSV and finds the row where the first column
    matches the given entry_id.

    Args:
        tsv_path: Path to TSV file.
        entry_id: Value to match in the first column.

    Returns:
        Dict with found=True/False and the row data if found.
    """
    if not os.path.isfile(tsv_path):
        raise FileNotFoundError(f"File not found: {tsv_path}")

    rows = read_tsv(tsv_path)
    if not rows:
        return {"found": False, "entry_id": entry_id, "row": None}

    key_col = list(rows[0].keys())[0]
    for row in rows:
        if row[key_col] == entry_id:
            return {"found": True, "entry_id": entry_id, "row": row}

    return {"found": False, "entry_id": entry_id, "row": None}


def read_tsv(path: str) -> list[dict]:
    """Read a TSV file and return rows as dicts.

    Args:
        path: Path to TSV file.

    Returns:
        List of row dicts with column headers as keys.
    """
    rows = []
    with open(path, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f, delimiter="\t")
        for row in reader:
            rows.append(dict(row))
    return rows


def tsv_summary(path: str) -> dict:
    """Get summary info about a TSV export file.

    Returns:
        Dict with row count, column names, first few rows.
    """
    rows = read_tsv(path)
    return {
        "path": path,
        "row_count": len(rows),
        "columns": list(rows[0].keys()) if rows else [],
        "preview": rows[:5],
    }
