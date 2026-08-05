# SPDX-License-Identifier: GPL-3.0-or-later
"""Fail-closed contract for crossplatform.yml build-server isolation (#2062)."""

from __future__ import annotations

import re
import shlex
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = ROOT / ".github" / "workflows" / "crossplatform.yml"


def all_job_blocks(text: str) -> dict[str, str]:
    jobs_match = re.search(r"(?ms)^jobs:\n(?P<body>.*)\Z", text)
    if jobs_match is None:
        raise AssertionError("Missing jobs section")
    jobs_text = jobs_match.group("body")
    matches = list(re.finditer(r"(?m)^  (?P<name>[a-zA-Z0-9_-]+):\n", jobs_text))
    jobs: dict[str, str] = {}
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(jobs_text)
        jobs[match.group("name")] = jobs_text[match.end() : end]
    return jobs


def named_steps(block: str) -> list[tuple[str, str]]:
    matches = list(re.finditer(r"(?m)^    - name: (?P<name>.+)\n", block))
    steps: list[tuple[str, str]] = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(block)
        steps.append((match.group("name").strip(), block[match.start() : end]))
    return steps


def run_command(step: str) -> str:
    match = re.search(r"(?m)^      run:\s*(?P<value>.*)$", step)
    if match is None:
        return ""

    value = match.group("value").strip()
    if value not in {"|", "|-", ">", ">-"}:
        return value

    lines = step[match.end() :].splitlines()
    return "\n".join(
        line.strip()
        for line in lines
        if line.startswith("        ") and line.strip()
    )


class CrossPlatformWorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        cls.all_jobs = all_job_blocks(cls.workflow)
        cls.jobs = {
            name: cls.all_jobs[name]
            for name in (
                "workflow-contract",
                "build",
                "mcp-adapter-tests",
                "mcp-real-backend-tests",
                "publish",
            )
        }

    def test_contract_job_gates_parallel_dotnet_jobs(self) -> None:
        contract_steps = dict(named_steps(self.jobs["workflow-contract"]))
        self.assertEqual(
            "python -m unittest scripts/tests/test_crossplatform_workflow.py -v",
            run_command(contract_steps["Validate fail-closed .NET workflow steps"]),
        )
        self.assertRegex(self.jobs["build"], r"(?m)^    needs: workflow-contract$")
        self.assertRegex(
            self.jobs["mcp-real-backend-tests"],
            r"(?m)^    needs: workflow-contract$",
        )
        self.assertRegex(self.jobs["publish"], r"(?m)^    needs: build$")

    def test_every_build_test_and_publish_is_server_isolated(self) -> None:
        expected_steps: dict[str, dict[str, tuple[str, str]]] = {
            "build": {
                "Build ColorzCore (bundled EA tool)": (
                    "build",
                    "tools/ColorzCore/ColorzCore/ColorzCore.csproj",
                ),
                "Build Core library": ("build", "FEBuilderGBA.Core/FEBuilderGBA.Core.csproj"),
                "Build CLI": ("build", "FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj"),
                "Build SkiaSharp backend": (
                    "build",
                    "FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj",
                ),
                "Build Avalonia project": (
                    "build",
                    "FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj",
                ),
                "Run Core tests": ("test", "FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj"),
                "Run Avalonia tests (data-verify headless validation)": (
                    "test",
                    "FEBuilderGBA.Avalonia.Tests/FEBuilderGBA.Avalonia.Tests.csproj",
                ),
            },
            "mcp-real-backend-tests": {
                "Build CLI": ("build", "FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj"),
            },
            "publish": {
                "Publish ColorzCore (self-contained per-RID)": (
                    "publish",
                    "tools/ColorzCore/ColorzCore/ColorzCore.csproj",
                ),
                "Publish CLI": ("publish", "FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj"),
                "Publish Avalonia": (
                    "publish",
                    "FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj",
                ),
            },
        }

        discovered: dict[str, dict[str, str]] = {}
        for job_name, block in self.all_jobs.items():
            dotnet_steps = {}
            for name, step in named_steps(block):
                command = run_command(step)
                if re.search(r"\bdotnet (?:build|test|publish)\b", command):
                    dotnet_steps[name] = command
            if dotnet_steps:
                discovered[job_name] = dotnet_steps

        self.assertEqual(set(expected_steps), set(discovered))
        for job_name, required_steps in expected_steps.items():
            self.assertEqual(set(required_steps), set(discovered[job_name]), job_name)
            for name, command in discovered[job_name].items():
                with self.subTest(job=job_name, step=name):
                    invocations = list(
                        re.finditer(
                            r"\bdotnet (?P<verb>build|test|publish) (?P<project>\S+)",
                            command,
                        )
                    )
                    self.assertEqual(1, len(invocations), command)
                    invocation = invocations[0]
                    self.assertEqual(0, invocation.start(), command)
                    expected_verb, expected_project = required_steps[name]
                    self.assertEqual(expected_verb, invocation.group("verb"))
                    self.assertEqual(expected_project, invocation.group("project"))
                    tokens = shlex.split(command)
                    self.assertIn("--disable-build-servers", tokens)
                    self.assertIn("-p:UseSharedCompilation=false", tokens)
                    self.assertNotIn("|| true", command)
                    self.assertNotRegex(command, r"\bretry\b")

    def test_avalonia_operations_have_live_runner_timeouts(self) -> None:
        build_steps = dict(named_steps(self.jobs["build"]))
        publish_steps = dict(named_steps(self.jobs["publish"]))
        self.assertRegex(
            build_steps["Build Avalonia project"],
            r"(?m)^      timeout-minutes: 15$",
        )
        self.assertRegex(
            publish_steps["Publish Avalonia"],
            r"(?m)^      timeout-minutes: 20$",
        )

    def test_cli_smoke_uses_prebuilt_release_dll_without_dotnet_run(self) -> None:
        build_steps = named_steps(self.jobs["build"])
        names = [name for name, _ in build_steps]
        self.assertLess(names.index("Build CLI"), names.index("CLI smoke test"))
        self.assertLess(names.index("Build CLI"), names.index("CLI help test"))

        expected_dll = "FEBuilderGBA.CLI/bin/Release/net10.0/FEBuilderGBA.CLI.dll"
        step_map = dict(build_steps)
        self.assertEqual(
            f"dotnet {expected_dll} --version",
            run_command(step_map["CLI smoke test"]),
        )
        self.assertEqual(
            f"dotnet {expected_dll} --help",
            run_command(step_map["CLI help test"]),
        )
        for job_name, block in self.all_jobs.items():
            for name, step in named_steps(block):
                with self.subTest(job=job_name, step=name):
                    self.assertNotRegex(run_command(step), r"\bdotnet\s+run\b")

    def test_matrix_and_fail_closed_policy_remain_intact(self) -> None:
        expected_build_matrix = """        include:
          - check_name: ubuntu-latest
            runner: ubuntu-latest
          - check_name: macos-latest
            runner: macos-15
          - check_name: windows-latest
            runner: windows-latest"""
        self.assertIn(
            expected_build_matrix,
            self.jobs["build"],
        )
        self.assertRegex(
            self.jobs["build"],
            r"(?m)^    name: build \(\$\{\{ matrix\.check_name \}\}\)$",
        )
        self.assertRegex(
            self.jobs["build"],
            r"(?m)^    runs-on: \$\{\{ matrix\.runner \}\}$",
        )
        self.assertNotIn("matrix.os", self.jobs["build"])
        self.assertIn(
            "name: test-results-${{ matrix.check_name }}",
            self.jobs["build"],
        )
        self.assertRegex(
            self.jobs["publish"],
            r"(?ms)- os: macos-15\s+rid: osx-arm64",
        )
        self.assertIn(
            "rid: osx-arm64",
            self.jobs["publish"],
        )
        self.assertNotIn("only ever runs on macos-latest", self.jobs["publish"])
        self.assertIn(
            "os: [ubuntu-latest, macos-latest, windows-latest]",
            self.jobs["mcp-adapter-tests"],
        )
        self.assertRegex(
            self.jobs["mcp-adapter-tests"],
            r"(?m)^    runs-on: \$\{\{ matrix\.os \}\}$",
        )
        self.assertNotIn("actions/setup-dotnet", self.jobs["mcp-adapter-tests"])
        self.assertNotRegex(self.jobs["mcp-adapter-tests"], r"\bdotnet\b")
        self.assertIn(
            "FEBUILDERGBA_CLI_EXE: ${{ runner.temp }}/mcp-contract-no-backend",
            self.jobs["mcp-adapter-tests"],
        )
        for job_name in ("workflow-contract", "build", "mcp-real-backend-tests", "publish"):
            with self.subTest(job=job_name):
                self.assertNotRegex(
                    self.jobs[job_name],
                    r"(?m)^\s+continue-on-error:",
                )


if __name__ == "__main__":
    unittest.main()
