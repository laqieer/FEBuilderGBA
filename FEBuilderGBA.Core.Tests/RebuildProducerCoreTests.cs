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
        public void WalkAndAdd_U32LessThan_StopsWhenU32GreaterOrEqual()
        {
            // StatusUnitsMenuForm rule: continue while u32(addr+0) < 0xFF.
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 16;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + block * 0, 0x00); // < 0xFF -> exists
            rom.write_u32(table + block * 1, 0x10); // < 0xFF -> exists
            rom.write_u32(table + block * 2, 0xFF); // == 0xFF -> stop

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "UnitsMenu",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U32LessThan,
                RuleOffset = 0,
                RuleStopValue = 0xFF,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 2, length = 16 * (2 + 1) = 48
            Assert.Equal(48u, list[0].Length);
        }

        [Fact]
        public void WalkAndAdd_SoundBossBGMRule_StopsAtFFFFTerminator()
        {
            // SoundBossBGMForm rule: stop at the u16(addr)==0xFFFF terminator.
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 8;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u16(table + block * 0, 0x0001);
            rom.write_u16(table + block * 1, 0x0002);
            rom.write_u16(table + block * 2, 0xFFFF); // terminator

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "BossBGM",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.SoundBossBGMRule,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 2, length = 8 * (2 + 1) = 24
            Assert.Equal(24u, list[0].Length);
        }

        [Fact]
        public void WalkAndAdd_SoundBossBGMRule_StopsAtTrailingEmptyRunAfter10Entries()
        {
            // The second guard: after the first 10 entries, stop once a run of 10 empty blocks
            // (BlockSize*10 zero bytes) is hit. Fill 11 non-empty entries, then leave the rest 0;
            // at i==11 the IsEmpty(addr, 80) guard fires (i > 10) -> count == 11.
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 8;
            rom.write_u32(pointer, Ptr(table));
            for (uint i = 0; i < 11; i++)
            {
                // any non-zero, non-0xFFFF first u16 keeps the entry "present" and non-empty
                rom.write_u16(table + block * i, 0x0100 + i);
            }
            // entries 11.. are all-zero -> IsEmpty(addr, 80) true at i==11 (> 10).

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "BossBGM",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.SoundBossBGMRule,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 11, length = 8 * (11 + 1) = 96
            Assert.Equal(96u, list[0].Length);
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

        [Fact]
        public void MakeAllStructPointers_RejectsRomThatIsNotCoreStateRom()
        {
            // The producer must scan the LOADED CoreState.ROM — Address length/offset validation
            // is bound to CoreState.ROM, so a different instance cannot be scanned correctly.
            var loaded = new ROM();
            loaded.SwapNewROMDataDirect(new byte[0x2000]);
            CoreState.ROM = loaded;

            var other = new ROM();
            other.SwapNewROMDataDirect(new byte[0x2000]);

            var ex = Assert.Throws<ArgumentException>(
                () => RebuildProducerCore.MakeAllStructPointers(other));
            Assert.Equal("rom", ex.ParamName);

            // The list-returning overload guards the same way.
            Assert.Throws<ArgumentException>(
                () => RebuildProducerCore.MakeAllStructPointersList(other));
        }

        [Fact]
        public void WalkAndAdd_BlockSizeZero_EmitsNothing_AndDoesNotHang()
        {
            // BlockSize 0 would make getBlockDataCount loop forever (addr += 0). EmitOne must
            // skip it (a zero block is a descriptor bug, not a hang).
            var rom = CreateTestRom();
            uint pointer = 0x0240;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "ZeroBlock",
                PointerField = _ => pointer,
                BlockSize = 0, // bug
                Rule = RebuildProducerCore.DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d); // must return (not hang)
            Assert.Empty(list);
        }

        [Fact]
        public void WalkAndAdd_UnhandledDataCountRule_ThrowsLoudly()
        {
            // An out-of-range DataCountRule is a programming error — MakeIsDataExists must throw,
            // not silently treat the table as 0 entries.
            var rom = CreateTestRom();
            uint pointer = 0x0240;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(table + 0, 0x01);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "BadRule",
                PointerField = _ => pointer,
                BlockSize = 1,
                Rule = (RebuildProducerCore.DataCountRule)999, // invalid
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RebuildProducerCore.WalkAndAdd(rom, list, d));
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
            // Item/Class have embedded per-entry sub-pointer blocks (StatBooster/effectiveness,
            // MoveCost) the descriptor walker cannot emit yet -> must be DEFERRED, not in the batch.
            Assert.Contains("ItemForm", notYet);
            Assert.Contains("ClassForm", notYet);
        }

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2bCoveredForms_KeepsDeferredSiblings()
        {
            // Slice 2b ports these four single-table forms -> they must be removed from the
            // not-yet-ported tracker (so IsComplete coverage stays accurate). This check is
            // ROM-independent so it always runs (the FE8U batch test is env-gated).
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
            Assert.DoesNotContain("StatusUnitsMenuForm", notYet);
            Assert.DoesNotContain("LinkArenaDenyUnitForm", notYet);
            Assert.DoesNotContain("MonsterItemForm", notYet);
            Assert.DoesNotContain("MonsterProbabilityForm", notYet);

            // Their deferred siblings (embedded sub-walk / event-scan / dynamic count / patch
            // pointer set) must STAY tracked — porting only some forms while leaving these
            // un-tracked would dangle their pointers during a rebuild.
            Assert.Contains("MonsterWMapProbabilityForm", notYet); // EventScriptForm.ScanScript
            Assert.Contains("CCBranchForm", notYet);               // count == ClassForm.DataCount()
            Assert.Contains("MapTileAnimation1Form", notYet);      // embedded IMG sub-block
            Assert.Contains("MapTileAnimation2Form", notYet);      // embedded BIN sub-block
            Assert.Contains("MapTerrainFloorLookupTableForm", notYet); // PatchUtil GetPointers()
            Assert.Contains("MapTerrainBGLookupTableForm", notYet);    // PatchUtil GetPointers()
        }

        [Fact]
        public void GetNotYetPortedForms_HasNoDuplicates()
        {
            // Duplicates would inflate the count and make the IsComplete gate
            // ("empty == safe to wire into a real defragment") unreliable. Assert BOTH the public
            // (dedup'd) view AND the raw literal are duplicate-free.
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
            Assert.Equal(notYet.Length, notYet.Distinct().Count());

            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            var dups = raw.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.True(dups.Length == 0,
                "NotYetPorted raw list has duplicate entries: " + string.Join(", ", dups));
        }

        [Fact]
        public void MakeAllStructPointers_Cancelled_ReportsIncompleteAndCancelled()
        {
            var rom = CreateTestRom();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            RebuildProducerCore.ProducerResult result = RebuildProducerCore.MakeAllStructPointers(rom, null, cts.Token);
            // Pre-cancel returns before the descriptor walk (no RomInfo needed) and still
            // surfaces the coverage state.
            Assert.True(result.Cancelled);
            Assert.False(result.IsComplete);   // NotYetPorted is non-empty regardless of cancel
            Assert.NotEmpty(result.NotYetPorted);
            Assert.Empty(result.List);
        }

        [Fact]
        public void MakeAllStructPointers_FE8U_ReportsIncomplete_WhileFormsRemain()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip when no ROM available (env-only)

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;

                RebuildProducerCore.ProducerResult result = RebuildProducerCore.MakeAllStructPointers(rom);
                // COMPLETENESS-SAFETY: while any form is un-ported the result must report incomplete
                // so a future wiring slice refuses to feed a partial list to a real defragment.
                Assert.False(result.IsComplete);
                Assert.NotEmpty(result.NotYetPorted);
                Assert.False(result.Cancelled);
                Assert.NotEmpty(result.List);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // ---- real-FE8U parity: the batch finds the known tables -------------

        [Fact]
        public void MakeAllStructPointersList_FE8U_FindsBatchTables_AndDefersItemClass()
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

                // Pass a progress collector through and assert reporting happens (does not throw).
                var progressLines = new List<string>();
                var progress = new Progress<string>(s => { lock (progressLines) progressLines.Add(s); });
                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom, progress);
                Assert.NotEmpty(list);

                // SupportAttribute table (a clean single-walk batch form) must be present
                // with the correct block size and a terminator-bounded count.
                uint saBase = rom.p32(rom.RomInfo.support_attribute_pointer);
                Address sa = list.FirstOrDefault(a => a.Addr == saBase && a.Info == "SupportAttribute");
                Assert.NotNull(sa);
                Assert.Equal(8u, sa.BlockSize);
                uint saCount = sa.Length / sa.BlockSize - 1; // length = block*(count+1)
                Assert.True(saCount >= 1, "SupportAttribute count should be positive");

                // AI3 (fixed-8) table must be present with the expected 9-block length.
                uint ai3Base = rom.p32(rom.RomInfo.ai3_pointer);
                Address ai3 = list.FirstOrDefault(a => a.Addr == ai3Base && a.Info == "AI3");
                Assert.NotNull(ai3);
                Assert.Equal(20u, ai3.BlockSize);
                Assert.Equal(20u * (8 + 1), ai3.Length); // fixed 8 -> length = 20*(8+1)

                // ArenaClass emits THREE separate tables with DISTINCT WF Info strings (faithfulness
                // fix — not one collapsed multi-pointer entry). Each base must be present by its
                // own RomInfo pointer with the matching Info label.
                uint arNear = rom.p32(rom.RomInfo.arena_class_near_weapon_pointer);
                Assert.Contains(list, a => a.Addr == arNear && a.Info == "AreaClassForm near weapon");
                uint arFar = rom.p32(rom.RomInfo.arena_class_far_weapon_pointer);
                Assert.Contains(list, a => a.Addr == arFar && a.Info == "AreaClassForm far weapon");
                uint arMagic = rom.p32(rom.RomInfo.arena_class_magic_weapon_pointer);
                Assert.Contains(list, a => a.Addr == arMagic && a.Info == "AreaClassForm magic weapon");

                // ---- slice 2b: the new batch tables must be present with the WF block sizes
                //      and Info strings ----

                // SoundBossBGMForm -> "BossBGM" (block 8, called unconditionally in WF).
                uint bossBgmBase = rom.p32(rom.RomInfo.sound_boss_bgm_pointer);
                Address bossBgm = list.FirstOrDefault(a => a.Addr == bossBgmBase && a.Info == "BossBGM");
                Assert.NotNull(bossBgm);
                Assert.Equal(8u, bossBgm.BlockSize);

                // StatusUnitsMenuForm -> "UnitsMenu" (block 16, u32<0xFF, FE8-only).
                uint unitsMenuBase = rom.p32(rom.RomInfo.status_units_menu_pointer);
                Address unitsMenu = list.FirstOrDefault(a => a.Addr == unitsMenuBase && a.Info == "UnitsMenu");
                Assert.NotNull(unitsMenu);
                Assert.Equal(16u, unitsMenu.BlockSize);

                // LinkArenaDenyUnitForm -> "LinkAreaDenyUnitForm" (WF typo, block 2, FE8-only).
                uint linkDenyBase = rom.p32(rom.RomInfo.link_arena_deny_unit_pointer);
                Address linkDeny = list.FirstOrDefault(a => a.Addr == linkDenyBase && a.Info == "LinkAreaDenyUnitForm");
                Assert.NotNull(linkDeny);
                Assert.Equal(2u, linkDeny.BlockSize);

                // MonsterItemForm -> three distinct tables with WF Info strings and block sizes.
                uint monItemBase = rom.p32(rom.RomInfo.monster_item_item_pointer);
                Address monItem = list.FirstOrDefault(a => a.Addr == monItemBase && a.Info == "MonsterItemForm");
                Assert.NotNull(monItem);
                Assert.Equal(5u, monItem.BlockSize);
                uint monItemProbBase = rom.p32(rom.RomInfo.monster_item_probability_pointer);
                Assert.Contains(list, a => a.Addr == monItemProbBase && a.Info == "MonsterItemFormProbability" && a.BlockSize == 5);
                uint monItemTableBase = rom.p32(rom.RomInfo.monster_item_table_pointer);
                Assert.Contains(list, a => a.Addr == monItemTableBase && a.Info == "MonsterItemFormTable" && a.BlockSize == 32);

                // MonsterProbabilityForm -> "MonsterProbabilityForm" (block 12).
                uint monProbBase = rom.p32(rom.RomInfo.monster_probability_pointer);
                Assert.Contains(list, a => a.Addr == monProbBase && a.Info == "MonsterProbabilityForm" && a.BlockSize == 12);

                // The newly-covered forms must no longer be reported as NotYetPorted.
                string[] notYet2b = RebuildProducerCore.GetNotYetPortedForms();
                Assert.DoesNotContain("StatusUnitsMenuForm", notYet2b);
                Assert.DoesNotContain("LinkArenaDenyUnitForm", notYet2b);
                Assert.DoesNotContain("MonsterItemForm", notYet2b);
                Assert.DoesNotContain("MonsterProbabilityForm", notYet2b);
                // Deferred sibling forms MUST remain tracked (event-scan / dynamic count / patch).
                Assert.Contains("MonsterWMapProbabilityForm", notYet2b);
                Assert.Contains("CCBranchForm", notYet2b);
                Assert.Contains("MapTileAnimation1Form", notYet2b);
                Assert.Contains("MapTerrainFloorLookupTableForm", notYet2b);

                // FAITHFULNESS / COMPLETENESS-SAFETY: Item and Class are NOT emitted by this
                // slice (embedded sub-pointer blocks). They must be absent from the list AND
                // tracked in NotYetPorted so a rebuild does not silently drop their sub-blocks.
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                Assert.DoesNotContain(list, a => a.Addr == itemBase && a.Info == "Item");
                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                Assert.DoesNotContain(list, a => a.Addr == classBase && a.Info == "Class");

                // The progress collector must have received reports (per-descriptor + summary).
                lock (progressLines)
                {
                    Assert.NotEmpty(progressLines);
                    Assert.Contains(progressLines, s => s.Contains("not-yet-ported"));
                }
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
