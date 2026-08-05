#!/usr/bin/env bash
# Deterministic fake-adb regression harness for android-parity-run.sh (#2060).

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "${SCRIPT_DIR}/../.." && pwd)
RUNNER="${REPO_ROOT}/scripts/android-parity-run.sh"
TEST_PKG="com.laqieer.febuildergba.tests"
TEMP_ROOT=$(mktemp -d)
trap 'rm -rf "$TEMP_ROOT"' EXIT

FAKE_BIN="${TEMP_ROOT}/bin"
mkdir -p "$FAKE_BIN"

cat > "${FAKE_BIN}/adb" <<'FAKE_ADB'
#!/usr/bin/env sh
set -eu

printf '%s\n' "$*" >> "$FAKE_ADB_LOG"

increment() {
  name="$1"
  path="${FAKE_COUNTER_DIR}/${name}"
  value=0
  if [ -f "$path" ]; then
    value=$(cat "$path")
  fi
  value=$((value + 1))
  printf '%s\n' "$value" > "$path"
  printf '%s\n' "$value"
}

if [ "$1" = "shell" ] && [ "${2:-}" = "pm" ] && [ "${3:-}" = "list" ]; then
  count=$(increment query)
  if [ "$count" -le "${FAKE_QUERY_FAILURES:-0}" ]; then
    echo "fake package query failure" >&2
    exit 1
  fi
  if [ "${FAKE_LOOKALIKE:-0}" = "1" ]; then
    echo "package:${FAKE_TEST_PKG}.suffix"
  fi
  if [ -f "$FAKE_STATE_FILE" ]; then
    echo "package:${FAKE_TEST_PKG}"
  fi
  exit 0
fi

if [ "$1" = "uninstall" ]; then
  count=$(increment uninstall)
  if [ "$count" -le "${FAKE_UNINSTALL_FAILURES:-0}" ]; then
    echo "Failure [DELETE_FAILED_INTERNAL_ERROR]" >&2
    exit 1
  fi
  echo "Success"
  if [ "${FAKE_UNINSTALL_STICKY:-0}" != "1" ]; then
    rm -f "$FAKE_STATE_FILE"
  fi
  exit 0
fi

if [ "$1" = "shell" ] && [ "${2:-}" = "rm" ]; then
  count=$(increment clear)
  if [ "$count" -le "${FAKE_CLEAR_FAILURES:-0}" ]; then
    echo "fake result cleanup failure" >&2
    exit 1
  fi
  exit 0
fi

if [ "$1" = "install" ]; then
  count=$(increment install)
  if [ "$count" -le "${FAKE_INSTALL_FAILURES:-0}" ]; then
    echo "Failure [INSTALL_FAILED_INTERNAL_ERROR]" >&2
    exit 1
  fi
  : > "$FAKE_STATE_FILE"
  echo "Success"
  exit 0
fi

if [ "$1" = "shell" ] && [ "${2:-}" = "am" ]; then
  echo "INSTRUMENTATION_RESULT: return-code=0"
  echo "INSTRUMENTATION_RESULT: failed-tests=0"
  echo "INSTRUMENTATION_CODE: -1"
  exit 0
fi

if [ "$1" = "shell" ] && [ "${2:-}" = "test" ]; then
  exit 0
fi

if [ "$1" = "pull" ]; then
  case "$2" in
    */TestResults.xml)
      cat > "$3" <<'XML'
<assemblies total="5" passed="5" failed="0" skipped="0" failures="0" errors="0" />
XML
      exit 0
      ;;
    */instrumentation-error.txt)
      exit 1
      ;;
  esac
fi

if [ "$1" = "logcat" ]; then
  exit 0
fi

echo "Unexpected fake adb call: $*" >&2
exit 1
FAKE_ADB
chmod +x "${FAKE_BIN}/adb"

cat > "${FAKE_BIN}/sleep" <<'FAKE_SLEEP'
#!/usr/bin/env sh
exit 0
FAKE_SLEEP
chmod +x "${FAKE_BIN}/sleep"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

assert_count() {
  expected="$1"
  pattern="$2"
  actual=$(grep -Fxc "$pattern" "$CASE_LOG" 2>/dev/null || true)
  [ "$actual" = "$expected" ] \
    || fail "${CASE_NAME}: expected ${expected} calls '${pattern}', got ${actual}"
}

assert_contains() {
  grep -Fq -- "$1" "$CASE_OUTPUT" \
    || fail "${CASE_NAME}: missing output '$1'"
}

assert_transaction_log() {
  expected=$(cat)
  actual=$(grep -E \
    '^(shell pm list packages --user 0 |uninstall |shell rm -f |install -r |shell am instrument )' \
    "$CASE_LOG" || true)
  [ "$actual" = "$expected" ] || fail "${CASE_NAME}: transaction log mismatch
EXPECTED:
${expected}
ACTUAL:
${actual}"
}

run_case() {
  CASE_NAME="$1"
  initial_installed="$2"
  expected_exit="$3"
  shift 3

  CASE_DIR="${TEMP_ROOT}/${CASE_NAME}"
  CASE_LOG="${CASE_DIR}/adb.log"
  CASE_OUTPUT="${CASE_DIR}/output.log"
  state_file="${CASE_DIR}/installed"
  counter_dir="${CASE_DIR}/counters"
  mkdir -p \
    "${CASE_DIR}/FEBuilderGBA.Android.Tests/bin/Release/net10.0-android" \
    "$counter_dir"
  : > "${CASE_DIR}/FEBuilderGBA.Android.Tests/bin/Release/net10.0-android/fake-Signed.apk"
  if [ "$initial_installed" = "1" ]; then
    : > "$state_file"
  fi

  set +e
  (
    cd "$CASE_DIR"
    env \
      PATH="${FAKE_BIN}:$PATH" \
      FAKE_ADB_LOG="$CASE_LOG" \
      FAKE_COUNTER_DIR="$counter_dir" \
      FAKE_STATE_FILE="$state_file" \
      FAKE_TEST_PKG="$TEST_PKG" \
      "$@" \
      bash "$RUNNER" x86_64
  ) > "$CASE_OUTPUT" 2>&1
  actual_exit=$?
  set -e

  [ "$actual_exit" = "$expected_exit" ] \
    || fail "${CASE_NAME}: expected exit ${expected_exit}, got ${actual_exit}; $(cat "$CASE_OUTPUT")"
}

query_call="shell pm list packages --user 0 ${TEST_PKG}"
uninstall_call="uninstall ${TEST_PKG}"
clear_call="shell rm -f /sdcard/Download/TestResults.xml /sdcard/Download/instrumentation-error.txt"
install_call="install -r FEBuilderGBA.Android.Tests/bin/Release/net10.0-android/fake-Signed.apk"
instrument_call="shell am instrument -w -e results-file-path /sdcard/Download ${TEST_PKG}/${TEST_PKG}.TestInstrumentation"

run_case cached-success 1 0
assert_transaction_log <<EOF
$query_call
$uninstall_call
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 2 "$query_call"
assert_count 1 "$uninstall_call"
assert_count 1 "$clear_call"
assert_count 1 "$install_call"

run_case absent-success 0 0
assert_transaction_log <<EOF
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 1 "$query_call"
assert_count 0 "$uninstall_call"
assert_count 1 "$install_call"

run_case lookalike-is-absent 0 0 FAKE_LOOKALIKE=1
assert_transaction_log <<EOF
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 0 "$uninstall_call"
assert_contains "--- No cached ${TEST_PKG} package is installed ---"

run_case transient-query-failure 0 0 FAKE_QUERY_FAILURES=1
assert_transaction_log <<EOF
$query_call
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 2 "$query_call"
assert_count 1 "$install_call"
assert_contains "transaction failed on attempt 1"

run_case transient-uninstall-failure 1 0 FAKE_UNINSTALL_FAILURES=1
assert_transaction_log <<EOF
$query_call
$uninstall_call
$query_call
$uninstall_call
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 2 "$uninstall_call"
assert_count 1 "$install_call"
assert_contains "failed to uninstall cached package"

run_case sticky-uninstall 1 1 FAKE_UNINSTALL_STICKY=1
assert_transaction_log <<EOF
$query_call
$uninstall_call
$query_call
$query_call
$uninstall_call
$query_call
EOF
assert_count 2 "$uninstall_call"
assert_count 0 "$install_call"
assert_contains "is still installed after adb uninstall"
assert_contains "failed after 2 attempts"

run_case transient-install-failure 1 0 FAKE_INSTALL_FAILURES=1
assert_transaction_log <<EOF
$query_call
$uninstall_call
$query_call
$clear_call
$install_call
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 1 "$uninstall_call"
assert_count 2 "$clear_call"
assert_count 2 "$install_call"
assert_contains "failed to install Android parity APK"

run_case transient-result-cleanup-failure 0 0 FAKE_CLEAR_FAILURES=1
assert_transaction_log <<EOF
$query_call
$clear_call
$query_call
$clear_call
$install_call
$instrument_call
EOF
assert_count 2 "$clear_call"
assert_count 1 "$install_call"
assert_contains "failed to clear stale Android parity result files"

run_case persistent-install-failure 0 1 FAKE_INSTALL_FAILURES=2
assert_transaction_log <<EOF
$query_call
$clear_call
$install_call
$query_call
$clear_call
$install_call
EOF
assert_count 2 "$install_call"
assert_contains "failed after 2 attempts"

run_case persistent-result-cleanup-failure 0 1 FAKE_CLEAR_FAILURES=2
assert_transaction_log <<EOF
$query_call
$clear_call
$query_call
$clear_call
EOF
assert_count 2 "$clear_call"
assert_count 0 "$install_call"
assert_count 0 "$instrument_call"
assert_contains "failed to clear stale Android parity result files"
assert_contains "failed after 2 attempts"

run_case persistent-query-failure 0 1 FAKE_QUERY_FAILURES=2
assert_transaction_log <<EOF
$query_call
$query_call
EOF
assert_count 2 "$query_call"
assert_count 0 "$install_call"
assert_contains "failed to inspect installed package"

run_case persistent-uninstall-failure 1 1 FAKE_UNINSTALL_FAILURES=2
assert_transaction_log <<EOF
$query_call
$uninstall_call
$query_call
$uninstall_call
EOF
assert_count 2 "$uninstall_call"
assert_count 0 "$install_call"
assert_contains "failed to uninstall cached package"

echo "PASS: android-parity-run fake-adb transaction tests"
