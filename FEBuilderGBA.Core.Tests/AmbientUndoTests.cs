using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class AmbientUndoTests
    {
        static ROM CreateTestRom()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[256]);
            CoreState.ROM = rom;
            return rom;
        }

        [Fact]
        public void AmbientUndo_WhenNull_NoTracking()
        {
            var rom = CreateTestRom();
            // No ambient scope — should write without error
            rom.write_u8(0x10, 0xAB);
            Assert.Equal(0xABu, rom.u8(0x10));
            Assert.Null(ROM.GetAmbientUndoData());
        }

        [Fact]
        public void AmbientUndo_RecordsPositions()
        {
            var rom = CreateTestRom();
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                rom.write_u8(0x10, 0xAA);
                rom.write_u16(0x20, 0xBBCC);
                rom.write_u32(0x30, 0xDDEEFF00);
            }

            // 3 write calls => 3 undo positions
            Assert.Equal(3, ud.list.Count);
            Assert.Equal(0x10u, ud.list[0].addr);
            Assert.Equal(1, ud.list[0].data.Length); // u8 = 1 byte
            Assert.Equal(0x20u, ud.list[1].addr);
            Assert.Equal(2, ud.list[1].data.Length); // u16 = 2 bytes
            Assert.Equal(0x30u, ud.list[2].addr);
            Assert.Equal(4, ud.list[2].data.Length); // u32 = 4 bytes
        }

        [Fact]
        public void AmbientUndo_DisposeClearsState()
        {
            var rom = CreateTestRom();
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            var scope = ROM.BeginUndoScope(ud);
            Assert.NotNull(ROM.GetAmbientUndoData());
            scope.Dispose();
            Assert.Null(ROM.GetAmbientUndoData());
        }

        [Fact]
        public void AmbientUndo_WriteRange_RecordsPosition()
        {
            var rom = CreateTestRom();
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                rom.write_range(0x40, new byte[] { 1, 2, 3, 4 });
            }

            Assert.Single(ud.list);
            Assert.Equal(0x40u, ud.list[0].addr);
            Assert.Equal(4, ud.list[0].data.Length);
        }

        [Fact]
        public void AmbientUndo_WriteFill_RecordsPosition()
        {
            var rom = CreateTestRom();
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                rom.write_fill(0x50, 8, 0xFF);
            }

            Assert.Single(ud.list);
            Assert.Equal(0x50u, ud.list[0].addr);
            Assert.Equal(8, ud.list[0].data.Length);
        }

        [Fact]
        public void AmbientUndo_WriteP32_RecordsPosition()
        {
            var rom = CreateTestRom();
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                rom.write_p32(0x60, 0x10);
            }

            Assert.Single(ud.list);
            Assert.Equal(0x60u, ud.list[0].addr);
            Assert.Equal(4, ud.list[0].data.Length);
        }

        [Fact]
        public void AmbientUndo_WriteU4_RecordsPosition()
        {
            var rom = CreateTestRom();
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                rom.write_u4(0x70, 0x0A, false);
            }

            Assert.Single(ud.list);
            Assert.Equal(0x70u, ud.list[0].addr);
            Assert.Equal(1, ud.list[0].data.Length);
        }

        [Fact]
        public void AmbientUndo_RecordsOriginalDataBeforeWrite()
        {
            var rom = CreateTestRom();
            rom.write_u8(0x10, 0x42); // pre-existing data (no ambient scope)
            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                rom.write_u8(0x10, 0xFF);
            }

            // The undo position should have captured the ORIGINAL byte (0x42)
            Assert.Equal(0x42, ud.list[0].data[0]);
            // The ROM should now have the new value
            Assert.Equal(0xFFu, rom.u8(0x10));
        }
    }
}
