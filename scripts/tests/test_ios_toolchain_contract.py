# SPDX-License-Identifier: GPL-3.0-or-later
"""Keep both iOS CI builds on the same supported SDK, workload and Xcode."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

from scripts.tests.test_crossplatform_workflow import all_job_blocks


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = (
    (ROOT / ".github" / "workflows" / "ios.yml", "ios-build"),
    (ROOT / ".github" / "workflows" / "release.yml", "ios"),
)
SDK_VERSION = "10.0.401"
WORKLOAD_COMMAND = f"dotnet workload install ios --version {SDK_VERSION}"
XCODE_PATH = "/Applications/Xcode_26.6.app/Contents/Developer"
PIN_SDK_COMMAND = (
    f"""printf '%s\\n' '{{"sdk":{{"version":"{SDK_VERSION}","rollForward":"disable"}}}}' > global.json"""
)
VERIFY_SDK_COMMAND = f'test "$(dotnet --version)" = "{SDK_VERSION}"'


def ios_steps(block: str) -> dict[str, str]:
    matches = list(re.finditer(r"(?m)^      - name: (.+)\n", block))
    return {
        match.group(1): block[match.start() : matches[index + 1].start() if index + 1 < len(matches) else len(block)]
        for index, match in enumerate(matches)
    }


def assert_ios_toolchain(workflow: str, job: str) -> None:
    block = all_job_blocks(workflow)[job]
    assert re.search(r"(?m)^    runs-on: macos-26$", block), "Expected macos-26 runner"
    steps = ios_steps(block)
    assert {"Select Xcode 26.6", "Setup .NET 10.0", "Pin selected .NET SDK", "Install ios workload"} <= steps.keys(), (
        "Required toolchain step is missing"
    )
    for name in ("Select Xcode 26.6", "Setup .NET 10.0", "Pin selected .NET SDK", "Install ios workload"):
        assert not re.search(r"(?m)^        (?:if|continue-on-error)\s*:", steps[name]), (
            f"Required toolchain step must be unconditional: {name}"
        )
    select = steps["Select Xcode 26.6"]
    assert re.search(
        rf"(?m)^          sudo xcode-select --switch {re.escape(XCODE_PATH)}$", select
    ), "Expected preinstalled Xcode 26.6"
    sdk = steps["Setup .NET 10.0"]
    assert re.search(
        rf"(?m)^          dotnet-version: '{re.escape(SDK_VERSION)}'$", sdk
    ), "SDK must be pinned to the workload-set's release"
    pin = steps["Pin selected .NET SDK"]
    assert re.search(rf"(?m)^          {re.escape(PIN_SDK_COMMAND)}$", pin), "SDK selection must be exact"
    assert re.search(rf"(?m)^          {re.escape(VERIFY_SDK_COMMAND)}$", pin), "Selected SDK must be verified"
    install = steps["Install ios workload"]
    assert re.search(
        rf"(?m)^        run: {re.escape(WORKLOAD_COMMAND)}$", install
    ), "iOS workload set must be pinned"
    names = list(steps)
    assert names.index("Select Xcode 26.6") < names.index("Setup .NET 10.0") < names.index(
        "Pin selected .NET SDK"
    ) < names.index("Install ios workload"), "Select Xcode and pin the SDK before workload installation"


class IosToolchainContractTests(unittest.TestCase):
    def test_project_build_guide_uses_canonical_ios_setup(self) -> None:
        project = (ROOT / "FEBuilderGBA.iOS" / "FEBuilderGBA.iOS.csproj").read_text(encoding="utf-8")
        guide = project.split("<PropertyGroup>", 1)[0]
        self.assertIn("docs/IOS.md", guide)
        self.assertNotIn("dotnet workload install ios", guide)

    def test_both_ios_builds_use_supported_toolchain(self) -> None:
        for path, job in WORKFLOWS:
            with self.subTest(workflow=path.name):
                assert_ios_toolchain(path.read_text(encoding="utf-8"), job)

    def test_toolchain_drift_is_rejected_in_each_workflow(self) -> None:
        for path, job in WORKFLOWS:
            workflow = path.read_text(encoding="utf-8")
            mutations = {
                "floating SDK": (f"dotnet-version: '{SDK_VERSION}'", "dotnet-version: '10.0.x'"),
                "floating workload": (WORKLOAD_COMMAND, "dotnet workload install ios"),
                "wrong workload": (WORKLOAD_COMMAND, "dotnet workload install ios --version 10.0.401.1"),
                "wrong Xcode": (XCODE_PATH, "/Applications/Xcode_26.5.app/Contents/Developer"),
            }
            for name, (before, after) in mutations.items():
                with self.subTest(workflow=path.name, mutation=name):
                    self.assertIn(before, workflow)
                    with self.assertRaises(AssertionError):
                        assert_ios_toolchain(workflow.replace(before, after, 1), job)

    def test_disabled_toolchain_steps_are_rejected(self) -> None:
        for path, job in WORKFLOWS:
            workflow = path.read_text(encoding="utf-8")
            block = all_job_blocks(workflow)[job]
            for step in ("Select Xcode 26.6", "Setup .NET 10.0", "Pin selected .NET SDK", "Install ios workload"):
                before = f"      - name: {step}\n"
                self.assertIn(before, block)
                for metadata in ("if: false", "continue-on-error: true"):
                    with self.subTest(workflow=path.name, step=step, metadata=metadata):
                        changed_block = block.replace(before, before + f"        {metadata}\n", 1)
                        changed = workflow.replace(block, changed_block, 1)
                        with self.assertRaises(AssertionError):
                            assert_ios_toolchain(changed, job)

    def test_sdk_selection_drift_is_rejected(self) -> None:
        for path, job in WORKFLOWS:
            workflow = path.read_text(encoding="utf-8")
            block = all_job_blocks(workflow)[job]
            mutations = {
                "missing pin": (f"          {PIN_SDK_COMMAND}\n", ""),
                "wrong version": (PIN_SDK_COMMAND, PIN_SDK_COMMAND.replace(SDK_VERSION, "10.0.402")),
                "allow roll forward": (PIN_SDK_COMMAND, PIN_SDK_COMMAND.replace("disable", "latestFeature")),
                "missing verification": (f"          {VERIFY_SDK_COMMAND}\n", ""),
            }
            for name, (before, after) in mutations.items():
                with self.subTest(workflow=path.name, mutation=name):
                    self.assertIn(before, block)
                    changed = workflow.replace(block, block.replace(before, after, 1), 1)
                    with self.assertRaises(AssertionError):
                        assert_ios_toolchain(changed, job)


if __name__ == "__main__":
    unittest.main()
