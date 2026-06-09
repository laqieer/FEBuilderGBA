// SPDX-License-Identifier: GPL-3.0-or-later
// Integration tests for ImageUnitPaletteViewModel.ExpandList (#1078).
//
// The Unit Palette editor's "Expand List" button grows the unit-palette
// pointer table with PREDICATE-AWARE semantics:
//   - real (sentinel-excluded) row count
//   - FIRST-fill new rows from a non-empty template row + clear each P12 so
//     the new rows are scan-visible (P12==0 && name!=0)
//   - a FULL all-zero 16-byte terminator row (NOT a 0xFFFFFFFF dword), so the
//     LoadList scan (which accepts P12==0 && name!=0) stops exactly at newCount
//   - RepointAllReferences for raw + LDR-literal references to the old base
//   - validate-all-before-mutate (no template row => error, NO mutation)
//
// These drive the VM directly against a synthetic ROM with
// image_unit_palette_pointer wired (mirrors ImageUnitPaletteParityTests).
using System;
using System.Linq;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImageUnitPaletteExpandTests : IDisposable
    {
        readonly ROM? _savedRom;

        public ImageUnitPaletteExpandTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        const uint SIZE = 16;
        const uint POINTER_SLOT = 0x100;
        const uint TABLE_BASE = 0x200;
        // A raw pointer to TABLE_BASE elsewhere in the ROM. MUST be >= 0x200 (the
        // isSafetyOffset floor) so RepointAllReferences's danger-zone slot gate
        // doesn't skip it, and outside the table rows / free region.
        const uint SECONDARY_PTR_SLOT = 0x1000;
        const uint FREE_REGION = 0x100100;     // 0xFF free run for the grown table

        sealed class StubRomInfo : ROMFEINFO
        {
            public StubRomInfo(uint imageUnitPalettePtr)
            {
                this.image_unit_palette_pointer = imageUnitPalettePtr;
                this.version = 8;
            }
        }

        static void SetRomInfo(ROM rom, ROMFEINFO info)
        {
            var prop = typeof(ROM).GetProperty("RomInfo");
            prop?.GetSetMethod(true)?.Invoke(rom, new object[] { info });
        }

        static void WriteName(byte[] data, uint addr, string name)
        {
            for (int i = 0; i < 12 && i < name.Length; i++)
                data[addr + (uint)i] = (byte)name[i];
        }

        /// <summary>
        /// Build a synthetic ROM with 3 unit-palette rows (each P12==0,
        /// name!=0) + an all-zero terminator, a 0xFF free run for the grown
        /// table, and a secondary raw pointer to the table base so the
        /// RepointAllReferences integration is observable.
        /// </summary>
        static ROM MakeRom(out uint oldBase)
        {
            byte[] data = new byte[0x200000];
            // Pointer slot -> TABLE_BASE (GBA pointer form).
            U.write_u32(data, POINTER_SLOT, U.toPointer(TABLE_BASE));
            // A SECONDARY raw pointer to the same base elsewhere in the ROM.
            U.write_u32(data, SECONDARY_PTR_SLOT, U.toPointer(TABLE_BASE));

            // 3 valid rows: name!=0, P12==0.
            WriteName(data, TABLE_BASE + 0 * SIZE, "PLY0");
            WriteName(data, TABLE_BASE + 1 * SIZE, "ENMY");
            WriteName(data, TABLE_BASE + 2 * SIZE, "OTHR");
            // Row 3 is the all-zero terminator (already 0x00).

            // Free 0xFF run for the relocated table.
            for (int i = 0; i < 0x400; i++)
                data[FREE_REGION + (uint)i] = 0xFF;

            var rom = new ROM();
            rom.LoadLow("synth.gba", data, "NAZO");
            SetRomInfo(rom, new StubRomInfo(POINTER_SLOT));
            oldBase = rom.p32(POINTER_SLOT);
            return rom;
        }

        [Fact]
        public void ExpandList_GrowsTable_NewRowsScanVisible_TerminatorStopsScan()
        {
            var rom = MakeRom(out uint oldBase);
            CoreState.ROM = rom;
            var vm = new ImageUnitPaletteViewModel();

            int realCount = vm.LoadList(rom).Count - 1;
            Assert.Equal(3, realCount);

            uint newCount = (uint)(realCount + 3); // 6, well below the 512 scan bound

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };

            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = vm.ExpandList(newCount, ud);
            }
            Assert.Equal("", err);

            // The pointer moved to a new base.
            uint newBase = rom.p32(POINTER_SLOT);
            Assert.NotEqual(oldBase, newBase);

            // LoadList now sees exactly newCount real rows (+1 sentinel).
            var list = vm.LoadList(rom);
            Assert.Equal((int)newCount + 1, list.Count);

            // Old rows are preserved at the new base (names intact, P12 intact==0).
            string[] oldNames = { "PLY0", "ENMY", "OTHR" };
            for (int i = 0; i < realCount; i++)
            {
                uint rowAddr = newBase + (uint)(i * (int)SIZE);
                string ident = ReadName(rom, rowAddr);
                Assert.Equal(oldNames[i], ident);
                Assert.Equal(0u, rom.u32(rowAddr + 12)); // P12 unchanged
            }

            // New rows are scan-visible: template identifier ("PLY0") copied,
            // P12 cleared to 0, nameFirst != 0.
            uint templateNameFirst = rom.u32(newBase + 0); // template = row 0
            for (int i = realCount; i < (int)newCount; i++)
            {
                uint rowAddr = newBase + (uint)(i * (int)SIZE);
                Assert.Equal("PLY0", ReadName(rom, rowAddr));
                Assert.Equal(0u, rom.u32(rowAddr + 12));     // P12 cleared
                Assert.NotEqual(0u, rom.u32(rowAddr + 0));   // nameFirst != 0
                Assert.Equal(templateNameFirst, rom.u32(rowAddr + 0));
            }

            // FULL all-zero terminator row at newBase + newCount*SIZE.
            uint termAddr = newBase + newCount * SIZE;
            for (uint b = 0; b < SIZE; b++)
                Assert.Equal(0x00, rom.Data[termAddr + b]);

            // The scan (LoadList predicate) stops at exactly newCount.
            uint scanned = 0;
            uint cur = newBase;
            while (cur + SIZE <= (uint)rom.Data.Length)
            {
                uint p = rom.u32(cur + 12);
                uint nameFirst = rom.u32(cur + 0);
                bool valid = U.isPointer(p) || (p == 0 && nameFirst != 0);
                if (!valid) break;
                scanned++;
                cur += SIZE;
                if (scanned > newCount + 10) break;
            }
            Assert.Equal(newCount, scanned);
        }

        [Fact]
        public void ExpandList_RepointsSecondaryRawPointer_ToNewBase()
        {
            var rom = MakeRom(out uint oldBase);
            CoreState.ROM = rom;
            var vm = new ImageUnitPaletteViewModel();
            int realCount = vm.LoadList(rom).Count - 1;
            uint newCount = (uint)(realCount + 2);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette repoint test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            using (ROM.BeginUndoScope(ud))
            {
                Assert.Equal("", vm.ExpandList(newCount, ud));
            }

            uint newBase = rom.p32(POINTER_SLOT);
            // The canonical pointer slot was repointed by ExpandTableTo.
            Assert.Equal(U.toPointer(newBase), rom.u32(POINTER_SLOT));
            // The SECONDARY raw pointer was repointed by RepointAllReferences.
            Assert.Equal(U.toPointer(newBase), rom.u32(SECONDARY_PTR_SLOT));
        }

        [Fact]
        public void ExpandList_OuterRollback_RestoresByteIdentical()
        {
            var rom = MakeRom(out _);
            CoreState.ROM = rom;
            var vm = new ImageUnitPaletteViewModel();
            int realCount = vm.LoadList(rom).Count - 1;
            uint newCount = (uint)(realCount + 4);

            byte[] snapshot = new byte[rom.Data.Length];
            Array.Copy(rom.Data, snapshot, rom.Data.Length);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette rollback test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            using (ROM.BeginUndoScope(ud))
            {
                Assert.Equal("", vm.ExpandList(newCount, ud));
            }

            // Roll back the recorded positions in reverse order.
            for (int i = ud.list.Count - 1; i >= 0; i--)
            {
                var up = ud.list[i];
                Array.Copy(up.data, 0, rom.Data, up.addr, up.data.Length);
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != rom.Data[i])
                    Assert.Fail($"Byte mismatch at 0x{i:X06}: snapshot=0x{snapshot[i]:X02}, post-rollback=0x{rom.Data[i]:X02}");
            }
        }

        [Fact]
        public void ExpandList_AllRowsZeroIdentifier_ReturnsError_NoMutation()
        {
            // Build a ROM where every "row" has a zero first identifier dword but
            // a non-zero P12 (so LoadList still accepts them as valid rows via the
            // isPointer branch). No row qualifies as a non-empty TEMPLATE => the
            // VM must refuse WITHOUT mutating the ROM.
            byte[] data = new byte[0x200000];
            U.write_u32(data, POINTER_SLOT, U.toPointer(TABLE_BASE));
            // 2 rows: name == 0 (zero first dword) but P12 = a valid pointer.
            for (int i = 0; i < 2; i++)
                U.write_u32(data, TABLE_BASE + (uint)(i * (int)SIZE) + 12, U.toPointer(0x300));
            // Row 2 terminator (all zero).
            for (int i = 0; i < 0x400; i++)
                data[FREE_REGION + (uint)i] = 0xFF;

            var rom = new ROM();
            rom.LoadLow("synth.gba", data, "NAZO");
            SetRomInfo(rom, new StubRomInfo(POINTER_SLOT));
            CoreState.ROM = rom;

            byte[] snapshot = new byte[rom.Data.Length];
            Array.Copy(rom.Data, snapshot, rom.Data.Length);

            var vm = new ImageUnitPaletteViewModel();
            int realCount = vm.LoadList(rom).Count - 1;
            Assert.True(realCount >= 1);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette no-template test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = vm.ExpandList((uint)(realCount + 3), ud);
            }
            Assert.False(string.IsNullOrEmpty(err));

            // The ROM must be byte-identical (no mutation occurred).
            for (int i = 0; i < snapshot.Length; i++)
                Assert.Equal(snapshot[i], rom.Data[i]);
        }

        [Fact]
        public void ExpandList_SameCount_NoOpSuccess()
        {
            var rom = MakeRom(out uint oldBase);
            CoreState.ROM = rom;
            var vm = new ImageUnitPaletteViewModel();
            int realCount = vm.LoadList(rom).Count - 1;

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette no-op test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = vm.ExpandList((uint)realCount, ud);
            }
            Assert.Equal("", err);
            // No-op: pointer unchanged.
            Assert.Equal(oldBase, rom.p32(POINTER_SLOT));
        }

        [Fact]
        public void ExpandList_NewCountSmallerThanCurrent_ReturnsError()
        {
            var rom = MakeRom(out _);
            CoreState.ROM = rom;
            var vm = new ImageUnitPaletteViewModel();
            int realCount = vm.LoadList(rom).Count - 1;
            Assert.True(realCount >= 2);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette smaller test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = vm.ExpandList((uint)(realCount - 1), ud);
            }
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ExpandList_NewCountAbove512_ReturnsError()
        {
            var rom = MakeRom(out _);
            CoreState.ROM = rom;
            var vm = new ImageUnitPaletteViewModel();
            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Expand Unit Palette over-512 test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = vm.ExpandList(600, ud);
            }
            Assert.False(string.IsNullOrEmpty(err));
        }

        static string ReadName(ROM rom, uint addr)
        {
            string ident = "";
            for (int j = 0; j < 12; j++)
            {
                byte b = rom.Data[addr + (uint)j];
                if (b >= 0x20 && b < 0x7F) ident += (char)b;
                else if (b == 0) break;
            }
            return ident;
        }
    }
}
