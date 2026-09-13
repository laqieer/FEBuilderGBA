# SPDX-License-Identifier: GPL-3.0-or-later
"""Source/data contracts; production behavior is exercised by test-pure.ps1."""

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "scripts" / "OfflinePatchImportProof"


class OfflinePatchImportProofContractTests(unittest.TestCase):
    def test_complete_public_inventory(self):
        expected = {
            "Desktop.cs", "Policy.cs", "Policy.Tests.cs", "Readiness.cs",
            "RuntimeBinding.ps1", "RuntimeBinding.Tests.ps1",
            "ProcessImage.ps1", "ProcessImage.Tests.ps1", "prepare.ps1",
            "validate-helper.ps1", "run.ps1", "launch.ps1", "test-pure.ps1",
            "Configuration.ps1", "Configuration.Tests.ps1", "configuration.example.json",
            "restage/RestagePolicy.ps1", "restage/restage.ps1", "restage/test-pure.ps1",
            "supervision/NonCopySupervisor.ps1", "supervision/RestageSupervisor.ps1",
        }
        self.assertEqual(expected, {p.relative_to(PACKAGE).as_posix() for p in PACKAGE.rglob("*") if p.is_file()})
        self.assertTrue((ROOT / "scripts/WindowsDesktopProof/PinnedLoader.ps1").is_file())
        self.assertTrue((ROOT / "scripts/WindowsDesktopProof/PinnedLoader.Tests.ps1").is_file())

    def test_no_machine_evidence_or_hidden_executable_templates(self):
        for path in PACKAGE.rglob("*"):
            if not path.is_file():
                continue
            text = path.read_text(encoding="utf-8-sig")
            with self.subTest(path=path.name):
                self.assertNotRegex(text, r"(?i)\.copilot|session-state|C:\\Users\\|restage-v3-before-")
                self.assertNotIn(".template.txt", text)
                if path.name == "configuration.example.json":
                    self.assertNotRegex(text, r"[a-f0-9]{64}", "Use data pins, not embedded historical hashes")
        example = json.loads((PACKAGE / "configuration.example.json").read_text(encoding="utf-8"))
        self.assertIn("REPLACE_WITH_", example["host"]["sha256"])
        self.assertIn("REPLACE_WITH_", example["applicationSource"])
        self.assertEqual({"systemRoot", "dotnetRoot", "gitPath", "programFiles", "programFilesX86"}, set(example["machine"]))

    def test_operational_configuration_precedes_side_effects(self):
        for name in ("prepare.ps1", "validate-helper.ps1", "run.ps1", "launch.ps1", "restage/restage.ps1"):
            text = (PACKAGE / name).read_text(encoding="utf-8")
            with self.subTest(path=name):
                read = text.index("Read-ProofConfiguration")
                closure = text.index("Assert-ProofSource")
                self.assertLess(read, closure)
                for marker in ("Add-Type", "[Diagnostics.Process]::new()", "[IO.Directory]::CreateDirectory"):
                    if marker in text:
                        self.assertLess(closure, text.index(marker))
                self.assertIn("$PSScriptRoot", text)
        run = (PACKAGE / "run.ps1").read_text(encoding="utf-8")
        self.assertIn("$proofConfiguration.machine.systemRoot", run)
        self.assertNotIn("$config.machine.", run)
        self.assertIn("Read-ProofInput", run)

    def test_resource_admission_checks_discovered_config_rows(self):
        text = (PACKAGE / "prepare.ps1").read_text(encoding="utf-8")
        start = text.index("function Resources {")
        resources = text[start:text.index("\nfunction ", start + 1)]
        self.assertTrue("if (!$config.Count)" in resources, "Resource admission must check discovered config rows")
        self.assertTrue("$proofConfiguration.Count" not in resources, "Machine configuration is not resource evidence")

    def test_ordinary_feature_scenario_and_retained_custody(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        launch = (PACKAGE / "launch.ps1").read_text(encoding="utf-8")
        run = (PACKAGE / "run.ps1").read_text(encoding="utf-8")
        for token in ("Main_PatchManager_Button", "PatchManager_ImportPatchDatabase_Button",
                      "PatchManager_StatusMessage_Label", "single-owned-active-picker-BM_CLICK",
                      "PrintWindow-failed-no-fallback", "Patch database import failed:"):
            self.assertIn(token, desktop)
        self.assertEqual(1, desktop.count("Native.PrintWindow("))
        self.assertEqual(1, desktop.count("value.SetValue(path)"))
        self.assertEqual(1, launch.count("[Diagnostics.Process]::GetProcessById("))
        self.assertIn("ArgumentList.Add('--rom='", run)
        for text in (desktop, launch, run):
            self.assertNotRegex(text, r"GetProcesses(?:ByName)?\s*\(|SetForegroundWindow|SendInput|SendKeys|\.Kill\((?:true|\$true)\)")
            self.assertNotRegex(text, r"--smoke-test|--screenshot-all")

    def test_real_staging_and_reporting_regressions_are_in_aggregate(self):
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        tests = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        for token in ("Invoke-ConfigurationIntegrationTests", "Invoke-ReportingWriterTests", "Invoke-PinnedLoaderTests"):
            self.assertIn(token, aggregate)
        self.assertIn("& (Join-Path $PSScriptRoot 'restage\\restage.ps1')", tests)
        self.assertIn("Confirm-ProofChildResult", tests)
        self.assertIn("Invoke-RTerminalPublication", tests)
        self.assertIn("@(105,315)", tests)
        self.assertIn("@(100,310)", tests)
        pure = (PACKAGE / "restage/test-pure.ps1").read_text(encoding="utf-8")
        names = re.findall(r"^\s+@\{name='([^']+)';reject=", pure, re.M)
        self.assertEqual(286, len(names))
        self.assertEqual(286, len(set(names)))
        later = re.findall(r"^\s+@\{name='([^']+)';reject=", tests, re.M)
        self.assertEqual(36, len(later))
        self.assertEqual(322, len(set(names + later)))
        self.assertEqual(26, sum(name.startswith("receipt-retention-") for name in later))
        self.assertEqual(10, sum(name.startswith("external-deadline-") for name in later))
        self.assertIn("$cases+=@(Get-LaterRestageCases)", pure)
        self.assertLessEqual((PACKAGE / "restage/test-pure.ps1").stat().st_size, 65536)

    def test_later_corrections_use_public_boundaries(self):
        pure = (PACKAGE / "restage/test-pure.ps1").read_text(encoding="utf-8")
        tests = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        policy = (PACKAGE / "restage/RestagePolicy.ps1").read_text(encoding="utf-8")
        supervisor = (PACKAGE / "supervision/NonCopySupervisor.ps1").read_text(encoding="utf-8")
        config = (PACKAGE / "Configuration.ps1").read_text(encoding="utf-8")
        driver = (PACKAGE / "restage/restage.ps1").read_text(encoding="utf-8")
        self.assertNotIn("& $case.run 3>$null", pure)
        self.assertIn("Invoke-ProofPureCase $case", pure)
        self.assertIn("@('inventory-json-depth-bound','receipt-completion-depth-bound')", tests)
        self.assertIn("Unexpected warnings were suppressed.", tests)
        self.assertIn("Assert-RReceiptRetentionAdmission $Window.terminalPrevious", policy)
        self.assertIn("terminalPrevious=$terminalWindow.previous", supervisor)
        self.assertLess(supervisor.index("if(Test-RExternalDeadlineExceeded"),
                        supervisor.index("if($observed.exitConfirmed -and $observed.outputConfirmed)"))
        self.assertIn("$freeze=Read-ProofHistory $config.restage.history", driver)
        self.assertIn("'pureCaseNames','limits','externalClosureRule'", config)
        self.assertIn("Assert-ProofTiming $Report", supervisor)
        self.assertNotIn("-DateKind", config)
        loader_tests = (ROOT / "scripts/WindowsDesktopProof/PinnedLoader.Tests.ps1").read_text(encoding="utf-8")
        self.assertIn("Set-Alias -Name r -Value Invoke-ProofShortAliasTrap", loader_tests)

    def test_final_writer_is_not_optional_process_grace(self):
        policy = (PACKAGE / "restage/RestagePolicy.ps1").read_text(encoding="utf-8")
        writer = policy[policy.index("function Write-RReportingFile"):policy.index("function Assert-R(")]
        self.assertNotIn("Assert-ROptionalPublicationAdmission", writer)
        for token in ("CreateNew", "Flush($true)", "Dispose()", "Read-ProofPinnedBytes", "$Window.attempted=$true"):
            self.assertIn(token, writer)
        self.assertLess(writer.index("'flush'"), writer.index("'close'"))
        self.assertLess(writer.index("'close'"), writer.index("'hash'"))
        self.assertLess(writer.index("'hash'"), writer.index("'acknowledgement'"))


if __name__ == "__main__":
    unittest.main()
