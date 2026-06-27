// SPDX-License-Identifier: GPL-3.0-or-later
// Issue #1418 + #1598 — regression tests for the World Map Path Move editor list
// terminator, now carried forward onto WorldMapPathMoveCore.LoadMovementList.
//
// LoadMovementList walks 8-byte move nodes (ElapsedTime u16 @+0, X u16 @+4, Y u16 @+6).
// The list terminator is a u32 0xFFFFFFFF sentinel — matching WinForms:
//   * WorldMapPathMoveEditorForm.cs list filter: u32(addr) != 0xFFFFFFFF
//   * WorldMapPathForm.CalcPathMoveDataLength: returns on u32 == 0xFFFFFFFF
//   * WorldMapPathForm expand handler: writes 0xFFFFFFFF as the terminator
//
// #1598: ElapsedTime is a u16 (NOT the old u32 DWord read) and the editor walks the
// MOVEMENT sub-table resolved as p32(record+8), NOT the path-record table. These
// tests construct in-memory synthetic FE8 ROMs and drive LoadMovementList directly
// (CoreState.ROM-backed, so the class is in the "SharedState" collection).
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class WorldMapPathMoveListTerminatorTests
    {
        const uint BASE_ADDR = 0x1000; // movement sub-table base (file offset)
        const uint BLOCK = 8;
        const int ROM_SIZE = 0x1000000; // 16 MB (FE8U detection)

        static void WriteU32(byte[] d, uint addr, uint v)
        {
            d[addr + 0] = (byte)(v & 0xFF);
            d[addr + 1] = (byte)((v >> 8) & 0xFF);
            d[addr + 2] = (byte)((v >> 16) & 0xFF);
            d[addr + 3] = (byte)((v >> 24) & 0xFF);
        }

        static void WriteU16(byte[] d, uint addr, ushort v)
        {
            d[addr + 0] = (byte)(v & 0xFF);
            d[addr + 1] = (byte)((v >> 8) & 0xFF);
        }

        /// <summary>Write one 8-byte move node (ElapsedTime u16, X u16, Y u16).</summary>
        static void WriteNode(byte[] d, uint addr, ushort elapsed, ushort x, ushort y)
        {
            WriteU16(d, addr, elapsed);
            WriteU16(d, addr + 4, x);
            WriteU16(d, addr + 6, y);
        }

        /// <summary>Run <paramref name="body"/> with a synthetic FE8 ROM installed as
        /// CoreState.ROM, restoring the previous value afterward.</summary>
        static void WithRom(System.Action<ROM, byte[]> body)
        {
            var saved = CoreState.ROM;
            try
            {
                byte[] data = new byte[ROM_SIZE];
                var rom = new ROM();
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                body(rom, rom.Data);
            }
            finally { CoreState.ROM = saved; }
        }

        // ---------------------------------------------------------------
        // 1. The 0xFFFFFFFF sentinel stops the list (no sentinel row).
        // ---------------------------------------------------------------
        [Fact]
        public void Sentinel_StopsList_AfterRealNodes()
        {
            WithRom((rom, d) =>
            {
                // 2 real nodes, then the 0xFFFFFFFF u32 sentinel.
                WriteNode(d, BASE_ADDR + 0 * BLOCK, 10, 5, 6);
                WriteNode(d, BASE_ADDR + 1 * BLOCK, 20, 7, 8);
                WriteU32(d, BASE_ADDR + 2 * BLOCK, 0xFFFFFFFF);

                var list = WorldMapPathMoveCore.LoadMovementList(rom, BASE_ADDR);

                Assert.Equal(2, list.Count);
                Assert.Equal(BASE_ADDR + 0 * BLOCK, list[0].addr);
                Assert.Equal(BASE_ADDR + 1 * BLOCK, list[1].addr);
            });
        }

        // ---------------------------------------------------------------
        // 2. An all-zeros entry is NOT a terminator (the old bug).
        //    A mid-list all-zeros node must be kept; the walk must
        //    continue to the real 0xFFFFFFFF sentinel.
        // ---------------------------------------------------------------
        [Fact]
        public void AllZeros_IsNotTerminator_WalkContinuesToSentinel()
        {
            WithRom((rom, d) =>
            {
                WriteNode(d, BASE_ADDR + 0 * BLOCK, 10, 1, 2);
                WriteNode(d, BASE_ADDR + 1 * BLOCK, 0, 0, 0);   // all-zeros node (T=0,X=0,Y=0)
                WriteNode(d, BASE_ADDR + 2 * BLOCK, 30, 3, 4);
                WriteU32(d, BASE_ADDR + 3 * BLOCK, 0xFFFFFFFF); // real terminator

                var list = WorldMapPathMoveCore.LoadMovementList(rom, BASE_ADDR);

                Assert.Equal(3, list.Count);
                Assert.Equal(BASE_ADDR + 1 * BLOCK, list[1].addr); // all-zeros node included
                Assert.Equal(BASE_ADDR + 2 * BLOCK, list[2].addr);
            });
        }

        // ---------------------------------------------------------------
        // 3. No over-read past the sentinel into adjacent ROM bytes.
        // ---------------------------------------------------------------
        [Fact]
        public void NoOverRead_PastSentinel_IntoAdjacentRom()
        {
            WithRom((rom, d) =>
            {
                WriteNode(d, BASE_ADDR + 0 * BLOCK, 10, 5, 6);
                uint sentinelAddr = BASE_ADDR + 1 * BLOCK;
                WriteU32(d, sentinelAddr, 0xFFFFFFFF);
                // Poison: 20 nodes of non-zero garbage after the sentinel.
                for (uint i = 0; i < 20; i++)
                {
                    WriteNode(d, sentinelAddr + BLOCK + i * BLOCK,
                              (ushort)(0x1234 + i), (ushort)(100 + i), (ushort)(200 + i));
                }

                var list = WorldMapPathMoveCore.LoadMovementList(rom, BASE_ADDR);

                Assert.Single(list);
                foreach (AddrResult r in list)
                    Assert.True(r.addr < sentinelAddr,
                        $"row at 0x{r.addr:X} over-read at/past sentinel 0x{sentinelAddr:X}");
            });
        }

        // ---------------------------------------------------------------
        // 4. Empty list: sentinel at the base address -> 0 rows.
        // ---------------------------------------------------------------
        [Fact]
        public void Empty_SentinelAtBase_ReturnsZeroRows()
        {
            WithRom((rom, d) =>
            {
                WriteU32(d, BASE_ADDR, 0xFFFFFFFF);
                var list = WorldMapPathMoveCore.LoadMovementList(rom, BASE_ADDR);
                Assert.Empty(list);
            });
        }

        // ---------------------------------------------------------------
        // 5. ElapsedTime is decoded as a u16 (>255 value), not a 4-byte DWord.
        // ---------------------------------------------------------------
        [Fact]
        public void ElapsedTime_DecodedAsU16_Not32Bit()
        {
            WithRom((rom, d) =>
            {
                // 0x0547 ElapsedTime (>255 so genuinely 16-bit); +2/+3 stay 0.
                WriteNode(d, BASE_ADDR + 0 * BLOCK, 0x0547, 12, 34);
                WriteU32(d, BASE_ADDR + 1 * BLOCK, 0xFFFFFFFF);

                var list = WorldMapPathMoveCore.LoadMovementList(rom, BASE_ADDR);
                Assert.Single(list);
                Assert.Contains("T=1351", list[0].name); // 0x0547 == 1351 (u16)
                Assert.Contains("(12,34)", list[0].name);
            });
        }
    }
}
