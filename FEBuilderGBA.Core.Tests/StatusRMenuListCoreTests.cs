// SPDX-License-Identifier: GPL-3.0-or-later
// #1459 — StatusRMenuListCore parity tests.
//
// WinForms StatusRMenuForm exposes up to SIX RMenu tables via a version-gated
// FilterComboBox and discovers each table by following the four directional
// pointers (+0/+4/+8/+12) of each 28-byte node (ListFounder). The old Avalonia
// editor only ever read status_rmenu_unit_pointer with a weak linear +i*28 scan.
//
// These tests use a fully-synthetic ROM with a stub ROMFEINFO so we control the
// version + the six status_rmenu*_pointer roots, then plant directional graphs
// (including a non-contiguous child reachable ONLY via a directional pointer and
// a terminal node with no valid children) to prove the directional traversal.

using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class StatusRMenuListCoreTests
    {
        const int RomSize = 0x10000;

        // Root pointer slots (where p32 reads the table's start node from).
        // Must sit at/above the 0x200 isSafetyOffset floor (real ROMs root these
        // far higher, e.g. FE8U unit=0x889D8).
        const uint ROOT0 = 0x300; // unit
        const uint ROOT1 = 0x304; // game
        const uint ROOT2 = 0x308; // rmenu3
        const uint ROOT3 = 0x30C; // rmenu4
        const uint ROOT4 = 0x310; // rmenu5
        const uint ROOT5 = 0x314; // rmenu6 (FE8 only)

        /// <summary>Stub RomInfo letting the test set version + the 6 RMenu roots.</summary>
        sealed class RMenuStubRomInfo : ROMFEINFO
        {
            public RMenuStubRomInfo(int ver, uint r0, uint r1, uint r2, uint r3, uint r4, uint r5)
            {
                this.version = ver;
                this.status_rmenu_unit_pointer = r0;
                this.status_rmenu_game_pointer = r1;
                this.status_rmenu3_pointer = r2;
                this.status_rmenu4_pointer = r3;
                this.status_rmenu5_pointer = r4;
                this.status_rmenu6_pointer = r5;
            }
        }

        static ROM MakeRom(int ver, uint r0, uint r1, uint r2, uint r3, uint r4, uint r5)
        {
            var rom = new ROM();
            rom.LoadLow("rmenu.gba", new byte[RomSize], "NAZO");
            var info = new RMenuStubRomInfo(ver, r0, r1, r2, r3, r4, r5);
            // Assert the non-public setter still exists so a future ROM.RomInfo
            // refactor can't turn this injection into a silent no-op (Copilot
            // PR #1566 review hardening note).
            var setter = typeof(ROM).GetProperty("RomInfo")?.GetSetMethod(true);
            Assert.NotNull(setter);
            setter!.Invoke(rom, new object[] { info });
            Assert.Same(info, rom.RomInfo);
            return rom;
        }

        /// <summary>Write a node: 4 directional pointers, X/Y, text id @+18, two routines.</summary>
        static void WriteNode(ROM rom, uint addr,
            uint up, uint down, uint left, uint right, ushort textId)
        {
            rom.write_u32(addr + 0, up == 0 ? 0 : U.toPointer(up));
            rom.write_u32(addr + 4, down == 0 ? 0 : U.toPointer(down));
            rom.write_u32(addr + 8, left == 0 ? 0 : U.toPointer(left));
            rom.write_u32(addr + 12, right == 0 ? 0 : U.toPointer(right));
            rom.write_u8(addr + 16, 0x10); // PosX
            rom.write_u8(addr + 17, 0x20); // PosY
            rom.write_u16(addr + 18, textId);
        }

        // -----------------------------------------------------------------
        // TableCount / GetTablePointer (version gate)
        // -----------------------------------------------------------------

        [Fact]
        public void TableCount_FE8_Is6_Others_Are5()
        {
            var fe8 = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            var fe6 = MakeRom(6, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, 0);
            var fe7 = MakeRom(7, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, 0);

            Assert.Equal(6, StatusRMenuListCore.TableCount(fe8));
            Assert.Equal(5, StatusRMenuListCore.TableCount(fe6));
            Assert.Equal(5, StatusRMenuListCore.TableCount(fe7));
        }

        [Fact]
        public void TableCount_NullRom_IsZero()
        {
            Assert.Equal(0, StatusRMenuListCore.TableCount(null));
        }

        [Fact]
        public void GetTablePointer_MapsEachIndexToItsRoot()
        {
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            Assert.Equal(ROOT0, StatusRMenuListCore.GetTablePointer(rom, 0));
            Assert.Equal(ROOT1, StatusRMenuListCore.GetTablePointer(rom, 1));
            Assert.Equal(ROOT2, StatusRMenuListCore.GetTablePointer(rom, 2));
            Assert.Equal(ROOT3, StatusRMenuListCore.GetTablePointer(rom, 3));
            Assert.Equal(ROOT4, StatusRMenuListCore.GetTablePointer(rom, 4));
            Assert.Equal(ROOT5, StatusRMenuListCore.GetTablePointer(rom, 5));
        }

        [Fact]
        public void GetTablePointer_OutOfRange_IsZero()
        {
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            Assert.Equal(0u, StatusRMenuListCore.GetTablePointer(rom, -1));
            Assert.Equal(0u, StatusRMenuListCore.GetTablePointer(rom, 6));
            Assert.Equal(0u, StatusRMenuListCore.GetTablePointer(rom, 99));
        }

        // -----------------------------------------------------------------
        // BuildTableList — directional traversal
        // -----------------------------------------------------------------

        [Fact]
        public void BuildTableList_FollowsDirectionalPointer_ToNonContiguousChild()
        {
            // Table 0 graph: start node A @0x1000, its UP pointer → node B @0x2000
            // (far away — NOT contiguous). B is terminal (all 4 children null).
            // The old +i*28 linear scan would stop at A+28 (zeroed) and never
            // reach B. The directional walk must include BOTH A and B.
            const uint A = 0x1000, B = 0x2000;
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            rom.write_u32(ROOT0, U.toPointer(A));
            WriteNode(rom, A, up: B, down: 0, left: 0, right: 0, textId: 0x1000);
            WriteNode(rom, B, up: 0, down: 0, left: 0, right: 0, textId: 0x2000);

            var list = StatusRMenuListCore.BuildTableList(rom, 0);

            Assert.Equal(2, list.Count);
            var addrs = new HashSet<uint>();
            foreach (var r in list) addrs.Add(r.addr);
            Assert.Contains(A, addrs);
            Assert.Contains(B, addrs); // reached ONLY via the directional pointer
        }

        [Fact]
        public void BuildTableList_IncludesTerminalRootWithNoValidChildren()
        {
            // A single root node whose four directional pointers are all null is
            // still a valid menu node and MUST be listed (WF includes any safely
            // reached 28-byte node regardless of children).
            const uint A = 0x1000;
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            rom.write_u32(ROOT0, U.toPointer(A));
            WriteNode(rom, A, 0, 0, 0, 0, textId: 0x1234);

            var list = StatusRMenuListCore.BuildTableList(rom, 0);
            Assert.Single(list);
            Assert.Equal(A, list[0].addr);
        }

        [Fact]
        public void BuildTableList_TablesAreIndependent()
        {
            // Table 0 → node A; table 1 → node C. Selecting a different index
            // must surface a DIFFERENT table (the core bug: only table 0 was
            // ever reachable).
            const uint A = 0x1000, C = 0x3000;
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            rom.write_u32(ROOT0, U.toPointer(A));
            rom.write_u32(ROOT1, U.toPointer(C));
            WriteNode(rom, A, 0, 0, 0, 0, textId: 0x1111);
            WriteNode(rom, C, 0, 0, 0, 0, textId: 0x2222);

            var t0 = StatusRMenuListCore.BuildTableList(rom, 0);
            var t1 = StatusRMenuListCore.BuildTableList(rom, 1);

            Assert.Single(t0);
            Assert.Single(t1);
            Assert.Equal(A, t0[0].addr);
            Assert.Equal(C, t1[0].addr);
            Assert.NotEqual(t0[0].addr, t1[0].addr);
        }

        [Fact]
        public void BuildTableList_EveryNode_LandsOnStride28Geometry()
        {
            // Plant a chain A(+0→B), B(+0→D) at multiples of 28 from a base so
            // each AddrResult.addr is a valid 28-byte record start.
            const uint BASE = 0x1000;
            uint A = BASE, B = BASE + 28, D = BASE + 56;
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            rom.write_u32(ROOT0, U.toPointer(A));
            WriteNode(rom, A, up: B, down: 0, left: 0, right: 0, textId: 0x1001);
            WriteNode(rom, B, up: D, down: 0, left: 0, right: 0, textId: 0x1002);
            WriteNode(rom, D, up: 0, down: 0, left: 0, right: 0, textId: 0x1003);

            var list = StatusRMenuListCore.BuildTableList(rom, 0);
            Assert.Equal(3, list.Count);
            foreach (var r in list)
            {
                // Each node sits on the 28-byte stride from BASE.
                Assert.Equal(0u, (r.addr - BASE) % StatusRMenuListCore.RMENU_STRIDE);
            }
            Assert.Equal(StatusRMenuListCore.RMENU_STRIDE, 28);
        }

        [Fact]
        public void BuildTableList_DedupsCycles()
        {
            // A → B, B → A (cycle). Must list each node once, not loop forever.
            const uint A = 0x1000, B = 0x2000;
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            rom.write_u32(ROOT0, U.toPointer(A));
            WriteNode(rom, A, up: B, down: 0, left: 0, right: 0, textId: 0x1000);
            WriteNode(rom, B, up: A, down: 0, left: 0, right: 0, textId: 0x2000);

            var list = StatusRMenuListCore.BuildTableList(rom, 0);
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void BuildTableList_IncludesNodeEndingExactlyAtEOF()
        {
            // A node whose 28 bytes end EXACTLY at Data.Length must still be
            // listed — the inclusive (addr+28 <= length) bound, not the strict
            // isSafetyOffset `<` which would off-by-one-skip it (Copilot PR #1566
            // review thread on line 123).
            uint A = (uint)(RomSize - 28);
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            rom.write_u32(ROOT0, U.toPointer(A));
            WriteNode(rom, A, 0, 0, 0, 0, textId: 0x1234);

            var list = StatusRMenuListCore.BuildTableList(rom, 0);
            Assert.Single(list);
            Assert.Equal(A, list[0].addr);
        }

        [Fact]
        public void BuildTableList_ZeroRoot_ReturnsEmpty_NoThrow()
        {
            // FE6/FE7: rmenu6 (index 5) root is 0 → empty list, no exception.
            var rom = MakeRom(6, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, /*rmenu6*/ 0);
            var list = StatusRMenuListCore.BuildTableList(rom, 5);
            Assert.Empty(list);
        }

        [Fact]
        public void BuildTableList_OutOfRangeIndex_ReturnsEmpty()
        {
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            Assert.Empty(StatusRMenuListCore.BuildTableList(rom, -1));
            Assert.Empty(StatusRMenuListCore.BuildTableList(rom, 6));
        }

        [Fact]
        public void BuildTableList_NullRom_ReturnsEmpty()
        {
            Assert.Empty(StatusRMenuListCore.BuildTableList(null, 0));
        }

        // -----------------------------------------------------------------
        // GetMenuName — WF label parity (tid<=0x10 blank; first line only)
        // -----------------------------------------------------------------

        [Fact]
        public void GetMenuName_LowTextId_IsBlank()
        {
            const uint A = 0x1000;
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            WriteNode(rom, A, 0, 0, 0, 0, textId: 0x10); // <= 0x10
            Assert.Equal("", StatusRMenuListCore.GetMenuName(rom, A));

            WriteNode(rom, A, 0, 0, 0, 0, textId: 0x05);
            Assert.Equal("", StatusRMenuListCore.GetMenuName(rom, A));
        }

        [Fact]
        public void GetMenuName_UnsafeAddr_IsBlank_NoThrow()
        {
            var rom = MakeRom(8, ROOT0, ROOT1, ROOT2, ROOT3, ROOT4, ROOT5);
            // Address past EOF — must not throw, returns "".
            Assert.Equal("", StatusRMenuListCore.GetMenuName(rom, 0xFFFFFF0));
        }
    }
}
