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
    assert {"Select Xcode 26.6", "Setup .NET 10.0", "Install ios workload"} <= steps.keys(), (
        "Required toolchain step is missing"
    )
    select = steps["Select Xcode 26.6"]
    assert re.search(
        rf"(?m)^          sudo xcode-select --switch {re.escape(XCODE_PATH)}$", select
    ), "Expected preinstalled Xcode 26.6"
    sdk = steps["Setup .NET 10.0"]
    assert re.search(
        rf"(?m)^          dotnet-version: '{re.escape(SDK_VERSION)}'$", sdk
    ), "SDK must be pinned to the workload-set's release"
    install = steps["Install ios workload"]
    assert re.search(
        rf"(?m)^        run: {re.escape(WORKLOAD_COMMAND)}$", install
    ), "iOS workload set must be pinned"
    names = list(steps)
    assert names.index("Select Xcode 26.6") < names.index("Setup .NET 10.0") < names.index(
        "Install ios workload"
    ), "Select Xcode and SDK before workload installation"


class IosToolchainContractTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
