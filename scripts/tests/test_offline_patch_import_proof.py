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
                self.assertTrue(text.startswith("throw 'PinnedProof.UnsupportedDirectRoute'\nfunction ProofEnvelope {"))
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
        following = re.search(r"\n\s*function ", text[start + 1:])
        self.assertIsNotNone(following)
        resources = text[start:start + 1 + following.start()]
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

    def test_installed_marker_uses_the_production_snapshot_and_both_pure_runners(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        self.assertIn("InstalledDatabaseSnapshot.Capture(database, Guard, Hash)", desktop)
        self.assertNotIn("files == 2", desktop)
        self.assertIn('MarkerName = ".febuilder-patch-import.json"', policy)
        self.assertIn('MarkerOwner = "FEBuilderGBA.PatchDatabaseImport"', policy)
        self.assertIn("ReadMarker(entry)", policy)
        self.assertIn("SHA256.HashData(marker)", policy)
        self.assertIn("files == 3 && rows.Count == 4", policy)
        self.assertIn("16777217", tests)
        self.assertIn("DesktopPolicy.Preserved(original, altered", tests)
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            runner = (PACKAGE / name).read_text(encoding="utf-8")
            self.assertIn("[DesktopPolicyTests]::RunSnapshotTests(", runner)
            self.assertIn("Incomplete installed snapshot cases.", runner)

    def test_real_staging_and_reporting_regressions_are_in_aggregate(self):
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        tests = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        for token in ("Invoke-ConfigurationIntegrationTests", "Invoke-ReportingWriterTests", "Invoke-PinnedLoaderTests"):
            self.assertIn(token, aggregate)
        self.assertIn("Invoke-PinnedTestMode -Root $Root -Mode RestageChild", tests)
        self.assertIn("Invoke-PinnedTestMode -Fixture $relocated -Mode PrerequisitesChild", tests)
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

    def test_bootstrap_closure_and_fixed_dispatch(self):
        loader = (ROOT / "scripts/WindowsDesktopProof/PinnedLoader.ps1").read_text(encoding="utf-8")
        modes = set(re.findall(r"^\s+(\w+) \{\$file=", loader, re.M))
        self.assertEqual({
            "Pure", "AggregatePure", "SupervisedPure", "ReadOnlyPrerequisites", "Restage", "Gui",
            "Build", "Validate", "Inputs", "ValidatePureChild", "ValidateCompileChild",
            "PrerequisitesChild", "RestageChild", "RunChild",
        }, modes)
        for token in ("ClosurePath", "ClosureBytes", "ClosureSha256", "pinned-proof-bindings-v2",
                      "pinned-proof-closure-v1", "PinnedProof.Authentication:", "23068672",
                      "$manifest.files.Count -ne 22", "$CommandBytes -gt 65536",
                      "$CommandSha256 -cne $row.sha256", "$body.GetScriptBlock()"):
            self.assertIn(token, loader)
        invoke = loader[loader.index("function Invoke-PinnedProof {"):]
        self.assertLess(invoke.index("Read-PinnedProofClosure"), invoke.index("ConvertTo-PinnedProofAst"))
        self.assertIn(". (Get-PinnedProofEnvelope", invoke)
        for name in ("prepare.ps1", "launch.ps1", "supervision/NonCopySupervisor.ps1"):
            text = (PACKAGE / name).read_text(encoding="utf-8")
            self.assertIn("New-PinnedProofChildArguments", text)
            self.assertNotRegex(text, r"'-File',\s*(?:\"\$Code\\validate-helper|.*'run\.ps1')")

    def test_libraries_are_imported_without_main(self):
        for name, entry in (("NonCopySupervisor.ps1", "Invoke-NonCopyEntry"),
                            ("RestageSupervisor.ps1", "Invoke-RestageSupervisorEntry")):
            text = (PACKAGE / "supervision" / name).read_text(encoding="utf-8")
            self.assertTrue(text.startswith("throw 'PinnedProof.UnsupportedDirectRoute'\nfunction ProofEnvelope {"))
            self.assertIn("function " + entry, text)
            self.assertIn(". (Get-PinnedProofLibrary -Library ", text)
            self.assertNotIn("$MyInvocation.InvocationName", text)
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        self.assertIn("function Invoke-AggregateEntry", aggregate)
        self.assertIn("outer driver trusts the reviewed checkout", aggregate)
        self.assertIn("Invoke-PinnedTestMode -Fixture $checkoutFixture -Mode AggregatePure", aggregate)

    def test_startup_observer_is_shared_by_driver_and_both_runners(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        validation = (PACKAGE / "validate-helper.ps1").read_text(encoding="utf-8")
        for token in ("new DesktopStartupObservation()", "ReadStartupSample()",
                      "sample.Observe(observation, clock.ElapsedMilliseconds, loadingExists, revalidate)",
                      "result.StartupRoute = decision.Route",
                      "result.LoadingObserved = observation.LoadingObserved",
                      "startup-main-acceptance-control", "startup-wizard-acceptance-control"):
            self.assertIn(token, desktop)
        self.assertNotIn('"missing-loading-observation"', desktop)
        for token in ("DesktopPolicy.Handoff(true, LoadingAt", "unknown != 0",
                      "wizard.Owner == main.Handle", "wizard.WindowClass == main.WindowClass",
                      "startup-duplicate-root", "startup-duplicate-main", "startup-duplicate-loading",
                      "startup-duplicate-wizard", "startup-observation-order", "startup-acceptance-changed",
                      "main-visible-loading-not-observed", "real-main-visible-and-loading-destroyed"):
            self.assertIn(token, policy)
        handoff = desktop[desktop.index("    void Handoff()"):desktop.index("    void OpenEditor()")]
        self.assertLess(handoff.index("ReadStartupSample()"), handoff.index("ObserveStartup("))
        self.assertIn("ObserveStartup(observation, acceptance, true)", handoff)
        self.assertIn("ValidateWindow(window, decision.WindowClass)", handoff)
        self.assertIn("Control(acceptedMain, MainButton, ControlType.Button, false, acceptance.Tree)", handoff)
        self.assertIn("acceptance.CheckRevision()", handoff)
        self.assertNotIn("Invoke(", handoff)
        self.assertIn('Stage("loading-handoff", 45000)', handoff)
        for text in (aggregate, validation):
            self.assertIn("[DesktopPolicyTests]::RunStartupTests()", text)
            self.assertIn("startupObservationCases", text)
            self.assertRegex(text, r"startup(?:Observation)?Cases -ne 75")
            self.assertIn("installedSnapshotCases", text)
            self.assertIn("-ne 26", text)
        for token in ("main-first-honest-fast-route", "unlabeled-leftover-order-",
                      "exact-main-owned-wizard-order-", "late-loading-makes-observation-sticky",
                      "invalid-observed-handoff-never-falls-back-", "acceptance-offscreen-main-control-blocks"):
            self.assertIn(token, tests)

    def test_owned_tree_is_the_real_discovery_and_query_boundary(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        self.assertIn("OwnedTreeAdapter : IDesktopOwnedTreeAdapter<AutomationElement>", desktop)
        self.assertIn("new DesktopOwnedTree<AutomationElement>(new OwnedTreeAdapter(this)", desktop)
        self.assertEqual(1, desktop.count(".FindAll("))
        self.assertIn("desktop.FindAll(TreeScope.Children", desktop)
        self.assertIn("PropertyCondition(AutomationElement.ProcessIdProperty, owner.pid)", desktop)
        self.assertNotIn("TreeScope.Descendants", desktop)
        self.assertNotIn("ControlViewWalker", desktop)
        for method in ("GetParent", "GetFirstChild", "GetNextSibling"):
            self.assertIn(f"TreeWalker.RawViewWalker.{method}(node)", desktop)
        for token in ("tree.Find(tree.WindowKey(window, selector)", "tree.Validate(tree.WindowKey(",
                      "tree.Read(tree.WindowKey(", "DesktopStartupSample<AutomationElement>.Capture(tree, owned =>"):
            self.assertIn(token, desktop)
        for token in ("DesktopSelector.Loading", "DesktopSelector.FilenameHost",
                      "DesktopSelector.FilenameEdit, 1, field, tree",
                      "DesktopSelector.PickerOpen", "DesktopSelector.Row, 1, list, tree",
                      "DesktopSelector.RowName, 2, rows[0], tree"):
            self.assertIn(token, desktop)
        self.assertNotRegex(desktop, r"GetAncestor\([^,\n]+,\s*3\)")
        walk = policy[policy.index("    void Walk("):policy.index("    void Drain()")]
        self.assertLess(walk.index("adapter.ProcessId(node) == pid"), walk.index("Resolve(node, expected)"))
        self.assertLess(walk.index("CrossRoot(expected, root)"), walk.index("adapter.Matches(node, selector)"))
        self.assertIn("query-window-cycle", policy)
        self.assertIn("pending.Enqueue(root)", policy)
        self.assertIn("while (pending.Count != 0)", policy)

    def test_owned_tree_budget_and_failure_privacy_are_explicit(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        for token in ("Nodes >= 4096", "Calls >= 65536", "depth > 32", "windows.Count < 8",
                      "id.Length <= 32", "depth < 8", "query-topology-changed"):
            self.assertIn(token, policy)
        self.assertIn("unchecked((uint)value)", policy)
        self.assertIn("new IntPtr(unchecked((int)value))", policy)
        self.assertIn("Key(Native.GetAncestor(", desktop)
        self.assertIn("Same(Native.GetForegroundWindow(), pickerHandle)", desktop)
        failure = policy[policy.index("public sealed class DesktopQueryFailure"):
                         policy.index("internal sealed class DesktopTreeGuardException")]
        self.assertNotRegex(failure, r"\b(?:Name|Value|Path|Title|RuntimeId|ActualPid)\b")
        for token in ("ExpectedOwnedRoot", "PreviouslyOwnedHandle", "OwnedRootBefore", "OwnedRootAfter",
                      "AliveBefore", "AliveAfter", "OwnPidBefore", "OwnPidAfter", "Predicate"):
            self.assertIn(token, failure)
        refusal = policy[policy.index("    void Fail("):policy.index("    void Require(")]
        self.assertIn("failure.PreviouslyOwnedHandle != 0 && !Budget.Closed", refusal)
        self.assertLess(refusal.index("adapter.NativePid(root) == pid"),
                        refusal.index("failure.OwnedRootAfter = root"))
        self.assertIn("query-element-unavailable", policy)
        self.assertIn('Fail(ex.GetType().Name == "ElementNotAvailableException"', policy)
        self.assertIn("if (result.QueryFailure == null) result.QueryFailure = ex.Failure", desktop)
        self.assertIn("worker.Join(100)", desktop)
        self.assertIn("dispatch.Close()", desktop)

    def test_owned_tree_inventory_exercises_production_adapter_calls(self):
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        self.assertIn("OwnedModel : IDesktopOwnedTreeAdapter<OwnedNode>", tests)
        self.assertIn("new DesktopOwnedTree<OwnedNode>(this", tests)
        for token in ("main-seed-discovers-owned-wizard", "owner-query-never-matches-wizard-descendant",
                      "foreign-uia-pid-before-all-other-node-access", "raw-parent-includes-native-root-through-wrappers",
                      "cross-native-window-cycle-is-not-consistent-rediscovery",
                      "unavailable-provider-is-refused-without-retry",
                      "query-discovered-root-is-drained-before-return",
                      "actual-traversal-wizard-selection-drives-startup",
                      "observed-visibility-change-invalidates-acceptance-revision",
                      "diagnostics-cannot-hide-original-refusal-when-budget-closes"):
            self.assertIn(token, tests)
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            runner = (PACKAGE / name).read_text(encoding="utf-8")
            self.assertIn("[DesktopPolicyTests]::RunOwnedTreeTests()", runner)
            self.assertIn("ownedTreeCases", runner)
            self.assertRegex(runner, r"ownedTreeCases -ne 108")

    def test_projection_epoch_is_frozen_before_skipped_or_projected_roots(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        sample = policy[policy.index("internal sealed class DesktopStartupSample"):
                        policy.index("// Native/UIA ownership and control ancestry")]
        self.assertIn("internal int Revision { get; }", sample)
        self.assertIn("Tree = tree; Revision = tree.Revision;", sample)
        self.assertNotIn("sample.Revision = tree.Revision", sample)
        capture = sample[sample.index("    internal static"):sample.index("    internal DesktopStartupDecision")]
        self.assertLess(capture.index("sample.CheckRevision()"), capture.index("for (int i"))
        self.assertRegex(capture, r"bool visible = tree.Visible\(window\);\s+sample.CheckRevision\(\);\s+if \(!visible\)")
        self.assertRegex(capture, r"var projection = project\(window\);[\s\S]*?sample.CheckRevision\(\);\s+sample.windows.Add")
        observe = sample[sample.index("    internal DesktopStartupDecision"):]
        self.assertLess(observe.index("CheckRevision()"), observe.index("observation.Revalidate("))
        self.assertIn("observation.Observe(at, Roots, loadingExists)", observe)
        self.assertIn("DesktopStartupSample<AutomationElement>.Capture", desktop)
        self.assertIn("DesktopStartupSample<OwnedNode>.Capture", tests)
        self.assertIn("return count + RunProjectionTests()", tests)
        for name in ("initial-projection-cannot-omit-earlier-hidden-root",
                     "acceptance-refresh-cannot-omit-earlier-hidden-root",
                     "already-projected-root-visibility-change-refuses",
                     "observed-change-after-refresh-decision-refuses-final-acceptance",
                     "stable-hidden-root-keeps-honest-candidate-and-refresh"):
            self.assertIn(name, tests)

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
