#!/usr/bin/env bash
# android-parity-run.sh — Android emulator parity test runner for #1125.
#
# Usage: bash scripts/android-parity-run.sh <arch>
#
# Called as a SINGLE LINE from the android-emulator-parity.yml workflow's
# `script:` block (reactivecircus/android-emulator-runner@v2 executes each
# `script:` line in a SEPARATE /usr/bin/sh -c invocation, so multi-line
# constructs — variable persistence, if/fi, functions — cannot span lines;
# the canonical fix is to move all logic here and call with one line).
#
# Arguments:
#   $1  ABI string, e.g. x86_64

set -euo pipefail

ARCH="${1:?arch arg required (e.g. x86_64)}"
echo "=== Android Emulator Parity: arch=${ARCH} ==="

# ---------------------------------------------------------------
# 1. Locate the signed APK
# ---------------------------------------------------------------
APK_PATH=$(find FEBuilderGBA.Android.Tests/bin/Release/net10.0-android -name "*-Signed.apk" | head -1)
if [ -z "$APK_PATH" ]; then
  echo "ERROR: No *-Signed.apk found under FEBuilderGBA.Android.Tests/bin/Release/net10.0-android/"
  ls -la FEBuilderGBA.Android.Tests/bin/Release/net10.0-android/ || true
  exit 1
fi
echo "APK: $APK_PATH"

RESULTS_DEVICE_PATH="/sdcard/Download"

# ---------------------------------------------------------------
# 2. Install APK with one retry on transient adb failure
# ---------------------------------------------------------------
install_apk() {
  adb install -r "$APK_PATH"
}
if ! install_apk; then
  echo "adb install failed, retrying in 10 seconds..."
  sleep 10
  install_apk
fi

# ---------------------------------------------------------------
# 2b. Delete stale device-side result files BEFORE running am instrument.
#     The emulator AVD is cached across workflow runs (~/.android/avd/*,
#     including the emulator sdcard/userdata image).  A prior green
#     TestResults.xml left on /sdcard/Download could survive across AVD
#     cache restores.  If the current instrumentation crashes before
#     writing fresh XML, adb pull would succeed on the OLD file and the
#     strict parser would see passed=5 failed=0 -- a false green.
#     Deleting here + requiring the current run to write the file (section 3b)
#     closes that false-green window completely.
# ---------------------------------------------------------------
echo "--- Clearing stale device-side result files before instrument run ---"
adb shell rm -f "${RESULTS_DEVICE_PATH}/TestResults.xml" "${RESULTS_DEVICE_PATH}/instrumentation-error.txt" || true

# ---------------------------------------------------------------
# 3. Run the instrumented tests via am instrument (with retry)
# ---------------------------------------------------------------
# -e results-file-path: where the custom reflection-based TestInstrumentation
#   runner writes TestResults.xml on the device (readable via adb pull).
#   NOT XHarness -- the runner is a direct Android.App.Instrumentation subclass
#   that invokes [Fact]/[SkippableFact] methods via reflection and writes its
#   own xUnit-shaped TestResults.xml (see Instrumentation.cs for rationale).
# -w: wait for instrumentation to finish (required for result collection).
OUTFILE="instrument-out-${ARCH}.txt"

run_instrument() {
  adb shell am instrument -w \
    -e results-file-path "${RESULTS_DEVICE_PATH}" \
    com.laqieer.febuildergba.tests/com.laqieer.febuildergba.tests.TestInstrumentation \
    2>&1 | tee "${OUTFILE}"
}
if ! run_instrument; then
  echo "am instrument failed, retrying in 15 seconds..."
  sleep 15
  run_instrument
fi

# ---------------------------------------------------------------
# 3b. Prove freshness: verify the CURRENT run wrote TestResults.xml
#     on-device.  We deleted any stale copy in section 2b, so if the file
#     is absent now the instrumentation crashed or was trimmed before it
#     could write results -- in either case we MUST NOT parse a stale
#     file (there is none) and must fail immediately.
# ---------------------------------------------------------------
if ! adb shell test -f "${RESULTS_DEVICE_PATH}/TestResults.xml"; then
  echo "FAIL: current run did not write TestResults.xml on device."
  echo "      Stale copy was cleared before the run (section 2b), so this cannot be a cache hit."
  echo "      The instrumentation likely crashed or was trimmed before writing results."
  exit 1
fi

echo ""
echo "=== instrument output ==="
cat "${OUTFILE}"
echo ""

# ---------------------------------------------------------------
# 4. Pull TestResults.xml from the device
# ---------------------------------------------------------------
XML_LOCAL="TestResults-${ARCH}.xml"
if ! adb pull "${RESULTS_DEVICE_PATH}/TestResults.xml" "${XML_LOCAL}"; then
  echo "WARNING: could not pull TestResults.xml -- the test run may not have written it."
  XML_LOCAL=""
fi

# Pull instrumentation-error.txt (written by Instrumentation.cs on exception).
# Best-effort -- absence means no on-device exception was caught.
ERROR_LOCAL="instrumentation-error-${ARCH}.txt"
if adb pull "${RESULTS_DEVICE_PATH}/instrumentation-error.txt" "${ERROR_LOCAL}" 2>/dev/null; then
  echo "=== instrumentation-error.txt ==="
  cat "${ERROR_LOCAL}"
  echo ""
else
  ERROR_LOCAL=""
fi

# ---------------------------------------------------------------
# 5. Pull logcat for diagnostics
# ---------------------------------------------------------------
adb logcat -d > "logcat-${ARCH}.txt" || true


# ---------------------------------------------------------------
# 6. PARSE RESULTS -- fail the step if any test failed
# ---------------------------------------------------------------
FAILED=0
RC_VAL=""
FT_VAL=""
# Initialize XML vars to safe defaults so the final summary echo never
# hits an unbound-variable error even when XML is absent/empty (set -u).
XML_TOTAL="0"
XML_PASSED="0"
XML_FAILED="0"
XML_FAILURES="0"
XML_ERRORS="0"
XML_SKIPPED="0"

# (a) am instrument output: FAILURES!!! or INSTRUMENTATION_ABORTED markers.
if grep -q "FAILURES!!!" "${OUTFILE}" 2>/dev/null; then
  echo "FAIL: instrument output contains FAILURES!!!"
  FAILED=1
fi
if grep -q "INSTRUMENTATION_ABORTED" "${OUTFILE}" 2>/dev/null; then
  echo "FAIL: INSTRUMENTATION_ABORTED in output"
  FAILED=1
fi

# (b) Non-zero return-code in INSTRUMENTATION_RESULT.
RC_VAL=$(grep "INSTRUMENTATION_RESULT: return-code=" "${OUTFILE}" 2>/dev/null \
  | tail -1 | sed "s/.*return-code=//" || echo "")
if [ -n "${RC_VAL}" ] && [ "${RC_VAL}" != "0" ]; then
  echo "FAIL: INSTRUMENTATION_RESULT return-code=${RC_VAL} (non-zero)"
  FAILED=1
fi

# (c) Non-zero failed-tests in INSTRUMENTATION_RESULT.
FT_VAL=$(grep "INSTRUMENTATION_RESULT: failed-tests=" "${OUTFILE}" 2>/dev/null \
  | tail -1 | sed "s/.*failed-tests=//" || echo "")
if [ -n "${FT_VAL}" ] && [ "${FT_VAL}" != "0" ]; then
  echo "FAIL: INSTRUMENTATION_RESULT failed-tests=${FT_VAL} (non-zero)"
  FAILED=1
fi

# (d) TestResults.xml: parse failure/error counts and REQUIRE executed (passed+failed) >= 4.
# A missing or empty XML is an UNCONDITIONAL FAIL -- the reflection runner
# always writes TestResults.xml; absence means trimming/crash/discovery failure.
# We never fall through to a pass on a missing XML.
if [ -n "${XML_LOCAL}" ] && [ -f "${XML_LOCAL}" ] && [ -s "${XML_LOCAL}" ]; then
  # POSIX-portable XML attribute extraction (no GNU -P/PCRE).
  # Extracts the value of attr="VALUE" using sed (works on macOS/BSD/Linux).
  _xml_attr() {
    # Usage: _xml_attr ATTR FILE
    sed -n "s/.*${1}=\"\([0-9]*\)\".*/\1/p" "${2}" | head -1
  }
  XML_FAILED=$(   _xml_attr "failed"    "${XML_LOCAL}"); XML_FAILED="${XML_FAILED:-0}"
  XML_FAILURES=$( _xml_attr "failures"  "${XML_LOCAL}"); XML_FAILURES="${XML_FAILURES:-0}"
  XML_ERRORS=$(   _xml_attr "errors"    "${XML_LOCAL}"); XML_ERRORS="${XML_ERRORS:-0}"
  XML_TOTAL=$(    _xml_attr "total"     "${XML_LOCAL}"); XML_TOTAL="${XML_TOTAL:-0}"
  XML_PASSED=$(   _xml_attr "passed"    "${XML_LOCAL}"); XML_PASSED="${XML_PASSED:-0}"
  XML_SKIPPED=$(  _xml_attr "skipped"   "${XML_LOCAL}"); XML_SKIPPED="${XML_SKIPPED:-0}"
  # executed = passed + failed (total includes skipped; gate on actually-executed tests)
  XML_EXECUTED=$(( XML_PASSED + XML_FAILED ))
  echo "TestResults.xml: total=${XML_TOTAL} passed=${XML_PASSED} failed=${XML_FAILED} skipped=${XML_SKIPPED} executed=${XML_EXECUTED} failures=${XML_FAILURES} errors=${XML_ERRORS}"
  if [ "${XML_FAILED:-0}"    != "0" ] || \
     [ "${XML_FAILURES:-0}"  != "0" ] || \
     [ "${XML_ERRORS:-0}"    != "0" ]; then
    echo "FAIL: TestResults.xml reports failed=${XML_FAILED} failures=${XML_FAILURES} errors=${XML_ERRORS}"
    FAILED=1
  fi
  # REQUIRE at least 4 EXECUTED (passed+failed) tests (NOT total which includes skipped).
  # We have 5 executed: 2 image-parity + 2 font-parity + 1 runtime-version-guard;
  # 2 tests skip on-device (declared/restored graph guards need the source tree).
  if [ "${XML_EXECUTED:-0}" -lt "4" ] 2>/dev/null; then
    echo "FAIL: expected executed (passed+failed) >= 4, got ${XML_EXECUTED} (passed=${XML_PASSED}+failed=${XML_FAILED}); trimming/discovery problem?"
    FAILED=1
  fi
else
  # Missing or empty XML is an unconditional failure.
  # The reflection runner ALWAYS writes TestResults.xml; absence means the
  # instrumentation crashed or the APK was trimmed incorrectly.
  echo "FAIL: TestResults.xml missing or empty -- reflection runner did not write results."
  echo "      This is an unconditional failure (no 'OK marker' fallback)."
  FAILED=1
fi

# ---------------------------------------------------------------
# 7. Final result
# ---------------------------------------------------------------
echo ""
echo "=== Summary: arch=${ARCH} total=${XML_TOTAL} passed=${XML_PASSED} failed=${XML_FAILED} executed=${XML_EXECUTED:-0} errors=${XML_ERRORS} return-code=${RC_VAL:-0} ==="
if [ "${FAILED}" = "0" ]; then
  echo "=== PASS: Android Emulator Parity (arch=${ARCH}) ==="
else
  echo "=== FAIL: Android Emulator Parity (arch=${ARCH}) ==="
  exit 1
fi
