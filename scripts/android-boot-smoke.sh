#!/usr/bin/env bash
# android-boot-smoke.sh — Android emulator BOOT SMOKE TEST for #1640.
#
# Usage: bash scripts/android-boot-smoke.sh <arch>
#
# WHAT THIS PROVES (and why it's distinct from android-parity-run.sh)
# ------------------------------------------------------------------
# android-parity-run.sh instruments the SkiaSharp byte-parity TEST head
# (com.laqieer.febuildergba.tests) — it never boots the REAL app. #1640 is the
# gap that the actual FEBuilderGBA.Android application (com.laqieer.febuildergba)
# had never been launched on a device/emulator: config first-run extraction
# (#1123 -> FilesDir), the single-view Avalonia lifetime (#1122 -> MainView), and
# the editor-launcher shell were all documented BUILD-ONLY. This script installs
# the real signed APK, launches the launcher activity, and asserts it reaches the
# RESUMED state with NO fatal exception in logcat — a real boot smoke test.
#
# WHY launch via the LAUNCHER intent (monkey) and NOT `am start -n pkg/.Class`
# ---------------------------------------------------------------------------
# The .NET-for-Android build CRC-mangles the managed activity class name in the
# merged manifest (e.g. crc64....MainActivity), so a hardcoded component name is
# brittle. `monkey -p <pkg> -c android.intent.category.LAUNCHER 1` resolves and
# fires the package's own LAUNCHER intent regardless of the mangled class name.
#
# SINGLE-LINE INVOCATION
# ----------------------
# Like android-parity-run.sh, this is called as ONE line from the workflow's
# `script:` block: reactivecircus/android-emulator-runner@v2 runs each `script:`
# line in a SEPARATE `/usr/bin/sh -c` invocation, so multi-line shell constructs
# cannot span `script:` lines — all logic lives here and is invoked with one line.
#
# Arguments:
#   $1  ABI string, e.g. x86_64

set -euo pipefail

ARCH="${1:?arch arg required (e.g. x86_64)}"
PKG="com.laqieer.febuildergba"
echo "=== Android Boot Smoke: arch=${ARCH} pkg=${PKG} ==="

# ---------------------------------------------------------------
# 1. Locate the REAL signed app APK (not the test head)
# ---------------------------------------------------------------
APK_PATH=$(find FEBuilderGBA.Android/bin/Release/net9.0-android -name "*-Signed.apk" | head -1)
if [ -z "$APK_PATH" ]; then
  echo "ERROR: No *-Signed.apk found under FEBuilderGBA.Android/bin/Release/net9.0-android/"
  ls -la FEBuilderGBA.Android/bin/Release/net9.0-android/ || true
  exit 1
fi
echo "APK: $APK_PATH"

# ---------------------------------------------------------------
# 2. Install the APK with one retry on transient adb failure
# ---------------------------------------------------------------
# Force a clean FIRST-RUN: the AVD is cached across workflow runs, so a prior
# run's app + its extracted config in FilesDir can persist. Uninstalling first
# guarantees this run exercises the real first-run config extraction (#1123)
# (which is version-stamped + idempotent and would otherwise be skipped).
echo "--- Uninstalling any prior ${PKG} to force a clean first-run ---"
adb uninstall "${PKG}" || true

install_apk() {
  adb install -r "$APK_PATH"
}
if ! install_apk; then
  echo "adb install failed, retrying in 10 seconds..."
  sleep 10
  install_apk
fi

# ---------------------------------------------------------------
# 3. Clear logcat, then launch the app via its LAUNCHER intent
# ---------------------------------------------------------------
# Clearing the buffer first means any FATAL EXCEPTION we later see belongs to
# THIS boot, not a prior install/system event.
adb logcat -c || true

echo "--- Launching ${PKG} via LAUNCHER intent (monkey) ---"
# monkey returns non-zero if the package has no launchable activity; capture but
# do not abort yet — the RESUMED poll below is the authoritative success gate.
adb shell monkey -p "${PKG}" -c android.intent.category.LAUNCHER 1 || true

# ---------------------------------------------------------------
# 3b. Resolve the app PID so crash detection can be SCOPED to our process.
#     Failing on ANY "FATAL EXCEPTION" in the buffer would false-fail on an
#     unrelated emulator/system crash; we only care about crashes in OUR app.
#     `pidof` may need a moment to resolve right after launch; retry briefly.
# ---------------------------------------------------------------
APP_PID=""
for _ in $(seq 1 10); do
  APP_PID=$( { adb shell pidof "${PKG}" 2>/dev/null || true; } | tr -d '\r' | awk '{print $1}' || true)
  if [ -n "${APP_PID}" ]; then
    echo "App PID for ${PKG}: ${APP_PID}"
    break
  fi
  sleep 1
done
if [ -z "${APP_PID}" ]; then
  echo "NOTE: could not resolve a PID for ${PKG} yet (process may not be up); crash gate will fall back to package-name-scoped logcat matching."
fi

# Helper: does logcat contain a crash attributable to OUR app?
# Prefer --pid (exact process scope) when we have a PID; otherwise scope by
# package name within the crash block (never a bare global FATAL EXCEPTION).
app_crashed() {
  if [ -n "${APP_PID}" ]; then
    adb logcat -d --pid "${APP_PID}" 2>/dev/null \
      | grep -qE "FATAL EXCEPTION|AndroidRuntime" && return 0
  fi
  # The app's own fail-fast config-extraction error is logged under the
  # FEBuilderGBA tag (NOT the package name), so match it INDEPENDENTLY. The
  # logcat buffer was cleared right before launch (section 3), so any
  # occurrence belongs to THIS boot.
  adb logcat -d 2>/dev/null | grep -q "config extraction FAILED" && return 0
  # A generic AndroidRuntime crash block: scope to OUR package so an unrelated
  # system/other-app crash cannot false-fail the smoke test.
  adb logcat -d 2>/dev/null | grep -E "AndroidRuntime" | grep -q "${PKG}" && return 0
  return 1
}

# ---------------------------------------------------------------
# 4. Poll for the app to reach the RESUMED (foreground) state
# ---------------------------------------------------------------
# `dumpsys activity activities` reports the currently resumed activity as
# mResumedActivity (newer Android) or ResumedActivity. We also accept the app
# appearing as the top resumed record for the package. Poll up to ~90s; the
# first-run config extraction (#1123) unzips the bundled config tree before
# Avalonia boots, so allow generous headroom on a software-GPU emulator.
RESUMED=0
DUMP_LOCAL="boot-dumpsys-${ARCH}.txt"
for i in $(seq 1 45); do
  adb shell dumpsys activity activities > "${DUMP_LOCAL}" 2>/dev/null || true
  # Match a resumed-activity line that names our package.
  if grep -E "(mResumedActivity|ResumedActivity|topResumedActivity)" "${DUMP_LOCAL}" 2>/dev/null | grep -q "${PKG}"; then
    RESUMED=1
    echo "App reached RESUMED state after ~$((i*2))s."
    break
  fi
  # Early-out on a fatal crash IN OUR APP so we don't waste the full poll window.
  if app_crashed; then
    echo "Detected a fatal crash marker for ${PKG} during boot poll — stopping early."
    break
  fi
  sleep 2
done

# ---------------------------------------------------------------
# 5. Capture full logcat for diagnostics (always)
# ---------------------------------------------------------------
LOGCAT_LOCAL="boot-logcat-${ARCH}.txt"
adb logcat -d > "${LOGCAT_LOCAL}" 2>/dev/null || true

echo ""
echo "=== resumed-activity lines ==="
grep -E "(mResumedActivity|ResumedActivity|topResumedActivity)" "${DUMP_LOCAL}" 2>/dev/null || echo "(none captured)"
echo ""

# ---------------------------------------------------------------
# 6. EVALUATE — fail on a crash IN OUR APP, OR if RESUMED was never reached
# ---------------------------------------------------------------
FAILED=0

# (a) Crash attributable to OUR process/package (PID-scoped when available,
#     package-name-scoped otherwise — NEVER a bare global FATAL EXCEPTION, so an
#     unrelated emulator/system crash cannot false-fail this smoke test).
if app_crashed; then
  echo "FAIL: ${PKG} crashed during boot (PID/package-scoped logcat match)."
  echo "--- offending logcat lines ---"
  if [ -n "${APP_PID}" ]; then
    adb logcat -d --pid "${APP_PID}" 2>/dev/null | grep -nE "FATAL EXCEPTION|AndroidRuntime|Caused by:" | head -40 || true
  fi
  grep -nE "config extraction FAILED" "${LOGCAT_LOCAL}" 2>/dev/null | head -10 || true
  FAILED=1
fi

# (c) RESUMED must have been reached.
if [ "${RESUMED}" != "1" ]; then
  echo "FAIL: ${PKG} never reached the RESUMED (foreground) state within the poll window."
  echo "--- last dumpsys snapshot (head) ---"
  head -60 "${DUMP_LOCAL}" 2>/dev/null || true
  echo "--- logcat tail ---"
  tail -60 "${LOGCAT_LOCAL}" 2>/dev/null || true
  FAILED=1
fi

# ---------------------------------------------------------------
# 7. Final result
# ---------------------------------------------------------------
echo ""
if [ "${FAILED}" = "0" ]; then
  echo "=== PASS: Android Boot Smoke (arch=${ARCH}) — ${PKG} booted to RESUMED with no fatal exception ==="
else
  echo "=== FAIL: Android Boot Smoke (arch=${ARCH}) ==="
  exit 1
fi
