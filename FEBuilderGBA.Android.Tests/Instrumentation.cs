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
//   4. Logs the reflected test-class count BEFORE RunAsync, so CI can
//      distinguish trimmer-stripped classes (count=0) from a runtime
//      exception as the cause of zero executed tests (#1125 r4).
//   5. Writes an instrumentation-error.txt file into the results dir if
//      an exception is caught, so CI can adb-pull and display it.
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
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.DefaultAndroidEntryPoint.Xunit;
using Microsoft.DotNet.XHarness.TestRunners.Common;

namespace FEBuilderGBA.Android.Tests
{
    // ---------------------------------------------------------------------------
    // ParityAndroidEntryPoint -- thin subclass of DefaultAndroidEntryPoint that
    // overrides GetTestAssemblies() to supply a REAL on-disk path for the test
    // assembly DLL.  The base class yields assembly.Location (which is empty on
    // .NET 9 Android), causing xUnit's FileExists guard to throw
    // "File not found: FEBuilderGBA.Android.Tests.dll".  Our override bypasses
    // that by pointing xUnit at the extracted path.
    // ---------------------------------------------------------------------------
    sealed class ParityAndroidEntryPoint : DefaultAndroidEntryPoint
    {
        public Assembly? TestAssembly { get; set; }
        public string TestAssemblyPath { get; set; } = string.Empty;

        public ParityAndroidEntryPoint(string resultsPath, Dictionary<string, string> bundle)
            : base(resultsPath, bundle)
        {
        }

        protected override IEnumerable<TestAssemblyInfo> GetTestAssemblies()
        {
            if (TestAssembly != null && !string.IsNullOrEmpty(TestAssemblyPath))
                yield return new TestAssemblyInfo(TestAssembly, TestAssemblyPath);
        }
    }

    [global::Android.App.Instrumentation(Name = "com.laqieer.febuildergba.tests.TestInstrumentation")]
    public class TestInstrumentation : global::Android.App.Instrumentation
    {
        const string LogTag = "FEBuilderGBA.Tests";
        const string DefaultResultsPath = "/sdcard/Download";
        const string TestDllName = "FEBuilderGBA.Android.Tests.dll";

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

            // --- Reflect and count test classes BEFORE RunAsync ---
            // This lets CI distinguish trimmer-stripped classes (count=0)
            // from a runtime exception as the cause of zero executed tests.
            int reflectedTestClassCount = CountReflectedTestClasses(out string reflectionDiag);
            global::Android.Util.Log.Info(LogTag,
                $"Reflected test classes found: {reflectedTestClassCount}. {reflectionDiag}");
            if (reflectedTestClassCount == 0)
            {
                global::Android.Util.Log.Error(LogTag,
                    "ZERO test classes found via reflection -- IL trimmer may have removed them. " +
                    "Ensure AndroidLinkMode=None is set in the test project.");
            }

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
            int executedCount = 0;
            string? caughtError = null;
            string? finalResultsXmlPath = null;

            try
            {
                // -----------------------------------------------------------
                // Extract the test assembly DLL from the APK to a real path
                // so xUnit's FileExists guard can open it.
                // -----------------------------------------------------------
                string extractedPath = ExtractTestAssembly(resultsPath);
                global::Android.Util.Log.Info(LogTag,
                    $"Test assembly extracted to: {extractedPath}");

                var entryPoint = new ParityAndroidEntryPoint(resultsPath, bundleDict)
                {
                    TestAssembly = typeof(TestInstrumentation).Assembly,
                    TestAssemblyPath = extractedPath,
                    // Also set the base Tests list for other internal uses.
                    Tests = new[] { typeof(TestInstrumentation).Assembly },
                };

                entryPoint.TestsCompleted += (sender, results) =>
                {
                    // TestRunResult is a struct; access fields directly (no null check).
                    failedCount   = (int)results.FailedTests;
                    passedCount   = (int)results.PassedTests;
                    executedCount = (int)results.ExecutedTests;
                    global::Android.Util.Log.Info(LogTag,
                        $"XHarness completed: passed={passedCount} failed={failedCount} " +
                        $"skipped={results.SkippedTests} total={results.ExecutedTests}");
                };

                await entryPoint.RunAsync();

                finalResultsXmlPath = entryPoint.TestsResultsFinalPath;
                global::Android.Util.Log.Info(LogTag,
                    $"RunAsync finished. Results XML: {finalResultsXmlPath}");

                // Belt-and-suspenders: if the TestsCompleted event never fired
                // (e.g., 0 tests discovered), log and force a failure.
                if (executedCount == 0)
                {
                    global::Android.Util.Log.Warn(LogTag,
                        "Zero tests executed after RunAsync -- " +
                        "likely trimming/discovery failure or empty assembly. " +
                        $"Reflected test classes before run: {reflectedTestClassCount}.");
                    // Force at least one failure so CI does not falsely report PASS.
                    failedCount = Math.Max(1, failedCount);
                }
            }
            catch (Exception ex)
            {
                caughtError = ex.ToString();
                global::Android.Util.Log.Error(LogTag, $"XHarness RunAsync threw: {caughtError}");
                failedCount = Math.Max(1, failedCount);

                // Write the full exception to a file that CI can adb-pull for diagnostics.
                TryWriteErrorFile(resultsPath, caughtError);
            }

            // Verify the XML was actually written and is non-empty.
            // A missing or empty XML after a zero-test run means CI must not report PASS.
            if (finalResultsXmlPath != null)
            {
                try
                {
                    var fi = new FileInfo(finalResultsXmlPath);
                    if (!fi.Exists || fi.Length == 0)
                    {
                        global::Android.Util.Log.Warn(LogTag,
                            $"Results XML missing or empty at {finalResultsXmlPath} -- treating as failure.");
                        failedCount = Math.Max(1, failedCount);
                    }
                }
                catch (Exception xmlCheckEx)
                {
                    global::Android.Util.Log.Warn(LogTag, $"Could not stat results XML: {xmlCheckEx.Message}");
                }
            }

            // Finish with a result bundle. Non-zero return code signals CI failure.
            bool anyFailure = failedCount > 0 || executedCount == 0;
            var result = new global::Android.OS.Bundle();
            result.PutString("results-file-path",   resultsPath);
            result.PutInt("return-code",             anyFailure ? 1 : 0);
            result.PutInt("failed-tests",            failedCount);
            result.PutInt("passed-tests",            passedCount);
            result.PutInt("executed-tests",          executedCount);
            result.PutInt("reflected-test-classes",  reflectedTestClassCount);
            if (caughtError != null)
                result.PutString("error", caughtError.Length > 500 ? caughtError[..500] : caughtError);

            Finish(anyFailure ? global::Android.App.Result.Canceled : global::Android.App.Result.Ok, result);
        }

        /// <summary>
        /// Extract FEBuilderGBA.Android.Tests.dll from the APK zip to a real writable
        /// path (Context.FilesDir) so xUnit's FileExists guard can open it.
        ///
        /// With AndroidUseAssemblyStore=false + AndroidEnableAssemblyCompression=false
        /// the DLL is stored as an individual, uncompressed entry inside the APK.
        /// Common APK layout (probed in order):
        ///   assemblies/FEBuilderGBA.Android.Tests.dll       (primary path)
        ///   assemblies/x86_64/FEBuilderGBA.Android.Tests.dll (arch-specific fallback)
        ///
        /// If none is found, throws an exception whose message lists all .dll entries
        /// in the APK (first 30) so the next CI run shows the actual layout.
        /// </summary>
        string ExtractTestAssembly(string resultsPath)
        {
            string apkPath = Context!.ApplicationInfo!.SourceDir!;
            global::Android.Util.Log.Info(LogTag, $"Opening APK: {apkPath}");

            // Destination: Context.FilesDir is always writable (private app storage).
            string destDir = Context.FilesDir!.AbsolutePath;
            string destPath = Path.Combine(destDir, TestDllName);

            using var apk = ZipFile.OpenRead(apkPath);

            // Probe candidate paths in priority order.
            ZipArchiveEntry? entry =
                apk.GetEntry($"assemblies/{TestDllName}")
                ?? apk.GetEntry($"assemblies/x86_64/{TestDllName}")
                ?? apk.GetEntry($"assemblies/x86/{TestDllName}")
                ?? apk.GetEntry($"assemblies/arm64-v8a/{TestDllName}")
                ?? apk.GetEntry($"assemblies/armeabi-v7a/{TestDllName}")
                ?? apk.Entries.FirstOrDefault(e => e.Name == TestDllName);

            if (entry == null)
            {
                // Collect diagnostic info: list .dll / FEBuilderGBA entries (up to 30).
                var dllEntries = apk.Entries
                    .Where(e => e.FullName.Contains("FEBuilderGBA", StringComparison.OrdinalIgnoreCase)
                             || e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .Take(30)
                    .Select(e => e.FullName)
                    .ToArray();

                string entryList = string.Join("; ", dllEntries);
                string errorMsg =
                    $"Test assembly DLL not found in APK. " +
                    $"AndroidUseAssemblyStore/Compression may not be effective. " +
                    $"Relevant entries ({dllEntries.Length}): {entryList}";

                global::Android.Util.Log.Error(LogTag, errorMsg);
                TryWriteErrorFile(resultsPath, errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            global::Android.Util.Log.Info(LogTag,
                $"Found APK entry: {entry.FullName} ({entry.Length} bytes). " +
                $"Extracting to {destPath}");

            // Extract (overwrite any stale copy).
            entry.ExtractToFile(destPath, overwrite: true);

            global::Android.Util.Log.Info(LogTag,
                $"Extracted {TestDllName}: {new System.IO.FileInfo(destPath).Length} bytes");

            return destPath;
        }

        /// <summary>
        /// Count the number of types in this assembly whose name ends with Tests
        /// or that have at least one method decorated with [Fact] or [Theory].
        /// Catches ReflectionTypeLoadException and logs the loader exceptions
        /// (which reveal missing-dependency load failures) without throwing.
        /// </summary>
        int CountReflectedTestClasses(out string diag)
        {
            try
            {
                var asm = typeof(TestInstrumentation).Assembly;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    // Log every loader exception so CI can see which dependency is missing.
                    var loaderMsgs = rtle.LoaderExceptions
                        .Where(e => e != null)
                        .Select(e => e!.Message)
                        .ToArray();
                    global::Android.Util.Log.Error(LogTag,
                        $"ReflectionTypeLoadException: {rtle.Message}. " +
                        $"LoaderExceptions ({loaderMsgs.Length}): {string.Join("; ", loaderMsgs)}");
                    types = rtle.Types.Where(t => t != null).ToArray()!;
                }

                int count = types.Count(t =>
                    t.Name.EndsWith("Tests", StringComparison.Ordinal) ||
                    t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                     .Any(m => m.GetCustomAttributes(inherit: false)
                               .Any(a => a.GetType().Name is "FactAttribute" or "TheoryAttribute")));

                diag = $"Assembly: {asm.FullName}; total types: {types.Length}; test classes: {count}";
                return count;
            }
            catch (Exception ex)
            {
                diag = $"Reflection failed: {ex.Message}";
                global::Android.Util.Log.Error(LogTag, $"CountReflectedTestClasses threw: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// Write the full exception text to instrumentation-error.txt in the
        /// results directory so CI can adb pull it for diagnostics.
        /// Failures here are silently swallowed -- error file is best-effort.
        /// </summary>
        void TryWriteErrorFile(string resultsPath, string errorText)
        {
            try
            {
                Directory.CreateDirectory(resultsPath);
                string errorFile = Path.Combine(resultsPath, "instrumentation-error.txt");
                File.WriteAllText(errorFile, errorText);
                global::Android.Util.Log.Info(LogTag, $"Wrote error file: {errorFile}");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, $"Could not write error file: {ex.Message}");
            }
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
