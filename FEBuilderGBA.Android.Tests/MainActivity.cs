// SPDX-License-Identifier: GPL-3.0-or-later
//
// Minimal launcher activity for the FEBuilderGBA.Android.Tests instrumented
// test host (#1125).
//
// This activity satisfies the APK launcher requirement (every APK must have
// an Activity with MainLauncher=true). The actual test execution is driven by
// TestInstrumentation (an Android.App.Instrumentation subclass), which is
// what ADB `am instrument` targets — NOT this Activity.
//
// The activity body is intentionally empty: on-device this is only launched
// if someone manually opens the test APK from a device launcher; the CI
// workflow runs via `adb shell am instrument -w ...` which bypasses the
// Activity entirely and boots the Instrumentation directly.
//
// Exported = true is REQUIRED on Android 12+ (targetSdkVersion >= 31) for any
// activity with an implicit or explicit intent-filter, or with MainLauncher=true.
// Without it, aapt2 packaging fails with an error about missing android:exported.
//
// NAMESPACE NOTE: `global::Android.App.Activity` is used to avoid the
// `Android` segment being resolved against the `FEBuilderGBA.Android`
// sub-namespace instead of the top-level `Android` (Mono.Android) namespace.

namespace FEBuilderGBA.Android.Tests
{
    [global::Android.App.Activity(
        Label = "FEBuilderGBA.Android.Tests",
        MainLauncher = true,
        Exported = true)]
    public class MainActivity : global::Android.App.Activity
    {
        // Intentionally empty — see file header.
    }
}
