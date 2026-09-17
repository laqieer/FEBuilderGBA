# SPDX-License-Identifier: GPL-3.0-or-later
"""Fail-closed contract for crossplatform.yml build-server isolation (#2062)."""

from __future__ import annotations

import contextlib
import importlib
import io
import json
import os
import re
import shlex
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock


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
            "python -m unittest scripts.tests.test_crossplatform_workflow scripts.tests.test_build_warning_contract -v",
            run_command(contract_steps["Validate fail-closed .NET workflow steps"]),
        )
        self.assertRegex(self.jobs["build"], r"(?m)^    needs: workflow-contract$")
        self.assertRegex(
            self.jobs["mcp-real-backend-tests"],
            r"(?m)^    needs: workflow-contract$",
        )
        self.assertRegex(self.jobs["publish"], r"(?m)^    needs: build$")

    def test_linux_x11_pure_suite_runs_unconditionally_after_python_setup(self) -> None:
        build_steps = named_steps(self.jobs["build"])
        names = [name for name, _ in build_steps]
        suite_name = "Run Linux X11 pure contract tests (issue #2160)"
        self.assertEqual(1, names.count(suite_name))
        self.assertLess(names.index("Setup Python 3.12"), names.index(suite_name))

        command = (
            "python -B -m unittest scripts.tests.test_linux_x11 "
            "scripts.tests.test_linux_x11_metadata"
        )
        suite_step = dict(build_steps)[suite_name]
        self.assertEqual(command, run_command(suite_step))
        self.assertEqual(
            1,
            sum(run_command(step) == command for _, step in build_steps),
        )
        self.assertNotRegex(
            suite_step,
            r"(?m)^      (?:if|continue-on-error|working-directory|shell):",
        )
        self.assertNotRegex(self.jobs["build"], r"(?m)^    if:")
        self.assertNotIn("linux_x11_native_smoke", self.jobs["build"])

    def test_metadata_supervisor_contracts_are_windows_only_and_never_observe(self):
        steps = dict(named_steps(self.jobs["build"]))
        step = steps["Run Windows Linux metadata supervisor pure contracts (issue #2160)"]
        self.assertEqual(
            r".\scripts\tests\test_linux_x11_metadata_supervisor.ps1",
            run_command(step),
        )
        self.assertRegex(step, r"(?m)^      if: runner\.os == 'Windows'$")
        self.assertRegex(step, r"(?m)^      shell: pwsh$")
        self.assertNotRegex(step, r"(?m)^      continue-on-error:")
        for block in self.all_jobs.values():
            for _, item in named_steps(block):
                command = run_command(item)
                self.assertNotIn("Invoke-LinuxX11Metadata.ps1", command)
                self.assertNotIn("--observe", command)
                self.assertNotIn("linux_x11_metadata.py", command)

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
                "Run Core tests (macOS no-dump diagnostics)": (
                    "test", "FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj",
                ),
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
                    self.assertIn("-warnaserror", tokens)
                    self.assertIn("--disable-build-servers", tokens)
                    self.assertIn("-p:UseSharedCompilation=false", tokens)
                    self.assertNotIn("|| true", command)
                    self.assertNotRegex(command, r"\bretry\b")

    def test_colorzcore_restore_steps_pair_with_no_restore_compiles(self) -> None:
        build_steps = dict(named_steps(self.jobs["build"]))
        publish_steps = dict(named_steps(self.jobs["publish"]))

        build_restore = run_command(build_steps["Restore ColorzCore (bundled EA tool)"])
        build_compile = run_command(build_steps["Build ColorzCore (bundled EA tool)"])
        self.assertEqual(
            "dotnet restore tools/ColorzCore/ColorzCore/ColorzCore.csproj -p:Configuration=Release -p:TargetFramework=net10.0 -warnaserror --disable-build-servers -p:UseSharedCompilation=false",
            build_restore,
        )
        for required in ("--no-restore", "-p:TargetFramework=net10.0", "-warnaserror"):
            self.assertIn(required, shlex.split(build_compile))

        publish_restore = run_command(publish_steps["Restore ColorzCore (self-contained per-RID)"])
        publish_compile = run_command(publish_steps["Publish ColorzCore (self-contained per-RID)"])
        self.assertEqual(
            "dotnet restore tools/ColorzCore/ColorzCore/ColorzCore.csproj -r ${{ matrix.rid }} -p:Configuration=Release -p:SelfContained=true -p:TargetFramework=net10.0 -warnaserror --disable-build-servers -p:UseSharedCompilation=false",
            publish_restore,
        )
        for required in (
            "-r ${{ matrix.rid }}",
            "--self-contained true",
            "--no-restore",
            "-p:TargetFramework=net10.0",
            "-warnaserror",
        ):
            self.assertIn(required, publish_compile)

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


class MacCoreDiagnosticsWorkflowTests(unittest.TestCase):
    MAC_STEP = "Run Core tests (macOS no-dump diagnostics)"
    CORE_COMMAND = (
        "dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj "
        "-c Release -warnaserror --disable-build-servers -p:UseSharedCompilation=false "
        '--logger "trx;LogFileName=core-tests.trx"'
    )
    MAC_COMMAND = (
        CORE_COMMAND
        + ' --logger "console;verbosity=minimal"'
        " --results-directory TestResults/core-ci-diagnostics/raw"
        " --blame-hang --blame-hang-timeout 5m --blame-hang-dump-type none"
    )
    PREPARE = "Prepare macOS Core diagnostic context"
    CHECKPOINT = "Upload macOS Core context checkpoint"
    FINISH = "Collect macOS Core diagnostic summary"
    SUMMARY = "Upload macOS Core diagnostic summary"

    def assert_contract(self, text: str) -> None:
        build = all_job_blocks(text)["build"]
        steps = dict(named_steps(build))
        names = list(steps)
        for name in (self.PREPARE, self.CHECKPOINT, "Run Core tests", self.MAC_STEP, self.FINISH, self.SUMMARY):
            self.assertIn(name, steps)
        mac = "${{ matrix.runner == 'macos-15' }}"
        non_mac = "${{ matrix.runner != 'macos-15' }}"
        for name in (self.PREPARE, self.MAC_STEP):
            self.assertEqual([mac], re.findall(r"(?m)^      if:\s*(.+)$", steps[name]))
        for producer, identifier, uploader in (
            (self.PREPARE, "core_diagnostics_prepare", self.CHECKPOINT),
            (self.FINISH, "core_diagnostics_finish", self.SUMMARY),
        ):
            self.assertEqual([identifier], re.findall(r"(?m)^      id:\s*(.+)$", steps[producer]))
            self.assertEqual(
                ["${{ always() && matrix.runner == 'macos-15' && steps."
                 + identifier + ".outcome == 'success' }}"],
                re.findall(r"(?m)^      if:\s*(.+)$", steps[uploader]),
            )
        self.assertEqual([non_mac], re.findall(r"(?m)^      if:\s*(.+)$", steps["Run Core tests"]))
        self.assertEqual(self.CORE_COMMAND, run_command(steps["Run Core tests"]))
        self.assertEqual(self.MAC_COMMAND, run_command(steps[self.MAC_STEP]))
        self.assertEqual(["15"], re.findall(r"(?m)^      timeout-minutes:\s*(\d+)$", steps[self.MAC_STEP]))
        self.assertEqual(
            "python scripts/ci_core_diagnostics.py prepare", run_command(steps[self.PREPARE])
        )
        self.assertEqual(
            "python scripts/ci_core_diagnostics.py finish", run_command(steps[self.FINISH])
        )
        self.assertEqual(
            ["${{ always() && matrix.runner == 'macos-15' }}"],
            re.findall(r"(?m)^      if:\s*(.+)$", steps[self.FINISH]),
        )
        for name in (self.PREPARE, self.CHECKPOINT, self.FINISH, self.SUMMARY):
            self.assertEqual(["2"], re.findall(r"(?m)^      timeout-minutes:\s*(\d+)$", steps[name]))
        self.assertLess(names.index("Build Avalonia project"), names.index(self.PREPARE))
        self.assertEqual(
            ["15"],
            re.findall(r"(?m)^      timeout-minutes:\s*(\d+)$", steps["Build Avalonia project"]),
        )
        self.assertLess(names.index(self.PREPARE), names.index(self.CHECKPOINT))
        self.assertLess(names.index(self.CHECKPOINT), names.index(self.MAC_STEP))
        self.assertLess(names.index("CLI help test"), names.index(self.FINISH))
        self.assertLess(names.index(self.FINISH), names.index(self.SUMMARY))
        self.assertLess(names.index(self.SUMMARY), names.index("Upload test results"))
        checkpoint = steps[self.CHECKPOINT]
        for line in (
            "uses: actions/upload-artifact@v4",
            "name: core-context-${{ matrix.check_name }}-${{ github.run_attempt }}",
            "path: TestResults/core-ci-diagnostics/context.json",
            "if-no-files-found: error",
            "include-hidden-files: false",
            "retention-days: 7",
            "compression-level: 0",
        ):
            self.assertIn(line, checkpoint)
        for line in (
            "uses: actions/upload-artifact@v4",
            "name: core-sequence-${{ matrix.check_name }}-${{ github.run_attempt }}",
            "path: TestResults/core-ci-diagnostics/sequence-summary.json",
            "if-no-files-found: error",
            "include-hidden-files: false",
            "retention-days: 7",
            "compression-level: 0",
        ):
            self.assertIn(line, steps[self.SUMMARY])
        upload = steps["Upload test results"]
        self.assertEqual(["always()"], re.findall(r"(?m)^      if:\s*(.+)$", upload))
        for line in (
            "uses: actions/upload-artifact@v4",
            "name: test-results-${{ matrix.check_name }}",
            "if-no-files-found: error",
            "include-hidden-files: false",
            "retention-days: 7",
        ):
            self.assertIn(line, upload)
        match = re.search(r"(?m)^        path: \|\n((?:          .*\n)+)", upload)
        self.assertIsNotNone(match)
        self.assertEqual(
            [
                "**/core-tests.trx",
                "**/avalonia-tests.trx",
            ],
            [line.strip() for line in match.group(1).splitlines()],
        )
        for name in ("Run Core tests", self.MAC_STEP):
            command = run_command(steps[name])
            self.assertNotRegex(
                command,
                r"--(?:filter|list-tests|no-build|no-restore|diag|blame-crash)\b|"
                r"verbosity=(?:normal|detailed)|\|\||\bretry\b",
            )
        self.assertNotIn("continue-on-error:", build)
        self.assertEqual(
            ["ubuntu-latest", "macos-15", "windows-latest"],
            re.findall(r"(?m)^            runner: (\S+)$", build),
        )
        permissions = re.search(r"(?ms)^permissions:\n(.*?)(?=^\S)", text)
        self.assertIsNotNone(permissions)
        self.assertEqual("contents: read", permissions.group(1).strip())

    def test_live_workflow_has_mac_only_bounded_no_dump_diagnostics(self) -> None:
        self.assert_contract(WORKFLOW_PATH.read_text(encoding="utf-8"))

    def test_matrix_partition_selects_one_complete_core_invocation(self) -> None:
        steps = dict(named_steps(all_job_blocks(WORKFLOW_PATH.read_text(encoding="utf-8"))["build"]))
        self.assertIn(self.MAC_STEP, steps)
        for runner in ("ubuntu-latest", "macos-15", "windows-latest"):
            with self.subTest(runner=runner):
                selected = [name for name, condition in (
                    ("Run Core tests", runner != "macos-15"),
                    (self.MAC_STEP, runner == "macos-15"),
                ) if condition]
                self.assertEqual(1, len(selected))
                command = run_command(steps[selected[0]])
                self.assertEqual(self.MAC_COMMAND if runner == "macos-15" else self.CORE_COMMAND, command)

    def test_live_contract_rejects_diagnostic_bypasses(self) -> None:
        text = WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assert_contract(text)
        mutations = (
            ("no dump", "--blame-hang-dump-type none", "--blame-hang-dump-type full"),
            ("hang limit", "--blame-hang-timeout 5m", "--blame-hang-timeout 0"),
            ("step limit", "      timeout-minutes: 15", "      timeout-minutes: 0"),
            ("filter", self.MAC_COMMAND, self.MAC_COMMAND + " --filter FullyQualifiedName~One"),
            ("trace", self.MAC_COMMAND, self.MAC_COMMAND + " --diag private.log"),
            ("crash", self.MAC_COMMAND, self.MAC_COMMAND + " --blame-crash"),
            ("mask exit", self.MAC_COMMAND, self.MAC_COMMAND + " || true"),
            ("runner", "            runner: macos-15", "            runner: macos-14"),
            ("permission", "  contents: read", "  contents: write"),
            ("overlap", "matrix.runner != 'macos-15'", "matrix.runner == 'macos-15'"),
            ("hidden", "include-hidden-files: false", "include-hidden-files: true"),
            ("missing files", "if-no-files-found: error", "if-no-files-found: ignore"),
            ("broad upload", "**/core-tests.trx", "TestResults/**"),
            ("retention", "retention-days: 7", "retention-days: 90"),
            ("wrong phase", "ci_core_diagnostics.py prepare", "ci_core_diagnostics.py finish"),
            ("wrong producer", "steps.core_diagnostics_finish.outcome", "steps.other_producer.outcome"),
            ("missing producer", " && steps.core_diagnostics_finish.outcome == 'success'", ""),
            ("producer bypass", " && steps.core_diagnostics_finish.outcome", " || steps.core_diagnostics_finish.outcome"),
            ("unconditional context", "**/avalonia-tests.trx\n", "**/avalonia-tests.trx\n          TestResults/core-ci-diagnostics/context.json\n"),
            ("unconditional summary", "**/avalonia-tests.trx\n", "**/avalonia-tests.trx\n          TestResults/core-ci-diagnostics/sequence-summary.json\n"),
        )
        for label, before, after in mutations:
            with self.subTest(label=label):
                self.assertIn(before, text)
                with self.assertRaises(AssertionError):
                    self.assert_contract(text.replace(before, after, 1))

    @staticmethod
    def publication_targets(
        workflow: str, runner: str, outcomes: dict[str, str], *,
        job_success: bool, after_core: bool = False,
    ) -> list[tuple[str, str]]:
        """Interpret only the workflow's closed upload-condition vocabulary."""
        selected = []
        for name, step in named_steps(all_job_blocks(workflow)["build"]):
            if "uses: actions/upload-artifact@v4" not in step:
                continue
            if after_core and name == MacCoreDiagnosticsWorkflowTests.CHECKPOINT:
                continue
            match = re.search(r"(?m)^      if:\s*(.+)$", step)
            condition = match.group(1) if match else ""
            if condition.startswith("${{") and condition.endswith("}}"):
                condition = condition[3:-2].strip()
            terms = [term.strip() for term in condition.split("&&") if term.strip()]
            eligible = "always()" in terms or job_success
            for term in terms:
                if term == "always()":
                    continue
                matrix = re.fullmatch(r"matrix\.runner (==|!=) '([^']+)'", term)
                producer = re.fullmatch(r"steps\.([a-z_]+)\.outcome == 'success'", term)
                if matrix:
                    matches = runner == matrix.group(2)
                    eligible &= matches if matrix.group(1) == "==" else not matches
                elif producer:
                    eligible &= outcomes.get(producer.group(1), "skipped") == "success"
                else:
                    raise AssertionError("Unsupported upload condition")
            if not eligible:
                continue
            path = re.search(r"(?m)^        path:\s*(.+)$", step)
            if path is None:
                raise AssertionError("Missing artifact path")
            if path.group(1) == "|":
                block = re.search(r"(?m)^        path: \|\n((?:          .*\n)+)", step)
                if block is None:
                    raise AssertionError("Missing artifact path block")
                paths = [line.strip() for line in block.group(1).splitlines()]
            else:
                paths = [path.group(1)]
            selected.extend((name, value) for value in paths)
        return selected

    def test_current_success_and_mac_os_are_required_for_each_diagnostic_upload(self) -> None:
        workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        for runner in ("ubuntu-latest", "macos-15", "windows-latest"):
            for prepare in ("success", "failure", "cancelled", "skipped"):
                for finish in ("success", "failure", "cancelled", "skipped"):
                    with self.subTest(runner=runner, prepare=prepare, finish=finish):
                        targets = self.publication_targets(
                            workflow, runner, {
                                "core_diagnostics_prepare": prepare,
                                "core_diagnostics_finish": finish,
                            }, job_success=False,
                        )
                        diagnostics = {path for _, path in targets if path.endswith(".json")}
                        expected = set()
                        if runner == "macos-15" and prepare == "success":
                            expected.add("TestResults/core-ci-diagnostics/context.json")
                        if runner == "macos-15" and finish == "success":
                            expected.add("TestResults/core-ci-diagnostics/sequence-summary.json")
                        self.assertEqual(expected, diagnostics)
                        self.assertIn(("Upload test results", "**/core-tests.trx"), targets)
                        self.assertIn(("Upload test results", "**/avalonia-tests.trx"), targets)


class CoreDiagnosticCollectorTests(unittest.TestCase):
    def setUp(self) -> None:
        helper = ROOT / "scripts" / "ci_core_diagnostics.py"
        self.assertTrue(helper.is_file(), "The planned public diagnostic collector is missing")
        self.diag = importlib.import_module("scripts.ci_core_diagnostics")
        fixture_root = ROOT / "TestResults" / "core-diagnostic-contract-fixtures"
        fixture_root.mkdir(parents=True, exist_ok=True)
        temporary = tempfile.TemporaryDirectory(prefix="case-", dir=fixture_root)
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.directory = self.root / "TestResults" / "core-ci-diagnostics"
        self.raw = self.directory / "raw"
        self.env = {
            "RUNNER_OS": "macOS", "RUNNER_ARCH": "ARM64", "RUNNER_NAME": "fixture-runner",
            "ImageOS": "macos-15-arm64", "ImageVersion": "fixture-image",
            "GITHUB_SHA": "c" * 40, "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_TOKEN": "DO_NOT_EXPORT_CREDENTIAL",
            "UNRELATED_SECRET": "DO_NOT_EXPORT_OTHER_VALUE",
        }
        self.host = {
            "system": "Darwin", "release": "fixture-release", "version": "fixture-version",
            "machine": "arm64", "python": "3.12.10",
        }
        self.responses = {
            ("git", "rev-parse", "HEAD"): "a" * 40,
            ("git", "rev-parse", "HEAD^{tree}"): "b" * 40,
            ("dotnet", "--version"): "10.0.401",
            ("dotnet", "--list-runtimes"):
                "Microsoft.NETCore.App 10.0.12 [/PRIVATE_INSTALLATION]\n"
                "Microsoft.AspNetCore.App 10.0.12 [/PRIVATE_INSTALLATION]",
        }
        self.calls: list[tuple[str, ...]] = []
        for target in ("subprocess.run", "subprocess.Popen"):
            patcher = mock.patch(target, side_effect=AssertionError("No real process in tests"))
            patcher.start()
            self.addCleanup(patcher.stop)

    def query(self, command: tuple[str, ...]) -> str:
        self.calls.append(tuple(command))
        return self.responses[tuple(command)]

    def publication_targets(self, *, prepare: str, finish: str, after_core: bool = False):
        return MacCoreDiagnosticsWorkflowTests.publication_targets(
            WORKFLOW_PATH.read_text(encoding="utf-8"), "macos-15", {
                "core_diagnostics_prepare": prepare,
                "core_diagnostics_finish": finish,
            }, job_success=False, after_core=after_core,
        )

    def prepare(self) -> dict:
        return self.diag.prepare(self.root, environment=self.env, query=self.query, host=self.host)

    def sequence(self, name: str = "Sequence_fixture.xml", text: str | None = None,
                 encoding: str = "utf-8") -> Path:
        self.raw.mkdir(parents=True, exist_ok=True)
        path = self.raw / name
        path.write_bytes((text or (
            '<TestSequence><Test Name="Public.Tests.One" Completed="True" '
            'DisplayName="SECRET_DISPLAY" Source="/SECRET_SOURCE" />'
            '<Test Name="Public.Tests.Two" Completed="False" '
            'DisplayName="SECRET_ARGUMENT" Source="/SECRET_PATH" /></TestSequence>'
        )).encode(encoding))
        return path

    def test_prepare_whitelists_bounded_actual_metadata(self) -> None:
        result = self.prepare()
        data = (self.directory / "context.json").read_bytes()
        self.assertLessEqual(len(data), 8192)
        self.assertEqual(result, json.loads(data))
        self.assertEqual("a" * 40, result["git_head"])
        self.assertEqual("b" * 40, result["git_tree"])
        self.assertEqual("10.0.401", result["sdk"])
        self.assertEqual(self.host, result["host"])
        self.assertEqual(4, len(self.calls))
        self.assertNotIn(b"DO_NOT_EXPORT", data)
        self.assertNotIn(b"PRIVATE_INSTALLATION", data)
        self.assertNotIn(b"GITHUB_TOKEN", data)
        self.assertEqual(
            [{"name": "Microsoft.NETCore.App", "version": "10.0.12"},
             {"name": "Microsoft.AspNetCore.App", "version": "10.0.12"}],
            result["runtimes"],
        )

    def test_unavailable_optional_image_is_not_invented(self) -> None:
        del self.env["ImageVersion"]
        result = self.prepare()
        self.assertIsNone(result["runner"]["ImageVersion"])
        self.assertIn("ImageVersion", result["unavailable"])

    def test_prepare_rejects_bad_and_oversized_query_data(self) -> None:
        for value in ("secret invalid sdk", "x" * 16385):
            with self.subTest(value_length=len(value)):
                self.responses[("dotnet", "--version")] = value
                with self.assertRaises(self.diag.DiagnosticError):
                    self.prepare()
        self.assertFalse((self.directory / "context.json").exists())

    def test_declared_limits_match_the_reviewed_budget(self) -> None:
        for name, expected in (
            ("CONTEXT_LIMIT", 8192), ("SUMMARY_LIMIT", 32768),
            ("QUERY_LIMIT", 16384), ("QUERY_SECONDS", 10),
            ("INPUT_LIMIT", 16 * 1024 * 1024), ("ENTRY_LIMIT", 256),
            ("SEQUENCE_LIMIT", 4), ("SUMMARY_ENTRIES", 32), ("NAME_LIMIT", 256),
        ):
            with self.subTest(name=name):
                self.assertEqual(expected, getattr(self.diag, name))

    def test_real_query_reader_bounds_bytes_and_preserves_failure(self) -> None:
        command = ("dotnet", "--version")
        for data, code, expected_error in (
            (b"10.0.401\n", 0, None),
            (b"SECRET_QUERY_OUTPUT", 7, "query"),
            (b"x" * 16385, 0, "query_limit"),
            (b"\xff", 0, "query"),
        ):
            with self.subTest(bytes=len(data), exit=code):
                process = mock.Mock()
                process.stdout.fileno.return_value = 123
                process.wait.return_value = code
                process.poll.return_value = code
                selector = mock.MagicMock()
                selector.__enter__.return_value = selector
                selector.select.return_value = [(123, 1)]
                remaining = io.BytesIO(data)
                sizes = []

                def read_pipe(fd, count):
                    self.assertEqual(123, fd)
                    sizes.append(count)
                    return remaining.read(count)

                with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                        mock.patch("subprocess.Popen", return_value=process) as spawn, \
                        mock.patch("selectors.DefaultSelector", return_value=selector), \
                        mock.patch("os.read", side_effect=read_pipe), \
                        mock.patch("time.monotonic", return_value=0):
                    if expected_error is None:
                        self.assertEqual("10.0.401", self.diag.query_metadata(command, self.root))
                    else:
                        with self.assertRaises(self.diag.DiagnosticError) as caught:
                            self.diag.query_metadata(command, self.root)
                        self.assertEqual(expected_error, str(caught.exception))
                        self.assertNotIn("SECRET", str(caught.exception))
                self.assertEqual(command, spawn.call_args.args[0])
                self.assertFalse(spawn.call_args.kwargs["shell"])
                self.assertEqual(subprocess.DEVNULL, spawn.call_args.kwargs["stderr"])
                self.assertEqual(subprocess.DEVNULL, spawn.call_args.kwargs["stdin"])
                self.assertTrue(all(0 < size <= 4096 for size in sizes))
                process.stdout.close.assert_called_once()
                process.kill.assert_not_called()

    def test_real_query_timeout_cleans_only_its_owned_process(self) -> None:
        process = mock.Mock()
        process.poll.return_value = None
        process.wait.return_value = 0
        selector = mock.MagicMock()
        selector.__enter__.return_value = selector
        selector.select.return_value = []
        with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                mock.patch("subprocess.Popen", return_value=process), \
                mock.patch("selectors.DefaultSelector", return_value=selector), \
                mock.patch("time.monotonic", return_value=0):
            with self.assertRaises(self.diag.DiagnosticError) as caught:
                self.diag.query_metadata(("dotnet", "--version"), self.root)
        self.assertEqual("query_timeout", str(caught.exception))
        selector.select.assert_called_once_with(10)
        process.kill.assert_called_once_with()
        process.wait.assert_called_once_with(timeout=1)
        process.stdout.close.assert_called_once()

    def test_real_query_rechecks_elapsed_deadline_before_read_and_wait(self) -> None:
        for times, chunks in (([0, 11], []), ([0, 0, 11], [b""])):
            with self.subTest(times=times):
                process = mock.Mock()
                process.poll.return_value = None
                process.wait.return_value = 0
                selector = mock.MagicMock()
                selector.__enter__.return_value = selector
                selector.select.return_value = [(123, 1)]
                with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                        mock.patch("subprocess.Popen", return_value=process), \
                        mock.patch("selectors.DefaultSelector", return_value=selector), \
                        mock.patch("time.monotonic", side_effect=times), \
                        mock.patch("os.read", side_effect=chunks):
                    with self.assertRaises(self.diag.DiagnosticError) as caught:
                        self.diag.query_metadata(("dotnet", "--version"), self.root)
                self.assertEqual("query_timeout", str(caught.exception))
                process.kill.assert_called_once_with()
                process.wait.assert_called_once_with(timeout=1)

    def test_real_query_spawn_and_cleanup_failures_are_redacted(self) -> None:
        with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                mock.patch("subprocess.Popen", side_effect=OSError("SECRET_SPAWN /PATH")):
            with self.assertRaises(self.diag.DiagnosticError) as caught:
                self.diag.query_metadata(("dotnet", "--version"), self.root)
        self.assertEqual("query", str(caught.exception))
        process = mock.Mock()
        process.poll.return_value = None
        process.wait.side_effect = subprocess.TimeoutExpired("SECRET_COMMAND", 1)
        selector = mock.MagicMock()
        selector.__enter__.return_value = selector
        selector.select.return_value = []
        with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                mock.patch("subprocess.Popen", return_value=process), \
                mock.patch("selectors.DefaultSelector", return_value=selector), \
                mock.patch("time.monotonic", return_value=0):
            with self.assertRaises(self.diag.DiagnosticError) as caught:
                self.diag.query_metadata(("dotnet", "--version"), self.root)
        self.assertEqual("query_cleanup", str(caught.exception))
        process.stdout.close.assert_called_once()

    def test_arbitrary_query_is_rejected_before_process_creation(self) -> None:
        with self.assertRaises(self.diag.DiagnosticError) as caught:
            self.diag.query_metadata(("dotnet", "test", "SECRET_TARGET"), self.root)
        self.assertEqual("query", str(caught.exception))

    def test_prepare_surfaces_query_failure_without_raw_disclosure(self) -> None:
        def failing_query(command: tuple[str, ...]) -> str:
            raise OSError("SECRET_QUERY /SECRET_PATH")

        with self.assertRaises(self.diag.DiagnosticError) as caught:
            self.diag.prepare(self.root, environment=self.env, query=failing_query, host=self.host)
        self.assertNotIn("SECRET", str(caught.exception))
        self.assertFalse((self.directory / "context.json").exists())

    def test_context_output_is_exclusive_and_size_limited(self) -> None:
        self.prepare()
        original = (self.directory / "context.json").read_bytes()
        with self.assertRaises(self.diag.DiagnosticError):
            self.prepare()
        self.assertEqual(original, (self.directory / "context.json").read_bytes())
        (self.directory / "context.json").unlink()
        with mock.patch.object(self.diag, "CONTEXT_LIMIT", 16):
            with self.assertRaises(self.diag.DiagnosticError):
                self.prepare()
        self.assertFalse((self.directory / "context.json").exists())

    def test_output_io_failure_is_redacted_at_main_boundary(self) -> None:
        self.sequence()
        output = io.StringIO()
        with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                mock.patch.object(self.diag.sys, "platform", "darwin"), \
                mock.patch.object(self.diag, "finish", side_effect=OSError("SECRET_IO /PATH")), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            self.assertEqual(1, self.diag.main(["finish"], workspace=self.root))
        self.assertEqual("CoreDiagnostics=failed;Reason=io\n", output.getvalue())

    def test_metadata_controls_and_runtime_paths_never_leak(self) -> None:
        for value in ("bad\nSECRET_CONTROL", "x" * 257):
            with self.subTest(length=len(value)):
                self.env["ImageVersion"] = value
                with self.assertRaises(self.diag.DiagnosticError) as caught:
                    self.prepare()
                self.assertNotIn("SECRET", str(caught.exception))
        self.assertFalse((self.directory / "context.json").exists())

    def test_sequence_projection_keeps_symbols_not_displays_paths_or_output(self) -> None:
        self.sequence()
        result = self.diag.finish(self.root)
        data = (self.directory / "sequence-summary.json").read_bytes()
        self.assertEqual(result, json.loads(data))
        self.assertLessEqual(len(data), 32768)
        self.assertEqual("available", result["status"])
        self.assertEqual(2, result["total_tests"])
        self.assertEqual(1, result["incomplete_tests"])
        self.assertEqual(["Public.Tests.One", "Public.Tests.Two"],
                         [entry["name"] for entry in result["entries"]])
        self.assertNotIn(b"SECRET", data)
        self.assertNotIn(b"DisplayName", data)
        self.assertNotIn(b"Source", data)

    def test_second_documented_sequence_name_and_utf16_are_supported(self) -> None:
        self.sequence("fixture_Sequence.xml", encoding="utf-16")
        self.assertEqual(2, self.diag.finish(self.root)["total_tests"])

    def test_missing_sequence_is_unavailable_not_a_test_result(self) -> None:
        result = self.diag.finish(self.root)
        self.assertEqual("unavailable", result["status"])
        self.assertEqual(0, result["total_tests"])
        self.assertEqual([], result["entries"])
        self.assertNotIn("passed", result)
        self.assertNotIn("failed_test", result)

    def test_encoding_aware_dtd_and_entity_refusal(self) -> None:
        text = '<!DOCTYPE TestSequence [<!ENTITY private "SECRET_ENTITY">]><TestSequence />'
        for encoding in ("utf-8", "utf-16", "utf-16-le", "utf-16-be"):
            with self.subTest(encoding=encoding):
                path = self.sequence(text=text, encoding=encoding)
                with self.assertRaises(self.diag.DiagnosticError) as caught:
                    self.diag.finish(self.root)
                self.assertNotIn("SECRET", str(caught.exception))
                path.unlink()
        self.assertFalse((self.directory / "sequence-summary.json").exists())

    def test_external_entity_and_encoded_unsupported_xml_fail_closed(self) -> None:
        for text in (
            '<!DOCTYPE TestSequence SYSTEM "file:///SECRET"><TestSequence />',
            '<!DOCTYPE TestSequence [<!ENTITY % x SYSTEM "https://invalid/SECRET">%x;]><TestSequence />',
            '<?private SECRET?><TestSequence />',
        ):
            path = self.sequence(text=text, encoding="utf-16")
            with self.assertRaises(self.diag.DiagnosticError) as caught:
                self.diag.finish(self.root)
            self.assertNotIn("SECRET", str(caught.exception))
            path.unlink()
        self.sequence(text="<TestSequence />", encoding="utf-32")
        with self.assertRaises(self.diag.DiagnosticError):
            self.diag.finish(self.root)

    def test_malformed_structure_and_non_symbol_names_are_refused(self) -> None:
        examples = (
            "<TestSequence>",
            "<Unexpected />",
            '<TestSequence><Test Name="Public.Tests.One" Completed="maybe" /></TestSequence>',
            '<TestSequence><Test Name="Public.Tests.One(password=SECRET)" Completed="False" /></TestSequence>',
        )
        for text in examples:
            with self.subTest(text_kind=text[:20]):
                path = self.sequence(text=text)
                with self.assertRaises(self.diag.DiagnosticError) as caught:
                    self.diag.finish(self.root)
                self.assertNotIn("SECRET", str(caught.exception))
                path.unlink()

    def test_sequence_and_traversal_quotas_fail_closed(self) -> None:
        self.sequence()
        for limit, value in (("INPUT_LIMIT", 1), ("ENTRY_LIMIT", 0), ("SEQUENCE_LIMIT", 0)):
            with self.subTest(limit=limit), mock.patch.object(self.diag, limit, value):
                with self.assertRaises(self.diag.DiagnosticError):
                    self.diag.finish(self.root)
        for index in range(4):
            self.sequence(f"Sequence_{index}.xml")
        with self.assertRaises(self.diag.DiagnosticError):
            self.diag.finish(self.root)

    def test_input_limit_accepts_exact_boundary_and_rejects_aggregate(self) -> None:
        first = self.sequence(text="<TestSequence />")
        exact = first.stat().st_size
        with mock.patch.object(self.diag, "INPUT_LIMIT", exact):
            self.assertEqual("available", self.diag.finish(self.root)["status"])
        (self.directory / "sequence-summary.json").unlink()
        self.sequence("Sequence_second.xml", text="<TestSequence />")
        with mock.patch.object(self.diag, "INPUT_LIMIT", exact):
            with self.assertRaises(self.diag.DiagnosticError):
                self.diag.finish(self.root)

    def test_sequence_entry_and_symbol_limits_include_exact_boundaries(self) -> None:
        name = "Public." + "X" * (self.diag.NAME_LIMIT - len("Public."))
        path = self.sequence(
            text=f'<TestSequence><Test Name="{name}" Completed="True" /></TestSequence>'
        )
        with mock.patch.object(self.diag, "ENTRY_LIMIT", 1), \
                mock.patch.object(self.diag, "SEQUENCE_LIMIT", 1):
            self.assertEqual(name, self.diag.finish(self.root)["entries"][0]["name"])
        (self.directory / "sequence-summary.json").unlink()
        path.write_text(
            f'<TestSequence><Test Name="{name}X" Completed="True" /></TestSequence>',
            encoding="utf-8",
        )
        with self.assertRaises(self.diag.DiagnosticError) as caught:
            self.diag.finish(self.root)
        self.assertEqual("symbol", str(caught.exception))

    def test_output_limit_accepts_exact_boundary(self) -> None:
        self.sequence()
        self.diag.finish(self.root)
        path = self.directory / "sequence-summary.json"
        exact = len(path.read_bytes())
        path.unlink()
        with mock.patch.object(self.diag, "SUMMARY_LIMIT", exact):
            self.diag.finish(self.root)
        self.assertEqual(exact, len(path.read_bytes()))
        path.unlink()
        with mock.patch.object(self.diag, "SUMMARY_LIMIT", exact - 1):
            with self.assertRaises(self.diag.DiagnosticError):
                self.diag.finish(self.root)

    def test_summary_is_bounded_and_exclusive(self) -> None:
        text = "<TestSequence>" + "".join(
            f'<Test Name="Public.Tests.Case{index}" Completed="False" />' for index in range(40)
        ) + "</TestSequence>"
        self.sequence(text=text)
        result = self.diag.finish(self.root)
        self.assertEqual(40, result["total_tests"])
        self.assertLessEqual(len(result["entries"]), 32)
        self.assertTrue(result["truncated"])
        path = self.directory / "sequence-summary.json"
        original = path.read_bytes()
        with self.assertRaises(self.diag.DiagnosticError):
            self.diag.finish(self.root)
        self.assertEqual(original, path.read_bytes())
        path.unlink()
        with mock.patch.object(self.diag, "SUMMARY_LIMIT", 16):
            with self.assertRaises(self.diag.DiagnosticError):
                self.diag.finish(self.root)
        self.assertFalse(path.exists())

    def test_unknown_files_and_dump_content_are_not_opened(self) -> None:
        self.raw.mkdir(parents=True)
        (self.raw / "ignored.dmp").write_bytes(b"SECRET_DUMP")
        (self.raw / "ignored.log").write_text("SECRET_RAW_TRACE", encoding="utf-8")
        result = self.diag.finish(self.root)
        self.assertEqual("unavailable", result["status"])
        self.assertNotIn("SECRET", (self.directory / "sequence-summary.json").read_text(encoding="utf-8"))

    def test_linklike_file_and_ancestor_are_refused_without_host_privileges(self) -> None:
        path = self.sequence()
        real_lstat = os.lstat
        real_stat = os.stat
        for target in (path, self.raw):
            def linked_lstat(candidate, *args, **kwargs):
                result = real_lstat(candidate, *args, **kwargs)
                if Path(candidate) == target:
                    values = list(result)
                    values[stat.ST_MODE] = stat.S_IFLNK | 0o777
                    return os.stat_result(values)
                return result

            def linked_stat(candidate, *args, **kwargs):
                result = real_stat(candidate, *args, **kwargs)
                if candidate == target.name and kwargs.get("dir_fd") is not None:
                    values = list(result)
                    values[stat.ST_MODE] = stat.S_IFLNK | 0o777
                    return os.stat_result(values)
                return result

            with self.subTest(target_kind=target.name), \
                    mock.patch("os.lstat", side_effect=linked_lstat), \
                    mock.patch("os.stat", side_effect=linked_stat):
                with self.assertRaises(self.diag.DiagnosticError):
                    self.diag.finish(self.root)
        self.assertFalse((self.directory / "sequence-summary.json").exists())

    def test_cli_refuses_platform_without_real_no_follow_primitives(self) -> None:
        output = io.StringIO()
        with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=False), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            self.assertEqual(1, self.diag.main(["finish"], workspace=self.root))
        self.assertEqual("CoreDiagnostics=failed;Reason=platform\n", output.getvalue())
        self.assertFalse(self.directory.exists())

    def test_workspace_traversal_is_refused(self) -> None:
        with self.assertRaises(self.diag.DiagnosticError):
            self.diag.finish(self.root / ".." / "unowned")
        with self.assertRaises(self.diag.DiagnosticError):
            self.diag.finish(self.root / ("x" * 4097))

    def test_reparse_hardlink_and_special_file_metadata_are_refused(self) -> None:
        for mode, links, attributes in (
            (stat.S_IFREG | 0o600, 2, 0),
            (stat.S_IFIFO | 0o600, 1, 0),
            (stat.S_IFREG | 0o600, 1, 0x400),
        ):
            with self.subTest(mode=mode, links=links, attributes=attributes):
                info = mock.Mock(st_mode=mode, st_nlink=links, st_file_attributes=attributes)
                with self.assertRaises(self.diag.DiagnosticError) as caught:
                    self.diag._regular(info, directory=False)
                self.assertEqual("path", str(caught.exception))

    def test_actual_directory_open_uses_no_follow_for_each_component(self) -> None:
        directory_info = os.stat_result((stat.S_IFDIR | 0o700, 1, 1, 1, 0, 0, 0, 0, 0, 0))
        opened = []
        closed = []
        serial = 10

        def fake_open(path, flags, *args, **kwargs):
            nonlocal serial
            serial += 1
            opened.append((path, flags, kwargs.get("dir_fd")))
            return serial

        with mock.patch.object(self.diag, "_descriptor_paths_supported", return_value=True), \
                mock.patch.object(os, "O_DIRECTORY", 0x10000, create=True), \
                mock.patch.object(os, "O_NOFOLLOW", 0x20000, create=True), \
                mock.patch("os.open", side_effect=fake_open), \
                mock.patch("os.fstat", return_value=directory_info), \
                mock.patch("os.close", side_effect=closed.append):
            with self.diag._directory(self.root) as descriptor:
                self.assertEqual(serial, descriptor)
        self.assertEqual(len(self.root.parts), len(opened))
        self.assertTrue(all(flags & 0x10000 and flags & 0x20000 for _, flags, _ in opened))
        self.assertIsNone(opened[0][2])
        self.assertTrue(all(parent is not None for _, _, parent in opened[1:]))
        self.assertEqual(list(range(11, serial + 1)), closed)

    def test_actual_file_open_checks_identity_and_exclusive_creation(self) -> None:
        self.sequence()
        regular = os.stat_result((stat.S_IFREG | 0o600, 7, 1, 1, 0, 0, 2, 0, 0, 0))
        different = os.stat_result((stat.S_IFREG | 0o600, 8, 1, 1, 0, 0, 2, 0, 0, 0))

        @contextlib.contextmanager
        def directory(*args, **kwargs):
            yield 10

        with mock.patch.object(self.diag, "_directory", side_effect=directory), \
                mock.patch.object(os, "O_NOFOLLOW", 0x20000, create=True), \
                mock.patch("os.stat", return_value=regular), \
                mock.patch("os.open", return_value=11) as opened, \
                mock.patch("os.fstat", return_value=different), \
                mock.patch("os.close") as closed:
            with self.assertRaises(self.diag.DiagnosticError):
                with self.diag._file(self.raw / "Sequence_fixture.xml"):
                    self.fail("A changed file identity must not be readable")
            self.assertTrue(opened.call_args.args[1] & 0x20000)
            self.assertEqual(10, opened.call_args.kwargs["dir_fd"])
            closed.assert_called_once_with(11)
        with mock.patch.object(self.diag, "_directory", side_effect=directory), \
                mock.patch.object(os, "O_NOFOLLOW", 0x20000, create=True), \
                mock.patch("os.open", return_value=11) as opened, \
                mock.patch("os.fstat", return_value=regular), \
                mock.patch("os.close"):
            with self.diag._file(self.directory / "context.json", write=True):
                pass
            flags = opened.call_args.args[1]
            self.assertTrue(flags & os.O_EXCL and flags & os.O_CREAT and flags & 0x20000)

    def test_actual_writer_partial_failure_stays_bounded_and_redacted(self) -> None:
        writes = []

        @contextlib.contextmanager
        def file(*args, **kwargs):
            yield 11

        def partial_then_fail(descriptor, data):
            writes.append(bytes(data))
            if len(writes) == 1:
                return 1
            raise OSError("SECRET_PARTIAL /PATH")

        with mock.patch.object(self.diag, "_file", side_effect=file), \
                mock.patch("os.write", side_effect=partial_then_fail), \
                mock.patch("os.fsync") as flush:
            with self.assertRaises(self.diag.DiagnosticError) as caught:
                self.prepare()
        self.assertEqual("io", str(caught.exception))
        self.assertEqual(2, len(writes))
        self.assertLessEqual(len(writes[0]), self.diag.CONTEXT_LIMIT)
        self.assertEqual(writes[0][1:], writes[1])
        self.assertNotIn(b"SECRET", writes[0])
        flush.assert_not_called()

    def test_main_reports_one_bounded_constant_error_without_raw_input(self) -> None:
        self.sequence(text='<!DOCTYPE TestSequence [<!ENTITY x "SECRET_ENTITY">]><TestSequence />')
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            code = self.diag.main(["finish"], workspace=self.root)
        self.assertEqual(1, code)
        self.assertLessEqual(len(output.getvalue().encode("utf-8")), 256)
        self.assertNotIn("SECRET", output.getvalue())
        self.assertNotIn(str(self.root), output.getvalue())

    def test_refused_existing_summary_is_not_selected_for_publication(self) -> None:
        self.directory.mkdir(parents=True)
        output = self.directory / "sequence-summary.json"
        for payload in (b"SECRET_RAW_SUMMARY", b"X" * 32769):
            with self.subTest(bytes=len(payload)):
                output.write_bytes(payload)
                with self.assertRaises(self.diag.DiagnosticError):
                    self.diag.finish(self.root)
                self.assertEqual(payload, output.read_bytes())
                targets = self.publication_targets(prepare="success", finish="failure", after_core=True)
                self.assertNotIn("TestResults/core-ci-diagnostics/sequence-summary.json",
                                 [path for _, path in targets])
                self.assertIn(("Upload test results", "**/core-tests.trx"), targets)

    def test_refused_existing_context_is_not_selected_by_any_later_upload(self) -> None:
        self.directory.mkdir(parents=True)
        output = self.directory / "context.json"
        for payload in (b"SECRET_RAW_CONTEXT", b"X" * 8193):
            with self.subTest(bytes=len(payload)):
                output.write_bytes(payload)
                with self.assertRaises(self.diag.DiagnosticError):
                    self.prepare()
                self.assertEqual(payload, output.read_bytes())
                targets = self.publication_targets(prepare="failure", finish="skipped")
                self.assertNotIn("TestResults/core-ci-diagnostics/context.json",
                                 [path for _, path in targets])

    def test_linklike_output_refusal_is_not_a_publication_fallback(self) -> None:
        self.directory.mkdir(parents=True)
        output = self.directory / "sequence-summary.json"
        output.write_bytes(b"SECRET_LINK_TARGET")
        real_open = os.open

        def reject_output(path, flags, *args, **kwargs):
            if Path(path).name == output.name:
                raise OSError("SECRET_LINK_REFUSAL")
            return real_open(path, flags, *args, **kwargs)

        with mock.patch("os.open", side_effect=reject_output):
            with self.assertRaises(self.diag.DiagnosticError):
                self.diag.finish(self.root)
        self.assertEqual(b"SECRET_LINK_TARGET", output.read_bytes())
        targets = self.publication_targets(prepare="success", finish="failure", after_core=True)
        self.assertNotIn("TestResults/core-ci-diagnostics/sequence-summary.json",
                         [path for _, path in targets])

    def test_context_replaced_after_checkpoint_is_never_reuploaded(self) -> None:
        self.prepare()
        path = self.directory / "context.json"
        checkpoint = path.read_bytes()
        path.write_bytes(b"SECRET_POST_CHECKPOINT_REPLACEMENT")
        self.diag.finish(self.root)
        targets = self.publication_targets(prepare="success", finish="success", after_core=True)
        self.assertNotIn("TestResults/core-ci-diagnostics/context.json", [value for _, value in targets])
        self.assertEqual(b"SECRET_POST_CHECKPOINT_REPLACEMENT", path.read_bytes())
        self.assertNotIn(b"SECRET", checkpoint)
        self.assertIn(
            (MacCoreDiagnosticsWorkflowTests.SUMMARY, "TestResults/core-ci-diagnostics/sequence-summary.json"),
            targets,
        )

    def test_core_failure_can_publish_only_a_successful_sanitized_finish(self) -> None:
        self.sequence()
        summary = self.diag.finish(self.root)
        self.assertEqual("available", summary["status"])
        targets = self.publication_targets(prepare="success", finish="success", after_core=True)
        self.assertEqual(
            [("Upload macOS Core diagnostic summary", "TestResults/core-ci-diagnostics/sequence-summary.json")],
            [(name, path) for name, path in targets if path.endswith(".json")],
        )
        self.assertIn(("Upload test results", "**/core-tests.trx"), targets)


if __name__ == "__main__":
    unittest.main()
