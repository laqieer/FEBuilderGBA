// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for ImageUnitPaletteView (#397).
//
// Closes the 45 WF-only labels on ImageUnitPaletteForm by adding the
// following Avalonia controls:
//   - Address-bar infra (ReadStartAddress / ReadCount / Reload / Size /
//     SelectedAddressPrefix / Address / Write / FilterLabel / Expand).
//   - Meta panel (Pointer / Identifier / IdentifierBreakdown / UnitClassAndAnime /
//     BattleAnime / NewAlloc / Comment).
//   - Palette tab: 16 row-number labels + R/G/B column headers + 16 R/G/B
//     NumericUpDowns + 16 swatch preview borders + PaletteAddress / PaletteType /
//     Zoom / PaletteOverrideALL / PaletteWrite / Clipboard / Export / Import /
//     UNDO / REDO controls.
//   - 3-tab structure (Edit / Palette / Search Tools).
//
// Asserts WF row-acceptance parity (P12==0 with name!=0 still loads). Asserts
// density verdict moves to Verdict.Low after the controls land. Asserts
// l10n coverage zero untranslated for ja+zh (ko.txt does not exist; this
// matches the project-wide gap-sweep precedent).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImageUnitPaletteParityTests
    {
        static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is T candidate)
                {
                    var aid = AutomationProperties.GetAutomationId(candidate);
                    if (aid == automationId) return candidate;
                }
            }
            return null;
        }

        static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        // ===== WU1: Address-bar + meta panel =====

        [AvaloniaFact]
        public void View_Hosts_AddressBar_InfraControls()
        {
            var view = new ImageUnitPaletteView();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_ReadStartAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_ReadCount_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_Reload_Button"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_Size_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_SelectedAddressPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_Address_Label"));
            // ImageUnitPalette_Write_Label was an inert stray header label
            // intentionally removed in #984 (it looked like a non-functional
            // "Write" text after the Selected Address bar). The real Write
            // affordance is the ImageUnitPalette_Write_Button on the Edit tab.
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_FilterLabel_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_Expand_Button"));
        }

        [AvaloniaFact]
        public void View_Hosts_MetaPanel_Controls()
        {
            var view = new ImageUnitPaletteView();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_Pointer_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_Identifier_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_IdentifierBreakdown_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_UnitClassAndAnime_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_BattleAnime_Label"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_NewAlloc_Button"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "ImageUnitPalette_Comment_Input"));
        }

        // ===== WU2: Palette tab =====

        [AvaloniaFact]
        public void View_Hosts_PaletteSwatchControls()
        {
            var view = new ImageUnitPaletteView();
            // R/G/B column header labels
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_R_Header_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_G_Header_Label"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_B_Header_Label"));
            // 16 row-number labels (1..16) + 16 RGB inputs + 16 swatch previews
            for (int i = 1; i <= 16; i++)
            {
                Assert.NotNull(FindByAutomationId<TextBlock>(view, $"ImageUnitPalette_Index{i}_Label"));
                Assert.NotNull(FindByAutomationId<NumericUpDown>(view, $"ImageUnitPalette_R{i}_Input"));
                Assert.NotNull(FindByAutomationId<NumericUpDown>(view, $"ImageUnitPalette_G{i}_Input"));
                Assert.NotNull(FindByAutomationId<NumericUpDown>(view, $"ImageUnitPalette_B{i}_Input"));
                Assert.NotNull(FindByAutomationId<Border>(view, $"ImageUnitPalette_Swatch{i}_Image"));
            }
        }

        [AvaloniaFact]
        public void View_Hosts_PaletteCommands_And_Combos()
        {
            var view = new ImageUnitPaletteView();
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_PaletteAddress_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "ImageUnitPalette_PaletteAddress_Input"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_PaletteType_Label"));
            Assert.NotNull(FindByAutomationId<ComboBox>(view, "ImageUnitPalette_PaletteType_Combo"));
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_Zoom_Label"));
            Assert.NotNull(FindByAutomationId<ComboBox>(view, "ImageUnitPalette_Zoom_Combo"));
            Assert.NotNull(FindByAutomationId<CheckBox>(view, "ImageUnitPalette_PaletteOverrideALL_Check"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_PaletteWrite_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_Clipboard_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_Export_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_Import_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_UNDO_Button"));
            Assert.NotNull(FindByAutomationId<Button>(view, "ImageUnitPalette_REDO_Button"));
        }

        [AvaloniaFact]
        public void View_Has_Three_Tabs()
        {
            var view = new ImageUnitPaletteView();
            var tabs = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tabs);
            Assert.Equal(3, tabs!.Items.Count);
        }

        [AvaloniaFact]
        public void PaletteTypeCombo_DefaultsToAlly()
        {
            var view = new ImageUnitPaletteView();
            var combo = FindByAutomationId<ComboBox>(view, "ImageUnitPalette_PaletteType_Combo");
            Assert.NotNull(combo);
            Assert.Equal(0, combo!.SelectedIndex);
        }

        [AvaloniaFact]
        public void PaletteOverrideALL_DefaultIsChecked()
        {
            var view = new ImageUnitPaletteView();
            var check = FindByAutomationId<CheckBox>(view, "ImageUnitPalette_PaletteOverrideALL_Check");
            Assert.NotNull(check);
            Assert.True(check!.IsChecked ?? false);
        }

        [AvaloniaFact]
        public void KnownGap_Controls_AreDisabled()
        {
            var view = new ImageUnitPaletteView();
            // Per the v3 plan, write-back is functional; only the genuinely-unported
            // surfaces are disabled stubs.
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_UNDO_Button")!.IsEnabled);
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_REDO_Button")!.IsEnabled);
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_NewAlloc_Button")!.IsEnabled);
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_Expand_Button")!.IsEnabled);
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_Clipboard_Button")!.IsEnabled);
            // #904: Export/Import Image are now functional — they are no longer
            // disabled KnownGap stubs.
            Assert.False(FindByAutomationId<ComboBox>(view, "ImageUnitPalette_Zoom_Combo")!.IsEnabled);
        }

        // ===== WU1: WF row-acceptance parity =====

        [Fact]
        public void LoadList_Treats_ZeroPointer_With_NonEmptyName_As_Valid()
        {
            // Build a synthetic ROM where the row at base + 0 has name="SOME" (u32 != 0)
            // and P12 (u32 at +12) = 0. WF's Init() validator accepts this row, but the
            // pre-fix Avalonia VM rejected it (breaks at !U.isPointer(p)).
            byte[] data = new byte[0x10000];
            uint baseAddr = 0x200;
            // Set up the ROMFE struct so RomInfo.image_unit_palette_pointer points to a
            // pointer at offset 0x100 -> baseAddr (0x200) GBA pointer form.
            data[0x100] = (byte)(U.toPointer(baseAddr) & 0xFF);
            data[0x101] = (byte)((U.toPointer(baseAddr) >> 8) & 0xFF);
            data[0x102] = (byte)((U.toPointer(baseAddr) >> 16) & 0xFF);
            data[0x103] = (byte)((U.toPointer(baseAddr) >> 24) & 0xFF);

            // Row 0 at baseAddr: name "SOME" (u32 = 0x454D4F53 LE), P12 = 0
            data[baseAddr + 0] = (byte)'S';
            data[baseAddr + 1] = (byte)'O';
            data[baseAddr + 2] = (byte)'M';
            data[baseAddr + 3] = (byte)'E';
            // The remaining 8 bytes default to 0, P12 = 0 already.

            // Row 1 (terminator): all zero.

            // Build a minimal ROM that LoadList can scan. We need RomInfo so the VM
            // can find `image_unit_palette_pointer`. Use a real FE8U RomInfo loaded
            // through a stub ROM (the VM only reads .image_unit_palette_pointer).
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            // Inject the RomInfo: set image_unit_palette_pointer = 0x100
            SetRomInfo(rom, new StubRomInfo(0x100));

            // Pass the synthetic ROM directly to LoadList to avoid a race with
            // other xUnit collections that may transiently mutate CoreState.ROM
            // while this test runs in parallel on CI.
            var vm = new ImageUnitPaletteViewModel();
            var list = vm.LoadList(rom);
            Assert.NotEmpty(list);
            // First row must be the "SOME" row (P12=0 with name!=0).
            Assert.Equal(baseAddr, list[0].addr);
        }

        [Fact]
        public void LoadList_TreatsRow_With_Both_Zero_As_Terminator()
        {
            byte[] data = new byte[0x10000];
            uint baseAddr = 0x200;
            data[0x100] = (byte)(U.toPointer(baseAddr) & 0xFF);
            data[0x101] = (byte)((U.toPointer(baseAddr) >> 8) & 0xFF);
            data[0x102] = (byte)((U.toPointer(baseAddr) >> 16) & 0xFF);
            data[0x103] = (byte)((U.toPointer(baseAddr) >> 24) & 0xFF);

            // First row is the all-zero terminator -> LoadList should return empty (or
            // the synthetic last entry only).
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            SetRomInfo(rom, new StubRomInfo(0x100));

            // Pass the synthetic ROM directly to LoadList to avoid a race with
            // other xUnit collections that may transiently mutate CoreState.ROM
            // while this test runs in parallel on CI.
            var vm = new ImageUnitPaletteViewModel();
            var list = vm.LoadList(rom);
            // The VM appends a trailing "Unit Palette Editor" sentinel row at the
            // end. The first row should NOT include the baseAddr terminator row.
            foreach (var entry in list)
            {
                Assert.NotEqual(baseAddr, entry.addr);
            }
        }

        // ===== WU3: density + l10n =====

        [Fact]
        public void DensityVerdict_ImageUnitPaletteForm_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "ImageUnitPaletteForm");
            Assert.NotNull(pair);

            var row = ControlDensityScanner.Scan(new[] { pair! }, repoRoot).FirstOrDefault();
            Assert.NotNull(row);
            if (row!.WfControlCount == 0) return;
            Assert.True(
                Math.Abs(row.DeltaPct) < 25.0,
                $"ImageUnitPaletteForm density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        [Fact]
        public void L10nCoverage_ImageUnitPaletteView_HasNoUntranslated_jaAndZh()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // Scan only ja+zh (matches project precedent — ko.txt does not exist;
            // see ClassEditorParityTests / TextViewerParityTests for the same gate).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("ImageUnitPaletteView.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "ImageUnitPaletteView.axaml has untranslated literals in ja+zh:\n" +
                string.Join("\n", untranslated.Select(f => $"  line {f.LineNumber} [{f.AttributeName}]: {f.Literal}")));
        }

        // ===== #840: RenderClassSamplePreview (class battle-anime sample) =====

        [Fact]
        public void RenderClassSamplePreview_ValidSetup_ReturnsNonNullGrid()
        {
            EnsureImageService();
            var rom = MakePreviewRom();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new ImageUnitPaletteViewModel
                {
                    ClassID = PREVIEW_CLASS_ID,
                    SelectedPaletteSlot = 1, // unit-palette slot 1
                    PaletteTypeIndex = 0,
                };
                using IImage grid = vm.RenderClassSamplePreview();
                Assert.NotNull(grid);
                Assert.Equal(BattleAnimeRendererCore.SampleGridWidth, grid!.Width);   // 360
                Assert.Equal(BattleAnimeRendererCore.SampleGridHeight, grid.Height);  // 290
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderClassSamplePreview_UsesUnitPaletteOverride_NotAnimePalette()
        {
            // The unit-palette slot 1 block 0 index 5 = MAGENTA; the anime's own
            // rec+0x1C block 0 index 5 = GREEN. The preview must render MAGENTA at
            // grid (0,0) -> proving the UNIT-palette override is applied, not the
            // anime's own palette (the blocking-bug guard).
            EnsureImageService();
            var rom = MakePreviewRom();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new ImageUnitPaletteViewModel
                {
                    ClassID = PREVIEW_CLASS_ID,
                    SelectedPaletteSlot = 1,
                    PaletteTypeIndex = 0,
                };
                using IImage grid = vm.RenderClassSamplePreview();
                Assert.NotNull(grid);
                byte[] px = grid!.GetPixelData();
                // grid (0,0) RGBA. Magenta (0x7C1F) -> R=248, G=0, B=248.
                Assert.Equal(248, px[0]); // R
                Assert.Equal(0, px[1]);   // G
                Assert.Equal(248, px[2]); // B
                Assert.Equal(255, px[3]); // A
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderClassSamplePreview_NullRom_ReturnsNull()
        {
            EnsureImageService();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ImageUnitPaletteViewModel { ClassID = 5, SelectedPaletteSlot = 1 };
                Assert.Null(vm.RenderClassSamplePreview());
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderClassSamplePreview_ClassZero_ReturnsNull()
        {
            EnsureImageService();
            var rom = MakePreviewRom();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new ImageUnitPaletteViewModel { ClassID = 0, SelectedPaletteSlot = 1 };
                Assert.Null(vm.RenderClassSamplePreview());
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderClassSamplePreview_NoSlotSelected_ReturnsNull()
        {
            EnsureImageService();
            var rom = MakePreviewRom();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new ImageUnitPaletteViewModel { ClassID = PREVIEW_CLASS_ID, SelectedPaletteSlot = 0 };
                Assert.Null(vm.RenderClassSamplePreview());
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderClassSamplePreview_UnresolvableClass_ReturnsNull()
        {
            // A class whose anime-setting pointer is wiped -> anime id 0 -> null.
            EnsureImageService();
            var rom = MakePreviewRom();
            uint classAddr = PREVIEW_CLASS_BASE + PREVIEW_CLASS_ID * PREVIEW_CLASS_DATASIZE;
            U.write_u32(rom.Data, classAddr + 52, 0); // FE8 reads +52
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new ImageUnitPaletteViewModel { ClassID = PREVIEW_CLASS_ID, SelectedPaletteSlot = 1 };
                Assert.Null(vm.RenderClassSamplePreview());
            }
            finally { CoreState.ROM = prevRom; }
        }

        static void EnsureImageService()
        {
            // Mirrors ClassEditorListPreviewTests: App.axaml.cs wires
            // SkiaImageService at startup; in headless tests CoreState may be
            // null, so create one on demand.
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }

        // ===== Stub RomInfo so the VM scan can find the palette table pointer =====
        // ROMFEINFO is a plain class with `{ get; protected set; }` auto-properties.
        // The stub uses the protected setter via a subclass constructor.

        sealed class StubRomInfo : ROMFEINFO
        {
            public StubRomInfo(uint imageUnitPalettePtr)
            {
                this.image_unit_palette_pointer = imageUnitPalettePtr;
                this.version = 8;
            }
        }

        /// <summary>
        /// FE8-flavoured (version 8) stub RomInfo wiring all four table pointers
        /// the #840 preview path resolves: class, unit-palette, anime-list.
        /// </summary>
        sealed class PreviewStubRomInfo : ROMFEINFO
        {
            public PreviewStubRomInfo()
            {
                this.version = 8;
                this.class_pointer = PREVIEW_CLASS_PTR_SLOT;
                this.class_datasize = PREVIEW_CLASS_DATASIZE;
                this.image_unit_palette_pointer = PREVIEW_UNITPAL_PTR_SLOT;
                this.image_battle_animelist_pointer = PREVIEW_ANIMELIST_PTR_SLOT;
            }
        }

        // ----- #840 preview synthetic ROM -----
        // Wires a class table (class PREVIEW_CLASS_ID -> anime-setting -> anime id),
        // an anime list (the anime record -> section/frame/OAM/palette), and a
        // unit-palette table (slot 1 -> a MAGENTA override block). Mirrors the
        // proven BattleAnimeSamplePreviewTests.MakeAnimeRom graphics pipeline.
        const uint PREVIEW_CLASS_DATASIZE = 84;
        const uint PREVIEW_CLASS_ID       = 5;
        const ushort PREVIEW_ANIME_ID     = 1;   // 1-based; record offset = base + (id-1)*0x20

        const uint PREVIEW_CLASS_PTR_SLOT   = 0x100;
        const uint PREVIEW_UNITPAL_PTR_SLOT = 0x110;
        const uint PREVIEW_ANIMELIST_PTR_SLOT = 0x120;

        const uint PREVIEW_CLASS_BASE     = 0x1000;
        const uint PREVIEW_UNITPAL_BASE   = 0x2000;
        const uint PREVIEW_ANIMELIST_BASE = 0x3000;  // anime record (id 1) lives here
        const uint PREVIEW_ANIME_SETTING  = 0x4000;

        const uint PREVIEW_SECTION   = 0x201000;
        const uint PREVIEW_FRAME     = 0x202000;
        const uint PREVIEW_OAM       = 0x203000;
        const uint PREVIEW_ANIME_PAL = 0x204000;   // anime's own palette (green)
        const uint PREVIEW_UNIT_PAL  = 0x205000;   // unit-palette override block (magenta)
        const uint PREVIEW_GFX       = 0x210000;

        static ROM MakePreviewRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");

            // version==8 -> class anime-setting at +52 (the FE7/8 branch).
            SetRomInfo(rom, new PreviewStubRomInfo());

            // Table-base pointer slots.
            U.write_u32(rom.Data, PREVIEW_CLASS_PTR_SLOT, U.toPointer(PREVIEW_CLASS_BASE));
            U.write_u32(rom.Data, PREVIEW_UNITPAL_PTR_SLOT, U.toPointer(PREVIEW_UNITPAL_BASE));
            U.write_u32(rom.Data, PREVIEW_ANIMELIST_PTR_SLOT, U.toPointer(PREVIEW_ANIMELIST_BASE));

            // Class PREVIEW_CLASS_ID -> anime-setting pointer at +52 (FE8) ->
            // u16 anime id at setting+2.
            uint classAddr = PREVIEW_CLASS_BASE + PREVIEW_CLASS_ID * PREVIEW_CLASS_DATASIZE;
            U.write_u32(rom.Data, classAddr + 52, U.toPointer(PREVIEW_ANIME_SETTING));
            U.write_u16(rom.Data, PREVIEW_ANIME_SETTING + 2, PREVIEW_ANIME_ID);

            // Anime record (id 1) at animelist base + (1-1)*0x20 = base.
            uint rec = PREVIEW_ANIMELIST_BASE;
            U.write_u32(rom.Data, rec + 12, U.toPointer(PREVIEW_SECTION));
            U.write_u32(rom.Data, rec + 16, U.toPointer(PREVIEW_FRAME));
            U.write_u32(rom.Data, rec + 20, U.toPointer(PREVIEW_OAM));
            U.write_u32(rom.Data, rec + 24, U.toPointer(PREVIEW_OAM));
            U.write_u32(rom.Data, rec + 28, U.toPointer(PREVIEW_ANIME_PAL));

            // Frame stream: section 0 = 1 frame (green sprite via GFX index 5).
            byte[] frameStream = new byte[12];
            frameStream[3] = 0x86;
            U.write_u32(frameStream, 4, U.toPointer(PREVIEW_GFX));
            U.write_u32(frameStream, 8, 0); // OAM offset
            PlantCompressed(rom, PREVIEW_FRAME, frameStream);

            // Section array: section 0 = [0,12), rest empty.
            for (int s = 0; s < 12; s++)
            {
                uint start = s == 0 ? 0u : 12u;
                U.write_u32(rom.Data, PREVIEW_SECTION + (uint)(s * 4), start);
            }

            // OAM: one sprite centered so its index-5 pixel lands at crop (0,0).
            byte[] oam = new byte[24];
            WriteSpriteOAM(oam, 0, vramX: -48, vramY: -58);
            oam[12] = 0x01; // terminator
            PlantCompressed(rom, PREVIEW_OAM, oam);

            // Graphics: solid tile of color index 5.
            PlantCompressed(rom, PREVIEW_GFX, SolidTileIndex(5));

            // Anime's OWN palette: block 0 idx5 = GREEN (0x03E0).
            byte[] animePal = new byte[64];
            U.write_u16(animePal, (0 * 16 + 5) * 2, 0x03E0);
            PlantCompressed(rom, PREVIEW_ANIME_PAL, animePal);

            // Unit-palette table slot 1 (IDToAddr(0)) +12 -> the MAGENTA override block.
            U.write_u32(rom.Data, PREVIEW_UNITPAL_BASE + 12, U.toPointer(PREVIEW_UNIT_PAL));
            byte[] unitPal = new byte[64];
            U.write_u16(unitPal, (0 * 16 + 5) * 2, 0x7C1F); // block0 idx5 = magenta
            PlantCompressed(rom, PREVIEW_UNIT_PAL, unitPal);

            return rom;
        }

        static void WriteSpriteOAM(byte[] oam, int at, int vramX, int vramY)
        {
            oam[at + 6] = (byte)(vramX & 0xFF);
            oam[at + 7] = (byte)((vramX >> 8) & 0xFF);
            oam[at + 8] = (byte)(vramY & 0xFF);
            oam[at + 9] = (byte)((vramY >> 8) & 0xFF);
        }

        static byte[] SolidTileIndex(int index)
        {
            byte packed = (byte)(((index & 0x0F) << 4) | (index & 0x0F));
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++) tile[i] = packed;
            return tile;
        }

        static void PlantCompressed(ROM rom, uint offset, byte[] raw)
        {
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
        }

        /// <summary>Helper: ROM.RomInfo has `protected set`, so set it via reflection.</summary>
        static void SetRomInfo(ROM rom, ROMFEINFO info)
        {
            var prop = typeof(ROM).GetProperty("RomInfo");
            prop?.GetSetMethod(true)?.Invoke(rom, new object[] { info });
        }
    }
}
