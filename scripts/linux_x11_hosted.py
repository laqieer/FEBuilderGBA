# SPDX-License-Identifier: GPL-3.0-or-later
"""Bounded GitHub-hosted orchestration for issue #2160 native X11 review."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import urllib.request

from scripts.tests import linux_x11_native_smoke as native_smoke


REPOSITORY = "laqieer/FEBuilderGBA"
ISSUE_NUMBER = 2160
CALLER_WORKFLOW_PATH = ".github/workflows/e2e-norom.yml"
HOSTED_WORKFLOW_PATH = ".github/workflows/linux-x11-hosted.yml"
GRANT_SCHEMA = "issue2160-native-grant-v1"
OPERATION = "issue2160-linux-x11-hosted-v1"
PREPARE_ROUTE = "issue2160-prepare"
RESERVE_ROUTE = "issue2160-reserve-native"
ALLOWED_ROUTES = frozenset({PREPARE_ROUTE, RESERVE_ROUTE})
TIMEOUT_TOTAL = 20
TIMEOUT_WORK = 18
TIMEOUT_CLEANUP = 2
QUERY_LIMIT = 16384
JSON_LIMIT = 32768
HTTP_LIMIT = 1048576
HTTP_PAGES = 5
LOOKUP_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._:-]{0,79}\Z")
RECEIPT_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,79}\Z")
SHA_RE = re.compile(r"[0-9a-f]{40}\Z")
SHA256_RE = re.compile(r"[0-9a-f]{64}\Z")
ABS_PATH_RE = re.compile(r"(?:/[^\0]+|[A-Za-z]:\\[^\0]+)\Z")
DISPLAYFD_RE = re.compile(r"[1-9][0-9]{0,9}\Z")
RUN_ID_RE = re.compile(r"[1-9][0-9]{0,19}\Z")
RUN_ATTEMPT_RE = re.compile(r"[1-9][0-9]*\Z")
DPKG_OWNER_RE = re.compile(r"(?P<package>[A-Za-z0-9.+-]+(?::[A-Za-z0-9.+-]+)?): (?P<path>.+)\Z")
REFERENCED_WORKFLOW_RE = re.compile(
    rf"{re.escape(REPOSITORY)}/(?P<workflow_path>\.github/workflows/[A-Za-z0-9_.-]+\.yml)@(?P<workflow_sha>[0-9a-f]{{40}})\Z"
)
SOURCE_FILES = (
    ".github/workflows/e2e-norom.yml",
    ".github/workflows/linux-x11-hosted.yml",
    "scripts/linux_x11.py",
    "scripts/linux_x11_hosted.py",
    "scripts/tests/linux_x11_native_smoke.py",
)
NATIVE_SOURCE_PATHS = {
    "linux_x11.py": "scripts/linux_x11.py",
    "linux_x11_native_smoke.py": "scripts/tests/linux_x11_native_smoke.py",
}
RECEIPT_PHASES = frozenset({"setup", "readiness", "worker", "validation"})
LIBX11_CANDIDATES = (
    "/usr/lib/x86_64-linux-gnu/libX11.so.6",
    "/lib/x86_64-linux-gnu/libX11.so.6",
)
RAW_RECEIPT_JSON_LIMIT = (
    JSON_LIMIT
    + 6 * native_smoke.WORKER_STDERR_LIMIT
    + 6 * sum(native_smoke.IO_LIMITS.values())
    + 12 * native_smoke.FAILURE_TEXT_LIMIT
    + 6 * len(native_smoke.CLEANUP_FAILURE_KEYS) * native_smoke.CLEANUP_FAILURE_TEXT_LIMIT
)


class HostedWorkflowError(RuntimeError):
    """Fail-closed hosted workflow contract error."""


def _normalized_json(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def _sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def _sha256_path(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while True:
            chunk = stream.read(65536)
            if not chunk:
                return digest.hexdigest()
            digest.update(chunk)


def _bounded_json(path, value):
    data = (_normalized_json(value) + "\n").encode("utf-8")
    if len(data) > JSON_LIMIT:
        raise HostedWorkflowError("Bounded JSON output exceeded the reviewed limit")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8") as stream:
        stream.write(data.decode("utf-8"))


def _read_json(path, *, limit=JSON_LIMIT):
    data = path.read_bytes()
    if len(data) > limit:
        raise HostedWorkflowError("JSON input exceeded the reviewed limit")
    return json.loads(data)


def _read_native_receipt_json(path):
    return _read_json(path, limit=RAW_RECEIPT_JSON_LIMIT)


def _receipt_directory_name(receipt_stem):
    stem = _require_match("receipt stem", receipt_stem, RECEIPT_RE)
    return f"linux-x11-smoke-{stem}"


def _receipt_path(receipt_stem):
    return Path(_receipt_directory_name(receipt_stem)) / "receipt.json"


def _require_match(name, value, pattern):
    if not isinstance(value, str) or pattern.fullmatch(value) is None:
        raise HostedWorkflowError(f"Invalid {name}")
    return value


def _require_route(route):
    if route not in ALLOWED_ROUTES:
        raise HostedWorkflowError("Hosted route must be issue2160-prepare or issue2160-reserve-native")
    return route


def _query(command, *, cwd=None, timeout=10):
    result = subprocess.run(
        command,
        cwd=cwd,
        check=False,
        capture_output=True,
        timeout=timeout,
    )
    if len(result.stdout) > QUERY_LIMIT or len(result.stderr) > QUERY_LIMIT:
        raise HostedWorkflowError("Bounded command output exceeded the reviewed limit")
    if result.returncode != 0:
        raise HostedWorkflowError(
            f"Command failed: {' '.join(command)}: {result.stderr.decode('utf-8', 'replace').strip()}"
        )
    stderr = result.stderr.decode("utf-8", "replace").strip()
    if stderr:
        raise HostedWorkflowError(f"Unexpected stderr from {' '.join(command)}: {stderr}")
    return result.stdout.decode("utf-8", "replace").strip()


def _fetch_json(url, token, *, timeout=10):
    if not token:
        raise HostedWorkflowError("GITHUB_TOKEN is required for hosted readback")
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "FEBuilderGBA-issue2160-hosted",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    with urllib.request.urlopen(request, timeout=timeout) as response:
        chunks = []
        total = 0
        while True:
            chunk = response.read(65536)
            if not chunk:
                break
            total += len(chunk)
            if total > HTTP_LIMIT:
                raise HostedWorkflowError("GitHub API response exceeded the reviewed limit")
            chunks.append(chunk)
    return json.loads(b"".join(chunks))


def _package_record(path, query):
    owner = query(("dpkg-query", "-S", str(path)))
    match = DPKG_OWNER_RE.fullmatch(owner)
    if match is None or match.group("path") != str(path):
        raise HostedWorkflowError(f"Unexpected package owner record for {path}")
    package = match.group("package")
    version_info = query(("dpkg-query", "-W", "-f=${binary:Package}\t${Version}\t${Architecture}", package))
    pieces = version_info.split("\t")
    if len(pieces) != 3 or pieces[0] != package:
        raise HostedWorkflowError(f"Unexpected package metadata for {path}")
    target = path.resolve(strict=True)
    return {
        "path": str(path),
        "resolved_path": str(target),
        "mode": stat.S_IMODE(path.stat().st_mode),
        "sha256": _sha256_path(target),
        "package": {
            "name": pieces[0],
            "version": pieces[1],
            "architecture": pieces[2],
        },
    }


def _tool_constraints(query, which):
    python_path = which("python3")
    xvfb_path = which("Xvfb")
    if python_path != "/usr/bin/python3":
        raise HostedWorkflowError("Already-installed /usr/bin/python3 is required")
    if xvfb_path != "/usr/bin/Xvfb":
        raise HostedWorkflowError("Already-installed /usr/bin/Xvfb is required")
    libx11 = next((Path(path) for path in LIBX11_CANDIDATES if Path(path).is_file()), None)
    if libx11 is None:
        raise HostedWorkflowError("Already-installed libX11.so.6 is required")
    return {
        "python3": _package_record(Path(python_path), query),
        "xvfb": _package_record(Path(xvfb_path), query),
        "libx11": _package_record(libx11, query),
    }


def _source_hashes(root):
    data = {}
    for relative in SOURCE_FILES:
        path = root / relative
        if not path.is_file():
            raise HostedWorkflowError(f"Reviewed source file is missing: {relative}")
        data[relative] = _sha256_path(path)
    return data


def _issue_comments(api_base, token, fetch_json):
    comments = []
    for page in range(1, HTTP_PAGES + 1):
        batch = fetch_json(
            f"{api_base}/repos/{REPOSITORY}/issues/{ISSUE_NUMBER}/comments?per_page=100&page={page}",
            token,
        )
        if not isinstance(batch, list):
            raise HostedWorkflowError("Issue comment readback returned an unexpected payload")
        comments.extend(batch)
        if len(batch) < 100:
            return comments
    raise HostedWorkflowError("Issue comment pagination exceeded the reviewed limit")


def _issue_comment(api_base, comment_id, token, fetch_json):
    payload = fetch_json(f"{api_base}/repos/{REPOSITORY}/issues/comments/{comment_id}", token)
    if not isinstance(payload, dict) or payload.get("id") != comment_id:
        raise HostedWorkflowError("Issue comment readback returned an unexpected payload")
    return payload


def _referenced_workflow(item):
    if not isinstance(item, dict):
        raise HostedWorkflowError("GitHub API referenced_workflows payload was malformed")
    path_value = item.get("path")
    sha_value = item.get("sha")
    ref_value = item.get("ref")
    if not isinstance(path_value, str) or not isinstance(sha_value, str) or not isinstance(ref_value, str):
        raise HostedWorkflowError("GitHub API referenced_workflows payload was malformed")
    match = REFERENCED_WORKFLOW_RE.fullmatch(path_value)
    if match is None or match.group("workflow_sha") != sha_value:
        raise HostedWorkflowError("Hosted reusable workflow SHA/path evidence was malformed")
    return {
        "workflow_path": match.group("workflow_path"),
        "workflow_sha": sha_value,
        "workflow_ref": ref_value,
    }


def _run_binding(environment, token, fetch_json):
    api_base = environment.get("GITHUB_API_URL", "https://api.github.com")
    run_id = _require_match("GITHUB_RUN_ID", environment.get("GITHUB_RUN_ID", ""), RUN_ID_RE)
    run_attempt = int(_require_match("GITHUB_RUN_ATTEMPT", environment.get("GITHUB_RUN_ATTEMPT", ""), RUN_ATTEMPT_RE))
    payload = fetch_json(f"{api_base}/repos/{REPOSITORY}/actions/runs/{run_id}", token)
    if not isinstance(payload, dict):
        raise HostedWorkflowError("GitHub API run readback returned an unexpected payload")
    head_repository = payload.get("head_repository")
    if not isinstance(head_repository, dict) or head_repository.get("full_name") != REPOSITORY:
        raise HostedWorkflowError("Hosted readback must stay on the fork")
    if payload.get("head_sha") != environment["GITHUB_SHA"]:
        raise HostedWorkflowError("GitHub API run head SHA mismatched the current workflow SHA")
    if int(payload.get("run_attempt", 0)) != run_attempt:
        raise HostedWorkflowError("GitHub API run attempt mismatched the current workflow attempt")
    caller_path = str(payload.get("path", "")).split("@", 1)[0]
    if caller_path != CALLER_WORKFLOW_PATH:
        raise HostedWorkflowError("Hosted run must originate from e2e-norom.yml")
    referenced = payload.get("referenced_workflows")
    if not isinstance(referenced, list):
        raise HostedWorkflowError("GitHub API run lacked referenced_workflows evidence")
    hosted = []
    for item in referenced:
        evidence = _referenced_workflow(item)
        if evidence["workflow_path"] != HOSTED_WORKFLOW_PATH:
            raise HostedWorkflowError("Hosted run referenced an unexpected reusable workflow")
        if evidence["workflow_sha"] != environment["GITHUB_SHA"]:
            raise HostedWorkflowError("Hosted reusable workflow SHA/path evidence was not exact")
        hosted.append(evidence)
    if len(hosted) != 1:
        raise HostedWorkflowError("Hosted reusable workflow SHA/path evidence was not exact")
    return {
        "run_id": run_id,
        "run_attempt": run_attempt,
        "caller_workflow_path": CALLER_WORKFLOW_PATH,
        "caller_workflow_sha": environment["GITHUB_WORKFLOW_SHA"],
        "hosted_workflow_path": HOSTED_WORKFLOW_PATH,
        "hosted_workflow_sha": hosted[0]["workflow_sha"],
    }


def prepare_payload(root, environment=None, *, query=_query, fetch_json=_fetch_json, which=shutil.which):
    environment = dict(os.environ if environment is None else environment)
    if environment.get("GITHUB_REPOSITORY") != REPOSITORY:
        raise HostedWorkflowError("Hosted workflow must target only laqieer/FEBuilderGBA")
    route = _require_route(environment.get("ISSUE2160_ROUTE"))
    candidate_sha = _require_match("candidate SHA", environment.get("ISSUE2160_CANDIDATE_SHA", ""), SHA_RE)
    github_sha = _require_match("GITHUB_SHA", environment.get("GITHUB_SHA", ""), SHA_RE)
    workflow_sha = _require_match("GITHUB_WORKFLOW_SHA", environment.get("GITHUB_WORKFLOW_SHA", ""), SHA_RE)
    if candidate_sha != github_sha or candidate_sha != workflow_sha:
        raise HostedWorkflowError("candidate_sha, github.sha, and github.workflow_sha must match exactly")
    receipt_stem = _require_match("receipt stem", environment.get("ISSUE2160_RECEIPT_STEM", ""), RECEIPT_RE)
    lookup_key = environment.get("ISSUE2160_GRANT_LOOKUP_KEY", "")
    if lookup_key:
        _require_match("grant lookup key", lookup_key, LOOKUP_RE)
    if os.geteuid() == 0 or os.getuid() == 0:
        raise HostedWorkflowError("Fresh hosted review must run as non-root")
    head = query(("git", "rev-parse", "HEAD"), cwd=root)
    if head != candidate_sha:
        raise HostedWorkflowError("Checked-out HEAD mismatched the immutable candidate SHA")
    token = environment.get("GITHUB_TOKEN", "")
    tools = _tool_constraints(query, which)
    tool_constraints_digest = _sha256_bytes(_normalized_json(tools).encode("utf-8"))
    sources = _source_hashes(root)
    run_binding = _run_binding(environment, token, fetch_json)
    runner_requirements = {
        "os": environment.get("RUNNER_OS"),
        "arch": environment.get("RUNNER_ARCH"),
    }
    if runner_requirements["os"] != "Linux" or runner_requirements["arch"] != "X64":
        raise HostedWorkflowError("Fresh hosted review must run on Linux X64")
    payload = {
        "schema": OPERATION,
        "route": route,
        "candidate_sha": candidate_sha,
        "receipt_stem": receipt_stem,
        "grant_lookup_key": lookup_key,
        "repository": REPOSITORY,
        "run_binding": run_binding,
        "runner_requirements": runner_requirements,
        "runner_observation": {
            "name": environment.get("RUNNER_NAME"),
            "image_os": environment.get("ImageOS"),
            "image_version": environment.get("ImageVersion"),
        },
        "identity_observation": {
            "uid": os.getuid(),
            "gid": os.getgid(),
            "euid": os.geteuid(),
            "egid": os.getegid(),
        },
        "source_hashes": sources,
        "tools": tools,
        "tool_constraints_digest": tool_constraints_digest,
    }
    payload["stable_constraints"] = {
        "schema": OPERATION,
        "repository": REPOSITORY,
        "route": route,
        "candidate_sha": candidate_sha,
        "receipt_stem": receipt_stem,
        "run_binding": run_binding,
        "runner_requirements": runner_requirements,
        "source_hashes": sources,
        "tools": tools,
        "tool_constraints_digest": tool_constraints_digest,
        "timeout_total": TIMEOUT_TOTAL,
        "timeout_work": TIMEOUT_WORK,
        "timeout_cleanup": TIMEOUT_CLEANUP,
    }
    payload["stable_constraints_digest"] = _sha256_bytes(
        _normalized_json(payload["stable_constraints"]).encode("utf-8")
    )
    payload["preflight_digest"] = _sha256_bytes(_normalized_json(payload).encode("utf-8"))
    return payload


def _parse_grant_body(body):
    lines = body.splitlines()
    if not lines or lines[0] != GRANT_SCHEMA or any(not line or line.startswith((">", "`")) for line in lines[1:]):
        raise HostedWorkflowError("Grant comment was not strict unquoted grant data")
    data = {}
    for line in lines[1:]:
        match = re.fullmatch(r"([a-z_]+)=(.+)", line)
        if match is None or match.group(1) in data:
            raise HostedWorkflowError("Grant comment had malformed key/value data")
        data[match.group(1)] = match.group(2)
    required = {
        "lookup_key", "run_id", "run_attempt", "candidate_sha",
        "caller_workflow_path", "caller_workflow_sha",
        "hosted_workflow_path", "hosted_workflow_sha",
        "preflight_digest", "tool_constraints_digest", "receipt_stem",
        "operation", "invocations", "timeout_total",
        "timeout_work", "timeout_cleanup", "valid_after", "valid_before",
    }
    if set(data) != required:
        raise HostedWorkflowError("Grant comment fields were incomplete or unexpected")
    return data


def _grant_body_sha256(body):
    if not isinstance(body, str):
        raise HostedWorkflowError("Grant comment body was malformed")
    return _sha256_bytes("\n".join(body.splitlines()).encode("utf-8"))


def _parse_aware_instant(value, *, error_message):
    if not isinstance(value, str):
        raise HostedWorkflowError(error_message)
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise HostedWorkflowError(error_message) from error
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise HostedWorkflowError(error_message)
    return parsed.astimezone(timezone.utc)


def _parse_instant(value):
    return _parse_aware_instant(value, error_message="Grant comment time window was malformed")


def _grant_comment_timestamps(comment):
    created_at = _parse_instant(comment.get("created_at"))
    updated_at = _parse_instant(comment.get("updated_at"))
    if updated_at < created_at:
        raise HostedWorkflowError("Grant comment timestamps were malformed")
    return {
        "created_at": comment["created_at"],
        "updated_at": comment["updated_at"],
    }


def find_matching_grant(comments, preflight, *, now=None):
    expected = {
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
        "operation": OPERATION,
        "invocations": "1",
        "timeout_total": str(TIMEOUT_TOTAL),
        "timeout_work": str(TIMEOUT_WORK),
        "timeout_cleanup": str(TIMEOUT_CLEANUP),
    }
    now = datetime.now(timezone.utc) if now is None else now
    matches = []
    for comment in comments:
        if comment.get("user", {}).get("login") != "laqieer":
            continue
        if comment.get("author_association") != "OWNER":
            continue
        try:
            grant = _parse_grant_body(comment.get("body", ""))
        except HostedWorkflowError:
            continue
        if any(grant[key] != value for key, value in expected.items()):
            continue
        valid_after = _parse_instant(grant["valid_after"])
        valid_before = _parse_instant(grant["valid_before"])
        if not valid_after <= now <= valid_before:
            continue
        comment_id = comment.get("id")
        if not isinstance(comment_id, int) or comment_id <= 0:
            raise HostedWorkflowError("Grant comment lacked an exact comment identifier")
        body = comment.get("body", "")
        if len(body.encode("utf-8")) > JSON_LIMIT:
            raise HostedWorkflowError("Grant comment body exceeded the reviewed limit")
        timestamps = _grant_comment_timestamps(comment)
        matches.append({
            "id": comment_id,
            "grant": grant,
            "grant_freeze": {
                "comment_id": comment_id,
                "author_login": comment["user"]["login"],
                "author_association": comment["author_association"],
                "created_at": timestamps["created_at"],
                "updated_at": timestamps["updated_at"],
                "normalized_body_sha256": _grant_body_sha256(body),
            },
        })
    if len(matches) != 1:
        raise HostedWorkflowError("Exact one-use coordinator grant match is required")
    return matches[0]


def confirm_grant_freeze(comment, authorized, *, now=None):
    freeze = authorized["grant_freeze"]
    if comment.get("id") != freeze["comment_id"]:
        raise HostedWorkflowError("Grant comment identity changed after authorization")
    if comment.get("user", {}).get("login") != freeze["author_login"]:
        raise HostedWorkflowError("Grant author changed after authorization")
    if comment.get("author_association") != freeze["author_association"]:
        raise HostedWorkflowError("Grant author association changed after authorization")
    timestamps = _grant_comment_timestamps(comment)
    if timestamps["created_at"] != freeze["created_at"] or timestamps["updated_at"] != freeze["updated_at"]:
        raise HostedWorkflowError("Grant comment timestamps changed after authorization")
    body = comment.get("body", "")
    if _grant_body_sha256(body) != freeze["normalized_body_sha256"]:
        raise HostedWorkflowError("Grant comment body changed after authorization")
    grant = _parse_grant_body(body)
    if grant != authorized["grant"]:
        raise HostedWorkflowError("Grant comment data changed after authorization")
    current = datetime.now(timezone.utc) if now is None else now
    valid_after = _parse_instant(grant["valid_after"])
    valid_before = _parse_instant(grant["valid_before"])
    if not valid_after <= current <= valid_before:
        raise HostedWorkflowError("Grant comment was outside its reviewed validity window")
    return {
        "schema": OPERATION,
        "grant_comment_id": freeze["comment_id"],
        "preflight_digest": authorized["preflight_digest"],
    }


def _sanitize_failure_diagnostic(diagnostic):
    if not isinstance(diagnostic, dict) or diagnostic.get("status") != "failed":
        raise HostedWorkflowError("Native receipt diagnostic payload was malformed")
    return {
        "status": "failed",
        "failure": _sanitize_bounded_text(
            "Native receipt diagnostic payload was malformed",
            diagnostic.get("failure"),
            limit=native_smoke.FAILURE_TEXT_LIMIT,
        ),
    }


def _sanitize_diagnostic(diagnostic):
    if not isinstance(diagnostic, dict):
        raise HostedWorkflowError("Native receipt diagnostic payload was malformed")
    status = diagnostic.get("status")
    if status == "passed":
        return _sanitize_pass_diagnostic(diagnostic)
    if status == "failed":
        return _sanitize_failure_diagnostic(diagnostic)
    raise HostedWorkflowError("Native receipt diagnostic payload was malformed")


def _sanitize_cleanup_failures(cleanup_failures):
    if cleanup_failures is None:
        return None
    if not isinstance(cleanup_failures, dict):
        raise HostedWorkflowError("Native receipt cleanup evidence was malformed")
    sanitized = {}
    for key, value in cleanup_failures.items():
        if key not in native_smoke.CLEANUP_FAILURE_KEYS or not isinstance(value, str):
            raise HostedWorkflowError("Native receipt cleanup evidence was malformed")
        if len(value) > native_smoke.CLEANUP_FAILURE_TEXT_LIMIT:
            raise HostedWorkflowError("Native receipt cleanup evidence was malformed")
        sanitized[key] = value
    return sanitized


def _sanitize_bounded_text(error_message, value, *, limit):
    if not isinstance(value, str) or len(value) > limit:
        raise HostedWorkflowError(error_message)
    return value


def _sanitize_receipt_status(status):
    if not isinstance(status, str) or status not in {"passed", "failed"}:
        raise HostedWorkflowError("Native receipt payload was malformed")
    return status


def _sanitize_receipt_timeout(timeout_seconds):
    if type(timeout_seconds) is not int or not 10 <= timeout_seconds <= 60:
        raise HostedWorkflowError("Native receipt payload was malformed")
    return timeout_seconds


def _sanitize_receipt_phase(phase):
    if not isinstance(phase, str) or phase not in RECEIPT_PHASES:
        raise HostedWorkflowError("Native receipt payload was malformed")
    return phase


def _sanitize_receipt_started_utc(started_utc):
    _parse_aware_instant(started_utc, error_message="Native receipt payload was malformed")
    return started_utc


def _sanitize_receipt_elapsed_seconds(elapsed_seconds):
    if type(elapsed_seconds) not in (int, float) or not math.isfinite(elapsed_seconds) or elapsed_seconds < 0:
        raise HostedWorkflowError("Native receipt payload was malformed")
    return elapsed_seconds


def _sanitize_receipt_bool(value):
    if type(value) is not bool:
        raise HostedWorkflowError("Native receipt payload was malformed")
    return value


def _sanitize_receipt_exit_code(value, *, allow_none=False):
    if value is None:
        if allow_none:
            return None
        raise HostedWorkflowError("Native receipt payload was malformed")
    if type(value) is not int:
        raise HostedWorkflowError("Native receipt payload was malformed")
    return value


def _sanitize_optional_summary_field(summary, payload, key, sanitizer):
    if key in payload:
        summary[key] = sanitizer(payload[key])


def _sanitize_summary_payload(receipt, *, status):
    summary = {
        "status": status,
        "timeout_seconds": _sanitize_receipt_timeout(receipt.get("timeout_seconds")),
        "phase": _sanitize_receipt_phase(receipt.get("phase")),
        "elapsed_seconds": _sanitize_receipt_elapsed_seconds(receipt.get("elapsed_seconds")),
    }
    _sanitize_optional_summary_field(summary, receipt, "started_utc", _sanitize_receipt_started_utc)
    _sanitize_optional_summary_field(
        summary,
        receipt,
        "failure",
        lambda value: _sanitize_bounded_text(
            "Native receipt payload was malformed",
            value,
            limit=native_smoke.FAILURE_TEXT_LIMIT,
        ),
    )
    _sanitize_optional_summary_field(
        summary,
        receipt,
        "capture_failure",
        lambda value: _sanitize_bounded_text(
            "Native receipt payload was malformed",
            value,
            limit=native_smoke.FAILURE_TEXT_LIMIT,
        ),
    )
    _sanitize_optional_summary_field(summary, receipt, "deadline_exceeded", _sanitize_receipt_bool)
    if "sources" in receipt:
        summary["sources"] = _sanitize_source_hashes(receipt.get("sources"))
    if "worker_exit_code" in receipt:
        summary["worker_exit_code"] = _sanitize_receipt_exit_code(
            receipt.get("worker_exit_code"),
            allow_none=(status == "failed"),
        )
    if "xvfb_observed_exit_code" in receipt:
        summary["xvfb_observed_exit_code"] = _sanitize_receipt_exit_code(
            receipt.get("xvfb_observed_exit_code"),
            allow_none=True,
        )
    if "xvfb_exit_code" in receipt:
        summary["xvfb_exit_code"] = _sanitize_receipt_exit_code(
            receipt.get("xvfb_exit_code"),
            allow_none=(status == "failed"),
        )
    if status == "passed":
        for key in ("started_utc", "sources", "worker_exit_code", "xvfb_observed_exit_code", "xvfb_exit_code"):
            if key not in summary:
                raise HostedWorkflowError("Native receipt payload was malformed")
    return summary


def _sanitize_source_hashes(sources):
    if not isinstance(sources, dict):
        raise HostedWorkflowError("Native receipt source hashes were malformed")
    expected = tuple(NATIVE_SOURCE_PATHS)
    if set(sources) != set(expected):
        raise HostedWorkflowError("Native receipt source hashes were malformed")
    sanitized = {}
    for name in expected:
        value = sources.get(name)
        if not isinstance(value, str) or SHA256_RE.fullmatch(value) is None:
            raise HostedWorkflowError("Native receipt source hashes were malformed")
        sanitized[name] = value
    return sanitized


def _select_diagnostic_source_hashes(sources):
    if not isinstance(sources, dict):
        raise HostedWorkflowError("Native receipt source hashes were malformed")
    sanitized = {}
    for name in NATIVE_SOURCE_PATHS:
        value = sources.get(name)
        if not isinstance(value, str) or SHA256_RE.fullmatch(value) is None:
            raise HostedWorkflowError("Native receipt source hashes were malformed")
        sanitized[name] = value
    return sanitized


def _sanitize_identity(identity, *, required=()):
    if not isinstance(identity, dict):
        raise HostedWorkflowError("Native receipt identity payload was malformed")
    sanitized = {}
    for key in required:
        value = identity.get(key)
        if type(value) is not int or value <= 0:
            raise HostedWorkflowError("Native receipt identity payload was malformed")
        sanitized[key] = value
    return sanitized


def _sanitize_libx11(libx11):
    if not isinstance(libx11, dict):
        raise HostedWorkflowError("Loaded libX11 evidence is required")
    sanitized = {}
    for key in ("path", "resolved_path"):
        value = libx11.get(key)
        if not isinstance(value, str) or not value.startswith("/"):
            raise HostedWorkflowError("Loaded libX11 evidence is required")
        sanitized[key] = value
    sha256 = libx11.get("sha256")
    if not isinstance(sha256, str) or SHA256_RE.fullmatch(sha256) is None:
        raise HostedWorkflowError("Loaded libX11 evidence is required")
    sanitized["sha256"] = sha256
    return sanitized


def _sanitize_exact_native_error_event(event, *, label):
    if not isinstance(event, dict):
        raise HostedWorkflowError(f"{label} evidence is required")
    sanitized = {}
    for key in ("callback_display", "display", "resourceid"):
        value = event.get(key)
        if type(value) is not int or value <= 0:
            raise HostedWorkflowError(f"{label} evidence is required")
        sanitized[key] = value
    for key in ("serial", "error_code", "request_code", "minor_code", "type"):
        value = event.get(key)
        if type(value) is not int or value < 0:
            raise HostedWorkflowError(f"{label} evidence is required")
        sanitized[key] = value
    return sanitized


def _sanitize_pass_diagnostic(diagnostic):
    try:
        report = native_smoke.validate_pass_report(diagnostic)
    except ValueError as error:
        raise HostedWorkflowError(str(error)) from error
    window = report["window"]
    owner_display = report["owner_display"]
    observer_display = report["observer_display"]
    request = report["badwindow_request"]
    badwindow_event = _sanitize_exact_native_error_event(
        report["badwindow_events"][0],
        label="BadWindow event",
    )
    unrelated_event = _sanitize_exact_native_error_event(
        report["unrelated_rejected"][0],
        label="Unrelated native error",
    )
    sanitized = {
        "status": "passed",
        "window": window,
        "owner_display": owner_display,
        "observer_display": observer_display,
        "present_before_destroy": [window],
        "live_value": "owned smoke",
        "missing_property_ok": True,
        "owned_destroyed": {"display": owner_display, "window": window},
        "badwindow_request": {
            "display": observer_display,
            "serial": request["serial"],
            "window": window,
        },
        "badwindow_events": [badwindow_event],
        "fresh_children_display": observer_display,
        "fresh_children_after_destroy": [],
        "unrelated_rejected": [unrelated_event],
        "pending_error_rejected": True,
        "sources": _select_diagnostic_source_hashes(report["sources"]),
        "worker": _sanitize_identity(report["worker"], required=("pid", "start_ticks")),
    }
    if "libx11" in report:
        sanitized["libx11"] = _sanitize_libx11(report["libx11"])
    return sanitized


def _sanitize_xvfb_command(command):
    if not isinstance(command, list) or not all(isinstance(item, str) for item in command):
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    if len(command) != 11:
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    executable = command[0]
    if ABS_PATH_RE.fullmatch(executable) is None or Path(executable).name != "Xvfb":
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    if command[1] != "-displayfd" or DISPLAYFD_RE.fullmatch(command[2]) is None:
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    if command[3:6] != ["-screen", "0", "320x240x24"]:
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    if command[6:8] != ["-nolisten", "tcp"]:
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    if command[8] != "-auth" or ABS_PATH_RE.fullmatch(command[9]) is None:
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    if command[10] != "-noreset":
        raise HostedWorkflowError("Native receipt xvfb command payload was malformed")
    return [
        executable,
        "-displayfd",
        command[2],
        "-screen",
        "0",
        "320x240x24",
        "-nolisten",
        "tcp",
        "-noreset",
    ]


def _sanitize_stream_entry(name, entry):
    if not isinstance(entry, dict):
        raise HostedWorkflowError(f"Native receipt {name} diagnostics were malformed")
    text = entry.get("text")
    if not isinstance(text, str):
        raise HostedWorkflowError(f"Native receipt {name} diagnostics were malformed")
    for key in ("limit_bytes", "observed_bytes", "retained_bytes"):
        if type(entry.get(key)) is not int or entry[key] < 0:
            raise HostedWorkflowError(f"Native receipt {name} diagnostics were malformed")
    for key in ("eof", "truncated"):
        if type(entry.get(key)) is not bool:
            raise HostedWorkflowError(f"Native receipt {name} diagnostics were malformed")
    error = entry.get("error")
    if error is not None and not isinstance(error, str):
        raise HostedWorkflowError(f"Native receipt {name} diagnostics were malformed")
    return {
        "limit_bytes": entry["limit_bytes"],
        "observed_bytes": entry["observed_bytes"],
        "retained_bytes": entry["retained_bytes"],
        "eof": entry["eof"],
        "truncated": entry["truncated"],
        "error": error,
    }


def _sanitize_io_entries(io_entries, *, require_all):
    if not isinstance(io_entries, dict):
        raise HostedWorkflowError("Native receipt io payload was malformed")
    names = ("displayfd", "xvfb_stderr", "worker_stdout", "worker_stderr")
    extra = set(io_entries) - set(names)
    if extra:
        raise HostedWorkflowError("Native receipt io payload was malformed")
    if require_all and set(io_entries) != set(names):
        raise HostedWorkflowError("Native receipt io payload was malformed")
    sanitized = {}
    for name in names:
        if name in io_entries:
            sanitized[name] = _sanitize_stream_entry(name, io_entries[name])
    return sanitized


def summarize_native_receipt_for_upload(receipt):
    if not isinstance(receipt, dict):
        raise HostedWorkflowError("Native receipt payload was malformed")
    status = _sanitize_receipt_status(receipt.get("status"))
    summary = _sanitize_summary_payload(receipt, status=status)
    supervisor = receipt.get("supervisor")
    if supervisor is not None:
        summary["supervisor"] = _sanitize_identity(supervisor, required=("pid", "start_ticks"))
    elif status == "passed":
        raise HostedWorkflowError("Native receipt identity payload was malformed")
    cleanup_failures = _sanitize_cleanup_failures(receipt.get("cleanup_failures"))
    if cleanup_failures:
        summary["cleanup_failures"] = cleanup_failures
    worker = receipt.get("worker")
    if worker is not None:
        summary["worker"] = _sanitize_identity(worker, required=("pid", "start_ticks"))
    xvfb = receipt.get("xvfb")
    if xvfb is not None:
        sanitized_xvfb = _sanitize_identity(xvfb, required=("pid", "start_ticks"))
        sanitized_xvfb["command"] = _sanitize_xvfb_command(xvfb.get("command"))
        summary["xvfb"] = sanitized_xvfb
    diagnostic = receipt.get("diagnostic")
    if diagnostic is not None:
        summary["diagnostic"] = _sanitize_diagnostic(diagnostic)
    summary["io"] = _sanitize_io_entries(receipt.get("io"), require_all=(status == "passed"))
    return summary


def run_native_smoke(preflight):
    receipt_stem = preflight.get("receipt_stem")
    tools = preflight.get("tools")
    if not isinstance(tools, dict) or not isinstance(tools.get("libx11"), dict):
        raise HostedWorkflowError("Preflight tool evidence was malformed")
    xlib_path = tools["libx11"].get("path")
    if not isinstance(xlib_path, str):
        raise HostedWorkflowError("Preflight tool evidence was malformed")
    receipt_dir = _receipt_directory_name(receipt_stem)
    native_smoke.validate_options(True, TIMEOUT_TOTAL, receipt_dir, xlib_path)
    return native_smoke.supervise(TIMEOUT_TOTAL, receipt_dir, xlib_path=xlib_path)


def stage_artifacts(preflight_path, grant_path, receipt_stem, output_dir):
    _receipt_directory_name(receipt_stem)
    bundle = Path(output_dir)
    if bundle.exists():
        raise HostedWorkflowError("Hosted upload bundle must be a fresh directory")
    bundle.mkdir(parents=True)
    for source_path, output_name in (
            (Path(preflight_path), "preflight.json"),
            (Path(grant_path), "grant.json")):
        if source_path.is_file():
            _bounded_json(bundle / output_name, _read_json(source_path))
    receipt_path = _receipt_path(receipt_stem)
    if receipt_path.is_file():
        _bounded_json(
            bundle / "receipt-summary.json",
            summarize_native_receipt_for_upload(_read_native_receipt_json(receipt_path)),
        )


def authorize_native(root, preflight, environment=None, *, query=_query, fetch_json=_fetch_json, which=shutil.which, now=None):
    environment = dict(os.environ if environment is None else environment)
    if environment.get("GITHUB_RUN_ATTEMPT") != "1":
        raise HostedWorkflowError("Native reservation must refuse reruns before any grant readback")
    current = prepare_payload(root, environment, query=query, fetch_json=fetch_json, which=which)
    if current["route"] != RESERVE_ROUTE:
        raise HostedWorkflowError("Native authorization is only valid for the reserve-native route")
    for key in (
        "candidate_sha",
        "receipt_stem",
        "source_hashes",
        "tools",
        "run_binding",
        "tool_constraints_digest",
        "stable_constraints",
        "stable_constraints_digest",
    ):
        if current[key] != preflight[key]:
            raise HostedWorkflowError(f"Fresh hosted runner evidence drifted for {key}")
    comments = _issue_comments(
        environment.get("GITHUB_API_URL", "https://api.github.com"),
        environment.get("GITHUB_TOKEN", ""),
        fetch_json,
    )
    match = find_matching_grant(comments, preflight, now=now)
    return {
        "schema": OPERATION,
        "preflight_digest": preflight["preflight_digest"],
        "current_preflight_digest": current["preflight_digest"],
        "stable_constraints_digest": current["stable_constraints_digest"],
        "grant": match["grant"],
        "grant_freeze": match["grant_freeze"],
    }


def _expected_native_sources(preflight):
    expected = {}
    for basename, relative in NATIVE_SOURCE_PATHS.items():
        if relative not in preflight["source_hashes"]:
            raise HostedWorkflowError(f"Reviewed source file is missing from preflight: {relative}")
        expected[basename] = preflight["source_hashes"][relative]
    return expected


def _validate_native_source_hashes(observed, preflight, label):
    if observed != _expected_native_sources(preflight):
        raise HostedWorkflowError(f"{label} source hashes mismatched the reviewed source")


def validate_native_receipt(receipt, preflight):
    summary = _sanitize_summary_payload(receipt, status=_sanitize_receipt_status(receipt.get("status")))
    if summary["status"] != "passed":
        raise HostedWorkflowError("Native receipt was not passed")
    if summary["timeout_seconds"] != TIMEOUT_TOTAL:
        raise HostedWorkflowError("Native receipt timeout must remain exactly 20 seconds")
    if summary["phase"] != "validation":
        raise HostedWorkflowError("Native receipt did not reach validation")
    if "failure" in summary or "capture_failure" in summary or receipt.get("cleanup_failures"):
        raise HostedWorkflowError("Native receipt recorded a failure-shaped cleanup or capture state")
    if summary.get("deadline_exceeded"):
        raise HostedWorkflowError("Native receipt exceeded the reviewed outer deadline")
    if summary["elapsed_seconds"] > TIMEOUT_TOTAL:
        raise HostedWorkflowError("Native receipt elapsed time exceeded the reviewed outer deadline")
    _validate_native_source_hashes(summary["sources"], preflight, "Supervisor")
    diagnostic = _sanitize_pass_diagnostic(receipt.get("diagnostic"))
    _validate_native_source_hashes(diagnostic["sources"], preflight, "Worker")
    worker = receipt.get("worker", {})
    if diagnostic["worker"] != {key: worker.get(key) for key in ("pid", "start_ticks")}:
        raise HostedWorkflowError("Worker identity mismatched the supervised child")
    if summary["worker_exit_code"] != 0:
        raise HostedWorkflowError("Native worker did not exit cleanly")
    if summary["xvfb_observed_exit_code"] is not None:
        raise HostedWorkflowError("Xvfb must remain live until owned cleanup")
    xvfb = receipt.get("xvfb")
    if not isinstance(xvfb, dict):
        raise HostedWorkflowError("Native receipt identity payload was malformed")
    sanitized_xvfb_command = _sanitize_xvfb_command(xvfb.get("command"))
    if sanitized_xvfb_command[0] != preflight["tools"]["xvfb"]["path"]:
        raise HostedWorkflowError("Native receipt used an unexpected Xvfb path")
    expected_libx11 = {
        key: preflight["tools"]["libx11"][key]
        for key in ("path", "resolved_path", "sha256")
    }
    if diagnostic.get("libx11") != expected_libx11:
        raise HostedWorkflowError("Native receipt loaded an unexpected libX11 object")
    if receipt.get("worker", {}).get("command") != [
        preflight["tools"]["python3"]["path"],
        "-B",
        "-m",
        "scripts.tests.linux_x11_native_smoke",
        "--allow-native-smoke",
        "--worker",
        "--xlib-path",
        preflight["tools"]["libx11"]["path"],
    ]:
        raise HostedWorkflowError("Native receipt used an unexpected worker command")
    io_state = receipt.get("io", {})
    for name in ("displayfd", "xvfb_stderr", "worker_stdout", "worker_stderr"):
        entry = io_state.get(name)
        if not isinstance(entry, dict) or entry.get("truncated") or entry.get("error"):
            raise HostedWorkflowError(f"Native receipt {name} diagnostics were incomplete or malformed")
    return {
        "schema": OPERATION,
        "preflight_digest": preflight["preflight_digest"],
        "receipt": diagnostic,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)

    prepare = subcommands.add_parser("prepare")
    prepare.add_argument("--output", required=True)

    authorize = subcommands.add_parser("authorize-native")
    authorize.add_argument("--preflight", required=True)
    authorize.add_argument("--output", required=True)

    confirm = subcommands.add_parser("confirm-grant")
    confirm.add_argument("--grant", required=True)

    run_native = subcommands.add_parser("run-native-smoke")
    run_native.add_argument("--preflight", required=True)

    stage = subcommands.add_parser("stage-artifacts")
    stage.add_argument("--preflight", required=True)
    stage.add_argument("--grant", required=True)
    stage.add_argument("--receipt-stem")
    stage.add_argument("--output-dir", required=True)

    summarize = subcommands.add_parser("summarize-receipt")
    summarize.add_argument("--receipt", required=True)
    summarize.add_argument("--output", required=True)

    validate = subcommands.add_parser("validate-receipt")
    validate.add_argument("--preflight", required=True)
    validate.add_argument("--receipt")

    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parents[1]
    if args.command == "prepare":
        payload = prepare_payload(root)
        _bounded_json(Path(args.output), payload)
        return 0
    if args.command == "authorize-native":
        result = authorize_native(root, _read_json(Path(args.preflight)))
        _bounded_json(Path(args.output), result)
        return 0
    if args.command == "confirm-grant":
        authorization = _read_json(Path(args.grant))
        comment = _issue_comment(
            os.environ.get("GITHUB_API_URL", "https://api.github.com"),
            authorization["grant_freeze"]["comment_id"],
            os.environ.get("GITHUB_TOKEN", ""),
            _fetch_json,
        )
        print(_normalized_json(confirm_grant_freeze(comment, authorization)))
        return 0
    if args.command == "run-native-smoke":
        return run_native_smoke(_read_json(Path(args.preflight)))
    if args.command == "stage-artifacts":
        preflight = _read_json(Path(args.preflight))
        stage_artifacts(
            Path(args.preflight),
            Path(args.grant),
            args.receipt_stem if args.receipt_stem is not None else preflight.get("receipt_stem"),
            Path(args.output_dir),
        )
        return 0
    if args.command == "summarize-receipt":
        _bounded_json(
            Path(args.output),
            summarize_native_receipt_for_upload(_read_native_receipt_json(Path(args.receipt))),
        )
        return 0
    preflight = _read_json(Path(args.preflight))
    receipt_path = Path(args.receipt) if args.receipt else _receipt_path(preflight.get("receipt_stem"))
    result = validate_native_receipt(_read_native_receipt_json(receipt_path), preflight)
    print(_normalized_json(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
