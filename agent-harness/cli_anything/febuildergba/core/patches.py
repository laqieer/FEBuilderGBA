"""Patch discovery — scans config/patch2/{version}/ for available patches."""

import os


def list_patches(config_dir: str, version: str) -> dict:
    """List available patches for a given ROM version.

    Pure Python — scans the config/patch2/{version}/ directory for
    PATCH_*.txt files and extracts NAME and INFO/COMMENT metadata.

    Args:
        config_dir: Path to the config/ directory (repo root config/).
        version: ROM version string (FE6, FE7J, FE7U, FE8J, FE8U).

    Returns:
        Dict with patches list and count.
    """
    patch_dir = os.path.abspath(os.path.join(config_dir, "patch2", version))

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
        info = ""

        try:
            with open(filepath, "r", encoding="utf-8", errors="replace") as f:
                for line in f:
                    line = line.strip()
                    if line.startswith("NAME="):
                        name = line[len("NAME="):]
                    elif line.startswith("INFO=") and not info:
                        info = line[len("INFO="):]
                    elif line.startswith("INFO.en=") and not info:
                        info = line[len("INFO.en="):]
                    elif line.startswith("COMMENT=") and not info:
                        info = line[len("COMMENT="):]
                    if name and info:
                        break
        except Exception:
            continue

        patches.append({
            "file": filename,
            "name": name or filename,
            "info": info,
        })

    return {
        "version": version,
        "patch_dir": patch_dir,
        "patches": patches,
        "count": len(patches),
    }
