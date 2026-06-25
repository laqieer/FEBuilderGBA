using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Pure / read-only AoeRangeCore tests (no CoreState mutation).
    /// </summary>
    public class AoeRangeCorePureTests
    {
        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO");
            return rom;
        }

        static void WriteRecord(ROM rom, uint addr, byte w, byte h, byte cx, byte cy, byte[] cells)
        {
            rom.Data[addr + 0] = w;
            rom.Data[addr + 1] = h;
            rom.Data[addr + 2] = cx;
            rom.Data[addr + 3] = cy;
            for (int i = 0; i < cells.Length; i++) rom.Data[addr + 4 + i] = cells[i];
        }

        [Fact]
        public void ReadAoeRange_NullRom_ReturnsNull()
        {
            Assert.Null(AoeRangeCore.ReadAoeRange(null, 0x100));
        }

        [Fact]
        public void ReadAoeRange_RoundTrips_HeaderAndCells()
        {
            var rom = MakeRom();
            uint addr = 0x1000;
            byte[] cells = { 0, 1, 1, 0, 1, 1 }; // 3x2
            WriteRecord(rom, addr, 3, 2, 1, 1, cells);

            var data = AoeRangeCore.ReadAoeRange(rom, addr);
            Assert.NotNull(data);
            Assert.Equal(3u, data.Width);
            Assert.Equal(2u, data.Height);
            Assert.Equal(1u, data.CenterX);
            Assert.Equal(1u, data.CenterY);
            Assert.Equal(cells, data.Cells);
        }

        [Fact]
        public void ReadAoeRange_AcceptsGbaPointer()
        {
            var rom = MakeRom();
            uint addr = 0x1000;
            WriteRecord(rom, addr, 2, 2, 0, 0, new byte[] { 1, 0, 0, 1 });

            // Pass the GBA pointer form (offset + 0x08000000) — should normalise.
            var data = AoeRangeCore.ReadAoeRange(rom, addr + 0x08000000);
            Assert.NotNull(data);
            Assert.Equal(2u, data.Width);
        }

        [Fact]
        public void ReadAoeRange_OutOfBoundsExtent_ReturnsNull()
        {
            var rom = MakeRom(0x100);
            // Header near the end with a huge w*h that overruns the ROM.
            uint addr = 0xF8;
            rom.Data[addr + 0] = 0xFF;
            rom.Data[addr + 1] = 0xFF;
            Assert.Null(AoeRangeCore.ReadAoeRange(rom, addr));
        }

        [Fact]
        public void ReadAoeRange_HeaderTruncatedAtEof_ReturnsNullNotThrow()
        {
            // addr+2 is the last valid byte; addr+3 (CenterY) is past EOF. The method
            // must return null, never throw (Copilot review boundary case).
            var rom = MakeRom(0x103);
            uint addr = 0x100; // addr+2 = 0x102 (last index), addr+3 = 0x103 (OOB).
            Assert.Null(AoeRangeCore.ReadAoeRange(rom, addr));
        }

        [Fact]
        public void ReadAoeRange_ZeroSize_ReturnsEmptyGrid()
        {
            var rom = MakeRom();
            uint addr = 0x1000;
            WriteRecord(rom, addr, 0, 0, 0, 0, Array.Empty<byte>());
            var data = AoeRangeCore.ReadAoeRange(rom, addr);
            Assert.NotNull(data);
            Assert.Equal(0u, data.Width);
            Assert.Empty(data.Cells);
        }

        [Fact]
        public void BuildBinary_LaysOutHeaderThenCells()
        {
            byte[] cells = { 5, 6, 7, 8 };
            byte[] bin = AoeRangeCore.BuildBinary(2, 2, 1, 0, cells);
            Assert.Equal(4 + 4, bin.Length);
            Assert.Equal(2, bin[0]);
            Assert.Equal(2, bin[1]);
            Assert.Equal(1, bin[2]);
            Assert.Equal(0, bin[3]);
            Assert.Equal(5, bin[4]);
            Assert.Equal(8, bin[7]);
        }

        [Fact]
        public void BuildBinary_ShortCells_ZeroPads()
        {
            byte[] bin = AoeRangeCore.BuildBinary(2, 2, 0, 0, new byte[] { 9 });
            Assert.Equal(8, bin.Length);
            Assert.Equal(9, bin[4]);
            Assert.Equal(0, bin[5]);
            Assert.Equal(0, bin[6]);
            Assert.Equal(0, bin[7]);
        }

        [Theory]
        [InlineData(3u, 2u, 1u, 1u, 4)]   // 1 + 1*3 = 4
        [InlineData(3u, 2u, 0u, 0u, 0)]   // top-left
        [InlineData(3u, 2u, 2u, 1u, 5)]   // bottom-right
        [InlineData(0u, 0u, 0u, 0u, -1)]  // empty grid
        [InlineData(3u, 2u, 5u, 5u, -1)]  // out of range
        public void CenterIndex_MatchesWinForms(uint w, uint h, uint cx, uint cy, int expected)
        {
            Assert.Equal(expected, AoeRangeCore.CenterIndex(w, h, cx, cy));
        }

        [Fact]
        public void CenterIndex_OnDecodedData_Matches()
        {
            var rom = MakeRom();
            WriteRecord(rom, 0x1000, 3, 2, 2, 1, new byte[] { 0, 0, 0, 0, 0, 1 });
            var data = AoeRangeCore.ReadAoeRange(rom, 0x1000);
            Assert.Equal(5, data.CenterIndex);
        }
    }

    /// <summary>
    /// ROM-mutating AoeRangeCore.WriteAoeRange tests. These set CoreState.ROM
    /// (the append path routes through RecycleAddress, which writes via
    /// CoreState.ROM) under a BeginUndoScope, so they live in the shared
    /// collection and restore CoreState.ROM on dispose.
    /// </summary>
    [Collection("SharedState")]
    public class AoeRangeCoreWriteTests : IDisposable
    {
        readonly ROM _savedRom;

        public AoeRangeCoreWriteTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO");
            return rom;
        }

        static void WritePointer(ROM rom, uint slot, uint offset)
        {
            uint gba = offset + 0x08000000;
            rom.Data[slot + 0] = (byte)(gba & 0xFF);
            rom.Data[slot + 1] = (byte)((gba >> 8) & 0xFF);
            rom.Data[slot + 2] = (byte)((gba >> 16) & 0xFF);
            rom.Data[slot + 3] = (byte)((gba >> 24) & 0xFF);
        }

        static void WriteRecord(ROM rom, uint addr, byte w, byte h, byte cx, byte cy, byte[] cells)
        {
            rom.Data[addr + 0] = w;
            rom.Data[addr + 1] = h;
            rom.Data[addr + 2] = cx;
            rom.Data[addr + 3] = cy;
            for (int i = 0; i < cells.Length; i++) rom.Data[addr + 4 + i] = cells[i];
        }

        static void Fill(ROM rom, uint addr, int len, byte v)
        {
            for (int i = 0; i < len; i++) rom.Data[addr + i] = v;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "aoe test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        [Fact]
        public void WriteAoeRange_NullRom_Refused()
        {
            var r = AoeRangeCore.WriteAoeRange(null, 0, 0x1000, 2, 2, 0, 0, new byte[4]);
            Assert.Equal(AoeRangeCore.WriteStatus.Refused, r.Status);
        }

        [Fact]
        public void WriteAoeRange_InPlace_SameSize_NoMove()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            WriteRecord(rom, addr, 2, 2, 0, 0, new byte[] { 1, 1, 1, 1 });

            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = AoeRangeCore.WriteAoeRange(rom, 0, addr, 2, 2, 1, 1, new byte[] { 9, 8, 7, 6 });
            }
            Assert.Equal(AoeRangeCore.WriteStatus.InPlace, r.Status);
            Assert.Equal(addr, r.Address);
            // Header + cells overwritten in place.
            Assert.Equal(1, rom.Data[addr + 2]); // cx
            Assert.Equal(1, rom.Data[addr + 3]); // cy
            Assert.Equal(9, rom.Data[addr + 4]);
            Assert.Equal(6, rom.Data[addr + 7]);
        }

        [Fact]
        public void WriteAoeRange_InPlace_Smaller_ZeroFillsSurplus()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = 0x1000;
            // Old: 3x2 => 4 + 6 = 10 bytes.
            WriteRecord(rom, addr, 3, 2, 0, 0, new byte[] { 1, 2, 3, 4, 5, 6 });

            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                // New: 2x2 => 4 + 4 = 8 bytes (fits the 10-byte old region).
                r = AoeRangeCore.WriteAoeRange(rom, 0, addr, 2, 2, 0, 0, new byte[] { 7, 7, 7, 7 });
            }
            Assert.Equal(AoeRangeCore.WriteStatus.InPlace, r.Status);
            Assert.Equal(2, rom.Data[addr + 0]); // new width
            // Surplus bytes (offsets 8,9) zero-filled.
            Assert.Equal(0, rom.Data[addr + 8]);
            Assert.Equal(0, rom.Data[addr + 9]);
        }

        [Fact]
        public void WriteAoeRange_Grow_Appends_RepointsParentSlot_ZerosOld()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint slot = 0x400; // >= 0x200 so it is not in the header danger zone.
            uint oldAddr = 0x1000;
            // Old: 2x2 => 8 bytes.
            WriteRecord(rom, oldAddr, 2, 2, 0, 0, new byte[] { 1, 1, 1, 1 });
            WritePointer(rom, slot, oldAddr);
            // Free space for the append.
            Fill(rom, 0x100100, 256, 0xFF);

            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                // New: 4x4 => 4 + 16 = 20 bytes (does NOT fit the 8-byte old region).
                r = AoeRangeCore.WriteAoeRange(rom, slot, oldAddr, 4, 4, 0, 0, new byte[16]);
            }
            Assert.Equal(AoeRangeCore.WriteStatus.Moved, r.Status);
            Assert.NotEqual(oldAddr, r.Address);
            Assert.NotEqual(U.NOT_FOUND, r.Address);
            // Parent slot now points at the new data.
            Assert.Equal(r.Address, rom.p32(slot));
            // New data has the new header.
            Assert.Equal(4, rom.Data[r.Address + 0]);
            // Old region zeroed.
            for (uint i = 0; i < 8; i++) Assert.Equal(0, rom.Data[oldAddr + i]);
            Assert.True(r.RepointedSlots >= 1);
        }

        [Fact]
        public void WriteAoeRange_Grow_RepointsAllReferences_NotJustParent()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint slotA = 0x400; // >= 0x200 (header danger zone excluded).
            uint slotB = 0x800;
            uint oldAddr = 0x1000;
            WriteRecord(rom, oldAddr, 2, 2, 0, 0, new byte[] { 1, 1, 1, 1 });
            // TWO raw pointer references to the same blob.
            WritePointer(rom, slotA, oldAddr);
            WritePointer(rom, slotB, oldAddr);
            Fill(rom, 0x100100, 256, 0xFF);

            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                // Grow, passing slotA as the explicit parent; slotB must ALSO move
                // via the all-reference rescan.
                r = AoeRangeCore.WriteAoeRange(rom, slotA, oldAddr, 4, 4, 0, 0, new byte[16]);
            }
            Assert.Equal(AoeRangeCore.WriteStatus.Moved, r.Status);
            Assert.Equal(r.Address, rom.p32(slotA));
            Assert.Equal(r.Address, rom.p32(slotB));
        }

        [Fact]
        public void WriteAoeRange_Grow_NoParentNoReference_RefusedNoMutation()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint oldAddr = 0x1000;
            WriteRecord(rom, oldAddr, 2, 2, 0, 0, new byte[] { 1, 1, 1, 1 });
            // NO pointer anywhere references oldAddr; NO parent slot supplied.
            Fill(rom, 0x100100, 256, 0xFF);

            byte[] snapshot = (byte[])rom.Data.Clone();

            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = AoeRangeCore.WriteAoeRange(rom, 0, oldAddr, 4, 4, 0, 0, new byte[16]);
            }
            Assert.Equal(AoeRangeCore.WriteStatus.Refused, r.Status);
            Assert.Equal(U.NOT_FOUND, r.Address);
            // No net mutation: the orphan blob was zeroed back; old region intact.
            Assert.Equal(snapshot, rom.Data);
        }

        [Fact]
        public void WriteAoeRange_FreshAppend_RequiresParentSlot()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            // addr == 0 with no parent slot -> refused.
            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = AoeRangeCore.WriteAoeRange(rom, 0, 0, 2, 2, 0, 0, new byte[4]);
            }
            Assert.Equal(AoeRangeCore.WriteStatus.Refused, r.Status);
        }

        [Fact]
        public void WriteAoeRange_FreshAppend_WithParentSlot_AppendsAndRepoints()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            uint slot = 0x400; // >= 0x200 (header danger zone excluded).
            Fill(rom, 0x100100, 256, 0xFF);

            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = AoeRangeCore.WriteAoeRange(rom, slot, 0, 2, 2, 0, 0, new byte[] { 1, 0, 0, 1 });
            }
            Assert.Equal(AoeRangeCore.WriteStatus.Moved, r.Status);
            Assert.NotEqual(U.NOT_FOUND, r.Address);
            Assert.Equal(r.Address, rom.p32(slot));
            Assert.Equal(2, rom.Data[r.Address + 0]);
        }

        [Fact]
        public void WriteAoeRange_UnsafeAddr_RefusedNoMutation()
        {
            var rom = MakeRom(0x200);
            CoreState.ROM = rom;
            byte[] snapshot = (byte[])rom.Data.Clone();
            // Address well past the ROM end.
            AoeRangeCore.WriteResult r;
            using (ROM.BeginUndoScope(NewUndo(rom)))
            {
                r = AoeRangeCore.WriteAoeRange(rom, 0, 0x10000, 2, 2, 0, 0, new byte[4]);
            }
            Assert.Equal(AoeRangeCore.WriteStatus.Refused, r.Status);
            Assert.Equal(snapshot, rom.Data);
        }

        [Fact]
        public void WriteAoeRange_OversizedDimensions_Refused()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            var r = AoeRangeCore.WriteAoeRange(rom, 0, 0x1000, 256, 2, 0, 0, new byte[512]);
            Assert.Equal(AoeRangeCore.WriteStatus.Refused, r.Status);
        }
    }
}
