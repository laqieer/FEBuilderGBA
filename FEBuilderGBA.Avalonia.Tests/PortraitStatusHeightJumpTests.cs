using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the Portrait editor's FE8 "Status Height" ID-based jump (#1019):
    ///   - UnitIncreaseHeightViewModel.IdToAddress (dense FE8 table, guard-before-
    ///     subtract, RESOLVED count = raw switch byte + 1)
    ///   - ImagePortraitViewModel.GetSelectedPortraitId (aligned-only + named sentinel)
    ///   - UnitIncreaseHeightView.NavigateToId + pending-navigate timing pattern
    ///
    /// Uses a synthetic FE8U ROM (rom.LoadLow(.., "BE8E01")) so the run is
    /// deterministic and ROM-independent. The FE8U RomInfo fixes:
    ///   portrait_pointer                     = 0x5524
    ///   unit_increase_height_pointer         = 0x5C38
    ///   unit_increase_height_switch2_address = 0x5C26
    /// We write the switch2 instruction pattern, the two table base pointers, and
    /// the table data into the 16 MB buffer at those offsets.
    /// </summary>
    [Collection("SharedState")]
    public class PortraitStatusHeightJumpTests
    {
        // FE8U RomInfo fixed addresses (see ROMFE8U.cs).
        const uint Switch2Addr = 0x5C26;
        const uint HeightPtrSlot = 0x5C38;
        const uint PortraitPtrSlot = 0x5524;

        const uint DefaultHeightBase = 0x00100000; // well inside the 16 MB buffer
        const uint DefaultPortraitBase = 0x00120000;
        const uint HeightEntrySize = 4;
        const uint PortraitEntrySize = 28;

        /// <summary>
        /// Build a synthetic FE8U ROM. <paramref name="startId"/> is the first
        /// managed portrait id; <paramref name="rawCount"/> is the raw switch2
        /// byte (resolved row count = rawCount + 1). Set <paramref name="enable"/>
        /// false to write a non-SUB op1 so IsSwitch2Enable() fails.
        /// </summary>
        static ROM BuildRom(
            byte startId = 0x10,
            byte rawCount = 0x04,
            uint heightBase = DefaultHeightBase,
            uint portraitBase = DefaultPortraitBase,
            bool enable = true,
            bool zeroHeightPtr = false,
            bool zeroPortraitPtr = false)
        {
            var rom = new ROM();
            rom.LoadLow("synth-fe8u.gba", new byte[0x1000000], "BE8E01");

            // switch2 instruction pattern at 0x5C26:
            //   +0 startId  +1 op1(SUB)  +2 rawCount  +3 op2(CMP)
            rom.write_u8(Switch2Addr + 0, startId);
            rom.write_u8(Switch2Addr + 1, enable ? 0x38u : 0x00u); // SUB op (0x38..0x3D) / invalid
            rom.write_u8(Switch2Addr + 2, rawCount);
            rom.write_u8(Switch2Addr + 3, 0x28u); // CMP op (0x28..0x2D)

            // Height table base pointer at 0x5C38, portrait base at 0x5524.
            rom.write_p32(HeightPtrSlot, zeroHeightPtr ? 0u : (heightBase + 0x08000000));
            rom.write_p32(PortraitPtrSlot, zeroPortraitPtr ? 0u : (portraitBase + 0x08000000));

            return rom;
        }

        static IDisposable UseRom(ROM rom)
        {
            ROM prev = CoreState.ROM;
            CoreState.ROM = rom;
            return new Restore(prev);
        }

        sealed class Restore : IDisposable
        {
            readonly ROM _prev;
            public Restore(ROM prev) => _prev = prev;
            public void Dispose() => CoreState.ROM = _prev;
        }

        // ===================================================================
        // IdToAddress
        // ===================================================================

        [Fact]
        public void IdToAddress_FirstId_ReturnsBaseAddr()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, heightBase: DefaultHeightBase);
            using (UseRom(rom))
            {
                uint addr = UnitIncreaseHeightViewModel.IdToAddress(rom, 0x10);
                Assert.Equal(DefaultHeightBase, addr);
            }
        }

        [Fact]
        public void IdToAddress_LastResolvedRow_ReturnsLastRowAddr()
        {
            // raw count 0x04 -> resolved count 5 -> rows 0x10..0x14 (5 rows).
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, heightBase: DefaultHeightBase);
            using (UseRom(rom))
            {
                // Off-by-one assertion: with raw switch byte N, the table has N+1
                // rows, so id == startId + N (the LAST) must be a valid addr.
                uint addr = UnitIncreaseHeightViewModel.IdToAddress(rom, 0x10 + 0x04);
                Assert.Equal(DefaultHeightBase + 0x04 * HeightEntrySize, addr);
            }
        }

        [Fact]
        public void IdToAddress_OnePastEnd_ReturnsZero()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, heightBase: DefaultHeightBase);
            using (UseRom(rom))
            {
                // startId + count == startId + 5 == 0x15 is one past the last row.
                uint addr = UnitIncreaseHeightViewModel.IdToAddress(rom, 0x10 + 0x05);
                Assert.Equal(0u, addr);
            }
        }

        [Fact]
        public void IdToAddress_BelowStartId_ReturnsZero()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04);
            using (UseRom(rom))
            {
                Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(rom, 0x0F));
                Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(rom, 0x00));
            }
        }

        [Fact]
        public void IdToAddress_SentinelId_ReturnsZeroNoThrow()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04);
            using (UseRom(rom))
            {
                // 0xFFFFFFFF is the "no portrait selected" sentinel — must be
                // rejected by the >= startId+count guard with no unsigned underflow.
                uint addr = UnitIncreaseHeightViewModel.IdToAddress(rom, 0xFFFFFFFF);
                Assert.Equal(0u, addr);
            }
        }

        [Fact]
        public void IdToAddress_ResolvedCountOffByOne_RawByteRowsAreNPlusOne()
        {
            // Raw switch byte 0 still yields ONE valid row (resolved count = 1).
            var rom = BuildRom(startId: 0x10, rawCount: 0x00, heightBase: DefaultHeightBase);
            using (UseRom(rom))
            {
                Assert.Equal(DefaultHeightBase, UnitIncreaseHeightViewModel.IdToAddress(rom, 0x10));
                Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(rom, 0x11));
            }
        }

        [Fact]
        public void IdToAddress_ZeroPointer_ReturnsZero()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, zeroHeightPtr: true);
            using (UseRom(rom))
            {
                Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(rom, 0x10));
            }
        }

        [Fact]
        public void IdToAddress_DisabledSwitch2_ReturnsZero()
        {
            // enable:false writes a non-SUB op1 so IsSwitch2Enable() fails.
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, enable: false);
            using (UseRom(rom))
            {
                Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(rom, 0x10));
            }
        }

        [Fact]
        public void IdToAddress_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(null, 0x10));
        }

        [Fact]
        public void IdToAddress_FinalRowNearEof_ReturnsZero()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04);
            uint romLen = (uint)rom.Data.Length;
            // Place the base so the last row (id startId+4 -> base + 16) overflows.
            // base = len - 6 -> last addr = len + 10 -> addr + EntrySize > len.
            uint nearEofBase = romLen - 6;
            var eofRom = BuildRom(startId: 0x10, rawCount: 0x04, heightBase: nearEofBase);
            using (UseRom(eofRom))
            {
                // First row is still in bounds...
                Assert.Equal(nearEofBase, UnitIncreaseHeightViewModel.IdToAddress(eofRom, 0x10));
                // ...the last row overflows the ROM and must return 0.
                Assert.Equal(0u, UnitIncreaseHeightViewModel.IdToAddress(eofRom, 0x14));
            }
        }

        // ===================================================================
        // GetSelectedPortraitId
        // ===================================================================

        [Fact]
        public void GetSelectedPortraitId_AlignedEntry_ReturnsId()
        {
            var rom = BuildRom(portraitBase: DefaultPortraitBase);
            using (UseRom(rom))
            {
                var vm = new ImagePortraitViewModel();
                const uint k = 7;
                vm.CurrentAddr = DefaultPortraitBase + k * PortraitEntrySize;
                Assert.Equal(k, vm.GetSelectedPortraitId());
            }
        }

        [Fact]
        public void GetSelectedPortraitId_CurrentAddrZero_ReturnsSentinel()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            {
                var vm = new ImagePortraitViewModel();
                vm.CurrentAddr = 0;
                Assert.Equal(ImagePortraitViewModel.NoPortraitSelection, vm.GetSelectedPortraitId());
            }
        }

        [Fact]
        public void GetSelectedPortraitId_AddrBelowBase_ReturnsSentinel()
        {
            var rom = BuildRom(portraitBase: DefaultPortraitBase);
            using (UseRom(rom))
            {
                var vm = new ImagePortraitViewModel();
                vm.CurrentAddr = DefaultPortraitBase - PortraitEntrySize; // below base
                Assert.Equal(ImagePortraitViewModel.NoPortraitSelection, vm.GetSelectedPortraitId());
            }
        }

        [Fact]
        public void GetSelectedPortraitId_NonAligned_ReturnsSentinel()
        {
            var rom = BuildRom(portraitBase: DefaultPortraitBase);
            using (UseRom(rom))
            {
                var vm = new ImagePortraitViewModel();
                // base + 28*K + 1 — one byte past an aligned entry.
                vm.CurrentAddr = DefaultPortraitBase + 3 * PortraitEntrySize + 1;
                Assert.Equal(ImagePortraitViewModel.NoPortraitSelection, vm.GetSelectedPortraitId());
            }
        }

        [Fact]
        public void GetSelectedPortraitId_ZeroPortraitPointer_ReturnsSentinel()
        {
            var rom = BuildRom(zeroPortraitPtr: true);
            using (UseRom(rom))
            {
                var vm = new ImagePortraitViewModel();
                vm.CurrentAddr = DefaultPortraitBase; // any non-zero addr
                Assert.Equal(ImagePortraitViewModel.NoPortraitSelection, vm.GetSelectedPortraitId());
            }
        }

        // ===================================================================
        // NavigateToId (view, headless)
        // ===================================================================

        [AvaloniaFact]
        public void NavigateToId_OutOfRange_ReturnsFalseNoThrow()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04);
            using (UseRom(rom))
            {
                var view = new UnitIncreaseHeightView();
                // id below the first managed id -> no height row.
                bool ok = view.NavigateToId(0x00);
                Assert.False(ok);
                // Sentinel id (what GetSelectedPortraitId returns with no selection).
                Assert.False(view.NavigateToId(0xFFFFFFFF));
            }
        }

        [AvaloniaFact]
        public void NavigateToId_BeforeListLoads_StashesPendingAndReplaysAfterLoad()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, heightBase: DefaultHeightBase);
            using (UseRom(rom))
            {
                var view = new UnitIncreaseHeightView();
                var list = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(list);

                // List not yet loaded (Opened/LoadList fires on Show()).
                Assert.Equal(0, list!.ItemCount);

                // NavigateToId of a valid id BEFORE the list loads -> returns true
                // (id resolved to an address) and stashes the pending navigation.
                uint expectedAddr = UnitIncreaseHeightViewModel.IdToAddress(rom, 0x12); // 3rd row
                Assert.NotEqual(0u, expectedAddr);
                Assert.True(view.NavigateToId(0x12));

                // Trigger Opened -> LoadList(), which replays the pending selection.
                view.Show();

                Assert.True(list.ItemCount > 0);
                Assert.NotNull(list.SelectedItem);
                Assert.Equal(expectedAddr, list.SelectedItem!.addr);
            }
        }

        [AvaloniaFact]
        public void NavigateToId_AfterListLoads_SelectsImmediately()
        {
            var rom = BuildRom(startId: 0x10, rawCount: 0x04, heightBase: DefaultHeightBase);
            using (UseRom(rom))
            {
                var view = new UnitIncreaseHeightView();
                var list = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(list);

                view.Show(); // list loads now
                Assert.True(list!.ItemCount > 0);

                uint expectedAddr = UnitIncreaseHeightViewModel.IdToAddress(rom, 0x13); // 4th row
                Assert.True(view.NavigateToId(0x13));

                Assert.NotNull(list.SelectedItem);
                Assert.Equal(expectedAddr, list.SelectedItem!.addr);
            }
        }
    }
}
