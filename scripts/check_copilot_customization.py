#!/usr/bin/env python3
"""Validate repository-owned Copilot customization surfaces."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path


PROJECT_SKILLS = {"daily-maintenance", "dev-flow"}
SUPERPOWERS_SKILLS = {
    "brainstorming",
    "dispatching-parallel-agents",
    "executing-plans",
    "finishing-a-development-branch",
    "receiving-code-review",
    "requesting-code-review",
    "subagent-driven-development",
    "systematic-debugging",
    "test-driven-development",
    "using-git-worktrees",
    "using-superpowers",
    "verification-before-completion",
    "writing-plans",
    "writing-skills",
}

MAINTAINED_ENTRYPOINTS = (
    ".github/copilot-instructions.md",
    ".github/skills/daily-maintenance/SKILL.md",
    ".github/skills/dev-flow/SKILL.md",
    "CONTRIBUTING.md",
    "DEVELOPMENT-WORKFLOW.md",
    "README.md",
    "docs/CORE-SEAMS.md",
    "docs/GUI-STRATEGY.md",
    "FEBuilderGBA.Avalonia/Views/ProcsScriptView.axaml.cs",
)

STALE_CLAUDE_PATTERN = re.compile(
    r"CLAUDE\.md|Claude Code|\.claude[/\\]skills",
)


def _skill_name(path: Path) -> str | None:
    match = re.search(
        r"(?m)^name:\s*([a-z0-9-]+)\s*$",
        path.read_text(encoding="utf-8"),
    )
    return match.group(1) if match else None


def validate_repository(root: Path) -> list[str]:
    errors: list[str] = []

    legacy = root / ".claude" / "skills"
    if legacy.exists() and any(legacy.rglob("SKILL.md")):
        errors.append("legacy .claude/skills must be removed")

    skill_root = root / ".github" / "skills"
    found: set[str] = set()
    for expected in sorted(PROJECT_SKILLS):
        path = skill_root / expected / "SKILL.md"
        if not path.is_file():
            errors.append(f"missing project skill {expected}")
            continue
        name = _skill_name(path)
        if name != expected:
            errors.append(f"project skill {expected} has invalid frontmatter name")
        content = path.read_text(encoding="utf-8")
        if "scripts/classify-review-risk.py" in content:
            errors.append(
                f"project skill {expected} references the wrong classifier path"
            )
        if path.stat().st_size >= 8192:
            errors.append(f"project skill {expected} exceeds 8192 bytes")
        found.add(expected)

    settings_path = root / ".github" / "copilot" / "settings.json"
    try:
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
    except (FileNotFoundError, json.JSONDecodeError) as error:
        errors.append(f"invalid Copilot settings: {error}")
        settings = {}

    plugins = settings.get("enabledPlugins", {})
    for plugin in ("superpowers@superpowers-marketplace", "copilot-image-gen"):
        if plugins.get(plugin) is not False:
            errors.append(f"enabledPlugins must disable {plugin}")

    disabled = set(settings.get("disabledSkills", []))
    for skill in sorted({"pua", *SUPERPOWERS_SKILLS}):
        if skill not in disabled:
            errors.append(f"disabledSkills is missing {skill}")

    if "copilot-image-gen" not in set(settings.get("disabledMcpServers", [])):
        errors.append("disabledMcpServers is missing copilot-image-gen")

    if (root / ".mcp.json").exists():
        tracked = subprocess.run(
            ["git", "ls-files", "--error-unmatch", ".mcp.json"],
            cwd=root,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if tracked.returncode == 0:
            errors.append("tracked .mcp.json must be removed")

    example_path = root / ".mcp.example.json"
    try:
        example = json.loads(example_path.read_text(encoding="utf-8"))
        servers = set(example.get("mcpServers", {}))
        if servers != {"febuildergba-computer-use", "febuildergba-cli"}:
            errors.append(".mcp.example.json must define both FEBuilder MCP servers")
    except (FileNotFoundError, json.JSONDecodeError) as error:
        errors.append(f"invalid .mcp.example.json: {error}")

    try:
        ignore_lines = {
            line.strip()
            for line in (root / ".gitignore").read_text(encoding="utf-8").splitlines()
        }
        if "/.mcp.json" not in ignore_lines:
            errors.append(".gitignore must ignore /.mcp.json")
    except FileNotFoundError:
        errors.append(".gitignore is missing")

    for relative in MAINTAINED_ENTRYPOINTS:
        path = root / relative
        if path.is_file() and STALE_CLAUDE_PATTERN.search(
            path.read_text(encoding="utf-8")
        ):
            errors.append(f"stale Claude reference in {relative}")

    return errors


def main(argv: list[str]) -> int:
    root = Path(argv[1]).resolve() if len(argv) > 1 else Path(__file__).resolve().parent.parent
    errors = validate_repository(root)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print("Copilot customization validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
