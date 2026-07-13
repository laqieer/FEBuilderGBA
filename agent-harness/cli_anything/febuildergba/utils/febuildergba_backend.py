"""Backend module: wraps the real FEBuilderGBA.CLI executable.

This module finds and invokes the actual FEBuilderGBA.CLI binary.
The CLI harness does NOT reimplement ROM manipulation — it calls
the real .NET application for all operations.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Optional


def find_febuildergba_cli() -> list[str]:
    """Find the FEBuilderGBA.CLI executable.

    Search order:
    1. FEBUILDERGBA_CLI env var (explicit path)
    2. Published exe in repo agent-harness/../FEBuilderGBA.CLI/bin/
    3. 'dotnet run' via project file in the repo

    Returns:
        Command list to invoke the CLI (e.g., ["dotnet", "run", ...] or ["/path/to/exe"]).

    Raises:
        RuntimeError: If FEBuilderGBA.CLI cannot be found.
    """
    # 1. Explicit env var (prefer FEBUILDERGBA_CLI_EXE if set)
    env_path = os.environ.get("FEBUILDERGBA_CLI_EXE") or os.environ.get("FEBUILDERGBA_CLI")
    if env_path:
        if os.path.isfile(env_path):
            return [env_path]
        raise RuntimeError(
            f"FEBUILDERGBA_CLI_EXE/FEBUILDERGBA_CLI={env_path} but file does not exist."
        )

    # 2. Walk up from this file to find the repo root containing FEBuilderGBA.CLI/
    pkg_dir = Path(__file__).resolve().parent
    for _ in range(8):
        pkg_dir = pkg_dir.parent
        if (pkg_dir / "FEBuilderGBA.CLI").is_dir():
            break
    else:
        # Fallback: try __file__ without resolve() (handles Cygwin/editable installs)
        pkg_dir = Path(__file__).parent
        for _ in range(8):
            pkg_dir = pkg_dir.parent
            if (pkg_dir / "FEBuilderGBA.CLI").is_dir():
                break

    # Check for published exe
    for config in ["Release", "Debug"]:
        for rid in ["win-x64", "linux-x64", "osx-arm64"]:
            exe_path = pkg_dir / "FEBuilderGBA.CLI" / "bin" / config / "net9.0" / rid / "publish"
            for name in ["FEBuilderGBA.CLI.exe", "FEBuilderGBA.CLI"]:
                candidate = exe_path / name
                if candidate.is_file():
                    return [str(candidate)]

    # Check for build output (not published)
    for config in ["Release", "Debug"]:
        for arch in ["net9.0", "net9.0-windows"]:
            exe_dir = pkg_dir / "FEBuilderGBA.CLI" / "bin" / config / arch
            for name in ["FEBuilderGBA.CLI.exe", "FEBuilderGBA.CLI"]:
                candidate = exe_dir / name
                if candidate.is_file():
                    return [str(candidate)]

    # 3. Use 'dotnet run' with the project file
    csproj = pkg_dir / "FEBuilderGBA.CLI" / "FEBuilderGBA.CLI.csproj"
    if csproj.is_file():
        dotnet = shutil.which("dotnet")
        if dotnet:
            return [dotnet, "run", "--project", str(csproj), "--"]

    raise RuntimeError(
        "FEBuilderGBA.CLI not found. Ensure it is built:\n"
        "  dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj\n"
        "Or set FEBUILDERGBA_CLI=/path/to/FEBuilderGBA.CLI executable"
    )


def run_cli(args: list[str], capture: bool = True,
            timeout: int = 300) -> subprocess.CompletedProcess:
    """Run a FEBuilderGBA.CLI command.

    Args:
        args: CLI arguments (e.g., ["--rom", "rom.gba", "--lint"]).
        capture: Whether to capture stdout/stderr.
        timeout: Timeout in seconds.

    Returns:
        CompletedProcess with stdout/stderr.

    Raises:
        RuntimeError: If CLI not found or execution fails.
    """
    cmd = find_febuildergba_cli() + args
    try:
        result = subprocess.run(
            cmd,
            capture_output=capture,
            text=True,
            timeout=timeout,
        )
        return result
    except FileNotFoundError:
        raise RuntimeError(
            f"Failed to run: {' '.join(cmd)}\n"
            "Is .NET 9.0 SDK installed? https://dotnet.microsoft.com/download"
        )
    except subprocess.TimeoutExpired:
        raise RuntimeError(
            f"Command timed out after {timeout}s: {' '.join(cmd)}"
        )


def get_version() -> str:
    """Get FEBuilderGBA version string."""
    result = run_cli(["--version"])
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        suffix = f": {detail[:4096]}" if detail else ""
        raise RuntimeError(
            f"FEBuilderGBA.CLI version check failed with exit code "
            f"{result.returncode}{suffix}"
        )
    version = result.stdout.strip()
    if not version:
        raise RuntimeError("FEBuilderGBA.CLI version check returned no version text")
    return version


def check_backend() -> dict:
    """Check if the FEBuilderGBA.CLI backend is available.

    Returns:
        Dict with status info.
    """
    try:
        cmd = find_febuildergba_cli()
        version = get_version()
        return {
            "available": True,
            "command": cmd,
            "version": version,
        }
    except RuntimeError as e:
        return {
            "available": False,
            "error": str(e),
        }
