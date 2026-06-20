// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice 2v — the ASM-path producer
// (AppendAllAsmStructPointers), a Core port of U.AppendAllASMStructPointersList.
//
// The ldrmap-free table emitters expose explicit-address *At / *Table seams
// (matching the data-path EmitSoundFootStepsAt precedent), so the synthetic-ROM
// unit tests supply the base pointer + count directly and never need a populated
// RomInfo. The version-gated public methods (EmitEventFunctionPointer) and the
// AppendAllAsmStructPointers entrypoint are exercised on a real versioned ROM.
//
// Coverage:
//   1. The 6 ldrmap-FREE InputFormRef-table emitters (EventFunctionPointer
//      [FE7 + FE8 dual-table], Command85Pointer, ItemEffectPointer [magic cap],
//      UnitIncreaseHeight [switch2, NOT_FOUND main pointer], MapLoadFunction
//      [WF verbatim no-op], MapMiniMapTerrain [fixed count]) — main IFR Address +
//      per-entry ASM AddFunction, BasePointer vs NOT_FOUND, near-EOF truncation.
//   2. EmitEventAsmMapList — both WF passes (thumb -> ASM, event -> EVENTSCRIPT
//      alias) + the disasm-unwired throw.
//   3. BuildLdrMap — pure-ROM LDR map matches DisassemblerTrumb.MakeLDRMap.
//   4. AppendAllAsmStructPointers — the ldrmap!=null gate, deferred-form coverage
//      honesty (IsComplete false), cancellation, CoreState.ROM guard, and the
//      disasm-unwired re-report of EventScript(MakeEventASMMAPList).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerAsmTests : IDisposable
    {
        // xUnit constructs a fresh instance per test and Disposes it after. CreateTestRom mutates the
        // CoreState.ROM / SystemTextEncoder / EventScript globals; capture the baseline here and restore
        // it in Dispose so a test that leaves them mutated can't bleed into a later (order-dependent) test.
        readonly ROM _savedRom = CoreState.ROM;
        readonly ISystemTextEncoder _savedEncoder = CoreState.SystemTextEncoder;
        readonly EventScript _savedEventScript = CoreState.EventScript;

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedEncoder;
            CoreState.EventScript = _savedEventScript;
        }

        // ---- helpers -------------------------------------------------------

        static ROM CreateTestRom(int size = 0x8000)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            return rom;
        }

        static uint Ptr(uint offset) => offset | 0x08000000u;

        static ROM MakeVersionedRom(string versionString, int size = 0x0200_0000)
        {
            var rom = new ROM();
            bool ok = rom.LoadLow("fake.gba", new byte[size], versionString);
            Assert.True(ok, "LoadLow did not recognize version string: " + versionString);
            return rom;
        }

        static EventScript BuildEventScript(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            var prop = typeof(EventScript).GetProperty("Scripts");
            prop!.SetValue(es, scripts);
            return es;
        }

        // The InputFormRef_ASM IsDataExists used by EventFunctionPointer (v8) and Command85: write a
        // table whose slots are valid then a terminator. We test the shared EmitAsmPointerTable with the
        // exact Command85 predicate via EmitCommand85Pointer's seam (its predicate is representative).

        // ====================================================================
        // EmitCommand85Pointer (via the shared EmitAsmPointerTable seam)
        // ====================================================================

        // Command85 predicate: isPointerOrNULL(u32) && (u32==0 || u32 > 0x08000100).
        static Func<int, uint, bool> Command85Predicate(ROM rom) => (int i, uint addr) =>
        {
            uint a = rom.u32(addr);
            if (!U.isPointerOrNULL(a)) return false;
            if (a == 0) return true;
            if (a <= 0x08000100) return false;
            return true;
        };

        [Fact]
        public void EmitAsmPointerTable_EmitsMainIfrAndAsmFunctionPerEntry()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0400;      // base-pointer field (a RomInfo slot, here synthetic)
            uint table = 0x1000;        // base = p32(pointer)
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000));
            rom.write_u32(table + 4, Ptr(0x2100));
            rom.write_u32(table + 8, Ptr(0x2200));
            rom.write_u32(table + 12, 0x08000050); // <= 0x08000100 -> terminates the count

            var list = new List<Address>();
            RebuildProducerCore.EmitAsmPointerTable(rom, list, pointer, 4, Command85Predicate(rom),
                "Command85Pointer", Address.DataTypeEnum.InputFormRef_ASM);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);    // BasePointer IS the (safe) slot.
            Assert.Equal(4u, main.BlockSize);
            Assert.Equal(4u * (3 + 1), main.Length);
            Assert.Equal(new uint[] { 0 }, main.PointerIndexes);

            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(3, asms.Count);
            Assert.Contains(asms, a => a.Addr == 0x2000);
            Assert.Contains(asms, a => a.Addr == 0x2100);
            Assert.Contains(asms, a => a.Addr == 0x2200);
        }

        [Fact]
        public void EmitAsmPointerTable_KeepsNullSlots()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000));
            rom.write_u32(table + 4, 0x00000000); // NULL is KEPT (predicate returns true)
            rom.write_u32(table + 8, Ptr(0x2200));
            rom.write_u32(table + 12, 0x08000010); // terminator

            var list = new List<Address>();
            RebuildProducerCore.EmitAsmPointerTable(rom, list, pointer, 4, Command85Predicate(rom),
                "Command85Pointer", Address.DataTypeEnum.InputFormRef_ASM);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Equal(4u * (3 + 1), main.Length); // 3 entries incl. the NULL slot.
            // AddFunction over the NULL slot is a no-op (u32==0 -> !isSafetyPointer), so only 2 ASM.
            Assert.Equal(2, list.Count(a => a.DataType == Address.DataTypeEnum.ASM));
        }

        [Fact]
        public void EmitAsmPointerTable_UnsafeBasePointer_EmitsNothing()
        {
            var rom = CreateTestRom(0x8000);
            // base-pointer field points at a slot whose p32 is not a safe offset -> base 0 -> no emit.
            uint pointer = 0x0400;
            rom.write_u32(pointer, 0x00000003); // not a pointer, not a safe offset
            var list = new List<Address>();
            RebuildProducerCore.EmitAsmPointerTable(rom, list, pointer, 4, Command85Predicate(rom),
                "Command85Pointer", Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitAsmPointerTable_ZeroBlock_EmitsNothing()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(0x1000));
            var list = new List<Address>();
            RebuildProducerCore.EmitAsmPointerTable(rom, list, pointer, 0, Command85Predicate(rom),
                "X", Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Empty(list); // block 0 -> guarded (would loop forever in getBlockDataCount).
        }

        [Fact]
        public void EmitAsmPointerTable_CountRunsPastEof_TruncatesWithoutThrowing()
        {
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint pointer = 0x0400;
            uint table = size - 8; // only 2 of its 4-byte entries fit before EOF.
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x1000));
            rom.write_u32(table + 4, Ptr(0x1100));

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitAsmPointerTable(rom, list, pointer, 4, Command85Predicate(rom),
                    "Command85Pointer", Address.DataTypeEnum.InputFormRef_ASM));
            Assert.Null(ex);
            Assert.True(list.Count(a => a.DataType == Address.DataTypeEnum.ASM) <= 2);
        }

        // ====================================================================
        // EmitMapMiniMapTerrainAt — fixed-count (map_terrain_type_count)
        // ====================================================================

        [Fact]
        public void EmitMapMiniMapTerrainAt_EmitsFixedCountByTerrainTypeCount()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            for (uint i = 0; i < 8; i++)
            {
                rom.write_u32(table + i * 4, Ptr(0x2000 + i * 0x10));
            }

            var list = new List<Address>();
            RebuildProducerCore.EmitMapMiniMapTerrainAt(rom, list, pointer, terrainCount: 5);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(4u * (5 + 1), main.Length);
            Assert.Equal(5, list.Count(a => a.DataType == Address.DataTypeEnum.ASM));
        }

        // ====================================================================
        // EmitSwitch2GatedAsmTable (UnitIncreaseHeight) — NOT_FOUND main pointer
        // ====================================================================

        static void PlantSwitch2(ROM rom, uint switch2Addr, byte count)
        {
            rom.write_u8(switch2Addr + 0, 0x00);
            rom.write_u8(switch2Addr + 1, 0x3A);
            rom.write_u8(switch2Addr + 2, count);
            rom.write_u8(switch2Addr + 3, 0x2A);
        }

        [Fact]
        public void EmitSwitch2GatedAsmTable_Enabled_MainPointerIsNotFound()
        {
            var rom = CreateTestRom(0x8000);
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400;
            uint table = 0x1000;
            PlantSwitch2(rom, switch2Addr, count: 2); // entries = 3
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000));
            rom.write_u32(table + 4, Ptr(0x2100));
            rom.write_u32(table + 8, Ptr(0x2200));

            var list = new List<Address>();
            RebuildProducerCore.EmitSwitch2GatedAsmTable(rom, list, switch2Addr, pointer,
                "UnitIncreaseHeight");

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Equal(table, main.Addr);
            // CRITICAL: BasePointer is 0 after ReInit -> AddAddress emits NOT_FOUND, not the slot.
            Assert.Equal(U.NOT_FOUND, main.Pointer);
            Assert.Equal(4u * (3 + 1), main.Length);
            Assert.Equal(3, list.Count(a => a.DataType == Address.DataTypeEnum.ASM));
        }

        [Fact]
        public void EmitSwitch2GatedAsmTable_Disabled_EmitsNothing()
        {
            var rom = CreateTestRom(0x8000);
            uint switch2Addr = 0x0300; // all-zero -> subOp/cmpOp out of range -> disabled.
            uint pointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000));

            var list = new List<Address>();
            RebuildProducerCore.EmitSwitch2GatedAsmTable(rom, list, switch2Addr, pointer,
                "UnitIncreaseHeight");
            Assert.Empty(list);
        }

        // ====================================================================
        // EmitMapLoadFunction — WF verbatim no-op (Init base 0, no ReInit)
        // ====================================================================

        [Fact]
        public void EmitMapLoadFunction_IsVerbatimNoOp_EmitsNothing()
        {
            // WF MakeAllDataLength constructs the IFR with base pointer 0 and never calls ReInit, so
            // AddAddress's !isSafetyOffset(0) guard emits nothing. EmitMapLoadFunction is RomInfo-driven
            // but routes through EmitAsmPointerTable with basePointerField 0 -> no-op regardless of ROM.
            var fe8 = MakeVersionedRom("BE8E01");
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                var list = new List<Address>();
                RebuildProducerCore.EmitMapLoadFunction(fe8, list);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitMapLoadFunction_BasePointerFieldZero_IsAlwaysNoOp()
        {
            // Belt-and-suspenders: even with a fully valid table planted, basePointerField 0 emits nothing.
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            rom.write_u32(table + 0, Ptr(0x2000));
            var list = new List<Address>();
            RebuildProducerCore.EmitAsmPointerTable(rom, list, 0, 4, (i, a) => true,
                "MapLoadFunction", Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Empty(list);
        }

        // ====================================================================
        // EmitOAMSP / EmitOAMSPCore — OAMSPForm.MakeAllDataLength (slice 2w)
        // ====================================================================
        //
        // The OAM walk needs a populated ROM region past compress_image_borderline_address
        // (loop 1's gate) — so these tests use MakeVersionedRom("BE8E01") (borderline 0xDB000,
        // 32 MiB) and plant a hand-authored OAM byte stream + a synthetic ldrmap, exactly as
        // the *At seams let the data-path tests do without RomInfo grafting.

        // Plant one OAM-12 record stream at oam12Off: one data record ([0]==0 -> continue) then an
        // all-zero terminator record. Returns the byte length the walker should report (24).
        static uint PlantOam12(ROM rom, uint oam12Off)
        {
            // record 0: data ([0]==0, rest arbitrary non-terminator-tripping)
            rom.write_u8(oam12Off + 0, 0x00);
            for (uint b = 1; b < 12; b++) rom.write_u8(oam12Off + b, (byte)(0x10 + b));
            // record 1: all-zero first 8 bytes -> terminator
            for (uint b = 0; b < 12; b++) rom.write_u8(oam12Off + 12 + b, 0x00);
            return 24;
        }

        // Plant an OAMSP word table at oamspOff: `ptrCount` words pointing to distinct OAM-12 sub-tables
        // (each carved out 0x100 apart starting at oam12Base), then a 0x8X0000XX terminator word.
        // Returns the OAMSP word-table byte length (= (ptrCount + 1) * 4).
        static uint PlantOamspTable(ROM rom, uint oamspOff, uint oam12Base, int ptrCount)
        {
            for (int k = 0; k < ptrCount; k++)
            {
                uint oam12Off = oam12Base + (uint)k * 0x100;
                PlantOam12(rom, oam12Off);
                rom.write_u32(oamspOff + (uint)k * 4, Ptr(oam12Off));
            }
            rom.write_u32(oamspOff + (uint)ptrCount * 4, 0x80000001); // OAM term 0x8X0000XX
            return (uint)(ptrCount + 1) * 4;
        }

        [Fact]
        public void EmitOAMSPCore_ValidLdrmapEntry_EmitsOamspAndOam12()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01"); // borderline 0xDB000
                CoreState.ROM = fe8;

                uint oamspOff = 0x00E0000;     // past borderline
                uint oam12Base = 0x00E1000;
                // 2 OAM-12 pointers + terminator -> main length 12 (>= loop-1 threshold 4*3).
                uint expectMainLen = PlantOamspTable(fe8, oamspOff, oam12Base, ptrCount: 2);

                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(oamspOff),
                        ldr_data_address = 0x00E5000, // where the pointer lives (becomes the OAMSP pointer)
                        ldr_address = 0x00E6000,
                    }
                };

                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap, new Dictionary<uint, string>());

                Address main = list.Single(a => a.DataType == Address.DataTypeEnum.OAMSP);
                Assert.Equal(oamspOff, main.Addr);
                Assert.Equal(expectMainLen, main.Length);
                Assert.Equal(0x00E5000u, main.Pointer); // ldr_data_address

                var oam12 = list.Where(a => a.DataType == Address.DataTypeEnum.OAMSP12).ToList();
                Assert.Equal(2, oam12.Count);
                Assert.Contains(oam12, a => a.Addr == oam12Base && a.Length == 24);
                Assert.Contains(oam12, a => a.Addr == oam12Base + 0x100 && a.Length == 24);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitOAMSPCore_ConfigDictEntry_EmitsOamspWithNotFoundPointer()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;

                uint oamspOff = 0x00E0000;
                uint oam12Base = 0x00E1000;
                PlantOamspTable(fe8, oamspOff, oam12Base, ptrCount: 1); // length 8 (>= loop-2 threshold 4)

                var oamName = new Dictionary<uint, string> { { Ptr(oamspOff), "TestSprite" } };

                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap: null, oamName: oamName);

                Address main = list.Single(a => a.DataType == Address.DataTypeEnum.OAMSP);
                Assert.Equal(oamspOff, main.Addr);
                Assert.Equal(8u, main.Length);
                Assert.Equal(U.NOT_FOUND, main.Pointer); // loop-2 pointer is NOT_FOUND
                Assert.Equal(1, list.Count(a => a.DataType == Address.DataTypeEnum.OAMSP12));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitOAMSPCore_DedupAcrossBothLoops()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;

                uint oamspOff = 0x00E0000;
                uint oam12Base = 0x00E1000;
                PlantOamspTable(fe8, oamspOff, oam12Base, ptrCount: 2);

                // The SAME table address appears in BOTH loop 1 (ldrmap) and loop 2 (config dict).
                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(oamspOff), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var oamName = new Dictionary<uint, string> { { Ptr(oamspOff), "Dup" } };

                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap, oamName);

                // alreadyMatch keyed on the offset: loop 1 records it, loop 2 skips it -> exactly ONE OAMSP.
                Assert.Equal(1, list.Count(a => a.DataType == Address.DataTypeEnum.OAMSP));
                // alreadyMatch12 dedups the OAM-12 sub-tables too -> exactly two distinct OAM-12s.
                Assert.Equal(2, list.Count(a => a.DataType == Address.DataTypeEnum.OAMSP12));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitOAMSPCore_EmptyZeroedRom_EmitsNothing()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01"); // all-zero 32 MiB ROM
                CoreState.ROM = fe8;

                // An ldrmap pointing at all-zero data: u32 reads 0 -> !isSafetyPointer -> NOT_FOUND -> skip.
                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(0x00E0000), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap, new Dictionary<uint, string>());
                Assert.Empty(list);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitOAMSPCore_NearEofSlot_DoesNotThrow()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;

                // An OAMSP start within a few bytes of EOF: the Calc* `while (addr < Data.Length-4/-12)`
                // bound (and the start guard) must keep every u32/getBinaryData read in-bounds.
                uint nearEof = (uint)fe8.Data.Length - 6;
                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(nearEof), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var list = new List<Address>();
                var ex = Record.Exception(() =>
                    RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap, new Dictionary<uint, string>()));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitOAMSPCore_BeforeBorderline_SkippedInLoop1()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01"); // borderline 0xDB000
                CoreState.ROM = fe8;

                uint beforeBorder = 0x00010000; // < 0xDB000
                uint oam12Base = 0x00011000;
                PlantOamspTable(fe8, beforeBorder, oam12Base, ptrCount: 2);

                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(beforeBorder), ldr_data_address = 0x00015000, ldr_address = 0x00016000,
                    }
                };
                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap, new Dictionary<uint, string>());
                Assert.Empty(list); // loop 1 skips addr < borderline.
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitOAMSPCore_ShortTableBelowThreshold_LoopOneSkips()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;

                uint oamspOff = 0x00E0000;
                uint oam12Base = 0x00E1000;
                // 1 pointer + terminator -> main length 8, BELOW loop-1 threshold (4*3=12) -> skipped.
                PlantOamspTable(fe8, oamspOff, oam12Base, ptrCount: 1);

                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(oamspOff), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSPCore(fe8, list, ldrmap, new Dictionary<uint, string>());
                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.OAMSP);
            }
            finally { CoreState.ROM = saved; }
        }

        // EmitOAMSP (the public entry) loads the oam_name_ config dict via ConfigDataFilename, which on a
        // versioned ROM hits U.IsRequiredFileExist (a Debug.Assert when the file is absent). Build a temp
        // config/data dir with an empty oam_name_ALL.txt (the ConfigDataFilename ALL fallback) so the load
        // path runs without tripping the required-config assert; returns the temp BaseDirectory.
        static string MakeTempConfigBaseDir()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "feb_oamsp_" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(baseDir, "config", "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "oam_name_ALL.txt"), "");
            return baseDir;
        }

        [Fact]
        public void EmitOAMSP_PublicEntry_LoadsConfigAndEmits()
        {
            var saved = CoreState.ROM;
            var savedBaseDir = CoreState.BaseDirectory;
            string baseDir = null;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                baseDir = MakeTempConfigBaseDir();
                CoreState.BaseDirectory = baseDir;

                uint oamspOff = 0x00E0000;
                PlantOamspTable(fe8, oamspOff, 0x00E1000, ptrCount: 2);
                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(oamspOff), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var list = new List<Address>();
                RebuildProducerCore.EmitOAMSP(fe8, list, ldrmap);
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.OAMSP && a.Addr == oamspOff);
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.BaseDirectory = savedBaseDir;
                if (baseDir != null) try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void AppendAllAsmStructPointers_IsUseOamspFalse_EmitsNoOamsp()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                uint oamspOff = 0x00E0000;
                PlantOamspTable(fe8, oamspOff, 0x00E1000, ptrCount: 2);
                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(oamspOff), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var list = new List<Address>();
                // isUseOAMSP defaults to false -> the OAMSP gate is closed, even with a valid table present.
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap,
                    isUseOAMSP: false);
                Assert.False(res.Cancelled);
                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.OAMSP);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void AppendAllAsmStructPointers_IsUseOamspTrue_EmitsOamsp()
        {
            var saved = CoreState.ROM;
            var savedBaseDir = CoreState.BaseDirectory;
            string baseDir = null;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                // The full entrypoint's EmitOAMSP loads the RomInfo-derived oam_name_ config dict via
                // ConfigDataFilename; supply an empty temp config so the load runs (loop 2 no-op; loop 1 runs).
                baseDir = MakeTempConfigBaseDir();
                CoreState.BaseDirectory = baseDir;

                uint oamspOff = 0x00E0000;
                PlantOamspTable(fe8, oamspOff, 0x00E1000, ptrCount: 2);
                // The ldrmap MUST actually contain the OAMSP pointer for loop 1 to find it. Drive EmitOAMSP
                // through the full entrypoint with a hand-built ldrmap.
                var ldrmap = new List<DisassemblerTrumb.LDRPointer>
                {
                    new DisassemblerTrumb.LDRPointer
                    {
                        ldr_data = Ptr(oamspOff), ldr_data_address = 0x00E5000, ldr_address = 0x00E6000,
                    }
                };
                var list = new List<Address>();
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap,
                    isUseOAMSP: true);
                Assert.False(res.Cancelled);
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.OAMSP && a.Addr == oamspOff);
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.BaseDirectory = savedBaseDir;
                if (baseDir != null) try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        // ====================================================================
        // EmitItemEffectPointerAt — InputFormRef_MIX + magic-count cap
        // ====================================================================

        [Fact]
        public void EmitItemEffectPointerAt_CapsAsmAtMagicCount_LengthIsFullDataCount()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000));
            rom.write_u32(table + 4, Ptr(0x2100));
            rom.write_u32(table + 8, Ptr(0x2200));
            rom.write_u32(table + 12, Ptr(0x2300));
            rom.write_u32(table + 16, Ptr(0x2400));
            rom.write_u32(table + 20, 0x08000010); // terminator (5 valid entries)

            var list = new List<Address>();
            // magic count = 3 -> only first 3 entries become ASM, but table length reflects all 5.
            RebuildProducerCore.EmitItemEffectPointerAt(rom, list, pointer, magicOriginalCount: 3);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_MIX);
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(4u * (5 + 1), main.Length);

            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(3, asms.Count);
            Assert.Contains(asms, a => a.Addr == 0x2000);
            Assert.Contains(asms, a => a.Addr == 0x2200);
            Assert.DoesNotContain(asms, a => a.Addr == 0x2300);
        }

        // ====================================================================
        // EmitEventFunctionPointer — version split (real ROM)
        // ====================================================================

        // Plant a valid EventFunctionPointer table at the FE8U RomInfo slots so the producer has
        // something to walk (the blank synthetic ROM has zeros at those offsets -> p32 == 0 -> no base).
        // FE8U: event_function_pointer_table_pointer = 0x0CEE0, table2 = 0x0CF08.
        static void PlantFe8EventFunctionTables(ROM fe8)
        {
            uint main = fe8.RomInfo.event_function_pointer_table_pointer;
            uint world = fe8.RomInfo.event_function_pointer_table2_pointer;
            uint mainTable = 0x00900000u;
            uint worldTable = 0x00900100u;
            fe8.write_u32(main, Ptr(mainTable));
            fe8.write_u32(world, Ptr(worldTable));
            // One valid odd-thumb ASM entry each, then a non-pointer terminator.
            fe8.write_u32(mainTable + 0, Ptr(0x00910000u) | 1);
            fe8.write_u32(mainTable + 4, 0x00000000);
            fe8.write_u32(worldTable + 0, Ptr(0x00910100u) | 1);
            fe8.write_u32(worldTable + 4, 0x00000000);
        }

        [Fact]
        public void EmitEventFunctionPointer_FE8_EmitsTwoTables()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                Assert.Equal(8, fe8.RomInfo.version);
                PlantFe8EventFunctionTables(fe8);

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitEventFunctionPointer(fe8, list));
                Assert.Null(ex);

                var mains = list.Where(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM
                    && a.Info != null && a.Info.StartsWith("EventFunctionPointer")).ToList();
                Assert.Equal(2, mains.Count);
                Assert.Contains(mains, a => a.Info == "EventFunctionPointer");
                Assert.Contains(mains, a => a.Info == "EventFunctionPointer Worldmap");
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void EmitEventFunctionPointer_FE7_NoWorldmapTable()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe7 = MakeVersionedRom("AE7E01"); // FE7U
                CoreState.ROM = fe7;
                Assert.True(fe7.RomInfo.version <= 7);

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitEventFunctionPointer(fe7, list));
                Assert.Null(ex);
                Assert.DoesNotContain(list, a => a.Info == "EventFunctionPointer Worldmap");
            }
            finally { CoreState.ROM = saved; }
        }

        // ====================================================================
        // BuildLdrMap
        // ====================================================================

        [Fact]
        public void BuildLdrMap_MatchesDisassemblerMakeLdrMap()
        {
            var rom = CreateTestRom(0x8000);
            rom.write_u16(0x0200, 0x4800);          // LDR r0,[pc,#0]
            rom.write_u32(0x0204, Ptr(0x1000));     // literal pointer

            var built = RebuildProducerCore.BuildLdrMap(rom);
            var direct = DisassemblerTrumb.MakeLDRMap(rom.Data, 0x100);
            Assert.Equal(direct.Count, built.Count);
            for (int i = 0; i < direct.Count; i++)
            {
                Assert.Equal(direct[i].ldr_address, built[i].ldr_address);
                Assert.Equal(direct[i].ldr_data_address, built[i].ldr_data_address);
                Assert.Equal(direct[i].ldr_data, built[i].ldr_data);
            }
        }

        [Fact]
        public void BuildLdrMap_NullRom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.BuildLdrMap(null));
        }

        // ====================================================================
        // EmitEventAsmMapList — both WF passes
        // ====================================================================

        [Fact]
        public void EmitEventAsmMapList_ThumbWord_EmitsAsmAddress()
        {
            uint asmTarget = 0x00901000u;
            var script = MakeScriptWithWords(Ptr(asmTarget) | 1); // thumb bit set
            var es = BuildEventScript(script);
            WithEnv(es, rom =>
            {
                var list = new List<Address>();
                RebuildProducerCore.EmitEventAsmMapList(rom, list);

                var asm = list.Single(a => a.DataType == Address.DataTypeEnum.ASM);
                Assert.StartsWith("CALL_ASM_FROM_EVENT", asm.Info);
                Assert.Equal(0u, asm.Length);
            });
        }

        [Fact]
        public void EmitEventAsmMapList_EventWord_NotFoundInRom_EmitsEventScriptAlias()
        {
            uint eventTarget = 0x00905000u;
            var script = MakeScriptWithWords(Ptr(eventTarget)); // no thumb bit
            var es = BuildEventScript(script);
            WithEnv(es, rom =>
            {
                var list = new List<Address>();
                RebuildProducerCore.EmitEventAsmMapList(rom, list);

                var evt = list.Single(a => a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.StartsWith("CALL_EVENT", evt.Info);
                Assert.Equal(0u, evt.Length);
                Assert.Equal(eventTarget, evt.Addr);
            });
        }

        [Fact]
        public void EmitEventAsmMapList_DisasmUnwired_Throws()
        {
            var savedEs = CoreState.EventScript;
            var savedRom = CoreState.ROM;
            try
            {
                var rom = CreateTestRom(0x8000);
                CoreState.EventScript = null;
                Assert.Throws<InvalidOperationException>(() =>
                    RebuildProducerCore.EmitEventAsmMapList(rom, new List<Address>()));
            }
            finally
            {
                CoreState.EventScript = savedEs;
                CoreState.ROM = savedRom;
            }
        }

        // ====================================================================
        // AppendAllAsmStructPointers — the entrypoint
        // ====================================================================

        [Fact]
        public void AppendAllAsmStructPointers_NullLdrmap_SkipsTheGatedGroup()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                PlantFe8EventFunctionTables(fe8); // a valid table EXISTS, yet the null-ldrmap gate suppresses it.
                var list = new List<Address>();
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap: null);
                Assert.False(res.Cancelled);
                Assert.DoesNotContain(list, a => a.Info != null && a.Info.StartsWith("EventFunctionPointer"));
                Assert.DoesNotContain(list, a => a.Info != null && a.Info.StartsWith("Command85"));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void AppendAllAsmStructPointers_WithLdrmap_RunsTheGatedGroup()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                PlantFe8EventFunctionTables(fe8);
                var list = new List<Address>();
                var ldrmap = RebuildProducerCore.BuildLdrMap(fe8);
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap);
                Assert.False(res.Cancelled);
                Assert.Contains(list, a => a.Info != null && a.Info.StartsWith("EventFunctionPointer"));
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void AppendAllAsmStructPointers_IsNeverComplete_DeferredFormsReported()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var list = new List<Address>();
                var ldrmap = RebuildProducerCore.BuildLdrMap(fe8);
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap);
                Assert.False(res.IsComplete);
                Assert.Contains("PatchForm(MakePatchStructDataList)", res.NotYetPorted);
                Assert.Contains("ProcsScriptForm", res.NotYetPorted);
                Assert.Contains("GraphicsToolForm", res.NotYetPorted);
                // OAMSPForm is PORTED in slice 2w — it is NOT re-reported as deferred.
                Assert.DoesNotContain("OAMSPForm", res.NotYetPorted);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void AppendAllAsmStructPointers_PreCancelled_ReturnsCancelled()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var cts = new CancellationTokenSource();
                cts.Cancel();
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, new List<Address>(),
                    ldrmap: RebuildProducerCore.BuildLdrMap(fe8), ct: cts.Token);
                Assert.True(res.Cancelled);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void AppendAllAsmStructPointers_NotCoreStateRom_Throws()
        {
            var saved = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var other = MakeVersionedRom("BE8E01");
                Assert.Throws<ArgumentException>(() =>
                    RebuildProducerCore.AppendAllAsmStructPointers(other, new List<Address>(), null));
            }
            finally { CoreState.ROM = saved; }
        }

        // ====================================================================
        // Coverage honesty
        // ====================================================================

        [Fact]
        public void GetAsmNotYetPortedForms_ListsDeferredForms_NoDuplicates()
        {
            string[] notYet = RebuildProducerCore.GetAsmNotYetPortedForms();
            Assert.Contains("PatchForm(MakePatchStructDataList)", notYet);
            Assert.Contains("ProcsScriptForm", notYet);
            Assert.Contains("GraphicsToolForm", notYet);
            // The PORTED forms are NOT in the static deferred list.
            Assert.DoesNotContain("OAMSPForm", notYet); // PORTED in slice 2w.
            Assert.DoesNotContain("EventScript(MakeEventASMMAPList)", notYet);
            Assert.DoesNotContain("EventFunctionPointerForm", notYet);
            Assert.DoesNotContain("Command85PointerForm", notYet);
            Assert.DoesNotContain("MapMiniMapTerrainImageForm", notYet);

            string[] raw = RebuildProducerCore.GetAsmNotYetPortedFormsRaw();
            var dups = raw.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.Empty(dups);
        }

        [Fact]
        public void AppendAllAsmStructPointers_DisasmUnwired_ReReportsEventScript()
        {
            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                CoreState.EventScript = null; // disasm unwired
                var list = new List<Address>();
                var ldrmap = RebuildProducerCore.BuildLdrMap(fe8);
                var res = RebuildProducerCore.AppendAllAsmStructPointers(fe8, list, ldrmap);
                Assert.Contains("EventScript(MakeEventASMMAPList)", res.NotYetPorted);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
            }
        }

        // ---- EventScript env ----------------------------------------------

        static void WithEnv(EventScript es, Action<ROM> body)
        {
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                var rom = MakeVersionedRom("BE8E01", 0x1100000);
                CoreState.ROM = rom;
                CoreState.EventScript = es;
                CoreState.CommentCache = new HeadlessEtcCache();
                body(rom);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        // Build a Script whose Data is exactly the given 4-byte words (so MakeEventASMMAPList's
        // word-walk sees them as candidate pointers).
        static EventScript.Script MakeScriptWithWords(params uint[] words)
        {
            var data = new byte[words.Length * 4];
            for (int i = 0; i < words.Length; i++)
            {
                data[i * 4 + 0] = (byte)(words[i] & 0xFF);
                data[i * 4 + 1] = (byte)((words[i] >> 8) & 0xFF);
                data[i * 4 + 2] = (byte)((words[i] >> 16) & 0xFF);
                data[i * 4 + 3] = (byte)((words[i] >> 24) & 0xFF);
            }
            return new EventScript.Script { Data = data, Info = new string[] { "test" } };
        }
    }
}
