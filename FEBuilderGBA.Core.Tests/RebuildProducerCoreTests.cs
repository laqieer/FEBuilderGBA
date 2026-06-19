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
            // The CString/BinString sub-walks decode strings via ROM.getString, which needs a
            // SystemTextEncoder. A headless ASCII encoder is enough for the synthetic-ROM tests.
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
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
            // ItemForm stays DEFERRED: its StatBooster sub-block size depends on un-ported PatchUtil
            // patch detection. (ClassForm WAS deferred for its MoveCost sub-blocks but is ported in
            // slice 2c — see GetNotYetPortedForms_DropsSlice2cCoveredForms_KeepsDeferredSiblings.)
            Assert.Contains("ItemForm", notYet);
            Assert.DoesNotContain("ClassForm", notYet);
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
            // (CCBranchForm is now PORTED in the #1261 producer sweep via ClassDataCount — it is no
            //  longer a deferred sibling; EventBattleTalkForm stays deferred for its ScanScript.)
            Assert.Contains("EventBattleTalkForm", notYet);        // per-entry EventScriptForm.ScanScript
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
        public void MakeAllStructPointersList_FE8U_FindsBatchTables_AndDefersItem()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip when no ROM available (env-only)

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 8) return; // this assertion is FE8U-specific
                // The full producer walk now decodes strings (slice 2c StatusParam/MapTerrainName
                // sub-walks) via ROM.getString, which needs a SystemTextEncoder.
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

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
                // (CCBranchForm is now PORTED in the #1261 producer sweep — count = ClassDataCount —
                //  so it is no longer asserted-present here; EventBattleTalkForm stays deferred for its
                //  per-entry EventScriptForm.ScanScript expansion.)
                Assert.Contains("MonsterWMapProbabilityForm", notYet2b);
                Assert.Contains("EventBattleTalkForm", notYet2b);
                Assert.Contains("MapTileAnimation1Form", notYet2b);
                Assert.Contains("MapTerrainFloorLookupTableForm", notYet2b);

                // FAITHFULNESS / COMPLETENESS-SAFETY: ItemForm is NOT emitted (its StatBooster
                // sub-block size needs un-ported PatchUtil detection) — it must be absent from the
                // list AND tracked in NotYetPorted so a rebuild does not silently drop its
                // sub-blocks. ClassForm IS emitted as of slice 2c (covered by the dedicated
                // MakeAllStructPointersList_FE8U_FindsClassMoveCostSubBlocks_AndDefersItem test).
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                Assert.DoesNotContain(list, a => a.Addr == itemBase && a.Info == "Item");

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
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // ---- slice 2c: per-entry embedded sub-walks ------------------------

        // Synthetic versioned ROMs: LoadLow matches the GBA game-code substring to select the
        // ROMFE* subclass, which populates RomInfo (version / class_datasize). The version string
        // MUST be one LoadLow actually recognizes (FE6="AFEJ01", FE8U="BE8E01"), else RomInfo stays
        // null. (Distinct from ClassFE6LayoutTests, which constructs ROMFE6JP directly.)
        static ROM MakeVersionedRom(string versionString)
        {
            var rom = new ROM();
            var data = new byte[0x200_0000]; // 32MB — survives ROMFE ctor patch probing
            bool ok = rom.LoadLow("fake.gba", data, versionString);
            Assert.True(ok, "LoadLow did not recognize version string: " + versionString);
            return rom;
        }

        [Fact]
        public void BuildBatchDescriptors_FE6_UsesFE6ClassLayout_NotFE78()
        {
            // CORRECTNESS (FE6 version bug): on a FE6 ROM the producer MUST use the ClassFE6Form
            // layout (pointerIndexes {48,52,56,60,64}, 4x MoveCost @ {48,52,56,60} length 52, skip
            // class 0, terrain pointers length 52) — NOT the FE7/8 layout ({52..76}, 6x len 66).
            // Running the FE7/8 descriptor on FE6 would relocate the wrong bytes (corruption).
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01"); // FE6 (ROMFE6JP) version code LoadLow matches
                CoreState.ROM = fe6;
                Assert.NotNull(fe6.RomInfo);
                Assert.Equal(6, fe6.RomInfo.version);

                var descs = RebuildProducerCore.BuildBatchDescriptors(fe6);
                var cls = descs.Single(d => d.Name == "Class");

                Assert.Equal(new uint[] { 48, 52, 56, 60, 64 }, cls.PointerIndexes);
                Assert.Equal(1u, cls.SubWalkStartIndex); // FE6 skips class 0
                Assert.NotNull(cls.SubWalks);
                Assert.Equal(4, cls.SubWalks.Count);
                Assert.Equal(new uint[] { 48, 52, 56, 60 },
                    cls.SubWalks.Select(s => s.EmbeddedPointerOffset).ToArray());
                Assert.All(cls.SubWalks, s => Assert.Equal(52u, s.FixedLength));
                Assert.All(cls.SubWalks, s => Assert.Equal(RebuildProducerCore.SubKind.BinFixed, s.Kind));
                Assert.NotNull(cls.ExtraFixedPointers);
                Assert.Equal(3, cls.ExtraFixedPointers.Length);
                Assert.All(cls.ExtraFixedPointers, e => Assert.Equal(52u, e.FixedLength));
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void BuildBatchDescriptors_FE8_UsesFE78ClassLayout()
        {
            // Conversely, a FE8 ROM must get the FE7/8 Class layout ({52..76}, 6x len 66, start 0).
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01"); // FE8U (ROMFE8U) version code LoadLow matches
                CoreState.ROM = fe8;
                Assert.NotNull(fe8.RomInfo);
                Assert.Equal(8, fe8.RomInfo.version);

                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var cls = descs.Single(d => d.Name == "Class");

                Assert.Equal(new uint[] { 52, 56, 60, 64, 68, 72, 76 }, cls.PointerIndexes);
                Assert.Equal(0u, cls.SubWalkStartIndex);
                Assert.Equal(6, cls.SubWalks.Count);
                Assert.Equal(new uint[] { 56, 60, 64, 68, 72, 76 },
                    cls.SubWalks.Select(s => s.EmbeddedPointerOffset).ToArray());
                Assert.All(cls.SubWalks, s => Assert.Equal(66u, s.FixedLength));
                Assert.All(cls.ExtraFixedPointers, e => Assert.Equal(66u, e.FixedLength));
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void SubWalkStartIndex_SkipsLeadingEntries()
        {
            // FE6 Class skips class 0: with SubWalkStartIndex=1 the entry-0 embedded pointer is NOT
            // sub-walked even though it's a valid pointer; entry 1+ are.
            var rom = CreateTestRom(0x4000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 0x48; // > 60 not needed; FE6 offsets go up to 60, use a block that fits
            block = 0x40;
            rom.write_u32(pointer, Ptr(table));
            // 2 entries: index 0 (always) + index 1 (u8(+4)=1); index 2 stops.
            rom.write_u8(table + block * 1 + 4, 0x01);
            // both entries have a valid MoveCost pointer @ +48
            uint mc0 = 0x2000, mc1 = 0x2100;
            rom.write_u32(table + block * 0 + 48, Ptr(mc0));
            rom.write_u32(table + block * 1 + 48, Ptr(mc1));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Class",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { 48 },
                SubWalkStartIndex = 1, // skip class 0
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 48,
                        Kind = RebuildProducerCore.SubKind.BinFixed,
                        FixedLength = 52,
                        Name = (r, i) => "MoveCost Clear",
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR + exactly 1 MoveCost block (entry 1 only — entry 0 skipped).
            Assert.Equal(2, list.Count);
            Assert.Single(list, a => a.Addr == mc1 && a.Length == 52);
            Assert.DoesNotContain(list, a => a.Addr == mc0); // entry 0 NOT sub-walked
        }

        [Fact]
        public void EmitSubWalks_NoEncoder_SkipsStringKindsGracefully_NoNRE()
        {
            // REGRESSION: the string-decoding sub-walks (CString/BinString) read ROM.getString,
            // which needs CoreState.SystemTextEncoder. With no encoder the producer must SKIP them
            // (never NRE). BinFixed does not decode strings, so it still runs.
            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x4000]);
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = null; // the critical condition

                uint table = 0x1000, pointer = 0x0240, block = 16;
                rom.write_u32(pointer, Ptr(table));
                // entry 0 has a CString pointer @ +12 and a BinFixed pointer @ +0
                rom.write_u32(table + 12, Ptr(0x2000)); // PointerAt keeps entry 0
                rom.write_u8(0x2000, (byte)'A');
                rom.write_u8(0x2001, 0x00);
                rom.write_u32(table + 0, Ptr(0x2200)); // BinFixed target

                var d = new RebuildProducerCore.StructDescriptor
                {
                    Name = "Mixed",
                    PointerField = _ => pointer,
                    BlockSize = block,
                    Rule = RebuildProducerCore.DataCountRule.PointerAt,
                    RuleOffset = 12,
                    MaxCount = 0x100,
                    PointerIndexes = new uint[] { 0, 12 },
                    SubWalks = new List<RebuildProducerCore.SubWalk>
                    {
                        new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 12, Kind = RebuildProducerCore.SubKind.CString },
                        new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 0, Kind = RebuildProducerCore.SubKind.BinFixed, FixedLength = 40, Name = (r, i) => "Fixed" },
                    },
                };

                var list = new List<Address>();
                // must NOT throw NRE
                RebuildProducerCore.WalkAndAdd(rom, list, d);

                // main IFR + the BinFixed sub-block; the CString is skipped (no encoder).
                Assert.Contains(list, a => a.Addr == 0x2200 && a.Length == 40);
                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.CSTRING);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void SubWalk_BinFixed_EmitsFixedLengthBinBehindEachEntryPointer()
        {
            // ClassForm MoveCost: a 66-byte BIN block behind the embedded pointer at offset 56.
            var rom = CreateTestRom(0x4000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 0x54; // > 56 so the embedded pointer fits in the block
            rom.write_u32(pointer, Ptr(table));
            // entry 0 exists (index-0 always), entry 1 u8(+4)=1 exists, entry 2 u8(+4)=0 stops.
            rom.write_u8(table + block * 1 + 4, 0x01);
            // entry 0's MoveCost pointer @ +56 -> 0x2000 ; entry 1's -> 0x2100
            uint mc0 = 0x2000, mc1 = 0x2100;
            rom.write_u32(table + block * 0 + 56, Ptr(mc0));
            rom.write_u32(table + block * 1 + 56, Ptr(mc1));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Class",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { 56 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 56,
                        Kind = RebuildProducerCore.SubKind.BinFixed,
                        FixedLength = 66,
                        Name = (r, i) => "MoveCost Clear",
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR + 2 MoveCost sub-blocks
            Assert.Equal(3, list.Count);
            // main is first
            Assert.Equal(table, list[0].Addr);
            // both MoveCost blocks present at the right addr/length/pointer-slot/type
            Address b0 = list.First(a => a.Addr == mc0);
            Assert.Equal(66u, b0.Length);
            Assert.Equal(table + block * 0 + 56, b0.Pointer);
            Assert.Equal(Address.DataTypeEnum.BIN, b0.DataType);
            Address b1 = list.First(a => a.Addr == mc1);
            Assert.Equal(66u, b1.Length);
            Assert.Equal(table + block * 1 + 56, b1.Pointer);
        }

        [Fact]
        public void SubWalk_BinFixed_SkipsEntryWhenEmbeddedPointerUnsafe()
        {
            // If an entry's embedded pointer is not a safe offset, no sub-block is emitted
            // (WF: `if (U.isSafetyOffset(pointer))`).
            var rom = CreateTestRom(0x4000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 0x54;
            rom.write_u32(pointer, Ptr(table));
            // single entry (index 0); embedded pointer @ +56 left 0 (unsafe) -> no sub-block.

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Class",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { 56 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 56,
                        Kind = RebuildProducerCore.SubKind.BinFixed,
                        FixedLength = 66,
                        Name = (r, i) => "MoveCost Clear",
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // only the main IFR Address — the 0 (unsafe) embedded pointer is skipped.
            Assert.Single(list);
            Assert.Equal(table, list[0].Addr);
        }

        [Fact]
        public void ExtraFixedPointers_EmittedOncePerDescriptor()
        {
            // ClassForm's three 全クラス共通 terrain pointers are emitted ONCE (not per entry).
            var rom = CreateTestRom(0x4000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 0x10;
            rom.write_u32(pointer, Ptr(table));
            // 2 entries (index 0 + u8(+4)=1 at entry 1)
            rom.write_u8(table + block * 1 + 4, 0x01);

            uint tr0 = 0x0250, tr1 = 0x0254;
            uint tBlock0 = 0x2200, tBlock1 = 0x2300;
            rom.write_u32(tr0, Ptr(tBlock0));
            rom.write_u32(tr1, Ptr(tBlock1));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Class",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { },
                ExtraFixedPointers = new[]
                {
                    new RebuildProducerCore.ExtraFixedPointer { PointerField = _ => tr0, FixedLength = 66, Name = "MoveCost ref" },
                    new RebuildProducerCore.ExtraFixedPointer { PointerField = _ => tr1, FixedLength = 66, Name = "MoveCost recovery bad status" },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR + exactly 2 extra pointers (once, regardless of the 2 entries).
            Assert.Equal(3, list.Count);
            Assert.Single(list, a => a.Addr == tBlock0 && a.Length == 66 && a.DataType == Address.DataTypeEnum.BIN);
            Assert.Single(list, a => a.Addr == tBlock1 && a.Length == 66 && a.DataType == Address.DataTypeEnum.BIN);
        }

        [Fact]
        public void SubWalk_CString_EmitsNulTerminatedStringBehindEmbeddedPointer()
        {
            // StatusParamForm: a CString (len+1, CSTRING) behind the embedded pointer at offset 12.
            var rom = CreateTestRom(0x4000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 16;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: embedded pointer @ +12 must be a real pointer (PointerAt rule) -> 0x2000
            // entry 1: embedded pointer @ +12 == 0 (NULL) -> PointerAt stops (count == 1)
            uint str0 = 0x2000;
            rom.write_u32(table + block * 0 + 12, Ptr(str0));
            // plant the C string "AB\0" at str0
            rom.write_u8(str0 + 0, (byte)'A');
            rom.write_u8(str0 + 1, (byte)'B');
            rom.write_u8(str0 + 2, 0x00);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "StatusParam0",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.PointerAt,
                RuleOffset = 12,
                MaxCount = 0x10000,
                PointerIndexes = new uint[] { 0, 4, 12 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 12,
                        Kind = RebuildProducerCore.SubKind.CString,
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR (dataCount == 1) + 1 CString sub-block
            Assert.Equal(2, list.Count);
            Address main = list[0];
            Assert.Equal(table, main.Addr);
            Assert.Equal(16u * (1 + 1), main.Length); // count 1 -> length block*(1+1)
            Address cstr = list.First(a => a.DataType == Address.DataTypeEnum.CSTRING);
            Assert.Equal(str0, cstr.Addr);
            Assert.Equal(3u, cstr.Length); // "AB" + NUL == strlen(2) + 1
            Assert.Equal(table + 0 * block + 12, cstr.Pointer);
        }

        [Fact]
        public void SubWalk_BinString_EmitsStringLengthBinNoPlusOne()
        {
            // MapTerrainNameForm: a string-derived BIN block of length == strlen (NO +1) behind the
            // embedded pointer at offset 0.
            var rom = CreateTestRom(0x4000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 4;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: embedded pointer @ +0 -> 0x2000 (a real pointer, PointerOrNullAt continues)
            // entry 1: embedded pointer @ +0 == not-a-pointer -> stop.
            uint str0 = 0x2000;
            rom.write_u32(table + block * 0 + 0, Ptr(str0));
            rom.write_u32(table + block * 1 + 0, 0x12345678); // not a pointer-or-null -> stop
            rom.write_u8(str0 + 0, (byte)'X');
            rom.write_u8(str0 + 1, (byte)'Y');
            rom.write_u8(str0 + 2, (byte)'Z');
            rom.write_u8(str0 + 3, 0x00);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Terrain",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.PointerOrNullAt,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 0 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 0,
                        Kind = RebuildProducerCore.SubKind.BinString,
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR (dataCount == 1) + 1 string-BIN sub-block
            Assert.Equal(2, list.Count);
            Address bin = list.First(a => a.DataType == Address.DataTypeEnum.BIN);
            Assert.Equal(str0, bin.Addr);
            Assert.Equal(3u, bin.Length); // "XYZ" == strlen 3, NO +1 (distinct from CString)
            Assert.Equal(table + 0 * block + 0, bin.Pointer);
        }

        [Fact]
        public void DataCountRule_PointerAt_NullSlotTerminates_OrNullDoesNot()
        {
            // PointerAt stops at a NULL slot; PointerOrNullAt continues through it. Two synthetic
            // tables prove the difference (StatusParam vs MapTerrain semantics).
            var rom = CreateTestRom(0x4000);
            uint pointer = 0x0240;
            uint table = 0x1000;
            uint block = 16;
            rom.write_u32(pointer, Ptr(table));
            // entry 0 slot @ +12 = real ptr ; entry 1 slot @ +12 = NULL(0) ; entry 2 = real ptr ;
            // entry 3 slot @ +12 = garbage (neither pointer nor NULL) -> the hard stop for BOTH rules.
            rom.write_u32(table + block * 0 + 12, Ptr(0x2000));
            rom.write_u32(table + block * 1 + 12, 0x00000000);   // NULL
            rom.write_u32(table + block * 2 + 12, Ptr(0x2100));
            rom.write_u32(table + block * 3 + 12, 0x12345678);   // garbage (not pointer-or-null)

            var pAt = new RebuildProducerCore.StructDescriptor
            {
                Name = "PAt", PointerField = _ => pointer, BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.PointerAt, RuleOffset = 12,
                MaxCount = 0x100, PointerIndexes = new uint[] { },
            };
            var listAt = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, listAt, pAt);
            // PointerAt stops at entry 1 (NULL terminates) -> dataCount 1 -> length block*(1+1)
            Assert.Equal(block * (1 + 1), listAt[0].Length);

            var pOrNull = new RebuildProducerCore.StructDescriptor
            {
                Name = "POrNull", PointerField = _ => pointer, BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.PointerOrNullAt, RuleOffset = 12,
                MaxCount = 0x100, PointerIndexes = new uint[] { },
            };
            var listOrNull = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, listOrNull, pOrNull);
            // PointerOrNullAt passes THROUGH the NULL at entry 1; it stops only at the entry-3
            // garbage -> dataCount 3 -> length block*(3+1).
            Assert.Equal(block * (3 + 1), listOrNull[0].Length);
        }

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2cCoveredForms_KeepsDeferredSiblings()
        {
            // Slice 2c ports ClassForm + StatusParamForm + MapTerrainNameForm -> they must be
            // removed from the not-yet-ported tracker. ROM-independent so it always runs.
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
            Assert.DoesNotContain("ClassForm", notYet);
            Assert.DoesNotContain("StatusParamForm", notYet);
            Assert.DoesNotContain("MapTerrainNameForm", notYet);

            // The deferred siblings (un-ported patch detection / PROCS disasm / config-file driven)
            // MUST stay tracked — porting only some embedded forms while leaving these un-tracked
            // would dangle their sub-block pointers during a rebuild. (MenuDefinitionForm and
            // StatusRMenuForm were the recursive-tree siblings here; slice 2d ports them via dedicated
            // recursive walkers, so they are now covered — see GetNotYetPortedForms_DropsSlice2dCoveredForms.)
            Assert.Contains("ItemForm", notYet);          // StatBooster size needs PatchUtil detection
            Assert.Contains("ItemWeaponEffectForm", notYet); // PROCS length needs ProcsScript disasm
            Assert.Contains("OtherTextForm", notYet);        // config-file (U.ConfigDataFilename) driven
        }

        [Fact]
        public void MakeAllStructPointersList_FE8U_FindsClassMoveCostSubBlocks_AndDefersItem()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip when no ROM available (env-only)

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 8) return; // FE8U-specific
                // Slice 2c forms decode strings (StatusParam CString, MapTerrainName string-BIN) via
                // ROM.getString, which needs a SystemTextEncoder. A headless encoder is sufficient.
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // ClassForm main IFR must now be present (it was DEFERRED before slice 2c).
                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                Address main = list.FirstOrDefault(a => a.Addr == classBase && a.Info == "Class");
                Assert.NotNull(main);
                Assert.Equal(rom.RomInfo.class_datasize, main.BlockSize);
                Assert.Equal(new uint[] { 52, 56, 60, 64, 68, 72, 76 }, main.PointerIndexes);

                // At least one MoveCost sub-block (66-byte BIN) must be emitted behind an embedded
                // class pointer @ off 56..76. (Vanilla FE8U classes share MoveCost tables.)
                int moveCostBlocks = list.Count(a => a.DataType == Address.DataTypeEnum.BIN
                    && a.Length == 66
                    && a.Info != null && a.Info.StartsWith("MoveCost"));
                Assert.True(moveCostBlocks > 0, "expected at least one 66-byte MoveCost BIN sub-block");

                // The three 全クラス共通 terrain pointers must be present (once each).
                uint trBase = rom.p32(rom.RomInfo.terrain_recovery_pointer);
                if (U.isSafetyOffset(trBase))
                {
                    Assert.Contains(list, a => a.Addr == trBase && a.Length == 66
                        && a.Info == "MoveCost ref" && a.DataType == Address.DataTypeEnum.BIN);
                }

                // ClassForm must no longer be tracked as not-yet-ported; ItemForm STAYS deferred
                // and its main IFR must be ABSENT from the list (StatBooster size unportable).
                string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
                Assert.DoesNotContain("ClassForm", notYet);
                Assert.Contains("ItemForm", notYet);
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                Assert.DoesNotContain(list, a => a.Addr == itemBase && a.Info == "Item");
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        static string FindTestRom() => FindTestRomNamed("FE8U.gba");

        static string FindTestRomNamed(string romFile)
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
                        string path = System.IO.Path.Combine(romsDir, romFile);
                        if (System.IO.File.Exists(path)) return path;
                    }
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void MakeAllStructPointersList_FE6_UsesFE6ClassLayout_Len52SubBlocks()
        {
            // CORRECTNESS (FE6 version bug) end-to-end on a real FE6 ROM: the Class descriptor must
            // emit 52-byte MoveCost BIN sub-blocks (NOT 66) and the FE6 pointerIndexes, proving the
            // version gate picks ClassFE6Form's layout. Env-gated on roms/FE6.gba.
            string romPath = FindTestRomNamed("FE6.gba");
            if (romPath == null) return; // skip when no FE6 ROM available (env-only)

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 6) return; // FE6-specific
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // Class main IFR present with FE6 pointerIndexes {48,52,56,60,64}.
                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                Address main = list.FirstOrDefault(a => a.Addr == classBase && a.Info == "Class");
                Assert.NotNull(main);
                Assert.Equal(new uint[] { 48, 52, 56, 60, 64 }, main.PointerIndexes);

                // MoveCost sub-blocks must be 52-byte (the FE6 length) — NEVER 66 (the FE7/8 length).
                int len52 = list.Count(a => a.DataType == Address.DataTypeEnum.BIN
                    && a.Length == 52 && a.Info != null && a.Info.StartsWith("MoveCost"));
                Assert.True(len52 > 0, "expected at least one 52-byte FE6 MoveCost BIN sub-block");
                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.BIN
                    && a.Length == 66 && a.Info != null && a.Info.StartsWith("MoveCost"));
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // ==== producer sweep (#1261): new DataCountRules ====================

        [Fact]
        public void TerminatorWithEmptyGuard_U16_StopsAtTerminator()
        {
            var rom = CreateTestRom();
            // block 16, u16(addr)==0xFFFF terminator (SupportTalk FE8 shape), 2 valid entries.
            uint table = 0x1000, pointer = 0x0240, block = 16;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u16(table + 0 * block, 0x1234);
            rom.write_u16(table + 1 * block, 0x5678);
            rom.write_u16(table + 2 * block, 0xFFFF); // terminator

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "SupportTalk",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.TerminatorWithEmptyGuard,
                RuleWidth = 2,
                RuleOffset = 0,
                RuleStopValue = 0xFFFF,
                HasEmptyGuard = true,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            // count 2 -> length = block*(2+1) = 48
            Assert.Equal(block * 3, list[0].Length);
        }

        [Fact]
        public void TerminatorWithEmptyGuard_TwoStopValues_EitherTerminates()
        {
            var rom = CreateTestRom();
            // EventBattleTalkFE6 shape: u16==0x0 OR 0xFFFF terminates. Here entry 1 is 0x0000.
            uint table = 0x1000, pointer = 0x0240, block = 12;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u16(table + 0 * block, 0x00AB); // valid (non-zero, non-FFFF)
            rom.write_u16(table + 1 * block, 0x0000); // terminator via stop value 0
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "EventBattleTalkFE6Form",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.TerminatorWithEmptyGuard,
                RuleWidth = 2,
                RuleOffset = 0,
                RuleStopValue = 0x0,
                RuleStopValue2 = 0xFFFF,
                HasEmptyGuard = true,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (1 + 1), list[0].Length); // 1 valid entry
        }

        [Fact]
        public void TerminatorWithEmptyGuard_U32_NoGuard()
        {
            var rom = CreateTestRom();
            // SoundRoomCG shape: u32==0xFFFFFFFF terminator, no empty-guard.
            uint table = 0x1000, pointer = 0x0240, block = 4;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0 * block, 0x08001000);
            rom.write_u32(table + 1 * block, 0x08002000);
            rom.write_u32(table + 2 * block, 0x08003000);
            rom.write_u32(table + 3 * block, 0xFFFFFFFF); // terminator
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "SoundRoomCG",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.TerminatorWithEmptyGuard,
                RuleWidth = 4,
                RuleOffset = 0,
                RuleStopValue = 0xFFFFFFFF,
                HasEmptyGuard = false,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (3 + 1), list[0].Length);
        }

        [Fact]
        public void FixedCountU8Address_CountFromRomAddress()
        {
            var rom = CreateTestRom();
            uint table = 0x1000, pointer = 0x0240, countAddr = 0x0300;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(countAddr, 5); // count = 5
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "GameOptionOrder",
                PointerField = _ => pointer,
                BlockSize = 1,
                Rule = RebuildProducerCore.DataCountRule.FixedCountU8Address,
                CountAddressField = _ => countAddr,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(1u * (5 + 1), list[0].Length);
        }

        [Fact]
        public void SummonsDemonKingRule_CountIsMaxPlusOne_AndCapsAt100()
        {
            var rom = CreateTestRom();
            uint table = 0x1000, pointer = 0x0240, countAddr = 0x0300, block = 20;
            rom.write_u32(pointer, Ptr(table));

            // max = 3 -> i <= 3 -> 4 entries -> length block*(4+1).
            rom.write_u8(countAddr, 3);
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Summons",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.SummonsDemonKingRule,
                CountAddressField = _ => countAddr,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (4 + 1), list[0].Length);

            // max >= 100 -> 0 entries -> length block*(0+1).
            rom.write_u8(countAddr, 200);
            var list2 = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list2, d);
            Assert.Single(list2);
            Assert.Equal(block * (0 + 1), list2[0].Length);
        }

        [Fact]
        public void U32InRangeAt_OnlyCountsInclusiveRange()
        {
            var rom = CreateTestRom();
            // EventFinalSerifFE7 shape: u32(addr+0) in [1, 0xff].
            uint table = 0x1000, pointer = 0x0240, block = 8;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0 * block, 0x01);   // in range
            rom.write_u32(table + 1 * block, 0xFF);   // in range (upper bound)
            rom.write_u32(table + 2 * block, 0x100);  // out of range -> stop
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "EventFinalserif",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U32InRangeAt,
                RuleOffset = 0,
                RuleRangeLo = 0x1,
                RuleRangeHi = 0xff,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (2 + 1), list[0].Length);
        }

        [Fact]
        public void TripleU32PointerOrNull_AllThreeMustBePointerOrNull()
        {
            var rom = CreateTestRom();
            // WorldMapPoint shape: 12/16/20 each pointer-or-NULL. Entry 0 valid (all NULL),
            // entry 1 has +16 = junk (not pointer, not 0) -> stop.
            uint table = 0x1000, pointer = 0x0240, block = 32;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: 12/16/20 all zero (NULL accepted)
            // entry 1: +12 NULL, +16 = 0x12345678 junk (not pointer-or-null) -> terminates
            rom.write_u32(table + 1 * block + 16, 0x12345678);
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "WorldMapPoint",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.TripleU32PointerOrNullAt121620,
                PointerIndexes = new uint[] { 12, 16, 20 },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (1 + 1), list[0].Length); // 1 valid entry
        }

        [Fact]
        public void WorldMapBGMRule_StopsOnSentinelSongPairs()
        {
            var rom = CreateTestRom();
            uint table = 0x1000, pointer = 0x0240, block = 4;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: (5, 6) valid
            rom.write_u16(table + 0 * block + 0, 5);
            rom.write_u16(table + 0 * block + 2, 6);
            // entry 1: (1, 0) -> stop
            rom.write_u16(table + 1 * block + 0, 1);
            rom.write_u16(table + 1 * block + 2, 0);
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "WorldMapBGM",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.WorldMapBGMRule,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (1 + 1), list[0].Length);
        }

        [Fact]
        public void DicMainRule_StopsWhenEitherTextIdZero()
        {
            var rom = CreateTestRom();
            uint table = 0x1000, pointer = 0x0240, block = 12;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: u16(+2)=10, u16(+4)=20 valid
            rom.write_u16(table + 0 * block + 2, 10);
            rom.write_u16(table + 0 * block + 4, 20);
            // entry 1: u16(+2)=0 -> stop
            rom.write_u16(table + 1 * block + 4, 99);
            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "dic_main",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.DicMainRule,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list);
            Assert.Equal(block * (1 + 1), list[0].Length);
        }

        // ==== producer sweep (#1261): version-gated descriptor presence ======

        [Fact]
        public void BuildBatchDescriptors_FE8_HasFE8OnlyForms_NotFE6OrFE7Forms()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var names = RebuildProducerCore.BuildBatchDescriptors(fe8).Select(d => d.Name).ToList();

                // FE8-only forms ported this sweep:
                Assert.Contains("EDForm_1", names);
                Assert.Contains("EDForm_3b", names);
                Assert.Contains("CCBranch", names);
                Assert.Contains("CCClassAlphaName", names);
                Assert.Contains("WorldMapPoint", names);
                Assert.Contains("WorldMapBGM", names);
                Assert.Contains("dic_main", names);
                Assert.Contains("dic_chaptor", names);
                Assert.Contains("dic_title", names);
                Assert.Contains("ForceSorite", names);
                Assert.Contains("Summon", names);
                Assert.Contains("Summons", names);
                Assert.Contains("UnitForm", names);
                Assert.Contains("SupportTalk", names);
                // StatusOptionOrder is shared v8+v7:
                Assert.Contains("GameOptionOrder", names);

                // FE6/FE7-only forms must be ABSENT on FE8 (version-divergence guard):
                Assert.DoesNotContain("EDFE6Form", names);
                Assert.DoesNotContain("EDFE7Form_1", names);
                Assert.DoesNotContain("SupportTalkFE6", names);
                Assert.DoesNotContain("SupportTalkFE7", names);
                Assert.DoesNotContain("TacticianAffinity", names);
                Assert.DoesNotContain("EventFinalserif", names);
                Assert.DoesNotContain("EDSensekiForm", names); // v6+v7 only
                Assert.DoesNotContain("SoundRoomFE6", names);
                Assert.DoesNotContain("OPClassAlphaName", names); // FE6 string-BIN variant
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void BuildBatchDescriptors_FE7_HasFE7OnlyForms_NotFE8OrFE6Forms()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe7 = MakeVersionedRom("AE7E01"); // FE7U
                CoreState.ROM = fe7;
                Assert.Equal(7, fe7.RomInfo.version);
                var names = RebuildProducerCore.BuildBatchDescriptors(fe7).Select(d => d.Name).ToList();

                // FE7-only forms ported this sweep:
                Assert.Contains("EDFE7Form_1", names);
                Assert.Contains("EDFE7Form_4", names);
                Assert.Contains("Unit", names);          // UnitFE7Form (flat, "Unit")
                Assert.Contains("SupportTalkFE7", names);
                Assert.Contains("SoundRoomCG", names);
                Assert.Contains("TacticianAffinity", names);
                Assert.Contains("EventFinalserif", names);
                Assert.Contains("EDSensekiForm", names); // shared v6+v7
                Assert.Contains("GameOptionOrder", names); // shared v8+v7

                // FE8/FE6-only forms must be ABSENT on FE7:
                Assert.DoesNotContain("EDForm_1", names);
                Assert.DoesNotContain("CCBranch", names);
                Assert.DoesNotContain("UnitForm", names); // FE8 support-sub-walk variant
                Assert.DoesNotContain("SupportTalk", names);
                Assert.DoesNotContain("SupportTalkFE6", names);
                Assert.DoesNotContain("EDFE6Form", names);
                Assert.DoesNotContain("SoundRoomFE6", names);

                // UnitFE7Form is flat (no support BinFixed sub-walk), unlike FE8 UnitForm.
                var unit = RebuildProducerCore.BuildBatchDescriptors(fe7).Single(d => d.Name == "Unit");
                Assert.Null(unit.SubWalks);
                Assert.Equal(new uint[] { 44 }, unit.PointerIndexes);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void BuildBatchDescriptors_FE6_HasFE6OnlyForms_NotFE8OrFE7Forms()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01");
                CoreState.ROM = fe6;
                var names = RebuildProducerCore.BuildBatchDescriptors(fe6).Select(d => d.Name).ToList();

                // FE6-only forms ported this sweep:
                Assert.Contains("EDFE6Form", names);
                Assert.Contains("EventBattleTalkFE6Form", names);
                Assert.Contains("EventBattleTalkFE6Form_2", names);
                Assert.Contains("Haiku", names);           // EventHaikuFE6Form
                Assert.Contains("SupportTalkFE6", names);
                Assert.Contains("SoundRoomFE6", names);
                Assert.Contains("OPClassAlphaName", names); // FE6 string-BIN variant
                Assert.Contains("EDSensekiForm", names);    // shared v6+v7

                // FE8/FE7-only forms must be ABSENT on FE6:
                Assert.DoesNotContain("EDForm_1", names);
                Assert.DoesNotContain("EDFE7Form_1", names);
                Assert.DoesNotContain("CCBranch", names);
                Assert.DoesNotContain("UnitForm", names);
                Assert.DoesNotContain("SupportTalk", names);
                Assert.DoesNotContain("SupportTalkFE7", names);
                Assert.DoesNotContain("TacticianAffinity", names);
                Assert.DoesNotContain("GameOptionOrder", names); // v8+v7 only, not v6

                // FE6 OPClassAlphaName carries a string-BIN sub-walk (block 4, @+0), distinct from
                // the FE8 "CCClassAlphaName" fixed-count table.
                var op = RebuildProducerCore.BuildBatchDescriptors(fe6).Single(d => d.Name == "OPClassAlphaName");
                Assert.Equal(4u, op.BlockSize);
                Assert.NotNull(op.SubWalks);
                Assert.Single(op.SubWalks);
                Assert.Equal(RebuildProducerCore.SubKind.BinString, op.SubWalks[0].Kind);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void ClassDataCount_MatchesClassTableWalk()
        {
            var rom = CreateTestRom(0x8000);
            // Build a class table: block 'block', U8NotZeroIndex0Always @+4, base via class_pointer.
            // We cannot set RomInfo on the bare ROM, so test ClassDataCount via a versioned ROM with a
            // synthetic class table at the RomInfo.class_pointer location.
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                uint block = fe8.RomInfo.class_datasize;
                Assert.True(block > 0);
                uint pointerLoc = U.toOffset(fe8.RomInfo.class_pointer);
                uint table = 0x100000;
                fe8.write_u32(pointerLoc, Ptr(table));
                // entry 0 always counts; entry1 u8(+4)=1; entry2 u8(+4)=2; entry3 u8(+4)=0 -> stop.
                fe8.write_u8(table + 1 * block + 4, 1);
                fe8.write_u8(table + 2 * block + 4, 2);
                uint count = RebuildProducerCore.ClassDataCount(fe8);
                Assert.Equal(3u, count); // entries 0,1,2
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // ==== producer sweep (#1261): NotYetPorted coverage bookkeeping =======

        [Fact]
        public void GetNotYetPortedForms_DropsSweepCoveredForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // Ported this sweep -> must be GONE:
            foreach (var gone in new[]
            {
                "EDForm", "CCBranchForm", "OPClassAlphaNameForm", "WorldMapPointForm",
                "WorldMapBGMForm", "TextDicForm", "EventForceSortieForm", "SummonUnitForm",
                "SummonsDemonKingForm", "UnitForm", "UnitFE7Form", "StatusOptionOrderForm",
                "SupportTalkForm", "SoundRoomCGForm", "TacticianAffinityFE7",
                "EventFinalSerifFE7Form", "EDSensekiCommentForm",
            })
            {
                Assert.DoesNotContain(gone, notYet);
            }

            // Still deferred (un-ported subsystem) -> must REMAIN:
            foreach (var kept in new[]
            {
                "UnitFE6Form", "EventBattleTalkForm", "EventHaikuForm", "SupportUnitForm",
                "WorldMapPathForm", "WorldMapEventPointerForm", "EDStaffRollForm", "OPPrologueForm",
                "MapSettingForm", "OPClassFontForm", "OPClassDemoForm", "FE8SpellMenuExtendsForm",
                "MonsterWMapProbabilityForm", "SoundRoomForm",
                // (StatusOptionForm + SoundFootStepsForm ported in slice 2d -> no longer kept here.)
                "ItemForm", "MapTileAnimation1Form", "MapTerrainFloorLookupTableForm",
            })
            {
                Assert.Contains(kept, notYet);
            }
        }

        [Fact]
        public void GetNotYetPortedForms_StillHasNoDuplicates_AfterSweep()
        {
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ==== producer sweep (#1261): real-ROM coverage =======================

        [Fact]
        public void MakeAllStructPointersList_FE8U_FindsSweepTables()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // env-only

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 8) return;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // CCBranch (count = ClassForm.DataCount) present with block 2.
                uint ccBase = rom.p32(rom.RomInfo.ccbranch_pointer);
                Assert.Contains(list, a => a.Addr == ccBase && a.Info == "CCBranch" && a.BlockSize == 2);

                // WorldMapPoint present, block 32, with the 12/16/20 pointer columns relocated.
                uint wmpBase = rom.p32(rom.RomInfo.worldmap_point_pointer);
                Address wmp = list.FirstOrDefault(a => a.Addr == wmpBase && a.Info == "WorldMapPoint");
                Assert.NotNull(wmp);
                Assert.Equal(32u, wmp.BlockSize);

                // SupportTalk (FE8 0xFFFF terminator) present, block 16.
                uint stBase = rom.p32(rom.RomInfo.support_talk_pointer);
                Assert.Contains(list, a => a.Addr == stBase && a.Info == "SupportTalk" && a.BlockSize == 16);

                // UnitForm present with block unit_datasize and the {44} support pointer column.
                uint unitBase = rom.p32(rom.RomInfo.unit_pointer);
                Address unit = list.FirstOrDefault(a => a.Addr == unitBase && a.Info == "UnitForm");
                Assert.NotNull(unit);
                Assert.Equal(rom.RomInfo.unit_datasize, unit.BlockSize);

                // TextDic three tables present.
                Assert.Contains(list, a => a.Info == "dic_main" && a.BlockSize == 12);
                Assert.Contains(list, a => a.Info == "dic_chaptor" && a.BlockSize == 4);
                Assert.Contains(list, a => a.Info == "dic_title" && a.BlockSize == 2);

                // Coverage bookkeeping reflects reality.
                string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
                Assert.DoesNotContain("CCBranchForm", notYet);
                Assert.DoesNotContain("WorldMapPointForm", notYet);
                Assert.Contains("EventBattleTalkForm", notYet); // still deferred (ScanScript)
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void MakeAllStructPointersList_FE6_FindsSweepTables()
        {
            string romPath = FindTestRomNamed("FE6.gba");
            if (romPath == null) return; // env-only

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 6) return;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // EDFE6Form (ed_3a, block 8, fixed 0x42) present.
                uint edBase = rom.p32(rom.RomInfo.ed_3a_pointer);
                Assert.Contains(list, a => a.Addr == edBase && a.Info == "EDFE6Form" && a.BlockSize == 8);

                // SupportTalkFE6 (block 16) present.
                uint stBase = rom.p32(rom.RomInfo.support_talk_pointer);
                Assert.Contains(list, a => a.Addr == stBase && a.Info == "SupportTalkFE6" && a.BlockSize == 16);

                // FE8/FE7-only tables must NOT have been emitted on FE6.
                Assert.DoesNotContain(list, a => a.Info == "CCBranch");
                Assert.DoesNotContain(list, a => a.Info == "SupportTalk"); // FE8 variant
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // ==================================================================
        // slice 2d: ASM-function SubKind + recursive walkers
        // ==================================================================

        // ---- SubKind.AsmFunction (StatusOptionForm per-entry ASM) -----------

        [Fact]
        public void WalkAndAdd_AsmFunctionSubWalk_EmitsAsmAddressAtResolvedTarget()
        {
            // A descriptor with a SubKind.AsmFunction sub-walk must, for each table entry, emit an
            // ASM Address whose Addr is ProgramAddrToPlain(u32(p + off)) — the thumb LSB cleared, the
            // GBA prefix stripped by the Address ctor. This is the StatusOptionForm @40 shape.
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            const uint block = 44;
            rom.write_u32(pointer, Ptr(table));

            // 2 entries: PointerAt @40 (U.isPointer terminates on NULL). Entry 0/1 have a valid ASM
            // pointer @40 (thumb bit set); entry 2's @40 is NULL -> stop.
            uint asm0 = 0x2000, asm1 = 0x2100;
            rom.write_u32(table + block * 0 + 40, Ptr(asm0) | 1); // thumb bit
            rom.write_u32(table + block * 1 + 40, Ptr(asm1) | 1);
            rom.write_u32(table + block * 2 + 40, 0); // NULL -> PointerAt stops

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "GameOption",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.PointerAt,
                RuleOffset = 40,
                PointerIndexes = new uint[] { 40 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 40,
                        Kind = RebuildProducerCore.SubKind.AsmFunction,
                        Name = (r, i) => "GameOption",
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR Address + 2 ASM functions.
            Address main = list.Single(a => a.Info == "GameOption" && a.BlockSize == block);
            Assert.Equal(table, main.Addr);
            Assert.Equal(2u * block, main.Length - block); // length = block*(count+1), count=2

            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(2, asms.Count);
            Assert.Contains(asms, a => a.Addr == asm0); // thumb bit cleared by ProgramAddrToPlain
            Assert.Contains(asms, a => a.Addr == asm1);
            Assert.All(asms, a => Assert.Equal(0u, a.Length)); // ASM length 0 (disasm at rebuild)
        }

        [Fact]
        public void BuildBatchDescriptors_FE8_EmitsStatusOptionWithAsmFunctionSubWalk()
        {
            // StatusOptionForm must be in the v8 batch with a PointerAt@40 main rule + an AsmFunction
            // sub-walk @40 (the WF per-entry Address.AddFunction(p+40)).
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);

                var go = descs.Single(d => d.Name == "GameOption" && d.BlockSize == 44);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, go.Rule);
                Assert.Equal(40u, go.RuleOffset);
                Assert.Equal(new uint[] { 40 }, go.PointerIndexes);
                Assert.NotNull(go.SubWalks);
                var sw = go.SubWalks.Single();
                Assert.Equal(40u, sw.EmbeddedPointerOffset);
                Assert.Equal(RebuildProducerCore.SubKind.AsmFunction, sw.Kind);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_FE6_OmitsStatusOption()
        {
            // StatusOptionForm is called ONLY in the WF v8 + v7 branches, never v6. The descriptor
            // must be gated accordingly (its data pointer is junk on FE6).
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01");
                CoreState.ROM = fe6;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe6);
                Assert.DoesNotContain(descs, d => d.Name == "GameOption" && d.BlockSize == 44);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitSoundFootStepsAt (Switch2-gated dedicated walker) ----------

        // Plant a valid Switch2 byte pattern at switch2Addr so IsSwitch2Enable returns true:
        //   u8(+1) (subOp) in [0x38,0x3D]; u8(+3) (cmpOp) in [0x28,0x2D]; u16(+2) != 0x9A00.
        // The count is read from u8(switch2Addr+2). entries = count + 1.
        static void PlantSwitch2(ROM rom, uint switch2Addr, byte count)
        {
            rom.write_u8(switch2Addr + 0, 0x00);
            rom.write_u8(switch2Addr + 1, 0x3A); // subOp in range
            rom.write_u8(switch2Addr + 2, count); // also the count byte
            rom.write_u8(switch2Addr + 3, 0x2A); // cmpOp in range (u16(+2)=0x2A?? != 0x9A00)
        }

        [Fact]
        public void EmitSoundFootStepsAt_Switch2Enabled_EmitsAsmFunctionPerEntry()
        {
            var rom = CreateTestRom(0x8000);
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400;       // RomInfo sound_foot_steps_pointer slot
            uint table = 0x1000;         // base = p32(pointer)
            PlantSwitch2(rom, switch2Addr, count: 2); // entries = 3
            rom.write_u32(pointer, Ptr(table));

            // 3 entries, block 4; each 4-byte slot is an ASM-routine pointer (thumb bit set).
            uint a0 = 0x2000, a1 = 0x2100, a2 = 0x2200;
            rom.write_u32(table + 0, Ptr(a0) | 1);
            rom.write_u32(table + 4, Ptr(a1) | 1);
            rom.write_u32(table + 8, Ptr(a2) | 1);

            var list = new List<Address>();
            RebuildProducerCore.EmitSoundFootStepsAt(rom, list, switch2Addr, pointer);

            // main IFR Address: type InputFormRef_ASM, length = 4*(3+1)=16, pointerIndexes {0}.
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Equal(table, main.Addr);
            // BasePointer is NOT_FOUND: WF's IFR BasePointer is 0 (Init's 3rd arg), so AddAddress
            // sets pointer = NOT_FOUND. The base is reached via the Switch2 LDR, not a RomInfo slot.
            Assert.Equal(U.NOT_FOUND, main.Pointer);
            Assert.Equal(4u, main.BlockSize);
            Assert.Equal(16u, main.Length);
            Assert.Equal(new uint[] { 0 }, main.PointerIndexes);

            // one ASM function per entry at the entry block address itself.
            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(3, asms.Count);
            Assert.Contains(asms, a => a.Addr == a0);
            Assert.Contains(asms, a => a.Addr == a1);
            Assert.Contains(asms, a => a.Addr == a2);
        }

        [Fact]
        public void EmitSoundFootStepsAt_Switch2Disabled_EmitsNothing()
        {
            // The WF ReInit returns NOT_FOUND (no emit) when IsSwitch2Enable is false. An all-zero
            // switch2 region (subOp/cmpOp out of range) is disabled.
            var rom = CreateTestRom();
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000) | 1);

            var list = new List<Address>();
            RebuildProducerCore.EmitSoundFootStepsAt(rom, list, switch2Addr, pointer);
            Assert.Empty(list);
        }

        // ---- EmitStatusRMenuRoots / EmitStatusRMenuSub (recursive MIX tree) -

        // Plant a 28-byte RMENU node at addr with child sub-pointers at 0/4/8/12 and ASM ptrs at
        // 20/24 (thumb-bit set). u16(+18) is the node's preview id (drives the name).
        static void PlantRMenuNode(ROM rom, uint addr, uint child0, uint child4, uint child8, uint child12,
            uint asm20, uint asm24, ushort previewId)
        {
            rom.write_u32(addr + 0, child0 == 0 ? 0u : Ptr(child0));
            rom.write_u32(addr + 4, child4 == 0 ? 0u : Ptr(child4));
            rom.write_u32(addr + 8, child8 == 0 ? 0u : Ptr(child8));
            rom.write_u32(addr + 12, child12 == 0 ? 0u : Ptr(child12));
            rom.write_u16(addr + 18, previewId);
            rom.write_u32(addr + 20, asm20 == 0 ? 0u : Ptr(asm20) | 1);
            rom.write_u32(addr + 24, asm24 == 0 ? 0u : Ptr(asm24) | 1);
        }

        [Fact]
        public void EmitStatusRMenuRoots_WalksTree_VisitsEachNodeOnce_EmitsAsmAt2024()
        {
            var rom = CreateTestRom(0x8000);
            uint rootPtr = 0x0300;   // RomInfo rmenu pointer slot
            uint nodeA = 0x1000;     // root node = p32(rootPtr)
            uint nodeB = 0x1100;     // child @ +0 of A
            uint nodeC = 0x1200;     // child @ +4 of A
            rom.write_u32(rootPtr, Ptr(nodeA));

            // A -> B (off0), C (off4); B and C are leaves. ASM ptrs at 20/24 on each.
            PlantRMenuNode(rom, nodeA, child0: nodeB, child4: nodeC, child8: 0, child12: 0,
                asm20: 0x3000, asm24: 0x3100, previewId: 0x00AA);
            PlantRMenuNode(rom, nodeB, 0, 0, 0, 0, asm20: 0x3200, asm24: 0x3300, previewId: 0x00BB);
            PlantRMenuNode(rom, nodeC, 0, 0, 0, 0, asm20: 0x3400, asm24: 0x3500, previewId: 0x00CC);

            var list = new List<Address>();
            RebuildProducerCore.EmitStatusRMenuRoots(rom, list, new uint[] { rootPtr });

            // 3 MIX nodes, each emitted exactly once at its address, block 28, pointerIndexes {0,4,8,12,20,24}.
            var mix = list.Where(a => a.DataType == Address.DataTypeEnum.MIX).ToList();
            Assert.Equal(3, mix.Count);
            foreach (uint n in new[] { nodeA, nodeB, nodeC })
            {
                Address node = mix.Single(a => a.Addr == n);
                Assert.Equal(28u, node.Length);
                Assert.Equal(28u, node.BlockSize);
                Assert.Equal(new uint[] { 0, 4, 8, 12, 20, 24 }, node.PointerIndexes);
            }

            // 2 ASM functions per node = 6 total.
            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(6, asms.Count);
            foreach (uint t in new[] { 0x3000u, 0x3100u, 0x3200u, 0x3300u, 0x3400u, 0x3500u })
            {
                Assert.Contains(asms, a => a.Addr == t);
            }
        }

        [Fact]
        public void EmitStatusRMenuRoots_CycleGuard_StopsSelfReferentialNode()
        {
            // A node whose +0 sub-pointer points back to ITSELF must be emitted once and NOT recursed
            // into again (the foundDic visited-set is the cycle-guard). Without it this infinite-loops.
            var rom = CreateTestRom(0x8000);
            uint rootPtr = 0x0300;
            uint node = 0x1000;
            rom.write_u32(rootPtr, Ptr(node));
            // +0 points to itself; +4/+8/+12 NULL.
            PlantRMenuNode(rom, node, child0: node, child4: 0, child8: 0, child12: 0,
                asm20: 0x3000, asm24: 0x3100, previewId: 0x0001);

            var list = new List<Address>();
            RebuildProducerCore.EmitStatusRMenuRoots(rom, list, new uint[] { rootPtr });

            // exactly one MIX node (self-cycle did not re-emit / infinite-loop).
            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.MIX);
            Assert.Equal(2, list.Count(a => a.DataType == Address.DataTypeEnum.ASM));
        }

        [Fact]
        public void EmitStatusRMenuRoots_SharedVisitedSet_DedupsNodeReachableFromTwoRoots()
        {
            // A node reachable from two distinct roots must be emitted ONCE (the foundDic is shared
            // across all roots — verbatim WF). Two roots both point at the same node.
            var rom = CreateTestRom(0x8000);
            uint root1 = 0x0300, root2 = 0x0304;
            uint shared = 0x1000;
            rom.write_u32(root1, Ptr(shared));
            rom.write_u32(root2, Ptr(shared));
            PlantRMenuNode(rom, shared, 0, 0, 0, 0, asm20: 0x3000, asm24: 0x3100, previewId: 0x0002);

            var list = new List<Address>();
            RebuildProducerCore.EmitStatusRMenuRoots(rom, list, new uint[] { root1, root2 });

            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.MIX && a.Addr == shared);
        }

        // ---- EmitMenuDefinitionPointers + EmitMenuCommandSubTable -----------

        [Fact]
        public void EmitMenuDefinitionPointers_WalksTable_RecursesIntoMenuCommandSubTable()
        {
            var rom = CreateTestRom(0x1_0000);
            uint defPtr = 0x0300;     // RomInfo menu_definiton pointer slot
            uint defTable = 0x1000;   // base = p32(defPtr), block 36
            rom.write_u32(defPtr, Ptr(defTable));

            // 1 MenuDef entry: IsDataExists = isPointer(u32(addr+8)); entry 0 has a valid +8 menu ptr,
            // entry 1's +8 is NULL -> table stops at count 1.
            uint menuTable = 0x2000;  // the MenuCommand sub-table behind +8
            rom.write_u32(defTable + 0 * 36 + 8, Ptr(menuTable));
            rom.write_u32(defTable + 1 * 36 + 8, 0); // stop

            // 6 ASM ptrs on the MenuDef entry @ {12,16,20,24,28,32}.
            uint dbase = 0x4000;
            for (uint k = 0; k < 6; k++)
            {
                rom.write_u32(defTable + 0 * 36 + 12 + k * 4, Ptr(dbase + k * 0x100) | 1);
            }

            // MenuCommand sub-table: block 36, IsDataExists = isPointer(u32(addr+0xc)); 1 entry.
            rom.write_u32(menuTable + 0 * 36 + 0xc, Ptr(0x5000) | 1); // entry 0 valid
            rom.write_u32(menuTable + 1 * 36 + 0xc, 0);               // stop
            // entry 0: a CString @ +0 + 6 ASM @ {12,16,20,24,28,32}.
            uint cstr = 0x6000;
            rom.write_u32(menuTable + 0, Ptr(cstr));
            rom.write_u8(cstr + 0, (byte)'A');
            rom.write_u8(cstr + 1, (byte)'B');
            rom.write_u8(cstr + 2, 0x00); // NUL
            uint mbase = 0x7000;
            for (uint k = 0; k < 6; k++)
            {
                rom.write_u32(menuTable + 0 * 36 + 12 + k * 4, Ptr(mbase + k * 0x100) | 1);
            }

            var list = new List<Address>();
            RebuildProducerCore.EmitMenuDefinitionPointers(rom, list, new uint[] { defPtr });

            // MenuDefinition main table: type InputFormRef_1, length = 36*1 (NO +1), pointerIndexes 7-wide.
            Address defMain = list.Single(a => a.Info == "MenuDefinition");
            Assert.Equal(defTable, defMain.Addr);
            Assert.Equal(Address.DataTypeEnum.InputFormRef_1, defMain.DataType);
            Assert.Equal(36u, defMain.Length); // block * DataCount (no +1)
            Assert.Equal(new uint[] { 8, 12, 16, 20, 24, 28, 32 }, defMain.PointerIndexes);

            // MenuCommand sub-table main: type InputFormRef_MIX, length = 36*(1+1) (the +1 form).
            Address menuMain = list.Single(a => a.Info == "MENU");
            Assert.Equal(menuTable, menuMain.Addr);
            Assert.Equal(Address.DataTypeEnum.InputFormRef_MIX, menuMain.DataType);
            Assert.Equal(72u, menuMain.Length);
            Assert.Equal(new uint[] { 0, 12, 16, 20, 24, 28, 32 }, menuMain.PointerIndexes);

            // 6 (MenuDef entry) + 6 (MenuCommand entry) = 12 ASM blocks.
            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(12, asms.Count);
            for (uint k = 0; k < 6; k++)
            {
                Assert.Contains(asms, a => a.Addr == dbase + k * 0x100);
                Assert.Contains(asms, a => a.Addr == mbase + k * 0x100);
            }

            // the menu-name CString behind +0 (length strlen+1 = 3, type CSTRING).
            Assert.Contains(list, a => a.Addr == cstr && a.DataType == Address.DataTypeEnum.CSTRING);
        }

        [Fact]
        public void EmitMenuCommandSubTable_NoEncoder_SkipsCStringButStillEmitsAsm()
        {
            // The +0 CString needs a SystemTextEncoder; without one it is gracefully skipped (no NRE)
            // while the 6 ASM blocks still emit (the wiring slice gates on IsComplete anyway).
            var savedEnc = CoreState.SystemTextEncoder;
            var rom = CreateTestRom(0x1_0000);
            CoreState.SystemTextEncoder = null; // force the no-encoder path
            try
            {
                uint pointer = 0x0300;
                uint menuTable = 0x2000;
                rom.write_u32(pointer, Ptr(menuTable));
                rom.write_u32(menuTable + 0xc, Ptr(0x5000) | 1); // 1 entry
                rom.write_u32(menuTable + 36 + 0xc, 0);          // stop
                rom.write_u32(menuTable + 0, Ptr(0x6000));       // a +0 pointer (CString target)
                uint mbase = 0x7000;
                for (uint k = 0; k < 6; k++)
                {
                    rom.write_u32(menuTable + 12 + k * 4, Ptr(mbase + k * 0x100) | 1);
                }

                var list = new List<Address>();
                RebuildProducerCore.EmitMenuCommandSubTable(rom, list, pointer, "MenuDef0_");

                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.CSTRING);
                Assert.Equal(6, list.Count(a => a.DataType == Address.DataTypeEnum.ASM));
                Assert.Contains(list, a => a.Info == "MENU"); // main table still emitted
            }
            finally { CoreState.SystemTextEncoder = savedEnc; }
        }

        [Fact]
        public void EmitMenuCommandSubTable_OutOfRangeAsmPointer_SkipsBlock_NoThrow()
        {
            // RELEASE-SAFETY (the #1261 no-divergence audit, team-lead point 2): the 6 ASM blocks call
            // Address.AddAddress(ProgramAddrToPlain(p32(off+p)), ...) with NO pre-check (verbatim WF).
            // A junk/out-of-range p32 must SILENTLY SKIP that block via AddAddress's early-return — no
            // throw (in Debug OR Release), matching WF. (The producer requires rom==CoreState.ROM, so
            // AddAddress's isSafetyOffset early-return and the Address ctor's Debug.Assert test the same
            // Data.Length; the ctor is never reached with an unsafe addr.)
            var rom = CreateTestRom(0x1_0000);
            uint pointer = 0x0300;
            uint menuTable = 0x2000;
            rom.write_u32(pointer, Ptr(menuTable));
            rom.write_u32(menuTable + 0xc, Ptr(0x5000) | 1); // 1 entry
            rom.write_u32(menuTable + 36 + 0xc, 0);          // stop
            rom.write_u32(menuTable + 0, Ptr(0x6000));       // CString target (valid)
            rom.write_u8(0x6000, 0x00);                      // empty string

            // 3 valid ASM ptrs (@12/16/20), 3 out-of-range (@24/28/32): junk that ProgramAddrToPlain
            // cannot rescue (0x00000000, 0xFFFFFFFF, and a > ROM-length offset).
            rom.write_u32(menuTable + 12, Ptr(0x7000) | 1);
            rom.write_u32(menuTable + 16, Ptr(0x7100) | 1);
            rom.write_u32(menuTable + 20, Ptr(0x7200) | 1);
            rom.write_u32(menuTable + 24, 0x00000000);       // NULL  -> skipped
            rom.write_u32(menuTable + 28, 0xFFFFFFFF);       // wild  -> skipped
            rom.write_u32(menuTable + 32, 0x00FF_FFFE);      // offset > 0x1_0000 ROM -> skipped

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMenuCommandSubTable(rom, list, pointer, "MenuDef0_"));
            Assert.Null(ex); // no throw

            // Exactly the 3 in-range ASM blocks emit; the 3 out-of-range ones are skipped.
            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(3, asms.Count);
            Assert.Contains(asms, a => a.Addr == 0x7000);
            Assert.Contains(asms, a => a.Addr == 0x7100);
            Assert.Contains(asms, a => a.Addr == 0x7200);
        }

        // ---- GetNotYetPortedForms coverage update --------------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2dCoveredForms()
        {
            var ported = RebuildProducerCore.GetNotYetPortedForms();
            Assert.DoesNotContain("StatusOptionForm", ported);
            Assert.DoesNotContain("SoundFootStepsForm", ported);
            Assert.DoesNotContain("StatusRMenuForm", ported);
            Assert.DoesNotContain("MenuDefinitionForm", ported);
            // sibling forms that genuinely still need un-ported subsystems STAY.
            Assert.Contains("ItemForm", ported);
            Assert.Contains("SoundRoomForm", ported);
            Assert.Contains("ItemWeaponEffectForm", ported);
        }

        // ---- real ROM: the new tables are found --------------------------

        [Fact]
        public void MakeAllStructPointersList_FE8U_FindsStatusRMenuMenuDefAndStatusOption()
        {
            string romPath = FindTestRomNamed("FE8U.gba");
            if (romPath == null) return; // env-only

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 8) return;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // StatusOptionForm (v8): main IFR at p32(status_game_option_pointer), block 44.
                uint goBase = rom.p32(rom.RomInfo.status_game_option_pointer);
                Assert.Contains(list, a => a.Addr == goBase && a.Info == "GameOption" && a.BlockSize == 44);

                // StatusRMenu: at least one 28-byte MIX node from a non-0 root.
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.MIX && a.BlockSize == 28
                    && a.Info.StartsWith("RMENU"));

                // MenuDefinition main table (InputFormRef_1) + a MenuCommand sub-table (InputFormRef_MIX).
                Assert.Contains(list, a => a.Info == "MenuDefinition"
                    && a.DataType == Address.DataTypeEnum.InputFormRef_1);
                Assert.Contains(list, a => a.Info == "MENU"
                    && a.DataType == Address.DataTypeEnum.InputFormRef_MIX);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void MakeAllStructPointersList_FE7_FindsStatusOptionAndMenuDef()
        {
            string romPath = FindTestRomNamed("FE7U.gba");
            if (romPath == null) return; // env-only

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 7) return;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // StatusOptionForm is called in the WF v7 branch too.
                uint goBase = rom.p32(rom.RomInfo.status_game_option_pointer);
                Assert.Contains(list, a => a.Addr == goBase && a.Info == "GameOption" && a.BlockSize == 44);

                // MenuDefinition is unconditional.
                Assert.Contains(list, a => a.Info == "MenuDefinition"
                    && a.DataType == Address.DataTypeEnum.InputFormRef_1);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void MakeAllStructPointersList_FE6_FindsMenuDefAndStatusRMenu_ButNoStatusOption()
        {
            string romPath = FindTestRomNamed("FE6.gba");
            if (romPath == null) return; // env-only

            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (rom.RomInfo.version != 6) return;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                List<Address> list = RebuildProducerCore.MakeAllStructPointersList(rom);
                Assert.NotEmpty(list);

                // MenuDefinition + StatusRMenu are unconditional (version-agnostic).
                Assert.Contains(list, a => a.Info == "MenuDefinition"
                    && a.DataType == Address.DataTypeEnum.InputFormRef_1);
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.MIX && a.BlockSize == 28
                    && a.Info.StartsWith("RMENU"));

                // StatusOptionForm is NOT called on FE6 -> no "GameOption" block-44 IFR.
                Assert.DoesNotContain(list, a => a.Info == "GameOption" && a.BlockSize == 44);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }
    }
}
