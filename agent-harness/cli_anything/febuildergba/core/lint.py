"""ROM lint/validation — wraps FEBuilderGBA.CLI --lint."""

from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


def lint_rom(rom_path: str, force_version: str = "") -> dict:
    """Run integrity checks on a ROM.

    Args:
        rom_path: Path to ROM file.
        force_version: Optional forced version.

    Returns:
        Dict with lint results (errors, warnings).
    """
    args = ["--lint", f"--rom={rom_path}"]
    if force_version:
        args.append(f"--force-version={force_version}")

    result = run_cli(args)

    # Parse output for errors and warnings
    lines = result.stdout.strip().splitlines() if result.stdout else []
    errors = [l for l in lines if "ERROR" in l.upper()]
    warnings = [l for l in lines if "WARNING" in l.upper() or "WARN" in l.upper()]
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
        "raw_output": result.stdout.strip(),
    }
