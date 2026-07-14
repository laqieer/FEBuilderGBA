#!/usr/bin/env python3
"""Verify that the pre-commit commitlint hook reads its supplied message file."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


VALID_MESSAGE = "fix: verify commitlint hook\n"
INVALID_MESSAGE = "invalidtype: this must fail\n"
TIMEOUT_SECONDS = 300


class ValidationError(RuntimeError):
    """Raised when the commitlint hook does not enforce the expected contract."""


def run_command(
    command: list[str],
    *,
    cwd: Path,
    timeout: int = TIMEOUT_SECONDS,
) -> subprocess.CompletedProcess[str]:
    env = os.environ.copy()
    env.update(
        {
            "FORCE_COLOR": "0",
            "NO_COLOR": "1",
            "PRE_COMMIT_COLOR": "never",
        }
    )
    return subprocess.run(
        command,
        cwd=cwd,
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        check=False,
    )


def require_tool(name: str) -> str:
    executable = shutil.which(name)
    if executable is None:
        raise ValidationError(f"Required executable was not found on PATH: {name}")
    return executable


def git_output(git: str, root: Path, *args: str) -> str:
    result = run_command([git, *args], cwd=root)
    if result.returncode != 0:
        diagnostics = (result.stdout + result.stderr).strip()
        raise ValidationError(
            f"git {' '.join(args)} failed with exit {result.returncode}: {diagnostics}"
        )
    return result.stdout.strip()


def run_hook(
    pre_commit: str,
    root: Path,
    config: Path,
    message_file: Path,
) -> subprocess.CompletedProcess[str]:
    return run_command(
        [
            pre_commit,
            "run",
            "-c",
            str(config),
            "commitlint",
            "--hook-stage",
            "commit-msg",
            "--commit-msg-filename",
            str(message_file),
        ],
        cwd=root,
    )


def diagnostics(result: subprocess.CompletedProcess[str]) -> str:
    return (result.stdout + result.stderr).strip()


def require_pass(result: subprocess.CompletedProcess[str], label: str) -> None:
    if result.returncode != 0:
        raise ValidationError(
            f"{label} unexpectedly failed with exit {result.returncode}:\n"
            f"{diagnostics(result)}"
        )


def require_invalid_type_failure(
    result: subprocess.CompletedProcess[str],
    label: str,
) -> None:
    output = diagnostics(result)
    if result.returncode == 0:
        raise ValidationError(f"{label} unexpectedly passed:\n{output}")
    if "type-enum" not in output:
        raise ValidationError(
            f"{label} failed without the expected type-enum diagnostic:\n{output}"
        )


def validate(config_arg: str) -> None:
    git = require_tool("git")
    pre_commit = require_tool("pre-commit")

    invocation_root = Path.cwd()
    root = Path(git_output(git, invocation_root, "rev-parse", "--show-toplevel"))
    config = Path(config_arg)
    if not config.is_absolute():
        config = root / config
    config = config.resolve()
    if not config.is_file():
        raise ValidationError(f"Pre-commit config was not found: {config}")

    git_message_path = Path(
        git_output(git, root, "rev-parse", "--git-path", "COMMIT_EDITMSG")
    )
    if not git_message_path.is_absolute():
        git_message_path = root / git_message_path
    git_message_path = git_message_path.resolve()

    fallback_existed = git_message_path.exists()
    fallback_bytes = git_message_path.read_bytes() if fallback_existed else b""

    try:
        git_message_path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="commitlint hook ") as temp_dir:
            temp_root = Path(temp_dir)
            runtime_config = temp_root / "pre-commit config.yaml"
            valid_file = temp_root / "valid candidate message.txt"
            invalid_file = temp_root / "invalid candidate message.txt"
            shutil.copyfile(config, runtime_config)
            valid_file.write_text(VALID_MESSAGE, encoding="utf-8")
            invalid_file.write_text(INVALID_MESSAGE, encoding="utf-8")

            # Run an exact detached copy so pre-commit's staged-config guard
            # does not prevent validating an edited config before it is staged.
            # A broken pass_filenames=false hook reads this fallback for both
            # candidates. Keeping it fixed makes the invalid false-pass visible.
            git_message_path.write_text(VALID_MESSAGE, encoding="utf-8")
            valid_with_valid_fallback = run_hook(
                pre_commit, root, runtime_config, valid_file
            )
            invalid_with_valid_fallback = run_hook(
                pre_commit, root, runtime_config, invalid_file
            )
            require_pass(valid_with_valid_fallback, "Valid candidate")
            require_invalid_type_failure(
                invalid_with_valid_fallback,
                "Invalid candidate with valid fallback",
            )

            # The inverse catches a hook that hides a valid candidate behind a
            # stale invalid fallback.
            git_message_path.write_text(INVALID_MESSAGE, encoding="utf-8")
            invalid_with_invalid_fallback = run_hook(
                pre_commit, root, runtime_config, invalid_file
            )
            valid_with_invalid_fallback = run_hook(
                pre_commit, root, runtime_config, valid_file
            )
            require_invalid_type_failure(
                invalid_with_invalid_fallback,
                "Invalid candidate",
            )
            require_pass(
                valid_with_invalid_fallback,
                "Valid candidate with invalid fallback",
            )
    finally:
        if fallback_existed:
            git_message_path.write_bytes(fallback_bytes)
        else:
            git_message_path.unlink(missing_ok=True)

    print("commitlint hook forwarding: PASS")
    print("valid candidate: accepted")
    print("invalid candidate: rejected with type-enum")
    print(f"config: {config}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--config",
        default=".pre-commit-config.yaml",
        help="Pre-commit config path, relative to the repository root by default.",
    )
    args = parser.parse_args()

    try:
        validate(args.config)
    except (OSError, subprocess.SubprocessError, ValidationError) as exc:
        print(f"commitlint hook validation failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
