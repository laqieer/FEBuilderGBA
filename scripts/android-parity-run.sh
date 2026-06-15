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
APK_PATH=$(find FEBuilderGBA.Android.Tests/bin/Release/net9.0-android -name "*-Signed.apk" | head -1)
if [ -z "$APK_PATH" ]; then
  echo "ERROR: No *-Signed.apk found under FEBuilderGBA.Android.Tests/bin/Release/net9.0-android/"
  ls -la FEBuilderGBA.Android.Tests/bin/Release/net9.0-android/ || true
  exit 1
fi
echo "APK: $APK_PATH"

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
# 3. Run the instrumented tests via am instrument (with retry)
# ---------------------------------------------------------------
# -e results-file-path: where XHarness DefaultAndroidEntryPoint writes
#   TestResults.xml on the device (readable via adb pull).
# -w: wait for instrumentation to finish (required for result collection).
RESULTS_DEVICE_PATH="/sdcard/Download"
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

echo ""
echo "=== instrument output ==="
cat "${OUTFILE}"
echo ""

# ---------------------------------------------------------------
# 4. Pull TestResults.xml from the device
# ---------------------------------------------------------------
XML_LOCAL="TestResults-${ARCH}.xml"
if ! adb pull "${RESULTS_DEVICE_PATH}/TestResults.xml" "${XML_LOCAL}"; then
  echo "WARNING: could not pull TestResults.xml — the test run may not have written it."
  XML_LOCAL=""
fi

# ---------------------------------------------------------------
# 5. Pull logcat for diagnostics
# ---------------------------------------------------------------
adb logcat -d > "logcat-${ARCH}.txt" || true

# ---------------------------------------------------------------
# 6. PARSE RESULTS — fail the step if any test failed
# ---------------------------------------------------------------
FAILED=0

# (a) am instrument output: FAILURES!!! marker (printed by am instrument on
#     nonzero instrumentation result) or INSTRUMENTATION_ABORTED.
if grep -q "FAILURES!!!" "${OUTFILE}" 2>/dev/null; then
  echo "FAIL: instrument output contains FAILURES!!!"
  FAILED=1
fi
if grep -q "INSTRUMENTATION_ABORTED" "${OUTFILE}" 2>/dev/null; then
  echo "FAIL: INSTRUMENTATION_ABORTED in output"
  FAILED=1
fi

# (b) TestResults.xml: parse failure-count attributes on the <assembly ...>
#     element.  XHarness writes xUnit v2 XML where the <assembly> element
#     uses `failed="N"` (xUnit v2) — but older schemas use `failures="N"`.
#     We grep for BOTH and treat either nonzero as failure.
#     We also check `errors="N"`.  Raw matched values are echoed for
#     diagnosability.
if [ -n "${XML_LOCAL}" ] && [ -f "${XML_LOCAL}" ]; then
  # failed= (xUnit v2 <assembly> attribute)
  XML_FAILED=$(grep -oP 'failed="\K[^"]+' "${XML_LOCAL}" | head -1 || echo "0")
  # failures= (older / NUnit-style schema)
  XML_FAILURES=$(grep -oP 'failures="\K[^"]+' "${XML_LOCAL}" | head -1 || echo "0")
  XML_ERRORS=$(grep -oP 'errors="\K[^"]+' "${XML_LOCAL}" | head -1 || echo "0")
  XML_TOTAL=$(grep -oP 'total="\K[^"]+' "${XML_LOCAL}" | head -1 || echo "0")
  XML_PASSED=$(grep -oP 'passed="\K[^"]+' "${XML_LOCAL}" | head -1 || echo "0")
  echo "TestResults.xml: total=${XML_TOTAL} passed=${XML_PASSED} failed=${XML_FAILED} failures=${XML_FAILURES} errors=${XML_ERRORS}"
  if [ "${XML_FAILED:-0}"    != "0" ] || \
     [ "${XML_FAILURES:-0}"  != "0" ] || \
     [ "${XML_ERRORS:-0}"    != "0" ]; then
    echo "FAIL: TestResults.xml reports failed=${XML_FAILED} failures=${XML_FAILURES} errors=${XML_ERRORS}"
    FAILED=1
  fi
else
  echo "WARNING: TestResults.xml not available; relying on instrument output only."
  # If no XML and no FAILURES marker, check for an explicit OK marker.
  if ! grep -qE "INSTRUMENTATION_STATUS_CODE: 0|OK \(" "${OUTFILE}" 2>/dev/null; then
    echo "FAIL: instrument output has no OK marker and no TestResults.xml."
    FAILED=1
  fi
fi

# ---------------------------------------------------------------
# 7. Final result
# ---------------------------------------------------------------
if [ "${FAILED}" = "0" ]; then
  echo ""
  echo "=== PASS: Android Emulator Parity (arch=${ARCH}) ==="
else
  echo ""
  echo "=== FAIL: Android Emulator Parity (arch=${ARCH}) ==="
  exit 1
fi
