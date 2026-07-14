"""ROM lint/validation — wraps FEBuilderGBA.CLI --lint."""

import re

from cli_anything.febuildergba.core.project import validated_rom_snapshot
from cli_anything.febuildergba.utils.febuildergba_backend import (
    run_cli,
    sanitize_snapshot_path,
)


_ERROR_LINE = re.compile(r"^\s*\[ERROR\](?:\s|$)", re.IGNORECASE)
_WARNING_LINE = re.compile(r"^\s*\[WARNING\](?:\s|$)", re.IGNORECASE)


def lint_rom(rom_path: str, force_version: str = "") -> dict:
    """Run integrity checks on a ROM.

    Args:
        rom_path: Path to ROM file.
        force_version: Optional forced version.

    Returns:
        Dict with lint results (errors, warnings).
    """
    # The backend must never reopen the caller's mutable path after local
    # validation; it receives a header-pinned, length-checked snapshot instead.
    with validated_rom_snapshot(rom_path) as snapshot_path:
        args = ["--lint", f"--rom={snapshot_path}"]
        if force_version:
            args.append(f"--force-version={force_version}")
        result = run_cli(args)
        stdout = sanitize_snapshot_path(result.stdout, snapshot_path, rom_path)

    # The CLI emits explicit severity markers for findings. Do not classify
    # summary text such as "Lint: No errors found." by incidental words.
    lines = stdout.strip().splitlines() if stdout else []
    errors = [line for line in lines if _ERROR_LINE.match(line)]
    warnings = [line for line in lines if _WARNING_LINE.match(line)]
    info_lines = [l for l in lines if l and l not in errors and l not in warnings]

    return {
        "rom_path": rom_path,
        "clean": result.returncode == 0 and len(errors) == 0,
        "error_count": len(errors),
        "warning_count": len(warnings),
        "errors": errors,
        "warnings": warnings,
        "info": info_lines,
        "exit_code": result.returncode,
        "raw_output": stdout.strip() if stdout else "",
    }
