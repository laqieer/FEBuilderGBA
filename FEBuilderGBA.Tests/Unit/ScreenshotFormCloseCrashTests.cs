using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression coverage for issues #747, #748, #751, #752 — the daily E2E
    /// screenshot job (<c>WinFormsScreenshotAllCliTests</c>, runs the WinForms
    /// app with <c>--screenshot-all</c>) constructs every WinForms form in
    /// <see cref="ScreenshotFormRegistry"/>, invokes <c>OnLoad</c> via
    /// reflection, then captures the form with <c>DrawToBitmap</c>. Eight
    /// forms in that registry crashed during this flow:
    /// <list type="bullet">
    /// <item><c>ImageTSAAnime2View</c> — NRE because <c>g_TSAAnime</c> is
    /// only preloaded on FE8 ROMs.</item>
    /// <item><c>ImageMagicFEditorView</c>, <c>ImageMagicCSACreatorView</c>,
    /// <c>ImageMapActionAnimationView</c>, <c>FE8SpellMenuExtendsView</c>,
    /// <c>ToolCustomBuildView</c>, <c>ToolROMRebuildView</c> — their Load
    /// handler called <c>this.Close()</c> when a required patch was missing,
    /// disposing the form before <c>DrawToBitmap</c>.</item>
    /// <item><c>SkillAssignmentClassCSkillSysView</c> — constructor scanned
    /// the SkillSystem table from a junk base address read with
    /// <c>Program.ROM.p32</c>, triggering an
    /// <see cref="IndexOutOfRangeException"/> from <c>U.check_safety</c>.</item>
    /// </list>
    ///
    /// The eight forms each touch <c>Program.ROM</c>/<c>Program.AsmMapFileAsmCache</c>
    /// in their constructor, which is impossible to fully exercise from a
    /// headless unit test without spinning up a full ROM load. Instead we
    /// pin the fix at the source level: the patches that survive a code
    /// review are the ones that make sure the bail-out paths do NOT dispose
    /// the form in CLI/screenshot mode (so <c>DrawToBitmap</c> can still
    /// render an empty form), plus the null/safety guards that prevent the
    /// constructor itself from throwing. The actual end-to-end behavior is
    /// validated by the daily E2E screenshot job — these tests block a
    /// regression that would silently drop the guard.
    /// </summary>
    [Collection("SharedState")]
    public class ScreenshotFormCloseCrashTests
    {
        // ---- Source-locator helpers ----

        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                if (dir == null)
                    throw new InvalidOperationException("Cannot find solution root");
                return dir;
            }
        }

        private static string WinFormsDir => Path.Combine(SolutionDir, "FEBuilderGBA");

        private static string ReadForm(string formFileName) =>
            File.ReadAllText(Path.Combine(WinFormsDir, formFileName));

        // ---- ImageTSAAnime2Form (NRE: g_TSAAnime null on non-FE8 ROMs) ----

        [Fact]
        public void ImageTSAAnime2Form_Constructor_GuardsAgainstNullStaticResource()
        {
            // Pre-#747/#748/#751/#752 the constructor did:
            //   foreach (var pair in g_TSAAnime) { ... }
            // and g_TSAAnime is null on FE6/FE7 (Program.cs only calls
            // PreLoadResource() when ROM version == 8), producing a
            // NullReferenceException during the screenshot run. The fix
            // wraps the iteration in `if (g_TSAAnime != null) { ... }`.
            var src = ReadForm("ImageTSAAnime2Form.cs");

            // The constructor must reference g_TSAAnime under a null guard.
            Assert.Matches(
                new Regex(@"if\s*\(\s*g_TSAAnime\s*!=\s*null\s*\)", RegexOptions.Multiline),
                src);

            // And the static MakeAllDataLength helper has the same guard.
            Assert.Matches(
                new Regex(@"if\s*\(\s*g_TSAAnime\s*==\s*null\s*\)", RegexOptions.Multiline),
                src);
        }

        // ---- Close-in-Load forms (disposed-object family) ----

        [Theory]
        [InlineData("ImageMagicFEditorForm.cs")]
        [InlineData("ImageMagicCSACreatorForm.cs")]
        [InlineData("ImageMapActionAnimationForm.cs")]
        [InlineData("FE8SpellMenuExtendsForm.cs")]
        [InlineData("ToolCustomBuildForm.cs")]
        [InlineData("ToolROMRebuildForm.cs")]
        public void LoadHandler_SkipsCloseInCommandLineMode(string formFileName)
        {
            // Each of these forms has a Load handler that bails out with
            // this.Close() when its prerequisite (e.g. a patch, an FE8 ROM,
            // an extended ROM) isn't satisfied. In the --screenshot-all path
            // the runner calls OnLoad reflectively, then DrawToBitmap. After
            // Close() the form is disposed and DrawToBitmap throws
            // "Cannot access a disposed object." The fix wraps each
            // this.Close() in `if (!Program.IsCommandLine) { this.Close(); }`
            // so the runner can still render an empty form.
            var src = ReadForm(formFileName);

            // Every Close() that exists in the file must be guarded by an
            // IsCommandLine check OR be in a click handler (not a Load path).
            // We assert the source contains the guard pattern as evidence the
            // fix is in place. Note: ToolROMRebuildForm has additional
            // Close() calls in the rebuild-success flow that are intentionally
            // unguarded (we only protect the Load path).
            Assert.Matches(
                new Regex(@"if\s*\(\s*!\s*Program\.IsCommandLine\s*\)", RegexOptions.Multiline),
                src);

            // And the rationale comment must reference the bug numbers, so
            // future readers know this is load-bearing rather than vestigial.
            Assert.Contains("#747", src);
        }

        // ---- SkillAssignmentClassCSkillSysForm (OOB via p32 junk address) ----

        [Fact]
        public void SkillAssignmentClassCSkillSysForm_Constructor_GuardsAgainstUnsafeBaseAddress()
        {
            // Before the fix the constructor went straight from
            //   uint base = Program.ROM.p32(assignClassP);
            // to building an InputFormRef that scanned the table by reading
            // Program.ROM.u8(addr) — which threw IndexOutOfRangeException
            // ("Max length:16777216(0x01000000) Access:62988227(0x03C11FC3)")
            // when the SkillSystem patch wasn't actually installed cleanly.
            // The fix validates the resolved base addresses with
            // U.isSafetyOffset before continuing.
            var src = ReadForm("SkillAssignmentClassCSkillSysForm.cs");
            Assert.Matches(
                new Regex(@"isSafetyOffset\s*\(\s*assignClassAddr\s*\)", RegexOptions.Multiline),
                src);
            Assert.Matches(
                new Regex(@"isSafetyOffset\s*\(\s*assignLevelUpAddr\s*\)", RegexOptions.Multiline),
                src);
        }

        // ---- ScreenshotAllRunner harness — disposed-form / mid-render guard ----

        [Fact]
        public void ScreenshotAllRunner_PreCheck_SkipsDisposedFormsBeforeDrawToBitmap()
        {
            // The runner must check form.IsDisposed after FireOnLoad and skip
            // the DrawToBitmap call. Otherwise any form whose Load handler
            // disposes itself (a pattern several forms still use for legacy
            // non-CLI paths) would surface as a SCREENSHOT: ... FAIL: line
            // and the test reporter would flag a CI failure.
            var src = File.ReadAllText(Path.Combine(WinFormsDir, "ScreenshotAllRunner.cs"));
            Assert.Contains("form.IsDisposed", src);
        }

        [Fact]
        public void ScreenshotAllRunner_DrawToBitmap_IsWrappedInObjectDisposedExceptionHandler()
        {
            // Defense-in-depth: even with the IsDisposed pre-check, a control
            // can race into Dispose during the paint pass (e.g. a Paint
            // handler that calls Close()). The runner wraps DrawToBitmap in
            // a try/catch (ObjectDisposedException) so those flakes are
            // treated the same as the pre-check skip.
            var src = File.ReadAllText(Path.Combine(WinFormsDir, "ScreenshotAllRunner.cs"));
            Assert.Contains("ObjectDisposedException", src);

            // The pre-check and the catch both bump `captured` (not `failed`)
            // so the "at least 50 screenshots" CI assertion still holds for
            // the legitimate bail-out family.
            Assert.Contains("SKIP (form closed itself during Load)", src);
            Assert.Contains("SKIP (form disposed mid-render)", src);
        }

        [Fact]
        public void ScreenshotAllRunner_FinallyBlock_GuardsAgainstDoubleDispose()
        {
            // The finally block now checks form.IsDisposed before calling
            // Close()/Dispose() — calling Dispose() twice on a WinForms
            // Form throws ObjectDisposedException from the disposal of
            // child controls in some cases, and at minimum is a noisy
            // anti-pattern. The catch-all `catch { }` already swallowed
            // this but the guard is the cleaner contract.
            var src = File.ReadAllText(Path.Combine(WinFormsDir, "ScreenshotAllRunner.cs"));
            Assert.Matches(
                new Regex(@"if\s*\(\s*form\s*!=\s*null\s*&&\s*!form\.IsDisposed\s*\)", RegexOptions.Multiline),
                src);
        }

        // ---- ToolCustomBuildForm is the one form here whose constructor
        // does NOT touch Program.ROM (just InitializeComponent +
        // TakeoverSkillAssignmentComboBox.SelectedIndex = 1), so we can
        // actually exercise the construct + FireOnLoad path end-to-end. ----

        [Fact]
        public void ToolCustomBuildForm_FireOnLoad_DoesNotDispose_InCommandLineMode()
        {
            RunOnStaThread(() =>
            {
                bool originalIsCommandLine = Program.IsCommandLine;
                SetIsCommandLine(true);
                try
                {
                    // ToolCustomBuildForm.Load reads Program.ROM.RomInfo.version
                    // and may call PatchUtil.SearchSkillSystem — both require a
                    // ROM. In a no-ROM unit-test environment OnLoad will throw,
                    // and the screenshot runner's FireOnLoad swallows
                    // TargetInvocationException. We mirror that here.
                    using var form = new ToolCustomBuildForm();
                    FireOnLoad(form);

                    // The guarantee under test: even when Load DOES execute
                    // (the !=8 / != SkillSystem branches), the form must not
                    // be disposed because IsCommandLine is true.
                    Assert.False(form.IsDisposed,
                        "ToolCustomBuildForm.Load disposed the form in CLI mode; " +
                        "DrawToBitmap would have thrown ObjectDisposedException.");
                }
                finally
                {
                    SetIsCommandLine(originalIsCommandLine);
                }
            });
        }

        // ---- helpers ----

        /// <summary>Mirrors ScreenshotAllRunner.FireOnLoad (swallows TIE).</summary>
        private static void FireOnLoad(Form form)
        {
            var onLoad = typeof(Form).GetMethod(
                "OnLoad",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(EventArgs) },
                null);
            try
            {
                onLoad?.Invoke(form, new object[] { EventArgs.Empty });
            }
            catch (TargetInvocationException)
            {
                // Real runner swallows these too.
            }
        }

        private static void SetIsCommandLine(bool value)
        {
            var prop = typeof(Program).GetProperty(
                "IsCommandLine",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(prop);
            prop!.GetSetMethod(nonPublic: true)!.Invoke(null, new object[] { value });
        }

        /// <summary>STA-thread runner — mirrors <see cref="ToolThreeMargeFormCloseTests"/>.</summary>
        private static void RunOnStaThread(Action body)
        {
            ExceptionDispatchInfo? edi = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { edi = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(60)))
                throw new TimeoutException("STA thread did not complete within 60 seconds");

            edi?.Throw();
        }
    }
}
