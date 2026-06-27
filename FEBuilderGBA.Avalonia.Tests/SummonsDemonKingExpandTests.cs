// SPDX-License-Identifier: GPL-3.0-or-later
// #1606 — headless UI tests for the Demon-King Summon editor list-expand button.
//
// Verify the actual control state (presence + enabled), not just VM logic:
//   - FE8U ROM  -> the Expand button exists, is named, and is ENABLED.
//   - FE6 ROM   -> the Expand button is DISABLED (the table pointer/count address
//                  are 0 on FE6/FE7, so SummonsDemonKingExpandCore.IsEnabled gates
//                  the button off).
//
// Synthetic ROMs (16 MB) planted with the FE8U pointer/count layout so LoadList
// resolves the editor without a real ROM file.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SummonsDemonKingExpandTests : IDisposable
    {
        const string FE8U_CODE = "BE8E01";
        const string FE6_CODE = "AFEJ01";
        const uint FE8U_PointerAddr = 0x7B32Cu;  // summons_demon_king_pointer
        const uint FE8U_CountAddr = 0x7B2BCu;     // summons_demon_king_count_address
        const uint TableBase = 0x00200000u;
        const uint EntrySize = 20u;

        readonly ROM? _savedRom;

        public SummonsDemonKingExpandTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        static ROM MakeFe8U(byte countByte = 11)
        {
            var bytes = new byte[0x01000000];
            BitConverter.GetBytes(TableBase | 0x08000000u).CopyTo(bytes, (int)FE8U_PointerAddr);
            bytes[(int)FE8U_CountAddr] = countByte;
            for (uint i = 0; i < 0x40; i++)
            {
                int row = (int)(TableBase + i * EntrySize);
                bytes[row + 0] = 0x01;
                bytes[row + 1] = 0x02;
            }
            var rom = new ROM();
            Assert.True(rom.LoadLow("synth-fe8u-1606.gba", bytes, FE8U_CODE));
            return rom;
        }

        static ROM MakeFe6()
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("synth-fe6-1606.gba", new byte[0x01000000], FE6_CODE),
                "Synthetic FE6 ROM must be recognized by LoadLow");
            return rom;
        }

        // Force the Opened-triggered LoadList to run so the button gate is applied.
        static void OpenAndLoad(SummonsDemonKingViewerView view)
        {
            view.Show();
            // Opened fires synchronously on Show in the headless platform.
        }

        [AvaloniaFact]
        public void ExpandButton_Exists_AndEnabled_ForFe8U()
        {
            CoreState.ROM = MakeFe8U();
            var view = new SummonsDemonKingViewerView();
            try
            {
                OpenAndLoad(view);
                var btn = view.FindControl<Button>("ExpandButton");
                Assert.NotNull(btn);
                Assert.Equal("Expand List", btn!.Content);
                Assert.True(btn.IsEnabled, "Expand button must be enabled for an FE8U ROM");
            }
            finally { view.Close(); }
        }

        [AvaloniaFact]
        public void ExpandButton_Disabled_ForFe6()
        {
            CoreState.ROM = MakeFe6();
            var view = new SummonsDemonKingViewerView();
            try
            {
                OpenAndLoad(view);
                var btn = view.FindControl<Button>("ExpandButton");
                Assert.NotNull(btn);
                Assert.False(btn!.IsEnabled, "Expand button must be disabled for an FE6 ROM (non-FE8)");
            }
            finally { view.Close(); }
        }
    }
}
