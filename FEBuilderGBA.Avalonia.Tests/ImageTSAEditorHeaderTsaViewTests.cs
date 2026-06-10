// SPDX-License-Identifier: GPL-3.0-or-later
// Headless bound-view tests for the HEADER-TSA per-cell editor (#1071).
//
// These complement the ViewModel-getter / static-AXAML parity tests by
// instantiating the REAL ImageTSAEditorView (DataContext = its VM), calling
// Init with a VALID header-TSA context, and asserting the rendered TSA Cell
// panel is actually ENABLED through its IsEnabled="{Binding CanEditCells}"
// binding (the Copilot-review bound-view notification gap) and that the Cell
// X / Cell Y NumericUpDown maxima are constrained to the header region. A
// corrupt header-TSA must leave the panel disabled.
using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ImageTSAEditorHeaderTsaViewTests
{
    // Build an FE8U synthetic ROM with a header-TSA + a 2-tile LZ77 image and
    // the resolving pointer slots, exactly like the parity-test setup.
    static ROM MakeRomWithHeaderTsa(int mhx, int mhy,
        uint imgSlot, uint tsaSlot, uint imgData, uint tsaData)
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");

        byte[] tiles = new byte[2 * 32];
        byte[] comp = LZ77.compress(tiles);
        Array.Copy(comp, 0, rom.Data, (int)imgData, comp.Length);
        rom.write_p32(imgSlot, imgData);

        int cells = (mhx + 1) * (mhy + 1);
        byte[] tsa = new byte[2 + cells * 2];
        tsa[0] = (byte)mhx;
        tsa[1] = (byte)mhy;
        for (int k = 0; k < cells; k++)
        {
            ushort v = (ushort)(0x100 + k * 7 + 1);
            tsa[2 + k * 2] = (byte)(v & 0xFF);
            tsa[2 + k * 2 + 1] = (byte)(v >> 8);
        }
        Array.Copy(tsa, 0, rom.Data, (int)tsaData, tsa.Length);
        rom.write_p32(tsaSlot, tsaData);
        return rom;
    }

    [AvaloniaFact]
    public void HeaderTsa_ValidContext_EnablesCellPanel_AndConstrainsMaxima()
    {
        var prevRom = CoreState.ROM;
        try
        {
            int mhx = 3, mhy = 2;
            uint imgSlot = 0x100000u, tsaSlot = 0x100010u;
            uint imgData = 0x200000u, tsaData = 0x210000u;
            CoreState.ROM = MakeRomWithHeaderTsa(mhx, mhy, imgSlot, tsaSlot, imgData, tsaData);

            var view = new ImageTSAEditorView();
            view.Init(32u, 20u, imgSlot, isHeaderTSA: true, isLZ77TSA: false,
                      tsaPointer: tsaSlot, palettePointer: U.NOT_FOUND,
                      paletteAddress: 0u, paletteCount: 1);

            // The Apply button lives inside the cell panel StackPanel whose
            // IsEnabled binds to CanEditCells; IsEffectivelyEnabled reflects the
            // propagated bound value (the bound-view notification gap Copilot
            // flagged would leave this FALSE).
            var apply = view.FindControl<Button>("TsaCellApplyButton");
            Assert.NotNull(apply);
            Assert.True(apply!.IsEffectivelyEnabled,
                "The TSA Cell panel must be ENABLED for a valid header-TSA context.");

            // The Cell X / Cell Y maxima are constrained to the header region.
            var xBox = view.FindControl<NumericUpDown>("TsaCellXBox");
            var yBox = view.FindControl<NumericUpDown>("TsaCellYBox");
            Assert.NotNull(xBox);
            Assert.NotNull(yBox);
            Assert.Equal(mhx, (int)(xBox!.Maximum));
            Assert.Equal(mhy, (int)(yBox!.Maximum));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [AvaloniaFact]
    public void HeaderTsa_CorruptContext_LeavesCellPanelDisabled()
    {
        var prevRom = CoreState.ROM;
        try
        {
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", new byte[0x1100000], "BE8E01");
            CoreState.ROM = rom;

            var view = new ImageTSAEditorView();
            // tsaPointer = 0 -> p32(0) unreadable -> DecodeHeaderTsaCells null ->
            // no editable cells -> panel disabled.
            view.Init(32u, 20u, 0u, isHeaderTSA: true, isLZ77TSA: false,
                      tsaPointer: 0u, palettePointer: U.NOT_FOUND,
                      paletteAddress: 0u, paletteCount: 1);

            var apply = view.FindControl<Button>("TsaCellApplyButton");
            Assert.NotNull(apply);
            Assert.False(apply!.IsEffectivelyEnabled,
                "A corrupt header-TSA must leave the TSA Cell panel DISABLED.");
        }
        finally { CoreState.ROM = prevRom; }
    }
}
