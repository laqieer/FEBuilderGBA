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
            Assert.NotNull(FindByAutomationId<TextBlock>(view, "ImageUnitPalette_Write_Label"));
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
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_Export_Button")!.IsEnabled);
            Assert.False(FindByAutomationId<Button>(view, "ImageUnitPalette_Import_Button")!.IsEnabled);
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
            var oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                // We need a RomInfo whose image_unit_palette_pointer == 0x100.
                // Use a stub RomInfo subclass to override only that field.
                SetRomInfo(rom, new StubRomInfo(0x100));

                var vm = new ImageUnitPaletteViewModel();
                var list = vm.LoadList();
                Assert.NotEmpty(list);
                // First row must be the "SOME" row (P12=0 with name!=0).
                Assert.Equal(baseAddr, list[0].addr);
            }
            finally
            {
                CoreState.ROM = oldRom;
            }
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
            var oldRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                SetRomInfo(rom, new StubRomInfo(0x100));
                var vm = new ImageUnitPaletteViewModel();
                var list = vm.LoadList();
                // The VM appends a trailing "Unit Palette Editor" sentinel row at the
                // end. The first row should NOT include the baseAddr terminator row.
                foreach (var entry in list)
                {
                    Assert.NotEqual(baseAddr, entry.addr);
                }
            }
            finally
            {
                CoreState.ROM = oldRom;
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

        /// <summary>Helper: ROM.RomInfo has `protected set`, so set it via reflection.</summary>
        static void SetRomInfo(ROM rom, ROMFEINFO info)
        {
            var prop = typeof(ROM).GetProperty("RomInfo");
            prop?.GetSetMethod(true)?.Invoke(rom, new object[] { info });
        }
    }
}
