# SPDX-License-Identifier: GPL-3.0-or-later
"""Source/data contracts; production behavior is exercised by test-pure.ps1."""

import json
import re
import unittest
from pathlib import Path
from xml.etree import ElementTree


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "scripts" / "OfflinePatchImportProof"


class PreparedLaunchContracts(unittest.TestCase):
    def test_shared_start_regression_calls_the_real_workflow(self):
        tests = (PACKAGE / "PreparedLaunch.Tests.ps1").read_text()
        fake = (PACKAGE / "PreparedLaunch.Tests.cs").read_text()
        self.assertIn("function Invoke-PreparedSharedStartEntry", tests)
        shared = tests[tests.index("function Invoke-PreparedSharedStartEntry"):]
        self.assertIn(". (Get-PinnedProofLibrary -Library Run)", shared)
        self.assertIn("Invoke-PreparedRunCore", shared)
        self.assertIn("Invoke-ProofAppWorkflow", shared)
        self.assertIn("#if PREPARED_SHARED_START_TEST", fake)
        self.assertNotIn("PREPARED_SHARED_START_TEST", (PACKAGE / "run.ps1").read_text())

    def test_prepared_routes_are_explicit_and_separate(self):
        loader = (ROOT / "scripts/WindowsDesktopProof/PinnedLoader.ps1").read_text()
        for mode in ("PrepareBundle", "PrepareWorkspace", "ActivatePrepared", "PreparedRun"):
            self.assertIn(mode, loader)
        self.assertIn("pinned-prepared-bindings-v1", loader)

    def test_preparation_privacy_admission_precedes_hash(self):
        text = (PACKAGE / "prepare.ps1").read_text()
        tree = text[text.index("function Tree("):text.index("function SameRows(")]
        self.assertLess(tree.index("Assert-ProofPublicResource"), tree.index("$r=Row"))

    def test_prepared_activation_has_no_preparation(self):
        source = (PACKAGE / "PreparedLaunch.ps1").read_text()
        activation = source[source.index("function Invoke-ActivatePreparedEntry"):source.index("function Invoke-PreparedRunEntry")]
        for forbidden in ("Add-Type", "::Copy(", "CopyFile", "dotnet ", "gh ", "Invoke-PreparationEntry"):
            self.assertNotIn(forbidden, activation)
        self.assertIn("Invoke-ProofSupervisedRunner", activation)

    def test_attempt_commit_is_after_readiness(self):
        source = (PACKAGE / "PreparedLaunch.cs").read_text()
        self.assertLess(source.index("if (!ready())"), source.index("CommitAttempt("))
        self.assertIn("FileMode.CreateNew", source)
        self.assertIn("FileShare.Read", source)

_COMMON_BUILD_PREFIX = (
    "--no-restore", "--disable-build-servers", "-p:E2E_HOOKS=false",
    "-p:UseSharedCompilation=false", "-p:MSBuildEnableWorkloadResolver=false",
    "-p:BuildProjectReferences=true", "-p:ImportDirectoryBuildProps=false",
    "-p:ImportDirectoryBuildTargets=false", "-p:ImportDirectoryPackagesProps=false",
    "-m:1", "-nr:false",
)
_TELEMETRY_FLAG = "-p:UsedAvaloniaProducts="
_PREPARATION_OPT_OUT_KEYS = ("AVALONIA_TELEMETRY_OPTOUT", "POWERSHELL_TELEMETRY_OPTOUT")
_SDK_ENVIRONMENT = {
    "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
    "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
    "DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK": "true",
    "VSTEST_DISABLE_ARTIFACTS_POSTPROCESSING": "1",
}
_USER_IMPORT_FLAGS = tuple(
    "-p:ImportUserLocationsByWildcard" + phase + target + "=false"
    for target in ("MicrosoftCommonProps", "MicrosoftCommonTargets", "MicrosoftCSharpTargets")
    for phase in ("Before", "After")
)
_DISCOVERY_SUFFIX = "+@('--','xUnit.PreEnumerateTheories=false')"
_BUILD_CALLS = (
    r"""Run $plan.dotnet (@('build',$project,'-c',$configuration,'-t:Rebuild')+$common) 360 $control $true""",
    r"""Run $plan.dotnet (@('test',$project,'-c',$configuration,'--no-build','--filter','FullyQualifiedName~FEBuilderGBA.Avalonia.Tests.SyntheticPatchImportFixtureTests','--logger',"trx;LogFileName=$trx",'--results-directory',"$control\tests")+$common+@('--','xUnit.PreEnumerateTheories=false')) 180 $control $true""",
    r"""Run $plan.dotnet (@('publish',"$W\FEBuilderGBA.Avalonia\FEBuilderGBA.Avalonia.csproj",'-c','Release','-t:Rebuild,Publish','-o',"$B\publish")+$common) 360 $control $true""",
    r"""Run $plan.dotnet (@('publish',"$W\scripts\SyntheticProofFixtures\SyntheticProofFixtures.csproj",'-c','Debug','-t:Rebuild,Publish','-o',"$B\generator")+$common) 180 $control $true""",
)
_PS_QUOTED_OR_COMMENT = re.compile(
    r"""'(?:[^']|'')*'|"(?:`.|[^"`])*"|<\#.*?\#>|\#[^\r\n]*""", re.DOTALL
)


def _ps_source_mask(text, *, keep_strings=False):
    """Constrained lexical source checks; PowerShell AST checks are separate."""
    def replace(match):
        value = match.group()
        if keep_strings and value[0] in "'\"":
            return value
        return re.sub(r"[^\r\n]", " ", value)
    return _PS_QUOTED_OR_COMMENT.sub(replace, text)


def _require_source(condition, code):
    if not condition:
        raise AssertionError(code)


def _brace_end(text, opening):
    mask = _ps_source_mask(text)
    depth = 0
    for index in range(opening, len(mask)):
        depth += (mask[index] == "{") - (mask[index] == "}")
        if depth == 0:
            return index
    raise AssertionError("BuildTelemetry.Structure")


def _source_block(text, pattern):
    visible = _ps_source_mask(text, keep_strings=True)
    mask = _ps_source_mask(text)
    matches = [m for m in re.finditer(pattern, visible, re.MULTILINE)
               if mask[m.end() - 1] == "{"]
    _require_source(len(matches) == 1, "BuildTelemetry.Structure")
    opening = matches[0].end() - 1
    return opening, _brace_end(text, opening)


def _child_header(launch):
    return (r"^\s*function Invoke-LaunchEntry\s*\{" if launch
            else r"^\s*function Run\([^\n]*\)\s*\{")


def _cleared_environment_source(text, *, launch=False):
    start, end = _source_block(text, _child_header(launch))
    child = text[start + 1:end]
    opening, closing = _source_block(child, r"^\s*\$environment\s*=\s*@\{")
    return child, opening, closing


def _environment_entries(text, key, *, launch=False):
    child, opening, closing = _cleared_environment_source(text, launch=launch)
    function_start, _ = _source_block(text, _child_header(launch))
    table = child[opening + 1:closing]
    mask = _ps_source_mask(table)
    keys = list(re.finditer(
        r"(?:^|[;\n])\s*(" + re.escape(key) + r")\s*=", mask,
        re.MULTILINE | re.IGNORECASE,
    ))
    entries = []
    table_start = function_start + opening + 2
    for match in keys:
        terminator = re.search(r"[;\r\n]", mask[match.end():])
        end = match.end() + terminator.start() if terminator else len(table)
        stop = end + (end < len(table) and table[end] == ";")
        entries.append((match.group(1), table[match.end():end].strip(),
                        table_start + match.start(1), table_start + stop))
    return entries


def _assert_fixed_child_environment(text, key, *, launch=False, value="1", expression=None):
    child, opening, closing = _cleared_environment_source(text, launch=launch)
    entries = _environment_entries(text, key, launch=launch)
    expected = expression if expression is not None else "'" + value + "'"
    _require_source([(name, literal) for name, literal, _, _ in entries] == [(key, expected)],
                    "BuildTelemetry.EnvironmentPolicy")
    code = _ps_source_mask(child)
    receiver = "start" if launch else "info"
    clear = list(re.finditer(r"\$" + receiver + r"\.Environment\.Clear\(\)", code))
    population = (r"foreach\s*\(\$entry in \$environment\.GetEnumerator\(\)\)\s*\{\s*"
                  r"\$start\.Environment\[\$entry\.Key\]\s*=\s*\$entry\.Value\s*\}"
                  if launch else
                  r"foreach\s*\(\$k in \$environment\.Keys\)\s*\{\s*"
                  r"\$info\.Environment\[\$k\]=\[string\]\$environment\[\$k\]\s*\}")
    populate = list(re.finditer(
        population, code,
    ))
    start = list(re.finditer(
        r"\bInvoke-ProofSupervisedRunner\b" if launch else r"\$p\.Start\(\)", code,
    ))
    if launch:
        left, right = _source_block(text, r"^\s*function Invoke-ProofSupervisedRunner\s*\{")
        shared = _ps_source_mask(text[left + 1:right])
        _require_source(shared.count("[Diagnostics.Process]::Start($start)") == 1
                        and ".Environment" not in shared, "BuildTelemetry.SharedRunner")
    writes = list(re.finditer(r"\$" + receiver + r"\.Environment\[", code))
    _require_source(
        len(clear) == len(populate) == len(start) == len(writes) == 1
        and clear[0].start() < opening < closing < populate[0].start() < start[0].start()
        and populate[0].start() < writes[0].start() < populate[0].end(),
        "BuildTelemetry.EnvironmentOrder",
    )


def _assert_common_build_vectors(text):
    opening, closing = _source_block(text, r"^\s*if\s*\(\$Stage -ceq 'Build'\)\s*\{")
    build = text[opening + 1:closing]
    source = _ps_source_mask(build, keep_strings=True)
    common = list(re.finditer(r"^\s*\$common\s*=\s*@\(([^)]*)\)\s*$",
                              source, re.MULTILINE))
    _require_source(len(common) == 1, "BuildTelemetry.CommonPolicy")
    data = common[0].group(1)
    literals = re.findall(r"'((?:[^']|'')*)'", data)
    remainder = re.sub(r"'(?:[^']|'')*'", "", data)
    _require_source(not re.sub(r"[\s,]", "", remainder)
                    and literals == [*_COMMON_BUILD_PREFIX, _TELEMETRY_FLAG, *_USER_IMPORT_FLAGS],
                    "BuildTelemetry.CommonPolicy")
    calls = list(re.finditer(r"^\s*\$null=(Run \$plan\.dotnet [^\r\n]+)$",
                             source, re.MULTILINE))
    _require_source(tuple(m.group(1).strip() for m in calls) == _BUILD_CALLS,
                    "BuildTelemetry.BuildVectors")
    loop_start, loop_end = _source_block(
        build, r"^\s*foreach\s*\(\$configuration in @\('Debug','Release'\)\)\s*\{",
    )
    _require_source(common[0].end() < loop_start
                    and all(loop_start < m.start() < loop_end for m in calls[:2])
                    and all(m.start() > loop_end for m in calls[2:])
                    and len(calls[:2]) * 2 + len(calls[2:]) == 6,
                    "BuildTelemetry.BuildVectors")
    for guard in ("$c.total -ne '4'", "$c.executed -ne '4'", "$c.passed -ne '4'",
                  "$c.failed -ne '0'", "$results.Count -ne 4",
                  "$_.outcome -cne 'Passed'"):
        _require_source(guard in source, "BuildTelemetry.FixtureSelection")


def _child_control_source(text, keys, *, launch=False, value="1"):
    # In-memory edits of real source isolate negative checks; actual-source tests
    # below never use this control and must independently establish the policy.
    for key in keys:
        if not _environment_entries(text, key, launch=launch):
            _, opening, _ = _cleared_environment_source(text, launch=launch)
            function_start, _ = _source_block(text, _child_header(launch))
            insertion = function_start + opening + 2
            text = text[:insertion] + "\n                " + key + "='" + value + "';" + text[insertion:]
    return text


def _telemetry_control_source(text):
    text = _child_control_source(text, _PREPARATION_OPT_OUT_KEYS)
    build_start, build_end = _source_block(text, r"^\s*if\s*\(\$Stage -ceq 'Build'\)\s*\{")
    build = text[build_start + 1:build_end]
    common = re.search(r"^\s*\$common\s*=\s*@\(([^)]*)\)",
                       _ps_source_mask(build, keep_strings=True), re.MULTILINE)
    _require_source(common is not None, "BuildTelemetry.Structure")
    if _TELEMETRY_FLAG not in re.findall(r"'([^']*)'", common.group(1)):
        insertion = build_start + common.end()
        text = text[:insertion] + ",'-p:UsedAvaloniaProducts='" + text[insertion:]
    for flag in _USER_IMPORT_FLAGS:
        if flag not in re.findall(r"'([^']*)'", common.group(1)):
            opening, closing = _source_block(text, r"^\s*if\s*\(\$Stage -ceq 'Build'\)\s*\{")
            current = re.search(r"^\s*\$common\s*=\s*@\(([^)]*)\)",
                                _ps_source_mask(text[opening + 1:closing], keep_strings=True), re.MULTILINE)
            insertion = opening + current.end()
            text = text[:insertion] + ",'" + flag + "'" + text[insertion:]
    text = text.replace(_BUILD_CALLS[1].replace(_DISCOVERY_SUFFIX, ""), _BUILD_CALLS[1])
    return text


def _environment_negative_variants(text, key, *, launch=False, value="1"):
    entries = _environment_entries(text, key, launch=launch)
    _require_source(len(entries) == 1, "BuildTelemetry.Structure")
    _, _, start, end = entries[0]
    removed = text[:start] + text[end:]
    wrong = text[:start] + text[start:end].replace("'" + value + "'", "'wrong'") + text[end:]
    receiver = "start" if launch else "info"
    population = (
        "foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }"
        if launch else "foreach ($k in $environment.Keys) { $info.Environment[$k]=[string]$environment[$k] }"
    )
    clear = "$" + receiver + ".Environment.Clear()"
    return (
        ("removed", removed, "EnvironmentPolicy"),
        ("wrong", wrong, "EnvironmentPolicy"),
        ("inherited-only", "$env:" + key + "='" + value + "'\n" + removed, "EnvironmentPolicy"),
        ("comment-only", "<#\n" + key + "='" + value + "';\n#>\n" + removed, "EnvironmentPolicy"),
        ("late-clear", text.replace(clear, "").replace(population, population + "\n" + clear),
         "EnvironmentOrder"),
    )


def _assert_property_propagation(xml):
    document = ElementTree.fromstring(xml)
    sensitive = {"treataslocalproperty", "globalpropertiestoremove", "removeproperties",
                 "properties", "additionalproperties"}
    for element in document.iter():
        entries = [(key.rsplit("}", 1)[-1].lower(), value)
                   for key, value in element.attrib.items()]
        entries.append((element.tag.rsplit("}", 1)[-1].lower(), element.text or ""))
        for key, value in entries:
            if key not in sensitive:
                continue
            names = [part.split("=", 1)[0].strip().lower() for part in value.split(";")]
            protected = ["usedavaloniaproducts", *(flag[3:].split("=", 1)[0].lower()
                                                  for flag in _USER_IMPORT_FLAGS)]
            _require_source(not set(protected).intersection(names)
                            and "$(" not in value and "@(" not in value and "*" not in value,
                            "BuildTelemetry.PropertyPropagation")


_PROPERTY_FIXTURES = (
    ("ordinary-property", False, "<Project><PropertyGroup><UsedAvaloniaProducts>AvaloniaUI</UsedAvaloniaProducts></PropertyGroup></Project>"),
    ("unrelated-removal", False, '<Project><MSBuild RemoveProperties="Other" Properties="Unrelated=1"/></Project>'),
    ("treat-as-local", True, '<Project TreatAsLocalProperty="Configuration;UsedAvaloniaProducts"/>'),
    ("global-removal-element", True, "<Project><ProjectReference><GlobalPropertiesToRemove>UsedAvaloniaProducts</GlobalPropertiesToRemove></ProjectReference></Project>"),
    ("global-removal-attribute", True, '<Project><ProjectReference GlobalPropertiesToRemove="UsedAvaloniaProducts"/></Project>'),
    ("msbuild-removal", True, '<Project><MSBuild RemoveProperties="usedavaloniaproducts"/></Project>'),
    ("msbuild-override", True, '<Project><MSBuild Properties="Other=1;UsedAvaloniaProducts=Other"/></Project>'),
    ("reference-override-element", True, "<Project><ProjectReference><AdditionalProperties>UsedAvaloniaProducts=Other</AdditionalProperties></ProjectReference></Project>"),
    ("reference-override-attribute", True, '<Project><ProjectReference AdditionalProperties="UsedAvaloniaProducts=Other"/></Project>'),
    ("dynamic-removal", True, '<Project><MSBuild RemoveProperties="$(RemovedGlobals)"/></Project>'),
)


_CS_TRIVIA = re.compile(
    r"""//[^\r\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'""",
    re.DOTALL,
)


def _cs_mask(text, *, keep_strings=False):
    def replace(match):
        value = match.group()
        if keep_strings and not value.startswith(("//", "/*")):
            return value
        return re.sub(r"[^\r\n]", " ", value)
    return _CS_TRIVIA.sub(replace, text)


def _cs_body(text, declaration):
    """Balanced source-only C# check; never compile/evaluate a candidate."""
    mask = _cs_mask(text)
    matches = [m for m in re.finditer(declaration, _cs_mask(text, keep_strings=True))
               if mask[m.end() - 1] == "{"]
    _require_source(len(matches) == 1, "StartupAcquisition.Structure")
    opening = matches[0].end() - 1
    depth = 1
    for index in range(opening + 1, len(mask)):
        depth += (mask[index] == "{") - (mask[index] == "}")
        if depth == 0:
            return text[opening + 1:index]
    raise AssertionError("StartupAcquisition.Structure")


def _assert_initial_discover_boundary(source):
    acquisition = _cs_body(source, r"\binternal sealed class DesktopStartupAcquisition\s*\{")
    body = _cs_body(acquisition, r"\binternal bool TryDiscover<TNode>\([^{}]*\)"
                    r"\s*where TNode : class\s*\{")
    code = _cs_mask(body)
    guarded = _cs_body(body, r"\btry\s*\{")
    _require_source(re.sub(r"\s+", "", _cs_mask(guarded)) == "tree.Discover();",
                    "StartupAcquisition.InitialDiscoverOnly")
    catches = re.findall(r"\bcatch\s*(?:\([^)]*\))?", code)
    _require_source(catches == ["catch (DesktopTreeException ex)"],
                    "StartupAcquisition.TypedCatchOnly")
    catch = _cs_body(body, r"\bcatch \(DesktopTreeException ex\)\s*\{")
    catch_code = _cs_mask(catch, keep_strings=True)
    _require_source("IsUnavailable(ex, tree.Windows.Count)" in catch_code
                    and "throw;" in catch_code and "tree = null;" in catch_code,
                    "StartupAcquisition.ClosedTreeDiscard")
    _require_source(re.findall(r'\brecord\(([^;]*)\);', catch_code)
                    == ['"startup-snapshot-unavailable"']
                    and catch_code.index("guard();") < catch_code.index("record("),
                    "StartupAcquisition.BoundedSafeObservation")
    required = ("guard();", "if (Recovering)", "Used >= 3", "Used++",
                "new DesktopOwnedTree<TNode>", "try")
    _require_source(all(token in code for token in required), "StartupAcquisition.ChargeBeforeAcquisition")
    positions = [code.index(token) for token in required]
    _require_source(positions == sorted(positions), "StartupAcquisition.ChargeBeforeAcquisition")
    _require_source(not re.search(r"\b(?:Seed|RefreshCatalog|Capture|Observe|Revalidate|TreeCall)\s*\(", code)
                    and not re.search(r"\b(?:Message|QueryFailure|Deserialize)\b", code),
                    "StartupAcquisition.NoOtherCallsite")


class OfflinePatchImportProofContractTests(unittest.TestCase):
    def test_aggregate_fixture_root_is_checkout_owned(self):
        text = (PACKAGE / "test-pure.ps1").read_text(encoding="utf-8")
        expected = (
            r"$root=Join-Path $repository "
            r"('TestResults\offline-patch-proof-'+[guid]::NewGuid().ToString('N'))"
        )
        roots = [
            line.strip()
            for line in _ps_source_mask(text, keep_strings=True).splitlines()
            if re.match(r"^\s*\$root\s*=", line)
        ]
        self.assertEqual([expected], roots)
        self.assertLess(text.index(expected), text.index("CreateDirectory($root)"))

    def test_startup_acquisition_initial_discover_has_a_closed_typed_boundary(self):
        _assert_initial_discover_boundary((PACKAGE / "Policy.cs").read_text(encoding="utf-8"))

    def test_startup_acquisition_structural_checker_rejects_broadened_source(self):
        # Parser-sensitivity witness only, not production behavior or GUI evidence.
        witness = """
internal sealed class DesktopStartupAcquisition {
    internal bool TryDiscover<TNode>(Adapter adapter) where TNode : class {
        guard();
        if (Recovering) { if (Used >= 3) throw refusal; Used++; }
        tree = new DesktopOwnedTree<TNode>(adapter);
        try { tree.Discover(); }
        catch (DesktopTreeException ex) {
            guard();
            if (!IsUnavailable(ex, tree.Windows.Count)) throw;
            record("startup-snapshot-unavailable");
            tree = null;
            return false;
        }
        return true;
    }
}"""
        _assert_initial_discover_boundary(witness)
        variants = (
            witness.replace("catch (DesktopTreeException ex)", "catch (Exception ex)"),
            witness.replace("tree.Discover();", "tree.Discover(); Capture(tree);"),
            witness.replace("tree.Discover();", "tree.RefreshCatalog();"),
            witness.replace("tree.Discover();", '/* tree.Discover(); */ "tree.Discover();";'),
            witness.replace("Used++;", "").replace("try {", "Used++; try {"),
            witness.replace('record("startup-snapshot-unavailable");', "record(ex.Message);"),
            witness.replace("tree = null;", ""),
            witness.replace("tree.Windows.Count", "Bindings.Count"),
        )
        for variant in variants:
            with self.subTest(source=variants.index(variant)):
                with self.assertRaisesRegex(AssertionError, "StartupAcquisition."):
                    _assert_initial_discover_boundary(variant)

    def test_startup_acquisition_classifier_requires_every_typed_conjunct(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        body = _cs_body(policy, r"\binternal static bool IsUnavailable\("
                        r"DesktopTreeException exception, int currentWindows\)\s*\{")
        visible = _cs_mask(body, keep_strings=True)
        expected = {
            "Stage": '"loading-handoff"', "Selector": '"Discovery"', "Predicate": '"query-native-gone"',
            "SeedOrdinal": "1", "SeedResolveKeyEqual": "true", "ResolveAlive": "false",
            "ResolvePidRelation": '"zero"', "ExpectedOwnedRoot": "0", "PreviouslyOwnedHandle": "0",
            "OwnedRootBefore": "0", "OwnedRootAfter": "0", "AliveBefore": "null", "AliveAfter": "null",
            "OwnPidBefore": "null", "OwnPidAfter": "null", "RootMatchesBefore": "null", "RootMatchesAfter": "null",
        }
        for field, value in expected.items():
            self.assertRegex(visible, r"\b\w+\." + field + r"\s*==\s*" + re.escape(value))
        self.assertIn("currentWindows == 0", visible)
        self.assertGreaterEqual(visible.count("&&"), len(expected))
        self.assertNotRegex(_cs_mask(body), r"\b(?:Message|Deserialize|QueryFailure)\b")

    def test_startup_acquisition_driver_wires_both_passes_before_common_guarded_tail(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        handoff = _cs_body(desktop, r"\bvoid Handoff\(\)\s*\{")
        visible = _cs_mask(handoff, keep_strings=True)
        self.assertEqual(visible.count('Stage("loading-handoff", 45000)'), 1)
        self.assertEqual(visible.count("new DesktopStartupObservation(rootBindings)"), 1)
        self.assertEqual(visible.count("new DesktopStartupAcquisition(rootBindings, observation)"), 1)
        self.assertLess(visible.index("new DesktopStartupAcquisition"), visible.index("while (true)"))
        self.assertEqual(visible.count("TryReadStartupSample(acquisition,"), 2)
        for sample, acceptance in (("sample", "false"), ("acceptance", "true")):
            observed = f"ObserveStartup(observation, {sample}, {acceptance})"
            complete = f"acquisition.Complete(decision, {acceptance})"
            self.assertIn(observed, visible)
            self.assertIn(complete, visible)
            self.assertLess(visible.index(observed), visible.index(complete))
        self.assertLess(visible.index("acquisition.Complete(decision, false)"),
                        visible.index("ObserveStartup(observation, acceptance, true)"))
        self.assertRegex(visible, r"GuardStartupAcquisition\(\);\s*Thread.Sleep\(25\);")
        self.assertEqual(visible.count("Thread.Sleep(25)"), 1)
        self.assertNotRegex(_cs_mask(handoff), r"\b(?:continue|Dispatch|Invoke|OpenEditor|QueryFailure)\b")
        self.assertEqual(desktop.count("new DesktopStartupAcquisition("), 1)
        self.assertNotIn("DesktopStartupAcquisition", desktop[desktop.index("    void OpenEditor()"):])

    def test_startup_acquisition_capture_and_observation_stay_outside_optional_catch(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        sample = _cs_body(desktop, r"\bbool TryReadStartupSample\(DesktopStartupAcquisition acquisition,"
                          r"\s*out DesktopStartupSample<AutomationElement> sample\)\s*\{")
        visible = _cs_mask(sample, keep_strings=True)
        self.assertIn("startupSample = null;", visible)
        self.assertIn("acquisition.TryDiscover(", visible)
        self.assertIn("DesktopStartupSample<AutomationElement>.Capture(", visible)
        self.assertLess(visible.index("startupSample = null;"), visible.index("acquisition.TryDiscover("))
        self.assertLess(visible.index("acquisition.TryDiscover("),
                        visible.index("DesktopStartupSample<AutomationElement>.Capture("))
        self.assertNotRegex(_cs_mask(sample), r"\bcatch\b|\bIsUnavailable\s*\(")
        self.assertEqual(desktop.count(".TryDiscover("), 1)
        self.assertNotRegex(_cs_mask(desktop), r"QueryFailure\s*=\s*null")
        tree_call = _cs_body(desktop, r"\bTValue TreeCall<TValue>\(Func<TValue> operation\)\s*\{")
        self.assertIn("if (result.QueryFailure == null) result.QueryFailure = ex.Failure", tree_call)
        self.assertNotIn("IsUnavailable", tree_call)

    def test_startup_acquisition_uses_real_attempt_guards_before_both_reads(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        sample = _cs_body(desktop, r"\bbool TryReadStartupSample\(DesktopStartupAcquisition acquisition,"
                          r"\s*out DesktopStartupSample<AutomationElement> sample\)\s*\{")
        self.assertRegex(_cs_mask(sample), r"\bacquisition.TryDiscover\(\s*new OwnedTreeAdapter\(this\),"
                         r"\s*pid,\s*stage,\s*GuardStartupAcquisition,")
        self.assertRegex(_cs_mask(sample), r"out tree,\s*GuardStartupQuery\)\)\)")
        guard = _cs_body(desktop, r"\bvoid GuardStartupAcquisition\(\)\s*\{")
        self.assertIn("Identity();", _cs_mask(guard))
        self.assertIn("Guard();", _cs_mask(guard))
        self.assertRegex(_cs_mask(guard, keep_strings=True),
                         r'lock \(sync\)[\s{]*Require\(events.Count < 240, "event-bound"\)')
        self.assertNotRegex(_cs_mask(guard), r"\bStage\s*\(|\bstageAt\s*=")
        self.assertIn('BoundedWindowsReadiness.Capture().Ready, "readiness-lost"', guard)
        query = _cs_body(desktop, r"\bvoid GuardStartupQuery\(\)\s*\{")
        self.assertIn("Guard();", _cs_mask(query))
        self.assertIn('Require(events.Count < 240, "event-bound")', query)
        self.assertIn("catch (Refusal ex)", query)
        self.assertIn("throw new DesktopTreeGuardException(ex.Message)", query)
        self.assertNotIn("Identity();", query)

    def test_startup_acquisition_invalidation_preserves_all_sticky_history(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        body = _cs_body(policy, r"\binternal void InvalidateCandidate\(\)\s*\{")
        self.assertEqual(re.sub(r"\s+", "", _cs_mask(body)), "candidate=null;")
        acquisition = _cs_body(policy, r"\binternal sealed class DesktopStartupAcquisition\s*\{")
        self.assertNotRegex(_cs_mask(acquisition), r"\bUsed\s*(?:--|-=|=\s*0)|\b(?:Clear|Reset|Stage)\s*\(")
        self.assertNotIn("new DesktopRootBindings", acquisition)
        self.assertNotIn("new DesktopStartupObservation", acquisition)
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        self.assertIn("events.Count < 240", desktop)
        self.assertIn("clock.ElapsedMilliseconds, 240000", desktop)
        self.assertIn("Thread.Sleep(25)", desktop)

    def test_startup_acquisition_inventory_is_additive_in_both_public_routes(self):
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        self.assertIn("startupAcquisitionCaseNames.Count != 115", tests)
        self.assertIn("bf03596c4f1f37a75f440d50587f8372a5bd7ba1fdaa7af828dbd39879a95a2f", tests)
        self.assertIn("startupAcquisitionCaseNames.GetRange(0, 103)", tests)
        self.assertIn("231875f268ba40c2423ba30541c789056c03c844cd890b553cb3ec6c00b89909", tests)
        self.assertIn("ProjectStartup(Model, tree)", tests)
        self.assertIn("Startup acquisition assertions failed:", tests)
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            runner = (PACKAGE / name).read_text(encoding="utf-8")
            self.assertIn("[DesktopPolicyTests]::RunStartupAcquisitionTests()", runner)
            self.assertIn("[DesktopPolicyTests]::AssertStartupAcquisitionCaseInventory()", runner)
            self.assertRegex(runner, r"startupAcquisitionCases -ne 115")
            for count in (146, 75, 39, 522):
                self.assertIn(f"-ne {count}", runner)

    def test_empty_startup_discovery_is_discarded_before_capture(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        acquisition = _cs_body(policy, r"\binternal sealed class DesktopStartupAcquisition\s*\{")
        body = _cs_body(acquisition, r"\binternal bool TryDiscover<TNode>\([^{}]*\)"
                        r"\s*where TNode : class\s*\{")
        empty = _cs_body(body, r"\bif\s*\(\s*tree\.Windows\.Count == 0\s*\)\s*\{")
        self.assertEqual(re.sub(r"\s+", "", _cs_mask(empty)),
                         "Observation.InvalidateCandidate();Complete(newDesktopStartupDecision(),false);"
                         "tree=null;returnfalse;")
        self.assertLess(body.index("catch (DesktopTreeException ex)"),
                        body.index("if (tree.Windows.Count == 0)"))
        self.assertLess(body.index("if (tree.Windows.Count == 0)"), body.rindex("return true;"))
        _assert_initial_discover_boundary(policy)

    def test_editor_probe_inventory_is_closed_and_does_not_extend_result_schema(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        inventory = _cs_body(desktop, r"\benum EditorProbe\s*\{")
        self.assertEqual(re.findall(r"\b[A-Za-z]\w*\b", _cs_mask(inventory)), [
            "WizardCloseReturned", "PatchManagerInvokeReturned",
            "ImportCatalogStarted", "ImportCatalogCompleted",
            "ImportMainSearchStarted", "ImportMainNotFound",
            "ImportOtherSearchStarted", "ImportOtherNotFound",
            "ImportCandidateFound", "ImportTypeAccepted",
            "ImportEnabled", "ImportDisabled", "ImportOnscreen", "ImportOffscreen",
            "ImportSearchCompleted",
        ])
        self.assertIn("readonly HashSet<EditorProbe> editorProbes = new HashSet<EditorProbe>();", desktop)
        result = _cs_body(desktop, r"\bpublic sealed class DesktopResult\s*\{")
        self.assertNotRegex(result, r"EditorProbe|EditorProgress")

    def test_editor_probe_recorder_is_once_only_locked_and_has_no_provider_reads(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        recorder = _cs_body(desktop, r"\bvoid RecordEditorProbe\(EditorProbe probe\)\s*\{")
        self.assertEqual(re.sub(r"\s+", "", _cs_mask(recorder, keep_strings=True)),
                         'lock(sync){Require(!dispatch.IsClosed,"cancelled");'
                         'if(!editorProbes.Add(probe))return;'
                         'Record("observation","editor:"+probe.ToString());}')
        self.assertNotRegex(_cs_mask(recorder),
                            r"\.Current\b|\.Cached\b|Native\.|QueryRead|TreeCall|NewTree|Identity|Readiness")
        record = _cs_body(desktop, r"\bvoid Record\([^{}]*\)\s*\{")
        self.assertIn('Require(events.Count < 240, "event-bound")', record)

    def test_editor_catalog_markers_are_import_only_and_use_existing_root_roles(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        window = _cs_body(desktop, r"\bAutomationElement WindowFor\([^{}]*\)\s*\{")
        self.assertIn('bool traceImport = id == ImportButton && stage == "open-editor";', window)
        self.assertIn("if (traceImport) RecordEditorProbe(EditorProbe.ImportCatalogStarted);", window)
        self.assertIn("if (traceImport) RecordEditorProbe(EditorProbe.ImportCatalogCompleted);", window)
        self.assertIn("if (traceImport) RecordEditorProbe(EditorProbe.ImportSearchCompleted);", window)
        self.assertLess(window.index("ImportCatalogStarted"), window.index("var tree = NewTree();"))
        self.assertLess(window.index("var tree = NewTree();"), window.index("ImportCatalogCompleted"))
        self.assertIn("if (traceImport)\n                RecordEditorProbe(owned.Handle == Key(mainHandle)", window)
        self.assertIn("EditorProbe.ImportMainSearchStarted : EditorProbe.ImportOtherSearchStarted", window)
        self.assertIn("traceImport ? owned.Handle == Key(mainHandle) : (bool?)null", window)
        self.assertNotIn("ImportMainNotFound", window)
        self.assertNotIn("ImportOtherNotFound", window)

    def test_editor_control_markers_preserve_matches_and_state_read_order(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        control = _cs_body(desktop, r"\bAutomationElement Control\([^{}]*\)\s*\{")
        self.assertIn('bool traceImport = id == ImportButton && stage == "open-editor";', control)
        empty = _cs_body(control, r"\bif \(found.Count == 0\)\s*\{")
        self.assertIn("if (traceImport && importMainRoot.HasValue)", empty)
        self.assertIn("EditorProbe.ImportMainNotFound : EditorProbe.ImportOtherNotFound", empty)
        self.assertIn("return null;", empty)
        self.assertIn("if (traceImport) RecordEditorProbe(EditorProbe.ImportCandidateFound);", control)
        self.assertIn("if (traceImport) RecordEditorProbe(EditorProbe.ImportTypeAccepted);", control)
        self.assertIn("if (traceImport) RecordEditorProbe(enabled ? EditorProbe.ImportEnabled : EditorProbe.ImportDisabled);", control)
        self.assertIn("if (traceImport) RecordEditorProbe(offscreen ? EditorProbe.ImportOffscreen : EditorProbe.ImportOnscreen);", control)
        self.assertEqual(len(re.findall(r"\bQueryRead\(", _cs_mask(control))), 3)
        self.assertLess(control.index("control.Current.ControlType"), control.index("ImportTypeAccepted"))
        active = _cs_body(control, r"\bif \(active\)\s*\{")
        self.assertLess(active.index("control.Current.IsEnabled"), active.index("if (!enabled) return null;"))
        self.assertLess(active.index("if (!enabled) return null;"), active.index("control.Current.IsOffscreen"))
        self.assertLess(active.index("control.Current.IsOffscreen"), active.index("if (offscreen) return null;"))

    def test_editor_invoke_return_markers_follow_existing_invocations(self):
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        editor = _cs_body(desktop, r"\bvoid OpenEditor\(\)\s*\{")
        self.assertRegex(editor, r'Invoke\(wizard, "ContentRepoSetupWizard_Close_Button"\);\s*'
                                r"RecordEditorProbe\(EditorProbe.WizardCloseReturned\);")
        self.assertRegex(editor, r"Invoke\(main, MainButton\);\s*"
                                r"RecordEditorProbe\(EditorProbe.PatchManagerInvokeReturned\);")
        self.assertIn('Stage("open-editor", 30000)', editor)
        self.assertIn("editor = Await(() => WindowFor(ImportButton, ControlType.Button));", editor)
        invoke = _cs_body(desktop, r"\bvoid Invoke\([^{}]*\)\s*\{")
        self.assertNotIn("RecordEditorProbe", invoke)

    def test_preparation_run_has_exact_sdk_side_effect_controls(self):
        source = (PACKAGE / "prepare.ps1").read_text(encoding="utf-8")
        for key, value in _SDK_ENVIRONMENT.items():
            with self.subTest(key=key):
                _assert_fixed_child_environment(source, key, value=value)

    def test_sdk_environment_negative_source_variants(self):
        actual = (PACKAGE / "prepare.ps1").read_text(encoding="utf-8")
        for key, value in _SDK_ENVIRONMENT.items():
            source = _child_control_source(actual, (key,), value=value)
            _assert_fixed_child_environment(source, key, value=value)
            for name, variant, reason in _environment_negative_variants(source, key, value=value):
                with self.subTest(key=key, name=name), self.assertRaisesRegex(AssertionError, "BuildTelemetry." + reason):
                    _assert_fixed_child_environment(variant, key, value=value)
            entry, = _environment_entries(source, key)
            for wrong in ("$false", "$true", "'false'", "'true'", "'1'", "'0'"):
                if wrong == "'" + value + "'":
                    continue
                variant = source[:entry[2]] + key + "=" + wrong + ";" + source[entry[3]:]
                with self.subTest(key=key, value=wrong), self.assertRaisesRegex(AssertionError, "EnvironmentPolicy"):
                    _assert_fixed_child_environment(variant, key, value=value)

    def test_user_import_flags_are_ordered_global_and_cannot_be_overridden(self):
        source = _telemetry_control_source((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"))
        _assert_common_build_vectors(source)
        for flag in _USER_IMPORT_FLAGS:
            literal = ",'" + flag + "'"
            variants = (
                source.replace(literal, ""),
                source.replace(literal, literal.replace("=false", "=true")),
                source.replace(literal, literal + literal),
                source.replace(literal, "").replace(",'-p:UsedAvaloniaProducts='",
                                                    literal + ",'-p:UsedAvaloniaProducts='"),
            )
            for index, variant in enumerate(variants):
                with self.subTest(flag=flag, variant=index), self.assertRaisesRegex(AssertionError, "CommonPolicy"):
                    _assert_common_build_vectors(variant)
            property_name = flag[3:].split("=", 1)[0]
            for _, rejected, xml in _PROPERTY_FIXTURES:
                if rejected:
                    with self.subTest(property=property_name, xml=xml), self.assertRaisesRegex(
                            AssertionError, "PropertyPropagation"):
                        _assert_property_propagation(re.sub("UsedAvaloniaProducts", property_name, xml, flags=re.I))

    def test_only_test_vectors_end_with_deferred_theory_runsettings(self):
        actual = (PACKAGE / "prepare.ps1").read_text(encoding="utf-8")
        opening, closing = _source_block(actual, r"^\s*if\s*\(\$Stage -ceq 'Build'\)\s*\{")
        source = _ps_source_mask(actual[opening + 1:closing], keep_strings=True)
        calls = re.findall(r"^\s*\$null=(Run \$plan\.dotnet [^\r\n]+)$", source, re.MULTILINE)
        self.assertEqual(_BUILD_CALLS, tuple(call.strip() for call in calls))

    def test_deferred_runsettings_negative_source_variants(self):
        source = _telemetry_control_source((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"))
        _assert_common_build_vectors(source)
        variants = (
            source.replace(_DISCOVERY_SUFFIX, ""),
            source.replace(_DISCOVERY_SUFFIX, _DISCOVERY_SUFFIX.replace("=false", "=true")),
            source.replace("+$common" + _DISCOVERY_SUFFIX, _DISCOVERY_SUFFIX + "+$common"),
            source.replace(_DISCOVERY_SUFFIX, "+@('xUnit.PreEnumerateTheories=false')"),
            source.replace(_BUILD_CALLS[2], _BUILD_CALLS[2].replace("+$common", "+$common" + _DISCOVERY_SUFFIX)),
        )
        for index, variant in enumerate(variants):
            with self.subTest(variant=index), self.assertRaisesRegex(AssertionError, "BuildVectors"):
                _assert_common_build_vectors(variant)

    def test_child_module_cache_paths_are_owned_and_survive_clear(self):
        key = "PSModuleAnalysisCachePath"
        for name, launch, expression in (
            ("prepare.ps1", False, '"$prefix.module-analysis-cache"'),
            ("launch.ps1", True, "(Join-Path $launchRoot 'scratch\\ModuleAnalysisCache')"),
        ):
            with self.subTest(name=name):
                _assert_fixed_child_environment((PACKAGE / name).read_text(encoding="utf-8"), key,
                                                launch=launch, expression=expression)
        policy = (PACKAGE / "restage" / "RestagePolicy.ps1").read_text(encoding="utf-8")
        opening, closing = _source_block(policy, r"^\s*function Get-RChildEnvironment\([^\n]*\)\s*\{")
        self.assertIn('PSModuleAnalysisCachePath="$Outer\\scratch\\ModuleAnalysisCache"',
                      _ps_source_mask(policy[opening + 1:closing], keep_strings=True))
        golden = (PACKAGE / "restage" / "test-pure.ps1").read_text(encoding="utf-8")
        self.assertIn("PSModuleAnalysisCachePath='C:\\owned\\scratch\\ModuleAnalysisCache'", golden)
        self.assertIn("Missing/unowned module cache accepted.", golden)

    def test_loader_disables_autoload_and_imports_only_absolute_host_modules_first(self):
        text = (ROOT / "scripts" / "WindowsDesktopProof" / "PinnedLoader.ps1").read_text(encoding="utf-8")
        visible = _ps_source_mask(text, keep_strings=True)
        preamble = visible[:visible.index("function Get-PinnedProofFiles")]
        expected = """$ErrorActionPreference='Stop'
$PinnedProofEntryTicks=[Diagnostics.Stopwatch]::GetTimestamp()
Set-StrictMode -Version Latest
$PSModuleAutoLoadingPreference='None'
foreach($name in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security')){
    Import-Module ([IO.Path]::Combine($PSHOME,'Modules',$name,$name+'.psd1')) -ErrorAction Stop
}"""
        self.assertEqual(re.sub(r"\s+", "", expected),
                         re.sub(r"\s+", "", preamble[preamble.index("$ErrorActionPreference"):]))

    def test_real_discovery_probe_is_distinct_from_production_argv_contract(self):
        source = (ROOT / "FEBuilderGBA.Avalonia.Tests" / "SyntheticPatchImportDiscoveryTests.cs").read_text(encoding="utf-8")
        for token in ('"xunit.discovery.PreEnumerateTheories", false',
                      '"xunit.discovery.PreEnumerateTheories", true', "new TheoryDiscoverer(sink)",
                      "new FactDiscoverer(sink)", ".RunAsync(sink, bus,", "OfType<ITestPassed>()",
                      "Assert.Equal(4, total)", "Assert.Equal(0, Probes.UnselectedProviderCalls)",
                      "Assert.Equal(1, Probes.UnselectedProviderCalls)", "IReflectionMethodInfo",
                      "new ReflectionMethodInfo", "inner.GetCustomAttributes"):
            self.assertIn(token, source)
        probes = source[source.index("public sealed class Probes"):]
        self.assertNotRegex(probes, r"\[(?:Fact|Theory|AvaloniaFact|AvaloniaTheory)\b")
        self.assertIn("#pragma warning disable xUnit1008", probes)
        self.assertIn("#pragma warning restore xUnit1008", probes)
        self.assertNotRegex(source, r":\s*ReflectionMethodInfo|override.*GetCustomAttributes")

    def test_preparation_run_has_fixed_avalonia_opt_out(self):
        _assert_fixed_child_environment((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"),
                                        "AVALONIA_TELEMETRY_OPTOUT")

    def test_preparation_run_has_fixed_powershell_opt_out(self):
        _assert_fixed_child_environment((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"),
                                        "POWERSHELL_TELEMETRY_OPTOUT")

    def test_launch_runner_has_fixed_powershell_opt_out(self):
        _assert_fixed_child_environment((PACKAGE / "launch.ps1").read_text(encoding="utf-8"),
                                        "POWERSHELL_TELEMETRY_OPTOUT", launch=True)

    def test_preparation_six_msbuild_vectors_have_empty_global_property(self):
        _assert_common_build_vectors((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"))

    def test_telemetry_environment_negative_source_variants(self):
        source = _telemetry_control_source((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"))
        for key in _PREPARATION_OPT_OUT_KEYS:
            _assert_fixed_child_environment(source, key)
            for name, variant, reason in _environment_negative_variants(source, key):
                with self.subTest(key=key, name=name), self.assertRaisesRegex(AssertionError, "BuildTelemetry." + reason):
                    _assert_fixed_child_environment(variant, key)

    def test_telemetry_launch_environment_negative_source_variants(self):
        key = "POWERSHELL_TELEMETRY_OPTOUT"
        source = _child_control_source((PACKAGE / "launch.ps1").read_text(encoding="utf-8"),
                                       (key,), launch=True)
        _assert_fixed_child_environment(source, key, launch=True)
        for name, variant, reason in _environment_negative_variants(source, key, launch=True):
            with self.subTest(name=name), self.assertRaisesRegex(AssertionError, "BuildTelemetry." + reason):
                _assert_fixed_child_environment(variant, key, launch=True)

    def test_telemetry_global_property_negative_source_variants(self):
        source = _telemetry_control_source((PACKAGE / "prepare.ps1").read_text(encoding="utf-8"))
        _assert_common_build_vectors(source)
        flag = ",'-p:UsedAvaloniaProducts='"
        variants = (
            ("removed", source.replace(flag, ""), "CommonPolicy"),
            ("nonempty", source.replace(_TELEMETRY_FLAG + "'", _TELEMETRY_FLAG + "Other'"), "CommonPolicy"),
            ("duplicate", source.replace(flag, flag + flag), "CommonPolicy"),
            ("conflicting", source.replace(flag, flag + ",'-p:UsedAvaloniaProducts=Other'"), "CommonPolicy"),
            ("comment-only", "# '-p:UsedAvaloniaProducts='\n" + source.replace(flag, ""), "CommonPolicy"),
            ("site-missing-common", source.replace(
                _BUILD_CALLS[0], _BUILD_CALLS[0].replace("+$common", "")), "BuildVectors"),
            ("site-later-conflict", source.replace(
                _BUILD_CALLS[0], _BUILD_CALLS[0].replace("+$common", "+$common+@('-p:UsedAvaloniaProducts=Other')")), "BuildVectors"),
        )
        for name, variant, reason in variants:
            with self.subTest(name=name), self.assertRaisesRegex(AssertionError, "BuildTelemetry." + reason):
                _assert_common_build_vectors(variant)

    def test_telemetry_property_propagation_structural_fixtures(self):
        for name, rejected, xml in _PROPERTY_FIXTURES:
            with self.subTest(name=name):
                if rejected:
                    with self.assertRaisesRegex(AssertionError, "BuildTelemetry.PropertyPropagation"):
                        _assert_property_propagation(xml)
                else:
                    _assert_property_propagation(xml)

    def test_current_build_projects_do_not_remove_or_repurpose_the_global_property(self):
        projects = [(name, name + ".csproj") for name in (
            "FEBuilderGBA.Core", "FEBuilderGBA.SkiaSharp", "FEBuilderGBA.Avalonia",
            "FEBuilderGBA.Avalonia.Tests",
        )] + [("scripts", "SyntheticProofFixtures", "SyntheticProofFixtures.csproj")]
        for parts in projects:
            with self.subTest(project=parts):
                text = ROOT.joinpath(*parts).read_text(encoding="utf-8")
                _assert_property_propagation(text)
                for element in ElementTree.fromstring(text).iter():
                    values = [element.tag, element.text or "", *element.attrib.keys(), *element.attrib.values()]
                    protected = ["usedavaloniaproducts", *(flag[3:].split("=", 1)[0].lower()
                                                          for flag in _USER_IMPORT_FLAGS)]
                    self.assertFalse(any(name in value.lower() for name in protected for value in values),
                                     "New project property use needs renewed scope review.")

    def test_telemetry_ast_probe_is_authenticated_and_nonexecuting(self):
        tests = (ROOT / "scripts" / "WindowsDesktopProof" / "PinnedLoader.Tests.ps1").read_text(encoding="utf-8")
        probe = tests[tests.index("function Invoke-PinnedBuildTelemetryTests"):
                      tests.index("function Invoke-PinnedScopeTests")]
        for forbidden in ("GetScriptBlock", "Invoke-Expression", "Add-Type", "Assembly.Load",
                          "[Diagnostics.Process]", "Start-Process"):
            self.assertNotIn(forbidden, _ps_source_mask(probe))
        scope = tests[tests.index("function Invoke-PinnedScopeTests"):
                      tests.index("function Invoke-PinnedClosureTests")]
        self.assertLess(scope.index("Read-PinnedProofClosure"),
                        scope.index("Invoke-PinnedBuildTelemetryTests $prepare $launch $loader"))
        self.assertIn("$fixture.buffers['OfflinePatchImportProof\\prepare.ps1']", scope)
        self.assertIn("$fixture.buffers['OfflinePatchImportProof\\launch.ps1']", scope)
        self.assertIn("$fixture.buffers['WindowsDesktopProof\\PinnedLoader.ps1']", scope)
        self.assertIn("return (3+$telemetryCases)", scope)
        self.assertIn("$names.Count -ne 128", probe)
        self.assertIn("Case 'actual-test-discovery-suffix'", probe)

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
            "startupAcquisitionCases",
            "processImageCases", "retainedImageCases", "retainedImageReportingCases",
            "runtimeBindingCases", "windowsLexicalCases", "reportingWriterCases",
            "laterProductionCases", "configurationIntegrationCases", "jsonLimitCases",
            "compilerReferenceFixtureCases",
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
            "PreparedLaunch.ps1", "PreparedLaunch.cs", "PreparedLaunch.Tests.ps1", "PreparedLaunch.Tests.cs",
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
        self.assertIn("Invoke-ProofJsonLimitTests", aggregate)
        self.assertIn('ReadJson "$B\\build-attempt\\output-manifest.json" 33554432 1000000',
                      (PACKAGE / "prepare.ps1").read_text(encoding="utf-8"))
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
            "PrepareBundle", "PrepareWorkspace", "ActivatePrepared", "PreparedRun", "PreparedPure",
            "PreparedCold", "PreparedInertChild", "PreparedSharedStart",
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
        for token in ("new DesktopStartupObservation(rootBindings)", "TryReadStartupSample(",
                      "sample.Observe(observation, clock.ElapsedMilliseconds, loadingExists, revalidate)",
                      "result.StartupRoute = decision.Route",
                      "result.LoadingObserved = observation.LoadingObserved",
                      "startup-main-acceptance-control", "startup-wizard-acceptance-control"):
            self.assertIn(token, desktop)
        self.assertNotIn('"missing-loading-observation"', desktop)
        for token in ("DesktopPolicy.Handoff(true, LoadingAt", "unknown != 0",
                      "wizard.Owner == main.Handle", "startup-wizard-owner",
                      "startup-duplicate-root", "startup-duplicate-main", "startup-duplicate-loading",
                      "startup-duplicate-wizard", "startup-observation-order", "startup-acceptance-changed",
                      "main-visible-loading-not-observed", "real-main-visible-and-loading-destroyed"):
            self.assertIn(token, policy)
        handoff = desktop[desktop.index("    void Handoff()"):desktop.index("    void OpenEditor()")]
        self.assertLess(handoff.index("TryReadStartupSample(acquisition, out var sample)"),
                        handoff.index("ObserveStartup("))
        self.assertIn("ObserveStartup(observation, acceptance, true)", handoff)
        self.assertIn("ValidateWindow(window)", handoff)
        self.assertNotIn("ValidateWindow(window, decision.WindowClass)", handoff)
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
        for token in ("tree.Find(tree.WindowKey(window, selector)", "tree.Validate(handle, control, selector)",
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
        self.assertIn("a71b278f9d82083304e2cd1cc7e932ab920a5de660614c34353fb44601c83671", tests)

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

    def test_window_class_identity_is_per_handle_across_driver_operations(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        self.assertIn("name.Length == 45", policy)
        self.assertIn("roots.Count >= 32", policy)
        self.assertIn("known.Class != windowClass", policy)
        self.assertIn("known.Identity != null && known.Identity != identity", policy)
        self.assertIn("if (known.Identity == null) known.Identity = identity", policy)
        self.assertIn("bindings.BindCanonical(handle, after.windowClass, identity, after.owner)", policy)
        self.assertIn("bindings.BindNativeClass(parent.handle, parent.windowClass)", policy)
        self.assertNotIn("readonly string expectedClass", policy)
        self.assertNotIn("avaloniaClass", desktop)
        self.assertNotIn("string expectedClass", desktop)
        self.assertIn("pid, rootBindings, stage", desktop)
        validation = desktop[desktop.index("    void ValidateWindow("):desktop.index("    sealed class OwnedTreeAdapter")]
        self.assertIn("tree.ValidateWindow(window, kind, expectedOwner)", validation)
        self.assertNotIn("Class(", validation)
        action = desktop[desktop.index("    void Action("):desktop.index("    void Dispatch(")]
        self.assertLess(action.index("rootBindings.HasCanonical(handle)"),
                        action.index("rootBindings.Kind(handle)"))
        self.assertNotIn("Class(", action)
        self.assertIn("ValidateControl(control, window, Selector(id), expectedOwner)", desktop)
        self.assertIn('Action("PrintWindow-owned-editor-only", editor, 0)', desktop)
        self.assertIn("ValidateWindow(editor, expectedOwner: 0)", desktop)

    def test_startup_classes_are_stable_per_handle_not_shared_across_windows(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        observation = policy[policy.index("public sealed class DesktopStartupObservation"):
                             policy.index("public sealed class DesktopStartupFailureRoot")]
        self.assertIn("classes.TryGetValue(root.Handle", observation)
        self.assertIn("classes.Count < 32", observation)
        self.assertIn("knownClass == root.WindowClass", observation)
        self.assertIn("LoadingWindowClass = loading.WindowClass", observation)
        self.assertIn("WindowClass = main.WindowClass", observation)
        self.assertNotIn("WindowClass = loading.WindowClass;", observation.replace("LoadingWindowClass = loading.WindowClass;", ""))
        self.assertNotIn("root.WindowClass == WindowClass", observation)
        self.assertNotIn("wizard.WindowClass == main.WindowClass", observation)
        self.assertIn("candidate.MainHandle == expected.MainHandle", observation)
        self.assertIn("candidate.WindowClass == expected.WindowClass", observation)
        self.assertIn('"startup-wizard-owner"', observation)
        self.assertIn('"startup-wizard-handle"', observation)

    def test_startup_failure_capture_is_closed_immutable_and_has_no_live_reads(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        desktop = (PACKAGE / "Desktop.cs").read_text(encoding="utf-8")
        root = policy[policy.index("public sealed class DesktopStartupFailureRoot"):
                      policy.index("public sealed class DesktopStartupFailure\n")]
        fields = set(re.findall(r"public\s+\w+\s+(\w+)\s*\{\s*get;\s*\}", root))
        self.assertEqual({"Handle", "Owner", "WindowClass", "Visible", "MainControlVisible",
                          "LoadingLabelVisible", "SetupWizardControlVisible"}, fields)
        capture = policy[policy.index("internal sealed class DesktopStartupFailureCapture"):
                         policy.index("public static class InstalledDatabaseSnapshot")]
        for token in ("if (attempted) return", "attempted = true", "Predicates.Contains(predicate)",
                      "roots.Count > 8", "!seen.Add(root.Handle)", "bindings.MatchesProjection(",
                      "bindings.KnownNativeOwner(", "new DesktopStartupFailureRoot(root)"):
            self.assertIn(token, capture)
        self.assertNotRegex(capture, r"adapter\.|Native\.|TreeWalker|NewTree|ReadStartupSample|GetProcess|\.Current\.")
        self.assertIn("Roots = Array.AsReadOnly(roots)", policy)
        self.assertIn("public DesktopStartupFailure StartupFailure", desktop)
        self.assertIn('if (ex is Refusal && stage == "loading-handoff")', desktop)
        self.assertIn("startupFailure.Record(ex.Message, startupSample?.Roots, rootBindings)", desktop)
        self.assertIn("result.StartupFailure = startupFailure.Value", desktop)
        sample = desktop[desktop.index("    bool TryReadStartupSample"):
                         desktop.index("    DesktopStartupDecision ObserveStartup")]
        self.assertLess(sample.index("startupSample = null"), sample.index("acquisition.TryDiscover("))
        self.assertLess(sample.index("DesktopStartupSample<AutomationElement>.Capture"), sample.index("startupSample = sample"))
        self.assertNotIn("StartupFailure", (PACKAGE / "run.ps1").read_text(encoding="utf-8"))
        self.assertNotIn("StartupFailure", (PACKAGE / "launch.ps1").read_text(encoding="utf-8"))

    def test_window_identity_and_diagnostic_suites_are_wired_without_report_counters(self):
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        for token in ("RunDistinctClassLifecycle", "actual-policy-failure-publishes-owned-specific-snapshot",
                      "history-32-then-33-without-eviction", "same-main-hwnd-class-mutation-at-operation-refused",
                      "canonical-runtime-identity-mutation-across-tree-refused",
                      "maximum-handle-diagnostic-serialization-shape",
                      "known-but-unobserved-diagnostic-owner-", "diagnostic-source-error-never-replaces-failure",
                      "4e9cf5a22747adedbe9d24c13c774d0d8529d249de199c73b5c0567f3c024e8c",
                      "0e4ee056e2b08d79ac650bdcd368cc3e4b4a9d3b5f09edc273507a679d5910eb"):
            self.assertIn(token, tests)
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            text = (PACKAGE / name).read_text(encoding="utf-8")
            for token in ("[DesktopPolicyTests]::AssertStartupCaseInventory()",
                          "[DesktopPolicyTests]::RunWindowIdentityTests()",
                          "[DesktopPolicyTests]::AssertWindowIdentityCaseInventory()",
                          "$windowIdentityCases -ne 57", "$startupDiagnosticSerializationCases -ne 12",
                          "[Text.Encoding]::UTF8.GetByteCount($json) -le 4096",
                          "Assert-ProofKeys $decoded @('Predicate','Roots')",
                          "@('compact','pretty','nested')"):
                self.assertIn(token, text)
            self.assertNotRegex(text, r"\$report\.(?:windowIdentityCases|startupDiagnosticSerializationCases)")
            self.assertNotRegex(text, r"(?m)^\s*(?:windowIdentityCases|startupDiagnosticSerializationCases)\s*=")


class PickerIsolationContracts(unittest.TestCase):
    def test_empty_library_has_fixed_fresh_bounded_placement(self):
        source = (PACKAGE / "Configuration.ps1").read_text(encoding="utf-8")
        start, end = _source_block(
            source, r"^\s*function Initialize-ProofEmptyPatchLibrary\([^\n]*\)\s*\{")
        helper = source[start:end]
        for token in ("Assert-ProofPath $AppRoot -Existing", "'config\\patch2\\FE8U'",
                      "Assert-ProofPath $target", "[IO.File]::GetAttributes($cursor)",
                      "[IO.FileAttributes]::Directory", "$cursor -cne $target",
                      ".GetEnumerator()", ".MoveNext()", ".Dispose()", "return $true"):
            self.assertIn(token, helper)
        self.assertEqual(helper.count(".MoveNext()"), 1)
        self.assertNotRegex(helper, r"WriteAll|Copy|Get-ChildItem|SearchOption|GetFiles|ReadAll|catch\s*\{")

    def test_setup_occurs_once_after_projection_before_readiness(self):
        source = (PACKAGE / "run.ps1").read_text(encoding="utf-8")
        call = "$report.empty_patch_library_initialized=Initialize-ProofEmptyPatchLibrary (Join-Path $runRoot 'app')"
        self.assertEqual(source.count("Initialize-ProofEmptyPatchLibrary"), 1)
        self.assertIn(call, source)
        before, after = source.split(call)
        self.assertLess(before.index("foreach ($row in $manifest.appFiles)"),
                        before.index("CheckArchive (Join-Path $runRoot 'fixtures\\zipdb-invalid.zip') $false"))
        self.assertRegex(before, r"Budget\s+if \(\$clock.ElapsedMilliseconds -ge 150000\).*?\s*$")
        self.assertRegex(after, r"^\s+Budget\s+if \(\$clock.ElapsedMilliseconds -ge 150000\)")
        self.assertIn("[BoundedWindowsReadiness]::Capture()", after)
        self.assertIn("[Diagnostics.Process]::Start($start)", after)
        for token in ("$row.path.StartsWith('config\\patch2\\', [StringComparison]::OrdinalIgnoreCase)",
                      "$row.path.StartsWith('.patch2-import', [StringComparison]::OrdinalIgnoreCase)",
                      "throw 'Invalid or preseeded app relative path.'"):
            self.assertIn(token, source)
        config = (PACKAGE / "Configuration.ps1").read_text(encoding="utf-8")
        self.assertIn("patch2|\\.patch2-import.*|", config)

    def test_class_candidate_uses_existing_read_and_completed_refresh_only(self):
        policy = (PACKAGE / "Policy.cs").read_text(encoding="utf-8")
        root = policy.split("(uint owner, string windowClass) RootFacts(uint handle)", 1)[1].split(
            "DesktopOwnedWindow<TNode> Register", 1)[0]
        self.assertEqual(root.count("adapter.Class(handle)"), 1)
        self.assertIn('if (!DesktopRootBindings.ValidClass(windowClass)) Fail("query-root-class", windowClass);', root)
        fail = policy.split("void Fail(", 1)[1].split("void Require(", 1)[0]
        self.assertIn("string rejectedRootClass = null", fail)
        for token in ('code == "query-root-class"', "!Budget.Closed",
                      "failure.AliveBefore == true", "failure.OwnPidBefore == true",
                      "failure.OwnedRootBefore == h", "root == h",
                      "rejectedRootClass.Length >= 1", "rejectedRootClass.Length <= 256",
                      "c >= 32 && c <= 126", "failure.RejectedRootClass = rejectedRootClass"):
            self.assertIn(token, fail)
        self.assertNotIn("adapter.Class", fail)
        self.assertNotIn("Substring", fail)
        self.assertNotRegex(fail, r"if\s*\([^)]*RootMatches")
        self.assertEqual(fail.count("adapter.Alive("), 1)
        self.assertEqual(fail.count("adapter.NativePid("), 2)
        self.assertEqual(fail.count("adapter.NativeRoot("), 1)
        self.assertEqual(policy.count('Fail("query-root-class", windowClass)'), 1)

    def test_both_runners_execute_separate_inventories_and_exact_wire_schema(self):
        for name in ("test-pure.ps1", "validate-helper.ps1"):
            source = (PACKAGE / name).read_text(encoding="utf-8")
            for token in ("Invoke-EmptyPatchLibraryTests", "RunRejectedRootClassTests()",
                          "Assert-PickerIsolationInventories", "Invoke-RejectedRootClassSerializationTests",
                          "$queryDiagnosticCases -ne 39", "$queryDiagnosticSerializationNames.Count -eq 48",
                          "daf65e77d4b7fb0a6775f4a14193d0041270d10fa46256beacfb7c3a64e933f7",
                          "'ResolvePidRelation','RejectedRootClass','Nodes','Calls','Windows'",
                          "$decoded.Count -eq 21", "$decoded.ContainsKey('RejectedRootClass')",
                          "$decoded.RejectedRootClass -ceq $expectedRejectedClass",
                          "'resolve-success-before-registration-failure'){'OwnedAuxiliaryClass'}else{$null}",
                          "[Text.Encoding]::UTF8.GetByteCount($json) -le 4096"):
                self.assertIn(token, source)
        tests = (PACKAGE / "Configuration.Tests.ps1").read_text(encoding="utf-8")
        for token in ("@('compact','pretty','nested')", "$decoded.Count -eq 21",
                      "$decoded.RejectedRootClass -ceq $expected[$i]",
                      "[Text.Encoding]::UTF8.GetByteCount($json) -le 4096",
                      "QueryDiagnosticPrivateSentinels", "$decoded.Predicate -ceq $sample.Predicate"):
            self.assertIn(token, tests)
        tests = (PACKAGE / "Policy.Tests.cs").read_text(encoding="utf-8")
        self.assertIn("24484e7e79d5ca27aed177861afcdc98ac9d7248678ae5d5576bccfc0ff6efaa", tests)
        self.assertIn("queryDiagnosticSamples.Count != 16", tests)
        self.assertIn('model.Native[100].Class = "OwnedAuxiliaryClass"', tests)


if __name__ == "__main__":
    unittest.main()
