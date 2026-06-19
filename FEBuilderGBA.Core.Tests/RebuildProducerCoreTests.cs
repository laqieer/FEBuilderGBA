using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="RebuildProducerCore"/> (#1261 slice 2a).
    /// Synthetic ROMs prove the descriptor-driven walker reproduces the WinForms
    /// <c>MakeAllDataLength</c> "table walk + IFR Address" behaviour for each
    /// <see cref="RebuildProducerCore.DataCountRule"/>; a real-FE8U test proves the
    /// batch finds the known item/class tables at the expected counts.
    /// </summary>
    [Collection("SharedState")]
    public class RebuildProducerCoreTests
    {
        // ---- helpers -------------------------------------------------------

        static ROM CreateTestRom(int size = 0x4000)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        // GBA pointer = offset | 0x08000000
        static uint Ptr(uint offset) => offset | 0x08000000;

        // ---- WalkAndAdd: each DataCountRule reproduces InputFormRef walk -----

        [Fact]
        public void WalkAndAdd_U8NotEqual_StopsAtTerminator_AndEmitsLengthPlusOne()
        {
            var rom = CreateTestRom();
            // table at 0x1000, blockSize 2, terminator u8(addr+0)==0xFF; 3 valid entries
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(table + 0, 0x01);
            rom.write_u8(table + 2, 0x02);
            rom.write_u8(table + 4, 0x03);
            rom.write_u8(table + 6, 0xFF); // terminator

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "T",
                PointerField = _ => pointer,
                BlockSize = 2,
                Rule = RebuildProducerCore.DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0xFF,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            Address a = list[0];
            Assert.Equal(table, a.Addr);
            Assert.Equal(pointer, a.Pointer);
            Assert.Equal(2u, a.BlockSize);
            // dataCount = 3, length = blockSize * (count + 1) = 2 * 4 = 8
            Assert.Equal(8u, a.Length);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, a.DataType);
        }

        [Fact]
        public void WalkAndAdd_FixedCount_EmitsExactCount()
        {
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Fixed8",
                PointerField = _ => pointer,
                BlockSize = 20,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 8,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // length = 20 * (8 + 1) = 180
            Assert.Equal(180u, list[0].Length);
        }

        [Fact]
        public void WalkAndAdd_U8NotZeroIndex0Always_CountsEntryZeroEvenIfZero()
        {
            var rom = CreateTestRom();
            // ClassForm rule: i==0 always exists; else u8(addr+4)!=0.
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 16;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: u8(+4)==0 but still counts
            // entry 1: u8(+4)=5 -> exists
            rom.write_u8(table + block + 4, 0x05);
            // entry 2: u8(+4)==0 -> stop

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Class",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 2 (entries 0 and 1), length = 16 * (2 + 1) = 48
            Assert.Equal(48u, list[0].Length);
        }

        [Fact]
        public void WalkAndAdd_U16NotZero_StopsAtZeroU16()
        {
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u16(table + 0, 0x0011);
            rom.write_u16(table + 2, 0x0022);
            rom.write_u16(table + 4, 0x0000); // stop

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "TerrainEng",
                PointerField = _ => pointer,
                BlockSize = 2,
                Rule = RebuildProducerCore.DataCountRule.U16NotZero,
                RuleOffset = 0,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 2, length = 2 * 3 = 6
            Assert.Equal(6u, list[0].Length);
        }

        [Fact]
        public void WalkAndAdd_MultiPointer_EmitsOnePerNonZeroPointer()
        {
            var rom = CreateTestRom();
            uint p0 = 0x0240, p1 = 0x0244, p2 = 0x0248;
            uint t0 = 0x1000, t2 = 0x1100;
            rom.write_u32(p0, Ptr(t0));
            rom.write_u8(t0 + 0, 0x01);
            rom.write_u8(t0 + 1, 0x00); // terminator (blockSize 1, u8!=0)
            // p1 left zero -> skipped
            rom.write_u32(p2, Ptr(t2));
            rom.write_u8(t2 + 0, 0x05);
            rom.write_u8(t2 + 1, 0x06);
            rom.write_u8(t2 + 2, 0x00); // terminator

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Multi",
                PointerFields = _ => new uint[] { p0, p1, p2 },
                BlockSize = 1,
                Rule = RebuildProducerCore.DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // p1 is zero -> only 2 Addresses
            Assert.Equal(2, list.Count);
            Assert.Equal(t0, list[0].Addr);
            Assert.Equal(t2, list[1].Addr);
            // t0: dataCount 1 -> length 1*(1+1)=2 ; t2: dataCount 2 -> length 1*3=3
            Assert.Equal(2u, list[0].Length);
            Assert.Equal(3u, list[1].Length);
        }

        [Fact]
        public void WalkAndAdd_PointerIndexes_ArePreservedOnAddress()
        {
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));
            // 1 entry exists, then a zero terminator at +0x24.
            rom.write_u16(table + 0, 0x0001);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Item",
                PointerField = _ => pointer,
                BlockSize = 0x24,
                Rule = RebuildProducerCore.DataCountRule.U16NotZero,
                RuleOffset = 0,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { 12, 16 },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            Assert.Equal(new uint[] { 12, 16 }, list[0].PointerIndexes);
        }

        [Fact]
        public void WalkAndAdd_UnsafePointer_EmitsNothing()
        {
            var rom = CreateTestRom();
            uint pointer = 0x0240;
            // pointer slot holds a bogus value (not a safe ROM pointer)
            rom.write_u32(pointer, 0xFFFFFFFF);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Bad",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Empty(list);
        }

        // ---- plumbing: cancellation returns partial list --------------------

        [Fact]
        public void MakeAllStructPointersList_Cancelled_ReturnsImmediately()
        {
            var rom = CreateTestRom();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var list = RebuildProducerCore.MakeAllStructPointersList(rom, null, cts.Token);

            // Cancelled before processing any descriptor -> empty list (no throw).
            Assert.Empty(list);
        }

        [Fact]
        public void MakeAllStructPointersList_NullRom_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => RebuildProducerCore.MakeAllStructPointersList(null));
        }

        [Fact]
        public void GetNotYetPortedForms_IsNonEmpty_AndTracksDeferredCoverage()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
            Assert.NotEmpty(notYet);
            // The heavy editors that need extraction first must be tracked, not dropped.
            Assert.Contains("TextForm", notYet);
            Assert.Contains("EventCondForm", notYet);
            Assert.Contains("SongTableForm", notYet);
        }

        // ---- real-FE8U parity: the batch finds the known tables -------------

        [Fact]
        public void MakeAllStructPointersList_FE8U_FindsKnownItemAndClassTables()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip when no ROM available (env-only)

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 8) return; // this assertion is FE8U-specific

                var progressLines = new List<string>();
                var progress = new Progress<string>(s => progressLines.Add(s));
                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);

                Assert.NotEmpty(list);

                // The item table must be present at the RomInfo.item_pointer target,
                // with the {12,16} pointer columns and a sane FE8U item count (~0x9F).
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                Address item = list.FirstOrDefault(a => a.Addr == itemBase && a.Info == "Item");
                Assert.NotNull(item);
                Assert.Equal(rom.RomInfo.item_datasize, item.BlockSize);
                Assert.Equal(new uint[] { 12, 16 }, item.PointerIndexes);
                uint itemCount = item.Length / item.BlockSize - 1; // length = block*(count+1)
                Assert.True(itemCount >= 0x80 && itemCount <= 0x100,
                    "FE8U item count out of expected range: 0x" + itemCount.ToString("X"));

                // The class table must be present too.
                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                Address cls = list.FirstOrDefault(a => a.Addr == classBase && a.Info == "Class");
                Assert.NotNull(cls);
                Assert.Equal(rom.RomInfo.class_datasize, cls.BlockSize);
                uint classCount = cls.Length / cls.BlockSize - 1;
                Assert.True(classCount >= 0x40 && classCount <= 0x100,
                    "FE8U class count out of expected range: 0x" + classCount.ToString("X"));
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        static string FindTestRom()
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string romsDir = System.IO.Path.Combine(dir, "roms");
                    if (System.IO.Directory.Exists(romsDir))
                    {
                        string path = System.IO.Path.Combine(romsDir, "FE8U.gba");
                        if (System.IO.File.Exists(path)) return path;
                    }
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
