// SPDX-License-Identifier: GPL-3.0-or-later
//
// Direct reflection-based on-device test runner for FEBuilderGBA.Android.Tests (#1125).
//
// WHY NO xUnit FILE-DISCOVERY:
//   .NET 9 Android embeds managed assemblies as ELF-wrapped native libraries
//   (lib/<abi>/lib_<Name>.dll.so) rather than as plain PE .dll zip entries.
//   xUnit's XunitFrontController calls Guard.FileExists() which requires a real
//   on-disk PE file; assembly.Location is empty on .NET 9 Android, so xUnit
//   throws "File not found" before executing any test.
//
//   This runner sidesteps xUnit file-discovery entirely: it references the two
//   test classes STATICALLY (defeating any residual IL trimmer), then invokes
//   every [Fact]/[SkippableFact] method directly via Reflection on the
//   in-memory, linked assembly -- executing the IDENTICAL golden assertions that
//   run on Linux/macOS/Windows CI.
//
// HOW IT WORKS:
//   1. CopyTuffyFontFromAssets() -- copies Tuffy-Regular.ttf from APK assets
//      into AppContext.BaseDirectory/Fonts/ before any test runs, so the
//      hard-coded LoadTuffy() path in SkiaRenderByteParityTests resolves.
//   2. RunTestsAsync() builds a list of [Fact]/[SkippableFact] methods from
//      the two statically-referenced test types, invokes each, catches
//      Xunit.SkipException (thrown by Skip.If/Skip.Unless from the
//      xunit.SkippableFact package), and records Pass/Fail/Skip.
//   3. Writes a xUnit v2-compatible TestResults.xml that the CI script parses.
//   4. Reports counts via the ADB instrumentation bundle so the CI script can
//      verify total >= 4 executed tests without even reading the XML.
//
// NAMESPACE NOTE: Android SDK types are referenced via global::Android.* to
// prevent the local FEBuilderGBA.Android sub-namespace from shadowing the top-
// level Android namespace from Mono.Android.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;

// Static references defeat IL trimmer: the linker sees these as live roots
// and preserves the entire test class hierarchy including their dependencies.
using FEBuilderGBA.Core.Tests;

namespace FEBuilderGBA.Android.Tests
{
    [global::Android.App.Instrumentation(Name = "com.laqieer.febuildergba.tests.TestInstrumentation")]
    public class TestInstrumentation : global::Android.App.Instrumentation
    {
        const string LogTag = "FEBuilderGBA.Tests";
        const string DefaultResultsPath = "/sdcard/Download";

        // Static references to the test types so the IL trimmer sees them as
        // live roots and does NOT strip the test methods or their dependencies.
        static readonly Type[] TestTypes = new[]
        {
            typeof(SkiaRenderByteParityTests),
            typeof(SkiaSharpVersionGuardTests),
        };

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
            await Task.Yield(); // ensure we're off the main thread

            // Parse the results path from the ADB instrument -e arguments bundle.
            string resultsPath = DefaultResultsPath;
            if (_arguments != null)
            {
                string? val = _arguments.GetString("results-file-path");
                if (!string.IsNullOrEmpty(val))
                    resultsPath = val;
            }

            global::Android.Util.Log.Info(LogTag, $"Reflection runner: results path = {resultsPath}");

            int passed = 0, failed = 0, skipped = 0;
            string? runnerError = null;
            var records = new List<TestRecord>();

            try
            {
                // Ensure the results directory exists before writing.
                // /sdcard/Download works on the API-34 google_apis emulator (test APK
                // is not sandboxed the same way as production apps). If this ever fails
                // on a stricter image, switch to Context.GetExternalFilesDir(null).
                Directory.CreateDirectory(resultsPath);

                foreach (var type in TestTypes)
                {
                    object? instance;
                    try
                    {
                        instance = Activator.CreateInstance(type);
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error(LogTag,
                            $"Could not create instance of {type.FullName}: {ex}");
                        // Record a synthetic failure for the entire class.
                        records.Add(new TestRecord(
                            type.FullName + ".<ctor>",
                            TestOutcome.Fail,
                            0.0,
                            failMessage: ex.GetType().Name + ": " + ex.Message,
                            failStack: ex.StackTrace ?? ""));
                        failed++;
                        continue;
                    }

                    // Collect [Fact] / [SkippableFact] methods.
                    // [SkippableFact] derives from [Fact] in xunit.SkippableFact, so a
                    // single check for Xunit.FactAttribute (inherit:true) catches BOTH.
                    var methods = type
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m =>
                            m.GetCustomAttributes(inherit: true)
                             .Any(a => a is Xunit.FactAttribute))
                        .ToArray();

                    global::Android.Util.Log.Info(LogTag,
                        $"Type {type.Name}: {methods.Length} [Fact]/[SkippableFact] method(s)");

                    foreach (var method in methods)
                    {
                        string testName = type.FullName + "." + method.Name;
                        global::Android.Util.Log.Info(LogTag, $"  RUN  {testName}");
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            // If the Fact method returns a Task (async), await it so
                            // async test failures are not silently swallowed.
                            var returnVal = method.Invoke(instance, null);
                            if (returnVal is System.Threading.Tasks.Task t)
                                t.GetAwaiter().GetResult();
                            sw.Stop();
                            passed++;
                            records.Add(new TestRecord(testName, TestOutcome.Pass, sw.Elapsed.TotalSeconds));
                            global::Android.Util.Log.Info(LogTag,
                                $"  PASS {testName} ({sw.ElapsedMilliseconds} ms)");
                        }
                        catch (TargetInvocationException tie)
                        {
                            sw.Stop();
                            var inner = tie.InnerException ?? tie;
                            // xunit.SkippableFact throws Xunit.SkipException for Skip.If/Skip.Unless
                            if (inner is Xunit.SkipException skipEx)
                            {
                                skipped++;
                                records.Add(new TestRecord(testName, TestOutcome.Skip,
                                    sw.Elapsed.TotalSeconds, skipReason: skipEx.Message));
                                global::Android.Util.Log.Info(LogTag,
                                    $"  SKIP {testName}: {skipEx.Message}");
                            }
                            else
                            {
                                failed++;
                                records.Add(new TestRecord(testName, TestOutcome.Fail,
                                    sw.Elapsed.TotalSeconds,
                                    failMessage: inner.GetType().Name + ": " + inner.Message,
                                    failStack: inner.StackTrace ?? ""));
                                global::Android.Util.Log.Error(LogTag,
                                    $"  FAIL {testName}: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
                            }
                        }
                    }
                }

                // Write TestResults.xml in xUnit v2 / JUnit format.
                WriteResultsXml(resultsPath, records, passed, failed, skipped);

                // On all-pass, write a short success note to stdout (no error file).
                if (failed == 0)
                {
                    global::Android.Util.Log.Info(LogTag,
                        $"All tests passed: passed={passed} skipped={skipped} failed=0");
                }
                else
                {
                    // Write human-readable failure summary to instrumentation-error.txt.
                    TryWriteErrorFile(resultsPath, records);
                }
            }
            catch (Exception ex)
            {
                runnerError = ex.ToString();
                global::Android.Util.Log.Error(LogTag, $"Runner-level exception: {runnerError}");
                failed = Math.Max(1, failed);
                TryWriteRunnerErrorFile(resultsPath, runnerError);
            }

            int executed = passed + failed;
            bool anyFailure = failed > 0 || executed == 0;

            var result = new global::Android.OS.Bundle();
            result.PutString("results-file-path",  resultsPath);
            result.PutInt("return-code",            anyFailure ? 1 : 0);
            result.PutInt("passed-tests",           passed);
            result.PutInt("failed-tests",           failed);
            result.PutInt("skipped-tests",          skipped);
            result.PutInt("executed-tests",         executed);
            if (runnerError != null)
                result.PutString("error",
                    runnerError.Length > 500 ? runnerError[..500] : runnerError);

            Finish(
                anyFailure ? global::Android.App.Result.Canceled : global::Android.App.Result.Ok,
                result);
        }

        // -----------------------------------------------------------------------
        // XML writer -- xUnit v2 schema (same as what the CI script already greps)
        // -----------------------------------------------------------------------
        static void WriteResultsXml(
            string resultsPath,
            List<TestRecord> records,
            int passed, int failed, int skipped)
        {
            string xmlPath = Path.Combine(resultsPath, "TestResults.xml");
            int total = passed + failed + skipped;
            double totalSec = records.Sum(r => r.DurationSec);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<assemblies>");
            sb.AppendLine(
                $"  <assembly name=\"FEBuilderGBA.Android.Tests\"" +
                $" total=\"{total}\"" +
                $" passed=\"{passed}\"" +
                $" failed=\"{failed}\"" +
                $" skipped=\"{skipped}\"" +
                $" errors=\"0\"" +
                $" time=\"{totalSec:F3}\">");
            sb.AppendLine(
                $"    <collection total=\"{total}\"" +
                $" passed=\"{passed}\"" +
                $" failed=\"{failed}\"" +
                $" skipped=\"{skipped}\"" +
                $" name=\"Reflection Runner\"" +
                $" time=\"{totalSec:F3}\">");

            foreach (var r in records)
            {
                string outcome = r.Outcome switch
                {
                    TestOutcome.Pass => "Pass",
                    TestOutcome.Fail => "Fail",
                    TestOutcome.Skip => "Skip",
                    _ => "Fail"
                };
                sb.AppendLine(
                    $"      <test name=\"{EscapeXml(r.Name)}\"" +
                    $" result=\"{outcome}\"" +
                    $" time=\"{r.DurationSec:F3}\">");
                if (r.Outcome == TestOutcome.Fail)
                {
                    sb.AppendLine("        <failure>");
                    sb.AppendLine(
                        $"          <message><![CDATA[{r.FailMessage ?? string.Empty}]]></message>");
                    sb.AppendLine(
                        $"          <stack-trace><![CDATA[{r.FailStack ?? string.Empty}]]></stack-trace>");
                    sb.AppendLine("        </failure>");
                }
                else if (r.Outcome == TestOutcome.Skip)
                {
                    sb.AppendLine(
                        $"        <reason><![CDATA[{r.SkipReason ?? string.Empty}]]></reason>");
                }
                sb.AppendLine("      </test>");
            }

            sb.AppendLine("    </collection>");
            sb.AppendLine("  </assembly>");
            sb.AppendLine("</assemblies>");

            File.WriteAllText(xmlPath, sb.ToString(), Encoding.UTF8);
            global::Android.Util.Log.Info(LogTag, $"TestResults.xml written: {xmlPath}");
        }

        static string EscapeXml(string s) =>
            SecurityElement.Escape(s) ?? s;

        // -----------------------------------------------------------------------
        // Error file helpers
        // -----------------------------------------------------------------------
        void TryWriteErrorFile(string resultsPath, List<TestRecord> records)
        {
            try
            {
                Directory.CreateDirectory(resultsPath);
                string errorFile = Path.Combine(resultsPath, "instrumentation-error.txt");
                var sb = new StringBuilder();
                foreach (var r in records.Where(r => r.Outcome == TestOutcome.Fail))
                {
                    sb.AppendLine($"FAIL: {r.Name}");
                    sb.AppendLine($"  {r.FailMessage}");
                    if (!string.IsNullOrEmpty(r.FailStack))
                        sb.AppendLine(r.FailStack);
                    sb.AppendLine();
                }
                File.WriteAllText(errorFile, sb.ToString(), Encoding.UTF8);
                global::Android.Util.Log.Info(LogTag, $"Wrote error file: {errorFile}");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, $"Could not write error file: {ex}");
            }
        }

        void TryWriteRunnerErrorFile(string resultsPath, string error)
        {
            try
            {
                Directory.CreateDirectory(resultsPath);
                string errorFile = Path.Combine(resultsPath, "instrumentation-error.txt");
                File.WriteAllText(errorFile, error, Encoding.UTF8);
                global::Android.Util.Log.Info(LogTag, $"Wrote runner error file: {errorFile}");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, $"Could not write runner error file: {ex}");
            }
        }

        // -----------------------------------------------------------------------
        // Font bootstrapper
        // -----------------------------------------------------------------------
        /// <summary>
        /// Copy Tuffy-Regular.ttf from APK assets into
        /// AppContext.BaseDirectory/Fonts/Tuffy-Regular.ttf so that the
        /// hard-coded LoadTuffy() in SkiaRenderByteParityTests resolves.
        /// Wrapped in try/catch: a font-copy failure does NOT abort the run --
        /// the image-parity + runtime-version-guard tests do not need the font.
        /// </summary>
        void CopyTuffyFontFromAssets()
        {
            try
            {
                string fontsDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
                string fontDest = Path.Combine(fontsDir, "Tuffy-Regular.ttf");

                Directory.CreateDirectory(fontsDir);

                var assets = Context?.Assets;
                if (assets == null)
                {
                    global::Android.Util.Log.Warn(LogTag,
                        "Instrumentation context Assets is null -- font parity tests may fail.");
                    return;
                }
                using var assetStream = assets.Open("Tuffy-Regular.ttf");
                if (assetStream == null)
                {
                    global::Android.Util.Log.Warn(LogTag,
                        "Font asset 'Tuffy-Regular.ttf' not found in APK -- font parity tests may fail.");
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
                    $"Font copy from assets failed (font parity tests will report missing font): {ex}");
            }
        }

        // -----------------------------------------------------------------------
        // Internal record types
        // -----------------------------------------------------------------------
        enum TestOutcome { Pass, Fail, Skip }

        sealed class TestRecord
        {
            public string Name { get; }
            public TestOutcome Outcome { get; }
            public double DurationSec { get; }
            public string? FailMessage { get; }
            public string? FailStack { get; }
            public string? SkipReason { get; }

            public TestRecord(
                string name,
                TestOutcome outcome,
                double durationSec,
                string? failMessage = null,
                string? failStack = null,
                string? skipReason = null)
            {
                Name       = name;
                Outcome    = outcome;
                DurationSec = durationSec;
                FailMessage = failMessage;
                FailStack   = failStack;
                SkipReason  = skipReason;
            }
        }
    }
}
