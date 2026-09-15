# SPDX-License-Identifier: GPL-3.0-or-later
"""Source/data contracts; production behavior is exercised by test-pure.ps1."""

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "scripts" / "OfflinePatchImportProof"


class OfflinePatchImportProofContractTests(unittest.TestCase):
    def test_supervised_fixture_constructs_real_reference_provenance(self):
        fixture = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        loader_tests = (ROOT / "scripts/WindowsDesktopProof/PinnedLoader.Tests.ps1").read_text(encoding="utf-8")
        self.assertIn("function Get-ProofTestCompileReferences", fixture)
        self.assertIn("$PSBoundParameters.ContainsKey('CompileReferences')", fixture)
        self.assertIn("Read-ProofPinnedBytes $row", fixture)
        self.assertIn("-CompileReferences (Get-ProofTestCompileReferences)", loader_tests)
        self.assertLess(loader_tests.index("-CompileReferences (Get-ProofTestCompileReferences)"),
                        loader_tests.index("'supervised-configuration.json'"))
        names = re.findall(r"'([^']+\.dll)'", fixture[
            fixture.index("function Get-ProofTestCompileReferences"):
            fixture.index("function New-ProofRestageFixture")])
        self.assertEqual(26, len(names))
        self.assertEqual(26, len(set(names)))
        self.assertIn("System.Diagnostics.Process.dll", names)
        self.assertIn("System.Runtime.InteropServices.dll", names)
        self.assertIn('("host\\ref-$i.dat")', fixture)

    def test_live_image_integration_is_reported_without_gui_authorization(self):
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        loader_tests = (ROOT / "scripts/WindowsDesktopProof/PinnedLoader.Tests.ps1").read_text(encoding="utf-8")
        self.assertIn("Invoke-CompilerReferenceFixtureTests $root", aggregate)
        self.assertIn("compilerReferenceFixtureCases = $referenceCases", aggregate)
        self.assertIn("liveProcessImageIntegrationCases = [int]$IsWindows", aggregate)
        self.assertIn("native_calls = [bool]$IsWindows", aggregate)
        self.assertIn("gui_authorization = $false", aggregate)
        self.assertIn("$result.selfImageObservation.code -cne 'image-observed'", loader_tests)
        self.assertIn("$result.imageObservation.code -cne 'image-observed'", loader_tests)
        self.assertIn("$readerAssembly=Join-Path $root 'Readiness.tests.dll'", aggregate)
        self.assertIn("[Runtime.Loader.AssemblyLoadContext]::Default.LoadFromStream($readerStream)", aggregate)
        self.assertIn("finally{$readerStream.Dispose()}", aggregate)
        self.assertIn("-ReferencedAssemblies @($references + $readerAssembly)", aggregate)
        self.assertLess(aggregate.index("[IO.Directory]::Delete($root,$true)"),
                        aggregate.index("$result | ConvertTo-Json -Compress"))

    def test_retained_image_reader_uses_one_borrowed_handle_query(self):
        reader = (PACKAGE / "Readiness.cs").read_text(encoding="utf-8")
        self.assertEqual(2, reader.count("QueryFullProcessImageNameW("))
        self.assertIn("Capacity = 32768", reader)
        self.assertIn("PreviewLimit = 256", reader)
        self.assertIn("DefaultDllImportSearchPaths(DllImportSearchPath.System32)", reader)
        self.assertIn("query(retainedHandle.DangerousGetHandle(), 0, buffer", reader)
        self.assertLess(reader.index("DangerousAddRef"), reader.index("query(retainedHandle"))
        self.assertIn("finally { if (borrowed) retainedHandle.DangerousRelease(); }", reader)
        self.assertNotRegex(reader, r"MainModule|EnumProcessModules|OpenProcess|GetProcessById|Thread.Sleep")
        self.assertNotIn("DesktopPolicy", reader)
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        self.assertIn("typeof(BoundedProcessImage).GetMethod(nameof(BoundedProcessImage.Read)", tests)
        self.assertIn("ReadInjected(h, m)", tests)
        self.assertIn("Delegate.CreateDelegate(method.GetParameters()[1].ParameterType", tests)
        self.assertIn("new SafeProcessHandle", tests)
        self.assertIn("Check(names.Count == 32)", tests)

    def test_all_image_consumers_use_the_shared_retained_reader(self):
        roles = {
            "prepare.ps1": ("prepare-initial", "prepare-cleanup"),
            "launch.ps1": ("launch-runner-initial", "launch-runner-cleanup", "launch-app-retain", "launch-app-cleanup"),
            "run.ps1": ("run-self", "run-app-initial"),
            "Desktop.cs": ("desktop-app",),
            "supervision/NonCopySupervisor.ps1": ("supervision-self", "supervision-child-initial", "supervision-child-cleanup"),
        }
        for name, required in roles.items():
            text = (PACKAGE / name).read_text(encoding="utf-8")
            with self.subTest(name=name):
                self.assertNotIn(".MainModule", text)
                self.assertIn("SafeHandle", text)
                self.assertRegex(text, r"BoundedProcessImage\]?[:.]")
                for role in required:
                    self.assertIn(role, text)
                if name != "launch.ps1":
                    self.assertNotIn("GetProcessById", text)
        launch = (PACKAGE / "launch.ps1").read_text(encoding="utf-8")
        self.assertLess(launch.index("$candidateHandle=$candidate.SafeHandle"),
                        launch.index("$candidate.StartTime"))
        self.assertLess(launch.index("$candidate.StartTime"),
                        launch.index("[BoundedProcessImage]::Read($candidateHandle)"))
        run = (PACKAGE / "run.ps1").read_text(encoding="utf-8")
        self.assertLess(run.index("$stdoutDrain ="), run.index("run-app-initial"))
        self.assertLess(run.index("'app-identity.json'"), run.index("run-app-initial"))
        self.assertLess(run.index("Add-Type -Path"), run.index("[BoundedProcessImage]::Read($hostHandle)"))
        supervisor = (PACKAGE / "supervision/NonCopySupervisor.ps1").read_text(encoding="utf-8")
        self.assertLess(supervisor.index("function Invoke-ProofSupervision"), supervisor.index("Add-Type"))
        self.assertLess(supervisor.index("Read-ProofPinnedBytes $row"), supervisor.index("Add-Type"))
        self.assertLess(supervisor.index("supervision-child-cleanup"), supervisor.index("$child.Kill()"))

    def test_image_failure_publication_and_all_inventories_are_wired(self):
        bridge = (PACKAGE / "ProcessImage.ps1").read_text(encoding="utf-8")
        self.assertNotRegex(bridge, r"ReadMainModule|DelayMilliseconds|Sleep|while\s*\(")
        self.assertIn("Image observation slot already consumed.", bridge)
        self.assertLess(bridge.index("$Observation[$key]=$description[$key]"),
                        bridge.index('throw "Retained process image refused:'))
        self.assertIn("remainingBeforeMs", bridge)
        self.assertIn("remainingAfterMs", bridge)
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            text = (PACKAGE / name).read_text(encoding="utf-8")
            self.assertIn("[RetainedProcessImageTests]::Run()", text)
            self.assertIn("Retained image inventory changed.", text)
            shapes = [line for line in text.splitlines()
                      if "Add-Type -Path" in line and ".compile-only.dll" in line]
            self.assertEqual(2, len(shapes))
            self.assertTrue(all("Readiness.cs" in line for line in shapes))
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        self.assertIn("retainedImageReportingCases = $imageReportingCases", aggregate)
        self.assertIn("Invoke-RetainedImageReportingTests", aggregate)
        policy = (PACKAGE / "restage/RestagePolicy.ps1").read_text(encoding="utf-8")
        self.assertIn("Assert-RImageObservation $value", policy)
        self.assertIn("$bytes.Length -le 4096", policy)
        self.assertIn("$Value.observedPathPreview.Length -le 256", policy)
        for field in ("selfImageObservation", "imageObservation", "cleanupImageObservation"):
            self.assertIn(field, policy)

    def test_preparation_exit_fallback_requires_one_bounded_positive_observation(self):
        bridge = (PACKAGE / "ProcessImage.ps1").read_text(encoding="utf-8")
        self.assertEqual(2, bridge.count("& $HasExited"))
        self.assertEqual(1, bridge.count("& $ReadImage"))
        self.assertEqual(3, bridge.count("& $RemainingMilliseconds"))
        fallback = bridge[bridge.index("$mayObserveExit="):]
        for guard in (
            "$Role -ceq 'prepare-initial'", "$Observation.code -ceq 'native-error'",
            "$image -is [ProcessImageRead]", "$image.Code -ceq 'native-error'",
            "$image.NativeError -eq 31", "$image.HandleValid", "$image.Queries -eq 1",
            "$null -eq $image.Path", "$null -eq $image.Characters",
            "$Observation.role -ceq 'prepare-initial'", "$Observation.nativeError -eq 31",
            "$Observation.handleValid -is [bool]", "$Observation.queries -eq 1",
            "$after -gt 0 -and $after -le $before", "$postExit -isnot [bool]",
            "$postExit -is [bool] -and $postExit",
            "$exitAfter -gt 0 -and $exitAfter -le $after",
            "[double]::IsFinite($after)", "[double]::IsFinite($exitAfter)",
        ):
            self.assertIn(guard, fallback)
        for field in ("returnedChars", "observedPathSha256", "observedPathLength",
                      "observedPathPreview", "previewTruncated"):
            self.assertIn(f"$null -eq $Observation.{field}", fallback)
        self.assertLess(fallback.index("$postExit=& $HasExited"),
                        fallback.index("$exitAfter=[double](& $RemainingMilliseconds)"))
        self.assertIn("catch { $Observation.code='exit-check-failed' }", fallback)
        self.assertIn("catch { $exitAfter=[double]::NaN }", fallback)
        self.assertIn("state='exited-unobserved';path=$null;attempts=1", fallback)
        self.assertNotRegex(fallback, r"nativeError\s*=|queries\s*=")
        self.assertNotRegex(bridge, r"Sleep|Start-Sleep|while\s*\(|GetProcessById|MainModule")

    def test_preparation_exit_inventories_are_separate_and_pinned_in_both_runners(self):
        suites = (
            ("preparationExitCases", "Invoke-PreparationExitObservationTests", 88,
             "d31e67d296b9e17b9dc7df0c1ea73957252eec3cd28f4b555a44e6ed8019a4af"),
            ("preparationExitReportingCases", "Invoke-PreparationExitReportingTests", 72,
             "f6028266a57578cd815a9458c04c7a46fa85878a5f184dba41ea854e838aab42"),
        )
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            text = (PACKAGE / name).read_text(encoding="utf-8")
            for variable, function, count, digest in suites:
                self.assertIn(f"${variable}=@({function})", text)
                self.assertIn(f"Assert-PreparationExitCaseNames ${variable} {count} '{digest}'", text)
                self.assertNotIn(f"$report.{variable}", text)
                self.assertNotRegex(text, rf"(?m)^\s*{variable}\s*=")
        helper = (PACKAGE / "validate-helper.ps1").read_text(encoding="utf-8")
        self.assertLess(helper.index("$null=Assert-ProofSource"),
                        helper.index('. "$Code\\Configuration.Tests.ps1"'))
        self.assertLess(helper.index("foreach($r in @($tools.tools)"),
                        helper.index('. "$Code\\Configuration.Tests.ps1"'))
        self.assertIn(". (Get-PinnedProofLibrary -Library RestagePolicy)", helper)
        self.assertIn('. "$Code\\ProcessImage.ps1"', helper)
        tests = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        fixture = tests[tests.index("function Invoke-PreparationExitTestObservation"):
                        tests.index("function Invoke-PreparationExitObservationTests")]
        self.assertIn("[ProcessImageRead]::new([NullString]::Value,'native-error',31,1,$true,$null)", fixture)
        self.assertIn("$actual -ceq $Sha256", tests)
        aggregate = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        self.assertIn(". (Join-Path $PSScriptRoot 'ProcessImage.ps1')", aggregate)
        result = aggregate.split("$result=[pscustomobject]@{", 1)[1].split("\n        }", 1)[0]
        self.assertEqual({
            "cases", "installedSnapshotCases", "startupObservationCases", "ownedTreeCases",
            "processImageCases", "retainedImageCases", "retainedImageReportingCases",
            "runtimeBindingCases", "windowsLexicalCases", "reportingWriterCases",
            "laterProductionCases", "configurationIntegrationCases", "compilerReferenceFixtureCases",
            "restageCases", "loaderCases", "liveProcessImageIntegrationCases",
            "outputOnlyCompilations", "passed", "native_calls", "gui_authorization",
        }, set(re.findall(r"(?m)^\s*(\w+)\s*=", result)))

    def test_post_query_exit_diagnostic_has_semantic_guard_without_new_fields(self):
        policy = (PACKAGE / "restage/RestagePolicy.ps1").read_text(encoding="utf-8")
        validator = policy[policy.index("function Assert-RImageObservation"):
                           policy.index("function New-RTerminalRecord")]
        fields = validator.split("Assert-RKeys $Value @(", 1)[1].split(")", 1)[0]
        self.assertEqual({
            "schema", "role", "method", "flags", "code", "queries", "handleValid", "nativeError",
            "capacity", "returnedChars", "expectedPathSha256", "observedPathSha256",
            "observedPathLength", "observedPathPreview", "previewTruncated",
            "remainingBeforeMs", "remainingAfterMs",
        }, set(re.findall(r"'([^']+)'", fields)))
        guard = validator[validator.index("if($Value.code -ceq 'exited-after-query-error')"):]
        for token in ("$Value.role -ceq 'prepare-initial'", "$Value.nativeError -eq 31",
                      "$Value.handleValid -is [bool]", "$Value.queries -eq 1",
                      "$null -eq $Value[$key]", "$null -ne $Value.remainingBeforeMs",
                      "$null -ne $Value.remainingAfterMs", "$Value.remainingBeforeMs -gt 0",
                      "$Value.remainingAfterMs -gt 0",
                      "$Value.remainingAfterMs -le $Value.remainingBeforeMs"):
            self.assertIn(token, guard)
        for field in ("returnedChars", "observedPathSha256", "observedPathLength",
                      "observedPathPreview", "previewTruncated"):
            self.assertIn(f"'{field}'", guard)
        tests = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        self.assertIn("terminal-snapshot-preserves-native-error", tests)
        self.assertIn("diagnostic-alone-cannot-promote", tests)
        self.assertIn("Invoke-RTerminalPublication", tests)

    def test_preparation_still_requires_retained_exit_zero_eof_and_source_gates(self):
        prepare = (PACKAGE / "prepare.ps1").read_text(encoding="utf-8")
        run = prepare[prepare.index("function Run("):prepare.index("function Git(")]
        ordered = (
            "$null=Get-ProcessImageObservation", "$exited=$p.WaitForExit(",
            "$record.exitCode=$p.ExitCode", "foreach ($task in @($copyOut,$copyErr))",
            "if (!$drained)", "if ($record.exitCode -ne 0)",
            "$record.stdoutSha256=Hash $record.stdout", "Budget; $record.passed=$true",
        )
        positions = [run.index(token) for token in ordered]
        self.assertEqual(sorted(positions), positions)
        self.assertGreaterEqual(run.count("Assert-ProofProcessDeadline"), 5)
        self.assertEqual(1, run.count("$record.passed=$true"))
        self.assertLess(prepare.index("$report.before=SourceState"),
                        prepare.index("$report.after=SourceState"))
        self.assertLess(prepare.index("$report.after=SourceState"),
                        prepare.index("Budget; $report.passed=$true"))
        self.assertNotIn("exited-after-query-error", prepare)

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
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        launch = (PACKAGE / "launch.ps1").read_text(encoding="utf-8")
        run = (PACKAGE / "run.ps1").read_text(encoding="utf-8")
        for name, value in (("MainButton", "Main_PatchManager_Button"),
                            ("ImportButton", "PatchManager_ImportPatchDatabase_Button"),
                            ("StatusLabel", "PatchManager_StatusMessage_Label")):
            self.assertIn(f'const string {name} = "{value}"', policy)
            self.assertIn(f"const string {name} = DesktopCandidateQuery.{name};", desktop)
        for token in ("single-owned-active-picker-BM_CLICK", "PrintWindow-failed-no-fallback",
                      "Patch database import failed:"):
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
        self.assertIn("acceptance.RefreshAndCheckRevision()", handoff)
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
        self.assertEqual(2, desktop.count(".FindAll("))
        self.assertIn("desktop.FindAll(TreeScope.Children", desktop)
        self.assertIn("PropertyCondition(AutomationElement.ProcessIdProperty, owner.pid)", desktop)
        self.assertIn("subtree.FindAll(TreeScope.Descendants, condition)", desktop)
        self.assertNotIn("desktop.FindAll(TreeScope.Descendants", desktop)
        self.assertIn("TreeFilter = Automation.RawViewCondition", desktop)
        self.assertIn("AutomationElementMode = AutomationElementMode.Full", desktop)
        self.assertNotIn("ControlViewWalker", desktop)
        self.assertIn("TreeWalker.RawViewWalker.GetParent(node)", desktop)
        self.assertNotIn("GetFirstChild", desktop)
        self.assertNotIn("GetNextSibling", desktop)
        for token in ("tree.Find(tree.WindowKey(window, selector)", "tree.Validate(tree.WindowKey(",
                      "tree.Read(tree.WindowKey(", "DesktopStartupSample<AutomationElement>.Capture(tree, owned =>"):
            self.assertIn(token, desktop)
        for token in ("DesktopSelector.Loading", "DesktopSelector.FilenameHost",
                      "DesktopSelector.FilenameEdit, 1, field, tree",
                      "DesktopSelector.PickerOpen", "DesktopSelector.Row, 1, list, tree",
                      "DesktopSelector.RowName, 2, rows[0], tree"):
            self.assertIn(token, desktop)
        self.assertNotRegex(desktop, r"GetAncestor\([^,\n]+,\s*3\)")
        candidates = policy[policy.index("    void Candidates("):policy.index("    void Drain()")]
        self.assertNotIn("void Walk(", policy)
        self.assertLess(candidates.index("Resolve(node, expected)"), candidates.index("NodeIdentity(node)"))
        self.assertLess(candidates.index("CrossRoot(expected, root)"), candidates.index("adapter.Matches(node, selector)"))
        self.assertEqual(2, candidates.count("WithinSubtree(node, parentIdentity)"))
        self.assertIn('Require(matched, "query-candidate-filter")', candidates)
        self.assertIn('Require(NodeIdentity(node) == identity, "query-node-alias")', candidates)
        self.assertIn('Require(NodeIdentity(parent) == parentIdentity, "query-subtree-changed")', candidates)
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
                      "query-discovered-root-is-drained-before-refusal",
                      "actual-traversal-wizard-selection-drives-startup",
                      "observed-visibility-change-invalidates-acceptance-revision",
                      "diagnostics-cannot-hide-original-refusal-when-budget-closes"):
            self.assertIn(token, tests)
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            runner = (PACKAGE / name).read_text(encoding="utf-8")
            self.assertIn("[DesktopPolicyTests]::RunOwnedTreeTests()", runner)
            self.assertIn("ownedTreeCases", runner)
            self.assertRegex(runner, r"ownedTreeCases -ne 146")
            self.assertIn("[DesktopPolicyTests]::AssertOwnedTreeCaseInventory()", runner)
        self.assertIn("82bf31556950da6396b76c3d3a72b5de315d2f988ace9386f0044c46725edff1", tests)

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
        self.assertLess(capture.index("tree.RefreshCatalog()"), capture.index("var sample ="))
        self.assertRegex(capture, r"bool visible = tree.Visible\(window\);\s+tree.RefreshCatalog\(\);\s+sample.CheckRevision\(\);\s+if \(!visible\)")
        self.assertRegex(capture, r"var projection = project\(window\);[\s\S]*?sample.CheckRevision\(\);\s+sample.windows.Add")
        observe = sample[sample.index("    internal DesktopStartupDecision"):]
        self.assertLess(observe.index("CheckRevision()"), observe.index("observation.Revalidate("))
        self.assertIn("observation.Observe(at, Roots, loadingExists)", observe)
        self.assertIn("DesktopStartupSample<AutomationElement>.Capture", desktop)
        self.assertIn("DesktopStartupSample<OwnedNode>.Capture", tests)
        self.assertIn("count + RunProjectionTests() + RunCandidateTests()", tests)
        self.assertIn("acceptance.RefreshAndCheckRevision()", desktop)
        for name in ("initial-projection-cannot-omit-earlier-hidden-root",
                     "acceptance-refresh-cannot-omit-earlier-hidden-root",
                     "already-projected-root-visibility-change-refuses",
                     "observed-change-after-refresh-decision-refuses-final-acceptance",
                     "stable-hidden-root-keeps-honest-candidate-and-refresh"):
            self.assertIn(name, tests)

    def test_closed_candidate_compiler_preserves_fixed_selector_semantics(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        compiler = policy[policy.index("internal static class DesktopCandidateQuery"):
                          policy.index("public sealed class DesktopQueryFailure")]
        self.assertIn("int pid, DesktopSelector selector", compiler)
        self.assertIn("DesktopQueryProperty.ControlType, DesktopQueryControl.Window", compiler)
        self.assertIn("compiler.Not(compiler.Equal(DesktopQueryProperty.NativeWindowHandle, 0))", compiler)
        self.assertIn("compiler.Equal(DesktopQueryProperty.ProcessId, pid)", compiler)
        self.assertIn('default: throw new DesktopTreeGuardException("query-selector")', compiler)
        for token in ("MainButton", "WizardButton", "ImportButton", "StatusLabel", "PatchList",
                      '"1148"', '"1"', '"MessageBoxContent_Yes_Button"', '"MessageBoxContent_Message_Label"'):
            self.assertIn(token, compiler)
        self.assertIn("DesktopQueryProperty.Name, LoadingName", compiler)
        self.assertIn("DesktopQueryProperty.Name, ExpectedRow", compiler)
        for kind in ("Window", "Text", "Edit", "ListItem"):
            self.assertIn(f"case DesktopQueryControl.{kind}: value = ControlType.{kind}; break;", desktop)
        self.assertIn("DesktopCandidateQuery.Compile(new ConditionCompiler(), owner.pid, selector)", desktop)
        self.assertIn("new DesktopPredicateCompiler<AutomationElement>(Property)", desktop)
        self.assertNotIn("Condition.TrueCondition", desktop)
        self.assertNotRegex(desktop, r"EnumWindows|EnumChildWindows|GetProcesses")

    def test_candidate_collection_limits_are_post_provider_not_a_raw_work_claim(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        collect = policy[policy.index("internal static IReadOnlyList<TNode> Collect"):
                         policy.index("public sealed class DesktopQueryFailure")]
        self.assertLess(collect.index("budget.Candidates(length, maximum)"), collect.index("new List<TNode>(length)"))
        self.assertLess(collect.index("budget.Candidates(length, maximum)"), collect.index("item(index)"))
        self.assertIn("int maximum = 256", collect)
        self.assertIn("count > 4096 - Nodes", policy)
        self.assertIn('throw new DesktopTreeGuardException(error)', policy)
        native = desktop[desktop.index("public IReadOnlyList<AutomationElement> Candidates"):
                         desktop.index("public AutomationElement FromHandle")]
        self.assertLess(native.index("subtree.FindAll("), native.index("DesktopCandidateQuery.Collect("))
        refresh = policy[policy.index("    void Refresh()"):policy.index("    internal void Discover()")]
        self.assertIn("edges.Clear()", refresh)
        self.assertIn("foreach (var root in windows) pending.Enqueue(root)", refresh)
        self.assertNotRegex(refresh, r"Nodes\s*=|Calls\s*=|new DesktopTreeBudget")
        find = policy[policy.index("internal List<TNode> Find("):policy.index("internal void Validate(")]
        self.assertLess(find.index("Candidates(start,"), find.index("Refresh();"))
        self.assertIn('Require(Revision == revision, "query-topology-changed")', find)

    def test_scale_fixtures_execute_shared_compiler_over_full_graph_and_frames(self):
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        self.assertIn("DesktopCandidateQuery.Compile(new DesktopPredicateCompiler<OwnedNode>", tests)
        self.assertIn("if (predicate(node)) matches.Add(node)", tests)
        self.assertIn("ProviderNodes++", tests)
        self.assertIn("return DesktopCandidateQuery.Collect(Budget", tests)
        self.assertNotIn("node.Selector == selector", tests)
        self.assertIn("i < 10000", tests)
        self.assertIn("var acceptance = ProjectStartup(model)", tests)
        self.assertIn("acceptance.RefreshAndCheckRevision()", tests)
        self.assertIn("model.CandidateQueries + model.SeedQueries > 160", tests)
        for token in ("257-provider-candidates-refused-before-items",
                      "candidate-count-respects-remaining-node-budget",
                      "same-root-out-of-subtree-candidate-refused-before-match",
                      "subtree-reparent-during-match-refused",
                      "id-filter-preserves-wrong-type-candidate",
                      "candidate-rpc-deadline-closes-before-collection-access",
                      "root-relations-refresh-does-not-cache-old-ancestry",
                      "unmatched-late-root-invalidates-final-acceptance-",
                      "full-graph-scale-depth-"):
            self.assertIn(token, tests)

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
