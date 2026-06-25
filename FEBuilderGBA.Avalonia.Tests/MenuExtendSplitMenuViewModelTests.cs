// SPDX-License-Identifier: GPL-3.0-or-later
// #1413 / #1430 — the Avalonia Split Menu (Menu Extend Split) editor used a
// wholly wrong data model: it walked 40-byte / 32-entry blocks and treated the
// header +8 command-array pointer and +12..+32 ASM handler pointers as inline
// u32 "String" fields. Saving clobbered the command pointer + handler pointers
// (silent FE8 ROM corruption), and the list fabricated up to 32 rows.
//
// These tests lock in the CORRECT model (matching WinForms
// MenuExtendSplitMenuForm.cs + MenuDefinitionForm.cs):
//   1. LoadList walks 36-byte headers, stops at !isPointer(u32(+8)) → 1 row.
//   2. LoadEntry reads text-ids from the DEREFERENCED command array
//      (u16 at p32(+8)+36*n+4), not inline.
//   3. Write preserves the header +8 command pointer AND the header handler
//      pointers (+12..+32) byte-for-byte.
//   4. Write lands text-ids at p32(+8)+36*n+4 and writes the per-command
//      handler pointers exactly like WinForms AllWriteButton_Click.
//   5. GetDataLength resolves 5 vs 8 editable commands.
//   6. NewAlloc allocates 36+36*9+4 with the 0xFFFFFFFF terminator + ASM ptrs.
//   7. FE6/FE7 (split pointer 0) → empty list; Write on an unsafe menu region
//      is a no-op.
//   8. The VM list and the golden ListParityHelper builder stay in lockstep.
using System;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MenuExtendSplitMenuViewModelTests
    {
        // FE8U: menu_definiton_split_pointer is a FIXED offset 0x86510.
        const uint SplitPtrLoc = 0x86510;
        const uint HeaderBase = 0x500000;   // menu-definition header
        const uint CmdBase = 0x500100;      // command array (deref of header+8)

        // Sentinel ASM handler pointers planted in the header so we can prove
        // Write never touches them.
        const uint H12 = 0x08111111, H16 = 0x08222222, H20 = 0x08333333,
                   H24 = 0x08444444, H28 = 0x08555555, H32 = 0x08666666;

        // FE8U (non-multibyte) EventMenuCommandEffect signature — planted so
        // Write does NOT bail with "patch not installed".
        static readonly byte[] EffectSigFE8U = {
            0x00, 0xB5, 0x3C, 0x20, 0x08, 0x5C, 0x03, 0x4B, 0x9E, 0x46, 0x00, 0xF8,
            0x17, 0x20, 0x02, 0xBC, 0x08, 0x47, 0x00, 0x00, 0xF8, 0xD1, 0x00, 0x08 };
        const uint EffectSigLoc = 0x10000;  // aligned, >= Grep start of 0x10000

        static void WritePtr(byte[] data, uint at, uint gbaOrOffset)
        {
            uint ptr = gbaOrOffset >= 0x08000000 ? gbaOrOffset : gbaOrOffset + 0x08000000;
            int i = (int)at;
            data[i + 0] = (byte)(ptr & 0xFF);
            data[i + 1] = (byte)((ptr >> 8) & 0xFF);
            data[i + 2] = (byte)((ptr >> 16) & 0xFF);
            data[i + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        static void WriteU16(byte[] data, uint at, ushort v)
        {
            data[(int)at + 0] = (byte)(v & 0xFF);
            data[(int)at + 1] = (byte)((v >> 8) & 0xFF);
        }

        /// <summary>
        /// 16 MB FE8U ROM: split pointer → 36-byte header → command array.
        /// The command array is a 5-command menu: cmd0/cmd1 carry text ids and
        /// handler pointers, cmd2 is the zero terminator.
        /// </summary>
        static ROM MakeSplitRom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", BuildSplitRomBytes(eightCommands: false), "BE8E01");
            return rom;
        }

        /// <summary>Shared byte layout for the synthetic split-menu ROM.</summary>
        static byte[] BuildSplitRomBytes(bool eightCommands)
        {
            byte[] data = new byte[0x1000000];
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("BE8E01"), 0, data, 0xAC, 6);

            WritePtr(data, SplitPtrLoc, HeaderBase);          // split pointer -> header

            // Header (36 bytes)
            data[(int)HeaderBase + 0] = 6;                    // x
            data[(int)HeaderBase + 1] = 8;                    // y
            data[(int)HeaderBase + 2] = 18;                   // width
            data[(int)HeaderBase + 3] = 0;                    // height
            data[(int)HeaderBase + 4] = 1;                    // style = plain u32 value 1
            WritePtr(data, HeaderBase + 8, CmdBase);          // +8 -> command array
            WritePtr(data, HeaderBase + 12, H12);             // handler pointers (must survive Write)
            WritePtr(data, HeaderBase + 16, H16);
            WritePtr(data, HeaderBase + 20, H20);
            WritePtr(data, HeaderBase + 24, H24);
            WritePtr(data, HeaderBase + 28, H28);
            WritePtr(data, HeaderBase + 32, H32);
            // header[1].+8 left 0 so LoadList stops after one entry.

            // Command array (36-byte stride). cmd0/cmd1 valid, cmd2 terminator.
            WriteU16(data, CmdBase + 36 * 0 + 4, 0xC15);
            data[(int)CmdBase + 36 * 0 + 9] = 0;
            WritePtr(data, CmdBase + 36 * 0 + 12, 0x0804F449);
            WriteU16(data, CmdBase + 36 * 1 + 4, 0xC16);
            data[(int)CmdBase + 36 * 1 + 9] = 1;
            WritePtr(data, CmdBase + 36 * 1 + 12, 0x0804F449);
            // cmd2 zero (terminator) — already 0.

            if (eightCommands)
            {
                // Slots 6/7 carry a matching MenuID + ASM-or-null handlers → 8.
                for (uint i = 6; i < 8; i++)
                {
                    uint a = CmdBase + 36 * i;
                    data[(int)a + 9] = (byte)i;                  // MenuID == index
                    WriteU16(data, a + 4, (ushort)(0xD00 + i));  // some text id
                    WritePtr(data, a + 12, 0x0804F449);          // ASM-or-null
                    // +16/+20 left 0 (null is allowed by isPointerASMOrNull)
                }
            }
            else
            {
                // Genuine FIVE-command menu: cmd6 has a MenuID that does not
                // equal its index (matches the real FE8U garbage tail), so
                // GetDataLength returns 5 rather than reading the zero tail as 8.
                data[(int)CmdBase + 36 * 6 + 9] = 47; // no != 6 → length 5
            }

            // EventMenuCommandEffect signature so Write resolves the patch.
            Array.Copy(EffectSigFE8U, 0, data, (int)EffectSigLoc, EffectSigFE8U.Length);
            return data;
        }

        /// <summary>
        /// Variant where the command array is a valid EIGHT-command menu:
        /// slots 6 and 7 carry a matching MenuID and ASM-or-null handler
        /// pointers, so GetDataLength returns 8. Built directly on the byte
        /// array (no ambient undo scope needed at construction time).
        /// </summary>
        static ROM MakeSplitRomEightCommands()
        {
            // Re-run MakeSplitRom's byte layout, then patch slots 6/7 directly.
            byte[] data = BuildSplitRomBytes(eightCommands: true);
            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            return rom;
        }

        static void WithRom(ROM rom, Action body)
        {
            ROM prev = CoreState.ROM;
            Undo prevUndo = CoreState.Undo;
            try
            {
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                body();
            }
            finally
            {
                CoreState.ROM = prev;
                CoreState.Undo = prevUndo;
            }
        }

        /// <summary>Open an ambient undo scope (writes are optional-undo anyway).</summary>
        static IDisposable Scope() => ROM.BeginUndoScope(CoreState.Undo.NewUndoData("test"));

        // ----------------------------------------------------------------

        [Fact]
        public void SyntheticRom_HasFixedSplitPointer()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                Assert.Equal(SplitPtrLoc, rom.RomInfo.menu_definiton_split_pointer);
                Assert.Equal(HeaderBase, U.toOffset(rom.u32(SplitPtrLoc)));
                Assert.True(U.isPointer(rom.u32(HeaderBase + 8)));
                Assert.Equal(CmdBase, U.toOffset(rom.u32(HeaderBase + 8)));
            });
        }

        [Fact]
        public void LoadList_ReturnsExactlyOneEntry_NoFabricatedRows()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                var list = vm.LoadList();
                Assert.Single(list);
                Assert.Equal(HeaderBase, list[0].addr);
            });
        }

        [Fact]
        public void LoadEntry_ReadsTextIdsFromDereferencedCommandArray()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                vm.LoadEntry(HeaderBase);

                Assert.True(vm.IsLoaded);
                Assert.Equal(6u, vm.PosX);
                Assert.Equal(8u, vm.PosY);
                Assert.Equal(18u, vm.Width);
                Assert.Equal(1u, vm.Style);
                // Text ids come from the DEREFERENCED command array, NOT inline +8.
                Assert.Equal(0xC15u, vm.String0);
                Assert.Equal(0xC16u, vm.String1);
                Assert.Equal(0u, vm.String2);
                // CommandPtr is the +8 pointer (GBA form), not a text id.
                Assert.Equal(CmdBase, U.toOffset(vm.CommandPtr));
            });
        }

        [Fact]
        public void Write_PreservesCommandPointerAndHeaderHandlerPointers()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                vm.LoadEntry(HeaderBase);

                // Snapshot the header's +8 pointer and every handler pointer.
                uint cmdPtrBefore = rom.u32(HeaderBase + 8);
                uint[] handlersBefore =
                {
                    rom.u32(HeaderBase + 12), rom.u32(HeaderBase + 16), rom.u32(HeaderBase + 20),
                    rom.u32(HeaderBase + 24), rom.u32(HeaderBase + 28), rom.u32(HeaderBase + 32),
                };

                vm.String0 = 0x100; // change a text id
                vm.String1 = 0x200;
                using (Scope())
                {
                    Assert.True(vm.Write());
                }

                // The +8 command pointer must be untouched.
                Assert.Equal(cmdPtrBefore, rom.u32(HeaderBase + 8));
                // Every header handler pointer must be untouched.
                Assert.Equal(handlersBefore[0], rom.u32(HeaderBase + 12));
                Assert.Equal(handlersBefore[1], rom.u32(HeaderBase + 16));
                Assert.Equal(handlersBefore[2], rom.u32(HeaderBase + 20));
                Assert.Equal(handlersBefore[3], rom.u32(HeaderBase + 24));
                Assert.Equal(handlersBefore[4], rom.u32(HeaderBase + 28));
                Assert.Equal(handlersBefore[5], rom.u32(HeaderBase + 32));
                // And specifically NOT the sentinel handler values overwritten.
                Assert.Equal(H12, rom.u32(HeaderBase + 12));
                Assert.Equal(H32, rom.u32(HeaderBase + 32));
            });
        }

        [Fact]
        public void Write_LandsTextIdsInDereferencedCommandArray()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                vm.LoadEntry(HeaderBase);
                vm.String0 = 0x123;
                vm.String1 = 0x456;
                using (Scope())
                {
                    Assert.True(vm.Write());
                }

                // Text ids must land at p32(+8)+36*n+4, NOT at header+8..+36.
                Assert.Equal(0x123u, rom.u16(CmdBase + 36 * 0 + 4));
                Assert.Equal(0x456u, rom.u16(CmdBase + 36 * 1 + 4));
                // MenuID written per command.
                Assert.Equal(0u, rom.u8(CmdBase + 36 * 0 + 9));
                Assert.Equal(1u, rom.u8(CmdBase + 36 * 1 + 9));
                // Per-command handler pointers written (non-multibyte: display=0x04F448+1).
                Assert.Equal(0x0804F449u, rom.u32(CmdBase + 36 * 0 + 12));
                Assert.Equal(0x0804F449u, rom.u32(CmdBase + 36 * 1 + 12));
            });
        }

        [Fact]
        public void Write_ZeroTextId_ZeroesOnlyThatCommandHandlersAndBreaks()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                vm.LoadEntry(HeaderBase);
                vm.String0 = 0;   // zero → write text/menu id, zero +12/+16/+20, break
                using (Scope())
                {
                    Assert.True(vm.Write());
                }

                Assert.Equal(0u, rom.u16(CmdBase + 36 * 0 + 4));
                Assert.Equal(0u, rom.u32(CmdBase + 36 * 0 + 12));
                Assert.Equal(0u, rom.u32(CmdBase + 36 * 0 + 16));
                Assert.Equal(0u, rom.u32(CmdBase + 36 * 0 + 20));
                // The header +8 pointer is still intact.
                Assert.Equal(CmdBase, U.toOffset(rom.u32(HeaderBase + 8)));
            });
        }

        [Fact]
        public void GetDataLength_FiveCommandMenu_ReturnsFive()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                int len = MenuExtendSplitMenuViewModel.GetDataLength(rom, CmdBase);
                Assert.Equal(5, len);
            });
        }

        [Fact]
        public void GetDataLength_EightCommandMenu_ReturnsEight()
        {
            var rom = MakeSplitRomEightCommands();
            WithRom(rom, () =>
            {
                Assert.Equal(8, MenuExtendSplitMenuViewModel.GetDataLength(rom, CmdBase));

                var vm = new MenuExtendSplitMenuViewModel();
                vm.LoadEntry(HeaderBase);
                Assert.Equal(8, vm.StringCount);
                Assert.Equal((uint)(0xD00 + 6), vm.String6);
                Assert.Equal((uint)(0xD00 + 7), vm.String7);
            });
        }

        [Fact]
        public void NewAlloc_WritesNineSlotHeaderTerminatorAndAsmPointers()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                uint addr;
                using (Scope())
                {
                    addr = vm.NewAlloc();
                }
                Assert.NotEqual(U.NOT_FOUND, addr);

                // Header: command pointer -> addr+36.
                Assert.Equal(addr + 36, U.toOffset(rom.u32(addr + 8)));
                Assert.Equal(1u, rom.u32(addr + 4));   // style
                Assert.Equal(6u, rom.u8(addr + 0));    // x
                // Default non-multibyte text ids in the first two slots.
                Assert.Equal(0xC15u, rom.u16(addr + 36 + 36 * 0 + 4));
                Assert.Equal(0xC16u, rom.u16(addr + 36 + 36 * 1 + 4));
                // 0xFFFFFFFF terminator after the 9 command slots.
                Assert.Equal(0xFFFFFFFFu, rom.u32(addr + 36 + 36 * 9));
                // First slot MenuID + ASM handler pointers.
                Assert.Equal(0u, rom.u8(addr + 36 + 36 * 0 + 9));            // MenuID 0
                // display = 0x04F448+1 (non-multibyte constant).
                Assert.Equal(0x0804F449u, rom.u32(addr + 36 + 36 * 0 + 12)); // display
                // effect = grepped EventMenuCommandEffect signature + 1 → a pointer.
                Assert.True(U.isPointer(rom.u32(addr + 36 + 36 * 0 + 20)), "effect handler must be a pointer");
            });
        }

        [Fact]
        public void NewAlloc_OnFFFreeSpace_LeavesNoFFHandlerPointers()
        {
            // Free region is 0xFF-filled (real ROM free space). Without a
            // zero-fill, header handler slots (+12..+32) and unused command
            // bytes would stay 0xFFFFFFFF — invalid pointers. The fix zero-
            // fills the whole allocation first.
            byte[] data = BuildSplitRomBytes(eightCommands: false);
            // Make the ENTIRE ROM non-free (0x01) so FindFreeSpace can ONLY
            // pick the 0xFF run we carve — guaranteeing the 0xFF code path.
            for (int i = 0x200; i < data.Length; i++) if (data[i] == 0x00) data[i] = 0x01;
            const int ffStart = 0x300000;
            for (int i = ffStart; i < ffStart + 0x800; i++) data[i] = 0xFF;
            // Re-plant the EventMenuCommandEffect signature (the 0x01 fill above
            // would have clobbered its embedded 0x00 bytes).
            Array.Copy(EffectSigFE8U, 0, data, (int)EffectSigLoc, EffectSigFE8U.Length);
            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            WithRom(rom, () =>
            {
                var vm = new MenuExtendSplitMenuViewModel();
                uint addr;
                using (Scope()) { addr = vm.NewAlloc(); }
                Assert.NotEqual(U.NOT_FOUND, addr);

                // Header handler slots must be zero (NOT 0xFFFFFFFF).
                Assert.Equal(0u, rom.u32(addr + 12));
                Assert.Equal(0u, rom.u32(addr + 16));
                Assert.Equal(0u, rom.u32(addr + 20));
                Assert.Equal(0u, rom.u32(addr + 24));
                Assert.Equal(0u, rom.u32(addr + 28));
                Assert.Equal(0u, rom.u32(addr + 32));
                // Unused command slot 8 must be zeroed too (not 0xFF).
                Assert.Equal(0u, rom.u32(addr + 36 + 36 * 8 + 12));
                // Terminator intact.
                Assert.Equal(0xFFFFFFFFu, rom.u32(addr + 36 + 36 * 9));
            });
        }

        [Fact]
        public void Write_CommandArrayTooCloseToEof_NoMutation_NotEvenHeader()
        {
            // menuaddr passes isSafetyOffset(menuaddr) but is too close to EOF
            // for all 5 command rows. Write must refuse WITHOUT mutating the
            // header (validate-all-before-mutate).
            byte[] data = BuildSplitRomBytes(eightCommands: false);
            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            WithRom(rom, () =>
            {
                // Repoint header +8 to 40 bytes before EOF: the base is a safe
                // offset, but base+36*4+21 overruns.
                uint nearEof = (uint)rom.Data.Length - 40;
                using (Scope()) { rom.write_p32(HeaderBase + 8, nearEof); }

                var vm = new MenuExtendSplitMenuViewModel();
                vm.CurrentAddr = HeaderBase;
                vm.PosX = 99; vm.String0 = 0x777; // would-be header + text changes

                byte[] headerBefore = rom.getBinaryData(HeaderBase, 36);
                bool wrote;
                using (Scope()) { wrote = vm.Write(); }

                Assert.False(wrote);
                // The header must be byte-for-byte unchanged (no partial mutation).
                Assert.Equal(headerBefore, rom.getBinaryData(HeaderBase, 36));
            });
        }

        [Fact]
        public void Write_UnsafeMenuRegion_NoMutation()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                // Point the header +8 at a null/unsafe offset → Write must refuse.
                using (Scope())
                {
                    rom.write_u32(HeaderBase + 8, 0); // not a safety offset
                }
                var vm = new MenuExtendSplitMenuViewModel();
                vm.CurrentAddr = HeaderBase;
                vm.String0 = 0x999;

                byte[] before = rom.getBinaryData(HeaderBase, 36);
                bool wrote;
                using (Scope())
                {
                    wrote = vm.Write();
                }
                Assert.False(wrote);
                Assert.Equal(before, rom.getBinaryData(HeaderBase, 36));
            });
        }

        [Fact]
        public void LoadList_NoSplitPointer_ReturnsEmpty()
        {
            // FE6/FE7 have menu_definiton_split_pointer == 0.
            byte[] data = new byte[0x800000];
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("AFEJ01"), 0, data, 0xAC, 6); // FE6 JP
            var rom = new ROM();
            rom.LoadLow("fe6.gba", data, "AFEJ01");
            WithRom(rom, () =>
            {
                Assert.Equal(0u, rom.RomInfo.menu_definiton_split_pointer);
                var vm = new MenuExtendSplitMenuViewModel();
                Assert.Empty(vm.LoadList());
            });
        }

        [Fact]
        public void VmList_And_ListParityBuilder_StayInLockstep()
        {
            var rom = MakeSplitRom();
            WithRom(rom, () =>
            {
                var vmList = new MenuExtendSplitMenuViewModel().LoadList();
                var golden = ListParityHelper.BuildReferenceList("MenuExtendSplitMenuView");
                Assert.NotNull(golden);
                Assert.Equal(vmList.Count, golden!.Count);
                for (int i = 0; i < vmList.Count; i++)
                {
                    Assert.Equal(vmList[i].addr, golden[i].addr);
                }
            });
        }
    }
}
