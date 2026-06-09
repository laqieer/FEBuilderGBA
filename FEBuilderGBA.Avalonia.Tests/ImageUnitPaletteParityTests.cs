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
            // #1006: Clipboard / Zoom / UNDO / REDO are now wired — they are no
            // longer disabled KnownGap stubs (see WiredControls_AreEnabled). Only
            // the two ROM-mutating allocation surfaces remain deferred stubs.
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_NewAlloc_Button")!.IsEnabled);
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_Expand_Button")!.IsEnabled);
        }

        // ===== #1006: Clipboard / Zoom / UNDO / REDO wiring =====

        [AvaloniaFact]
        public void WiredControls_AreEnabled()
        {
            var view = new ImageUnitPaletteView();
            // #1006: the four controls are now wired and enabled.
            Assert.True(FindByAutomationId<Button>(view, "ImageUnitPalette_Clipboard_Button")!.IsEnabled);
            Assert.True(FindByAutomationId<ComboBox>(view, "ImageUnitPalette_Zoom_Combo")!.IsEnabled);
            Assert.True(FindByAutomationId<Button>(view, "ImageUnitPalette_UNDO_Button")!.IsEnabled);
            Assert.True(FindByAutomationId<Button>(view, "ImageUnitPalette_REDO_Button")!.IsEnabled);
        }

        [Fact]
        public void Axaml_WiredControls_HaveHandlers_AndNoKnownGap()
        {
            string axaml = ReadViewAxaml();
            // The four wired controls have Click/SelectionChanged handlers.
            Assert.Contains("Click=\"Clipboard_Click\"", axaml);
            Assert.Contains("SelectionChanged=\"Zoom_SelectionChanged\"", axaml);
            Assert.Contains("Click=\"Undo_Click\"", axaml);
            Assert.Contains("Click=\"Redo_Click\"", axaml);

            // None of the four wired control declarations carry IsEnabled="False"
            // or the KnownGap tooltip. Inspect each control's element text.
            foreach (var aid in new[]
            {
                "ImageUnitPalette_Clipboard_Button",
                "ImageUnitPalette_Zoom_Combo",
                "ImageUnitPalette_UNDO_Button",
                "ImageUnitPalette_REDO_Button",
            })
            {
                string element = ExtractElement(axaml, aid);
                Assert.DoesNotContain("IsEnabled=\"False\"", element);
                Assert.DoesNotContain("KnownGap", element);
            }
        }

        [Fact]
        public void ZoomCombo_HasExplicitFactorItems_PopulatedInCodeViaR_NoFitWindow()
        {
            // Items are populated in code-behind via R._() (ComboBoxItem content is
            // not localized by ViewTranslationHelper), so a XAML literal would never
            // localize — Copilot bot review on PR #1068.
            string src = ReadViewSource();
            // Index 0 = Actual size = 1x; the loop adds 2x..8x; index i -> (i+1)x
            // (GbaImageControl clamps 1..8).
            Assert.Contains("ZoomCombo.Items.Add(R._(\"Actual size\"))", src);
            Assert.Contains("ZoomCombo.Items.Add(z + \"x\")", src);
            Assert.Contains("for (int z = 2; z <= 8; z++)", src);
            // "Fit window" dropped — not representable by GbaImageControl.Zoom.
            Assert.DoesNotContain("Fit window", src);
            // The XAML no longer hardcodes the items (they'd bypass localization).
            string axaml = ReadViewAxaml();
            Assert.DoesNotContain("<ComboBoxItem>Actual size</ComboBoxItem>", axaml);
        }

        [Fact]
        public void Axaml_DeferredControls_RemainDisabled_WithNonKnownGapTooltip()
        {
            string axaml = ReadViewAxaml();
            foreach (var aid in new[]
            {
                "ImageUnitPalette_NewAlloc_Button",
                "ImageUnitPalette_Expand_Button",
            })
            {
                string element = ExtractElement(axaml, aid);
                Assert.Contains("IsEnabled=\"False\"", element);
                // The tooltip is the concrete follow-up text, NOT KnownGap.
                Assert.DoesNotContain("KnownGap", element);
                Assert.Contains("tracked separately", element);
            }
        }

        [Fact]
        public void View_CodeBehind_References_WiredPatterns()
        {
            string src = ReadViewSource();
            // Clipboard reuses the #974 helper + the 5-bit -> 8-bit expansion.
            Assert.Contains("BuildPaletteClipboardHex", src);
            Assert.Contains("(c << 3) | (c >> 2)", src);
            // Undo/Redo mirror the CoreState.Undo pattern.
            Assert.Contains("CoreState.Undo.RunUndo()", src);
            Assert.Contains("CoreState.Undo.RunRedo()", src);
            // Zoom drives the GbaImageControl preview.
            Assert.Contains("SamplePreview.Zoom", src);
            // The shared reload uses IsLoading + MarkClean.
            Assert.Contains("void ReloadAfterUndoRedo()", src);
            Assert.Contains("_vm.MarkClean()", src);
        }

        // ===== #1006: Unit-Palette 5-bit -> 8-bit clipboard round-trip =====

        /// <summary>The exact 5-bit -&gt; 8-bit channel expansion the Clipboard
        /// handler uses: <c>(c&lt;&lt;3)|(c&gt;&gt;2)</c>, replicating the top 3 bits
        /// into the low nibble so the value round-trips back through the
        /// <c>&gt;&gt;3</c> inside <see cref="ImageTSAEditorViewModel.BuildPaletteClipboardHex"/>.</summary>
        static byte Exp5to8(int c) { c &= 0x1F; return (byte)((c << 3) | (c >> 2)); }

        [Fact]
        public void Clipboard_5bitChannels_RoundTrip_Through_BuildPaletteClipboardHex()
        {
            // Unit Palette spinners hold 5-bit channels (0..31). Entry 0 = white
            // (31,31,31), entry 1 = pure red (31,0,0), entry 2 = pure green,
            // entry 3 = pure blue, rest black.
            var rgb = new (byte R, byte G, byte B)[16];
            rgb[0] = (Exp5to8(31), Exp5to8(31), Exp5to8(31)); // 0x7FFF -> big16 FF7F
            rgb[1] = (Exp5to8(31), Exp5to8(0), Exp5to8(0));   // 0x001F -> big16 1F00
            rgb[2] = (Exp5to8(0), Exp5to8(31), Exp5to8(0));   // 0x03E0 -> big16 E003
            rgb[3] = (Exp5to8(0), Exp5to8(0), Exp5to8(31));   // 0x7C00 -> big16 007C

            string hex = ImageTSAEditorViewModel.BuildPaletteClipboardHex(rgb);

            Assert.Equal(64, hex.Length);
            // Channel 31 must land at 0x1F in the packed word (NOT 3) — proving the
            // 5-bit value survives the expand-then-(>>3) round-trip.
            Assert.Equal("FF7F", hex.Substring(0, 4));
            Assert.Equal("1F00", hex.Substring(4, 4));
            Assert.Equal("E003", hex.Substring(8, 4));
            Assert.Equal("007C", hex.Substring(12, 4));
            Assert.Equal(string.Concat(System.Linq.Enumerable.Repeat("0000", 12)),
                hex.Substring(16));
        }

        [Fact]
        public void Clipboard_5bitExpansion_Channel31_PacksTo0x1F_Not3()
        {
            // The whole point of the expansion: a raw 5-bit 31 fed UNEXPANDED to
            // BuildPaletteClipboardHex (which >>3s) would collapse to 3. The view
            // expands first, so the packed channel reads back as 0x1F.
            var rgb = new (byte R, byte G, byte B)[16];
            rgb[0] = (Exp5to8(31), 0, 0); // expanded red = 248 -> >>3 = 31 = 0x1F

            string hex = ImageTSAEditorViewModel.BuildPaletteClipboardHex(rgb);
            // word = 0x001F (R=31), big-endian display = "1F00".
            Assert.Equal("1F00", hex.Substring(0, 4));

            // Counter-example: feeding 31 UNEXPANDED collapses R to 3 -> 0x0003.
            var wrong = new (byte R, byte G, byte B)[16];
            wrong[0] = (31, 0, 0);
            string wrongHex = ImageTSAEditorViewModel.BuildPaletteClipboardHex(wrong);
            Assert.Equal("0300", wrongHex.Substring(0, 4)); // 0x0003 big-endian
        }

        static string ReadViewAxaml()
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string path = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia",
                "Views", "ImageUnitPaletteView.axaml");
            Assert.True(File.Exists(path), $"View AXAML not found at {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Extract the single XAML element (from its opening tag '&lt;' up to the
        /// matching '&gt;' or '/&gt;') that declares the given AutomationId.
        /// </summary>
        static string ExtractElement(string axaml, string automationId)
        {
            int idIdx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
            Assert.True(idIdx >= 0, $"AutomationId {automationId} not found in AXAML");
            int start = axaml.LastIndexOf('<', idIdx);
            int end = axaml.IndexOf('>', idIdx);
            Assert.True(start >= 0 && end > start, $"could not bound element for {automationId}");
            return axaml.Substring(start, end - start + 1);
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

        // ===== #1022: live-recolor — editedPaletteBlock overload =====

        [Fact]
        public void RenderClassSamplePreview_EditedBlockOverride_RecolorsPreview()
        {
            // #1022: passing an EXACT 32-byte edited block (idx5 = YELLOW) must
            // recolor the preview to yellow — overriding BOTH the unit-palette
            // slot's MAGENTA block AND the anime's own GREEN palette.
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
                byte[] edited = new byte[32];
                U.write_u16(edited, 5 * 2, 0x03FF); // block idx5 = yellow (R31 G31)

                using IImage grid = vm.RenderClassSamplePreview(
                    (int)PREVIEW_CLASS_ID, 1, 0, edited);
                Assert.NotNull(grid);
                byte[] px = grid!.GetPixelData();
                Assert.Equal(248, px[0]); // R (yellow)
                Assert.Equal(248, px[1]); // G (yellow)
                Assert.Equal(0, px[2]);   // B
                Assert.Equal(255, px[3]); // A
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderClassSamplePreview_NullEditedBlock_RendersSavedUnitPalette()
        {
            // #1022: a null edited block keeps the SAVED unit-palette slot render
            // (MAGENTA), identical to the parameterless overload.
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
                using IImage grid = vm.RenderClassSamplePreview(
                    (int)PREVIEW_CLASS_ID, 1, 0, null);
                Assert.NotNull(grid);
                byte[] px = grid!.GetPixelData();
                Assert.Equal(248, px[0]); // R (magenta = saved unit palette)
                Assert.Equal(0, px[1]);   // G
                Assert.Equal(248, px[2]); // B
            }
            finally { CoreState.ROM = prevRom; }
        }

        // ===== #1022: View source-text parity (the live-recolor wiring) =====
        // These assert the View wiring directly (a source-text gate, the
        // project-wide precedent for behaviour that only the running GUI would
        // otherwise exercise — see the comment in MEMORY about cross-project
        // source-text assertions caught only by these tests + CI).

        static string ReadViewSource()
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string path = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia",
                "Views", "ImageUnitPaletteView.axaml.cs");
            Assert.True(File.Exists(path), $"View source not found at {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void View_RgbValueChanged_RefreshesSamplePreview_GuardedByIsLoading()
        {
            // The R/G/B ValueChanged handlers must call RefreshSamplePreview()
            // guarded by !_vm.IsLoading (the bulk-load suppression).
            string src = ReadViewSource();
            Assert.Contains("if (!_vm.IsLoading) RefreshSamplePreview()", src);
            // All three channels (R/G/B) wire the guarded refresh.
            int guarded = System.Text.RegularExpressions.Regex.Matches(
                src, @"if \(!_vm\.IsLoading\) RefreshSamplePreview\(\)").Count;
            Assert.True(guarded >= 3,
                $"expected R/G/B (>=3) guarded RefreshSamplePreview calls, found {guarded}");
        }

        [Fact]
        public void View_BuildsEditedBlock_Via_PackRgb555_And_EditedBlockOverload()
        {
            string src = ReadViewSource();
            // BuildEditedPaletteBlock uses the same encoder the Write path uses.
            Assert.Contains("UnitPaletteWriteCore.PackRgb555", src);
            Assert.Contains("byte[] BuildEditedPaletteBlock()", src);
            // RefreshSamplePreview forwards the edited block to the 4-arg overload.
            Assert.Contains("_vm.RenderClassSamplePreview(", src);
            Assert.Contains("_vm.ClassID", src);
        }

        [Fact]
        public void View_HasAlignmentGuard_PaletteTypeIndex_EqualsEditableBlockIndex()
        {
            string src = ReadViewSource();
            // The edited block is only passed when the previewed sub-palette equals
            // the editable block (block 0).
            Assert.Contains("EditableBlockIndex", src);
            Assert.Contains("_vm.PaletteTypeIndex == EditableBlockIndex", src);
            Assert.Contains("const int EditableBlockIndex = 0", src);
        }

        [Fact]
        public void View_StaleDeferredComment_IsGone_FromViewModel()
        {
            // The VM's old "OnChangeColor ... deferred" doc must be replaced — the
            // editable block is no longer deferred.
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string vmPath = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia",
                "ViewModels", "ImageUnitPaletteViewModel.cs");
            string vmSrc = File.ReadAllText(vmPath);
            Assert.DoesNotContain("post-render bitmap-palette mutation", vmSrc);
            Assert.DoesNotContain("is deferred", vmSrc);
            // The new live-recolor doc is present.
            Assert.Contains("Live-recolor (#1022)", vmSrc);
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
