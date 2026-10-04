# SPDX-License-Identifier: GPL-3.0-or-later
"""Pure hosted issue-2160 workflow contracts with no native execution."""

from __future__ import annotations

import importlib.util
from datetime import datetime, timedelta, timezone
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "scripts" / "linux_x11_hosted.py"


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
            return {
                "head_repository": {"full_name": self.hosted.REPOSITORY},
                "head_sha": "a" * 40,
                "run_attempt": 1,
                "path": self.hosted.CALLER_WORKFLOW_PATH,
                "referenced_workflows": [{
                    "path": self.hosted.HOSTED_WORKFLOW_PATH,
                    "sha": "a" * 40,
                }],
            }
        if "/issues/2160/comments" in url:
            return []
        raise AssertionError(url)

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

    def test_prepare_payload_fails_closed_when_required_tool_is_missing(self):
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.prepare_payload(
                self.root,
                self.env,
                query=self.query,
                fetch_json=self.fetch,
                which=lambda name: None if name == "Xvfb" else f"/usr/bin/{name}",
            )

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

        def comment(body, *, author="laqieer", association="OWNER", comment_id=1):
            return {
                "id": comment_id,
                "body": body,
                "user": {"login": author},
                "author_association": association,
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
            [],
        )
        for comments in bad_cases:
            with self.subTest(comments=comments):
                with self.assertRaises(self.hosted.HostedWorkflowError):
                    self.hosted.find_matching_grant(comments, preflight, now=now)

    def test_validate_native_receipt_rejects_incomplete_positive_evidence(self):
        preflight = self.hosted.prepare_payload(
            self.root,
            {**self.env, "ISSUE2160_ROUTE": self.hosted.RESERVE_ROUTE},
            query=self.query,
            fetch_json=self.fetch,
            which=lambda name: f"/usr/bin/{name}",
        )
        report = {
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
            "sources": preflight["source_hashes"],
            "worker": {"pid": 101, "start_ticks": 202},
        }
        receipt = {
            "status": "passed",
            "timeout_seconds": 20,
            "phase": "validation",
            "elapsed_seconds": 1.2,
            "sources": preflight["source_hashes"],
            "diagnostic": report,
            "worker": {"pid": 101, "start_ticks": 202, "command": [
                "/usr/bin/python3", "-B", "-m", "scripts.tests.linux_x11_native_smoke",
                "--allow-native-smoke", "--worker",
            ]},
            "worker_exit_code": 0,
            "xvfb": {"command": ["/usr/bin/Xvfb"]},
            "xvfb_observed_exit_code": None,
            "io": {
                "displayfd": {"truncated": False, "error": None},
                "xvfb_stderr": {"truncated": False, "error": None},
                "worker_stdout": {"truncated": False, "error": None},
                "worker_stderr": {"truncated": False, "error": None},
            },
        }
        self.assertEqual(
            preflight["preflight_digest"],
            self.hosted.validate_native_receipt(receipt, preflight)["preflight_digest"],
        )
        broken = json.loads(json.dumps(receipt))
        del broken["diagnostic"]["missing_property_ok"]
        with self.assertRaises(self.hosted.HostedWorkflowError):
            self.hosted.validate_native_receipt(broken, preflight)
