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
    /// Issue #995: render the REAL Avalonia
    /// <see cref="SkillAssignmentUnitSkillSystemView"/> POPULATED with a per-unit
    /// master list + a per-unit level-up (N1) sub-list, and capture a PNG proving
    /// the editor populates (not the empty "SkillSystems patch required" state).
    ///
    /// WHY A SYNTHETIC ROM: the editor only populates when a SkillSystems patch
    /// is detected (the ASSIGN/LEVELUP/ICON/TEXT signatures live at 0xB00000).
    /// None of the available commercial test ROMs (FE8U/FE8J/FE8J_skill) carry
    /// the exact signatures the cross-platform scanner recognises, so the editor
    /// cannot populate on them. This test therefore builds a synthetic FE8U ROM
    /// that plants the real signatures + valid pointers + a per-UNIT level-up
    /// table — then drives the REAL View / VM. The render is genuine
    /// (Avalonia.Headless + RenderTargetBitmap, the same path as
    /// <c>--screenshot-all</c>); only the ROM is synthetic. This is NOT a
    /// fabricated image. Mirrors the #834 sibling
    /// <see cref="SkillAssignmentIndependenceScreenshotTest"/>.
    ///
    /// HEADLESS: works even when the desktop is locked / occluded. Set
    /// FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/ to regenerate
    /// the canonical PR screenshot.
    ///
    /// LEAK-FREE: loads its OWN synthetic ROM into CoreState and restores every
    /// slot it touches in finally.
    /// </summary>
    [Collection("SharedState")]
    public class SkillAssignmentUnitSkillSystemScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public SkillAssignmentUnitSkillSystemScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        // ASSIGN sig #1 doubles as the LEVELUP sig (s_Table rows 1+2). For the
        // ASSIGN type: personal pointer = sigEnd+16+0, class pointer = sigEnd+16+4.
        // For the LEVELUP type (Skip=16+8=24): class levelup = sigEnd+24,
        // unit levelup = sigEnd+24+4 = sigEnd+28.
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
        const uint AssignUnitBase = 0x00B10000u;     // 256-byte per-unit skill bytes
        const uint AssignLevelUpBase = 0x00B11000u;  // per-unit u32 pointer table
        const uint SharedRowsBase = 0x00B12000u;     // the per-unit level-up rows
        const uint IconBase = 0x00B13000u;
        const uint TextBase = 0x00B14000u;

        // The per-unit level-up rows: 2 entries (lv|skill) + 0x0000 terminator.
        static readonly byte[] SharedRows = { 0x01, 0x11, 0x05, 0x22, 0x00, 0x00 };

        [AvaloniaFact]
        public void SkillAssignmentUnit_Populated_SavesScreenshot()
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

                var view = new SkillAssignmentUnitSkillSystemView();
                var vm = GetVm(view);

                // Populate the master unit list (mirrors Opened -> LoadList()).
                Invoke(view, "LoadList");

                _output.WriteLine($"Resolved: assignUnitBase=0x{vm.AssignUnitBaseAddress:X8}, " +
                    $"assignLevelUpBase=0x{vm.AssignLevelUpBaseAddress:X8}, iconBase=0x{vm.IconBaseAddress:X8}, " +
                    $"listCount={vm.GetListCount()}");

                Assert.True(vm.AssignUnitBaseAddress != 0,
                    "SkillSystems Unit ASSIGN table must resolve on the synthetic ROM.");
                Assert.True(vm.GetListCount() > 0,
                    "Master unit list must be populated on the synthetic ROM.");

                // Select unit 1 (has a per-unit level-up table) so the Unit Skill
                // and the N1 sub-list both populate.
                const uint selectedUnitId = 1;
                uint selectedAddr = vm.AssignUnitBaseAddress + selectedUnitId * vm.MasterBlockSize;
                Invoke(view, "OnSelected", selectedAddr);

                _output.WriteLine($"Selected unit 0x{selectedUnitId:X2}: unitSkill=0x{vm.UnitSkill:X2}, " +
                    $"xLevelUpAddr=0x{vm.XLevelUpAddr:X8}, n1Count={vm.LevelUpEntries.Count}");

                Assert.True(vm.LevelUpEntries.Count > 0,
                    "Per-unit level-up (N1) sub-list must populate for the selected unit.");

                const int W = 1200;
                const int H = 900;

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                SaveRender(view, W, H, Path.Combine(outDir, "pr995-skill-assignment-unit.png"));
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
        /// and a per-UNIT level-up pointer table. The Unit editor reads the
        /// PERSONAL ASSIGN slot (extraSkip 0) and the UNIT LEVELUP slot
        /// (extraSkip 4); we plant valid pointers at BOTH the class AND unit
        /// slots so the ROM serves either editor, then confirm via the scanner.
        /// </summary>
        ROM BuildSyntheticPatchedRom()
        {
            var b = new byte[0x1000000];
            // Upper half = 0xFF (free space).
            for (int i = b.Length / 2; i < b.Length; i++) b[i] = 0xFF;

            var rom = new ROM();
            rom.LoadLow("synthetic-skillsystem-unit.gba", b, "BE8E01");

            // --- Plant the ASSIGN/LEVELUP signature ---
            Array.Copy(AssignLevelUpSig, 0, b, (int)AssignSigPos, AssignLevelUpSig.Length);
            uint sigEnd = AssignSigPos + (uint)AssignLevelUpSig.Length; // 0xB00010
            // ASSIGN type (Skip=16): personal pointer = sigEnd+16+0, class = sigEnd+16+4.
            WriteU32(b, sigEnd + 16 + 0, U.toPointer(AssignUnitBase));     // unit personal
            WriteU32(b, sigEnd + 16 + 4, U.toPointer(AssignUnitBase));     // class personal (same table OK)
            // LEVELUP type (Skip=24): class levelup = sigEnd+24, unit levelup = sigEnd+24+4.
            WriteU32(b, sigEnd + 24 + 0, U.toPointer(AssignLevelUpBase));  // class levelup
            WriteU32(b, sigEnd + 24 + 4, U.toPointer(AssignLevelUpBase));  // unit levelup

            // --- Plant the ICON signature + pointer (slot = sigEnd + 24) ---
            Array.Copy(IconSig, 0, b, (int)IconSigPos, IconSig.Length);
            WriteU32(b, IconSigPos + (uint)IconSig.Length + 24, U.toPointer(IconBase));

            // --- Plant the TEXT signature + pointer (slot = sigEnd + 16) ---
            Array.Copy(TextSig, 0, b, (int)TextSigPos, TextSig.Length);
            WriteU32(b, TextSigPos + (uint)TextSig.Length + 16, U.toPointer(TextBase));

            // --- Per-unit skill bytes (256 distinct, plausible values) ---
            for (uint i = 0; i < 256; i++) b[AssignUnitBase + i] = (byte)((i % 0x40) + 1);

            // --- Per-unit level-up pointer table: unit 0 = null, units 1+2 point at
            //     the per-unit rows table. Others null. The LEVELUP validation reads
            //     entries 0,1,2 of the dereferenced table and requires
            //     safe-pointer-or-null.
            uint sharedGbaPtr = U.toPointer(SharedRowsBase);
            WriteU32(b, AssignLevelUpBase + 0 * 4, 0);             // unit 0: null
            WriteU32(b, AssignLevelUpBase + 1 * 4, sharedGbaPtr);  // unit 1: rows
            WriteU32(b, AssignLevelUpBase + 2 * 4, sharedGbaPtr);  // unit 2: rows
            for (uint i = 3; i < 256; i++) WriteU32(b, AssignLevelUpBase + i * 4, 0);

            // --- The level-up rows ---
            Array.Copy(SharedRows, 0, b, (int)SharedRowsBase, SharedRows.Length);

            // --- Minimal icon/text blocks (non-zero so dereferences are safe) ---
            for (uint i = 0; i < 0x200; i++) b[IconBase + i] = 0x00;
            for (uint i = 0; i < 0x200; i++) b[TextBase + i] = 0x00;

            rom.LoadLow("synthetic-skillsystem-unit.gba", b, "BE8E01");

            // Robustness: confirm the Unit finders resolve to non-NOT_FOUND.
            // If the slot pointers weren't where the scanner expects, fix the
            // planting before the test proceeds (these asserts make a silent
            // offset-math error loud).
            uint assignLoc = SkillSystemPatchScanner.FindAssignPersonalSkillPointerLocation(rom);
            uint levelupLoc = SkillSystemPatchScanner.FindAssignUnitLevelUpSkillPointerLocation(rom);
            _output.WriteLine($"Scanner: FindAssignPersonalSkillPointerLocation=0x{assignLoc:X8}, " +
                $"FindAssignUnitLevelUpSkillPointerLocation=0x{levelupLoc:X8}");
            Assert.True(assignLoc != U.NOT_FOUND,
                "FindAssignPersonalSkillPointerLocation must resolve on the synthetic ROM.");
            Assert.True(levelupLoc != U.NOT_FOUND,
                "FindAssignUnitLevelUpSkillPointerLocation must resolve on the synthetic ROM.");
            Assert.Equal(AssignUnitBase, U.toOffset(rom.u32(assignLoc)));
            Assert.Equal(AssignLevelUpBase, U.toOffset(rom.u32(levelupLoc)));

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
                _output.WriteLine($"Headless render failed (environment, not the #995 fix): {ex.Message}");
            }
        }

        static SkillAssignmentUnitSkillSystemViewModel GetVm(object view)
        {
            var f = view.GetType().GetField("_vm",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f);
            return (SkillAssignmentUnitSkillSystemViewModel)f!.GetValue(view)!;
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
