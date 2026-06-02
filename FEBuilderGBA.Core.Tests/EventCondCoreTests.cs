using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class EventCondCoreTests
    {
        static Undo.UndoData MakeUndoData(ROM rom) => new Undo.UndoData
        {
            time = System.DateTime.Now,
            name = "test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        [Fact]
        public void Constants_FE8_MatchPlan()
        {
            Assert.Equal(20, EventCondCore.FE8SlotCount);
            Assert.Equal(10, EventCondCore.FE8Active);
            Assert.Equal(80, EventCondCore.FE8HeaderSize);
            Assert.Equal(184, EventCondCore.FE8Total);
        }

        [Fact]
        public void Constants_FE7_MatchPlan()
        {
            Assert.Equal(16, EventCondCore.FE7SlotCount);
            Assert.Equal(6, EventCondCore.FE7Active);
            Assert.Equal(64, EventCondCore.FE7HeaderSize);
            Assert.Equal(132, EventCondCore.FE7Total);
        }

        [Fact]
        public void Constants_FE6_MatchPlan()
        {
            Assert.Equal(7, EventCondCore.FE6SlotCount);
            Assert.Equal(4, EventCondCore.FE6Active);
            Assert.Equal(28, EventCondCore.FE6HeaderSize);
            Assert.Equal(76, EventCondCore.FE6Total);
        }

        [Fact]
        public void AllocNewEventCondBlock_NullRom_ReturnsNotFound()
        {
            uint result = EventCondCore.AllocNewEventCondBlock(null, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void AllocNewEventCondBlock_FE8_TotalIs184_And_ActiveSlotsCorrect()
        {
            var rom = MakeMinimalRom(8);
            var ud = MakeUndoData(rom);
            using (ROM.BeginUndoScope(ud))
            {
                uint off = EventCondCore.AllocNewEventCondBlock(rom, 0);
                Assert.NotEqual(U.NOT_FOUND, off);
                Assert.NotEqual(0u, off);
                Assert.True(off + EventCondCore.FE8Total <= (uint)rom.Data.Length);
                Assert.Equal(80u, (uint)(EventCondCore.FE8SlotCount * 4));
                uint[] sub = { 80, 92, 108, 120, 132, 144, 156, 168, 172, 178 };
                for (int i = 0; i < EventCondCore.FE8Active; i++)
                    Assert.Equal(U.toPointer(off + sub[i]), rom.u32(off + (uint)(i * 4)));
                for (int i = EventCondCore.FE8Active; i < EventCondCore.FE8SlotCount; i++)
                    Assert.Equal(0u, rom.u32(off + (uint)(i * 4)));
            }
        }

        [Fact]
        public void AllocNewEventCondBlock_FE7_TotalIs132_And_ActiveSlotsCorrect()
        {
            var rom = MakeMinimalRom(7);
            var ud = MakeUndoData(rom);
            using (ROM.BeginUndoScope(ud))
            {
                uint off = EventCondCore.AllocNewEventCondBlock(rom, 0);
                Assert.NotEqual(U.NOT_FOUND, off);
                Assert.True(off + EventCondCore.FE7Total <= (uint)rom.Data.Length);
                uint[] sub = { 64, 80, 96, 108, 120, 126 };
                for (int i = 0; i < EventCondCore.FE7Active; i++)
                    Assert.Equal(U.toPointer(off + sub[i]), rom.u32(off + (uint)(i * 4)));
                for (int i = EventCondCore.FE7Active; i < EventCondCore.FE7SlotCount; i++)
                    Assert.Equal(0u, rom.u32(off + (uint)(i * 4)));
            }
        }

        [Fact]
        public void AllocNewEventCondBlock_FE6_TotalIs76_And_ActiveSlotsCorrect()
        {
            var rom = MakeMinimalRom(6);
            var ud = MakeUndoData(rom);
            using (ROM.BeginUndoScope(ud))
            {
                uint off = EventCondCore.AllocNewEventCondBlock(rom, 0);
                Assert.NotEqual(U.NOT_FOUND, off);
                Assert.True(off + EventCondCore.FE6Total <= (uint)rom.Data.Length);
                uint[] sub = { 28, 40, 52, 64 };
                for (int i = 0; i < EventCondCore.FE6Active; i++)
                    Assert.Equal(U.toPointer(off + sub[i]), rom.u32(off + (uint)(i * 4)));
                for (int i = EventCondCore.FE6Active; i < EventCondCore.FE6SlotCount; i++)
                    Assert.Equal(0u, rom.u32(off + (uint)(i * 4)));
            }
        }


        [Fact]
        public void AllocNewEventCondBlock_FE8_SubRegionsAreZeroed()
        {
            var rom = MakeMinimalRom(8);
            var ud = MakeUndoData(rom);
            using (ROM.BeginUndoScope(ud))
            {
                uint off = EventCondCore.AllocNewEventCondBlock(rom, 0);
                Assert.NotEqual(U.NOT_FOUND, off);
                uint hdr = (uint)(EventCondCore.FE8SlotCount * 4);
                for (uint b = hdr; b < EventCondCore.FE8Total; b++)
                    Assert.Equal(0, (int)rom.u8(off + b));
            }
        }

        [Fact]
        public void WriteEventPLIST_NullRom_ReturnsFalse()
            => Assert.False(EventCondCore.WriteEventPLIST(null, 1, 0x1000));

        [Fact]
        public void WriteEventPLIST_Plist0_ReturnsFalse()
            => Assert.False(EventCondCore.WriteEventPLIST(MakeMinimalRom(8), 0, 0x1000));

        [Fact]
        public void WriteEventPLIST_ValidPlist_WritesPointerToSlot_FE8()
        {
            var rom = MakeMinimalRom(8);
            var ud = MakeUndoData(rom);
            using (ROM.BeginUndoScope(ud))
            {
                uint ep = rom.RomInfo.map_event_pointer;
                U.write_u32(rom.Data, ep, U.toPointer(0x20000));
                Assert.True(EventCondCore.WriteEventPLIST(rom, 1, 0x30000));
                Assert.Equal(U.toPointer(0x30000u), rom.u32(0x20000u + 4));
            }
        }

        [Fact]
        public void ResolveEventPlistSlotAddr_NullRom_ReturnsNotFound()
            => Assert.Equal(U.NOT_FOUND, EventCondCore.ResolveEventPlistSlotAddr(null, 1));

        [Fact]
        public void ResolveEventPlistSlotAddr_Plist0_ReturnsNotFound()
            => Assert.Equal(U.NOT_FOUND, EventCondCore.ResolveEventPlistSlotAddr(MakeMinimalRom(8), 0));


        [Fact]
        public void AllocAndWrite_AmbientUndo_RevertsOnRollback_FE8()
        {
            var rom = MakeMinimalRom(8);
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                uint ep = rom.RomInfo.map_event_pointer;
                U.write_u32(rom.Data, ep, U.toPointer(0x20000));
                uint slot1 = 0x20000u + 4;
                uint off;
                var ud = MakeUndoData(rom);
                using (ROM.BeginUndoScope(ud))
                {
                    off = EventCondCore.AllocNewEventCondBlock(rom, 0);
                    Assert.NotEqual(U.NOT_FOUND, off);
                    EventCondCore.WriteEventPLIST(rom, 1, off);
                }
                Assert.Equal(U.toPointer(off), rom.u32(slot1));
                var undo = new Undo();
                undo.Push(ud);
                undo.RunUndo();
                Assert.Equal(0u, rom.u32(slot1));
            }
            finally { CoreState.ROM = origRom; }
        }

        static ROM MakeMinimalRom(int version)
        {
            (string vs, int ms) = version switch {
                6 => ("AFEJ01", 0x800000),
                7 => ("AE7E01", 0x1000000),
                _ => ("BE8E01", 0x1000000),
            };
            var data = new byte[ms];
            var rom = new ROM();
            rom.LoadLow("test.gba", data, vs);
            return rom;
        }
    }
}

