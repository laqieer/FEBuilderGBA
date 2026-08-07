#!/usr/bin/env python3
"""Classify a changed-path set for the Copilot review gate."""

from __future__ import annotations

import argparse
import sys
from pathlib import PurePosixPath
from typing import Iterable


HIGH_EXACT = {
    ".gitmodules",
    ".mcp.example.json",
    ".pre-commit-config.yaml",
    "CONTRIBUTING.md",
    "DEVELOPMENT-WORKFLOW.md",
    "FEBuilderGBA.Core/CoreState.cs",
    "FEBuilderGBA.Core/DataExpansionCore.cs",
    "FEBuilderGBA.Core/Rom.cs",
    "FEBuilderGBA.Core/Undo.cs",
    "FEBuilderGBA.sln",
    "commitlint.config.mjs",
    "docs/DEPLOYMENT.md",
    "docs/ENGINEERING-NOTES.md",
    "docs/RELEASE.md",
    "docs/SECRET-SCANNING.md",
    "global.json",
    "release.ps1",
}

HIGH_PREFIXES = (
    ".github/",
    "scripts/",
)

HIGH_SUFFIXES = (
    ".csproj",
    ".props",
    ".targets",
    "packages.lock.json",
    "package-lock.json",
    "requirements.txt",
    "pyproject.toml",
)

HIGH_MANIFEST_NAMES = {
    "Cargo.lock",
    "Cargo.toml",
    "Directory.Packages.props",
    "NuGet.config",
    "Pipfile",
    "Pipfile.lock",
    "go.mod",
    "go.sum",
    "package.json",
    "pnpm-lock.yaml",
    "poetry.lock",
    "setup.cfg",
    "setup.py",
    "yarn.lock",
}

TRUSTED_ASSOCIATIONS = {"OWNER", "MEMBER", "COLLABORATOR"}

SUBMODULE_PATHS = {
    "config/patch2",
    "resources/FE-Repo",
    "resources/FE-Repo-Music-No-Preview",
    "resources/fe-info",
    "tools/ColorzCore",
    "tools/Event-Assembler",
}

LOW_EXACT = {
    "CHANGELOG.md",
    "CONTRIBUTING.md",
    "README.md",
    "THIRD-PARTY-NOTICES.md",
}


def _normalize(path: str) -> str | None:
    value = path.strip().replace("\\", "/")
    if (
        not value
        or value.startswith("/")
        or (
            len(value) >= 3
            and value[0].isalpha()
            and value[1:3] == ":/"
        )
    ):
        return None
    parts = PurePosixPath(value).parts
    if any(part in {"", ".", ".."} for part in parts):
        return None
    return "/".join(parts)


def _is_high(path: str) -> bool:
    if path in HIGH_EXACT:
        return True
    if path in SUBMODULE_PATHS:
        return True
    if path.startswith(HIGH_PREFIXES):
        return True
    if path.endswith(HIGH_SUFFIXES):
        return True
    name = PurePosixPath(path).name
    if name in HIGH_MANIFEST_NAMES:
        return True
    if name.startswith("requirements") and name.endswith(".txt"):
        return True
    return (
        path.startswith("FEBuilderGBA.Core/")
        and name.startswith("ROMFE")
        and name.endswith(".cs")
    )


def _is_low(path: str) -> bool:
    return path in LOW_EXACT or (
        path.startswith("docs/") and path.endswith((".md", ".txt"))
    )


def classify_paths(
    paths: Iterable[str],
    *,
    cross_repository: bool = False,
    author_association: str | None = None,
) -> str:
    if cross_repository:
        return "high"
    if (
        author_association is not None
        and author_association.upper() not in TRUSTED_ASSOCIATIONS
    ):
        return "high"

    nonblank = [path for path in paths if path.strip()]
    normalized = [_normalize(path) for path in nonblank]
    if not normalized or any(path is None for path in normalized):
        return "high"

    values = [path for path in normalized if path is not None]
    if any(_is_high(path) for path in values):
        return "high"
    if all(_is_low(path) for path in values):
        return "low"
    return "normal"


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--cross-repository",
        choices=("true", "false"),
        default="false",
    )
    parser.add_argument("--author-association")
    parser.add_argument("paths", nargs="*")
    args = parser.parse_args(argv[1:])
    paths = args.paths or [line.rstrip("\n") for line in sys.stdin]
    print(
        classify_paths(
            paths,
            cross_repository=args.cross_repository == "true",
            author_association=args.author_association,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
