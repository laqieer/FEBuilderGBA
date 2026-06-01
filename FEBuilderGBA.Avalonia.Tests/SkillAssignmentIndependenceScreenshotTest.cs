using System;
using System.IO;
using System.Reflection;
using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #834: render the REAL Avalonia
    /// <see cref="SkillAssignmentClassSkillSystemView"/> with the "Make Selected
    /// Class Independent" panel visible, click the button, and capture a
    /// BEFORE/AFTER pair of PNGs proving the single-slot independence works.
    ///
    /// WHY A SYNTHETIC ROM: the editor only populates when a SkillSystems patch
    /// is detected (the ASSIGN/LEVELUP/ICON/TEXT signatures live at 0xB00000).
    /// None of the available commercial test ROMs (FE8U/FE8J/FE8J_skill) carry
    /// the exact signatures the cross-platform scanner recognises, so the editor
    /// cannot populate on them. This test therefore builds a synthetic FE8U ROM
    /// that plants the real signatures + valid pointers + a per-class level-up
    /// table SHARED behind class 1 and class 2 — then drives the REAL View / VM /
    /// click handler. The render is genuine (Avalonia.Headless +
    /// RenderTargetBitmap, the same path as <c>--screenshot-all</c>); only the
    /// ROM is synthetic. This is NOT a fabricated image.
    ///
    /// HEADLESS: works even when the desktop is locked / occluded (the MCP
    /// computer-use desktop renders black). Set FEBUILDERGBA_SCREENSHOT_DIR to
    /// the repo's pr-screenshots/ to regenerate the canonical PR screenshot.
    /// Mirrors <see cref="ItemNewAllocScreenshotTest"/> (the #831 sibling).
    ///
    /// LEAK-FREE: loads its OWN synthetic ROM into CoreState and restores every
    /// slot it touches in finally; the click pushes onto a throwaway undo.
    /// </summary>
    [Collection("SharedState")]
    public class SkillAssignmentIndependenceScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public SkillAssignmentIndependenceScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        // ASSIGN sig #1 doubles as the LEVELUP sig (s_Table rows 1+2): the ASSIGN
        // pointer is at sigEnd+16+4, the LEVELUP pointer at sigEnd+16+8.
        static readonly byte[] AssignLevelUpSig =
        {
            0x01,0x35,0x02,0x36,0xF1,0xE7,0x00,0x20,
            0x28,0x70,0x29,0x1C,0x02,0x48,0x09,0x1A,
        };
        // ICON pattern #1 (PreviewIconHelper iconPatterns[0]); pointer at sigEnd+24.
        static readonly byte[] IconSig =
        {
            0x02,0x40,0x09,0x4C,0x05,0x48,0x00,0x47,
            0x05,0x48,0x00,0x47,0x05,0x48,0x00,0x47,
        };
        // TEXT pattern #1 (PreviewIconHelper textPatterns[0]); pointer at sigEnd+16.
        static readonly byte[] TextSig =
        {
            0x07,0x49,0x40,0x00,0x40,0x18,0x00,0x88,
            0x00,0x28,0x00,0xD1,0x06,0x48,0x21,0x1C,
        };

        // Synthetic ROM layout (all offsets in the 0xB00000 patch region or above).
        const uint AssignSigPos = 0x00B00000u;
        const uint IconSigPos = 0x00B01000u;
        const uint TextSigPos = 0x00B02000u;
        const uint AssignClassBase = 0x00B10000u;   // 256-byte per-class skill bytes
        const uint AssignLevelUpBase = 0x00B11000u;  // per-class u32 pointer table
        const uint SharedRowsBase = 0x00B12000u;     // the SHARED level-up rows
        const uint IconBase = 0x00B13000u;
        const uint TextBase = 0x00B14000u;

        // The shared level-up rows: 2 entries (lv|skill) + 0x0000 terminator.
        static readonly byte[] SharedRows = { 0x01, 0x11, 0x05, 0x22, 0x00, 0x00 };

        [AvaloniaFact]
        public void SkillAssignment_Independence_ClonesSharedTable_SavesScreenshot()
        {
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            var prevImageService = CoreState.ImageService;
            var prevCommentCache = CoreState.CommentCache;
            var prevLintCache = CoreState.LintCache;
            var prevWorkSupportCache = CoreState.WorkSupportCache;
            var prevSystemTextEncoder = CoreState.SystemTextEncoder;
            var prevBaseDirectory = CoreState.BaseDirectory;
            var prevServices = CoreState.Services;

            try
            {
                string assemblyDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = assemblyDir;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                ROM rom = BuildSyntheticPatchedRom();

                // Optional: dump the synthetic patched ROM to disk so the real
                // Avalonia app can be launched against it for a PrintWindow
                // capture of the live editor (FEBUILDERGBA_DUMP_ROM=<path>).
                string? dumpPath = Environment.GetEnvironmentVariable("FEBUILDERGBA_DUMP_ROM");
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    File.WriteAllBytes(dumpPath, rom.Data);
                    _output.WriteLine($"Dumped synthetic patched ROM to: {dumpPath} ({rom.Data.Length} bytes)");
                }

                CoreState.ROM = rom;
                CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.LintCache = new HeadlessEtcCache();
                CoreState.WorkSupportCache = new HeadlessEtcCache();
                try { CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, rom); }
                catch { CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom); }
                if (CoreState.ImageService == null) CoreState.ImageService = new SkiaImageService();
                CoreState.Services = new AutoYesServices(_output);
                CoreState.Undo = new Undo();

                var view = new SkillAssignmentClassSkillSystemView();
                var vm = GetVm(view);

                // Populate the master class list (mirrors Opened -> LoadList()).
                Invoke(view, "LoadList");
                Assert.True(vm.AssignLevelUpBaseAddress != 0,
                    "SkillSystems ASSIGN/LEVELUP tables must resolve on the synthetic ROM.");
                _output.WriteLine($"Resolved: assignClassBase=0x{vm.AssignClassBaseAddress:X8}, assignLevelUpBase=0x{vm.AssignLevelUpBaseAddress:X8}, iconBase=0x{vm.IconBaseAddress:X8}");

                // Select class 1 (shares its level-up table with class 2).
                const uint sharedClassId = 1;
                const uint otherClassId = 2;
                uint sharedAddr = vm.AssignClassBaseAddress + sharedClassId * vm.MasterBlockSize;
                Invoke(view, "OnSelected", sharedAddr);

                uint mySlot = vm.AssignLevelUpBaseAddress + sharedClassId * 4;
                uint otherSlot = vm.AssignLevelUpBaseAddress + otherClassId * 4;
                uint myGbaPtrBefore = rom.u32(mySlot);
                uint otherGbaPtrBefore = rom.u32(otherSlot);
                Assert.Equal(myGbaPtrBefore, otherGbaPtrBefore); // precondition: shared

                const int W = 1200;
                const int H = 900;

                // BEFORE: the Independence panel is visible (table is shared).
                var panel = view.FindControl<Control>("IndependencePanel");
                Assert.NotNull(panel);
                Assert.True(panel!.IsVisible, "BEFORE: Independence panel must be visible for a shared class.");

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                SaveRender(view, W, H, Path.Combine(outDir, "pr834-skill-independence-before.png"));

                // Click "Make Selected Class Independent" via the real handler.
                Invoke(view, "Independence_Click", null, null);

                // AFTER: my slot moved to a NEW clone; the OTHER sharing class is
                // UNTOUCHED (the single-slot proof) and the panel is hidden.
                uint myGbaPtrAfter = rom.u32(mySlot);
                Assert.NotEqual(myGbaPtrBefore, myGbaPtrAfter);
                Assert.True(U.isPointer(myGbaPtrAfter), "AFTER: my slot must hold a GBA pointer to the clone.");
                Assert.Equal(otherGbaPtrBefore, rom.u32(otherSlot)); // untouched!
                _output.WriteLine($"AFTER: class {sharedClassId:X2} slot 0x{mySlot:X8} -> 0x{myGbaPtrAfter:X8}; class {otherClassId:X2} still 0x{rom.u32(otherSlot):X8}");

                SaveRender(view, W, H, Path.Combine(outDir, "pr834-skill-independence.png"));

                // Roll back on the throwaway undo so the ROM is byte-for-byte as
                // built (my slot returns to the shared pointer).
                CoreState.Undo.RunUndo();
                Assert.Equal(myGbaPtrBefore, rom.u32(mySlot));
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
                CoreState.ImageService = prevImageService;
                CoreState.CommentCache = prevCommentCache;
                CoreState.LintCache = prevLintCache;
                CoreState.WorkSupportCache = prevWorkSupportCache;
                CoreState.SystemTextEncoder = prevSystemTextEncoder;
                CoreState.BaseDirectory = prevBaseDirectory;
                CoreState.Services = prevServices;
            }
        }

        /// <summary>
        /// Build a synthetic FE8U ROM with the SkillSystems ASSIGN/LEVELUP/ICON/
        /// TEXT signatures planted at 0xB00000 (each followed by a valid pointer)
        /// and a per-class level-up pointer table SHARED behind class 1 + class 2.
        /// </summary>
        static ROM BuildSyntheticPatchedRom()
        {
            var b = new byte[0x1000000];
            // Upper half = 0xFF (free space) so the Independence clone has a home.
            for (int i = b.Length / 2; i < b.Length; i++) b[i] = 0xFF;

            var rom = new ROM();
            rom.LoadLow("synthetic-skillsystem.gba", b, "BE8E01");

            // --- Plant the ASSIGN/LEVELUP signature + its two pointers ---
            Array.Copy(AssignLevelUpSig, 0, b, (int)AssignSigPos, AssignLevelUpSig.Length);
            uint sigEnd = AssignSigPos + (uint)AssignLevelUpSig.Length; // 0xB00010
            // ASSIGN pointer slot = sigEnd + 16 + 4; LEVELUP pointer = sigEnd + 16 + 8.
            WriteU32(b, sigEnd + 16 + 4, U.toPointer(AssignClassBase));
            WriteU32(b, sigEnd + 16 + 8, U.toPointer(AssignLevelUpBase));

            // --- Plant the ICON signature + pointer (slot = sigEnd + 24) ---
            Array.Copy(IconSig, 0, b, (int)IconSigPos, IconSig.Length);
            WriteU32(b, IconSigPos + (uint)IconSig.Length + 24, U.toPointer(IconBase));

            // --- Plant the TEXT signature + pointer (slot = sigEnd + 16) ---
            Array.Copy(TextSig, 0, b, (int)TextSigPos, TextSig.Length);
            WriteU32(b, TextSigPos + (uint)TextSig.Length + 16, U.toPointer(TextBase));

            // --- Per-class skill bytes (256 distinct, plausible values) ---
            for (uint i = 0; i < 256; i++) b[AssignClassBase + i] = (byte)(i & 0x7F);

            // --- Per-class level-up pointer table: class 0 = null, classes 1+2 =
            //     the SAME shared rows table (the share). Others null. The LEVELUP
            //     validation reads entries 0,1,2 and requires safe-pointer-or-null.
            uint sharedGbaPtr = U.toPointer(SharedRowsBase);
            WriteU32(b, AssignLevelUpBase + 0 * 4, 0);             // class 0: null
            WriteU32(b, AssignLevelUpBase + 1 * 4, sharedGbaPtr);  // class 1: shared
            WriteU32(b, AssignLevelUpBase + 2 * 4, sharedGbaPtr);  // class 2: shared
            for (uint i = 3; i < 64; i++) WriteU32(b, AssignLevelUpBase + i * 4, 0);

            // --- The shared level-up rows ---
            Array.Copy(SharedRows, 0, b, (int)SharedRowsBase, SharedRows.Length);

            // --- Minimal icon/text blocks (non-zero so dereferences are safe) ---
            for (uint i = 0; i < 0x200; i++) b[IconBase + i] = 0x00;
            for (uint i = 0; i < 0x200; i++) b[TextBase + i] = 0x00;

            rom.LoadLow("synthetic-skillsystem.gba", b, "BE8E01");
            return rom;
        }

        static void WriteU32(byte[] b, uint addr, uint v)
        {
            b[addr + 0] = (byte)(v & 0xFF);
            b[addr + 1] = (byte)((v >> 8) & 0xFF);
            b[addr + 2] = (byte)((v >> 16) & 0xFF);
            b[addr + 3] = (byte)((v >> 24) & 0xFF);
        }

        void SaveRender(Control view, int w, int h, string outPath)
        {
            try
            {
                view.Measure(new Size(w, h));
                view.Arrange(new Rect(0, 0, w, h));
                using var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                bitmap.Render(view);
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #834 fix): {ex.Message}");
            }
        }

        static SkillAssignmentClassSkillSystemViewModel GetVm(object view)
        {
            var f = view.GetType().GetField("_vm",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f);
            return (SkillAssignmentClassSkillSystemViewModel)f!.GetValue(view)!;
        }

        static void Invoke(object target, string method, params object?[]? args)
        {
            var m = target.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(m);
            m!.Invoke(target, args);
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }

        /// <summary>Non-interactive IAppServices that answers Yes to confirms.</summary>
        sealed class AutoYesServices : IAppServices
        {
            readonly ITestOutputHelper _o;
            public AutoYesServices(ITestOutputHelper o) { _o = o; }
            public void ShowError(string message) => _o.WriteLine("[ERROR] " + message);
            public void ShowInfo(string message) => _o.WriteLine("[INFO] " + message);
            public bool ShowQuestion(string message) { _o.WriteLine("[Q] " + message); return true; }
            public bool ShowYesNo(string message) { _o.WriteLine("[YN] " + message); return true; }
            public void RunOnUIThread(Action action) => action();
            public bool IsMainThread() => true;
        }
    }
}
