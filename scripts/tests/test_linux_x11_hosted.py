# SPDX-License-Identifier: GPL-3.0-or-later
"""Pure hosted issue-2160 workflow contracts with no native execution."""

from __future__ import annotations

import importlib.util
from datetime import datetime, timedelta, timezone
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "scripts" / "linux_x11_hosted.py"
RUN_PAYLOAD_FIXTURE = {
    "head_repository": {"full_name": "laqieer/FEBuilderGBA"},
    "head_sha": "a" * 40,
    "run_attempt": 1,
    "path": ".github/workflows/e2e-norom.yml",
    "referenced_workflows": [{
        "path": "laqieer/FEBuilderGBA/.github/workflows/linux-x11-hosted.yml@" + ("a" * 40),
        "sha": "a" * 40,
        "ref": "refs/heads/fix/issue-2160-linux-x11-attribution",
    }],
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
                "/usr/bin/Xvfb", ":7", "-screen", "0", "320x240x24",
                "-nolisten", "tcp", "-auth", "/tmp/linux-x11-smoke/authority",
            ]},
            "xvfb_observed_exit_code": None,
            "xvfb_exit_code": 0,
            "io": {
                "displayfd": {"text": "7\n", "limit_bytes": 32, "observed_bytes": 2, "retained_bytes": 2, "eof": False, "truncated": False, "error": None},
                "xvfb_stderr": {"text": "", "limit_bytes": 4096, "observed_bytes": 0, "retained_bytes": 0, "eof": False, "truncated": False, "error": None},
                "worker_stdout": {"text": json.dumps(self.pass_report()), "limit_bytes": 16384, "observed_bytes": 32, "retained_bytes": 32, "eof": False, "truncated": False, "error": None},
                "worker_stderr": {"text": "", "limit_bytes": 4096, "observed_bytes": 0, "retained_bytes": 0, "eof": False, "truncated": False, "error": None},
            },
        }

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

    def test_run_binding_accepts_actual_referenced_workflow_shape_and_rejects_conflicts(self):
        binding = self.hosted._run_binding(self.env, "token", lambda url, token, timeout=10: dict(RUN_PAYLOAD_FIXTURE))
        self.assertEqual(self.hosted.HOSTED_WORKFLOW_PATH, binding["hosted_workflow_path"])
        bad_payloads = [
            dict(RUN_PAYLOAD_FIXTURE, head_repository=None),
            dict(RUN_PAYLOAD_FIXTURE, head_repository={}),
            dict(RUN_PAYLOAD_FIXTURE, head_repository={"full_name": None}),
            dict(RUN_PAYLOAD_FIXTURE, head_repository={"full_name": "FEBuilderGBA/FEBuilderGBA"}),
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
            dict(RUN_PAYLOAD_FIXTURE, referenced_workflows=RUN_PAYLOAD_FIXTURE["referenced_workflows"] * 2),
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
        grant_fields = {
            "lookup_key": preflight["grant_lookup_key"],
            "run_id": preflight["run_binding"]["run_id"],
            "run_attempt": "1",
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
        grant_body = "\n".join(
            [self.hosted.GRANT_SCHEMA] + [f"{key}={value}" for key, value in grant_fields.items()]
        )
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
        base = {
            "lookup_key": preflight["grant_lookup_key"],
            "run_id": preflight["run_binding"]["run_id"],
            "run_attempt": "1",
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

        body = "\n".join([self.hosted.GRANT_SCHEMA] + [f"{key}={value}" for key, value in base.items()])
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
        grant_fields = {
            "lookup_key": preflight["grant_lookup_key"],
            "run_id": preflight["run_binding"]["run_id"],
            "run_attempt": "1",
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
        base_body = "\n".join(
            [self.hosted.GRANT_SCHEMA] + [f"{key}={value}" for key, value in grant_fields.items()]
        )
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
                initial | {"body": base_body + "\n# edited"},
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
            "preflight_digest": "digest",
        }
        body = "\n".join([
            self.hosted.GRANT_SCHEMA,
            "lookup_key=grant-key",
            "run_id=12345",
            "run_attempt=1",
            "candidate_sha=" + ("a" * 40),
            "caller_workflow_path=.github/workflows/e2e-norom.yml",
            "caller_workflow_sha=" + ("a" * 40),
            "hosted_workflow_path=.github/workflows/linux-x11-hosted.yml",
            "hosted_workflow_sha=" + ("a" * 40),
            "preflight_digest=digest",
            "tool_constraints_digest=fixed",
            "receipt_stem=receipt-stem",
            "operation=" + self.hosted.OPERATION,
            "invocations=1",
            "timeout_total=20",
            "timeout_work=18",
            "timeout_cleanup=2",
            "valid_after=2026-10-01T23:59:00Z",
            "valid_before=2026-10-02T00:01:00Z",
        ])
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
        receipt = self.pass_receipt()
        io_limits = self.hosted.native_smoke.IO_LIMITS
        six_byte = "\x00"
        receipt["worker_stderr"] = six_byte * self.hosted.native_smoke.WORKER_STDERR_LIMIT
        receipt["io"]["displayfd"]["text"] = six_byte * io_limits["displayfd"]
        receipt["io"]["xvfb_stderr"]["text"] = six_byte * io_limits["xvfb_stderr"]
        receipt["io"]["worker_stdout"]["text"] = six_byte * io_limits["worker_stdout"]
        receipt["io"]["worker_stderr"]["text"] = six_byte * io_limits["worker_stderr"]
        raw_path.write_text(json.dumps(receipt), encoding="utf-8")
        self.assertGreater(raw_path.stat().st_size, self.hosted.JSON_LIMIT)
        self.hosted.main([
            "summarize-receipt",
            "--receipt", str(raw_path),
            "--output", str(summary_path),
        ])
        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        self.assertEqual("passed", summary["status"])
        self.assertLessEqual(summary_path.stat().st_size, self.hosted.JSON_LIMIT)

    def test_summarize_native_receipt_for_upload_preserves_safe_producer_facts_and_strips_raw_stream_text(self):
        native_sources = self.native_sources()
        receipt = {
            "status": "failed",
            "timeout_seconds": 20,
            "phase": "worker",
            "started_utc": "2026-10-02T00:00:00Z",
            "elapsed_seconds": 18.0,
            "failure": "worker exited 1",
            "capture_failure": None,
            "cleanup_failures": {"worker_wait": "owned worker still alive"},
            "deadline_exceeded": False,
            "sources": native_sources,
            "diagnostic": {"status": "failed", "failure": "worker exited 1"},
            "supervisor": {"pid": 100, "start_ticks": 200},
            "worker": {"pid": 101, "start_ticks": 202, "command": [
                "/usr/bin/python3", "-B", "-m", "scripts.tests.linux_x11_native_smoke",
                "--allow-native-smoke", "--worker",
                "--xlib-path", "/usr/lib/x86_64-linux-gnu/libX11.so.6",
            ]},
            "worker_exit_code": 1,
            "xvfb": {"pid": 102, "start_ticks": 204, "command": [
                "/usr/bin/Xvfb", ":7", "-screen", "0", "320x240x24",
                "-nolisten", "tcp", "-auth", "/tmp/linux-x11-smoke/authority",
            ]},
            "xvfb_exit_code": 1,
            "worker_stderr": "raw stderr should not be uploaded",
            "io": {
                "displayfd": {
                    "text": "7\n",
                    "limit_bytes": 32,
                    "observed_bytes": 2,
                    "retained_bytes": 2,
                    "eof": False,
                    "truncated": False,
                    "error": None,
                },
                "xvfb_stderr": {
                    "text": "auth path /tmp/linux-x11-smoke/authority",
                    "limit_bytes": 4096,
                    "observed_bytes": 64,
                    "retained_bytes": 39,
                    "eof": False,
                    "truncated": False,
                    "error": None,
                },
                "worker_stdout": {
                    "text": "raw stdout",
                    "limit_bytes": 16384,
                    "observed_bytes": 16,
                    "retained_bytes": 10,
                    "eof": False,
                    "truncated": True,
                    "error": None,
                },
                "worker_stderr": {
                    "text": "raw stderr",
                    "limit_bytes": 4096,
                    "observed_bytes": 32,
                    "retained_bytes": 16,
                    "eof": False,
                    "truncated": True,
                    "error": "truncated",
                },
            },
        }
        summary = self.hosted.summarize_native_receipt_for_upload(receipt)
        self.assertEqual("failed", summary["status"])
        self.assertEqual("worker exited 1", summary["failure"])
        self.assertNotIn("worker_stderr", summary)
        self.assertEqual({"worker_wait": "owned worker still alive"}, summary["cleanup_failures"])
        self.assertEqual({"pid": 100, "start_ticks": 200}, summary["supervisor"])
        self.assertEqual({"pid": 101, "start_ticks": 202}, summary["worker"])
        self.assertEqual({"pid": 102, "start_ticks": 204, "command": [
            "/usr/bin/Xvfb", ":7", "-screen", "0", "320x240x24", "-nolisten", "tcp",
        ]}, summary["xvfb"])
        self.assertEqual(1, summary["xvfb_exit_code"])
        self.assertEqual(16, summary["io"]["worker_stderr"]["retained_bytes"])
        self.assertEqual(2, summary["io"]["displayfd"]["retained_bytes"])
        self.assertNotIn("text", summary["io"]["worker_stdout"])
        self.assertNotIn("text", summary["io"]["worker_stderr"])
        self.assertNotIn("-auth", summary["xvfb"]["command"])

    def test_summarize_native_receipt_for_upload_rejects_malformed_cleanup_and_stream_shapes(self):
        receipt = self.pass_receipt()
        broken = json.loads(json.dumps(receipt))
        broken["cleanup_failures"] = ["worker_wait"]
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken)
        broken = json.loads(json.dumps(receipt))
        del broken["io"]["worker_stdout"]["text"]
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.summarize_native_receipt_for_upload(broken)

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
