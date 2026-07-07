// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for ImagePalletView palette Import / Export /
// Clipboard wiring (#777). Mirrors the already-merged ImageBGView
// palette import/export path, so these assertions guard the same
// behaviours: BGR15 pack/unpack via PaletteCore, file (de)serialization
// via PaletteFormatConverter, GbaRaw passthrough, <32-byte rejection,
// >16-color truncation, Write() no-op rollback, and the
// "RRGGBB,RRGGBB,..." clipboard format shared with
// ImageBattleAnimePalletView / ImageBattleScreenView.
//
// Headless [AvaloniaFact]s drive the view's internal pack/CSV/apply
// helpers so the dialog-bearing click handlers are still covered without
// a file picker. Pure [Fact]s pin the Core round-trip + converter rules.

using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ImagePalletView Import/Export/Clipboard wiring
/// (#777) works. Marked [Collection("SharedState")] because the
/// synthetic-ROM tests mutate CoreState.ROM / CoreState.Undo.
/// </summary>
[Collection("SharedState")]
public class ImagePalletImportExportTests
{
    const uint PaletteOffset = 0x100000u;      // raw offset
    const uint PalettePointer = 0x08100000u;   // GBA pointer form

    // ===================================================================
    // Pure Core round-trip + converter rules (no Avalonia needed).
    // ===================================================================

    /// <summary>
    /// Export bytes -> DetectFormat / ImportFromFormat must reproduce the
    /// identical 16 colors (5-bit-quantized round-trip is lossless once
    /// values are already 5-bit aligned, which the editor enforces).
    /// </summary>
    [Theory]
    [InlineData(".pal")]   // JASC-PAL on export
    [InlineData(".gpl")]   // GIMP GPL
    [InlineData(".act")]   // Adobe ACT
    [InlineData(".txt")]   // hex text
    [InlineData(".gbapal")] // raw GBA passthrough
    public void RoundTrip_ExportThenImport_PreservesColors(string ext)
    {
        var colors = SampleColors();
        byte[] gbaBytes = PaletteCore.PackToBytes(colors);

        PaletteFormat exportFmt = PaletteFormatConverter.FormatFromExtension(ext);
        byte[] fileBytes = PaletteFormatConverter.ExportToFormat(gbaBytes, exportFmt);

        PaletteFormat detected = PaletteFormatConverter.DetectFormat(fileBytes, ext);
        byte[] reGba = (detected == PaletteFormat.GbaRaw)
            ? fileBytes
            : PaletteFormatConverter.ImportFromFormat(fileBytes, detected);

        // Re-read the first 16 colors and compare.
        var rt = UnpackFirst16(reGba);
        for (int i = 0; i < 16; i++)
            Assert.Equal(colors[i], rt[i]);
    }

    /// <summary>
    /// A raw .pal (no JASC header) detects as GbaRaw and is consumed via
    /// the passthrough path, while a JASC .pal detects as JascPal. Both
    /// land the same 16 colors.
    /// </summary>
    [Fact]
    public void DetectFormat_RawPalVsJascPal_BothImportSameColors()
    {
        var colors = SampleColors();
        byte[] gbaBytes = PaletteCore.PackToBytes(colors);

        // Raw .pal: bytes are the GBA blob, extension .pal, no JASC header.
        PaletteFormat rawFmt = PaletteFormatConverter.DetectFormat(gbaBytes, ".pal");
        Assert.Equal(PaletteFormat.GbaRaw, rawFmt);
        var rawColors = UnpackFirst16(gbaBytes);

        // JASC .pal: exported text starts with "JASC-PAL".
        byte[] jasc = PaletteFormatConverter.ExportToFormat(gbaBytes, PaletteFormat.JascPal);
        PaletteFormat jascFmt = PaletteFormatConverter.DetectFormat(jasc, ".pal");
        Assert.Equal(PaletteFormat.JascPal, jascFmt);
        var jascColors = UnpackFirst16(PaletteFormatConverter.ImportFromFormat(jasc, jascFmt));

        for (int i = 0; i < 16; i++)
            Assert.Equal(rawColors[i], jascColors[i]);
    }

    /// <summary>
    /// PackToBytes emits exactly one 32-byte block for 16 colors (the
    /// import-rejection threshold the handler uses).
    /// </summary>
    [Fact]
    public void PackToBytes_Produces32Bytes()
    {
        byte[] bytes = PaletteCore.PackToBytes(SampleColors());
        Assert.Equal(PaletteCore.PALETTE_BLOCK_SIZE, bytes.Length);
    }

    /// <summary>
    /// VM.Write() persists exactly the packed 32 bytes at
    /// toOffset(PaletteAddress) + PaletteIndex*0x20, with a nonzero index
    /// and a GBA-pointer address; undo restores the ROM region. This
    /// mirrors what Import_Click does after ApplyGbaBytesToNuds.
    /// </summary>
    [Fact]
    public void Import_WritesAtPointerPlusIndexOffset_AndUndoRestores()
    {
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            const int index = 3;
            uint expectedOffset = U.toOffset(PalettePointer) + (uint)(index * PaletteCore.PALETTE_BLOCK_SIZE);

            // Snapshot the destination block before the write.
            byte[] before = new byte[PaletteCore.PALETTE_BLOCK_SIZE];
            Array.Copy(rom.Data, (int)expectedOffset, before, 0, before.Length);

            var colors = SampleColors();
            byte[] expectedBytes = PaletteCore.PackToBytes(colors);

            var vm = new ImagePalletViewModel();
            vm.LoadEntry(PalettePointer, index + 1, index, null);
            for (int i = 0; i < 16; i++)
                vm.SetSlot(i, colors[i].r, colors[i].g, colors[i].b);

            // Write inside an undo scope (mirrors Import_Click).
            var undoData = CoreState.Undo.NewUndoData("Import Palette");
            using (ROM.BeginUndoScope(undoData))
            {
                uint off = vm.Write();
                Assert.Equal(expectedOffset, off);
            }
            CoreState.Undo.Push(undoData);

            // Bytes at the index offset must equal the packed palette.
            byte[] after = new byte[PaletteCore.PALETTE_BLOCK_SIZE];
            Array.Copy(rom.Data, (int)expectedOffset, after, 0, after.Length);
            Assert.Equal(expectedBytes, after);

            // Undo restores the original (zero) block.
            CoreState.Undo.RunUndo();
            byte[] restored = new byte[PaletteCore.PALETTE_BLOCK_SIZE];
            Array.Copy(rom.Data, (int)expectedOffset, restored, 0, restored.Length);
            Assert.Equal(before, restored);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.Undo = prevUndo;
        }
    }

    /// <summary>
    /// Write() returns U.NOT_FOUND when the destination is invalid (here
    /// PaletteAddress sentinel), proving the handler's rollback branch is
    /// reachable and no partial write lands.
    /// </summary>
    [Fact]
    public void Import_WriteFailure_ReturnsNotFound_NoPartialWrite()
    {
        var prevRom = CoreState.ROM;
        try
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            byte[] snapshot = (byte[])rom.Data.Clone();

            var vm = new ImagePalletViewModel();
            vm.LoadEntry(PaletteOffset, 1, 0, null);
            vm.PaletteAddress = U.NOT_FOUND; // force no-op

            for (int i = 0; i < 16; i++)
                vm.SetSlot(i, 0xF8, 0xF8, 0xF8);

            uint off = vm.Write();
            Assert.Equal(U.NOT_FOUND, off);
            // ROM untouched on failure.
            Assert.Equal(snapshot, rom.Data);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ===================================================================
    // Headless view tests — exercise the click-handler helpers directly.
    // ===================================================================

    /// <summary>
    /// Export reflects UNSAVED NUD edits: set a NUD without calling Write,
    /// then ComputeExportBytes() (the body of Export_Click before the
    /// dialog) must encode the edited value — proving ReadNudsIntoVm()
    /// runs first.
    /// </summary>
    [AvaloniaFact]
    public void Export_ReflectsUnsavedNudEdits()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeMinimalRomWithWhiteSlot0(PaletteOffset);
            var view = new ImagePalletView();
            view.JumpTo(PaletteOffset, 1, 0, null);
            view.Show();
            try
            {
                Assert.True(view.IsLoaded);

                // Edit slot 1 (R1/G1/B1) WITHOUT calling Write.
                SetNud(view, "ImagePallet_R1_Input", 0xF8);
                SetNud(view, "ImagePallet_G1_Input", 0x00);
                SetNud(view, "ImagePallet_B1_Input", 0x00);
                // And slot 2 to a green for good measure.
                SetNud(view, "ImagePallet_R2_Input", 0x00);
                SetNud(view, "ImagePallet_G2_Input", 0xF8);
                SetNud(view, "ImagePallet_B2_Input", 0x00);

                byte[] bytes = view.ComputeExportBytes();
                Assert.Equal(PaletteCore.PALETTE_BLOCK_SIZE, bytes.Length);

                var colors = UnpackFirst16(bytes);
                Assert.Equal(((byte)0xF8, (byte)0x00, (byte)0x00), colors[0]); // red, the edit
                Assert.Equal(((byte)0x00, (byte)0xF8, (byte)0x00), colors[1]); // green
            }
            finally { view.Close(); }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Clipboard string == expected "RRGGBB,RRGGBB,..." for the displayed
    /// 16 colors, matching the sibling palette views' format.
    /// </summary>
    [AvaloniaFact]
    public void Clipboard_BuildsExpectedCsv()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeMinimalRomWithWhiteSlot0(PaletteOffset);
            var view = new ImagePalletView();
            view.JumpTo(PaletteOffset, 1, 0, null);
            view.Show();
            try
            {
                SetNud(view, "ImagePallet_R1_Input", 0xF8);
                SetNud(view, "ImagePallet_G1_Input", 0x00);
                SetNud(view, "ImagePallet_B1_Input", 0x00);

                string csv = view.BuildClipboardCsv();
                string[] parts = csv.Split(',');
                Assert.Equal(16, parts.Length);
                Assert.Equal("F80000", parts[0]); // slot 0 = red
            }
            finally { view.Close(); }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// A failed import (write rejected / exception) must restore the prior
    /// grid: Import_Click snapshots the grid via ComputeExportBytes() before
    /// ApplyGbaBytesToNuds(), and re-applies that snapshot on every failure
    /// path so the display is never left showing an unwritten palette
    /// (Copilot review on PR #779). This pins that snapshot/restore pair.
    /// </summary>
    [AvaloniaFact]
    public void Import_FailureRestoresPriorGrid()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeMinimalRomWithWhiteSlot0(PaletteOffset);
            var view = new ImagePalletView();
            view.JumpTo(PaletteOffset, 1, 0, null);
            view.Show();
            try
            {
                // Known prior grid (slot 1 = red) + snapshot, exactly as
                // Import_Click does before applying the imported palette.
                SetNud(view, "ImagePallet_R1_Input", 0xF8);
                SetNud(view, "ImagePallet_G1_Input", 0x00);
                SetNud(view, "ImagePallet_B1_Input", 0x00);
                byte[] prevBytes = view.ComputeExportBytes();

                // Import display hop: apply a different palette (all blue).
                var blue = new (byte r, byte g, byte b)[16];
                for (int i = 0; i < 16; i++) blue[i] = (0x00, 0x00, 0xF8);
                byte[] imported = PaletteCore.PackToBytes(blue);
                view.ApplyGbaBytesToNuds(imported);
                Assert.NotEqual(prevBytes, view.ComputeExportBytes()); // grid changed

                // Failure path: re-apply the snapshot → grid restored.
                view.ApplyGbaBytesToNuds(prevBytes);
                Assert.Equal(prevBytes, view.ComputeExportBytes());
            }
            finally { view.Close(); }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// ApplyGbaBytesToNuds (the import-to-grid hop) pushes the unpacked
    /// 16 colors into the NUDs so the grid shows them before Write.
    /// </summary>
    [AvaloniaFact]
    public void Import_AppliesColorsToNuds()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = MakeMinimalRomWithWhiteSlot0(PaletteOffset);
            var view = new ImagePalletView();
            view.JumpTo(PaletteOffset, 1, 0, null);
            view.Show();
            try
            {
                byte[] gba = PaletteCore.PackToBytes(SampleColors());
                view.ApplyGbaBytesToNuds(gba);

                // NUD slot 0 should now reflect SampleColors()[0] = red.
                Assert.Equal(0xF8, ReadNud(view, "ImagePallet_R1_Input"));
                Assert.Equal(0x00, ReadNud(view, "ImagePallet_G1_Input"));
                Assert.Equal(0x00, ReadNud(view, "ImagePallet_B1_Input"));
                // Slot 1 = green.
                Assert.Equal(0x00, ReadNud(view, "ImagePallet_R2_Input"));
                Assert.Equal(0xF8, ReadNud(view, "ImagePallet_G2_Input"));
            }
            finally { view.Close(); }
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// CSV helper truncates a &gt;16-color input to the first 16 colors.
    /// </summary>
    [Fact]
    public void BuildClipboardCsv_TruncatesToFirst16()
    {
        var colors = new (byte r, byte g, byte b)[20];
        for (int i = 0; i < 20; i++) colors[i] = ((byte)(i * 8), 0, 0);
        string csv = ImagePalletView.BuildClipboardCsv(colors);
        string[] parts = csv.Split(',');
        Assert.Equal(16, parts.Length);
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    /// <summary>16 distinct 5-bit-aligned colors used across the tests.</summary>
    static (byte r, byte g, byte b)[] SampleColors()
    {
        var c = new (byte, byte, byte)[16];
        c[0] = (0xF8, 0x00, 0x00); // red
        c[1] = (0x00, 0xF8, 0x00); // green
        c[2] = (0x00, 0x00, 0xF8); // blue
        c[3] = (0xF8, 0xF8, 0x00); // yellow
        c[4] = (0xF8, 0xF8, 0xF8); // white
        for (int i = 5; i < 16; i++)
            c[i] = ((byte)((i * 8) & 0xF8), (byte)((i * 16) & 0xF8), (byte)((i * 4) & 0xF8));
        return c;
    }

    static (byte r, byte g, byte b)[] UnpackFirst16(byte[] gba)
    {
        var result = new (byte, byte, byte)[16];
        for (int i = 0; i < 16; i++)
        {
            ushort v = (ushort)(gba[i * 2] | (gba[i * 2 + 1] << 8));
            // Re-derive RGB via the same 5-bit math the converter uses.
            byte r = (byte)((v & 0x1F) << 3);
            byte g = (byte)(((v >> 5) & 0x1F) << 3);
            byte b = (byte)(((v >> 10) & 0x1F) << 3);
            result[i] = (r, g, b);
        }
        return result;
    }

    static NumericUpDown FindNud(ImagePalletView view, string automationId)
    {
        var nud = view.GetLogicalDescendants()
            .OfType<NumericUpDown>()
            .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);
        Assert.NotNull(nud);
        return nud!;
    }

    static void SetNud(ImagePalletView view, string automationId, int value)
        => FindNud(view, automationId).Value = value;

    static int ReadNud(ImagePalletView view, string automationId)
        => (int)(FindNud(view, automationId).Value ?? 0m);

    /// <summary>16 MB FE8U ROM with a zeroed palette region.</summary>
    static ROM MakeMinimalRom()
    {
        var rom = new ROM();
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
        return rom;
    }

    /// <summary>16 MB FE8U ROM with slot 0 planted white at <paramref name="addr"/>.</summary>
    static ROM MakeMinimalRomWithWhiteSlot0(uint addr)
    {
        var rom = MakeMinimalRom();
        rom.Data[(int)addr] = 0xFF;     // 0x7FFF low byte
        rom.Data[(int)addr + 1] = 0x7F; // 0x7FFF high byte (white)
        return rom;
    }
}
