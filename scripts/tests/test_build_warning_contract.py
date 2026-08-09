# SPDX-License-Identifier: GPL-3.0-or-later
"""Fail-closed warning-as-error contract for CI build surfaces (#2067)."""

from __future__ import annotations

from dataclasses import dataclass
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS_DIR = ROOT / ".github" / "workflows"
COLORZCORE_PROJECT = "tools/ColorzCore/ColorzCore/ColorzCore.csproj"


@dataclass(frozen=True)
class RunStep:
    workflow: str
    job: str
    name: str
    command: str


@dataclass(frozen=True)
class DotnetInvocation:
    step: RunStep
    verb: str
    command: str

    @property
    def tokens(self) -> list[str]:
        return split_shell_words(self.command)

    @property
    def project(self) -> str:
        tokens = self.tokens
        for token in tokens[2:]:
            if not token.startswith("-") and not token.startswith("/"):
                return normalize_path_token(token)
        return ""

    @property
    def key(self) -> str:
        return f"{self.step.workflow}:{self.step.job}:{self.step.name}: {self.command}"


def split_shell_words(command: str) -> list[str]:
    command = re.sub(
        r"\$\{\{\s*(.*?)\s*\}\}",
        lambda match: "${{" + re.sub(r"\s+", "", match.group(1)) + "}}",
        command,
    )
    return re.findall(r"\$\{\{.*?\}\}|\"[^\"]*\"|'[^']*'|\S+", command)


def normalize_path_token(token: str) -> str:
    return token.strip("\"'").replace("\\", "/")


def normalize_value(value: str) -> str:
    return re.sub(r"\s+", "", value.strip("\"'"))


def option_value(tokens: list[str], *names: str) -> str:
    normalized_names = {name.lower() for name in names}
    for index, token in enumerate(tokens):
        lower = token.lower()
        if lower in normalized_names and index + 1 < len(tokens):
            return normalize_value(tokens[index + 1])
        for name in normalized_names:
            prefix = f"{name}="
            if lower.startswith(prefix):
                return normalize_value(token[len(prefix) :])
            prefix = f"{name}:"
            if lower.startswith(prefix):
                return normalize_value(token[len(prefix) :])
    return ""


def property_value(tokens: list[str], name: str) -> str:
    needle = name.lower()
    for token in tokens:
        match = re.match(r"[-/](?:p|property):([^=]+)=(.*)", token, flags=re.IGNORECASE)
        if match and match.group(1).lower() == needle:
            return normalize_value(match.group(2))
    return ""


def configuration(tokens: list[str]) -> str:
    return option_value(tokens, "-c", "--configuration") or property_value(tokens, "Configuration")


def runtime_identifier(tokens: list[str]) -> str:
    return option_value(tokens, "-r", "--runtime") or property_value(tokens, "RuntimeIdentifier")


def self_contained(tokens: list[str]) -> str:
    return option_value(tokens, "--self-contained") or property_value(tokens, "SelfContained")


def target_framework(tokens: list[str]) -> str:
    return option_value(tokens, "-f", "--framework") or property_value(tokens, "TargetFramework")


def has_cli_warn_as_error(tokens: list[str]) -> bool:
    return any(token.lower() in {"-warnaserror", "--warnaserror"} for token in tokens)


def has_msbuild_warn_as_error(tokens: list[str]) -> bool:
    return any(token.lower() == "/warnaserror" for token in tokens)


def extract_run_value(lines: list[str], index: int, run_indent: int, marker: str) -> tuple[str, int]:
    value = marker.strip()
    if value not in {"|", "|-", "|+", ">", ">-", ">+"}:
        return value, index + 1

    block_lines: list[str] = []
    cursor = index + 1
    while cursor < len(lines):
        line = lines[cursor]
        if line.strip() and len(line) - len(line.lstrip(" ")) <= run_indent:
            break
        block_lines.append(line)
        cursor += 1

    content_lines = [line for line in block_lines if line.strip()]
    base_indent = min(
        (len(line) - len(line.lstrip(" ")) for line in content_lines),
        default=run_indent + 2,
    )
    stripped = [line[base_indent:] if len(line) >= base_indent else "" for line in block_lines]
    if value.startswith(">"):
        paragraphs: list[str] = []
        current: list[str] = []
        for line in stripped:
            if line.strip():
                current.append(line.strip())
            elif current:
                paragraphs.append(" ".join(current))
                current = []
        if current:
            paragraphs.append(" ".join(current))
        return "\n".join(paragraphs), cursor
    return "\n".join(stripped).strip("\n"), cursor


def parse_workflow_runs(text: str, workflow: str = "<memory>") -> list[RunStep]:
    runs: list[RunStep] = []
    lines = text.splitlines()
    in_jobs = False
    current_job = ""
    current_step = "<unnamed>"
    index = 0
    while index < len(lines):
        line = lines[index]
        if re.match(r"^jobs:\s*$", line):
            in_jobs = True
            index += 1
            continue
        if not in_jobs:
            index += 1
            continue

        job_match = re.match(r"^  ([A-Za-z0-9_-]+):\s*$", line)
        if job_match:
            current_job = job_match.group(1)
            current_step = "<unnamed>"
            index += 1
            continue

        step_match = re.match(r"^\s*-\s+name:\s*(.+?)\s*$", line)
        if step_match:
            current_step = step_match.group(1)
            index += 1
            continue

        run_match = re.match(r"^(?P<indent>\s*)run:\s*(?P<value>.*)$", line)
        if run_match and current_job:
            command, index = extract_run_value(
                lines,
                index,
                len(run_match.group("indent")),
                run_match.group("value"),
            )
            runs.append(RunStep(workflow, current_job, current_step, command))
            continue

        index += 1
    return runs


def logical_shell_commands(command: str) -> list[str]:
    commands: list[str] = []
    current = ""
    for raw_line in command.splitlines() or [command]:
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if current:
            current = f"{current} {line}"
        else:
            current = line
        if current.endswith("\\") or current.endswith("`"):
            current = current[:-1].rstrip()
            continue
        commands.append(current)
        current = ""
    if current:
        commands.append(current)
    return commands


def dotnet_invocations(step: RunStep) -> list[DotnetInvocation]:
    invocations: list[DotnetInvocation] = []
    for command in logical_shell_commands(step.command):
        for match in re.finditer(r"(?<!\S)dotnet\s+(build|publish|test|msbuild|restore)\b", command):
            invocations.append(DotnetInvocation(step, match.group(1).lower(), command[match.start() :]))
    return invocations


def is_compile_invocation(invocation: DotnetInvocation) -> bool:
    tokens = [token.lower() for token in invocation.tokens]
    if invocation.verb in {"build", "publish"}:
        return True
    if invocation.verb == "test":
        return "--no-build" not in tokens
    if invocation.verb == "msbuild":
        target = " ".join(
            token
            for token in tokens
            if token.startswith("/t:")
            or token.startswith("-t:")
            or token.startswith("/target:")
            or token.startswith("-target:")
        )
        return bool(re.search(r"(?:^|[:;,])(?:build|rebuild)(?:$|[;,])", target, flags=re.IGNORECASE))
    return False


def workflow_paths() -> list[Path]:
    return sorted(WORKFLOWS_DIR.glob("*.yml"))


def all_workflow_runs() -> list[RunStep]:
    runs: list[RunStep] = []
    for path in workflow_paths():
        runs.extend(parse_workflow_runs(path.read_text(encoding="utf-8"), path.name))
    return runs


def all_dotnet_invocations() -> list[DotnetInvocation]:
    invocations: list[DotnetInvocation] = []
    for step in all_workflow_runs():
        invocations.extend(dotnet_invocations(step))
    return invocations


class WorkflowParserFixtureTests(unittest.TestCase):
    def test_parser_handles_inline_literal_folded_bash_and_powershell_runs(self) -> None:
        workflow = r"""
jobs:
  fixtures:
    steps:
    - name: Inline
      run: dotnet build Inline.csproj -warnaserror
    - name: Literal
      run: |
        echo before
        dotnet test Literal.csproj -warnaserror
    - name: Folded
      run: >
        dotnet publish Folded.csproj
        -c Release
        -warnaserror
    - name: Bash continuation
      run: |
        dotnet build Bash.csproj \
          -c Release \
          -warnaserror
    - name: PowerShell continuation
      run: |
        dotnet msbuild `
          /t:ReBuild `
          /warnaserror Solution.sln
"""
        invocations = [
            invocation
            for step in parse_workflow_runs(workflow)
            for invocation in dotnet_invocations(step)
            if is_compile_invocation(invocation)
        ]

        self.assertEqual(
            [
                ("Inline", "build", "Inline.csproj"),
                ("Literal", "test", "Literal.csproj"),
                ("Folded", "publish", "Folded.csproj"),
                ("Bash continuation", "build", "Bash.csproj"),
                ("PowerShell continuation", "msbuild", "Solution.sln"),
            ],
            [(i.step.name, i.verb, i.project) for i in invocations],
        )
        self.assertTrue(all(has_cli_warn_as_error(i.tokens) or has_msbuild_warn_as_error(i.tokens) for i in invocations))

    def test_compile_classification_ignores_restore_run_and_no_build_tests(self) -> None:
        workflow = r"""
jobs:
  classify:
    steps:
    - name: Restore only
      run: dotnet restore RestoreOnly.csproj -warnaserror
    - name: Advisory run
      run: dotnet run --project Tool.csproj
    - name: No-build tests
      run: dotnet test Tests.csproj --no-build
    - name: Restore target
      run: dotnet msbuild /t:restore Solution.sln
    - name: Build target
      run: dotnet msbuild /t:build /warnaserror Solution.sln
    - name: Rebuild target
      run: dotnet msbuild /t:ReBuild /warnaserror Solution.sln
"""
        invocations = [
            invocation
            for step in parse_workflow_runs(workflow)
            for invocation in dotnet_invocations(step)
            if is_compile_invocation(invocation)
        ]

        self.assertEqual(
            [("Build target", "msbuild"), ("Rebuild target", "msbuild")],
            [(invocation.step.name, invocation.verb) for invocation in invocations],
        )


class BuildWarningContractTests(unittest.TestCase):
    def test_every_workflow_compilation_is_warning_as_error(self) -> None:
        missing: list[str] = []
        for invocation in all_dotnet_invocations():
            if not is_compile_invocation(invocation):
                continue
            if invocation.verb == "msbuild":
                if not has_msbuild_warn_as_error(invocation.tokens):
                    missing.append(invocation.key)
            elif not has_cli_warn_as_error(invocation.tokens):
                missing.append(invocation.key)

        self.assertEqual([], missing)

    def test_workflow_contract_scans_every_workflow(self) -> None:
        compiled_workflows = {
            invocation.step.workflow
            for invocation in all_dotnet_invocations()
            if is_compile_invocation(invocation)
        }
        expected = {
            "android-emulator-parity.yml",
            "android.yml",
            "check.yml",
            "crossplatform.yml",
            "e2e-run.yml",
            "gba-playtest.yml",
            "ios.yml",
            "msbuild.yml",
            "pages.yml",
            "release.yml",
        }
        self.assertEqual(expected, compiled_workflows)

    def test_no_workflow_suppresses_build_warnings(self) -> None:
        forbidden = re.compile(
            r"(?i)(?:^|[-/:])(?:nowarn|warningsnotaserrors|warnasmessage)\b|treatwarningsaserrors\s*=\s*false"
        )
        offenders = [
            f"{path.name}: {line_number}: {line.strip()}"
            for path in workflow_paths()
            for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1)
            if forbidden.search(line)
        ]
        self.assertEqual([], offenders)

    def test_msbuild_junit_logger_uses_available_exact_version(self) -> None:
        workflow = (WORKFLOWS_DIR / "msbuild.yml").read_text(encoding="utf-8")
        install_steps = [
            step
            for step in parse_workflow_runs(workflow, "msbuild.yml")
            if step.name == "Install JUnit Test Logger"
        ]

        self.assertEqual(1, len(install_steps))
        self.assertEqual(
            [
                "dotnet",
                "add",
                "FEBuilderGBA.Tests/FEBuilderGBA.Tests.csproj",
                "package",
                "JunitXml.TestLogger",
                "--version",
                "4.0.254",
            ],
            split_shell_words(install_steps[0].command),
        )

    def test_colorzcore_compiles_have_explicit_matching_restore(self) -> None:
        failures: list[str] = []
        by_workflow_job: dict[tuple[str, str], list[DotnetInvocation]] = {}
        for invocation in all_dotnet_invocations():
            by_workflow_job.setdefault((invocation.step.workflow, invocation.step.job), []).append(invocation)

        for invocations in by_workflow_job.values():
            for index, invocation in enumerate(invocations):
                if not is_compile_invocation(invocation):
                    continue
                if invocation.project != COLORZCORE_PROJECT:
                    continue
                tokens = invocation.tokens
                if "--no-restore" not in [token.lower() for token in tokens]:
                    failures.append(f"{invocation.key}: missing --no-restore")
                if target_framework(tokens) != "net10.0":
                    failures.append(f"{invocation.key}: missing TargetFramework=net10.0")
                if not configuration(tokens):
                    failures.append(f"{invocation.key}: missing Configuration")
                if invocation.verb == "publish":
                    if not runtime_identifier(tokens):
                        failures.append(f"{invocation.key}: missing RuntimeIdentifier")
                    if not self_contained(tokens):
                        failures.append(f"{invocation.key}: missing SelfContained")

                restore = self._matching_colorzcore_restore(invocation, invocations[:index])
                if restore is None:
                    failures.append(f"{invocation.key}: missing preceding matching restore")
                    continue

                restore_tokens = restore.tokens
                if not has_cli_warn_as_error(restore_tokens):
                    failures.append(f"{restore.key}: restore missing -warnaserror")
                if target_framework(restore_tokens) != "net10.0":
                    failures.append(f"{restore.key}: restore missing TargetFramework=net10.0")
                if not configuration(restore_tokens):
                    failures.append(f"{restore.key}: restore missing Configuration")

        self.assertEqual([], failures)

    def test_colorzcore_publish_restore_matching_requires_rid_and_selfcontained(self) -> None:
        workflow = r"""
jobs:
  package:
    steps:
    - name: Restore ColorzCore without publish identity
      run: dotnet restore tools/ColorzCore/ColorzCore/ColorzCore.csproj -p:Configuration=Release -p:TargetFramework=net10.0 -warnaserror
    - name: Publish ColorzCore without publish identity
      run: dotnet publish tools/ColorzCore/ColorzCore/ColorzCore.csproj -c Release --no-restore -p:TargetFramework=net10.0 -warnaserror
"""
        invocations = [
            invocation
            for step in parse_workflow_runs(workflow)
            for invocation in dotnet_invocations(step)
        ]

        self.assertIsNone(self._matching_colorzcore_restore(invocations[1], invocations[:1]))

    def _matching_colorzcore_restore(
        self,
        compile_invocation: DotnetInvocation,
        prior_invocations: list[DotnetInvocation],
    ) -> DotnetInvocation | None:
        compile_tokens = compile_invocation.tokens
        compile_tfm = target_framework(compile_tokens)
        compile_configuration = configuration(compile_tokens)
        for candidate in reversed(prior_invocations):
            if candidate.verb != "restore":
                continue
            if candidate.project != COLORZCORE_PROJECT:
                continue
            restore_tokens = candidate.tokens
            if target_framework(restore_tokens) != compile_tfm:
                continue
            if configuration(restore_tokens) != compile_configuration:
                continue
            if compile_invocation.verb == "publish":
                if not runtime_identifier(compile_tokens):
                    return None
                if not self_contained(compile_tokens):
                    return None
                if runtime_identifier(restore_tokens) != runtime_identifier(compile_tokens):
                    continue
                if self_contained(restore_tokens).lower() != self_contained(compile_tokens).lower():
                    continue
            return candidate
        return None


if __name__ == "__main__":
    unittest.main()
