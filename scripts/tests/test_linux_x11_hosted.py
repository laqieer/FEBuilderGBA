# SPDX-License-Identifier: GPL-3.0-or-later
"""Pure hosted issue-2160 workflow contracts with no native execution."""

from __future__ import annotations

import os
import importlib.util
from datetime import datetime, timedelta, timezone
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.tests import test_linux_x11 as smoke_fixtures


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "scripts" / "linux_x11_hosted.py"
RUN_PAYLOAD_FIXTURE = {
    "head_repository": {"full_name": "laqieer/FEBuilderGBA"},
    "head_branch": "fix/issue-2160-linux-x11-attribution",
    "head_sha": "a" * 40,
    "run_attempt": 1,
    "path": ".github/workflows/e2e-norom.yml",
    "referenced_workflows": [
        {
            "path": "laqieer/FEBuilderGBA/.github/workflows/linux-x11-hosted.yml@" + ("a" * 40),
            "sha": "a" * 40,
            "ref": "refs/heads/fix/issue-2160-linux-x11-attribution",
        },
        {
            "path": "laqieer/FEBuilderGBA/.github/workflows/e2e-run.yml@" + ("a" * 40),
            "sha": "a" * 40,
            "ref": "refs/heads/fix/issue-2160-linux-x11-attribution",
        },
    ],
}


def load_module():
    spec = importlib.util.spec_from_file_location("_linux_x11_hosted_test", MODULE_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class HostedSourceTests(unittest.TestCase):
    def test_import_is_inert(self):
        with patch("subprocess.run", side_effect=AssertionError("process")), \
                patch("urllib.request.urlopen", side_effect=AssertionError("network")):
            module = load_module()
        self.assertEqual("issue2160-linux-x11-hosted-v1", module.OPERATION)

    def test_module_entry_help_runs_from_repository_root(self):
        result = subprocess.run(
            [sys.executable, "-B", "-m", "scripts.linux_x11_hosted", "--help"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("authorize-native", result.stdout)
        self.assertIn("stage-artifacts", result.stdout)
        self.assertIn("summarize-receipt", result.stdout)
        self.assertIn("validate-receipt", result.stdout)


class HostedWorkflowContracts(unittest.TestCase):
    CANARY_KEY = "authority_cookie"

    def setUp(self):
        self.hosted = load_module()
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-hosted-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        for relative, content in {
            ".github/workflows/e2e-norom.yml": "caller\n",
            ".github/workflows/linux-x11-hosted.yml": "hosted\n",
            "scripts/linux_x11.py": "x11\n",
            "scripts/linux_x11_hosted.py": "helper\n",
            "scripts/tests/linux_x11_native_smoke.py": "smoke\n",
        }.items():
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        lib = self.root / "usr" / "lib" / "libX11.so.6"
        lib.parent.mkdir(parents=True, exist_ok=True)
        lib.write_text("libx11\n", encoding="utf-8")
        self.env = {
            "GITHUB_REPOSITORY": self.hosted.REPOSITORY,
            "ISSUE2160_ROUTE": self.hosted.PREPARE_ROUTE,
            "ISSUE2160_CANDIDATE_SHA": "a" * 40,
            "ISSUE2160_RECEIPT_STEM": "receipt-stem",
            "ISSUE2160_GRANT_LOOKUP_KEY": "grant-key",
            "GITHUB_SHA": "a" * 40,
            "GITHUB_WORKFLOW_SHA": "a" * 40,
            "GITHUB_RUN_ID": "12345",
            "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_TOKEN": "token",
            "RUNNER_OS": "Linux",
            "RUNNER_ARCH": "X64",
            "RUNNER_NAME": "Hosted Agent",
            "ImageOS": "ubuntu24",
            "ImageVersion": "20261001.1",
        }
        self.source_patch = patch.object(
            self.hosted,
            "SOURCE_FILES",
            tuple(str(path).replace("\\", "/") for path in (
                Path(".github/workflows/e2e-norom.yml"),
                Path(".github/workflows/linux-x11-hosted.yml"),
                Path("scripts/linux_x11.py"),
                Path("scripts/linux_x11_hosted.py"),
                Path("scripts/tests/linux_x11_native_smoke.py"),
            )),
        )
        self.lib_patch = patch.object(self.hosted, "LIBX11_CANDIDATES", (str(lib),))
        self.source_patch.start()
        self.lib_patch.start()
        self.package_patch = patch.object(
            self.hosted,
            "_package_record",
            side_effect=lambda path, query: {
                "path": str(path).replace("\\", "/"),
                "resolved_path": str(path).replace("\\", "/"),
                "mode": 0o755 if "Xvfb" in str(path) or "python3" in str(path) else 0o644,
                "sha256": ("a" if "python3" in str(path) else "b" if "Xvfb" in str(path) else "c") * 64,
                "package": {
                    "name": "python3-minimal" if "python3" in str(path) else "xvfb" if "Xvfb" in str(path) else "libx11-6:amd64",
                    "version": "1.0",
                    "architecture": "amd64",
                },
            },
        )
        self.package_patch.start()
        self.uid_stack = [
            patch.object(self.hosted.os, "getuid", return_value=1000, create=True),
            patch.object(self.hosted.os, "geteuid", return_value=1000, create=True),
            patch.object(self.hosted.os, "getgid", return_value=1000, create=True),
            patch.object(self.hosted.os, "getegid", return_value=1000, create=True),
        ]
        for item in self.uid_stack:
            item.start()
        self.addCleanup(self.source_patch.stop)
        self.addCleanup(self.lib_patch.stop)
        self.addCleanup(self.package_patch.stop)
        for item in reversed(self.uid_stack):
            self.addCleanup(item.stop)

    def query(self, command, *, cwd=None, timeout=10):
        mapping = {
            ("git", "rev-parse", "HEAD"): "a" * 40,
        }
        return mapping[tuple(command)]

    def fetch(self, url, token, *, timeout=10):
        self.assertEqual("token", token)
        if "/actions/runs/" in url:
            return dict(RUN_PAYLOAD_FIXTURE)
        if "/issues/2160/comments" in url:
            return []
        raise AssertionError(url)

    def native_sources(self):
        return self.hosted.native_smoke.source_hashes()

    def valid_preflight(self):
        return {
            "source_hashes": self.hosted._source_hashes(ROOT),
            "tools": {
                "python3": {"path": "/usr/bin/python3"},
                "xvfb": {"path": "/usr/bin/Xvfb"},
                "libx11": {
                    "path": "/usr/lib/x86_64-linux-gnu/libX11.so.6",
                    "resolved_path": "/usr/lib/x86_64-linux-gnu/libX11.so.6.4.0",
                    "sha256": "d" * 64,
                },
            },
            "preflight_digest": "digest",
        }

    def preflight_for_receipt(self, receipt):
        preflight = json.loads(json.dumps(self.valid_preflight()))
        xvfb = receipt.get("xvfb")
        if isinstance(xvfb, dict):
            command = xvfb.get("command")
            if isinstance(command, list) and command and isinstance(command[0], str):
                preflight["tools"]["xvfb"]["path"] = command[0]
        return preflight

    def grant_fields(self, preflight, now):
        return {
            "lookup_key": preflight["grant_lookup_key"],
            "run_id": preflight["run_binding"]["run_id"],
            "run_attempt": str(preflight["run_binding"]["run_attempt"]),
            "candidate_sha": preflight["candidate_sha"],
            "caller_workflow_path": preflight["run_binding"]["caller_workflow_path"],
            "caller_workflow_sha": preflight["run_binding"]["caller_workflow_sha"],
            "hosted_workflow_path": preflight["run_binding"]["hosted_workflow_path"],
            "hosted_workflow_sha": preflight["run_binding"]["hosted_workflow_sha"],
            "preflight_digest": preflight["preflight_digest"],
            "tool_constraints_digest": preflight["tool_constraints_digest"],
            "receipt_stem": preflight["receipt_stem"],
            "operation": self.hosted.OPERATION,
            "invocations": "1",
            "timeout_total": "20",
            "timeout_work": "18",
            "timeout_cleanup": "2",
            "valid_after": (now - timedelta(minutes=1)).isoformat().replace("+00:00", "Z"),
            "valid_before": (now + timedelta(minutes=1)).isoformat().replace("+00:00", "Z"),
        }

    def grant_footer_lines(self, *, cli_version="1.0.92-3", display_name="GPT-5.4", model_id="gpt-5.4"):
        return (
            f"Copilot CLI: {cli_version}",
            f"Model: {display_name} ({model_id})",
        )

    def actual_grant_footer_fixtures(self):
        return (
            self.grant_footer_lines(cli_version="1.0.85", display_name="Auto", model_id="auto"),
            self.grant_footer_lines(),
        )

    def grant_body(
            self,
            preflight,
            now,
            *,
            grant_fields=None,
            footer_lines=None,
            line_ending="\n",
            final_newline=False):
        fields = self.grant_fields(preflight, now) if grant_fields is None else grant_fields
        footer_lines = self.grant_footer_lines() if footer_lines is None else footer_lines
        body = line_ending.join(
            [self.hosted.GRANT_SCHEMA]
            + [f"{key}={value}" for key, value in fields.items()]
            + [""]
            + list(footer_lines)
        )
        if final_newline:
            body += line_ending
        return body

    def pass_report(self):
        native_sources = self.native_sources()
        return {
            "status": "passed",
            "window": 9,
            "owner_display": 10,
            "observer_display": 11,
            "present_before_destroy": [9],
            "live_value": "owned smoke",
            "missing_property_ok": True,
            "owned_destroyed": {"display": 10, "window": 9},
            "badwindow_request": {"display": 11, "serial": 7, "window": 9},
            "badwindow_events": [{
                "callback_display": 11,
                "display": 11,
                "serial": 7,
                "resourceid": 9,
                "error_code": 3,
                "request_code": 20,
                "minor_code": 0,
                "type": 0,
            }],
            "fresh_children_display": 11,
            "fresh_children_after_destroy": [],
            "unrelated_rejected": [{
                "callback_display": 11,
                "display": 11,
                "serial": 8,
                "resourceid": 9,
                "error_code": 3,
                "request_code": 19,
                "minor_code": 0,
                "type": 0,
            }],
            "pending_error_rejected": True,
            "sources": native_sources,
            "worker": {"pid": 101, "start_ticks": 202},
            "libx11": {
                "path": "/usr/lib/x86_64-linux-gnu/libX11.so.6",
                "resolved_path": "/usr/lib/x86_64-linux-gnu/libX11.so.6.4.0",
                "sha256": "d" * 64,
            },
        }

    def pass_receipt(self):
        native_sources = self.native_sources()
        worker_stdout = json.dumps(self.pass_report())
        worker_stdout_bytes = len(worker_stdout.encode("utf-8"))
        return {
            "status": "passed",
            "timeout_seconds": 20,
            "phase": "validation",
            "elapsed_seconds": 1.2,
            "started_utc": "2026-10-02T00:00:00Z",
            "supervisor": {"pid": 100, "start_ticks": 200},
            "sources": native_sources,
            "diagnostic": self.pass_report(),
            "worker": {"pid": 101, "start_ticks": 202, "command": [
                "/usr/bin/python3", "-B", "-m", "scripts.tests.linux_x11_native_smoke",
                "--allow-native-smoke", "--worker",
                "--xlib-path", "/usr/lib/x86_64-linux-gnu/libX11.so.6",
            ]},
            "worker_exit_code": 0,
            "xvfb": {"pid": 102, "start_ticks": 204, "command": [
                "/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24",
                "-nolisten", "tcp", "-auth", "/tmp/linux-x11-smoke/authority", "-noreset",
            ]},
            "xvfb_observed_exit_code": None,
            "xvfb_exit_code": 0,
            "io": {
                "displayfd": {"text": "7\n", "limit_bytes": 32, "observed_bytes": 2, "retained_bytes": 2, "eof": False, "truncated": False, "error": None},
                "xvfb_stderr": {"text": "", "limit_bytes": 4096, "observed_bytes": 0, "retained_bytes": 0, "eof": False, "truncated": False, "error": None},
                "worker_stdout": {"text": worker_stdout, "limit_bytes": 16384, "observed_bytes": worker_stdout_bytes, "retained_bytes": worker_stdout_bytes, "eof": True, "truncated": False, "error": None},
                "worker_stderr": {"text": "", "limit_bytes": 4096, "observed_bytes": 0, "retained_bytes": 0, "eof": True, "truncated": False, "error": None},
            },
        }

    def actual_smoke_receipt(self, *, prepare_kwargs=None, mutate=None, xlib_path="/usr/lib/x86_64-linux-gnu/libX11.so.6"):
        case = smoke_fixtures.SmokeDiagnosticsTests(methodName="runTest")
        case.setUp()
        try:
            kwargs = {} if prepare_kwargs is None else dict(prepare_kwargs)
            kwargs.setdefault("xlib_path", xlib_path)
            case.prepare_supervisor(**kwargs)
            if mutate is not None:
                mutate(case)
            _, receipt = case.supervise(xlib_path=xlib_path)
            return receipt
        finally:
            case.doCleanups()

    def inject_recursive_canaries(self, value, path="root"):
        cloned = json.loads(json.dumps(value))

        def visit(node, location):
            if isinstance(node, dict):
                node[self.CANARY_KEY] = f"secret:{location}"
                for key, child in list(node.items()):
                    if key != self.CANARY_KEY:
                        visit(child, f"{location}.{key}")
            elif isinstance(node, list):
                for index, child in enumerate(node):
                    visit(child, f"{location}[{index}]")

        visit(cloned, path)
        return cloned

    def assert_recursive_canary_absent(self, value):
        if isinstance(value, dict):
            self.assertNotIn(self.CANARY_KEY, value)
            for child in value.values():
                self.assert_recursive_canary_absent(child)
        elif isinstance(value, list):
            for child in value:
                self.assert_recursive_canary_absent(child)

    def permissive_pass_report_with_canaries(self, report):
        report = json.loads(json.dumps(report))
        report[self.CANARY_KEY] = "secret:diagnostic"
        report["sources"][self.CANARY_KEY] = "0" * 64
        report["worker"][self.CANARY_KEY] = "secret:diagnostic.worker"
        report["libx11"][self.CANARY_KEY] = "secret:diagnostic.libx11"
        return report

    def mutate_path(self, value, path, replacement):
        cloned = json.loads(json.dumps(value))
        target = cloned
        for segment in path[:-1]:
            target = target[segment]
        target[path[-1]] = replacement
        return cloned

    def assert_stage_artifacts_rejects_without_publishing_summary(self, receipt, *, canary_text=None, preflight=None):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-stage-invalid-summary-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        old_cwd = Path.cwd()
        os.chdir(root)
        self.addCleanup(lambda: os.chdir(old_cwd))
        bundle = root / "upload"
        preflight = ((self.preflight_for_receipt(receipt) if preflight is None else preflight) | {"receipt_stem": "receipt-stem"})
        grant = {"grant": {"receipt_stem": "receipt-stem"}}
        preflight_path = root / "preflight.json"
        grant_path = root / "grant.json"
        preflight_path.write_text(json.dumps(preflight), encoding="utf-8")
        grant_path.write_text(json.dumps(grant), encoding="utf-8")
        receipt_dir = root / "linux-x11-smoke-receipt-stem"
        receipt_dir.mkdir()
        with (receipt_dir / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.main([
                "stage-artifacts",
                "--preflight", str(preflight_path),
                "--grant", str(grant_path),
                "--output-dir", str(bundle),
            ])
        summary_path = bundle / "receipt-summary.json"
        self.assertFalse(summary_path.exists())
        if bundle.exists():
            bundle_text = "".join(
                path.read_text(encoding="utf-8", errors="ignore")
                for path in bundle.glob("*.json")
            )
            self.assertNotIn(self.CANARY_KEY, bundle_text)
            if canary_text is not None:
                self.assertNotIn(canary_text, bundle_text)

    def assert_stage_artifacts_summary(self, receipt, *, preflight=None):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-stage-summary-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        old_cwd = Path.cwd()
        os.chdir(root)
        self.addCleanup(lambda: os.chdir(old_cwd))
        bundle = root / "upload"
        preflight = ((self.preflight_for_receipt(receipt) if preflight is None else preflight) | {"receipt_stem": "receipt-stem"})
        grant = {"grant": {"receipt_stem": "receipt-stem"}}
        preflight_path = root / "preflight.json"
        grant_path = root / "grant.json"
        preflight_path.write_text(json.dumps(preflight), encoding="utf-8")
        grant_path.write_text(json.dumps(grant), encoding="utf-8")
        receipt_dir = root / "linux-x11-smoke-receipt-stem"
        receipt_dir.mkdir()
        with (receipt_dir / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        self.hosted.main([
            "stage-artifacts",
            "--preflight", str(preflight_path),
            "--grant", str(grant_path),
            "--output-dir", str(bundle),
        ])
        return json.loads((bundle / "receipt-summary.json").read_text(encoding="utf-8"))

    def safe_xvfb_command(self, executable="/usr/bin/Xvfb"):
        return [
            executable,
            "-displayfd", "11",
            "-screen", "0", "320x240x24",
            "-nolisten", "tcp",
            "-noreset",
        ]

    def test_prepare_payload_is_metadata_only_and_bound(self):
        payload = self.hosted.prepare_payload(
            self.root,
            self.env,
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        self.assertEqual(self.hosted.PREPARE_ROUTE, payload["route"])
        self.assertEqual("12345", payload["run_binding"]["run_id"])
        self.assertEqual(self.hosted.CALLER_WORKFLOW_PATH, payload["run_binding"]["caller_workflow_path"])
        self.assertEqual("/usr/bin/python3", payload["tools"]["python3"]["path"])
        self.assertEqual("/usr/bin/Xvfb", payload["tools"]["xvfb"]["path"])
        self.assertEqual("grant-key", payload["grant_lookup_key"])
        self.assertEqual(set(self.hosted.SOURCE_FILES), set(payload["source_hashes"]))
        self.assertIn("stable_constraints_digest", payload)

    def test_prepare_payload_fails_closed_when_required_tool_is_missing(self):
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.prepare_payload(
                self.root,
                self.env,
                query=self.query,
                fetch_json=self.fetch,
                which=lambda name: None if name == "Xvfb" else f"/usr/bin/{name}",
            )

    def test_prepare_payload_requires_explicit_candidate_sha_without_github_fallback(self):
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.prepare_payload(
                self.root,
                {key: value for key, value in self.env.items() if key != "ISSUE2160_CANDIDATE_SHA"},
                query=self.query,
                fetch_json=self.fetch,
                which=lambda name: f"/usr/bin/{name}",
            )

    def test_parse_instant_requires_explicit_aware_absolute_timestamp(self):
        self.assertEqual(
            datetime(2026, 10, 2, 0, 0, tzinfo=timezone.utc),
            self.hosted._parse_instant("2026-10-02T08:00:00+08:00"),
        )
        self.assertEqual(
            datetime(2026, 10, 2, 0, 0, tzinfo=timezone.utc),
            self.hosted._parse_instant("2026-10-02T00:00:00Z"),
        )
        for value in ("2026-10-02T00:00:00", "2026-10-02", "not-a-timestamp"):
            with self.subTest(value=value), self.assertRaises(self.hosted.HostedWorkflowError):
                self.hosted._parse_instant(value)

    def test_run_binding_accepts_actual_referenced_workflow_shape_and_rejects_conflicts(self):
        binding = self.hosted._run_binding(self.env, "token", lambda url, token, timeout=10: dict(RUN_PAYLOAD_FIXTURE))
        self.assertEqual(self.hosted.HOSTED_WORKFLOW_PATH, binding["hosted_workflow_path"])
        hosted_only = dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[RUN_PAYLOAD_FIXTURE["referenced_workflows"][0]])
        binding = self.hosted._run_binding(self.env, "token", lambda url, token, timeout=10: hosted_only)
        self.assertEqual(self.hosted.HOSTED_WORKFLOW_PATH, binding["hosted_workflow_path"])
        bad_payloads = [
            dict(RUN_PAYLOAD_FIXTURE, head_repository=None),
            dict(RUN_PAYLOAD_FIXTURE, head_repository={}),
            dict(RUN_PAYLOAD_FIXTURE, head_repository={"full_name": None}),
            dict(RUN_PAYLOAD_FIXTURE, head_repository={"full_name": "FEBuilderGBA/FEBuilderGBA"}),
            dict(RUN_PAYLOAD_FIXTURE, head_branch=None),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[{
                "path": "other/repo/.github/workflows/linux-x11-hosted.yml@" + ("a" * 40),
                "sha": "a" * 40,
                "ref": "refs/heads/master",
            }]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[{
                "path": "laqieer/FEBuilderGBA/.github/workflows/linux-x11-hosted.yml@" + ("b" * 40),
                "sha": "a" * 40,
                "ref": "refs/heads/master",
            }]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[{
                "path": "laqieer/FEBuilderGBA/.github/workflows/linux-x11-hosted.yml@" + ("a" * 40),
                "sha": "a" * 40,
                "ref": "refs/heads/other-branch",
            }]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[{
                "path": "laqieer/FEBuilderGBA/.github/workflows/linux-x11-hosted.yml@" + ("a" * 40),
                "sha": "b" * 40,
                "ref": "refs/heads/fix/issue-2160-linux-x11-attribution",
            }]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[{
                "path": "laqieer/FEBuilderGBA/.github/workflows/linux-x11-hosted.yml@notasha",
                "sha": "a" * 40,
                "ref": "refs/heads/fix/issue-2160-linux-x11-attribution",
            }]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[{
                "path": "laqieer/FEBuilderGBA/.github/workflows/unexpected.yml@" + ("a" * 40),
                "sha": "a" * 40,
                "ref": "refs/heads/fix/issue-2160-linux-x11-attribution",
            }]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[
                RUN_PAYLOAD_FIXTURE["referenced_workflows"][0],
                RUN_PAYLOAD_FIXTURE["referenced_workflows"][0],
            ]),
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=[
                RUN_PAYLOAD_FIXTURE["referenced_workflows"][0],
                RUN_PAYLOAD_FIXTURE["referenced_workflows"][1],
                RUN_PAYLOAD_FIXTURE["referenced_workflows"][1],
            ]),
        ]
        for payload in bad_payloads:
            with self.subTest(payload=payload):
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted._run_binding(self.env, "token", lambda url, token, timeout=10, payload=payload: payload)

    def test_authorize_native_rejects_reruns_before_grant_lookup(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.authorize_native(
                self.root,
                preflight,
                {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE, "GITHUB_RUN_ATTEMPT": "2"},
                query=self.query,
                fetch_json=lambda *args, **kwargs: (_ for _ in ()).throw(AssertionError("grant lookup")),
                which=lambda name: f"/usr/bin/{name}",
            )

    def test_authorize_native_accepts_fresh_runner_metadata_but_rejects_constraint_drift(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)
        grant_body = self.grant_body(preflight, now)
        comments = [{
            "id": 5,
            "body": grant_body,
            "user": {"login": "laqieer"},
            "author_association": "OWNER",
            "created_at": "2026-10-02T00:00:00Z",
            "updated_at": "2026-10-02T00:00:00Z",
        }]
        fresher = {
            **self.env,
            "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE,
            "RUNNER_NAME": "Another Hosted Agent",
            "ImageOS": "ubuntu24-other",
            "ImageVersion": "20261002.2",
        }
        with patch.object(self.hosted.os, "getuid", return_value=2000, create=True), \
                patch.object(self.hosted.os, "geteuid", return_value=2000, create=True), \
                patch.object(self.hosted.os, "getgid", return_value=3000, create=True), \
                patch.object(self.hosted.os, "getegid", return_value=3000, create=True):
            grant = self.hosted.authorize_native(
                self.root,
                preflight,
                fresher,
                query=self.query,
                fetch_json=lambda url, token, timeout=10: dict(RUN_PAYLOAD_FIXTURE)
                if "/actions/runs/" in url else comments,
                which=lambda name: f"/usr/bin/{name}",
                now=now,
            )
        self.assertEqual(5, grant["grant_freeze"]["comment_id"])
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.authorize_native(
                self.root,
                preflight,
                {**fresher, "RUNNER_ARCH": "ARM64"},
                query=self.query,
                fetch_json=lambda url, token, timeout=10: dict(RUN_PAYLOAD_FIXTURE)
                if "/actions/runs/" in url else comments,
                which=lambda name: f"/usr/bin/{name}",
                now=now,
            )

    def test_grant_lookup_rejects_absent_mismatched_expired_and_wrong_author(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)
        base = self.grant_fields(preflight, now)

        def comment(
            body,
            *,
            author="laqieer",
            association="OWNER",
            comment_id=1,
            created_at="2026-10-02T00:00:00Z",
            updated_at="2026-10-02T00:00:00Z",
        ):
            return {
                "id": comment_id,
                "body": body,
                "user": {"login": author},
                "author_association": association,
                "created_at": created_at,
                "updated_at": updated_at,
            }

        body = self.grant_body(preflight, now, grant_fields=base)
        self.assertEqual(
            1,
            self.hosted.find_matching_grant([comment(body)], preflight, now=now)["id"],
        )
        bad_cases = (
            [comment(body, author="other")],
            [comment(body, association="MEMBER")],
            [comment(body.replace("run_id=12345", "run_id=99999", 1))],
            [comment(body.replace(base["valid_before"], (now - timedelta(minutes=2)).isoformat().replace("+00:00", "Z"), 1))],
            [comment(body, created_at=None)],
            [comment(body, updated_at=None)],
            [comment(body, created_at="not-a-timestamp")],
            [comment(body, updated_at="not-a-timestamp")],
            [comment(body, created_at="2026-10-02T00:00:02Z", updated_at="2026-10-02T00:00:01Z")],
            [comment(body), comment(body, comment_id=2)],
            [],
        )
        for comments in bad_cases:
            with self.subTest(comments=comments):
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.find_matching_grant(comments, preflight, now=now)

    def test_grant_freeze_rejects_second_readback_edit_or_timestamp_change(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)
        base_body = self.grant_body(preflight, now)
        initial = {
            "id": 55,
            "body": base_body,
            "user": {"login": "laqieer"},
            "author_association": "OWNER",
            "created_at": "2026-10-02T00:00:00Z",
            "updated_at": "2026-10-02T00:00:00Z",
        }
        frozen = self.hosted.find_matching_grant([initial], preflight, now=now)
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.confirm_grant_freeze(
                initial | {"body": self.grant_body(
                    preflight,
                    now,
                    footer_lines=self.grant_footer_lines(cli_version="1.0.85", display_name="Auto", model_id="auto"),
                )},
                frozen,
                now=now,
            )
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.confirm_grant_freeze(
                initial | {"updated_at": "2026-10-02T00:00:01Z"},
                frozen,
                now=now,
            )

    def test_grant_freeze_rejects_malformed_frozen_timestamps_even_when_second_readback_matches(self):
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)
        preflight = {
            "grant_lookup_key": "grant-key",
            "candidate_sha": "a" * 40,
            "tool_constraints_digest": "fixed",
            "receipt_stem": "receipt-stem",
            "preflight_digest": "digest",
            "run_binding": {
                "run_id": "12345",
                "run_attempt": 1,
                "caller_workflow_path": ".github/workflows/e2e-norom.yml",
                "caller_workflow_sha": "a" * 40,
                "hosted_workflow_path": ".github/workflows/linux-x11-hosted.yml",
                "hosted_workflow_sha": "a" * 40,
            },
        }
        body = self.grant_body(preflight, now)
        grant = self.hosted._parse_grant_body(body)
        frozen = {
            "grant": grant,
            "grant_freeze": {
                "comment_id": 77,
                "normalized_body_sha256": self.hosted._grant_body_sha256(body),
                "author_login": "laqieer",
                "author_association": "OWNER",
                "created_at": "not-a-timestamp",
                "updated_at": "not-a-timestamp",
            },
            "preflight_digest": "digest",
        }
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.confirm_grant_freeze(
                {
                    "id": 77,
                    "body": body,
                    "user": {"login": "laqieer"},
                    "author_association": "OWNER",
                    "created_at": "not-a-timestamp",
                    "updated_at": "not-a-timestamp",
                },
                frozen,
                now=now,
            )

    def test_find_matching_grant_rejects_naive_and_date_only_timestamps(self):
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)
        preflight = {
            "grant_lookup_key": "grant-key",
            "candidate_sha": "a" * 40,
            "preflight_digest": "digest",
            "tool_constraints_digest": "fixed",
            "receipt_stem": "receipt-stem",
            "run_binding": {
                "run_id": "12345",
                "run_attempt": 1,
                "caller_workflow_path": ".github/workflows/e2e-norom.yml",
                "caller_workflow_sha": "a" * 40,
                "hosted_workflow_path": ".github/workflows/linux-x11-hosted.yml",
                "hosted_workflow_sha": "a" * 40,
            },
        }
        body = self.grant_body(preflight, now)
        for created_at, updated_at in (
                ("2026-10-02T00:00:00", "2026-10-02T00:00:01Z"),
                ("2026-10-02", "2026-10-02T00:00:01Z"),
                ("2026-10-02T08:00:00+08:00", "2026-10-02T08:00:01+08:00")):
            comment = {
                "id": 77,
                "body": body,
                "user": {"login": "laqieer"},
                "author_association": "OWNER",
                "created_at": created_at,
                "updated_at": updated_at,
            }
            with self.subTest(created_at=created_at, updated_at=updated_at):
                if created_at.endswith("+08:00"):
                    match = self.hosted.find_matching_grant([comment], preflight, now=now)
                    self.assertEqual(77, match["id"])
                else:
                    with self.assertRaises(self.hosted.HostedWorkflowError):
                        self.hosted.find_matching_grant([comment], preflight, now=now)

    def test_grant_parser_accepts_complete_footered_comment_and_freezes_full_body(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)

        def comment(body):
            return {
                "id": 91,
                "body": body,
                "user": {"login": "laqieer"},
                "author_association": "OWNER",
                "created_at": "2026-10-02T00:00:00Z",
                "updated_at": "2026-10-02T00:00:00Z",
            }

        for footer_lines in self.actual_grant_footer_fixtures():
            for line_ending in ("\n", "\r\n"):
                for final_newline in (False, True):
                    with self.subTest(footer_lines=footer_lines, line_ending=repr(line_ending), final_newline=final_newline):
                        body = self.grant_body(
                            preflight,
                            now,
                            footer_lines=footer_lines,
                            line_ending=line_ending,
                            final_newline=final_newline,
                        )
                        self.assertEqual(self.grant_fields(preflight, now), self.hosted._parse_grant_body(body))
                        match = self.hosted.find_matching_grant([comment(body)], preflight, now=now)
                        self.assertEqual(
                            self.hosted._grant_body_sha256(self.grant_body(preflight, now, footer_lines=footer_lines)),
                            match["grant_freeze"]["normalized_body_sha256"],
                        )
                        confirmed = self.hosted.confirm_grant_freeze(
                            comment(body),
                            match | {"preflight_digest": preflight["preflight_digest"]},
                            now=now,
                        )
                        self.assertEqual(self.hosted.OPERATION, confirmed["schema"])

    def test_grant_parser_rejects_footer_grammar_and_placement_violations(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        now = datetime(2026, 10, 2, tzinfo=timezone.utc)
        body = self.grant_body(preflight, now)
        lines = body.split("\n")

        def replacement_line(index, value):
            changed = list(lines)
            changed[index] = value
            return "\n".join(changed)

        cases = {
            "missing-footer": "\n".join(lines[:-2]),
            "partial-footer": "\n".join(lines[:-1]),
            "double-final-newline": body + "\n\n",
            "footer-leading-space": replacement_line(20, " Copilot CLI: 1.0.92-3"),
            "footer-trailing-space": replacement_line(21, "Model: GPT-5.4 (gpt-5.4) "),
            "version-component-too-long": replacement_line(20, "Copilot CLI: 1234567.0.0"),
            "version-suffix-too-long": replacement_line(20, "Copilot CLI: 1.0.0-" + ("a" * 32)),
            "version-total-too-long": replacement_line(20, "Copilot CLI: 123456.123456.123456-" + ("a" * 32)),
            "display-too-long": replacement_line(21, "Model: " + ("A" * 81) + " (gpt-5.4)"),
            "display-nonalnum-boundary": replacement_line(21, "Model: .Auto (auto)"),
            "display-non-ascii": replacement_line(21, "Model: Áuto (auto)"),
            "model-id-too-long": replacement_line(21, "Model: GPT-5.4 (" + ("a" * 81) + ")"),
            "model-id-upper-case": replacement_line(21, "Model: GPT-5.4 (GPT-5.4)"),
            "model-id-boundary": replacement_line(21, "Model: GPT-5.4 (.gpt-5.4)"),
            "quoted-footer": replacement_line(20, "> Copilot CLI: 1.0.92-3"),
            "fenced-footer": replacement_line(20, "```"),
            "literal-backtick-n": replacement_line(20, "Copilot CLI: 1.0.92-3`nModel: GPT-5.4 (gpt-5.4)"),
            "extra-blank-separator": "\n".join(lines[:20] + [""] + lines[20:]),
            "field-after-footer": "\n".join(lines + ["lookup_key=shadow"]),
            "footer-before-separator": "\n".join(lines[:19] + [lines[20], "", lines[21]]),
            "tab": replacement_line(20, "Copilot CLI:\t1.0.92-3"),
            "control": replacement_line(20, "Copilot CLI: 1.0.92-3\x1f"),
            "lone-cr": body.replace("\n", "\r", 1),
            "unicode-line-separator": body.replace("\n", "\u2028", 1),
            "unicode-paragraph-separator": body.replace("\n", "\u2029", 1),
        }
        for label, broken in cases.items():
            with self.subTest(label=label):
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted._parse_grant_body(broken)

    def test_stage_artifacts_rejects_untrusted_receipt_stem_before_filesystem_access(self):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-stage-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        output_dir = root / "bundle"
        preflight_path = root / "preflight.json"
        grant_path = root / "grant.json"
        preflight_path.write_text(json.dumps({"preflight_digest": "digest"}), encoding="utf-8")
        grant_path.write_text(json.dumps({"grant": {"receipt_stem": "receipt-stem"}}), encoding="utf-8")
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.stage_artifacts(preflight_path, grant_path, "bad;stem", output_dir)
        self.assertFalse(output_dir.exists())

    def test_summarize_receipt_cli_accepts_bounded_worst_case_raw_receipt_larger_than_default_json_limit(self):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-summary-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        raw_path = root / "receipt.json"
        summary_path = root / "summary.json"
        preflight_path = root / "preflight.json"
        receipt = self.pass_receipt()
        io_limits = self.hosted.native_smoke.IO_LIMITS
        six_byte = "\x00"
        receipt["worker_stderr"] = six_byte * self.hosted.native_smoke.WORKER_STDERR_LIMIT
        receipt["io"]["displayfd"]["text"] = six_byte * io_limits["displayfd"]
        receipt["io"]["xvfb_stderr"]["text"] = six_byte * io_limits["xvfb_stderr"]
        receipt["io"]["worker_stdout"]["text"] = six_byte * io_limits["worker_stdout"]
        receipt["io"]["worker_stderr"]["text"] = six_byte * io_limits["worker_stderr"]
        with raw_path.open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        preflight_path.write_text(json.dumps(self.valid_preflight()), encoding="utf-8")
        self.assertGreater(raw_path.stat().st_size, self.hosted.JSON_LIMIT)
        self.hosted.main([
            "summarize-receipt",
            "--preflight", str(preflight_path),
            "--receipt", str(raw_path),
            "--output", str(summary_path),
        ])
        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        self.assertEqual("passed", summary["status"])
        self.assertLessEqual(summary_path.stat().st_size, self.hosted.JSON_LIMIT)

    def test_summarize_native_receipt_for_upload_preserves_failed_outer_status_and_passed_worker_diagnostic(self):
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        summary = self.hosted.summarize_native_receipt_for_upload(receipt, self.preflight_for_receipt(receipt))
        self.assertEqual("failed", summary["status"])
        self.assertEqual("passed", summary["diagnostic"]["status"])
        self.assertEqual(receipt["diagnostic"]["owned_destroyed"], summary["diagnostic"]["owned_destroyed"])
        self.assertEqual(receipt["diagnostic"]["libx11"], summary["diagnostic"]["libx11"])
        self.assertIn("xvfb_kill", summary["cleanup_failures"])
        self.assertEqual(receipt["xvfb_exit_code"], summary["xvfb_exit_code"])
        self.assertNotIn("worker_stderr", summary)
        self.assertEqual(
            {key: receipt["worker"][key] for key in ("pid", "start_ticks")},
            summary["worker"],
        )
        self.assertEqual(receipt["supervisor"], summary["supervisor"])
        self.assertEqual(self.safe_xvfb_command(receipt["xvfb"]["command"][0]), summary["xvfb"]["command"])
        self.assertNotIn("text", summary["io"]["worker_stdout"])
        self.assertNotIn("text", summary["io"]["worker_stderr"])

    def test_summary_and_stage_project_only_known_safe_xvfb_arguments(self):
        receipt = self.pass_receipt()
        receipt["supervisor"] = receipt["supervisor"] | {"command": ["/bin/secret-supervisor"]}
        summary = self.hosted.summarize_native_receipt_for_upload(receipt, self.valid_preflight())
        self.assertEqual(self.safe_xvfb_command(), summary["xvfb"]["command"])
        self.assertEqual({key: receipt["worker"][key] for key in ("pid", "start_ticks")}, summary["worker"])
        self.assertNotIn("command", summary["worker"])
        self.assertNotIn("command", summary["supervisor"])
        self.assertNotIn("/tmp/linux-x11-smoke/authority", json.dumps(summary))
        staged = self.assert_stage_artifacts_summary(receipt)
        self.assertEqual(self.safe_xvfb_command(), staged["xvfb"]["command"])
        self.assertNotIn("command", staged["worker"])
        self.assertNotIn("command", staged["supervisor"])
        staged_text = json.dumps(staged)
        self.assertNotIn("/tmp/linux-x11-smoke/authority", staged_text)

    def test_passed_public_paths_require_admitted_sources_worker_and_libx11_bindings(self):
        preflight = self.valid_preflight()
        cases = (
            ("supervisor source hash", lambda receipt: receipt["sources"].__setitem__("linux_x11.py", "0" * 64)),
            ("worker source hash", lambda receipt: receipt["diagnostic"]["sources"].__setitem__("linux_x11.py", "0" * 64)),
            ("libx11 path", lambda receipt: receipt["diagnostic"]["libx11"].__setitem__("path", "/other/libX11.so.6")),
            ("libx11 sha256", lambda receipt: receipt["diagnostic"]["libx11"].__setitem__("sha256", "0" * 64)),
            ("worker command executable", lambda receipt: receipt["worker"]["command"].__setitem__(0, "/tmp/python3")),
            ("worker command xlib path", lambda receipt: receipt["worker"]["command"].__setitem__(-1, "/other/libX11.so.6")),
            ("worker command extra arg", lambda receipt: receipt["worker"]["command"].append("--token=secret-worker")),
            ("worker pid mismatch", lambda receipt: receipt["diagnostic"]["worker"].__setitem__("pid", 999)),
            ("worker start_ticks mismatch", lambda receipt: receipt["diagnostic"]["worker"].__setitem__("start_ticks", 999)),
        )
        for label, mutate in cases:
            with self.subTest(label=label):
                broken = self.pass_receipt()
                mutate(broken)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(broken, preflight)
                self.assert_stage_artifacts_rejects_without_publishing_summary(
                    broken,
                    canary_text="secret-worker",
                    preflight=preflight,
                )
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(broken, preflight)

    def test_summarize_native_receipt_for_upload_allows_missing_setup_supervisor_and_partial_failure_io(self):
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.identity_mock, "side_effect", OSError("injected identity failure")),
        )
        summary = self.hosted.summarize_native_receipt_for_upload(receipt, self.valid_preflight())
        self.assertEqual("failed", summary["status"])
        self.assertNotIn("supervisor", summary)
        self.assertEqual({}, summary["io"])
        self.assertNotIn("diagnostic", summary)
        self.assertIn("identity failure", summary["failure"])

        readiness = self.actual_smoke_receipt(prepare_kwargs={"events": {10: [b""], 12: [b"known EOF diagnostic", b""]}})
        summary = self.hosted.summarize_native_receipt_for_upload(readiness)
        self.assertEqual({"displayfd", "xvfb_stderr"}, set(summary["io"]))
        self.assertNotIn("worker_stdout", summary["io"])
        self.assertNotIn("worker_stderr", summary["io"])
        self.assertEqual("known EOF diagnostic", readiness["io"]["xvfb_stderr"]["text"])
        self.assertNotIn("text", summary["io"]["xvfb_stderr"])

    def test_stage_artifacts_cli_summarizes_real_failed_receipt_without_upgrading_status(self):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-stage-cli-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        old_cwd = Path.cwd()
        os.chdir(root)
        self.addCleanup(lambda: os.chdir(old_cwd))
        bundle = root / "upload"
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        preflight = self.preflight_for_receipt(receipt) | {"receipt_stem": "receipt-stem"}
        grant = {"grant": {"receipt_stem": "receipt-stem"}}
        preflight_path = root / "preflight.json"
        grant_path = root / "grant.json"
        preflight_path.write_text(json.dumps(preflight), encoding="utf-8")
        grant_path.write_text(json.dumps(grant), encoding="utf-8")
        receipt_dir = root / "linux-x11-smoke-receipt-stem"
        receipt_dir.mkdir()
        with (receipt_dir / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        self.hosted.main([
            "stage-artifacts",
            "--preflight", str(preflight_path),
            "--grant", str(grant_path),
            "--output-dir", str(bundle),
        ])
        summary = json.loads((bundle / "receipt-summary.json").read_text(encoding="utf-8"))
        self.assertEqual("failed", summary["status"])
        self.assertEqual("passed", summary["diagnostic"]["status"])
        self.assertIn("xvfb_kill", summary["cleanup_failures"])
        self.assertLessEqual((bundle / "receipt-summary.json").stat().st_size, self.hosted.JSON_LIMIT)

    def test_summarize_native_receipt_for_upload_strips_recursive_pass_diagnostic_canaries(self):
        receipt = self.pass_receipt()
        receipt["diagnostic"] = self.permissive_pass_report_with_canaries(receipt["diagnostic"])
        summary = self.hosted.summarize_native_receipt_for_upload(receipt, self.valid_preflight())
        self.assertEqual(self.pass_report(), summary["diagnostic"])
        self.assert_recursive_canary_absent(summary["diagnostic"])

    def test_validate_native_receipt_public_output_strips_recursive_pass_diagnostic_canaries(self):
        preflight = self.valid_preflight()
        receipt = self.pass_receipt()
        receipt["diagnostic"] = self.permissive_pass_report_with_canaries(receipt["diagnostic"])
        result = self.hosted.validate_native_receipt(receipt, preflight)
        self.assertEqual("digest", result["preflight_digest"])
        self.assertEqual(self.pass_report(), result["receipt"])
        self.assert_recursive_canary_absent(result["receipt"])

    def test_stage_artifacts_strips_recursive_pass_diagnostic_canaries(self):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-stage-pass-canary-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        old_cwd = Path.cwd()
        os.chdir(root)
        self.addCleanup(lambda: os.chdir(old_cwd))
        bundle = root / "upload"
        preflight = self.valid_preflight() | {"receipt_stem": "receipt-stem"}
        grant = {"grant": {"receipt_stem": "receipt-stem"}}
        preflight_path = root / "preflight.json"
        grant_path = root / "grant.json"
        preflight_path.write_text(json.dumps(preflight), encoding="utf-8")
        grant_path.write_text(json.dumps(grant), encoding="utf-8")
        receipt_dir = root / "linux-x11-smoke-receipt-stem"
        receipt_dir.mkdir()
        receipt = self.pass_receipt()
        receipt["diagnostic"] = self.permissive_pass_report_with_canaries(receipt["diagnostic"])
        with (receipt_dir / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        self.hosted.main([
            "stage-artifacts",
            "--preflight", str(preflight_path),
            "--grant", str(grant_path),
            "--output-dir", str(bundle),
        ])
        summary = json.loads((bundle / "receipt-summary.json").read_text(encoding="utf-8"))
        self.assertEqual(self.pass_report(), summary["diagnostic"])
        self.assert_recursive_canary_absent(summary["diagnostic"])

    def test_summary_and_stage_reject_unknown_or_malformed_xvfb_commands(self):
        failed_outer = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        preflight = self.valid_preflight()
        pass_cases = (
            ("unknown flag", self.pass_receipt(), ["/usr/bin/Xvfb", ":7", "--token=secret"]),
            ("wrong executable", self.pass_receipt(), ["/tmp/Xvfb", ":7"]),
            ("missing displayfd value", self.pass_receipt(), ["/usr/bin/Xvfb", "-displayfd"]),
            ("nonnumeric displayfd", self.pass_receipt(), ["/usr/bin/Xvfb", "-displayfd", "pipe"]),
            ("wrong screen tuple", self.pass_receipt(), ["/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "bad"]),
            ("wrong nolisten", self.pass_receipt(), ["/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "udp", "-auth", "/tmp/auth", "-noreset"]),
            ("missing auth value", self.pass_receipt(), ["/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "tcp", "-auth"]),
            ("extra trailing arg", self.pass_receipt(), ["/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "tcp", "-auth", "/tmp/auth", "-noreset", "--token=secret"]),
            ("failed outer unknown flag", failed_outer, ["/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "tcp", "-auth", "/tmp/auth", "-noreset", "--token=secret"]),
        )
        for label, receipt, command in pass_cases:
            with self.subTest(label=label):
                broken = self.mutate_path(receipt, ("xvfb", "command"), command)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(broken, preflight)
                self.assert_stage_artifacts_rejects_without_publishing_summary(
                    broken,
                    canary_text="secret",
                )

    def test_validate_native_receipt_rejects_unknown_or_malformed_commands(self):
        preflight = self.valid_preflight()
        cases = (
            ("xvfb token", ("xvfb", "command"), ["/usr/bin/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "tcp", "-auth", "/tmp/auth", "-noreset", "--token=secret"]),
            ("xvfb wrong exe", ("xvfb", "command"), ["/tmp/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "tcp", "-auth", "/tmp/auth", "-noreset"]),
            ("worker extra arg", ("worker", "command"), self.pass_receipt()["worker"]["command"] + ["--token=secret-worker"]),
            ("worker wrong exe", ("worker", "command"), ["/tmp/python3"] + self.pass_receipt()["worker"]["command"][1:]),
            ("worker missing xlib flag", ("worker", "command"), self.pass_receipt()["worker"]["command"][:-2]),
        )
        for label, path, replacement in cases:
            with self.subTest(label=label):
                broken = self.mutate_path(self.pass_receipt(), path, replacement)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(broken, preflight)

    def test_unbound_failed_summary_strips_observed_xvfb_path_without_admission(self):
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        receipt["xvfb"]["command"][0] = "/secret-canary/Xvfb"
        summary = self.hosted.summarize_native_receipt_for_upload(receipt)
        self.assertEqual(self.safe_xvfb_command("Xvfb"), summary["xvfb"]["command"])
        self.assertNotIn("/secret-canary/Xvfb", json.dumps(summary))

    def test_passed_summary_and_cli_require_preflight_bound_xvfb_path(self):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-summary-preflight-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        raw_path = root / "receipt.json"
        summary_path = root / "summary.json"
        receipt = self.pass_receipt()
        with raw_path.open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(receipt)
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.main([
                "summarize-receipt",
                "--receipt", str(raw_path),
                "--output", str(summary_path),
            ])
        self.assertFalse(summary_path.exists())

    def test_summary_stage_and_validation_reject_path_mismatched_passed_xvfb_command(self):
        preflight = self.valid_preflight()
        broken = self.mutate_path(
            self.pass_receipt(),
            ("xvfb", "command"),
            ["/secret-canary/Xvfb", "-displayfd", "11", "-screen", "0", "320x240x24", "-nolisten", "tcp", "-auth", "/tmp/auth", "-noreset"],
        )
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken, preflight)
        self.assert_stage_artifacts_rejects_without_publishing_summary(
            broken,
            canary_text="/secret-canary/Xvfb",
            preflight=preflight,
        )
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(broken, preflight)

    def test_passed_summary_stage_and_validation_require_owned_worker_and_xvfb_identities(self):
        preflight = self.valid_preflight()
        cases = (
            ("missing worker", ("worker",), None),
            ("missing xvfb", ("xvfb",), None),
        )
        for label, path, replacement in cases:
            with self.subTest(label=label):
                broken = self.mutate_path(self.pass_receipt(), path, replacement)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(broken, preflight)
                self.assert_stage_artifacts_rejects_without_publishing_summary(broken)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(broken, preflight)

    def test_failed_outer_summary_strips_recursive_passed_worker_diagnostic_canaries(self):
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        receipt["diagnostic"] = self.permissive_pass_report_with_canaries(receipt["diagnostic"])
        summary = self.hosted.summarize_native_receipt_for_upload(receipt, self.preflight_for_receipt(receipt))
        self.assertEqual("failed", summary["status"])
        self.assertEqual("passed", summary["diagnostic"]["status"])
        self.assert_recursive_canary_absent(summary["diagnostic"])

    def test_passed_diagnostic_exact_nested_canaries_are_rejected(self):
        preflight = self.valid_preflight()
        for path in (
                ("owned_destroyed", self.CANARY_KEY),
                ("badwindow_request", self.CANARY_KEY),
                ("badwindow_events", 0, self.CANARY_KEY),
                ("unrelated_rejected", 0, self.CANARY_KEY)):
            with self.subTest(path=path):
                receipt = self.pass_receipt()
                target = receipt["diagnostic"]
                for segment in path[:-1]:
                    target = target[segment]
                target[path[-1]] = "secret:nested"
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(receipt, preflight)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(receipt, preflight)

    def test_summarize_native_receipt_for_upload_accepts_cleanup_failure_subset_at_limit(self):
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("k" * 1000)),
        )
        summary = self.hosted.summarize_native_receipt_for_upload(receipt)
        self.assertEqual("k" * 1000, summary["cleanup_failures"]["xvfb_kill"])

    def test_summarize_native_receipt_for_upload_rejects_unknown_or_oversized_cleanup_failure_entries(self):
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("known failure")),
        )
        broken = json.loads(json.dumps(receipt))
        broken["cleanup_failures"]["authority_cookie"] = "should never publish"
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken)
        broken = json.loads(json.dumps(receipt))
        broken["cleanup_failures"]["xvfb_kill"] = "k" * 1001
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken)
        broken = json.loads(json.dumps(receipt))
        broken["cleanup_failures"]["xvfb_kill"] = 7
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken)

    def test_stage_artifacts_rejects_unknown_cleanup_failure_key_without_publishing_summary(self):
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-stage-cleanup-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        old_cwd = Path.cwd()
        os.chdir(root)
        self.addCleanup(lambda: os.chdir(old_cwd))
        bundle = root / "upload"
        preflight = self.valid_preflight() | {"receipt_stem": "receipt-stem"}
        grant = {"grant": {"receipt_stem": "receipt-stem"}}
        preflight_path = root / "preflight.json"
        grant_path = root / "grant.json"
        preflight_path.write_text(json.dumps(preflight), encoding="utf-8")
        grant_path.write_text(json.dumps(grant), encoding="utf-8")
        receipt_dir = root / "linux-x11-smoke-receipt-stem"
        receipt_dir.mkdir()
        receipt = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("known failure")),
        )
        receipt["cleanup_failures"]["authority_cookie"] = "secret canary"
        with (receipt_dir / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.main([
                "stage-artifacts",
                "--preflight", str(preflight_path),
                "--grant", str(grant_path),
                "--output-dir", str(bundle),
            ])
        summary_path = bundle / "receipt-summary.json"
        self.assertFalse(summary_path.exists())
        if bundle.exists():
            bundle_text = "".join(
                path.read_text(encoding="utf-8", errors="ignore")
                for path in bundle.glob("*.json")
            )
            self.assertNotIn("secret canary", bundle_text)

    def test_summarize_native_receipt_for_upload_rejects_unknown_or_malformed_failure_stream_shapes(self):
        preflight = self.valid_preflight()
        receipt = self.actual_smoke_receipt()
        broken = json.loads(json.dumps(receipt))
        broken["cleanup_failures"] = ["worker_wait"]
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken, preflight)
        broken = json.loads(json.dumps(receipt))
        broken["sources"]["authority_cookie"] = "0" * 64
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken, preflight)
        broken = json.loads(json.dumps(receipt))
        del broken["io"]["worker_stdout"]["text"]
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken, preflight)
        broken = json.loads(json.dumps(receipt))
        broken["io"]["mystery"] = {"text": "oops"}
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken, preflight)

    def test_summarize_and_stage_artifacts_reject_malformed_outer_scalar_slots(self):
        preflight = self.valid_preflight()
        cleanup_failure = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("known cleanup failure")),
        )
        deadline_failure = self.actual_smoke_receipt(
            mutate=lambda case: setattr(
                case.server,
                "wait",
                lambda timeout, original=case.server.wait: (setattr(case.pipes, "now", 20.1), original(timeout))[1],
            ),
        )
        cases = (
            ("status dict", self.pass_receipt(), ("status",), {self.CANARY_KEY: "secret:status"}, self.CANARY_KEY),
            ("status list", self.pass_receipt(), ("status",), ["passed"], self.CANARY_KEY),
            ("status wrong", self.pass_receipt(), ("status",), "unexpected", "unexpected"),
            ("timeout dict", self.pass_receipt(), ("timeout_seconds",), {self.CANARY_KEY: "secret:timeout"}, self.CANARY_KEY),
            ("timeout bool", self.pass_receipt(), ("timeout_seconds",), True, None),
            ("timeout low", self.pass_receipt(), ("timeout_seconds",), 9, None),
            ("phase dict", self.pass_receipt(), ("phase",), {self.CANARY_KEY: "secret:phase"}, self.CANARY_KEY),
            ("phase wrong", self.pass_receipt(), ("phase",), "cleanup", "cleanup"),
            ("started dict", self.pass_receipt(), ("started_utc",), {self.CANARY_KEY: "secret:started"}, self.CANARY_KEY),
            ("started naive", self.pass_receipt(), ("started_utc",), "2026-10-02T00:00:00", "2026-10-02T00:00:00"),
            ("elapsed dict", self.pass_receipt(), ("elapsed_seconds",), {self.CANARY_KEY: "secret:elapsed"}, self.CANARY_KEY),
            ("elapsed bool", self.pass_receipt(), ("elapsed_seconds",), False, None),
            ("elapsed inf", self.pass_receipt(), ("elapsed_seconds",), float("inf"), "Infinity"),
            ("elapsed nan", self.pass_receipt(), ("elapsed_seconds",), float("nan"), "NaN"),
            ("failure dict", cleanup_failure, ("failure",), {self.CANARY_KEY: "secret:failure"}, self.CANARY_KEY),
            ("failure list", cleanup_failure, ("failure",), ["known cleanup failure"], self.CANARY_KEY),
            ("failure long", cleanup_failure, ("failure",), "f" * 1001, "f" * 1001),
            ("capture dict", cleanup_failure | {"capture_failure": "known capture failure"}, ("capture_failure",), {self.CANARY_KEY: "secret:capture"}, self.CANARY_KEY),
            ("capture long", cleanup_failure | {"capture_failure": "c" * 1001}, ("capture_failure",), "c" * 1001, "c" * 1001),
            ("deadline dict", deadline_failure, ("deadline_exceeded",), {self.CANARY_KEY: "secret:deadline"}, self.CANARY_KEY),
            ("deadline int", deadline_failure, ("deadline_exceeded",), 1, None),
            ("worker exit dict", cleanup_failure, ("worker_exit_code",), {self.CANARY_KEY: "secret:worker-exit"}, self.CANARY_KEY),
            ("worker exit list", cleanup_failure, ("worker_exit_code",), [0], self.CANARY_KEY),
            ("xvfb observed dict", cleanup_failure, ("xvfb_observed_exit_code",), {self.CANARY_KEY: "secret:xvfb-observed"}, self.CANARY_KEY),
            ("xvfb observed list", cleanup_failure, ("xvfb_observed_exit_code",), [None], self.CANARY_KEY),
            ("xvfb exit dict", cleanup_failure, ("xvfb_exit_code",), {self.CANARY_KEY: "secret:xvfb-exit"}, self.CANARY_KEY),
            ("xvfb exit list", cleanup_failure, ("xvfb_exit_code",), [1], self.CANARY_KEY),
        )
        for label, receipt, path, replacement, canary_text in cases:
            with self.subTest(label=label):
                broken = self.mutate_path(receipt, path, replacement)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(broken, preflight if broken.get("status") == "passed" else None)
                self.assert_stage_artifacts_rejects_without_publishing_summary(
                    broken,
                    canary_text=canary_text,
                )

    def test_validate_native_receipt_rejects_malformed_outer_scalar_slots(self):
        preflight = self.valid_preflight()
        cases = (
            ("status dict", ("status",), {self.CANARY_KEY: "secret:status"}),
            ("timeout dict", ("timeout_seconds",), {self.CANARY_KEY: "secret:timeout"}),
            ("timeout bool", ("timeout_seconds",), True),
            ("phase dict", ("phase",), {self.CANARY_KEY: "secret:phase"}),
            ("started dict", ("started_utc",), {self.CANARY_KEY: "secret:started"}),
            ("started naive", ("started_utc",), "2026-10-02T00:00:00"),
            ("elapsed dict", ("elapsed_seconds",), {self.CANARY_KEY: "secret:elapsed"}),
            ("elapsed list", ("elapsed_seconds",), [1.2]),
            ("elapsed inf", ("elapsed_seconds",), float("inf")),
            ("elapsed nan", ("elapsed_seconds",), float("nan")),
            ("failure dict", ("failure",), {self.CANARY_KEY: "secret:failure"}),
            ("capture dict", ("capture_failure",), {self.CANARY_KEY: "secret:capture"}),
            ("deadline dict", ("deadline_exceeded",), {self.CANARY_KEY: "secret:deadline"}),
            ("worker exit dict", ("worker_exit_code",), {self.CANARY_KEY: "secret:worker-exit"}),
            ("xvfb observed dict", ("xvfb_observed_exit_code",), {self.CANARY_KEY: "secret:xvfb-observed"}),
            ("xvfb exit dict", ("xvfb_exit_code",), {self.CANARY_KEY: "secret:xvfb-exit"}),
        )
        for label, path, replacement in cases:
            with self.subTest(label=label):
                broken = self.mutate_path(self.pass_receipt(), path, replacement)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(broken, preflight)

    def test_summarize_native_receipt_for_upload_accepts_actual_failure_phase_scalar_variants(self):
        identity_failure = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.identity_mock, "side_effect", OSError("injected identity failure")),
        )
        eof_failure = self.actual_smoke_receipt(
            prepare_kwargs={"events": {10: [b""], 12: [b"known EOF diagnostic", b""]}},
        )
        cleanup_failure = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        deadline_failure = self.actual_smoke_receipt(
            mutate=lambda case: setattr(
                case.server,
                "wait",
                lambda timeout, original=case.server.wait: (setattr(case.pipes, "now", 20.1), original(timeout))[1],
            ),
        )
        cases = (
            ("setup-identity", identity_failure, {"started_utc"}, {"supervisor", "sources", "diagnostic"}),
            ("readiness-eof", eof_failure, {"started_utc", "sources", "xvfb_observed_exit_code", "xvfb_exit_code"}, set()),
            ("validation-cleanup", cleanup_failure, {"started_utc", "sources", "worker_exit_code", "xvfb_observed_exit_code", "xvfb_exit_code", "diagnostic"}, set()),
            ("deadline", deadline_failure, {"started_utc", "sources", "worker_exit_code", "xvfb_observed_exit_code", "xvfb_exit_code", "diagnostic", "deadline_exceeded"}, set()),
        )
        for label, receipt, expected_present, expected_absent in cases:
            with self.subTest(label=label):
                summary = self.hosted.summarize_native_receipt_for_upload(receipt)
                self.assertEqual("failed", summary["status"])
                self.assertEqual(receipt["timeout_seconds"], summary["timeout_seconds"])
                self.assertEqual(receipt["phase"], summary["phase"])
                self.assertEqual(receipt["elapsed_seconds"], summary["elapsed_seconds"])
                for key in expected_present:
                    self.assertIn(key, summary)
                for key in expected_absent:
                    self.assertNotIn(key, summary)
                if "started_utc" in expected_present:
                    self.assertEqual(receipt["started_utc"], summary["started_utc"])
                if "sources" in expected_present:
                    self.assertEqual(receipt["sources"], summary["sources"])
                if "worker_exit_code" in expected_present:
                    self.assertEqual(receipt["worker_exit_code"], summary["worker_exit_code"])
                if "xvfb_observed_exit_code" in expected_present:
                    self.assertEqual(receipt["xvfb_observed_exit_code"], summary["xvfb_observed_exit_code"])
                if "xvfb_exit_code" in expected_present:
                    self.assertEqual(receipt["xvfb_exit_code"], summary["xvfb_exit_code"])
                if "deadline_exceeded" in expected_present:
                    self.assertTrue(summary["deadline_exceeded"])
                if "diagnostic" in expected_present:
                    self.assertEqual(receipt["diagnostic"]["status"], summary["diagnostic"]["status"])

    def test_actual_producer_stream_shapes_survive_summary_stage_and_validation(self):
        passed = self.actual_smoke_receipt()
        preflight = self.preflight_for_receipt(passed)
        for basename, relative in self.hosted.NATIVE_SOURCE_PATHS.items():
            preflight["source_hashes"][relative] = passed["sources"][basename]
        preflight["tools"]["python3"]["path"] = passed["worker"]["command"][0]
        preflight["tools"]["libx11"] = dict(passed["diagnostic"]["libx11"])
        summary = self.hosted.summarize_native_receipt_for_upload(passed, preflight)
        self.assertEqual("passed", summary["status"])
        self.assertTrue(summary["io"]["worker_stdout"]["eof"])
        self.assertTrue(summary["io"]["worker_stderr"]["eof"])
        self.assertFalse(summary["io"]["xvfb_stderr"]["eof"])
        staged = self.assert_stage_artifacts_summary(passed, preflight=preflight)
        self.assertEqual(summary, staged)
        self.assertEqual("digest", self.hosted.validate_native_receipt(passed, preflight)["preflight_digest"])

        failed = self.actual_smoke_receipt(
            prepare_kwargs={"events": {10: [b""], 12: [b"known EOF diagnostic", b""]}},
        )
        failed_summary = self.hosted.summarize_native_receipt_for_upload(failed)
        self.assertEqual("failed", failed_summary["status"])
        self.assertEqual({"displayfd", "xvfb_stderr"}, set(failed_summary["io"]))
        self.assertTrue(failed_summary["io"]["xvfb_stderr"]["eof"])
        self.assert_stage_artifacts_summary(failed)

    def test_public_paths_reject_non_producer_stream_shapes(self):
        preflight = self.valid_preflight()
        pass_cases = []
        for stream_name, limit in self.hosted.native_smoke.IO_LIMITS.items():
            pass_cases.extend((
                (f"{stream_name} wrong limit", ("io", stream_name), {"limit_bytes": limit - 1}),
                (f"{stream_name} count mismatch", ("io", stream_name), {"observed_bytes": 999, "retained_bytes": 998}),
                (f"{stream_name} truncated", ("io", stream_name), {"truncated": True, "observed_bytes": limit + 1, "retained_bytes": limit}),
                (f"{stream_name} error", ("io", stream_name), {"error": "e"}),
                (f"{stream_name} oversized error", ("io", stream_name), {"error": "e" * 1001}),
            ))
        pass_cases.extend((
            ("worker_stdout eof missing", ("io", "worker_stdout"), {"eof": False}),
            ("worker_stderr eof missing", ("io", "worker_stderr"), {"eof": False}),
        ))
        for label, path, updates in pass_cases:
            with self.subTest(label=label):
                broken = self.pass_receipt()
                target = broken
                for segment in path:
                    target = target[segment]
                target.update(updates)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(broken, preflight)
                self.assert_stage_artifacts_rejects_without_publishing_summary(broken, preflight=preflight)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(broken, preflight)

        failed = self.actual_smoke_receipt(
            prepare_kwargs={"events": {10: [b""], 12: [b"known EOF diagnostic", b""]}},
        )
        failed["io"]["xvfb_stderr"]["error"] = "e" * 1001
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(failed)
        self.assert_stage_artifacts_rejects_without_publishing_summary(failed)

    def test_failed_stream_error_cap_accepts_1000_and_rejects_1001(self):
        cleanup_failure = self.actual_smoke_receipt(
            mutate=lambda case: setattr(case.server, "kill_error", OSError("injected owned kill failure")),
        )
        preflight = self.preflight_for_receipt(cleanup_failure)
        for stream_name in self.hosted.native_smoke.IO_LIMITS:
            with self.subTest(stream_name=stream_name, error_length=1000):
                accepted = json.loads(json.dumps(cleanup_failure))
                accepted["io"][stream_name]["error"] = "e" * 1000
                accepted["io"][stream_name]["eof"] = False
                summary = self.hosted.summarize_native_receipt_for_upload(accepted, preflight)
                self.assertEqual("e" * 1000, summary["io"][stream_name]["error"])
                staged = self.assert_stage_artifacts_summary(accepted, preflight=preflight)
                self.assertEqual("e" * 1000, staged["io"][stream_name]["error"])
            with self.subTest(stream_name=stream_name, error_length=1001):
                rejected = json.loads(json.dumps(cleanup_failure))
                rejected["io"][stream_name]["error"] = "e" * 1001
                rejected["io"][stream_name]["eof"] = False
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(rejected, preflight)
                self.assert_stage_artifacts_rejects_without_publishing_summary(
                    rejected,
                    preflight=preflight,
                )

    def test_passed_public_paths_require_complete_pass_evidence(self):
        preflight = self.valid_preflight()
        cases = (
            ("missing diagnostic", lambda receipt: receipt.pop("diagnostic")),
            ("cleanup failures", lambda receipt: receipt.__setitem__("cleanup_failures", {"xvfb_kill": "known failure"})),
            ("capture failure", lambda receipt: receipt.__setitem__("capture_failure", "capture failed")),
            ("failure text", lambda receipt: receipt.__setitem__("failure", "setup failed")),
            ("deadline exceeded", lambda receipt: receipt.__setitem__("deadline_exceeded", True)),
        )
        for label, mutate in cases:
            with self.subTest(label=label):
                broken = self.pass_receipt()
                mutate(broken)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.summarize_native_receipt_for_upload(broken, preflight)
                self.assert_stage_artifacts_rejects_without_publishing_summary(broken, preflight=preflight)
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.validate_native_receipt(broken, preflight)

    def test_validate_native_receipt_rejects_incomplete_positive_evidence(self):
        preflight = self.valid_preflight()
        receipt = self.pass_receipt()
        self.assertEqual(
            "digest",
            self.hosted.validate_native_receipt(receipt, preflight)["preflight_digest"],
        )
        broken = json.loads(json.dumps(receipt))
        del broken["diagnostic"]["missing_property_ok"]
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(broken, preflight)
        broken = json.loads(json.dumps(receipt))
        broken["diagnostic"]["libx11"]["sha256"] = "0" * 64
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(broken, preflight)

    def test_validate_native_receipt_accepts_native_two_file_source_shape_and_rejects_tampering(self):
        preflight = self.valid_preflight()
        receipt = self.pass_receipt()
        self.assertEqual("digest", self.hosted.validate_native_receipt(receipt, preflight)["preflight_digest"])
        tampered = json.loads(json.dumps(receipt))
        tampered["sources"]["linux_x11.py"] = "0" * 64
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(tampered, preflight)
        extra = json.loads(json.dumps(receipt))
        extra["sources"]["extra.py"] = "1" * 64
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(extra, preflight)
        wrong_command = json.loads(json.dumps(receipt))
        wrong_command["worker"]["command"][-1] = "/other/libX11.so.6"
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(wrong_command, preflight)


class HostedPackageRecordTests(unittest.TestCase):
    def test_package_record_uses_binary_package_metadata_for_arch_qualified_owner(self):
        hosted = load_module()
        temporary = tempfile.TemporaryDirectory(prefix="linux-x11-package-")
        self.addCleanup(temporary.cleanup)
        path = Path(temporary.name) / "libX11.so.6"
        path.write_text("lib", encoding="utf-8")

        seen = []

        def query(command, *, cwd=None, timeout=10):
            seen.append(tuple(command))
            mapping = {
                ("dpkg-query", "-S", str(path)): f"libx11-6:amd64: {path}",
                ("dpkg-query", "-W", "-f=${binary:Package}\t${Version}\t${Architecture}", "libx11-6:amd64"):
                    "libx11-6:amd64\t2:1.8.7-1build1\tamd64",
            }
            return mapping[tuple(command)]

        record = hosted._package_record(path, query)
        self.assertEqual("libx11-6:amd64", record["package"]["name"])
        self.assertEqual(
            [
                ("dpkg-query", "-S", str(path)),
                ("dpkg-query", "-W", "-f=${binary:Package}\t${Version}\t${Architecture}", "libx11-6:amd64"),
            ],
            seen,
        )
