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
            // (MapTileAnimation1Form/MapTileAnimation2Form are now PORTED in slice 2g — see
            //  GetNotYetPortedForms_DropsSlice2gCoveredForms_KeepsDeferredSiblings.)
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
                // (MapTileAnimation1Form is now PORTED in slice 2g — its still-deferred map sibling
                //  MapTerrainFloorLookupTableForm [PatchUtil GetPointers] stays tracked here instead.)
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
                "EventBattleTalkForm", "EventHaikuForm", "SupportUnitForm",
                "WorldMapPathForm", "WorldMapEventPointerForm", "EDStaffRollForm", "OPPrologueForm",
                "MapSettingForm", "OPClassFontForm", "OPClassDemoForm", "FE8SpellMenuExtendsForm",
                "MonsterWMapProbabilityForm", "SoundRoomForm",
                // (StatusOptionForm + SoundFootStepsForm ported in slice 2d -> no longer kept here.
                //  UnitFE6Form + ItemUsagePointerForm + AIPerform*/AIMapSetting/Mant/ArenaEnemyWeapon
                //  ported in slice 2f -> no longer kept here. MapTileAnimation1Form/MapTileAnimation2Form +
                //  ItemShopForm + MapChangeForm + MapExitPointForm ported in slice 2g -> no longer kept.)
                "ItemForm", "MapTerrainFloorLookupTableForm",
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

        [Fact]
        public void EmitSoundFootStepsAt_CountRunsPastEof_TruncatesWithoutThrowing()
        {
            // Regression (PR #1276 review): a corrupted/too-large count near EOF must TRUNCATE like
            // WF InputFormRef.MakeList() (breaks on `addr + BlockSize > Data.Length`), NOT throw.
            // AddFunction reads u32(entryAddr) whose check_safety throws past EOF.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400;
            // Place the table so that exactly 2 of its 4-byte entries fit before EOF.
            //   i=0 -> 0x1FF8 (+4=0x1FFC <= 0x2000) fits; i=1 -> 0x1FFC (+4=0x2000) fits;
            //   i=2 -> 0x2000 (+4=0x2004 > 0x2000) -> WF MakeList breaks here.
            uint table = size - 8; // 0x1FF8
            PlantSwitch2(rom, switch2Addr, count: 5); // claims 6 entries; only 2 fit
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x1000) | 1);
            rom.write_u32(table + 4, Ptr(0x1100) | 1);

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitSoundFootStepsAt(rom, list, switch2Addr, pointer));
            Assert.Null(ex); // must not throw on the past-EOF entries

            // Only the 2 entries that fit before EOF are emitted as ASM (the other 4 are truncated).
            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(2, asms.Count);
            Assert.Contains(asms, a => a.Addr == 0x1000);
            Assert.Contains(asms, a => a.Addr == 0x1100);
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

        [Fact]
        public void EmitStatusRMenuRoots_RootSlotNearEof_SkipsWithoutThrowing()
        {
            // Regression (PR #1276 review): a root whose 4-byte pointer slot straddles EOF must skip,
            // not throw. ROM.p32 only short-circuits when root >= Data.Length; a root in [Len-3, Len-1]
            // reaches u32 -> check_safety -> throw. The root+3 guard (matching MakeVarsIDArrayCore) skips.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint rootNearEof = size - 2; // 0x1FFE: < Len but root+3 = 0x2001 > Len

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitStatusRMenuRoots(rom, list, new uint[] { rootNearEof }));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitStatusRMenuSub_NodeNearEof_SkipsWithoutThrowing()
        {
            // Regression (PR #1276 review): a node whose 28-byte record straddles EOF must skip, not
            // throw. The node is readable at p+18 (old guard) but its u32 reads at p+20/p+24 (-> p+27)
            // run past EOF; the widened p+27 guard skips the whole node gracefully.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint rootPtr = 0x0300;
            uint node = size - 20; // 0x1FEC: p+18=0x1FFE < Len (old guard passes) but p+27=0x2007 > Len
            rom.write_u32(rootPtr, Ptr(node));

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitStatusRMenuRoots(rom, list, new uint[] { rootPtr }));
            Assert.Null(ex);
            Assert.Empty(list); // node straddles EOF -> not emitted
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

        // ====================================================================
        // slice 2e: flat LZ77-image + palette forms
        // ====================================================================

        // ---- LZ77 helpers --------------------------------------------------

        /// <summary>Hand-author a VALID all-literal LZ77 stream of <paramref name="uncompSize"/>
        /// uncompressed bytes and write it at <paramref name="offset"/>. Returns the byte length the
        /// stream occupies (== LZ77.getCompressedSize on it): header(4) + ceil(N/8) flag bytes + N
        /// literal bytes. Each flag byte is 0x00 (all 8 following bytes are literal/uncompressed).</summary>
        static uint WriteLz77AllLiteral(ROM rom, uint offset, int uncompSize)
        {
            var bytes = new List<byte>();
            bytes.Add(0x10);
            bytes.Add((byte)(uncompSize & 0xFF));
            bytes.Add((byte)((uncompSize >> 8) & 0xFF));
            bytes.Add((byte)((uncompSize >> 16) & 0xFF));
            int written = 0;
            while (written < uncompSize)
            {
                bytes.Add(0x00); // flag: next 8 bytes all literal
                for (int b = 0; b < 8 && written < uncompSize; b++, written++)
                {
                    bytes.Add((byte)(0x40 + (written & 0x3F))); // arbitrary non-zero literal
                }
            }
            for (int i = 0; i < bytes.Count; i++)
            {
                rom.write_u8(offset + (uint)i, bytes[i]);
            }
            uint clen = LZ77.getCompressedSize(rom.Data, offset);
            Assert.True(clen > 0, "hand-authored LZ77 stream must be valid (getCompressedSize > 0)");
            Assert.Equal((uint)bytes.Count, clen);
            return clen;
        }

        // ---- SubKind.Lz77Pointer + FixedPointer machinery (via WalkAndAdd) --

        [Fact]
        public void SubWalk_Lz77Pointer_EmitsLZ77IMG_WithGetCompressedSizeLength()
        {
            var rom = CreateTestRom(0x8000);
            // One-entry table: base at 0x1000, block 4, FixedCount 1. Entry +0 -> embedded LZ77 image.
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint imageData = 0x2000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(imageData)); // embedded image pointer at offset 0
            uint expectLen = WriteLz77AllLiteral(rom, imageData, 100);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Img",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 1,
                PointerIndexes = new uint[] { 0 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 0,
                        Kind = RebuildProducerCore.SubKind.Lz77Pointer,
                        DataType = Address.DataTypeEnum.LZ77IMG,
                        Name = (r, i) => "img" + i,
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR Address (block*(1+1)=8) + one LZ77IMG sub-Address.
            Address img = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG);
            Assert.Equal(imageData, img.Addr);
            Assert.Equal(expectLen, img.Length);
            Assert.Equal(LZ77.getCompressedSize(rom.Data, imageData), img.Length);
            Assert.Equal(table + 0, img.Pointer); // the embedded pointer field
        }

        [Fact]
        public void SubWalk_Lz77Pointer_MalformedNearEofStream_EmitsLengthZero_NoThrow()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint imageData = 0x2000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(imageData));
            // Plant a malformed stream: 0x10 header claiming a huge size with no body -> getCompressedSize 0.
            rom.write_u8(imageData + 0, 0x10);
            rom.write_u8(imageData + 1, 0xFF);
            rom.write_u8(imageData + 2, 0xFF);
            rom.write_u8(imageData + 3, 0xFF);
            Assert.Equal(0u, LZ77.getCompressedSize(rom.Data, imageData)); // confirm malformed

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Img",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 1,
                PointerIndexes = new uint[] { 0 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 0,
                        Kind = RebuildProducerCore.SubKind.Lz77Pointer,
                        DataType = Address.DataTypeEnum.LZ77IMG,
                        Name = (r, i) => "img",
                    },
                },
            };

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.WalkAndAdd(rom, list, d));
            Assert.Null(ex); // never throws (matches WF AddLZ77Pointer)
            Address img = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG);
            Assert.Equal(0u, img.Length); // malformed -> length 0 (WF parity)
        }

        [Fact]
        public void SubWalk_FixedPointer_EmitsFixedLengthBlock_WithConfiguredDataType()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint palData = 0x2000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 16, Ptr(palData)); // embedded palette pointer at offset 16

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Pal",
                PointerField = _ => pointer,
                BlockSize = 24,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 1,
                PointerIndexes = new uint[] { 16 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 16,
                        Kind = RebuildProducerCore.SubKind.FixedPointer,
                        FixedLength = 0x20,
                        DataType = Address.DataTypeEnum.PAL,
                        Name = (r, i) => "pal",
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Address pal = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.PAL);
            Assert.Equal(palData, pal.Addr);
            Assert.Equal(0x20u, pal.Length);
            Assert.Equal(table + 16, pal.Pointer);
        }

        [Fact]
        public void EmitMainIfr_False_SuppressesMainAddress_ButStillWalksSubWalks()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint imgData = 0x2000;
            uint palData = 0x3000;
            rom.write_u32(pointer, Ptr(table));
            // 2 entries, block 4, FixedCount 2; each entry: image @0, palette @16... but block is 4,
            // so use a 20-byte block to fit both columns and a fixed count of 1 for simplicity.
            rom.write_u32(table + 0, Ptr(imgData));
            rom.write_u32(table + 16, Ptr(palData));
            WriteLz77AllLiteral(rom, imgData, 40);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "GEP",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 1,
                PointerIndexes = new uint[] { },
                EmitMainIfr = false,
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 0, Kind = RebuildProducerCore.SubKind.FixedPointer, FixedLength = 0x200, DataType = Address.DataTypeEnum.IMG, Name = (r, i) => "img" },
                    new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 16, Kind = RebuildProducerCore.SubKind.FixedPointer, FixedLength = 0x20, DataType = Address.DataTypeEnum.PAL, Name = (r, i) => "pal" },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // No main IFR Address (InputFormRef type) — only the per-entry IMG + PAL columns.
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.IMG && a.Addr == imgData);
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.PAL && a.Addr == palData);
        }

        [Fact]
        public void EmitMainIfr_DefaultsTrue_EmitsMainAddress()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "T",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 3,
                PointerIndexes = new uint[] { },
            };
            Assert.True(d.EmitMainIfr); // default

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.InputFormRef);
        }

        // ---- new DataCountRules --------------------------------------------

        [Fact]
        public void Rule_TwoU32PointerAt04_ContinuesWhileBothPointers()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 12;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: +0 and +4 both pointers -> valid
            rom.write_u32(table + 0 * block + 0, Ptr(0x4000));
            rom.write_u32(table + 0 * block + 4, Ptr(0x4100));
            // entry 1: +0 pointer, +4 NOT a pointer -> terminator
            rom.write_u32(table + 1 * block + 0, Ptr(0x4200));
            rom.write_u32(table + 1 * block + 4, 0x12345678); // not a ROM pointer

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "BBG",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.TwoU32PointerAt04,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            // dataCount = 1, length = 12 * (1 + 1) = 24
            Assert.Equal(24u, list[0].Length);
        }

        [Fact]
        public void Rule_WaitIconRule_Entry0AlwaysAndTerminatesOnBothZero()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 8;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: both zero — but entry 0 always counts.
            rom.write_u32(table + 0 * block + 0, 0);
            rom.write_u32(table + 0 * block + 4, 0);
            // entry 1: +4 pointer -> valid
            rom.write_u32(table + 1 * block + 4, Ptr(0x4000));
            // entry 2: +4 == 0, +0 != 0 -> valid (continue)
            rom.write_u32(table + 2 * block + 0, 0x55);
            rom.write_u32(table + 2 * block + 4, 0);
            // entry 3: +4 == 0, +0 == 0 -> terminator
            rom.write_u32(table + 3 * block + 0, 0);
            rom.write_u32(table + 3 * block + 4, 0);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Wait",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.WaitIconRule,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            // dataCount = 3 (entries 0,1,2), length = 8 * (3 + 1) = 32
            Assert.Equal(32u, list[0].Length);
        }

        [Fact]
        public void Rule_WaitIconRule_TerminatesOnNonZeroNonPointerAt4()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 8;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0 * block + 4, Ptr(0x4000)); // entry 0 always
            // entry 1: +4 = non-zero non-pointer -> terminator
            rom.write_u32(table + 1 * block + 4, 0x000000AB);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Wait",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.WaitIconRule,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            // dataCount = 1, length = 8 * 2 = 16
            Assert.Equal(16u, list[0].Length);
        }

        [Fact]
        public void Rule_UnitPaletteRule_TerminatesWhenPaletteAndNameBothNull()
        {
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint block = 16;
            rom.write_u32(pointer, Ptr(table));
            // entry 0: +12 pointer -> valid
            rom.write_u32(table + 0 * block + 12, Ptr(0x4000));
            // entry 1: +12 == 0 but +0 (name) != 0 -> valid (continue)
            rom.write_u32(table + 1 * block + 0, 0x77);
            rom.write_u32(table + 1 * block + 12, 0);
            // entry 2: +12 == 0 and +0 == 0 -> terminator
            rom.write_u32(table + 2 * block + 0, 0);
            rom.write_u32(table + 2 * block + 12, 0);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "UPal",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.UnitPaletteRule,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);
            // dataCount = 2, length = 16 * 3 = 48
            Assert.Equal(48u, list[0].Length);
        }

        // ---- BuildBatchDescriptors: version-agnostic image descriptors ------

        [Fact]
        public void BuildBatchDescriptors_HasVersionAgnosticImageForms()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);

                var bbg = descs.Single(d => d.Name == "BattleBG");
                Assert.Equal(12u, bbg.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.TwoU32PointerAt04, bbg.Rule);
                Assert.Equal(new uint[] { 0, 4, 8 }, bbg.PointerIndexes);
                Assert.Equal(3, bbg.SubWalks.Count);
                Assert.All(bbg.SubWalks, s => Assert.Equal(RebuildProducerCore.SubKind.Lz77Pointer, s.Kind));
                Assert.Equal(Address.DataTypeEnum.LZ77PAL, bbg.SubWalks[2].DataType);

                var bt = descs.Single(d => d.Name == "BattleTerrain");
                Assert.Equal(24u, bt.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, bt.Rule);
                Assert.Equal(12u, bt.RuleOffset);
                Assert.Equal(new uint[] { 12, 16 }, bt.PointerIndexes);
                Assert.Equal(RebuildProducerCore.SubKind.Lz77Pointer, bt.SubWalks[0].Kind);
                Assert.Equal(RebuildProducerCore.SubKind.FixedPointer, bt.SubWalks[1].Kind);
                Assert.Equal(0x20u, bt.SubWalks[1].FixedLength);

                var wait = descs.Single(d => d.Name == "WaitUnitIcon");
                Assert.Equal(8u, wait.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.WaitIconRule, wait.Rule);
                Assert.Equal(new uint[] { 4 }, wait.PointerIndexes);

                var upal = descs.Single(d => d.Name == "UnitPalette" && d.BlockSize == 16);
                Assert.Equal(RebuildProducerCore.DataCountRule.UnitPaletteRule, upal.Rule);
                Assert.Equal(Address.DataTypeEnum.LZ77PAL, upal.SubWalks[0].DataType);

                var gep = descs.Single(d => d.Name == "GenericEnemyPortait");
                Assert.False(gep.EmitMainIfr);
                Assert.NotNull(gep.ExtraFixedPointers);
                Assert.Single(gep.ExtraFixedPointers);
                Assert.Equal(Address.DataTypeEnum.POINTER, gep.ExtraFixedPointers[0].DataType);
                Assert.Equal(8u * 2 * 4, gep.ExtraFixedPointers[0].FixedLength);
                Assert.Equal(2, gep.SubWalks.Count);
                Assert.Equal((4u * 8 / 2) * (4 * 8), gep.SubWalks[0].FixedLength);
                Assert.Equal(Address.DataTypeEnum.IMG, gep.SubWalks[0].DataType);
                Assert.Equal(Address.DataTypeEnum.PAL, gep.SubWalks[1].DataType);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void BuildBatchDescriptors_FE8_HasChapterTitle_Block12_ThreeLz77Columns()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var ct = descs.Single(d => d.Name == "ChapterTitleImage");
                Assert.Equal(12u, ct.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, ct.Rule);
                Assert.Equal(new uint[] { 0, 4, 8 }, ct.PointerIndexes);
                Assert.Equal(3, ct.SubWalks.Count);
                Assert.All(ct.SubWalks, s => Assert.Equal(RebuildProducerCore.SubKind.Lz77Pointer, s.Kind));
                Assert.All(ct.SubWalks, s => Assert.Equal(Address.DataTypeEnum.LZ77IMG, s.DataType));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_FE6_HasChapterTitleFE7_Block4_OneLz77Column()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01");
                CoreState.ROM = fe6;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe6);
                var ct = descs.Single(d => d.Name == "ChapterTitleImage");
                Assert.Equal(4u, ct.BlockSize); // FE7-form layout: block 4, ONE column
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, ct.Rule);
                Assert.Equal(new uint[] { 0 }, ct.PointerIndexes);
                Assert.Single(ct.SubWalks);
                Assert.Equal(Address.DataTypeEnum.LZ77IMG, ct.SubWalks[0].DataType);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- dedicated flat emitters: faithfulness on a version ROM ---------

        [Fact]
        public void EmitImageBattleScreen_EmitsConstTSA_Pal_AndLz77Images()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;

                // Plant a data pointer at each RomInfo slot and the data at the target.
                uint tsa1Slot = U.toOffset(fe8.RomInfo.battle_screen_TSA1_pointer);
                uint palSlot = U.toOffset(fe8.RomInfo.battle_screen_palette_pointer);
                uint img1Slot = U.toOffset(fe8.RomInfo.battle_screen_image1_pointer);
                uint tsaData = 0x1000000;
                uint palData = 0x1001000;
                uint imgData = 0x1002000;
                fe8.write_u32(tsa1Slot, Ptr(tsaData));
                fe8.write_u32(palSlot, Ptr(palData));
                fe8.write_u32(img1Slot, Ptr(imgData));
                uint expectImgLen = WriteLz77AllLiteral(fe8, imgData, 60);

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitImageBattleScreen(fe8, list));
                Assert.Null(ex);

                // TSA1: constant length (5+1)*((15+1)-1)*2 = 180, type TSA, pointer = the slot.
                Address tsa1 = Assert.Single(list, a => a.Info == "battle_screen_TSA1");
                Assert.Equal(tsaData, tsa1.Addr);
                Assert.Equal((uint)((5 + 1) * ((15 + 1) - 1) * 2), tsa1.Length);
                Assert.Equal(Address.DataTypeEnum.TSA, tsa1.DataType);
                Assert.Equal(tsa1Slot, tsa1.Pointer);

                // Palette: 0x20*4 = 128, type PAL.
                Address pal = Assert.Single(list, a => a.Info == "battle_screen_palette");
                Assert.Equal(palData, pal.Addr);
                Assert.Equal(0x20u * 4, pal.Length);
                Assert.Equal(Address.DataTypeEnum.PAL, pal.DataType);

                // image1: LZ77IMG with getCompressedSize length.
                Address img1 = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG && a.Addr == imgData);
                Assert.Equal(expectImgLen, img1.Length);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitWorldMapImageFE6_EmitsTenLz77Blocks_AtPlusEightOffsets()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01");
                CoreState.ROM = fe6;

                uint imgP = U.toOffset(fe6.RomInfo.worldmap_big_image_pointer);
                uint palP = U.toOffset(fe6.RomInfo.worldmap_big_palette_pointer);
                // Plant LZ77 streams at the base image slot and base palette slot.
                uint imgData = 0x700000;
                uint palData = 0x701000;
                fe6.write_u32(imgP + 0, Ptr(imgData));
                fe6.write_u32(palP + 0, Ptr(palData));
                uint imgLen = WriteLz77AllLiteral(fe6, imgData, 50);
                uint palLen = WriteLz77AllLiteral(fe6, palData, 24);

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitWorldMapImageFE6(fe6, list));
                Assert.Null(ex);

                // 10 AddLZ77Pointer calls; the +0 image is LZ77IMG, +0 palette is LZ77PAL.
                Address img0 = Assert.Single(list, a => a.Info == "worldmap_big_image");
                Assert.Equal(imgData, img0.Addr);
                Assert.Equal(imgLen, img0.Length);
                Assert.Equal(Address.DataTypeEnum.LZ77IMG, img0.DataType);
                Assert.Equal(imgP + 0, img0.Pointer);

                Address pal0 = Assert.Single(list, a => a.Info == "worldmap_big_palette");
                Assert.Equal(palData, pal0.Addr);
                Assert.Equal(palLen, pal0.Length);
                Assert.Equal(Address.DataTypeEnum.LZ77PAL, pal0.DataType);

                // The +8/+16/+24/+32 slots point at 0 (no data planted) -> AddLZ77Pointer skips them
                // gracefully (no throw), so the NW/NE/SW/SE labels are NOT in the list. Only the two
                // base (+0) blocks were planted.
                Assert.DoesNotContain(list, a => a.Info == "worldmap_big_imageNW");
                Assert.Equal(2, list.Count); // exactly the +0 image + +0 palette
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitWorldMapImageFE7_EmitsPalette_TwelveFixedImages_AndEventBlocks()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe7 = MakeVersionedRom("AE7E01");
                CoreState.ROM = fe7;

                // worldmap_big_image_pointer -> imagemap table of 12 image pointers.
                uint imgPtrSlot = U.toOffset(fe7.RomInfo.worldmap_big_image_pointer);
                uint palPtrSlot = U.toOffset(fe7.RomInfo.worldmap_big_palette_pointer);
                uint tsaPtrSlot = U.toOffset(fe7.RomInfo.worldmap_big_palettemap_pointer);
                uint imagemap = 0x600000; // table of 12 u32 image pointers
                uint palData = 0x601000;
                uint tsamap = 0x602000;   // p32(tsaPtrSlot) -> tsamap; p32(tsamap) -> a tsa addr
                uint tsaData = 0x603000;
                uint img0Data = 0x610000;
                fe7.write_u32(imgPtrSlot, Ptr(imagemap));
                fe7.write_u32(palPtrSlot, Ptr(palData));
                fe7.write_u32(tsaPtrSlot, Ptr(tsamap));
                fe7.write_u32(tsamap, Ptr(tsaData)); // p32(tsamap) read inside the loop
                // Plant all 12 image pointers in the imagemap table (each to a distinct target).
                for (uint i = 0; i < 12; i++)
                {
                    fe7.write_u32(imagemap + i * 4, Ptr(img0Data + i * 0x1000));
                }

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitWorldMapImageFE7(fe7, list));
                Assert.Null(ex);

                // Palette: 0x20*4, type PAL.
                Address pal = Assert.Single(list, a => a.Info == "worldmap_big_palette");
                Assert.Equal(palData, pal.Addr);
                Assert.Equal(0x20u * 4, pal.Length);

                // 12 image entries (worldmap_big_image0..11), each fixed 256/2*256.
                Address img0 = Assert.Single(list, a => a.Info == "worldmap_big_image0");
                Assert.Equal(img0Data, img0.Addr);
                Assert.Equal((uint)(256 / 2 * 256), img0.Length);
                Assert.Equal(Address.DataTypeEnum.IMG, img0.DataType);
                Assert.Equal(12, list.Count(a => a.Info != null && a.Info.StartsWith("worldmap_big_image") && a.DataType == Address.DataTypeEnum.IMG));

                // 12 tsa entries, all reading the SAME tsamap slot (WF quirk reproduced),
                // each fixed 256/8*256/8, pointer = tsamap.
                Address tsa0 = Assert.Single(list, a => a.Info == "worldmap_big_tsa0");
                Assert.Equal(tsaData, tsa0.Addr);
                Assert.Equal((uint)(256 / 8 * 256 / 8), tsa0.Length);
                Assert.Equal(Address.DataTypeEnum.TSA, tsa0.DataType);
                Assert.Equal(tsamap, tsa0.Pointer);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- whole-producer integration: version gating + no-throw ----------

        [Fact]
        public void MakeAllStructPointers_AllZeroSyntheticRom_DoesNotThrow_AndDefersImageForms()
        {
            // A synthetic CreateTestRom has RomInfo == null, so MakeAllStructPointers can't run
            // (it reads rom.RomInfo). Instead verify the producer is robust on a real-version ROM
            // whose image pointer slots are all-zero (no data) — every flat emitter / descriptor
            // gracefully emits nothing for an unsafe/zero pointer.
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(fe8);

                var ex = Record.Exception(() =>
                {
                    RebuildProducerCore.ProducerResult res = RebuildProducerCore.MakeAllStructPointers(fe8);
                    Assert.NotNull(res);
                    Assert.False(res.IsComplete); // image/anime/etc. forms still deferred
                });
                Assert.Null(ex);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = null;
            }
        }

        [Fact]
        public void GetNotYetPortedForms_NoLongerListsPortedImageForms_StillListsDeferred()
        {
            var ported = RebuildProducerCore.GetNotYetPortedForms();
            // ported in slice 2e -> removed from the deferred list:
            Assert.DoesNotContain("ImageBattleBGForm", ported);
            Assert.DoesNotContain("ImageBattleTerrainForm", ported);
            Assert.DoesNotContain("ImageBattleScreenForm", ported);
            Assert.DoesNotContain("ImageUnitWaitIconFrom", ported);
            Assert.DoesNotContain("ImageUnitPaletteForm", ported);
            Assert.DoesNotContain("ImageGenericEnemyPortraitForm", ported);
            Assert.DoesNotContain("ImageChapterTitleForm", ported);
            // still deferred (need ImageUtil / config / runtime-inspection subsystems):
            Assert.Contains("ImageBGForm", ported);
            Assert.Contains("ImageCGForm", ported);
            Assert.Contains("ImageSystemIconForm", ported);
            Assert.Contains("ImageBattleAnimeForm", ported);
            Assert.Contains("ImagePortraitForm", ported);
            Assert.Contains("WorldMapImageForm", ported); // the FE8 form stays (AddHeaderTSAPointer)
            Assert.Contains("ImageItemIconForm", ported);
            Assert.Contains("ImageTSAAnimeForm", ported);
        }

        // ===================================================================
        // slice 2f: AI / Arena / Mant flat descriptors + ItemUsage / UnitFE6
        // dedicated emitters.
        // ===================================================================

        // ---- AIMapSetting / AIPerformStaff / AIPerformItem / Mant / Arena descriptors ----

        [Fact]
        public void BuildBatchDescriptors_HasAIMapSetting_FlatU8NotEqualFF()
        {
            // AIMapSettingForm: base ai_map_setting_pointer, block 4, u8!=0xFF, pointerIndexes {},
            // no sub-walk. Version-agnostic (called unconditionally in WF).
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var d = descs.Single(x => x.Name == "AIMapSetting");
                Assert.Equal(4u, d.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.U8NotEqual, d.Rule);
                Assert.Equal(0u, d.RuleOffset);
                Assert.Equal(0xFFu, d.RuleStopValue);
                Assert.Equal(new uint[] { }, d.PointerIndexes);
                Assert.Null(d.SubWalks);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_HasAIPerformStaffAndItem_WithAsmFunctionSubWalkAt4()
        {
            // AIPerformStaff/AIPerformItem: block 8, u16!=0, pointerIndexes {4}, + AsmFunction sub-walk
            // @4 (WF AddFunctions(MakeList(), 4, ...)). Version-agnostic.
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);

                foreach (string name in new[] { "AIPerformStaff", "AIPerformItem" })
                {
                    var d = descs.Single(x => x.Name == name);
                    Assert.Equal(8u, d.BlockSize);
                    Assert.Equal(RebuildProducerCore.DataCountRule.U16NotZero, d.Rule);
                    Assert.Equal(new uint[] { 4 }, d.PointerIndexes);
                    Assert.NotNull(d.SubWalks);
                    var sw = d.SubWalks.Single();
                    Assert.Equal(4u, sw.EmbeddedPointerOffset);
                    Assert.Equal(RebuildProducerCore.SubKind.AsmFunction, sw.Kind);
                }
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_HasMant_PointerAt0_WithFixedPointer0x10SubWalk()
        {
            // MantAnimationForm: block 4, PointerAt @0, pointerIndexes {0}, + FixedPointer sub-walk @0
            // (0x10-byte POINTER block). Version-agnostic.
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var d = descs.Single(x => x.Name == "Mant");
                Assert.Equal(4u, d.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, d.Rule);
                Assert.Equal(0u, d.RuleOffset);
                Assert.Equal(new uint[] { 0 }, d.PointerIndexes);
                var sw = d.SubWalks.Single();
                Assert.Equal(0u, sw.EmbeddedPointerOffset);
                Assert.Equal(RebuildProducerCore.SubKind.FixedPointer, sw.Kind);
                Assert.Equal(0x10u, sw.FixedLength);
                Assert.Equal(Address.DataTypeEnum.POINTER, sw.DataType);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_HasArenaEnemyWeapon_EmittedTwice_BothBasic()
        {
            // VERBATIM QUIRK: WF ArenaEnemyWeaponForm.MakeAllDataLength emits the descriptor TWICE,
            // and BOTH read the BASIC table (Init(null), i<8) — the second is a copy/paste of the
            // basic, NOT the rankup table. So there must be EXACTLY 2 identical "ArenaEnemyWeapon"
            // descriptors, both FixedCount=8 on arena_enemy_weapon_basic_pointer.
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var arena = descs.Where(x => x.Name == "ArenaEnemyWeapon").ToList();
                Assert.Equal(2, arena.Count);
                Assert.All(arena, d =>
                {
                    Assert.Equal(1u, d.BlockSize);
                    Assert.Equal(RebuildProducerCore.DataCountRule.FixedCount, d.Rule);
                    Assert.Equal(8u, d.RuleFixedCount);
                    Assert.Equal(new uint[] { }, d.PointerIndexes);
                });
                // both resolve to the SAME basic pointer field (not the rankup pointer).
                Assert.Equal(arena[0].PointerField(fe8), arena[1].PointerField(fe8));
                Assert.Equal(fe8.RomInfo.arena_enemy_weapon_basic_pointer, arena[0].PointerField(fe8));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void WalkAndAdd_AIPerformStaffShape_EmitsMainPlusAsmFunctionPerEntry()
        {
            // Synthetic: base block 8, u16!=0 terminator, AsmFunction @4. 2 valid entries -> main IFR
            // (length 8*(2+1)=24) + 2 ASM blocks at ProgramAddrToPlain(u32(p+4)).
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            const uint block = 8;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u16(table + block * 0, 0x0001); // entry 0 valid (u16!=0)
            rom.write_u16(table + block * 1, 0x0002); // entry 1 valid
            rom.write_u16(table + block * 2, 0x0000); // entry 2 -> stop
            uint asm0 = 0x2000, asm1 = 0x2100;
            rom.write_u32(table + block * 0 + 4, Ptr(asm0) | 1); // thumb bit
            rom.write_u32(table + block * 1 + 4, Ptr(asm1) | 1);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "AIPerformStaff",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.U16NotZero,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 4 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 4, Kind = RebuildProducerCore.SubKind.AsmFunction, Name = (r, i) => "AIPerformStaff_ASM_" },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.Equal(table, main.Addr);
            Assert.Equal(24u, main.Length); // 8*(2+1)
            Assert.Equal(new uint[] { 4 }, main.PointerIndexes);

            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(2, asms.Count);
            Assert.Contains(asms, a => a.Addr == asm0); // thumb bit cleared by ProgramAddrToPlain
            Assert.Contains(asms, a => a.Addr == asm1);
            Assert.All(asms, a => Assert.Equal(0u, a.Length));
        }

        [Fact]
        public void WalkAndAdd_MantShape_EmitsFixedPointer0x10BehindEachEntry()
        {
            // Synthetic: base block 4, PointerAt @0, FixedPointer @0 (0x10 POINTER). 2 valid entries.
            var rom = CreateTestRom();
            uint table = 0x1000;
            uint pointer = 0x0240;
            const uint block = 4;
            rom.write_u32(pointer, Ptr(table));
            uint t0 = 0x2000, t1 = 0x2100;
            rom.write_u32(table + block * 0, Ptr(t0)); // entry 0: valid pointer
            rom.write_u32(table + block * 1, Ptr(t1)); // entry 1: valid pointer
            rom.write_u32(table + block * 2, 0);       // entry 2: NULL -> PointerAt stops

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "Mant",
                PointerField = _ => pointer,
                BlockSize = block,
                Rule = RebuildProducerCore.DataCountRule.PointerAt,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 0 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 0, Kind = RebuildProducerCore.SubKind.FixedPointer, FixedLength = 0x10, DataType = Address.DataTypeEnum.POINTER, Name = (r, i) => "MANT_P:" + U.To0xHexString((int)i) },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR + 2 POINTER sub-blocks of 0x10 bytes each.
            var ptrs = list.Where(a => a.DataType == Address.DataTypeEnum.POINTER).ToList();
            Assert.Equal(2, ptrs.Count);
            Address p0 = ptrs.Single(a => a.Addr == t0);
            Assert.Equal(0x10u, p0.Length);
            Assert.Equal(table + block * 0, p0.Pointer);
            Address p1 = ptrs.Single(a => a.Addr == t1);
            Assert.Equal(0x10u, p1.Length);
            Assert.Equal(table + block * 1, p1.Pointer);
        }

        // ---- EmitItemUsagePointerTables (Switch2-gated dedicated walker) ----

        // Build a single-usage table array with an explicit pointer + switch2 address.
        static RebuildProducerCore.ItemUsageTable[] OneUsageTable(uint pointer, uint switch2Addr, string name)
        {
            return new[]
            {
                new RebuildProducerCore.ItemUsageTable
                {
                    Pointer = _ => pointer,
                    Switch2Address = _ => switch2Addr,
                    Name = name,
                },
            };
        }

        [Fact]
        public void EmitItemUsagePointer_Switch2Enabled_EmitsMainAsmIfrPlusAsmPerEntry()
        {
            // Switch2 enabled, count byte = 2 -> DataCount = 3. Main InputFormRef_ASM Address
            // (length 4*(3+1)=16, pointer = the RomInfo slot, pointerIndexes {0}) + 3 ASM AddFunctions
            // at offset 0 (the entry block address itself).
            var rom = CreateTestRom(0x8000);
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400; // the usage data-pointer slot
            uint table = 0x1000;   // base = p32(pointer)
            PlantSwitch2(rom, switch2Addr, count: 2); // DataCount = count + 1 = 3
            rom.write_u32(pointer, Ptr(table));
            uint a0 = 0x2000, a1 = 0x2100, a2 = 0x2200;
            rom.write_u32(table + 0, Ptr(a0) | 1);
            rom.write_u32(table + 4, Ptr(a1) | 1);
            rom.write_u32(table + 8, Ptr(a2) | 1);

            var list = new List<Address>();
            RebuildProducerCore.EmitItemUsagePointerTables(rom, list, OneUsageTable(pointer, switch2Addr, "ItemUsageP0"));

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            Assert.Equal(table, main.Addr);
            // BasePointer IS the RomInfo slot here (safe offset), unlike SoundFootSteps (NOT_FOUND).
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(4u, main.BlockSize);
            Assert.Equal(16u, main.Length); // 4*(3+1)
            Assert.Equal(new uint[] { 0 }, main.PointerIndexes);

            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(3, asms.Count);
            Assert.Contains(asms, a => a.Addr == a0);
            Assert.Contains(asms, a => a.Addr == a1);
            Assert.Contains(asms, a => a.Addr == a2);
        }

        [Fact]
        public void EmitItemUsagePointer_Switch2Disabled_EmitsNothingForThatUsage()
        {
            // WF ReInit returns NOT_FOUND (continue) when IsSwitch2Enable is false. An all-zero
            // switch2 region (subOp/cmpOp out of range) is disabled.
            var rom = CreateTestRom();
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000) | 1);

            var list = new List<Address>();
            RebuildProducerCore.EmitItemUsagePointerTables(rom, list, OneUsageTable(pointer, switch2Addr, "ItemUsageP0"));
            Assert.Empty(list);
        }

        [Fact]
        public void EmitItemUsagePointer_CountRunsPastEof_TruncatesWithoutThrowing()
        {
            // A corrupted/too-large count near EOF must TRUNCATE (like WF MakeList breaking on
            // addr + BlockSize > Data.Length), NOT throw (AddFunction's u32 read would throw past EOF).
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint switch2Addr = 0x0300;
            uint pointer = 0x0400;
            uint table = size - 8; // 0x1FF8 — exactly 2 of the 4-byte entries fit before EOF.
            PlantSwitch2(rom, switch2Addr, count: 5); // claims 6 entries; only 2 fit
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x1000) | 1);
            rom.write_u32(table + 4, Ptr(0x1100) | 1);

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitItemUsagePointerTables(rom, list, OneUsageTable(pointer, switch2Addr, "ItemUsageP0")));
            Assert.Null(ex);

            // main IFR still emitted; only the 2 in-bounds entries become ASM (the rest truncated).
            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.InputFormRef_ASM);
            var asms = list.Where(a => a.DataType == Address.DataTypeEnum.ASM).ToList();
            Assert.Equal(2, asms.Count);
        }

        [Fact]
        public void EmitItemUsagePointer_PointerSlotNearEof_SkipsWithoutThrowing()
        {
            // Regression (PR #1278 review): a usage data-pointer SLOT whose 4 bytes straddle EOF must
            // skip, not throw. isSafetyOffset(pointer) alone passes (pointer < Len) but p32(pointer)
            // reads pointer..pointer+3 -> u32 -> check_safety throws. The pointer+3 guard skips it.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint switch2Addr = 0x0300;
            uint pointerNearEof = size - 2; // 0x1FFE: < Len (old guard passes) but pointer+3 = 0x2001 > Len
            PlantSwitch2(rom, switch2Addr, count: 2); // Switch2 enabled so we reach the pointer guard

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitItemUsagePointerTables(rom, list, OneUsageTable(pointerNearEof, switch2Addr, "ItemUsageP0")));
            Assert.Null(ex);
            Assert.Empty(list); // pointer slot straddles EOF -> usage skipped
        }

        [Fact]
        public void EmitItemUsagePointer_AllTenUsages_PresentInRomInfoDrivenPath()
        {
            // The public RomInfo-driven EmitItemUsagePointer must not throw on a real RomInfo (FE8U).
            // It is gated per-usage on IsSwitch2Enable; on an all-zero 32MB fake ROM none are enabled,
            // so it emits nothing — but it must run cleanly (no NRE / no throw).
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitItemUsagePointer(fe8, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitUnitFE6At (FE6-only dynamic-base dedicated walker) ----

        [Fact]
        public void EmitUnitFE6At_BaseIsOneBlockPastTableStart_PointerIsNotFound()
        {
            // WF UnitFE6Form: base = p32(unit_pointer) + unit_datasize (skip unit 0), BasePointer 0 ->
            // NOT_FOUND, i < unit_maxcount, pointerIndexes {44}.
            var rom = CreateTestRom(0x8000);
            uint unitPointerSlot = 0x0400;
            uint tableStart = 0x1000;
            const uint block = 0x34; // unit_datasize (> 44 so offset 44 fits)
            const uint maxcount = 3;
            rom.write_u32(unitPointerSlot, Ptr(tableStart));

            var list = new List<Address>();
            RebuildProducerCore.EmitUnitFE6At(rom, list, unitPointerSlot, block, maxcount);

            Address main = list.Single();
            // base = tableStart + block (one block past), pointer = NOT_FOUND.
            Assert.Equal(tableStart + block, main.Addr);
            Assert.Equal(U.NOT_FOUND, main.Pointer);
            Assert.Equal(block, main.BlockSize);
            // length = block * (DataCount + 1); DataCount = maxcount (i < maxcount).
            Assert.Equal(block * (maxcount + 1), main.Length);
            Assert.Equal(new uint[] { 44 }, main.PointerIndexes);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);
        }

        [Fact]
        public void EmitUnitFE6At_ZeroBlock_EmitsNothing_AndDoesNotHang()
        {
            var rom = CreateTestRom();
            uint unitPointerSlot = 0x0400;
            rom.write_u32(unitPointerSlot, Ptr(0x1000));
            var list = new List<Address>();
            RebuildProducerCore.EmitUnitFE6At(rom, list, unitPointerSlot, block: 0, maxcount: 5);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitUnitFE6At_UnsafeBase_EmitsNothing_NoThrow()
        {
            // If p32(unit_pointer)+block is not a safe offset, WF's ReInit -> AddAddress early-returns.
            var rom = CreateTestRom(0x2000);
            uint unitPointerSlot = 0x0400;
            // table start points past EOF -> base unsafe.
            rom.write_u32(unitPointerSlot, Ptr(0x1FFF0));
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitUnitFE6At(rom, list, unitPointerSlot, block: 0x34, maxcount: 3));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitUnitFE6_FE6_RunsCleanly_FE8UDoesNotEmitUnitViaThisPath()
        {
            // The public RomInfo-driven EmitUnitFE6 reads unit_pointer/unit_datasize/unit_maxcount.
            // On a fake all-zero FE6 ROM it must run without throwing (base resolves to a zero region).
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01");
                CoreState.ROM = fe6;
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitUnitFE6(fe6, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- NotYetPorted coverage delta for slice 2f ----

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2fCoveredForms_KeepsDeferredSiblings()
        {
            string[] ported = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2f ported these 7 — no longer in the deferred list:
            Assert.DoesNotContain("AIMapSettingForm", ported);
            Assert.DoesNotContain("AIPerformStaffForm", ported);
            Assert.DoesNotContain("AIPerformItemForm", ported);
            Assert.DoesNotContain("MantAnimationForm", ported);
            Assert.DoesNotContain("ArenaEnemyWeaponForm", ported);
            Assert.DoesNotContain("ItemUsagePointerForm", ported);
            Assert.DoesNotContain("UnitFE6Form", ported);

            // deferred siblings STAY (their blocking subsystem is not in Core):
            Assert.Contains("AIScriptForm", ported);              // AI bytecode CalcLength + nested LZ77
            Assert.Contains("UnitActionPointerForm", ported);     // PatchUtil SearchUnitActionReworkPatch
            Assert.Contains("MonsterWMapProbabilityForm", ported);// EventScriptForm.ScanScript skirmish
            // (the 5 map-PLIST forms that were deferred "for slice size" here are now PORTED in slice 2g
            //  — see GetNotYetPortedForms_DropsSlice2gCoveredForms_KeepsDeferredSiblings.)
            Assert.Contains("EventCondForm", ported);             // EventScriptForm.ScanScript
            // and the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ====================================================================
        // slice 2g — per-map PLIST forms (ItemShop / MapChange / MapExitPoint /
        // MapTileAnimation1 / MapTileAnimation2). Each exercises the test-seam
        // emitter with a synthetic per-map structure, then a near-EOF / corrupt
        // case asserting no throw.
        // ====================================================================

        // ---- EmitItemShopList (BIN block per shop) --------------------------

        [Fact]
        public void EmitItemShopList_EmitsBinPerShop_LengthIsItemCountPlus1Times2()
        {
            // A shop with 3 non-zero 2-byte item entries then a 0x00 terminator -> DataCount = 3 ->
            // length = (3+1)*2 = 8. The tag (pointer slot) is recorded on the emitted Address.
            var rom = CreateTestRom();
            uint shopAddr = 0x1000;
            uint slot = 0x0800; // the inbound 4-byte pointer slot (AddrResult.tag)
            // 3 items: each item-ID byte must be non-zero; the 4th byte is the 0x00 terminator.
            rom.write_u8(shopAddr + 0, 0x11); rom.write_u8(shopAddr + 1, 0x05);
            rom.write_u8(shopAddr + 2, 0x22); rom.write_u8(shopAddr + 3, 0x02);
            rom.write_u8(shopAddr + 4, 0x33); rom.write_u8(shopAddr + 5, 0x01);
            rom.write_u8(shopAddr + 6, 0x00); // terminator item-ID

            var list = new List<Address>();
            var shops = new List<AddrResult> { new AddrResult(shopAddr, "Shop", slot) };
            RebuildProducerCore.EmitItemShopList(rom, list, shops);

            Address a = Assert.Single(list);
            Assert.Equal(shopAddr, a.Addr);
            Assert.Equal(8u, a.Length); // (3+1)*2
            Assert.Equal(slot, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.BIN, a.DataType);
            Assert.Equal("Shop", a.Info);
        }

        [Fact]
        public void EmitItemShopList_EmptyShop_LengthIsTwo()
        {
            // First item-ID byte is 0x00 -> DataCount = 0 -> length = (0+1)*2 = 2 (one terminator block).
            var rom = CreateTestRom();
            uint shopAddr = 0x1000;
            uint slot = 0x0800;
            rom.write_u8(shopAddr, 0x00);

            var list = new List<Address>();
            RebuildProducerCore.EmitItemShopList(rom, list,
                new List<AddrResult> { new AddrResult(shopAddr, "Shop", slot) });

            Address a = Assert.Single(list);
            Assert.Equal(2u, a.Length);
        }

        [Fact]
        public void EmitItemShopList_UnsafeShopAddr_EmitsNothing()
        {
            // A shop whose item-list address is below 0x200 (unsafe) is skipped (WF ReInit -> AddAddress
            // early-returns). Null/empty list also no-ops.
            var rom = CreateTestRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitItemShopList(rom, list,
                new List<AddrResult> { new AddrResult(0x0100, "Shop", 0x0800) });
            Assert.Empty(list);

            RebuildProducerCore.EmitItemShopList(rom, list, null);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitItemShopList_UnterminatedShopNearEof_TruncatesWithoutThrowing()
        {
            // An unterminated all-non-zero run up to EOF must stop at addr+block > Data.Length
            // (getBlockDataCount's EOF cutoff), NOT throw. length = (count+1)*2 is clamped to a safe value.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint shopAddr = size - 6; // 0x1FFA — 3 two-byte entries fit before EOF, no terminator
            for (uint i = shopAddr; i < size; i++) rom.write_u8(i, 0x11);

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitItemShopList(rom, list,
                new List<AddrResult> { new AddrResult(shopAddr, "Shop", 0x0800) }));
            Assert.Null(ex);
        }

        // ---- EmitMapChangeAt (per-map 12-byte IFR + w*h*2 BIN per entry) ----

        [Fact]
        public void EmitMapChangeAt_MainIfrPlusPerEntryBinBlocks()
        {
            // Slot -> change-data base; the change table has 2 records (12 bytes each) then a 0xFF
            // terminator record. Each record: +3 = w, +4 = h, +8 = p32(change-map). Record 0 points at
            // a w*h*2 = 2*3*2 = 12-byte BIN; record 1 at a 1*1*2 = 2-byte BIN.
            var rom = CreateTestRom(0x8000);
            uint slot = 0x0800;        // PLIST slot (pointer)
            uint changeBase = 0x1000;  // p32(slot)
            rom.write_u32(slot, Ptr(changeBase));

            uint mar0 = 0x2000, mar1 = 0x2100;
            // record 0
            rom.write_u8(changeBase + 0, 0x01);  // id (non-0xFF)
            rom.write_u8(changeBase + 3, 2);     // w
            rom.write_u8(changeBase + 4, 3);     // h
            rom.write_u32(changeBase + 8, Ptr(mar0));
            // record 1
            rom.write_u8(changeBase + 12 + 0, 0x02);
            rom.write_u8(changeBase + 12 + 3, 1);
            rom.write_u8(changeBase + 12 + 4, 1);
            rom.write_u32(changeBase + 12 + 8, Ptr(mar1));
            // terminator record (id == 0xFF)
            rom.write_u8(changeBase + 24, 0xFF);

            var list = new List<Address>();
            RebuildProducerCore.EmitMapChangeAt(rom, list, mapid: 0x05, pointer: slot);

            // Main IFR: base = changeBase, length = 12*(2+1) = 36, pointer = slot, block 12, {8}.
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.Equal(changeBase, main.Addr);
            Assert.Equal(36u, main.Length);
            Assert.Equal(slot, main.Pointer);
            Assert.Equal(12u, main.BlockSize);
            Assert.Equal(new uint[] { 8 }, main.PointerIndexes);
            Assert.Equal("MapChange map:0x05", main.Info); // U.To0xHexString(0x05) zero-pads to 2 digits

            // Two BIN change-map blocks.
            var bins = list.Where(a => a.DataType == Address.DataTypeEnum.BIN).ToList();
            Assert.Equal(2, bins.Count);
            Assert.Contains(bins, a => a.Addr == mar0 && a.Length == 12 && a.Pointer == changeBase + 8);
            Assert.Contains(bins, a => a.Addr == mar1 && a.Length == 2 && a.Pointer == changeBase + 12 + 8);
        }

        [Fact]
        public void EmitMapChangeAt_UnsafeBase_EmitsNothing()
        {
            // A slot whose p32 base is unsafe (0) -> WF ReInitPointer -> AddAddress early-returns.
            var rom = CreateTestRom();
            uint slot = 0x0800;
            rom.write_u32(slot, 0); // base = 0 -> unsafe
            var list = new List<Address>();
            RebuildProducerCore.EmitMapChangeAt(rom, list, mapid: 0, pointer: slot);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitMapChangeAt_EntryPointerUnsafe_SkipsEntry_KeepsMainIfr()
        {
            // An entry whose +8 change pointer is unsafe is skipped (no BIN), but the main IFR is kept.
            var rom = CreateTestRom(0x8000);
            uint slot = 0x0800, changeBase = 0x1000;
            rom.write_u32(slot, Ptr(changeBase));
            rom.write_u8(changeBase + 0, 0x01);
            rom.write_u8(changeBase + 3, 2);
            rom.write_u8(changeBase + 4, 2);
            rom.write_u32(changeBase + 8, 0); // unsafe change pointer (0)
            rom.write_u8(changeBase + 12, 0xFF); // terminator

            var list = new List<Address>();
            RebuildProducerCore.EmitMapChangeAt(rom, list, mapid: 0, pointer: slot);
            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.BIN);
        }

        [Fact]
        public void EmitMapChangeAt_PointerSlotNearEof_SkipsWithoutThrowing()
        {
            // A PLIST slot whose 4 bytes straddle EOF must skip (pointer+3 guard), not throw.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint slotNearEof = size - 2; // 0x1FFE: < Len but slot+3 = 0x2001 > Len
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMapChangeAt(rom, list, mapid: 0, pointer: slotNearEof));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitMapChangeAt_RecordRunsPastEof_TruncatesWithoutThrowing()
        {
            // A change table with no 0xFF terminator near EOF: getBlockDataCount stops at the EOF cutoff,
            // and the per-entry p32(p+8) read (which reaches p+11) is guarded by the p+block <= Len check.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint slot = 0x0800;
            uint changeBase = size - 12; // exactly one 12-byte record fits; no terminator after it
            rom.write_u32(slot, Ptr(changeBase));
            rom.write_u8(changeBase + 0, 0x01); // non-0xFF id
            rom.write_u8(changeBase + 3, 1);
            rom.write_u8(changeBase + 4, 1);
            // +8 left 0 (unsafe pointer) so no BIN; the point is the walk must not throw.

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMapChangeAt(rom, list, mapid: 0, pointer: slot));
            Assert.Null(ex);
        }

        // ---- EmitMapExitPointAt (enemy + NPC slot tables + per-map N-tables) -

        [Fact]
        public void EmitMapExitPointAt_EmitsEnemyAndNpcMains_AndPerMapSubTables()
        {
            // Enemy base = p32(mainPointer). npc_blockadd = 2: enemy table caps at 2 slots, NPC base is
            // enemyBase + 4*2. Two map slots each point at an N-table (4-byte rows, u8 != 0xFF).
            var rom = CreateTestRom(0x8000);
            uint mainPointer = 0x0800;
            uint enemyBase = 0x1000;
            rom.write_u32(mainPointer, Ptr(enemyBase));
            uint npcBlockAdd = 2;
            uint npcBase = enemyBase + 4 * npcBlockAdd; // 0x1008

            // Enemy slots (2): slot 0 -> exitData0, slot 1 -> NULL (still pointer-or-null so counted).
            uint exitData0 = 0x2000;
            rom.write_u32(enemyBase + 0, Ptr(exitData0));
            rom.write_u32(enemyBase + 4, 0); // NULL pointer (isPointerOrNULL true)
            // exitData0 N-table: 2 rows then 0xFF terminator.
            rom.write_u8(exitData0 + 0, 0x01);
            rom.write_u8(exitData0 + 4, 0x02);
            rom.write_u8(exitData0 + 8, 0xFF);

            // NPC slots: slot 0 -> exitDataN.
            uint exitDataN = 0x3000;
            rom.write_u32(npcBase + 0, Ptr(exitDataN));
            rom.write_u32(npcBase + 4, 0);
            rom.write_u8(exitDataN + 0, 0x01);
            rom.write_u8(exitDataN + 4, 0xFF);

            var list = new List<Address>();
            RebuildProducerCore.EmitMapExitPointAt(rom, list, mainPointer, npcBlockAdd, mapCount: 4);

            // Enemy main: addr = enemyBase, pointer = mainPointer, block 4, {0}.
            Address enemyMain = list.Single(a => a.Info == "MapExit");
            Assert.Equal(enemyBase, enemyMain.Addr);
            Assert.Equal(mainPointer, enemyMain.Pointer);
            Assert.Equal(4u, enemyMain.BlockSize);
            Assert.Equal(new uint[] { 0 }, enemyMain.PointerIndexes);
            // Enemy DataCount: 2 pointer-or-null slots before the i < npc_blockadd cap -> length 4*(2+1)=12.
            Assert.Equal(12u, enemyMain.Length);

            // NPC main: pointer FORCED to NOT_FOUND (AddAddressButIgnorePointer).
            Address npcMain = list.Single(a => a.Info == "MapExit NPC");
            Assert.Equal(npcBase, npcMain.Addr);
            Assert.Equal(U.NOT_FOUND, npcMain.Pointer);

            // Enemy per-map sub-table for map 0 (exitData0): length 4*(2+1)=12, pointer = enemyBase+0.
            // (U.To0xHexString(0) zero-pads to 2 digits -> "0x00".)
            Address enemySub = list.Single(a => a.Info == "MapExit map:0x00");
            Assert.Equal(exitData0, enemySub.Addr);
            Assert.Equal(enemyBase + 0, enemySub.Pointer);
            Assert.Equal(12u, enemySub.Length);
            Assert.Equal(new uint[] { }, enemySub.PointerIndexes);

            // NPC per-map sub-table for map 0 (exitDataN): length 4*(1+1)=8, pointer = npcBase+0.
            Address npcSub = list.Single(a => a.Info == "MapExit map:0x00 NPC");
            Assert.Equal(exitDataN, npcSub.Addr);
            Assert.Equal(npcBase + 0, npcSub.Pointer);
            Assert.Equal(8u, npcSub.Length);
        }

        [Fact]
        public void EmitMapExitPointAt_MainPointerNearEof_SkipsWithoutThrowing()
        {
            // The map_exit_point_pointer slot straddling EOF must skip (mainPointer+3 guard), not throw.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMapExitPointAt(rom, list, size - 2, npcBlockAdd: 2, mapCount: 4));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitMapExitPointAt_SubTableNBaseUnsafe_SkipsThatSubTable()
        {
            // A map slot whose N base (p32) is unsafe (0) must be skipped (WF N_ReInitPointer ->
            // AddAddress early-returns), but the enemy main is still emitted.
            var rom = CreateTestRom(0x8000);
            uint mainPointer = 0x0800, enemyBase = 0x1000;
            rom.write_u32(mainPointer, Ptr(enemyBase));
            rom.write_u32(enemyBase + 0, 0); // NULL -> p32 = 0 -> N base unsafe -> sub-table skipped

            var list = new List<Address>();
            RebuildProducerCore.EmitMapExitPointAt(rom, list, mainPointer, npcBlockAdd: 2, mapCount: 4);
            Assert.Contains(list, a => a.Info == "MapExit");
            Assert.DoesNotContain(list, a => a.Info == "MapExit map:0x00");
        }

        // ---- EmitMapTileAnimationFor (anime1 IMG / anime2 BIN) --------------

        [Fact]
        public void EmitMapTileAnimationFor_Anime1_MainIfrPlusImgPerEntry()
        {
            // anime1: image pointer at +4, IMG length = u16(p+2), main pointerIndexes {4}. Two entries
            // (each +4 is a valid pointer) then a non-pointer +4 terminator.
            var rom = CreateTestRom(0x8000);
            uint baseAddr = 0x1000;
            uint img0 = 0x2000, img1 = 0x2100;
            // entry 0
            rom.write_u16(baseAddr + 2, 0x40);          // length
            rom.write_u32(baseAddr + 4, Ptr(img0));     // image pointer (+4)
            // entry 1
            rom.write_u16(baseAddr + 8 + 2, 0x20);
            rom.write_u32(baseAddr + 8 + 4, Ptr(img1));
            // entry 2 terminator: +4 not a pointer (0)
            rom.write_u32(baseAddr + 16 + 4, 0);

            var list = new List<Address>();
            RebuildProducerCore.EmitMapTileAnimationFor(rom, list, baseAddr, plist: 0x07, imgAtPlus4: true);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.Equal(baseAddr, main.Addr);
            Assert.Equal(8u * (2 + 1), main.Length); // 24
            Assert.Equal(U.NOT_FOUND, main.Pointer); // BasePointer 0 -> NOT_FOUND
            Assert.Equal(8u, main.BlockSize);
            Assert.Equal(new uint[] { 4 }, main.PointerIndexes);

            var imgs = list.Where(a => a.DataType == Address.DataTypeEnum.IMG).ToList();
            Assert.Equal(2, imgs.Count);
            Assert.Contains(imgs, a => a.Addr == img0 && a.Length == 0x40 && a.Pointer == baseAddr + 4);
            Assert.Contains(imgs, a => a.Addr == img1 && a.Length == 0x20 && a.Pointer == baseAddr + 8 + 4);
        }

        [Fact]
        public void EmitMapTileAnimationFor_Anime2_MainIfrPlusBinPerEntry()
        {
            // anime2: image pointer at +0, BIN length = u8(p+5)*2, main pointerIndexes {0}.
            var rom = CreateTestRom(0x8000);
            uint baseAddr = 0x1000;
            uint pal0 = 0x2000;
            rom.write_u32(baseAddr + 0, Ptr(pal0)); // pointer at +0
            rom.write_u8(baseAddr + 5, 4);          // count -> BIN length = 4*2 = 8
            // entry 1 terminator: +0 not a pointer.
            rom.write_u32(baseAddr + 8 + 0, 0);

            var list = new List<Address>();
            RebuildProducerCore.EmitMapTileAnimationFor(rom, list, baseAddr, plist: 0x03, imgAtPlus4: false);

            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.Equal(baseAddr, main.Addr);
            Assert.Equal(8u * (1 + 1), main.Length); // 16
            Assert.Equal(new uint[] { 0 }, main.PointerIndexes);

            Address bin = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.BIN);
            Assert.Equal(pal0, bin.Addr);
            Assert.Equal(8u, bin.Length); // count*2
            Assert.Equal(baseAddr + 0, bin.Pointer);
        }

        [Fact]
        public void EmitMapTileAnimationFor_BrokenResolution_EmitsNothing()
        {
            // dataAddr == NOT_FOUND (broken "(破損)" PLIST) -> ReInit on an unsafe base -> emit nothing.
            var rom = CreateTestRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitMapTileAnimationFor(rom, list, U.NOT_FOUND, plist: 0x01, imgAtPlus4: true);
            Assert.Empty(list);
            // Also an unsafe (<0x200) base emits nothing.
            RebuildProducerCore.EmitMapTileAnimationFor(rom, list, 0x0100, plist: 0x01, imgAtPlus4: true);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitMapTileAnimationFor_EntryRunsPastEof_TruncatesWithoutThrowing()
        {
            // An entry table with no terminator near EOF: the per-entry p32(p+4) read (reaching p+7) is
            // guarded by p+block <= Len, so the walk truncates rather than throwing.
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint baseAddr = size - 8; // exactly one 8-byte entry fits
            rom.write_u32(baseAddr + 4, Ptr(0x1000)); // valid pointer so the single entry is counted

            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMapTileAnimationFor(rom, list, baseAddr, plist: 0x01, imgAtPlus4: true));
            Assert.Null(ex);
        }

        // ---- NotYetPorted coverage delta for slice 2g ----------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2gCoveredForms_KeepsDeferredSiblings()
        {
            string[] ported = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2g ported these 5 map-PLIST forms — no longer in the deferred list:
            Assert.DoesNotContain("ItemShopForm", ported);
            Assert.DoesNotContain("MapChangeForm", ported);
            Assert.DoesNotContain("MapExitPointForm", ported);
            Assert.DoesNotContain("MapTileAnimation1Form", ported);
            Assert.DoesNotContain("MapTileAnimation2Form", ported);

            // deferred map siblings STAY (their blocking subsystem is not in Core):
            Assert.Contains("MapPointerForm", ported);                 // palette2 via PatchUtil patch detect
            Assert.Contains("MapTerrainFloorLookupTableForm", ported); // PatchUtil GetPointersExtendsPatch
            Assert.Contains("MapTerrainBGLookupTableForm", ported);    // PatchUtil GetPointersExtendsPatch
            Assert.Contains("MapSettingForm", ported);                 // IsMapSettingEnd + CString
            Assert.Contains("ItemForm", ported);                       // StatBooster size via PatchUtil

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        [Fact]
        public void EmitMapPlistForms_RomInfoDriven_DoNotThrow_OnEmptyFakeRom()
        {
            // The public RomInfo-driven entry points must run cleanly on a versioned-but-empty 32MB
            // fake ROM (real RomInfo, all-zero data -> every map/exit/shop/PLIST table base resolves
            // unsafe). They emit nothing but must NOT throw (no NRE on RomInfo, no read past EOF).
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(fe8);

                var list = new List<Address>();
                var ex = Record.Exception(() =>
                {
                    RebuildProducerCore.EmitItemShop(fe8, list);
                    RebuildProducerCore.EmitMapChange(fe8, list);
                    RebuildProducerCore.EmitMapExitPoint(fe8, list);
                    RebuildProducerCore.EmitMapTileAnimation1(fe8, list);
                    RebuildProducerCore.EmitMapTileAnimation2(fe8, list);
                });
                Assert.Null(ex);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }
    }
}
