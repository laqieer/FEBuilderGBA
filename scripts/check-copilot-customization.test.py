import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from check_copilot_customization import validate_repository


SKILL = """---
name: {name}
description: test skill
---

Test.
"""

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


def make_valid_repository(root: Path) -> None:
    skills = root / ".github" / "skills"
    for name in ("dev-flow", "daily-maintenance"):
        directory = skills / name
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "SKILL.md").write_text(
            SKILL.format(name=name),
            encoding="utf-8",
        )

    settings = {
        "contextTier": "default",
        "enabledPlugins": {
            "superpowers@superpowers-marketplace": False,
            "copilot-image-gen": False,
        },
        "disabledSkills": sorted({"pua", *SUPERPOWERS_SKILLS}),
        "disabledMcpServers": ["copilot-image-gen"],
    }
    settings_path = root / ".github" / "copilot" / "settings.json"
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings), encoding="utf-8")

    (root / ".mcp.example.json").write_text(
        '{"mcpServers":{"febuildergba-computer-use":{},"febuildergba-cli":{}}}',
        encoding="utf-8",
    )
    (root / ".gitignore").write_text("/.mcp.json\n", encoding="utf-8")


class CheckCopilotCustomizationTests(unittest.TestCase):
    def test_valid_repository_passes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            self.assertEqual([], validate_repository(root))

    def test_ignored_local_workspace_mcp_is_allowed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            (root / ".mcp.json").write_text("{}", encoding="utf-8")
            self.assertEqual([], validate_repository(root))

    def test_tracked_workspace_mcp_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            (root / ".mcp.json").write_text("{}", encoding="utf-8")
            subprocess.run(
                ["git", "add", "-f", ".mcp.json"],
                cwd=root,
                check=True,
            )
            self.assertIn("tracked .mcp.json must be removed", validate_repository(root))

    def test_legacy_skill_directory_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            legacy = root / ".claude" / "skills" / "dev-flow"
            legacy.mkdir(parents=True)
            (legacy / "SKILL.md").write_text(
                SKILL.format(name="dev-flow"),
                encoding="utf-8",
            )
            self.assertIn("legacy .claude/skills must be removed", validate_repository(root))

    def test_missing_disabled_skill_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            settings_path = root / ".github" / "copilot" / "settings.json"
            settings = json.loads(settings_path.read_text(encoding="utf-8"))
            settings["disabledSkills"].remove("using-superpowers")
            settings_path.write_text(json.dumps(settings), encoding="utf-8")
            self.assertIn(
                "disabledSkills is missing using-superpowers",
                validate_repository(root),
            )

    def test_stale_claude_reference_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            (root / "README.md").write_text(
                "Use CLAUDE.md with Claude Code.\n",
                encoding="utf-8",
            )
            self.assertIn(
                "stale Claude reference in README.md",
                validate_repository(root),
            )

    def test_hyphenated_classifier_reference_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            make_valid_repository(root)
            skill = root / ".github" / "skills" / "dev-flow" / "SKILL.md"
            skill.write_text(
                SKILL.format(name="dev-flow")
                + "\nRun scripts/classify-review-risk.py.\n",
                encoding="utf-8",
            )
            self.assertIn(
                "project skill dev-flow references the wrong classifier path",
                validate_repository(root),
            )


if __name__ == "__main__":
    unittest.main()
