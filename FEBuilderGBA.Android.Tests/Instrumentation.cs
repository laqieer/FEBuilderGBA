// SPDX-License-Identifier: GPL-3.0-or-later
//
// XHarness Android instrumentation entry point for FEBuilderGBA.Android.Tests (#1125).
//
// This class is the on-device runner harness. It:
//   1. Copies the bundled Tuffy font from APK assets into
//      AppContext.BaseDirectory/Fonts/ BEFORE any test runs, so the
//      hard-coded LoadTuffy() path in SkiaRenderByteParityTests resolves
//      without modifying the shared (linked) test source.
//   2. Delegates test execution to the XHarness DefaultAndroidEntryPoint,
//      which drives the xUnit runner and writes TestResults.xml.
//   3. Passes the results path and a pass/fail return code back to ADB
//      via Finish(), so the CI script can detect failures.
//
// Font path assumption: on .NET-Android, AppContext.BaseDirectory is the
// directory containing the managed assemblies (the native library extraction
// directory, e.g. /data/app/.../<package>/lib/x86_64/). Loose
// CopyToOutputDirectory content files are placed there by the .NET-Android
// build system in .NET 7+. However, because this behaviour is not 100%
// guaranteed across .NET versions and device configurations, this class
// ALSO copies from APK assets as a belt-and-suspenders fallback, always
// winning the race with an explicit write. The write is done with
// FileMode.Create so a stale CopyToOutputDirectory copy does not cause
// a "file already exists" failure.
//
// If AppContext.BaseDirectory is on a read-only mount (which can happen on
// some Android API levels for the native lib dir), the Directory.CreateDirectory
// and FileStream calls will throw; this is caught and logged. In that case the
// font tests emit a "bundled font missing" assertion failure with the actual
// path in the message, which clearly identifies the root cause in CI output.
// The IMAGE parity tests and the RuntimeLoadedSkiaSharpAssembly_Is_288 guard
// do NOT require the font and will still pass in that scenario.
//
// NAMESPACE NOTE: This project lives in the FEBuilderGBA.Android.Tests
// namespace. The `Android` segment in `Android.App.Instrumentation` would
// otherwise resolve to the `FEBuilderGBA.Android` sub-namespace rather than
// the top-level `Android` namespace from Mono.Android. All Android SDK types
// are therefore referenced via `global::Android.*` to force resolution from
// the global namespace root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.DefaultAndroidEntryPoint.Xunit;
using Microsoft.DotNet.XHarness.TestRunners.Common;

namespace FEBuilderGBA.Android.Tests
{
    [global::Android.App.Instrumentation(Name = "com.laqieer.febuildergba.tests.TestInstrumentation")]
    public class TestInstrumentation : global::Android.App.Instrumentation
    {
        const string LogTag = "FEBuilderGBA.Tests";
        const string DefaultResultsPath = "/sdcard/Download";

        global::Android.OS.Bundle? _arguments;

        public TestInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate(global::Android.OS.Bundle? arguments)
        {
            base.OnCreate(arguments);
            _arguments = arguments;

            // Copy Tuffy font from APK assets into AppContext.BaseDirectory/Fonts/
            // BEFORE Start() which triggers OnStart() and test execution.
            CopyTuffyFontFromAssets();

            Start();
        }

        public override void OnStart()
        {
            // Fire-and-forget the async work; Instrumentation.OnStart() is
            // called on the main thread and must not block.
            Task.Run(async () => await RunTestsAsync());
        }

        async Task RunTestsAsync()
        {
            // Parse the results path from the ADB instrument -e arguments bundle.
            // The XHarness CI script passes: adb shell am instrument -w
            //   -e results-file-path /sdcard/Download ...
            string resultsPath = DefaultResultsPath;
            if (_arguments != null)
            {
                string? val = _arguments.GetString("results-file-path");
                if (!string.IsNullOrEmpty(val))
                    resultsPath = val;
            }

            global::Android.Util.Log.Info(LogTag, $"XHarness results path: {resultsPath}");

            // Build the optional bundle dict from the instrumentation arguments.
            var bundleDict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_arguments != null)
            {
                foreach (string? key in _arguments.KeySet() ?? Array.Empty<string>())
                {
                    if (key == null) continue;
                    string? v = _arguments.GetString(key);
                    if (v != null)
                        bundleDict[key] = v;
                }
            }

            int failedCount = 0;
            int passedCount = 0;

            try
            {
                var entryPoint = new DefaultAndroidEntryPoint(resultsPath, bundleDict);

                // Wire the assembly containing the linked test classes.
                // Because SkiaRenderByteParityTests and SkiaSharpVersionGuardTests
                // are compiled INTO this assembly (via <Compile Link>), their
                // Assembly is this assembly — typeof(TestInstrumentation).Assembly.
                entryPoint.Tests = new[] { typeof(TestInstrumentation).Assembly };

                entryPoint.TestsCompleted += (sender, results) =>
                {
                    // TestRunResult is a struct; access fields directly (no null check).
                    failedCount = (int)results.FailedTests;
                    passedCount = (int)results.PassedTests;
                    global::Android.Util.Log.Info(LogTag,
                        $"XHarness completed: passed={passedCount} failed={failedCount} skipped={results.SkippedTests} total={results.ExecutedTests}");
                };

                await entryPoint.RunAsync();

                global::Android.Util.Log.Info(LogTag,
                    $"RunAsync finished. Results XML: {entryPoint.TestsResultsFinalPath}");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, $"XHarness RunAsync threw: {ex}");
                failedCount = Math.Max(1, failedCount);
            }

            // Finish with a result bundle. Non-zero return code signals CI failure.
            var result = new global::Android.OS.Bundle();
            result.PutString("results-file-path", resultsPath);
            result.PutInt("return-code", failedCount > 0 ? 1 : 0);
            result.PutInt("failed-tests", failedCount);
            result.PutInt("passed-tests", passedCount);

            Finish(failedCount > 0 ? global::Android.App.Result.Canceled : global::Android.App.Result.Ok, result);
        }

        /// <summary>
        /// Copy Tuffy-Regular.ttf from APK assets into
        /// <c>AppContext.BaseDirectory/Fonts/Tuffy-Regular.ttf</c> so that the
        /// hard-coded <c>LoadTuffy()</c> in SkiaRenderByteParityTests resolves.
        /// Wrapped in try/catch so a font-copy failure does not abort the entire
        /// instrumentation run — the image parity + runtime-version-guard tests
        /// do not need the font and must still run.
        /// </summary>
        void CopyTuffyFontFromAssets()
        {
            try
            {
                string fontsDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
                string fontDest = Path.Combine(fontsDir, "Tuffy-Regular.ttf");

                Directory.CreateDirectory(fontsDir);

                // Open from APK assets (packed as "Tuffy-Regular.ttf" by the
                // <AndroidAsset Link="Tuffy-Regular.ttf"/> item in the csproj).
                // Android.App.Instrumentation does not have an Assets property;
                // assets are accessed through the instrumentation context.
                var assets = Context?.Assets;
                if (assets == null)
                {
                    global::Android.Util.Log.Warn(LogTag,
                        "Instrumentation context Assets is null — font parity tests may fail.");
                    return;
                }
                using var assetStream = assets.Open("Tuffy-Regular.ttf");
                if (assetStream == null)
                {
                    global::Android.Util.Log.Warn(LogTag,
                        "Font asset 'Tuffy-Regular.ttf' not found in APK — font parity tests may fail.");
                    return;
                }

                using var destStream = new FileStream(fontDest, FileMode.Create, FileAccess.Write);
                assetStream.CopyTo(destStream);

                global::Android.Util.Log.Info(LogTag,
                    $"Tuffy font copied: {fontDest} ({new FileInfo(fontDest).Length} bytes)");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag,
                    $"Font copy from assets failed (font parity tests will report missing font): {ex.Message}");
                // Do NOT rethrow — the IMAGE parity tests (exact golden + PNG
                // round-trip) and the runtime-2.88 version guard do not need the
                // font and must still run even if the font copy fails.
            }
        }
    }
}
