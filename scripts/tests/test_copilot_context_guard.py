"""Standard-library contract tests for the issue #1995 context-budget guard.

Covers:
  * scripts/copilot_context_guard.py -- the fail-open preToolUse decision
    logic (documented camelCase payload, fall-through rules, cumulative
    dual-signal deny, dedupe/update, environment override, malformed/corrupt
    state handling, sessionId sanitization, OS-kernel file-lock contention,
    atomic writes, TTL cleanup).
  * .github/hooks/copilot-context-budget.json -- hook configuration
    loadability and internal `view`-only dispatch.
  * scripts/copilot-context-guard.sh and scripts/copilot-context-guard.ps1 --
    the platform wrappers, exercised as real subprocesses on the host
    platform(s) where their interpreter is available: usable interpreter,
    missing interpreter, guard exit 1 (crash), guard exit 2 (deny).

Run with:
  python -m unittest discover -s scripts/tests -p "test_copilot_context_guard.py" -v

No third-party dependencies; standard library only.
"""

import io
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCRIPTS_DIR = os.path.join(REPO_ROOT, "scripts")
GUARD_PATH = os.path.join(SCRIPTS_DIR, "copilot_context_guard.py")
HOOK_JSON_PATH = os.path.join(REPO_ROOT, ".github", "hooks", "copilot-context-budget.json")
BASH_WRAPPER_PATH = os.path.join(SCRIPTS_DIR, "copilot-context-guard.sh")
PS1_WRAPPER_PATH = os.path.join(SCRIPTS_DIR, "copilot-context-guard.ps1")

if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import copilot_context_guard as guard  # noqa: E402  (import after sys.path tweak)


def _write_random_file(path, size):
    with open(path, "wb") as fh:
        fh.write(os.urandom(size))


def _payload(session_id="test-session", tool_name="view", tool_args=None, cwd=None):
    return json.dumps({
        "sessionId": session_id,
        "timestamp": int(time.time() * 1000),
        "cwd": cwd or REPO_ROOT,
        "toolName": tool_name,
        "toolArgs": tool_args if tool_args is not None else {},
    })


class GuardTestCase(unittest.TestCase):
    """Base case: isolates guard state into a throwaway temp directory."""

    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="ctxguard-test-")
        self._state_dir = os.path.join(self._tmp, "state")
        self._images_dir = os.path.join(self._tmp, "images")
        os.makedirs(self._images_dir, exist_ok=True)
        self._old_state_override = os.environ.get(guard.STATE_DIR_OVERRIDE_ENV_VAR)
        self._old_budget = os.environ.get(guard.BUDGET_ENV_VAR)
        os.environ[guard.STATE_DIR_OVERRIDE_ENV_VAR] = self._state_dir
        os.environ.pop(guard.BUDGET_ENV_VAR, None)

    def tearDown(self):
        if self._old_state_override is None:
            os.environ.pop(guard.STATE_DIR_OVERRIDE_ENV_VAR, None)
        else:
            os.environ[guard.STATE_DIR_OVERRIDE_ENV_VAR] = self._old_state_override
        if self._old_budget is None:
            os.environ.pop(guard.BUDGET_ENV_VAR, None)
        else:
            os.environ[guard.BUDGET_ENV_VAR] = self._old_budget
        shutil.rmtree(self._tmp, ignore_errors=True)

    def make_image(self, name, size):
        path = os.path.join(self._images_dir, name)
        _write_random_file(path, size)
        return path


class TestFallThrough(GuardTestCase):
    """Non-target/uncertain paths must always abstain with {} and exit 0."""

    def test_non_view_tool_falls_through_even_with_image_path(self):
        big_image = self.make_image("huge.png", 2_000_000)
        output, code = guard.run(_payload(tool_name="bash", tool_args={"path": big_image}))
        self.assertEqual(output, "{}")
        self.assertEqual(code, 0)
        # Must not have recorded anything either.
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        self.assertFalse(os.path.exists(state_path))

    def test_missing_path_falls_through(self):
        output, code = guard.run(_payload(tool_args={}))
        self.assertEqual((output, code), ("{}", 0))

    def test_non_image_extension_falls_through(self):
        text_file = os.path.join(self._images_dir, "notes.txt")
        with open(text_file, "w", encoding="utf-8") as fh:
            fh.write("hello world" * 1000)
        output, code = guard.run(_payload(tool_args={"path": text_file}))
        self.assertEqual((output, code), ("{}", 0))

    def test_nonexistent_image_path_falls_through(self):
        missing = os.path.join(self._images_dir, "does-not-exist.png")
        output, code = guard.run(_payload(tool_args={"path": missing}))
        self.assertEqual((output, code), ("{}", 0))

    def test_malformed_json_stdin_falls_through(self):
        output, code = guard.run("{not valid json")
        self.assertEqual((output, code), ("{}", 0))

    def test_empty_stdin_falls_through(self):
        output, code = guard.run("")
        self.assertEqual((output, code), ("{}", 0))

    def test_non_object_json_falls_through(self):
        output, code = guard.run("[1, 2, 3]")
        self.assertEqual((output, code), ("{}", 0))

    def test_tool_args_as_json_string_is_parsed(self):
        image = self.make_image("a.png", 1000)
        payload = json.dumps({
            "sessionId": "test-session",
            "cwd": REPO_ROOT,
            "toolName": "view",
            "toolArgs": json.dumps({"path": image}),
        })
        output, code = guard.run(payload)
        self.assertEqual((output, code), ("{}", 0))

    def test_unparseable_tool_args_string_falls_through(self):
        payload = json.dumps({
            "sessionId": "test-session",
            "cwd": REPO_ROOT,
            "toolName": "view",
            "toolArgs": "{not json",
        })
        output, code = guard.run(payload)
        self.assertEqual((output, code), ("{}", 0))

    def test_tool_args_wrong_type_falls_through(self):
        payload = json.dumps({
            "sessionId": "test-session",
            "cwd": REPO_ROOT,
            "toolName": "view",
            "toolArgs": 12345,
        })
        output, code = guard.run(payload)
        self.assertEqual((output, code), ("{}", 0))


class TestBudgetEnforcement(GuardTestCase):
    def test_first_image_is_authorized_and_recorded(self):
        image = self.make_image("first.png", 900_000)
        output, code = guard.run(_payload(tool_args={"path": image}))
        self.assertEqual((output, code), ("{}", 0))
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        self.assertTrue(os.path.exists(state_path))
        with open(state_path) as fh:
            state = json.load(fh)
        self.assertEqual(state["total_bytes"], 900_000)
        self.assertEqual(len(state["entries"]), 1)

    def test_cumulative_overflow_denies_with_reason_and_exit_2(self):
        image1 = self.make_image("first.png", 900_000)
        image2 = self.make_image("second.png", 900_000)
        out1, code1 = guard.run(_payload(tool_args={"path": image1}))
        self.assertEqual((out1, code1), ("{}", 0))

        out2, code2 = guard.run(_payload(tool_args={"path": image2}))
        self.assertEqual(code2, 2)
        decision = json.loads(out2)
        self.assertEqual(decision["permissionDecision"], "deny")
        self.assertTrue(decision["permissionDecisionReason"])
        self.assertIn("1995", decision["permissionDecisionReason"])

        # The denied read must not be persisted as authorized.
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        with open(state_path) as fh:
            state = json.load(fh)
        self.assertEqual(state["total_bytes"], 900_000)
        self.assertEqual(len(state["entries"]), 1)

    def test_dedupe_does_not_double_count_identical_read(self):
        image = self.make_image("same.png", 900_000)
        guard.run(_payload(tool_args={"path": image}))
        out2, code2 = guard.run(_payload(tool_args={"path": image}))
        self.assertEqual((out2, code2), ("{}", 0))
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        with open(state_path) as fh:
            state = json.load(fh)
        self.assertEqual(state["total_bytes"], 900_000)
        self.assertEqual(len(state["entries"]), 1)

    def test_dedupe_updates_last_seen_metadata(self):
        image = self.make_image("touch.png", 1000)
        guard.run(_payload(tool_args={"path": image}))
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        with open(state_path) as fh:
            first_state = json.load(fh)
        first_seen = next(iter(first_state["entries"].values()))["last_seen"]

        time.sleep(0.01)
        guard.run(_payload(tool_args={"path": image}))
        with open(state_path) as fh:
            second_state = json.load(fh)
        second_seen = next(iter(second_state["entries"].values()))["last_seen"]
        self.assertGreaterEqual(second_seen, first_seen)

    def test_modified_file_is_treated_as_new_fingerprint(self):
        image = self.make_image("changes.png", 1000)
        guard.run(_payload(tool_args={"path": image}))
        time.sleep(0.01)
        _write_random_file(image, 1500)  # new size + new mtime => new fingerprint
        out2, code2 = guard.run(_payload(tool_args={"path": image}))
        self.assertEqual((out2, code2), ("{}", 0))
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        with open(state_path) as fh:
            state = json.load(fh)
        self.assertEqual(len(state["entries"]), 2)
        self.assertEqual(state["total_bytes"], 1000 + 1500)

    def test_environment_override_changes_budget(self):
        os.environ[guard.BUDGET_ENV_VAR] = "500000"
        try:
            image = self.make_image("over.png", 600_000)
            output, code = guard.run(_payload(tool_args={"path": image}))
            self.assertEqual(code, 2)
            decision = json.loads(output)
            self.assertEqual(decision["permissionDecision"], "deny")
        finally:
            os.environ.pop(guard.BUDGET_ENV_VAR, None)

    def test_invalid_environment_override_falls_back_to_default(self):
        os.environ[guard.BUDGET_ENV_VAR] = "not-a-number"
        try:
            self.assertEqual(guard._budget_bytes(), guard.DEFAULT_BUDGET_BYTES)
        finally:
            os.environ.pop(guard.BUDGET_ENV_VAR, None)


class TestCorruptAndContendedState(GuardTestCase):
    def test_corrupt_state_file_falls_through_without_crashing(self):
        image = self.make_image("a.png", 1000)
        os.makedirs(self._state_dir, exist_ok=True)
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        with open(state_path, "w", encoding="utf-8") as fh:
            fh.write("{not valid json at all")

        output, code = guard.run(_payload(tool_args={"path": image}))
        self.assertEqual((output, code), ("{}", 0))
        # Corrupt file must be left untouched, not silently "fixed".
        with open(state_path, encoding="utf-8") as fh:
            self.assertEqual(fh.read(), "{not valid json at all")

    def test_state_file_with_wrong_schema_is_treated_as_corrupt(self):
        image = self.make_image("a.png", 1000)
        os.makedirs(self._state_dir, exist_ok=True)
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)
        with open(state_path, "w", encoding="utf-8") as fh:
            json.dump({"unexpected": "shape"}, fh)

        output, code = guard.run(_payload(tool_args={"path": image}))
        self.assertEqual((output, code), ("{}", 0))

    def test_lock_contention_times_out_and_falls_through(self):
        image = self.make_image("a.png", 1000)
        os.makedirs(self._state_dir, exist_ok=True)
        state_path = os.path.join(self._state_dir, guard.STATE_FILE_NAME)

        original_max_wait = guard.LOCK_MAX_WAIT_SECONDS
        guard.LOCK_MAX_WAIT_SECONDS = 0.2
        try:
            # Hold the OS-kernel lock via a second, independent open() of the
            # same lock file (same process, different file descriptor/handle
            # -- flock/msvcrt.locking contend on the open file description
            # or handle, not on the process, so this genuinely contends).
            with guard._FileLock(state_path):
                start = time.monotonic()
                output, code = guard.run(_payload(tool_args={"path": image}))
                elapsed = time.monotonic() - start
        finally:
            guard.LOCK_MAX_WAIT_SECONDS = original_max_wait

        self.assertEqual((output, code), ("{}", 0))
        self.assertLess(elapsed, 5.0)


_LOCK_HOLDER_SCRIPT = """
import os
import sys
import time

sys.path.insert(0, {scripts_dir!r})
import copilot_context_guard as guard

target_path = sys.argv[1]
ready_marker = sys.argv[2]
hold_seconds = float(sys.argv[3])
crash = len(sys.argv) > 4 and sys.argv[4] == "crash"

lock = guard._FileLock(target_path)
lock.__enter__()
with open(ready_marker, "w", encoding="utf-8") as fh:
    fh.write("ready")

time.sleep(hold_seconds)

if crash:
    # Simulate an unclean crash/kill: bypass __exit__ entirely (no explicit
    # unlock, no normal interpreter shutdown handlers) and terminate the
    # process immediately. The OS must still release the kernel lock as
    # part of tearing down the process's open file descriptors/handles.
    os._exit(1)
else:
    lock.__exit__(None, None, None)
"""


class TestFileLockCrossProcess(unittest.TestCase):
    """Real cross-process contention coverage for the OS-kernel file lock.

    Regression coverage for the stale-lock race this replaces: a directory
    lock with a getmtime-then-rmtree staleness check can delete a lock that
    a legitimate holder just recreated/refreshed. The kernel-lock design has
    no staleness window at all -- these tests prove that directly against a
    real second process, on whichever platform the suite runs on (POSIX
    ``fcntl.flock`` / Windows ``msvcrt.locking``, selected internally by
    ``_FileLock``).
    """

    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="ctxguard-filelock-")
        self._target = os.path.join(self._tmp, "state")
        self._ready_marker = os.path.join(self._tmp, "ready")
        self._holder_script = os.path.join(self._tmp, "holder.py")
        with open(self._holder_script, "w", encoding="utf-8") as fh:
            fh.write(_LOCK_HOLDER_SCRIPT.format(scripts_dir=SCRIPTS_DIR))
        self._original_max_wait = guard.LOCK_MAX_WAIT_SECONDS

    def tearDown(self):
        guard.LOCK_MAX_WAIT_SECONDS = self._original_max_wait
        shutil.rmtree(self._tmp, ignore_errors=True)

    def _spawn_holder(self, hold_seconds, crash=False):
        args = [
            sys.executable, self._holder_script, self._target,
            self._ready_marker, str(hold_seconds),
        ]
        if crash:
            args.append("crash")
        proc = subprocess.Popen(args)
        deadline = time.monotonic() + 10.0
        while not os.path.exists(self._ready_marker):
            if time.monotonic() > deadline:
                proc.kill()
                proc.wait(timeout=10)
                raise AssertionError("holder subprocess never signaled ready")
            time.sleep(0.02)
        return proc

    def _acquire_with_retry(self, overall_timeout=5.0):
        deadline = time.monotonic() + overall_timeout
        while time.monotonic() < deadline:
            try:
                with guard._FileLock(self._target):
                    return True
            except guard._LockNotAcquired:
                time.sleep(0.05)
        return False

    def test_second_process_cannot_enter_while_held(self):
        guard.LOCK_MAX_WAIT_SECONDS = 0.3
        proc = self._spawn_holder(hold_seconds=2.0)
        try:
            with self.assertRaises(guard._LockNotAcquired):
                with guard._FileLock(self._target):
                    pass  # pragma: no cover -- must not be reached
        finally:
            proc.wait(timeout=10)

    def test_lock_is_acquirable_after_holder_exits_normally(self):
        proc = self._spawn_holder(hold_seconds=0.3)
        proc.wait(timeout=10)
        self.assertEqual(proc.returncode, 0)
        self.assertTrue(
            self._acquire_with_retry(),
            "expected to acquire the lock once the holder released it normally",
        )

    def test_lock_is_acquirable_after_holder_crashes(self):
        proc = self._spawn_holder(hold_seconds=0.3, crash=True)
        proc.wait(timeout=10)
        self.assertEqual(proc.returncode, 1)
        self.assertTrue(
            self._acquire_with_retry(),
            "expected the OS to release the kernel lock when the holder "
            "process crashed (os._exit) without ever calling __exit__",
        )


class TestSanitizationAndFingerprint(unittest.TestCase):
    def test_sanitize_session_id_strips_unsafe_characters(self):
        unsafe = "../../etc/passwd; rm -rf /"
        safe = guard._sanitize_session_id(unsafe)
        self.assertNotIn("/", safe)
        self.assertNotIn(" ", safe)
        self.assertNotIn(";", safe)

    def test_sanitize_session_id_handles_empty_and_non_string(self):
        self.assertEqual(guard._sanitize_session_id(""), "unknown-session")
        self.assertEqual(guard._sanitize_session_id(None), "unknown-session")
        self.assertEqual(guard._sanitize_session_id(12345), "unknown-session")

    def test_sanitize_session_id_truncates_long_ids(self):
        long_id = "a" * 5000
        safe = guard._sanitize_session_id(long_id)
        self.assertLessEqual(len(safe), 128)

    def test_fingerprint_is_case_normalized_on_windows(self):
        if not sys.platform.startswith("win"):
            self.skipTest("case-insensitive fingerprinting only applies on Windows")
        fp_lower = guard._normalized_fingerprint("/tmp/Foo.PNG", 10, 123)
        fp_mixed = guard._normalized_fingerprint("/TMP/foo.png", 10, 123)
        self.assertEqual(fp_lower, fp_mixed)

    def test_fingerprint_is_case_sensitive_on_non_windows(self):
        if sys.platform.startswith("win"):
            self.skipTest("this platform folds case; see the Windows-only counterpart above")
        # Conservative double-counting (treating differently-cased paths as
        # distinct) is preferable to undercounting on case-sensitive
        # filesystems (default on Linux; possible on macOS/APFS), so no
        # case-folding should happen on Darwin or Linux.
        fp_lower = guard._normalized_fingerprint("/tmp/Foo.PNG", 10, 123)
        fp_mixed = guard._normalized_fingerprint("/tmp/foo.png", 10, 123)
        self.assertNotEqual(fp_lower, fp_mixed)


class TestImageExtensions(GuardTestCase):
    def test_heic_and_heif_are_supported_image_extensions(self):
        self.assertIn(".heic", guard.SUPPORTED_IMAGE_EXTENSIONS)
        self.assertIn(".heif", guard.SUPPORTED_IMAGE_EXTENSIONS)

    def test_heic_file_is_tracked_toward_the_budget(self):
        os.environ[guard.BUDGET_ENV_VAR] = "100"
        try:
            heic = self.make_image("photo.HEIC", 5000)
            output, code = guard.run(_payload(tool_args={"path": heic}))
            self.assertEqual(code, 2)
            decision = json.loads(output)
            self.assertEqual(decision["permissionDecision"], "deny")
        finally:
            os.environ.pop(guard.BUDGET_ENV_VAR, None)


class TestUserPartitionedFallback(unittest.TestCase):
    def test_current_user_token_is_stable_and_nonempty(self):
        token = guard._current_user_token()
        self.assertIsInstance(token, str)
        self.assertTrue(token)
        self.assertEqual(token, guard._current_user_token())

    def test_fallback_state_dir_is_partitioned_by_user_token(self):
        fake_home = tempfile.mkdtemp(prefix="ctxguard-home-")
        try:
            old_home = os.environ.get("COPILOT_HOME")
            old_override = os.environ.pop(guard.STATE_DIR_OVERRIDE_ENV_VAR, None)
            os.environ["COPILOT_HOME"] = fake_home
            try:
                state_dir, is_fallback = guard._resolve_state_dir("never-created-session")
                self.assertTrue(is_fallback)
                token = guard._current_user_token()
                # The token is embedded directly in the top-level fallback
                # root's own directory name (e.g.
                # "copilot-context-guard-uid-1000"), not as a standalone
                # path component -- so this must be a substring check of
                # some path component, not an exact-segment membership
                # check.
                self.assertTrue(
                    any(token in part for part in state_dir.split(os.sep)),
                    "expected user token {0!r} embedded in some component of "
                    "{1!r}".format(token, state_dir),
                )
            finally:
                if old_home is None:
                    os.environ.pop("COPILOT_HOME", None)
                else:
                    os.environ["COPILOT_HOME"] = old_home
                if old_override is not None:
                    os.environ[guard.STATE_DIR_OVERRIDE_ENV_VAR] = old_override
        finally:
            shutil.rmtree(fake_home, ignore_errors=True)

    def test_user_fallback_root_name_is_user_specific_not_a_shared_subdir(self):
        root = guard._user_fallback_root()
        token = guard._current_user_token()
        self.assertIn(token, os.path.basename(root))
        # Regression guard: the bare, non-user-specific subdir name must
        # never itself be the fallback root -- that was the shared-parent
        # layout that let one local user's chmod 0700 lock out every other
        # UID sharing the same OS temp directory.
        self.assertNotEqual(os.path.basename(root), guard.FALLBACK_SUBDIR_NAME)
        shared_root = os.path.join(tempfile.gettempdir(), guard.FALLBACK_SUBDIR_NAME)
        self.assertNotEqual(root, shared_root)

    @unittest.skipIf(sys.platform.startswith("win"), "POSIX-only permission tightening")
    def test_makedirs_private_tightens_permissions_to_0700(self):
        root = tempfile.mkdtemp(prefix="ctxguard-private-")
        try:
            target = os.path.join(root, "a", "b", "c")
            self.assertTrue(guard._makedirs_private(target, root))
            for component in (target, os.path.join(root, "a", "b"), os.path.join(root, "a")):
                mode = stat.S_IMODE(os.stat(component).st_mode)
                self.assertEqual(mode, 0o700, "expected 0700 on {0}".format(component))
            # Must never touch stop_at (root) itself's permissions upward,
            # i.e. it stops walking exactly at stop_at (root is included,
            # nothing above it is touched -- there's nothing above root in
            # this test, so just assert root itself still exists untouched
            # in a way that would surface an exception if walk-up broke).
            self.assertTrue(os.path.isdir(root))
        finally:
            shutil.rmtree(root, ignore_errors=True)

    @unittest.skipIf(sys.platform.startswith("win"), "POSIX-only permission tightening")
    def test_private_stop_at_is_user_specific_and_shared_temp_root_untouched(self):
        """Regression test for the shared-parent chmod-lockout bug.

        The buggy layout used a shared ``<tmp>/copilot-context-guard``
        parent as ``_makedirs_private``'s ``stop_at``: the first local user
        to run the guard would ``chmod 0700`` that shared parent, silently
        denying every *other* local UID write access to create their own
        subdirectory beneath it. This asserts, for two distinct simulated
        users sharing one fake OS temp root, that: (1) each user's private
        stop_at/root embeds that user's own token, (2) both users can
        independently create and privatize their own fallback state
        without interfering with each other, and (3) the actual shared OS
        temp root itself is never a chmod target.
        """
        fake_tmp = tempfile.mkdtemp(prefix="ctxguard-faketmp-")
        try:
            original_tmp_mode = stat.S_IMODE(os.stat(fake_tmp).st_mode)
            original_gettempdir = guard.tempfile.gettempdir
            original_token_fn = guard._current_user_token
            guard.tempfile.gettempdir = lambda: fake_tmp
            try:
                for token, session in (("uid-1111", "session-a"), ("uid-2222", "session-b")):
                    guard._current_user_token = lambda t=token: t
                    root = guard._user_fallback_root()
                    self.assertIn(token, os.path.basename(root))
                    self.assertNotEqual(os.path.basename(root), guard.FALLBACK_SUBDIR_NAME)

                    state_dir = os.path.join(root, session)
                    self.assertTrue(
                        guard._makedirs_private(state_dir, root),
                        "user {0!r} must be able to create its own fallback "
                        "state dir independently of any other user's".format(token),
                    )
                    self.assertEqual(stat.S_IMODE(os.stat(root).st_mode), 0o700)
                    self.assertEqual(stat.S_IMODE(os.stat(state_dir).st_mode), 0o700)

                # The shared OS temp root itself must never be chmod-targeted.
                self.assertEqual(
                    stat.S_IMODE(os.stat(fake_tmp).st_mode), original_tmp_mode
                )
            finally:
                guard.tempfile.gettempdir = original_gettempdir
                guard._current_user_token = original_token_fn
        finally:
            os.chmod(fake_tmp, 0o700)
            shutil.rmtree(fake_tmp, ignore_errors=True)

    def test_makedirs_private_returns_false_on_creation_failure(self):
        if sys.platform.startswith("win"):
            self.skipTest("permission-based makedirs failure is unreliable to force on Windows")
        root = tempfile.mkdtemp(prefix="ctxguard-private-fail-")
        try:
            os.chmod(root, 0o500)  # read+execute only, no write: cannot create children
            target = os.path.join(root, "nope", "deeper")
            self.assertFalse(guard._makedirs_private(target, root))
        finally:
            os.chmod(root, 0o700)
            shutil.rmtree(root, ignore_errors=True)


class TestAtomicSave(unittest.TestCase):
    def test_save_state_atomically_leaves_no_temp_files(self):
        tmp_dir = tempfile.mkdtemp(prefix="ctxguard-atomic-")
        try:
            state_path = os.path.join(tmp_dir, guard.STATE_FILE_NAME)
            guard._save_state_atomically(tmp_dir, state_path, {"entries": {}, "total_bytes": 42})
            self.assertTrue(os.path.exists(state_path))
            leftovers = [f for f in os.listdir(tmp_dir) if f.startswith(".tmp-")]
            self.assertEqual(leftovers, [])
            with open(state_path) as fh:
                self.assertEqual(json.load(fh)["total_bytes"], 42)
        finally:
            shutil.rmtree(tmp_dir, ignore_errors=True)


class TestTtlCleanup(unittest.TestCase):
    def test_ttl_cleanup_removes_stale_fallback_entries_only(self):
        fallback_root = tempfile.mkdtemp(prefix="ctxguard-fallback-")
        try:
            stale_dir = os.path.join(fallback_root, "stale-session")
            fresh_dir = os.path.join(fallback_root, "fresh-session")
            os.makedirs(stale_dir)
            os.makedirs(fresh_dir)
            old_time = time.time() - (guard.TEMP_STATE_TTL_SECONDS + 3600)
            os.utime(stale_dir, (old_time, old_time))

            guard._cleanup_stale_temp_state(fallback_root)

            self.assertFalse(os.path.exists(stale_dir))
            self.assertTrue(os.path.exists(fresh_dir))
        finally:
            shutil.rmtree(fallback_root, ignore_errors=True)

    def test_ttl_cleanup_on_missing_root_is_a_no_op(self):
        # Must never raise even if the fallback root does not exist yet.
        guard._cleanup_stale_temp_state(os.path.join(tempfile.gettempdir(), "definitely-not-there-12345"))


class TestStateDirResolution(unittest.TestCase):
    def test_resolves_to_real_session_dir_when_present(self):
        fake_home = tempfile.mkdtemp(prefix="ctxguard-home-")
        try:
            session_id = "abc123"
            session_dir = os.path.join(fake_home, "session-state", session_id)
            os.makedirs(session_dir, exist_ok=True)
            old_home = os.environ.get("COPILOT_HOME")
            old_override = os.environ.pop(guard.STATE_DIR_OVERRIDE_ENV_VAR, None)
            os.environ["COPILOT_HOME"] = fake_home
            try:
                state_dir, is_fallback = guard._resolve_state_dir(session_id)
                self.assertFalse(is_fallback)
                self.assertTrue(state_dir.startswith(session_dir))
                # Must never resolve inside a git worktree's "files" dir.
                self.assertNotIn("files", state_dir.split(os.sep))
            finally:
                if old_home is None:
                    os.environ.pop("COPILOT_HOME", None)
                else:
                    os.environ["COPILOT_HOME"] = old_home
                if old_override is not None:
                    os.environ[guard.STATE_DIR_OVERRIDE_ENV_VAR] = old_override
        finally:
            shutil.rmtree(fake_home, ignore_errors=True)

    def test_falls_back_to_temp_when_session_dir_unresolvable(self):
        fake_home = tempfile.mkdtemp(prefix="ctxguard-home-")
        try:
            old_home = os.environ.get("COPILOT_HOME")
            old_override = os.environ.pop(guard.STATE_DIR_OVERRIDE_ENV_VAR, None)
            os.environ["COPILOT_HOME"] = fake_home
            try:
                state_dir, is_fallback = guard._resolve_state_dir("never-created-session")
                self.assertTrue(is_fallback)
                self.assertTrue(state_dir.startswith(tempfile.gettempdir()))
            finally:
                if old_home is None:
                    os.environ.pop("COPILOT_HOME", None)
                else:
                    os.environ["COPILOT_HOME"] = old_home
                if old_override is not None:
                    os.environ[guard.STATE_DIR_OVERRIDE_ENV_VAR] = old_override
        finally:
            shutil.rmtree(fake_home, ignore_errors=True)


class TestSubprocessContract(GuardTestCase):
    """A handful of exit-code/stdout contract checks via a real subprocess."""

    def _run_guard_subprocess(self, payload_text):
        env = dict(os.environ)
        result = subprocess.run(
            [sys.executable, GUARD_PATH],
            input=payload_text,
            capture_output=True,
            text=True,
            env=env,
            timeout=30,
        )
        return result.stdout, result.returncode

    def test_subprocess_fall_through_exit_0(self):
        out, code = self._run_guard_subprocess(_payload(tool_name="bash"))
        self.assertEqual((out, code), ("{}", 0))

    def test_subprocess_deny_exit_2(self):
        os.environ[guard.BUDGET_ENV_VAR] = "100"
        try:
            image = self.make_image("big.png", 5000)
            out, code = self._run_guard_subprocess(_payload(tool_args={"path": image}))
            self.assertEqual(code, 2)
            decision = json.loads(out)
            self.assertEqual(decision["permissionDecision"], "deny")
            self.assertTrue(decision["permissionDecisionReason"])
        finally:
            os.environ.pop(guard.BUDGET_ENV_VAR, None)

    def test_subprocess_never_raises_traceback_on_stdout_for_bad_input(self):
        out, code = self._run_guard_subprocess("this is not json at all {{{")
        self.assertEqual((out, code), ("{}", 0))


class TestHookConfigLoadability(unittest.TestCase):
    def test_hook_json_is_valid_and_uses_documented_shape(self):
        with open(HOOK_JSON_PATH, encoding="utf-8") as fh:
            data = json.load(fh)
        self.assertEqual(data.get("version"), 1)
        pre_tool_use = data["hooks"]["preToolUse"]
        self.assertIsInstance(pre_tool_use, list)
        self.assertEqual(len(pre_tool_use), 1)
        entry = pre_tool_use[0]
        self.assertEqual(entry.get("type"), "command")
        self.assertIn("bash", entry)
        self.assertIn("powershell", entry)
        # preToolUse hook-level "matcher" filtering is intentionally not
        # relied upon; the guard script itself filters toolName == "view"
        # so behavior does not depend on runtime matcher support.
        self.assertNotIn("matcher", entry)

    def test_settings_json_ships_and_sets_context_tier_default(self):
        # Installed-runtime evidence shows repo scope overrides user scope
        # in the settings merge order (user -> repo -> local), and official
        # docs confirm repo-level contextTier takes precedence in trusted
        # working directories. This file must exist and set exactly this.
        settings_path = os.path.join(REPO_ROOT, ".github", "copilot", "settings.json")
        self.assertTrue(
            os.path.exists(settings_path),
            "expected .github/copilot/settings.json to be shipped so this "
            "repo's trusted checkouts get contextTier=default by default",
        )
        with open(settings_path, encoding="utf-8") as fh:
            data = json.load(fh)
        self.assertEqual(data, {"contextTier": "default"})


def _find_real_bash():
    """Locate a genuine, runnable bash -- never the disabled WSL PATH stub.

    On this repo's dev hosts (and many corporate Windows images), ``bash``
    on PATH can resolve to ``%WINDIR%\\system32\\bash.exe``, a WSL launcher
    stub that is disabled by group policy and fails non-interactively. Git
    for Windows ships a real, always-usable bash; prefer it explicitly.
    """
    if sys.platform.startswith("win"):
        candidates = [
            r"C:\Program Files\Git\bin\bash.exe",
            r"C:\Program Files (x86)\Git\bin\bash.exe",
        ]
        for candidate in candidates:
            if os.path.isfile(candidate):
                return candidate
        return None
    return shutil.which("bash")


BASH_EXECUTABLE = _find_real_bash()


@unittest.skipUnless(BASH_EXECUTABLE, "no usable (non-WSL-stub) bash available on this host")
class TestBashWrapper(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="ctxguard-bashwrap-")

    def tearDown(self):
        shutil.rmtree(self._tmp, ignore_errors=True)

    def _run(self, payload_text, env=None):
        run_env = dict(os.environ)
        run_env[guard.STATE_DIR_OVERRIDE_ENV_VAR] = self._tmp
        if env:
            run_env.update(env)
        result = subprocess.run(
            [BASH_EXECUTABLE, BASH_WRAPPER_PATH],
            input=payload_text,
            capture_output=True,
            text=True,
            env=run_env,
            timeout=30,
        )
        return result.stdout, result.returncode

    def test_usable_interpreter_falls_through(self):
        out, code = self._run(_payload(tool_name="bash"))
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_missing_interpreter_maps_to_exit_0(self):
        python_dirs = set()
        for name in ("python3", "python"):
            found = shutil.which(name)
            if found:
                python_dirs.add(os.path.dirname(found))
        original_path = os.environ.get("PATH", "")
        sep = os.pathsep
        filtered = sep.join(
            part for part in original_path.split(sep)
            if os.path.normcase(os.path.normpath(part)) not in
            {os.path.normcase(os.path.normpath(d)) for d in python_dirs}
        )
        out, code = self._run(_payload(tool_name="bash"), env={"PATH": filtered})
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_crash_exit_1_maps_to_exit_0(self):
        crashing_guard = os.path.join(self._tmp, "crashing_guard.py")
        with open(crashing_guard, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.exit(1)\n")
        wrapper_copy = os.path.join(self._tmp, "wrapper.sh")
        with open(BASH_WRAPPER_PATH, encoding="utf-8") as fh:
            content = fh.read()
        # Point the wrapper at a stand-in guard that always exits 1.
        content = content.replace(
            'guard="$hook_dir/copilot_context_guard.py"',
            'guard="{0}"'.format(crashing_guard.replace("\\", "/")),
        )
        with open(wrapper_copy, "w", encoding="utf-8") as fh:
            fh.write(content)
        run_env = dict(os.environ)
        run_env[guard.STATE_DIR_OVERRIDE_ENV_VAR] = self._tmp
        result = subprocess.run(
            [BASH_EXECUTABLE, wrapper_copy], input="{}", capture_output=True, text=True,
            env=run_env, timeout=30,
        )
        self.assertEqual((result.stdout.strip(), result.returncode), ("{}", 0))

    def test_guard_exit_2_propagates(self):
        denying_guard = os.path.join(self._tmp, "denying_guard.py")
        with open(denying_guard, "w", encoding="utf-8") as fh:
            fh.write(
                "import sys\n"
                "sys.stdout.write('{\"permissionDecision\": \"deny\", "
                "\"permissionDecisionReason\": \"test\"}')\n"
                "sys.exit(2)\n"
            )
        wrapper_copy = os.path.join(self._tmp, "wrapper.sh")
        with open(BASH_WRAPPER_PATH, encoding="utf-8") as fh:
            content = fh.read()
        content = content.replace(
            'guard="$hook_dir/copilot_context_guard.py"',
            'guard="{0}"'.format(denying_guard.replace("\\", "/")),
        )
        with open(wrapper_copy, "w", encoding="utf-8") as fh:
            fh.write(content)
        run_env = dict(os.environ)
        result = subprocess.run(
            [BASH_EXECUTABLE, wrapper_copy], input="{}", capture_output=True, text=True,
            env=run_env, timeout=30,
        )
        self.assertEqual(result.returncode, 2)
        decision = json.loads(result.stdout)
        self.assertEqual(decision["permissionDecision"], "deny")

    def _run_with_stand_in_guard(self, guard_path):
        wrapper_copy = os.path.join(self._tmp, "wrapper.sh")
        with open(BASH_WRAPPER_PATH, encoding="utf-8") as fh:
            content = fh.read()
        content = content.replace(
            'guard="$hook_dir/copilot_context_guard.py"',
            'guard="{0}"'.format(guard_path.replace("\\", "/")),
        )
        with open(wrapper_copy, "w", encoding="utf-8") as fh:
            fh.write(content)
        run_env = dict(os.environ)
        run_env[guard.STATE_DIR_OVERRIDE_ENV_VAR] = self._tmp
        result = subprocess.run(
            [BASH_EXECUTABLE, wrapper_copy], input="{}", capture_output=True, text=True,
            env=run_env, timeout=30,
        )
        return result.stdout, result.returncode

    def test_missing_guard_script_maps_to_exit_0(self):
        # CPython itself exits 2 (not the guard's own decision logic) when
        # asked to run a nonexistent script -- must not be mistaken for a
        # real deny.
        missing = os.path.join(self._tmp, "does-not-exist.py")
        out, code = self._run_with_stand_in_guard(missing)
        self.assertEqual((out.strip(), code), ("{}", 0))

    @unittest.skipIf(sys.platform.startswith("win"), "POSIX file-mode permissions only")
    def test_unreadable_guard_script_maps_to_exit_0(self):
        unreadable = os.path.join(self._tmp, "unreadable_guard.py")
        with open(unreadable, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.exit(0)\n")
        os.chmod(unreadable, 0o000)
        try:
            out, code = self._run_with_stand_in_guard(unreadable)
            self.assertEqual((out.strip(), code), ("{}", 0))
        finally:
            os.chmod(unreadable, 0o700)

    def test_guard_exit_2_with_invalid_json_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.stdout.write('not json at all')\nsys.exit(2)\n")
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_wrong_decision_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write(
                "import sys\n"
                "sys.stdout.write('{\"permissionDecision\": \"allow\", "
                "\"permissionDecisionReason\": \"test\"}')\n"
                "sys.exit(2)\n"
            )
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_empty_reason_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write(
                "import sys\n"
                "sys.stdout.write('{\"permissionDecision\": \"deny\", "
                "\"permissionDecisionReason\": \"\"}')\n"
                "sys.exit(2)\n"
            )
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_empty_stdout_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.exit(2)\n")
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))


def _pwsh_executable():
    for candidate in ("pwsh", "powershell"):
        found = shutil.which(candidate)
        if found:
            return candidate
    return None


@unittest.skipUnless(sys.platform.startswith("win"), "PowerShell wrapper is exercised on Windows hosts")
@unittest.skipUnless(_pwsh_executable(), "no PowerShell executable available on this host")
class TestPowerShellWrapper(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="ctxguard-pswrap-")
        self._pwsh = _pwsh_executable()

    def tearDown(self):
        shutil.rmtree(self._tmp, ignore_errors=True)

    def _run(self, payload_text, script_path=None, env=None):
        run_env = dict(os.environ)
        run_env[guard.STATE_DIR_OVERRIDE_ENV_VAR] = self._tmp
        if env:
            run_env.update(env)
        target = script_path or PS1_WRAPPER_PATH
        result = subprocess.run(
            [self._pwsh, "-NoProfile", "-NonInteractive", "-File", target],
            input=payload_text,
            capture_output=True,
            text=True,
            env=run_env,
            timeout=30,
        )
        return result.stdout, result.returncode

    def test_usable_interpreter_falls_through(self):
        out, code = self._run(_payload(tool_name="bash"))
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_missing_interpreter_maps_to_exit_0(self):
        python_dirs = set()
        for name in ("python3", "python", "py"):
            found = shutil.which(name)
            if found:
                python_dirs.add(os.path.dirname(found))
        original_path = os.environ.get("PATH", "")
        sep = os.pathsep
        filtered = sep.join(
            part for part in original_path.split(sep)
            if os.path.normcase(os.path.normpath(part)) not in
            {os.path.normcase(os.path.normpath(d)) for d in python_dirs}
        )
        out, code = self._run(_payload(tool_name="bash"), env={"PATH": filtered})
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_crash_exit_1_maps_to_exit_0(self):
        crashing_guard = os.path.join(self._tmp, "crashing_guard.py")
        with open(crashing_guard, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.exit(1)\n")
        wrapper_copy = os.path.join(self._tmp, "wrapper.ps1")
        with open(PS1_WRAPPER_PATH, encoding="utf-8") as fh:
            content = fh.read()
        content = content.replace(
            "$guard = Join-Path $hookDir 'copilot_context_guard.py'",
            "$guard = '{0}'".format(crashing_guard.replace("\\", "\\\\")),
        )
        with open(wrapper_copy, "w", encoding="utf-8") as fh:
            fh.write(content)
        out, code = self._run("{}", script_path=wrapper_copy)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_propagates(self):
        denying_guard = os.path.join(self._tmp, "denying_guard.py")
        with open(denying_guard, "w", encoding="utf-8") as fh:
            fh.write(
                "import sys\n"
                "sys.stdout.write('{\"permissionDecision\": \"deny\", "
                "\"permissionDecisionReason\": \"test\"}')\n"
                "sys.exit(2)\n"
            )
        wrapper_copy = os.path.join(self._tmp, "wrapper.ps1")
        with open(PS1_WRAPPER_PATH, encoding="utf-8") as fh:
            content = fh.read()
        content = content.replace(
            "$guard = Join-Path $hookDir 'copilot_context_guard.py'",
            "$guard = '{0}'".format(denying_guard.replace("\\", "\\\\")),
        )
        with open(wrapper_copy, "w", encoding="utf-8") as fh:
            fh.write(content)
        out, code = self._run("{}", script_path=wrapper_copy)
        self.assertEqual(code, 2)
        decision = json.loads(out)
        self.assertEqual(decision["permissionDecision"], "deny")

    def _run_with_stand_in_guard(self, guard_path):
        wrapper_copy = os.path.join(self._tmp, "wrapper.ps1")
        with open(PS1_WRAPPER_PATH, encoding="utf-8") as fh:
            content = fh.read()
        content = content.replace(
            "$guard = Join-Path $hookDir 'copilot_context_guard.py'",
            "$guard = '{0}'".format(guard_path.replace("\\", "\\\\")),
        )
        with open(wrapper_copy, "w", encoding="utf-8") as fh:
            fh.write(content)
        return self._run("{}", script_path=wrapper_copy)

    def test_missing_guard_script_maps_to_exit_0(self):
        # CPython itself exits 2 (not the guard's own decision logic) when
        # asked to run a nonexistent script -- must not be mistaken for a
        # real deny.
        missing = os.path.join(self._tmp, "does-not-exist.py")
        out, code = self._run_with_stand_in_guard(missing)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_invalid_json_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.stdout.write('not json at all')\nsys.exit(2)\n")
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_wrong_decision_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write(
                "import sys\n"
                "sys.stdout.write('{\"permissionDecision\": \"allow\", "
                "\"permissionDecisionReason\": \"test\"}')\n"
                "sys.exit(2)\n"
            )
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_empty_reason_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write(
                "import sys\n"
                "sys.stdout.write('{\"permissionDecision\": \"deny\", "
                "\"permissionDecisionReason\": \"\"}')\n"
                "sys.exit(2)\n"
            )
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))

    def test_guard_exit_2_with_empty_stdout_maps_to_exit_0(self):
        bad_guard = os.path.join(self._tmp, "bad_guard.py")
        with open(bad_guard, "w", encoding="utf-8") as fh:
            fh.write("import sys\nsys.exit(2)\n")
        out, code = self._run_with_stand_in_guard(bad_guard)
        self.assertEqual((out.strip(), code), ("{}", 0))


class TestMainEntrypoint(unittest.TestCase):
    """Exercise main()'s own stdin-read/catch-all fail-open guarantees."""

    def test_main_never_raises_and_emits_fall_through_on_bad_stdin(self):
        old_stdin = sys.stdin
        old_stdout = sys.stdout
        try:
            sys.stdin = io.StringIO("not json {{{")
            captured = io.StringIO()
            sys.stdout = captured
            exit_code = guard.main()
        finally:
            sys.stdin = old_stdin
            sys.stdout = old_stdout
        self.assertEqual(exit_code, 0)
        self.assertEqual(captured.getvalue(), "{}")

    def test_main_fails_open_when_run_raises_unexpectedly(self):
        old_stdin = sys.stdin
        old_stdout = sys.stdout
        original_run = guard.run
        try:
            sys.stdin = io.StringIO("{}")
            captured = io.StringIO()
            sys.stdout = captured

            def _boom(_text):
                raise RuntimeError("simulated internal failure")

            guard.run = _boom
            exit_code = guard.main()
        finally:
            guard.run = original_run
            sys.stdin = old_stdin
            sys.stdout = old_stdout
        self.assertEqual(exit_code, 0)
        self.assertEqual(captured.getvalue(), "{}")


if __name__ == "__main__":
    unittest.main()
