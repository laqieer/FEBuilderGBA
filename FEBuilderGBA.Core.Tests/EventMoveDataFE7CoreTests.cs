using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Pure / read-only tests for the FE7 move-data command walk (#1440).
    /// No CoreState mutation — only crafted synthetic ROMs.
    /// </summary>
    public class EventMoveDataFE7CoreTests
    {
        static ROM MakeRom(int size = 0x10000)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO");
            return rom;
        }

        static void WriteBytes(ROM rom, uint addr, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                rom.Data[addr + i] = bytes[i];
        }

        // --- IsEnableData / IsAppnedData truth table (verbatim WinForms parity) ---

        [Theory]
        [InlineData(0x00, true)]   // Left
        [InlineData(0x01, true)]   // Right
        [InlineData(0x02, true)]   // Down
        [InlineData(0x03, true)]   // Up
        [InlineData(0x04, false)]  // Terminator (NOT enable)
        [InlineData(0x05, false)]  // invalid
        [InlineData(0x08, false)]  // invalid
        [InlineData(0x09, true)]   // Highlight
        [InlineData(0x0A, true)]   // Collision mark
        [InlineData(0x0B, false)]  // invalid
        [InlineData(0x0C, true)]   // Speed change
        [InlineData(0xFF, false)]  // invalid
        public void IsEnableData_MatchesWinForms(uint data, bool expected)
        {
            Assert.Equal(expected, EventMoveDataFE7Core.IsEnableData(data));
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x03, false)]
        [InlineData(0x09, true)]   // Highlight — has time byte
        [InlineData(0x0A, false)]  // Collision mark — single byte (NOT appended)
        [InlineData(0x0C, true)]   // Speed change — has time byte
        public void IsAppendedData_MatchesWinForms(uint data, bool expected)
        {
            Assert.Equal(expected, EventMoveDataFE7Core.IsAppendedData(data));
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0x09)]
        [InlineData(0x0A)]
        [InlineData(0x0C)]
        public void IsAppnedData_IsAliasOf_IsAppendedData(uint data)
        {
            // The WinForms-parity typo alias must agree with the corrected name.
            Assert.Equal(EventMoveDataFE7Core.IsAppendedData(data),
                         EventMoveDataFE7Core.IsAppnedData(data));
        }

        [Theory]
        [InlineData(0x00, 1u)]
        [InlineData(0x0A, 1u)]   // 0xA is single-byte
        [InlineData(0x09, 2u)]   // 9 carries a parameter byte
        [InlineData(0x0C, 2u)]   // 0xC carries a parameter byte
        public void Stride_MatchesAppendedType(uint type, uint expected)
        {
            Assert.Equal(expected, EventMoveDataFE7Core.Stride(type));
        }

        // --- WalkCommands ---

        [Fact]
        public void WalkCommands_NullRom_ReturnsEmpty()
        {
            Assert.Empty(EventMoveDataFE7Core.WalkCommands(null, 0x100));
        }

        [Fact]
        public void WalkCommands_SimpleDirections_StopAtTerminator()
        {
            var rom = MakeRom();
            uint addr = 0x1000;
            // Left, Right, Down, End
            WriteBytes(rom, addr, 0x00, 0x01, 0x02, 0x04);

            List<AddrResult> list = EventMoveDataFE7Core.WalkCommands(rom, addr);

            Assert.Equal(3, list.Count);
            Assert.Equal(addr + 0, list[0].addr);
            Assert.Equal(addr + 1, list[1].addr);
            Assert.Equal(addr + 2, list[2].addr);
            Assert.Equal(0x00u, list[0].tag);
            Assert.Equal(0x01u, list[1].tag);
            Assert.Equal(0x02u, list[2].tag);
        }

        [Fact]
        public void WalkCommands_AppendedTypes_AdvanceByTwo()
        {
            var rom = MakeRom();
            uint addr = 0x2000;
            // Up(03), Highlight(09)+param(0x14), Speed(0C)+param(0x02), Down(02), End(04)
            WriteBytes(rom, addr, 0x03, 0x09, 0x14, 0x0C, 0x02, 0x02, 0x04);

            List<AddrResult> list = EventMoveDataFE7Core.WalkCommands(rom, addr);

            // 4 commands: Up @+0, Highlight @+1, Speed @+3, Down @+5
            Assert.Equal(4, list.Count);
            Assert.Equal(addr + 0, list[0].addr); Assert.Equal(0x03u, list[0].tag);
            Assert.Equal(addr + 1, list[1].addr); Assert.Equal(0x09u, list[1].tag);
            Assert.Equal(addr + 3, list[2].addr); Assert.Equal(0x0Cu, list[2].tag);
            Assert.Equal(addr + 5, list[3].addr); Assert.Equal(0x02u, list[3].tag);
        }

        [Fact]
        public void WalkCommands_CollisionMark_IsSingleByte()
        {
            // Regression for the 0xA stride bug (#1440 review finding #1):
            // 0xA must be treated as a single-byte command, NOT skip an extra byte.
            var rom = MakeRom();
            uint addr = 0x3000;
            // Collision mark(0A), Left(00), End(04)
            WriteBytes(rom, addr, 0x0A, 0x00, 0x04);

            List<AddrResult> list = EventMoveDataFE7Core.WalkCommands(rom, addr);

            Assert.Equal(2, list.Count);
            Assert.Equal(addr + 0, list[0].addr); Assert.Equal(0x0Au, list[0].tag);
            Assert.Equal(addr + 1, list[1].addr); Assert.Equal(0x00u, list[1].tag);
        }

        [Fact]
        public void WalkCommands_InvalidLeadingByte_ReturnsEmpty()
        {
            var rom = MakeRom();
            uint addr = 0x4000;
            WriteBytes(rom, addr, 0x05); // invalid -> not enable
            Assert.Empty(EventMoveDataFE7Core.WalkCommands(rom, addr));
        }

        [Fact]
        public void WalkCommands_RespectsRomBounds()
        {
            var rom = MakeRom(0x100);
            // last byte is a valid direction but there is room for exactly it
            uint addr = (uint)rom.Data.Length - 1;
            rom.Data[addr] = 0x00;
            List<AddrResult> list = EventMoveDataFE7Core.WalkCommands(rom, addr);
            // one command, then cursor leaves ROM
            Assert.Single(list);
            Assert.Equal(addr, list[0].addr);
        }

        [Fact]
        public void WalkCommands_AppendedAtEof_NotEmitted_WhenParamOutOfRange()
        {
            var rom = MakeRom(0x100);
            // a Highlight (needs 2 bytes) sitting on the very last byte -> param out of range
            uint addr = (uint)rom.Data.Length - 1;
            rom.Data[addr] = 0x09;
            List<AddrResult> list = EventMoveDataFE7Core.WalkCommands(rom, addr);
            Assert.Empty(list);
        }
    }
}
