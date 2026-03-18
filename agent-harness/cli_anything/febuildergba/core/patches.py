"""Patch discovery — scans config/patch2/{version}/ for available patches."""

import os
import re


def list_patches(config_dir: str, version: str) -> dict:
    """List available patches for a given ROM version.

    Pure Python — scans the config/patch2/{version}/ directory for
    PATCH_*.txt files and extracts NAME and COMMENT metadata.

    Args:
        config_dir: Path to the config/ directory (repo root config/).
        version: ROM version string (FE6, FE7J, FE7U, FE8J, FE8U).

    Returns:
        Dict with patches list and count.
    """
    patch_dir = os.path.join(config_dir, "patch2", version)

    if not os.path.isdir(patch_dir):
        return {
            "version": version,
            "patch_dir": patch_dir,
            "patches": [],
            "count": 0,
            "error": f"Patch directory not found: {patch_dir}",
        }

    patches = []
    for filename in sorted(os.listdir(patch_dir)):
        if not filename.startswith("PATCH_") or not filename.endswith(".txt"):
            continue

        filepath = os.path.join(patch_dir, filename)
        name = ""
        comment = ""

        try:
            with open(filepath, "r", encoding="utf-8", errors="replace") as f:
                for line in f:
                    line = line.strip()
                    if line.startswith("NAME="):
                        name = line[len("NAME="):]
                    elif line.startswith("COMMENT="):
                        comment = line[len("COMMENT="):]
                    # Stop early once we have both
                    if name and comment:
                        break
        except Exception:
            continue

        patches.append({
            "file": filename,
            "name": name or filename,
            "comment": comment,
        })

    return {
        "version": version,
        "patch_dir": os.path.abspath(patch_dir),
        "patches": patches,
        "count": len(patches),
    }
