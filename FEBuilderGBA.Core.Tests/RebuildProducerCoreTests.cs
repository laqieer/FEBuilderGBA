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
            // (TextForm/TextCharCodeForm are ported in slice 2m; OtherTextForm in slice 2q — see
            //  GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("OtherTextForm", notYet);
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
            // (MapTileAnimation1Form/MapTileAnimation2Form are now PORTED in slice 2g; the MapTerrain
            //  lookup tables are now PORTED in slice 2j — see
            //  GetNotYetPortedForms_DropsSlice2jCoveredForms_KeepsDeferredSiblings.)
            Assert.Contains("SongTableForm", notYet);              // SongUtil.ParseTrack/RecycleOldInstrument
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
                // (MapTileAnimation1Form is now PORTED in slice 2g and the MapTerrain lookup tables in
                //  slice 2j — the still-deferred misc sibling SongTableForm [SongUtil.ParseTrack /
                //  RecycleOldInstrument not in Core] stays tracked here instead.)
                Assert.Contains("SongTableForm", notYet2b);

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
            // (ItemWeaponEffectForm was a deferred sibling here; slice 2l ports it via
            //  EmitItemWeaponEffect — see GetNotYetPortedForms_DropsSlice2lCoveredForms. OtherTextForm
            //  was the config-file sibling here; slice 2q ports it — see
            //  GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("OtherTextForm", notYet);
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
                "EventBattleTalkForm", "EventHaikuForm",
                "WorldMapEventPointerForm",
                // (FE8SpellMenuExtendsForm ported in slice 2m -> no longer kept here.)
                "MapSettingForm",
                "MonsterWMapProbabilityForm", "SoundRoomForm",
                // (StatusOptionForm + SoundFootStepsForm ported in slice 2d -> no longer kept here.
                //  UnitFE6Form + ItemUsagePointerForm + AIPerform*/AIMapSetting/Mant/ArenaEnemyWeapon
                //  ported in slice 2f -> no longer kept here. MapTileAnimation1Form/MapTileAnimation2Form +
                //  ItemShopForm + MapChangeForm + MapExitPointForm ported in slice 2g -> no longer kept.
                //  SupportUnitForm + WorldMapPathForm + EDStaffRollForm + OPPrologueForm + OPClassFontForm
                //  ported in slice 2h -> no longer kept here. OPClassDemoForm + OPClassDemoFE7Form ported
                //  in slice 2i -> no longer kept here. MapTerrain{BG,Floor}LookupTableForm + MapPointerForm
                //  + ExtraUnitForm + ExtraUnitFE8UForm ported in slice 2j -> no longer kept here.)
                "ItemForm", "SongTableForm",
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
            var notYet = RebuildProducerCore.GetNotYetPortedForms();
            Assert.DoesNotContain("StatusOptionForm", notYet);
            Assert.DoesNotContain("SoundFootStepsForm", notYet);
            Assert.DoesNotContain("StatusRMenuForm", notYet);
            Assert.DoesNotContain("MenuDefinitionForm", notYet);
            // sibling forms that genuinely still need un-ported subsystems STAY.
            Assert.Contains("ItemForm", notYet);
            Assert.Contains("SoundRoomForm", notYet);
            // (ItemWeaponEffectForm ported in slice 2l — see GetNotYetPortedForms_DropsSlice2lCoveredForms.)
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
            var notYet = RebuildProducerCore.GetNotYetPortedForms();
            // ported in slice 2e -> removed from the deferred list:
            Assert.DoesNotContain("ImageBattleBGForm", notYet);
            Assert.DoesNotContain("ImageBattleTerrainForm", notYet);
            Assert.DoesNotContain("ImageBattleScreenForm", notYet);
            Assert.DoesNotContain("ImageUnitWaitIconFrom", notYet);
            Assert.DoesNotContain("ImageUnitPaletteForm", notYet);
            Assert.DoesNotContain("ImageGenericEnemyPortraitForm", notYet);
            Assert.DoesNotContain("ImageChapterTitleForm", notYet);
            // ported in slice 2k (header-TSA image forms; CalcHeaderTsaLength + EmitHeaderTsaPointer +
            // CalcRomTcsLength now in Core) -> removed from the deferred list:
            Assert.DoesNotContain("ImageBGForm", notYet);
            Assert.DoesNotContain("ImageCGForm", notYet);
            Assert.DoesNotContain("ImageSystemIconForm", notYet);
            Assert.DoesNotContain("WorldMapImageForm", notYet);
            // still deferred (need ImageUtil OAM / runtime-inspection subsystems):
            Assert.Contains("ImageBattleAnimeForm", notYet);
            Assert.Contains("ImagePortraitForm", notYet);
            Assert.Contains("ImageItemIconForm", notYet);
            // (ImageTSAAnimeForm / ImageTSAAnime2Form were the config-file TSA-anime siblings here; slice
            //  2q ports them — see GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("ImageTSAAnimeForm", notYet);
            Assert.DoesNotContain("ImageTSAAnime2Form", notYet);
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
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2f ported these 7 — no longer in the deferred list:
            Assert.DoesNotContain("AIMapSettingForm", notYet);
            Assert.DoesNotContain("AIPerformStaffForm", notYet);
            Assert.DoesNotContain("AIPerformItemForm", notYet);
            Assert.DoesNotContain("MantAnimationForm", notYet);
            Assert.DoesNotContain("ArenaEnemyWeaponForm", notYet);
            Assert.DoesNotContain("ItemUsagePointerForm", notYet);
            Assert.DoesNotContain("UnitFE6Form", notYet);

            // deferred siblings STAY (their blocking subsystem is not in Core):
            Assert.Contains("AIScriptForm", notYet);              // AI bytecode CalcLength + nested LZ77
            Assert.Contains("UnitActionPointerForm", notYet);     // PatchUtil SearchUnitActionReworkPatch
            Assert.Contains("MonsterWMapProbabilityForm", notYet);// EventScriptForm.ScanScript skirmish
            // (the 5 map-PLIST forms that were deferred "for slice size" here are now PORTED in slice 2g
            //  — see GetNotYetPortedForms_DropsSlice2gCoveredForms_KeepsDeferredSiblings.)
            Assert.Contains("EventCondForm", notYet);             // EventScriptForm.ScanScript
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
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2g ported these 5 map-PLIST forms — no longer in the deferred list:
            Assert.DoesNotContain("ItemShopForm", notYet);
            Assert.DoesNotContain("MapChangeForm", notYet);
            Assert.DoesNotContain("MapExitPointForm", notYet);
            Assert.DoesNotContain("MapTileAnimation1Form", notYet);
            Assert.DoesNotContain("MapTileAnimation2Form", notYet);

            // (MapPointerForm + MapTerrain{Floor,BG}LookupTableForm were deferred map siblings here, but
            //  are now PORTED in slice 2j — their blocking Core helpers (MapPListResolverCore /
            //  MapTerrainLookupCore + the PatchDetection gates) landed for the Avalonia gap-sweep. See
            //  GetNotYetPortedForms_DropsSlice2jCoveredForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("MapPointerForm", notYet);
            Assert.DoesNotContain("MapTerrainFloorLookupTableForm", notYet);
            Assert.DoesNotContain("MapTerrainBGLookupTableForm", notYet);
            // deferred map siblings STAY (their blocking subsystem is not in Core):
            Assert.Contains("MapSettingForm", notYet);                 // IsMapSettingEnd + CString
            Assert.Contains("ItemForm", notYet);                       // StatBooster size via PatchUtil

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

        // ====================================================================
        // slice 2h — OP/ED LZ77 stragglers (OPPrologue / EDStaffRoll / OPClassFont
        // descriptors) + SupportUnit (owner-lookahead flat IFR) + WorldMapPath
        // (per-entry computed-length sub-blocks). Descriptor-shape + version-gate
        // tests, LZ77 emit tests, dedicated-emitter tests with a near-EOF no-throw
        // case, and the NotYetPorted coverage delta.
        // ====================================================================

        // ---- OPPrologue / EDStaffRoll descriptors (FE8, version==8) ----------

        [Fact]
        public void BuildBatchDescriptors_FE8_HasOPPrologue_Block12_TwoLz77Cols_AndPalExtra()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var d = descs.Single(x => x.Name == "OPPrologue");
                Assert.Equal(12u, d.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, d.Rule);
                Assert.Equal(0u, d.RuleOffset);
                Assert.Equal(new uint[] { 0, 4 }, d.PointerIndexes);
                // Two LZ77 columns: IMG @0, TSA @4.
                Assert.Equal(2, d.SubWalks.Count);
                Assert.All(d.SubWalks, s => Assert.Equal(RebuildProducerCore.SubKind.Lz77Pointer, s.Kind));
                Assert.Equal(Address.DataTypeEnum.LZ77IMG, d.SubWalks[0].DataType);
                Assert.Equal(0u, d.SubWalks[0].EmbeddedPointerOffset);
                Assert.Equal(Address.DataTypeEnum.LZ77TSA, d.SubWalks[1].DataType);
                Assert.Equal(4u, d.SubWalks[1].EmbeddedPointerOffset);
                // Standalone palette ExtraFixedPointer (emitted once, type PAL, length 0x20).
                var ep = Assert.Single(d.ExtraFixedPointers);
                Assert.Equal(2u * 16u, ep.FixedLength);
                Assert.Equal(Address.DataTypeEnum.PAL, ep.DataType);
                Assert.Equal("OPPrologue Palette", ep.Name);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_FE8_HasEDStaffRoll_Block8_PointerAtCappedAt12()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var d = descs.Single(x => x.Name == "EDStaffRoll");
                Assert.Equal(8u, d.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, d.Rule);
                // WF rule: isPointer(u32+0) && i < 12 -> PointerAt with MaxCount 12.
                Assert.Equal(12u, d.MaxCount);
                Assert.Equal(new uint[] { 0, 4 }, d.PointerIndexes);
                Assert.Equal(2, d.SubWalks.Count);
                Assert.Equal(Address.DataTypeEnum.LZ77IMG, d.SubWalks[0].DataType);
                Assert.Equal(Address.DataTypeEnum.LZ77TSA, d.SubWalks[1].DataType);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- OPClassFont: FE8-multibyte gate ---------------------------------

        [Fact]
        public void BuildBatchDescriptors_FE8Multibyte_HasOPClassFont_Block4_OneLz77Col()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8j = MakeVersionedRom("BE8J01"); // FE8J: version 8, is_multibyte == true
                CoreState.ROM = fe8j;
                Assert.True(fe8j.RomInfo.is_multibyte);
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8j);
                var d = descs.Single(x => x.Name == "OPClassFont");
                Assert.Equal(4u, d.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.PointerAt, d.Rule);
                Assert.Equal(new uint[] { 0 }, d.PointerIndexes);
                var sw = Assert.Single(d.SubWalks);
                Assert.Equal(RebuildProducerCore.SubKind.Lz77Pointer, sw.Kind);
                Assert.Equal(Address.DataTypeEnum.LZ77IMG, sw.DataType);
                Assert.Equal(0u, sw.EmbeddedPointerOffset);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void BuildBatchDescriptors_FE8U_NonMultibyte_OmitsOPClassFont()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8u = MakeVersionedRom("BE8E01"); // FE8U: version 8, is_multibyte == false
                CoreState.ROM = fe8u;
                Assert.False(fe8u.RomInfo.is_multibyte);
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8u);
                // OPClassFontForm is FE8-multibyte ONLY (the FE8U path uses OPClassFontFE8UForm, deferred).
                Assert.DoesNotContain(descs, x => x.Name == "OPClassFont");
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- LZ77 emit through WalkAndAdd: OPPrologue shape ------------------

        [Fact]
        public void OPPrologueShape_EmitsMainIfr_TwoLz77Cols_AndPalExtra()
        {
            // Reproduce the OPPrologue descriptor with an explicit pointer (RomInfo has no setters),
            // plant one entry whose +0 image and +4 tsa are valid LZ77 streams, and assert the emitted
            // Addresses: main IFR (block*(1+1)=24), LZ77IMG @ image, LZ77TSA @ tsa, plus the PAL extra.
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0400;
            uint palettePtr = 0x0500;
            uint paletteData = 0x2000;
            uint imageData = 0x3000;
            uint tsaData = 0x3800;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(palettePtr, Ptr(paletteData));
            // entry 0: +0 -> image pointer, +4 -> tsa pointer. Entry 1 +0 NULL terminates (PointerAt).
            rom.write_u32(table + 0, Ptr(imageData));
            rom.write_u32(table + 4, Ptr(tsaData));
            uint imgLen = WriteLz77AllLiteral(rom, imageData, 80);
            uint tsaLen = WriteLz77AllLiteral(rom, tsaData, 40);

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "OPPrologue",
                PointerField = _ => pointer,
                BlockSize = 12,
                Rule = RebuildProducerCore.DataCountRule.PointerAt,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 0, 4 },
                ExtraFixedPointers = new[]
                {
                    new RebuildProducerCore.ExtraFixedPointer { PointerField = _ => palettePtr, FixedLength = 2 * 16, Name = "OPPrologue Palette", DataType = Address.DataTypeEnum.PAL },
                },
                SubWalks = new System.Collections.Generic.List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 0, Kind = RebuildProducerCore.SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "OPPrologue image" },
                    new RebuildProducerCore.SubWalk { EmbeddedPointerOffset = 4, Kind = RebuildProducerCore.SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77TSA, Name = (r, i) => "OPPrologue tsa" },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR: DataCount = 1 (entry 0 valid, entry 1 +0 NULL terminates) -> length 12*(1+1)=24.
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef && a.Info == "OPPrologue");
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(24u, main.Length);
            Assert.Equal(new uint[] { 0, 4 }, main.PointerIndexes);
            // LZ77 columns with getCompressedSize length.
            Address img = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG);
            Assert.Equal(imageData, img.Addr);
            Assert.Equal(imgLen, img.Length);
            Address tsa = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77TSA);
            Assert.Equal(tsaData, tsa.Addr);
            Assert.Equal(tsaLen, tsa.Length);
            // PAL extra (emitted once).
            Address pal = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.PAL);
            Assert.Equal(paletteData, pal.Addr);
            Assert.Equal(2u * 16u, pal.Length);
            Assert.Equal("OPPrologue Palette", pal.Info);
        }

        // ---- EmitSupportUnitAt (owner-lookahead flat IFR) -------------------

        [Fact]
        public void EmitSupportUnitAt_FirstFieldNonZero_CountsEntries_EmitsFlatIfr()
        {
            // FE7/8: block 24, first-field u16. 3 entries with non-zero first u16, then a 0/unowned row
            // terminates. No unit table planted -> the lookahead never finds an owner, so termination is
            // purely first-field-driven. DataCount = 3 -> length = 24*(3+1) = 96; pointerIndexes {}.
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0400;
            const uint block = 24;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u16(table + 0 * block, 0x0011);
            rom.write_u16(table + 1 * block, 0x0022);
            rom.write_u16(table + 2 * block, 0x0033);
            // entry 3: first u16 = 0 and no owner in the next 4 blocks -> terminates here.

            var list = new List<Address>();
            RebuildProducerCore.EmitSupportUnitAt(rom, list, pointer, block, firstFieldWidth: 2, name: "SupportUnit");

            Address main = list.Single();
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(block, main.BlockSize);
            Assert.Equal(block * (3 + 1), main.Length);
            Assert.Equal(new uint[] { }, main.PointerIndexes);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);
            Assert.Equal("SupportUnit", main.Info);
        }

        [Fact]
        public void EmitSupportUnitAt_ZeroFirstField_NullRomInfo_TerminatesWithoutLookahead()
        {
            // With a null RomInfo (synthetic CreateTestRom), the WF "飛び地" owner-lookahead cannot fire
            // — SupportUnitNavigation.GetUnitIdAtSupportAddr returns null when RomInfo == null — so a
            // zero-first-field row terminates the count walk. This verifies the count rule degrades
            // gracefully in headless/synthetic contexts (no unit table to consult). The POSITIVE
            // lookahead path (a zero-first-field row KEPT because a unit's +44 support pointer owns it)
            // is covered by SupportUnitNavigationTests.GetUnitIdAtSupportAddr_FindsOwnerFE8U.
            var rom = CreateTestRom(0x8000);
            uint supTable = 0x1000;
            uint supPtr = 0x0400;
            const uint block = 24;
            rom.write_u32(supPtr, Ptr(supTable));
            // support entries: 0 has a non-zero first field; 1 has ZERO first field — with no RomInfo
            // it is unowned -> terminator at entry 1.
            rom.write_u16(supTable + 0 * block, 0x0011);
            rom.write_u16(supTable + 1 * block, 0x0000); // zero + (no RomInfo -> ) unowned -> stop
            rom.write_u16(supTable + 2 * block, 0x0000);

            var listNoUnit = new List<Address>();
            RebuildProducerCore.EmitSupportUnitAt(rom, listNoUnit, supPtr, block, firstFieldWidth: 2, name: "SupportUnit");
            Address mainNoUnit = listNoUnit.Single();
            // entry 0 valid, entry 1 zero+unowned -> DataCount = 1 -> length 24*(1+1) = 48.
            Assert.Equal(block * (1 + 1), mainNoUnit.Length);
        }

        [Fact]
        public void EmitSupportUnitAt_NearEof_NoThrow()
        {
            // Pointer slot resolves to a base near EOF: getBlockDataCount stops at the EOF bound, the
            // first-field reads stay in bounds, and the owner lookahead is internally guarded.
            var rom = CreateTestRom(0x2000);
            uint pointer = 0x0400;
            // base near EOF (only a couple of 24-byte blocks fit before 0x2000).
            rom.write_u32(pointer, Ptr(0x1FE0));
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitSupportUnitAt(rom, list, pointer, block: 24, firstFieldWidth: 2, name: "SupportUnit"));
            Assert.Null(ex);
        }

        [Fact]
        public void EmitSupportUnitAt_ZeroBlock_EmitsNothing_AndDoesNotHang()
        {
            var rom = CreateTestRom();
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(0x1000));
            var list = new List<Address>();
            RebuildProducerCore.EmitSupportUnitAt(rom, list, pointer, block: 0, firstFieldWidth: 2, name: "SupportUnit");
            Assert.Empty(list);
        }

        [Fact]
        public void EmitSupportUnit_FE6_UsesBlock32AndFE6Name()
        {
            // The RomInfo-driven EmitSupportUnit picks block 32 / first-field u8 / "SupportUnitFE6" for
            // a version-6 ROM. On a fake all-zero FE6 ROM the base resolves unsafe -> emits nothing, but
            // must run without throwing and must NOT crash on the version branch.
            var savedRom = CoreState.ROM;
            try
            {
                var fe6 = MakeVersionedRom("AFEJ01");
                CoreState.ROM = fe6;
                Assert.Equal(6, fe6.RomInfo.version);
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitSupportUnit(fe6, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitWorldMapPathAt (per-entry computed-length sub-blocks) -------

        [Fact]
        public void EmitWorldMapPathAt_EmitsMainIfr_AndPerEntryBinAndPointerSubBlocks()
        {
            // One entry: +0 path-data pointer, +8 path-move pointer. Plant a path-data stream
            // (terminated by an x8==0xFF header) and a path-move stream (terminated by 0xFFFFFFFF).
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0400;
            uint pathData = 0x2000;
            uint moveData = 0x3000;
            rom.write_u32(pointer, Ptr(table));
            // entry 0 valid (+0 is a pointer -> PointerAt continues); entry 1 +0 NULL -> terminate.
            rom.write_u32(table + 0, Ptr(pathData));
            rom.write_u32(table + 8, Ptr(moveData));

            // path-data: one chip header (x8=1,y8=2,count=2) + 2*2 chip bytes, then a terminator header
            // (x8=0xFF). Length = 4 + 4 + 4 = 12.
            rom.write_u8(pathData + 0, 0x01); rom.write_u8(pathData + 1, 0x02); rom.write_u8(pathData + 2, 0x02); rom.write_u8(pathData + 3, 0x00);
            rom.write_u8(pathData + 4, 0xAA); rom.write_u8(pathData + 5, 0xBB); rom.write_u8(pathData + 6, 0xCC); rom.write_u8(pathData + 7, 0xDD);
            rom.write_u8(pathData + 8, 0xFF); // terminator header (x8==0xFF), consumes 4 -> total 12
            uint expectPathLen = 12;

            // path-move: two u32 then 0xFFFFFFFF terminator. Length = 4 + 4 + 4 = 12.
            rom.write_u32(moveData + 0, 0x11111111);
            rom.write_u32(moveData + 4, 0x22222222);
            rom.write_u32(moveData + 8, 0xFFFFFFFF);
            uint expectMoveLen = 12;

            var list = new List<Address>();
            RebuildProducerCore.EmitWorldMapPathAt(rom, list, pointer);

            // main IFR: DataCount = 1 -> length 12*(1+1)=24, pointerIndexes {0,8}.
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef && a.Info == "WorldMapPath");
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(24u, main.Length);
            Assert.Equal(new uint[] { 0, 8 }, main.PointerIndexes);
            // path-data BIN sub-block.
            Address bin = list.Single(a => a.DataType == Address.DataTypeEnum.BIN && a.Addr == pathData);
            Assert.Equal(expectPathLen, bin.Length);
            Assert.Equal(table + 0, bin.Pointer);
            Assert.Equal("WorldMapPath:" + U.To0xHexString(0u), bin.Info);
            // path-move POINTER sub-block.
            Address ptrBlock = list.Single(a => a.DataType == Address.DataTypeEnum.POINTER && a.Addr == moveData);
            Assert.Equal(expectMoveLen, ptrBlock.Length);
            Assert.Equal(table + 8, ptrBlock.Pointer);
            Assert.Equal("WorldMapPathMove:" + U.To0xHexString(0u), ptrBlock.Info);
        }

        [Fact]
        public void EmitWorldMapPathAt_NullSubPointers_EmitOnlyMainIfr()
        {
            // entry 0: +0 is a pointer (so PointerAt continues) but +8 is 0 -> no move sub-block; and the
            // +0 path-data pointer is planted but points at a 0-length (immediate-terminator) stream.
            var rom = CreateTestRom(0x8000);
            uint table = 0x1000;
            uint pointer = 0x0400;
            uint pathData = 0x2000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(pathData));
            rom.write_u32(table + 8, 0); // a8 == 0 -> no POINTER sub-block
            rom.write_u8(pathData + 0, 0xFF); // immediate terminator header -> CalcPathDataLength = 4

            var list = new List<Address>();
            RebuildProducerCore.EmitWorldMapPathAt(rom, list, pointer);

            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.BIN); // path-data emitted
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.POINTER); // a8==0 skipped
        }

        [Fact]
        public void EmitWorldMapPathAt_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x2000);
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(0x1FF0)); // base near EOF
            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitWorldMapPathAt(rom, list, pointer));
            Assert.Null(ex);
        }

        [Fact]
        public void EmitWorldMapPath_FE8_RunsCleanly_OnEmptyFakeRom()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitWorldMapPath(fe8, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- CalcPath{,Move}DataLength terminator walks (verbatim) ----------

        [Fact]
        public void CalcPathDataLength_StopsAtFFHeader_CountsHeadersAndChips()
        {
            var rom = CreateTestRom();
            uint addr = 0x1000;
            // header (x8=3,y8=0,count=1) + 1*2 chips -> 4+2 ; then terminator header x8=0xFF (+4). 10 total.
            rom.write_u8(addr + 0, 0x03); rom.write_u8(addr + 2, 0x01);
            rom.write_u8(addr + 4, 0x12); rom.write_u8(addr + 5, 0x34);
            rom.write_u8(addr + 6, 0xFF); // terminator header
            Assert.Equal(10u, RebuildProducerCore.CalcPathDataLength(rom, addr));
        }

        [Fact]
        public void CalcPathMoveDataLength_StopsAtFFFFFFFFTerminator()
        {
            var rom = CreateTestRom();
            uint addr = 0x1000;
            rom.write_u32(addr + 0, 0x12345678);
            rom.write_u32(addr + 4, 0xFFFFFFFF); // terminator
            Assert.Equal(8u, RebuildProducerCore.CalcPathMoveDataLength(rom, addr));
        }

        [Fact]
        public void CalcPathMoveDataLength_UnterminatedNearEof_NoThrow_ReturnsConsumed()
        {
            // An unterminated u32 stream that runs to EOF: the root+3 guard returns the consumed length
            // instead of throwing (WF's isSafetyOffset(p)-only guard would throw on the final u32).
            var rom = CreateTestRom(0x2000);
            uint addr = 0x1FF0; // (0x2000 - 0x1FF0) = 0x10 bytes = 4 u32 slots, all non-terminator
            for (uint i = 0; i < 4; i++) rom.write_u32(addr + i * 4, 0x11111111);
            uint len = 0;
            var ex = Record.Exception(() => { len = RebuildProducerCore.CalcPathMoveDataLength(rom, addr); });
            Assert.Null(ex);
            // 4 in-bounds u32 reads consumed before p+3 would exceed EOF.
            Assert.Equal(0x10u, len);
        }

        [Fact]
        public void CalcPath_UnsafeStart_ReturnsZero()
        {
            var rom = CreateTestRom(0x2000);
            Assert.Equal(0u, RebuildProducerCore.CalcPathDataLength(rom, 0x30000));     // past EOF
            Assert.Equal(0u, RebuildProducerCore.CalcPathMoveDataLength(rom, 0x30000)); // past EOF
        }

        // ---- NotYetPorted coverage delta for slice 2h -----------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2hCoveredForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2h ported these 5 — no longer in the deferred list:
            Assert.DoesNotContain("EDStaffRollForm", notYet);
            Assert.DoesNotContain("OPPrologueForm", notYet);
            Assert.DoesNotContain("OPClassFontForm", notYet);
            Assert.DoesNotContain("SupportUnitForm", notYet);
            Assert.DoesNotContain("WorldMapPathForm", notYet);

            // deferred siblings STAY (their blocking subsystem is not in Core):
            // (NOTE: OPClassDemoForm / OPClassDemoFE7Form were the nested-IFR siblings deferred at
            //  slice 2h; slice 2i below ports them, so they move to DoesNotContain there.)
            Assert.Contains("MapSettingForm", notYet);      // IsMapSettingEnd needs WF text-count cache
            Assert.Contains("WorldMapEventPointerForm", notYet); // ScanScript
            // (ImageCGFE7UForm is ported in slice 2k — see GetNotYetPortedForms_DropsSlice2kCoveredForms.)
            Assert.DoesNotContain("ImageCGFE7UForm", notYet);

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ===================================================================
        // slice 2i — SubKind.NestedIfr + OPClassDemo (FE8-mb) / OPClassDemoFE7
        // (FE7-mb). Synthetic ROMs prove the main IFR Address AND each nested
        // IFR Address have the right addr/length/pointer/type/name; the version
        // gate is proven via multibyte (present) vs non-mb/other (absent); a
        // near-EOF plant proves no throw.
        // ===================================================================

        // ---- EmitNestedIfrSub (the reusable nested count-walked IFR) ---------

        [Fact]
        public void EmitNestedIfrSub_WalksSubTable_EmitsIfrAddress_LengthPlusOne_EmptyPointerIndexes()
        {
            // A sub-table at 0x2000, block 2, rule u8(addr)!=0: 3 valid entries then a 0 terminator.
            var rom = CreateTestRom(0x8000);
            uint pfield = 0x1000;     // the embedded pointer FIELD
            uint subBase = 0x2000;    // the nested sub-table base
            rom.write_u32(pfield, Ptr(subBase));
            rom.write_u8(subBase + 0, 0x11);
            rom.write_u8(subBase + 2, 0x22);
            rom.write_u8(subBase + 4, 0x33);
            rom.write_u8(subBase + 6, 0x00); // terminator (u8(addr)==0 -> stop) -> count 3

            var list = new List<Address>();
            RebuildProducerCore.EmitNestedIfrSub(rom, list, pfield, 2,
                (i, addr) => rom.u8(addr) != 0x00, "Nested");

            Address a = Assert.Single(list);
            Assert.Equal(subBase, a.Addr);
            Assert.Equal(pfield, a.Pointer);       // pointer = the embedded FIELD
            Assert.Equal(2u, a.BlockSize);
            Assert.Equal(2u * (3u + 1u), a.Length); // subBlock * (count + 1) = 8
            Assert.Equal(Address.DataTypeEnum.InputFormRef, a.DataType);
            Assert.Empty(a.PointerIndexes);        // WF new uint[] {} -> empty
        }

        [Fact]
        public void EmitNestedIfrSub_NullEmbeddedPointer_EmitsNothing()
        {
            var rom = CreateTestRom(0x8000);
            uint pfield = 0x1000;
            rom.write_u32(pfield, 0); // embedded pointer NULL -> p32 == 0, unsafe -> emit nothing
            var list = new List<Address>();
            RebuildProducerCore.EmitNestedIfrSub(rom, list, pfield, 2,
                (i, addr) => rom.u8(addr) != 0x00, "Nested");
            Assert.Empty(list);
        }

        [Fact]
        public void EmitNestedIfrSub_ZeroBlock_EmitsNothing_DoesNotHang()
        {
            var rom = CreateTestRom(0x8000);
            uint pfield = 0x1000;
            rom.write_u32(pfield, Ptr(0x2000));
            var list = new List<Address>();
            RebuildProducerCore.EmitNestedIfrSub(rom, list, pfield, 0,
                (i, addr) => true, "Nested");
            Assert.Empty(list);
        }

        [Fact]
        public void EmitNestedIfrSub_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x2000);
            uint pfield = 0x1FFE; // pfield+3 runs past EOF -> guarded, no throw, no emit
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitNestedIfrSub(rom, list, pfield, 2,
                    (i, addr) => rom.u8(addr) != 0, "Nested"));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitNestedIfrSub_NullSubRule_Throws()
        {
            // Regression (PR #1281 review): a NestedIfr SubWalk misconfigured with a null SubRule must
            // fail loudly (ArgumentNullException) rather than NRE deep inside getBlockDataCount's
            // callback — consistent with the producer treating other invalid configs as programming errors.
            var rom = CreateTestRom(0x8000);
            uint pfield = 0x1000;
            rom.write_u32(pfield, Ptr(0x2000));
            var list = new List<Address>();
            Assert.Throws<System.ArgumentNullException>(() =>
                RebuildProducerCore.EmitNestedIfrSub(rom, list, pfield, 2, null!, "Nested"));
        }

        // ---- EmitOPClassDemoAt (FE8-multibyte): main + N1 + N2 nested IFRs ---

        [Fact]
        public void EmitOPClassDemoAt_EmitsMainIfr_CString_AndBothNestedIfrs()
        {
            var rom = CreateTestRom(0x10000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            uint className = 0x2000;  // CString target (entry +0 pointer)
            uint n1Base = 0x3000;     // N1 sub-table (entry +8 pointer)
            uint n2Base = 0x4000;     // N2 sub-table (entry +24 pointer)
            rom.write_u32(pointer, Ptr(table));

            // entry 0: u8(+0xF) <= 4 -> valid. entry 1: u8(+0xF) = 5 -> terminate -> DataCount 1.
            rom.write_u8(table + 0 * 28 + 0xF, 0x02);
            rom.write_u8(table + 1 * 28 + 0xF, 0x05);
            // embedded pointers for entry 0:
            rom.write_u32(table + 0, Ptr(className)); // +0 class-name CString
            rom.write_u32(table + 8, Ptr(n1Base));    // +8 N1 sub-table
            rom.write_u32(table + 24, Ptr(n2Base));   // +24 N2 sub-table
            // class-name string
            rom.write_u8(className + 0, (byte)'A');
            rom.write_u8(className + 1, (byte)'B');
            rom.write_u8(className + 2, 0x00); // strlen 2 -> CString length 3
            // N1 sub-table block 1, rule i>=16?false:u8(addr)!=0xFF: 4 valid then 0xFF terminator.
            rom.write_u8(n1Base + 0, 0x01);
            rom.write_u8(n1Base + 1, 0x02);
            rom.write_u8(n1Base + 2, 0x03);
            rom.write_u8(n1Base + 3, 0x04);
            rom.write_u8(n1Base + 4, 0xFF); // terminator -> N1 count 4
            // N2 sub-table block 2, rule u8(addr)!=0: 2 valid then 0 terminator.
            rom.write_u8(n2Base + 0, 0x10);
            rom.write_u8(n2Base + 2, 0x20);
            rom.write_u8(n2Base + 4, 0x00); // terminator -> N2 count 2

            var list = new List<Address>();
            RebuildProducerCore.EmitOPClassDemoAt(rom, list, pointer);

            // Main IFR: block 28, DataCount 1 -> length 28*(1+1)=56, pointerIndexes {0,8,24}.
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef && a.Info == "OPClassDemo");
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(28u, main.BlockSize);
            Assert.Equal(56u, main.Length);
            Assert.Equal(new uint[] { 0, 8, 24 }, main.PointerIndexes);

            // CString at +0: length strlen+1 = 3.
            Address cstr = list.Single(a => a.DataType == Address.DataTypeEnum.CSTRING && a.Addr == className);
            Assert.Equal(3u, cstr.Length);
            Assert.Equal(table + 0, cstr.Pointer);

            // N1 nested IFR @ +8: block 1, count 4 -> length 1*(4+1)=5, pointer = table+8.
            Address n1 = list.Single(a => a.Info == "OPClassDemo_JPName");
            Assert.Equal(n1Base, n1.Addr);
            Assert.Equal(table + 8, n1.Pointer);
            Assert.Equal(1u, n1.BlockSize);
            Assert.Equal(5u, n1.Length);
            Assert.Empty(n1.PointerIndexes);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, n1.DataType);

            // N2 nested IFR @ +24: block 2, count 2 -> length 2*(2+1)=6, pointer = table+24.
            Address n2 = list.Single(a => a.Info == "OPClassDemo_Anime");
            Assert.Equal(n2Base, n2.Addr);
            Assert.Equal(table + 24, n2.Pointer);
            Assert.Equal(2u, n2.BlockSize);
            Assert.Equal(6u, n2.Length);
            Assert.Empty(n2.PointerIndexes);
        }

        [Fact]
        public void EmitOPClassDemoAt_N1CapsAt16_EvenIfNoTerminator()
        {
            // N1 rule i>=16 -> false even if u8(addr)!=0xFF, so a sub-table with no 0xFF stops at 16.
            var rom = CreateTestRom(0x10000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            uint n1Base = 0x3000;
            uint n2Base = 0x4000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(table + 0xF, 0x00);           // entry 0 valid
            rom.write_u8(table + 28 + 0xF, 0x05);      // entry 1 terminates main
            rom.write_u32(table + 8, Ptr(n1Base));
            rom.write_u32(table + 24, Ptr(n2Base));
            // N1: 32 non-0xFF bytes (would walk forever w/o the i>=16 cap).
            for (uint k = 0; k < 32; k++) rom.write_u8(n1Base + k, 0x01);
            // N2: immediate 0 terminator -> count 0.
            rom.write_u8(n2Base + 0, 0x00);

            var list = new List<Address>();
            RebuildProducerCore.EmitOPClassDemoAt(rom, list, pointer);

            Address n1 = list.Single(a => a.Info == "OPClassDemo_JPName");
            // count capped at 16 -> length 1*(16+1)=17.
            Assert.Equal(17u, n1.Length);
            Address n2 = list.Single(a => a.Info == "OPClassDemo_Anime");
            // count 0 -> length 2*(0+1)=2.
            Assert.Equal(2u, n2.Length);
        }

        [Fact]
        public void EmitOPClassDemoAt_UnsafeJpNameOrAnime_SkipsBothNestedTables_KeepsCString()
        {
            // If either embedded pointer (+8 jpName OR +24 anime) is unsafe, WF `continue`s -> NEITHER
            // nested table is emitted, but the +0 CString (emitted BEFORE the guards) still is.
            var rom = CreateTestRom(0x10000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            uint className = 0x2000;
            uint n1Base = 0x3000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(table + 0xF, 0x00);          // entry 0 valid
            rom.write_u8(table + 28 + 0xF, 0x05);     // entry 1 terminates
            rom.write_u32(table + 0, Ptr(className)); // CString OK
            rom.write_u8(className + 0, (byte)'X'); rom.write_u8(className + 1, 0x00);
            rom.write_u32(table + 8, Ptr(n1Base));    // jpName SAFE
            rom.write_u32(table + 24, 0);             // anime NULL -> unsafe -> skip BOTH nested

            var list = new List<Address>();
            RebuildProducerCore.EmitOPClassDemoAt(rom, list, pointer);

            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.CSTRING && a.Addr == className);
            Assert.DoesNotContain(list, a => a.Info == "OPClassDemo_JPName"); // skipped (anime unsafe)
            Assert.DoesNotContain(list, a => a.Info == "OPClassDemo_Anime");
        }

        [Fact]
        public void EmitOPClassDemoAt_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x2000);
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(0x1FF0)); // base near EOF
            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitOPClassDemoAt(rom, list, pointer));
            Assert.Null(ex);
        }

        // ---- EmitOPClassDemoFE7At (FE7-multibyte): main + LZ77 + N2 + palette -

        [Fact]
        public void EmitOPClassDemoFE7At_EmitsMainIfr_CString_Lz77_N2_AndCommonPalette()
        {
            // ROM must be large enough to hold the absolute JP_FONT_PALETTE_POINTER (0x0B0038).
            var rom = CreateTestRom(0xC0000);
            uint pointer = 0x0400;
            uint table = 0x1000;
            uint className = 0x2000;  // CString target (+0)
            uint jpImg = 0x3000;      // LZ77 target (+8)
            uint n2Base = 0x5000;     // N2 sub-table (+28)
            uint paletteData = 0x70000; // common-palette target (past 0x0B0038)
            rom.write_u32(pointer, Ptr(table));

            // Build ONLY 1 real entry then make the table end via EOF would be huge; instead rely on the
            // fixed i<=0x41 rule. To keep the per-entry assertions on entry 0, plant entry 0 fully and
            // leave the rest as zeros (their CString/LZ77/anime are all NULL/unsafe -> emit nothing).
            rom.write_u32(table + 0, Ptr(className));
            rom.write_u8(className + 0, (byte)'Z'); rom.write_u8(className + 1, 0x00); // strlen 1 -> len 2
            uint imgLen = WriteLz77AllLiteral(rom, jpImg, 48);
            rom.write_u32(table + 8, Ptr(jpImg));
            rom.write_u32(table + 28, Ptr(n2Base));
            // N2 block 2, rule u8(addr)!=0: 3 valid then 0 terminator.
            rom.write_u8(n2Base + 0, 0x10);
            rom.write_u8(n2Base + 2, 0x20);
            rom.write_u8(n2Base + 4, 0x30);
            rom.write_u8(n2Base + 6, 0x00); // -> N2 count 3
            // common palette: absolute JP_FONT_PALETTE_POINTER = 0x0B0038 holds a pointer to paletteData.
            rom.write_u32(0x0B0038, Ptr(paletteData));

            var list = new List<Address>();
            RebuildProducerCore.EmitOPClassDemoFE7At(rom, list, pointer);

            // Main IFR: block 32, DataCount = 0x42 (i<=0x41) -> length 32*(0x42+1).
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef && a.Info == "OPClassDemo");
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(32u, main.BlockSize);
            Assert.Equal(32u * (0x42u + 1u), main.Length);
            Assert.Equal(new uint[] { 0, 8, 28 }, main.PointerIndexes);

            // entry 0 CString @ +0.
            Address cstr = list.Single(a => a.DataType == Address.DataTypeEnum.CSTRING && a.Addr == className);
            Assert.Equal(2u, cstr.Length);
            Assert.Equal(table + 0, cstr.Pointer);

            // entry 0 LZ77 @ +8: name "OPClassDemo_Anime_<0>_JP_NAME_IMG", real compressed length.
            Address lz = list.Single(a => a.DataType == Address.DataTypeEnum.LZ77IMG && a.Addr == jpImg);
            Assert.Equal(imgLen, lz.Length);
            Assert.Equal(table + 8, lz.Pointer);
            Assert.Equal("OPClassDemo_Anime_" + U.ToHexString(0u) + "_JP_NAME_IMG", lz.Info);

            // entry 0 N2 nested IFR @ +28: block 2, count 3 -> length 2*(3+1)=8.
            Address n2 = list.Single(a => a.Info == "OPClassDemo_Anime_" + U.ToHexString(0u) + "_Anime");
            Assert.Equal(n2Base, n2.Addr);
            Assert.Equal(table + 28, n2.Pointer);
            Assert.Equal(2u, n2.BlockSize);
            Assert.Equal(8u, n2.Length);
            Assert.Empty(n2.PointerIndexes);

            // trailing common-palette pointer: AddPointer(0x0B0038, 2*16, PAL).
            Address pal = list.Single(a => a.Info == "OPClassDemo_CommonPalette");
            Assert.Equal(paletteData, pal.Addr);
            Assert.Equal(2u * 16u, pal.Length);
            Assert.Equal(0x0B0038u, pal.Pointer);
            Assert.Equal(Address.DataTypeEnum.PAL, pal.DataType);
        }

        [Fact]
        public void EmitOPClassDemoFE7At_MissingMainTable_StillEmitsCommonPalette()
        {
            // WF emits the trailing common-palette AddPointer OUTSIDE the main block (unconditional),
            // so even a missing main table still emits it.
            var rom = CreateTestRom(0xC0000); // large enough for the absolute JP_FONT_PALETTE_POINTER
            uint pointer = 0x0400;
            rom.write_u32(pointer, 0); // base NULL -> no main IFR
            uint paletteData = 0x70000;
            rom.write_u32(0x0B0038, Ptr(paletteData));

            var list = new List<Address>();
            RebuildProducerCore.EmitOPClassDemoFE7At(rom, list, pointer);

            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.InputFormRef && a.Info == "OPClassDemo");
            Address pal = list.Single(a => a.Info == "OPClassDemo_CommonPalette");
            Assert.Equal(paletteData, pal.Addr);
        }

        [Fact]
        public void EmitOPClassDemoFE7At_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x2000);
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(0x1FF0)); // base near EOF
            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitOPClassDemoFE7At(rom, list, pointer));
            Assert.Null(ex);
        }

        // ---- version gate: OPClassDemo present on multibyte, absent otherwise ----

        [Fact]
        public void MakeAllStructPointers_FE8Multibyte_EmitsOPClassDemo()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8j = MakeVersionedRom("BE8J01"); // FE8J: version 8, is_multibyte == true
                CoreState.ROM = fe8j;
                Assert.True(fe8j.RomInfo.is_multibyte);
                var result = RebuildProducerCore.MakeAllStructPointers(fe8j);
                // On a blank fake ROM the op_class_demo_pointer slot is 0 -> the main IFR is skipped, but
                // the emitter still RUNS without throwing (proven by FE8 gate firing). Coverage bookkeeping
                // proves the FORM is no longer deferred (the gate is reached).
                Assert.DoesNotContain("OPClassDemoForm", result.NotYetPorted);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitOPClassDemo_FE8Multibyte_RunsCleanly_OnEmptyFakeRom()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8j = MakeVersionedRom("BE8J01");
                CoreState.ROM = fe8j;
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitOPClassDemo(fe8j, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitOPClassDemoFE7_FE7Multibyte_RunsCleanly_OnEmptyFakeRom()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe7j = MakeVersionedRom("AE7J01"); // FE7J: version 7, is_multibyte == true
                CoreState.ROM = fe7j;
                Assert.True(fe7j.RomInfo.is_multibyte);
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitOPClassDemoFE7(fe7j, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- NotYetPorted coverage delta for slice 2i -----------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2iCoveredForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2i ported these 2 — no longer in the deferred list:
            Assert.DoesNotContain("OPClassDemoForm", notYet);
            Assert.DoesNotContain("OPClassDemoFE7Form", notYet);

            // deferred siblings STAY (their blocking subsystem is not in Core):
            Assert.Contains("MapSettingForm", notYet);           // IsMapSettingEnd needs WF text-count cache
            Assert.Contains("WorldMapEventPointerForm", notYet); // ScanScript
            Assert.Contains("MonsterWMapProbabilityForm", notYet); // ScanScript skirmish events
            // (ImageCGFE7UForm is ported in slice 2k — see GetNotYetPortedForms_DropsSlice2kCoveredForms.)
            Assert.DoesNotContain("ImageCGFE7UForm", notYet);
            // (FE8SpellMenuExtendsForm is ported in slice 2m — see
            //  GetNotYetPortedForms_DropsSlice2mCoveredForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("FE8SpellMenuExtendsForm", notYet);

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ====================================================================
        // slice 2j — misc self-contained stragglers (MapTerrain lookup tables,
        // MapPointer, ExtraUnit FE8J/FE8U + the shared RecycleOldUnits walk).
        // ====================================================================

        // ---- EmitMapTerrainLookupAt (one flat block-1 IFR per non-zero pointer) ----

        [Fact]
        public void EmitMapTerrainLookupAt_OneIfrPerNonZeroPointer_NameCarriesIndex_FixedCount()
        {
            // map_terrain_type_count drives the FixedCount walk (block 1). Two non-zero pointer slots
            // (index 0 and 2) plus a zero slot (index 1, skipped) -> 2 IFR Addresses, names ...00 / ...02.
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint count = fe8.RomInfo.map_terrain_type_count; // 65 on FE8U.

                uint slot0 = 0x100000, slot2 = 0x100100;
                uint table0 = 0x101000, table2 = 0x102000;
                fe8.write_u32(slot0, Ptr(table0));
                fe8.write_u32(slot2, Ptr(table2));
                uint[] pointers = { slot0, 0u, slot2 };

                var list = new List<Address>();
                RebuildProducerCore.EmitMapTerrainLookupAt(fe8, list, pointers, isFloor: false);

                Assert.Equal(2, list.Count);
                Address a0 = list.Single(a => a.Info == "MapTerrainBGLookupTable" + U.ToHexString(0));
                Assert.Equal(table0, a0.Addr);
                Assert.Equal(slot0, a0.Pointer);
                Assert.Equal(1u, a0.BlockSize);
                Assert.Equal(1u * (count + 1), a0.Length); // block 1 * (FixedCount + 1)
                Assert.Equal(Address.DataTypeEnum.InputFormRef, a0.DataType);
                Assert.Empty(a0.PointerIndexes);

                Address a2 = list.Single(a => a.Info == "MapTerrainBGLookupTable" + U.ToHexString(2));
                Assert.Equal(table2, a2.Addr);
                Assert.Equal(slot2, a2.Pointer);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitMapTerrainLookupAt_FloorUsesFloorName_AndSkipsZeroAndUnsafe()
        {
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint slot = 0x100000;
                fe8.write_u32(slot, Ptr(0x101000));
                // index 0 = a slot whose target is BELOW 0x200 (unsafe) -> skipped; index 1 = valid.
                uint badSlot = 0x100200;
                fe8.write_u32(badSlot, Ptr(0x100)); // target < 0x200 -> isSafetyOffset false
                uint[] pointers = { badSlot, slot };

                var list = new List<Address>();
                RebuildProducerCore.EmitMapTerrainLookupAt(fe8, list, pointers, isFloor: true);

                Address a = Assert.Single(list);
                Assert.Equal("MapTerrainFloorLookupTable" + U.ToHexString(1), a.Info);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitMapTerrainLookupAt_NullArray_EmitsNothing()
        {
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                var list = new List<Address>();
                RebuildProducerCore.EmitMapTerrainLookupAt(fe8, list, null, isFloor: false);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitMapTerrainLookupAt_PointerSlotNearEof_DoesNotThrow()
        {
            // A pointer slot at EOF-3 would make p32 read past the end; the slot+3 guard must skip it.
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint nearEof = (uint)fe8.Data.Length - 2; // slot+3 > Length
                var list = new List<Address>();
                var ex = Record.Exception(() =>
                    RebuildProducerCore.EmitMapTerrainLookupAt(fe8, list, new uint[] { nearEof }, isFloor: false));
                Assert.Null(ex);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitMapPointer (the 6 MAPPOINTERS IFR tables) -----------------

        [Fact]
        public void EmitMapPointer_EmitsSixMappointersTables_Block4_PI0_VanillaLimit()
        {
            // Plant all 6 FE8U map-pointer slots pointing at ONE shared base (vanilla, NOT split) so
            // IsPlistSplits()==false -> limit = map_map_pointer_list_default_size (0xEC). Each table's
            // IsDataExists is index-only (i==0||i<limit), so DataCount == limit and length == 4*(limit+1).
            // No map settings exist -> the per-map sweep adds nothing; only the 6 main tables are emitted.
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint sharedBase = 0x200000;
                uint[] slots =
                {
                    fe8.RomInfo.map_config_pointer, fe8.RomInfo.map_tileanime1_pointer,
                    fe8.RomInfo.map_obj_pointer, fe8.RomInfo.map_map_pointer_pointer,
                    fe8.RomInfo.map_event_pointer, fe8.RomInfo.map_mapchange_pointer,
                };
                foreach (uint s in slots) fe8.write_u32(U.toOffset(s), Ptr(sharedBase));

                var list = new List<Address>();
                RebuildProducerCore.EmitMapPointer(fe8, list);

                uint limit = fe8.RomInfo.map_map_pointer_list_default_size; // 0xEC, since not split.
                uint expectedLen = 4 * (limit + 1);
                foreach (string name in new[]
                {
                    "MAPPOINTERS", "MAPPOINTERS_ANIMATION", "MAPPOINTERS_OBJECT",
                    "MAPPOINTERS_MAP", "MAPPOINTERS_EVENT", "MAPPOINTERS_CHANGE",
                })
                {
                    Address a = Assert.Single(list, x => x.Info == name);
                    Assert.Equal(sharedBase, a.Addr);
                    Assert.Equal(4u, a.BlockSize);
                    Assert.Equal(expectedLen, a.Length);
                    Assert.Equal(Address.DataTypeEnum.InputFormRef, a.DataType);
                    Assert.Equal(new uint[] { 0 }, a.PointerIndexes);
                }
                // FE8 (version 8) has NO MAPPOINTERS_WMAP_EVENT (that alias is FE6-only).
                Assert.DoesNotContain(list, x => x.Info == "MAPPOINTERS_WMAP_EVENT");
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitMapPointer_EmptyVersionedRom_EmitsNothing_DoesNotThrow()
        {
            // All map-pointer slots are 0 in an all-zero ROM -> every table base resolves unsafe -> the
            // emitter adds nothing and never throws (no NRE on RomInfo, no read past EOF).
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitMapPointer(fe8, list));
                Assert.Null(ex);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitExtraUnitAt (FE8J: direct base, flag BINs, RecycleOldUnits) ----

        [Fact]
        public void EmitExtraUnitAt_FE8J_MainIfrNotFoundPointer_PerEntryUnitsAndFlags()
        {
            // FE8J: BaseAddress is a DIRECT address (ReInit), BasePointer 0 -> NOT_FOUND. block 4, rule
            // isSafetyPointer(u32(addr)). 2 valid entries (each points to a script-pointer), then a NULL
            // 3rd -> DataCount 2. Each entry expands via RecycleOldUnits; the flags are at the absolute
            // FE8J locations (out of this synthetic data's range -> the flag AddAddress safely no-ops).
            var fe8 = MakeVersionedRom("BE8J01"); // FE8J = multibyte
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint block = fe8.RomInfo.eventunit_data_size; // 20 on FE8.

                uint baseAddr = 0x300000;
                // Each ExtraUnit entry's +0 field is the "script pointer" RecycleOldUnits reads. WF:
                // script_addr = u32(script_pointer) IS the EventUnit IFR base (ReInitPointer -> BaseAddress
                // = p32(field)). So the entry holds Ptr(unitList) directly; the unit ids live at unitList.
                uint unitList0 = 0x311000, unitList1 = 0x321000;
                fe8.write_u32(baseAddr + 0, Ptr(unitList0));
                fe8.write_u32(baseAddr + 4, Ptr(unitList1));
                // entry 2 = NULL -> isSafetyPointer(0) false -> terminates (DataCount 2).
                fe8.write_u8(unitList0 + 0, 0x10); // unit id != 0 -> 1 entry, then 0 terminator at +block.
                fe8.write_u8(unitList1 + 0, 0x11);

                var list = new List<Address>();
                RebuildProducerCore.EmitExtraUnitAt(fe8, list, baseAddr);

                // Main IFR: addr = baseAddr, pointer = NOT_FOUND, block 4, length 4*(2+1)=12, PI {}.
                Address main = Assert.Single(list, a => a.Info == "ExtraUnit");
                Assert.Equal(baseAddr, main.Addr);
                Assert.Equal(U.NOT_FOUND, main.Pointer);
                Assert.Equal(4u, main.BlockSize);
                Assert.Equal(12u, main.Length);
                Assert.Empty(main.PointerIndexes);

                // Two RecycleOldUnits EVENT UNIT IFRs (one per entry), base = u32(entry+0), block 20. Each
                // has 1 unit + a 0 terminator -> length block*2. pointer = the entry field (baseAddr+i*4).
                var euList = list.Where(a => a.Info == "ExtraUnit EVENT UNIT").ToList();
                Assert.Equal(2, euList.Count);
                Assert.Contains(euList, a => a.Addr == unitList0 && a.BlockSize == block
                    && a.Length == block * 2 && a.Pointer == baseAddr + 0);
                Assert.Contains(euList, a => a.Addr == unitList1 && a.BlockSize == block
                    && a.Pointer == baseAddr + 4);
                // FE8 (v8) main EVENT UNIT IFR carries pointerIndexes {8}.
                Assert.All(euList, a => Assert.Equal(new uint[] { 8 }, a.PointerIndexes));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitExtraUnitAt_FE8J_UnsafeBase_EmitsNothing_AndNearEofDoesNotThrow()
        {
            var fe8 = MakeVersionedRom("BE8J01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                var list = new List<Address>();
                // base below 0x200 -> unsafe -> nothing.
                RebuildProducerCore.EmitExtraUnitAt(fe8, list, 0x100);
                Assert.Empty(list);

                // base near EOF with an all-non-null run -> must not throw.
                uint nearEof = (uint)fe8.Data.Length - 4;
                var ex = Record.Exception(() => RebuildProducerCore.EmitExtraUnitAt(fe8, list, nearEof));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitExtraUnitFE8UAt (FE8U: pointer-slot base, block 8, +4 field) ----

        [Fact]
        public void EmitExtraUnitFE8UAt_FE8U_MainIfrSafePointer_PerEntryUnitsNoFlag()
        {
            // FE8U: BasePointer = the slot (safe), BaseAddress = p32(slot). block 8, rule
            // isSafetyPointer(u32(addr+4)). 2 entries then a NULL +4 -> DataCount 2. Each expands via
            // RecycleOldUnits(addr+4). NO flag block (the +0 field is in-table).
            var fe8 = MakeVersionedRom("BE8E01"); // FE8U = non-multibyte
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint block8 = 8;
                uint euBlock = fe8.RomInfo.eventunit_data_size; // 20.

                uint slot = 0x300000;
                uint tableBase = 0x310000;
                fe8.write_u32(slot, Ptr(tableBase));

                // The +4 field of each 8-byte entry IS the RecycleOldUnits script pointer; u32(+4) is the
                // EventUnit IFR base directly (ReInitPointer). So +4 holds Ptr(unitList); the +0 field is
                // the in-table flag (unused by the producer). entry 2 +4 = NULL -> DataCount 2.
                uint unitList0 = 0x321000, unitList1 = 0x331000;
                fe8.write_u32(tableBase + 0 + 4, Ptr(unitList0));
                fe8.write_u32(tableBase + 8 + 4, Ptr(unitList1));
                fe8.write_u8(unitList0, 0x10);
                fe8.write_u8(unitList1, 0x11);

                var list = new List<Address>();
                RebuildProducerCore.EmitExtraUnitFE8UAt(fe8, list, slot);

                // Main IFR: addr = tableBase, pointer = slot (safe), block 8, length 8*(2+1)=24, PI {}.
                Address main = Assert.Single(list, a => a.Info == "ExtraUnit");
                Assert.Equal(tableBase, main.Addr);
                Assert.Equal(U.toOffset(slot), main.Pointer);
                Assert.Equal(block8, main.BlockSize);
                Assert.Equal(24u, main.Length);
                Assert.Empty(main.PointerIndexes);

                // Two EVENT UNIT IFRs, base = u32(entry+4), pointer = the +4 field, block 20.
                var euList = list.Where(a => a.Info == "ExtraUnit EVENT UNIT").ToList();
                Assert.Equal(2, euList.Count);
                Assert.Contains(euList, a => a.Addr == unitList0 && a.BlockSize == euBlock
                    && a.Pointer == tableBase + 0 + 4);
                Assert.Contains(euList, a => a.Addr == unitList1 && a.BlockSize == euBlock
                    && a.Pointer == tableBase + 8 + 4);

                // No "ExtraUnit Flag" BIN for FE8U.
                Assert.DoesNotContain(list, a => a.Info == "ExtraUnit Flag");
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitExtraUnitFE8UAt_FE8U_UnsafeSlot_EmitsNothing_AndNearEofDoesNotThrow()
        {
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                var list = new List<Address>();
                RebuildProducerCore.EmitExtraUnitFE8UAt(fe8, list, 0x100); // slot < 0x200 -> nothing.
                Assert.Empty(list);

                uint nearEof = (uint)fe8.Data.Length - 2; // slot+3 > Length -> skip.
                var ex = Record.Exception(() => RebuildProducerCore.EmitExtraUnitFE8UAt(fe8, list, nearEof));
                Assert.Null(ex);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- EmitRecycleOldUnits (the EventUnit IFR + v8 COORD sub-blocks) ----

        [Fact]
        public void EmitRecycleOldUnits_FE8_EmitsIfrWithPI8_AndCoordBinPerEntryWithAfterCoords()
        {
            // v8: main IFR {8} + per entry with u8(addr+7)>0 a count*8 BIN at p32(addr+8).
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                uint block = fe8.RomInfo.eventunit_data_size; // 20.

                uint scriptPointer = 0x400000;
                uint unitBase = 0x410000;
                fe8.write_u32(scriptPointer, Ptr(unitBase));

                // entry 0: unit id != 0 (counted); u8(+7) = 3 after-coords -> p32(+8) -> 3*8=24 BIN.
                uint coords0 = 0x420000;
                fe8.write_u8(unitBase + 0, 0x10);
                fe8.write_u8(unitBase + 7, 3);
                fe8.write_u32(unitBase + 8, Ptr(coords0));
                // entry 1: unit id != 0; u8(+7) = 0 -> NO coord block.
                fe8.write_u8(unitBase + block + 0, 0x11);
                fe8.write_u8(unitBase + block + 7, 0);
                // entry 2: unit id == 0 -> terminator (DataCount 2).

                var list = new List<Address>();
                RebuildProducerCore.EmitRecycleOldUnits(fe8, list, "ExtraUnit", scriptPointer);

                // Main EVENT UNIT IFR: addr = unitBase, pointer = scriptPointer, block 20, length 20*(2+1)=60, {8}.
                Address main = Assert.Single(list, a => a.Info == "ExtraUnit EVENT UNIT");
                Assert.Equal(unitBase, main.Addr);
                Assert.Equal(U.toOffset(scriptPointer), main.Pointer);
                Assert.Equal(block, main.BlockSize);
                Assert.Equal(block * 3, main.Length);
                Assert.Equal(new uint[] { 8 }, main.PointerIndexes);

                // One COORD BIN for entry 0 only (entry 1 had count 0).
                Address coord = Assert.Single(list, a => a.Info == "ExtraUnit EVENT UNIT COORD 0");
                Assert.Equal(coords0, coord.Addr);
                Assert.Equal(24u, coord.Length); // 3 * 8
                Assert.Equal(unitBase + 8, coord.Pointer);
                Assert.Equal(Address.DataTypeEnum.BIN, coord.DataType);
                Assert.DoesNotContain(list, a => a.Info == "ExtraUnit EVENT UNIT COORD 1");
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitRecycleOldUnits_FE7_EmitsSingleIfrWithEmptyPI_NoCoordBlocks()
        {
            // v<=7: a single EVENT UNIT IFR with EMPTY pointerIndexes and NO per-entry COORD blocks.
            var fe7 = MakeVersionedRom("AE7E01"); // FE7U (version 7)
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe7;
                Assert.True(fe7.RomInfo.version <= 7);
                uint block = fe7.RomInfo.eventunit_data_size; // 16 on FE7.

                uint scriptPointer = 0x400000;
                uint unitBase = 0x410000;
                fe7.write_u32(scriptPointer, Ptr(unitBase));
                fe7.write_u8(unitBase + 0, 0x10);          // entry 0 counted
                // entry 1 = 0 -> terminator (DataCount 1).

                var list = new List<Address>();
                RebuildProducerCore.EmitRecycleOldUnits(fe7, list, "ExtraUnit", scriptPointer);

                Address main = Assert.Single(list); // ONLY the main IFR (no COORD walk on v<=7).
                Assert.Equal("ExtraUnit EVENT UNIT", main.Info);
                Assert.Equal(unitBase, main.Addr);
                Assert.Equal(block, main.BlockSize);
                Assert.Equal(block * 2, main.Length); // 1 entry + terminator
                Assert.Empty(main.PointerIndexes);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmitRecycleOldUnits_NullOrUnsafeScriptPointer_EmitsNothing_AndNearEofDoesNotThrow()
        {
            var fe8 = MakeVersionedRom("BE8E01");
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = fe8;
                var list = new List<Address>();

                // script pointer field holds 0 (not a pointer) -> nothing.
                uint scriptPointer = 0x400000;
                fe8.write_u32(scriptPointer, 0);
                RebuildProducerCore.EmitRecycleOldUnits(fe8, list, "ExtraUnit", scriptPointer);
                Assert.Empty(list);

                // unit base near EOF with an all-non-zero run -> must not throw.
                uint scriptPointer2 = 0x400100;
                uint nearEof = (uint)fe8.Data.Length - 4;
                fe8.write_u32(scriptPointer2, Ptr(nearEof));
                for (uint i = nearEof; i < (uint)fe8.Data.Length; i++) fe8.write_u8(i, 0x10);
                var ex = Record.Exception(() =>
                    RebuildProducerCore.EmitRecycleOldUnits(fe8, list, "ExtraUnit", scriptPointer2));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- NotYetPorted coverage delta for slice 2j ----------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2jCoveredForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2j ported these 5 (or 4 forms; ExtraUnit is a FE8J/FE8U split) — no longer deferred:
            Assert.DoesNotContain("MapTerrainFloorLookupTableForm", notYet);
            Assert.DoesNotContain("MapTerrainBGLookupTableForm", notYet);
            Assert.DoesNotContain("MapPointerForm", notYet);
            Assert.DoesNotContain("ExtraUnitForm", notYet);
            Assert.DoesNotContain("ExtraUnitFE8UForm", notYet);

            // Still deferred (real missing-Core blocker) -> must REMAIN:
            Assert.Contains("SongTableForm", notYet);                   // SongUtil.ParseTrack/RecycleOldInstrument
            Assert.Contains("EventUnitForm(RecycleReserveUnits)", notYet); // NewAllocData = editor session state
            Assert.Contains("SoundRoomForm", notYet);                   // FE7 CString sub-walk + MIX type
            Assert.Contains("ItemForm", notYet);                        // StatBooster size via PatchUtil
            Assert.Contains("MapSettingForm", notYet);                  // IsMapSettingEnd text-count cache

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // =====================================================================
        // slice 2k — header-TSA image forms
        // =====================================================================

        /// <summary>Plant a 2-byte {x,y} header-TSA stream at <paramref name="offset"/>; returns the
        /// expected byte length 2 + (x+1)*(y+1)*2 (the body cells are left zero — only the length is
        /// asserted).</summary>
        static uint WriteHeaderTsa(ROM rom, uint offset, byte x, byte y)
        {
            rom.write_u8(offset + 0, x);
            rom.write_u8(offset + 1, y);
            return 2u + ((uint)x + 1) * ((uint)y + 1) * 2;
        }

        [Fact]
        public void CalcHeaderTsaLength_PlantedHeader_MatchesWFFormula()
        {
            var rom = CreateTestRom(0x8000);
            uint pos = 0x1000;
            uint expected = WriteHeaderTsa(rom, pos, 0x07, 0x03); // (7+1)*(3+1) = 32 cells -> 2 + 32*2 = 66
            Assert.Equal(66u, expected);
            Assert.Equal(expected, RebuildProducerCore.CalcHeaderTsaLength(rom, pos));
        }

        [Fact]
        public void CalcHeaderTsaLength_NearEOF_ReturnsZero_NoThrow()
        {
            var rom = CreateTestRom(0x8000);
            // pos + 2 >= Length -> degenerate (WF returns 0). Length == 0x8000.
            Assert.Equal(0u, RebuildProducerCore.CalcHeaderTsaLength(rom, 0x8000 - 2)); // pos+2 == Length -> >= -> 0
            Assert.Equal(0u, RebuildProducerCore.CalcHeaderTsaLength(rom, 0x8000 - 1));
            // A header ending one byte before EOF is still valid (pos+2 < Length).
            uint expected = WriteHeaderTsa(rom, 0x8000 - 3, 0x00, 0x00); // 2 + 1*1*2 = 4
            Assert.Equal(4u, expected);
            Assert.Equal(4u, RebuildProducerCore.CalcHeaderTsaLength(rom, 0x8000 - 3));
        }

        [Fact]
        public void EmitHeaderTsaPointer_EmitsHEADERTSA_WithCalcLength()
        {
            var rom = CreateTestRom(0x8000);
            uint tsaData = 0x2000;
            uint expected = WriteHeaderTsa(rom, tsaData, 0x0F, 0x01); // (15+1)*(1+1)=32 -> 2 + 32*2 = 66
            Assert.Equal(66u, expected);
            uint pointerSlot = 0x1000;
            rom.write_u32(pointerSlot, Ptr(tsaData));

            var list = new List<Address>();
            RebuildProducerCore.EmitHeaderTsaPointer(rom, list, pointerSlot, "BG 0x00 TSA");

            Assert.Single(list);
            Address a = list[0];
            Assert.Equal(tsaData, a.Addr);
            Assert.Equal(expected, a.Length);
            Assert.Equal(pointerSlot, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.HEADERTSA, a.DataType);
            Assert.Equal("BG 0x00 TSA", a.Info);
        }

        [Fact]
        public void EmitHeaderTsaPointer_PointerSlotNearEOF_EmitsNothing_NoThrow()
        {
            var rom = CreateTestRom(0x8000);
            var list = new List<Address>();
            // pointer slot in the last 3 bytes: u32(pointer) would read past EOF -> guarded, emits nothing.
            Exception ex = Record.Exception(() =>
                RebuildProducerCore.EmitHeaderTsaPointer(rom, list, 0x8000 - 2, "X"));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitHeaderTsaPointer_UnsafeTarget_EmitsNothing()
        {
            var rom = CreateTestRom(0x8000);
            uint pointerSlot = 0x1000;
            rom.write_u32(pointerSlot, 0x00000000); // NULL target -> isSafetyPointer false
            var list = new List<Address>();
            RebuildProducerCore.EmitHeaderTsaPointer(rom, list, pointerSlot, "X");
            Assert.Empty(list);
        }

        [Fact]
        public void CalcRomTcsLength_PlantedTerminator_MatchesWFFormula()
        {
            var rom = CreateTestRom(0x8000);
            uint addr = 0x1000;
            // Plant the shortest terminator pattern (index 4: {00 00 FF FF 10 00}, plusOffset 4) at +0x40.
            byte[] term = new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x10, 0x00 };
            for (int i = 0; i < term.Length; i++) rom.write_u8(addr + 0x40 + (uint)i, term[i]);
            // length = (matchAddr + plusOffset) - addr = (addr+0x40 + 4) - addr = 0x44.
            Assert.Equal(0x44u, RebuildProducerCore.CalcRomTcsLength(rom, addr));
        }

        [Fact]
        public void CalcRomTcsLength_NoTerminator_ReturnsZero()
        {
            var rom = CreateTestRom(0x8000);
            // A region of all-0xAA never matches any terminator pattern -> 0.
            uint addr = 0x1000;
            for (uint i = 0; i < 0x100; i++) rom.write_u8(addr + i, 0xAA);
            Assert.Equal(0u, RebuildProducerCore.CalcRomTcsLength(rom, addr));
        }

        [Fact]
        public void EmitRomTcsPointer_EmitsROMTCS_WithCalcLength()
        {
            var rom = CreateTestRom(0x8000);
            uint romtcsData = 0x2000;
            byte[] term = new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x10, 0x00 };
            for (int i = 0; i < term.Length; i++) rom.write_u8(romtcsData + 0x10 + (uint)i, term[i]);
            uint expected = (0x10u + 4u); // (match + plusOffset) - addr = (data+0x10 + 4) - data = 0x14
            uint pointerSlot = 0x1000;
            rom.write_u32(pointerSlot, Ptr(romtcsData));

            var list = new List<Address>();
            RebuildProducerCore.EmitRomTcsPointer(rom, list, pointerSlot, "Border ROMTCS");

            Assert.Single(list);
            Address a = list[0];
            Assert.Equal(romtcsData, a.Addr);
            Assert.Equal(expected, a.Length);
            Assert.Equal(pointerSlot, a.Pointer);
            Assert.Equal(Address.DataTypeEnum.ROMTCS, a.DataType);
        }

        [Fact]
        public void EmitRomTcsPointer_PointerSlotNearEOF_EmitsNothing_NoThrow()
        {
            var rom = CreateTestRom(0x8000);
            var list = new List<Address>();
            Exception ex = Record.Exception(() =>
                RebuildProducerCore.EmitRomTcsPointer(rom, list, 0x8000 - 1, "X"));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitImageBGAt_NormalEntry_EmitsMainIfrAndPerEntryColumns()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // One valid entry (block 12) + a terminator row (both pointers 0 -> NULL is pointerOrNULL,
            // so IsValidEntry passes; we end the table with a non-pointer-or-NULL +0 to stop the walk).
            // Entry 0: +0 LZ77 image, +4 HEADER-TSA, +8 palette.
            uint img = 0x2000;
            uint lz77len = WriteLz77AllLiteral(rom, img, 64);
            uint tsa = 0x3000;
            uint tsaLen = WriteHeaderTsa(rom, tsa, 0x07, 0x03); // 66
            uint pal = 0x4000;
            rom.write_u32(table + 0, Ptr(img));
            rom.write_u32(table + 4, Ptr(tsa));
            rom.write_u32(table + 8, Ptr(pal));
            // Terminator entry 1: +0 a non-pointer-non-NULL value (0x00000005) -> isPointerOrNULL false.
            rom.write_u32(table + 12 + 0, 0x00000005);
            rom.write_u32(table + 12 + 4, 0x00000005);

            var list = new List<Address>();
            RebuildProducerCore.EmitImageBGAt(rom, list, pointer);

            // Main IFR: base=table, block 12, count 1 -> length 12*(1+1)=24, PI {0,4,8}.
            Address main = list.FirstOrDefault(a => a.Addr == table && a.Info == "BG");
            Assert.NotNull(main);
            Assert.Equal(24u, main.Length);
            Assert.Equal(12u, main.BlockSize);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);

            // Per-entry columns.
            Assert.Contains(list, a => a.Addr == img && a.Length == lz77len
                && a.DataType == Address.DataTypeEnum.LZ77IMG && a.Info == "BG 0x00 IMAGE");
            Assert.Contains(list, a => a.Addr == tsa && a.Length == tsaLen
                && a.DataType == Address.DataTypeEnum.HEADERTSA && a.Info == "BG 0x00 TSA");
            Assert.Contains(list, a => a.Addr == pal && a.Length == 0x20 * 8
                && a.DataType == Address.DataTypeEnum.PAL && a.Info == "BG 0x00 PALETTE");
        }

        [Fact]
        public void EmitImageCGAt_TenSplitEntry_EmitsImageArrayHeaderTsaAndPalette()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // Entry 0 (block 12): +0 -> a 10-image-pointer array (each a valid LZ77 image), +4 HEADER-TSA,
            // +8 palette. The NestedPointer rule needs u32(addr+0)=ptr AND u32(toOffset(ptr))=ptr.
            uint imgArray = 0x2000;       // the 10-pointer array
            uint firstImg = 0x3000;
            uint lz0 = WriteLz77AllLiteral(rom, firstImg, 32);
            // Point each of the 10 array slots somewhere valid; slot 0 must itself be a pointer (the rule
            // reads u32(toOffset(u32(addr+0))) = u32(imgArray) and wants a pointer).
            for (int n = 0; n < 10; n++)
            {
                rom.write_u32(imgArray + (uint)n * 4, Ptr(firstImg)); // all point at the same valid LZ77
            }
            uint tsa = 0x4000;
            uint tsaLen = WriteHeaderTsa(rom, tsa, 0x0F, 0x0F); // (16*16)=256 -> 2 + 256*2 = 514
            uint pal = 0x5000;
            rom.write_u32(table + 0, Ptr(imgArray));
            rom.write_u32(table + 4, Ptr(tsa));
            rom.write_u32(table + 8, Ptr(pal));
            // Terminator entry 1: +0 not a pointer -> rule false.
            rom.write_u32(table + 12 + 0, 0x00000000);

            var list = new List<Address>();
            RebuildProducerCore.EmitImageCGAt(rom, list, pointer);

            Address main = list.FirstOrDefault(a => a.Addr == table && a.Info == "CG");
            Assert.NotNull(main);
            Assert.Equal(24u, main.Length); // block 12 * (1 + 1)
            Assert.Equal(12u, main.BlockSize);

            // 10 LZ77 image columns (all at firstImg here), the 4*10 POINTER header, the HEADER-TSA, the PAL.
            int lzCount = list.Count(a => a.Addr == firstImg && a.DataType == Address.DataTypeEnum.LZ77IMG
                && a.Info.StartsWith("CG 0x00 IMAGE@"));
            Assert.Equal(10, lzCount);
            Assert.Contains(list, a => a.Addr == imgArray && a.Length == 4 * 10
                && a.DataType == Address.DataTypeEnum.POINTER && a.Info == "CG 0x00 IMAGE_HEADER");
            Assert.Contains(list, a => a.Addr == tsa && a.Length == tsaLen
                && a.DataType == Address.DataTypeEnum.HEADERTSA && a.Info == "CG 0x00 TSA");
            Assert.Contains(list, a => a.Addr == pal && a.Length == 0x20 * 8
                && a.DataType == Address.DataTypeEnum.PAL && a.Info == "CG 0x00 PALETTE");
        }

        [Fact]
        public void EmitImageCGFE7UAt_16Color_And_10Split_PerEntryShapes()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // Block 16, rule u16(addr+2)==0. Entry 0: flag(+0)!=1 -> 16-color (LZ77@+4, HEADER-TSA@+8,
            // 0x20*1 PAL@+12). Entry 1: flag==1 -> 10-split. Terminator entry 2: u16(+2)!=0.
            uint img0 = 0x2000;
            uint lz0 = WriteLz77AllLiteral(rom, img0, 32);
            uint tsa0 = 0x3000;
            uint tsa0Len = WriteHeaderTsa(rom, tsa0, 0x03, 0x01); // (4*2)=8 -> 2 + 8*2 = 18
            uint pal0 = 0x4000;
            rom.write_u8(table + 0 + 0, 0x00);   // flag != 1
            rom.write_u16(table + 0 + 2, 0x0000); // valid row
            rom.write_u32(table + 0 + 4, Ptr(img0));
            rom.write_u32(table + 0 + 8, Ptr(tsa0));
            rom.write_u32(table + 0 + 12, Ptr(pal0));

            uint imgArray = 0x5000;
            uint firstImg = 0x6000;
            uint lzS = WriteLz77AllLiteral(rom, firstImg, 16);
            for (int n = 0; n < 10; n++) rom.write_u32(imgArray + (uint)n * 4, Ptr(firstImg));
            uint tsa1 = 0x7000;
            uint tsa1Len = WriteHeaderTsa(rom, tsa1, 0x01, 0x01); // 2 + 4*2 = 10
            uint pal1 = 0x7800;
            rom.write_u8(table + 16 + 0, 0x01);   // flag == 1 -> 10-split
            rom.write_u16(table + 16 + 2, 0x0000);
            rom.write_u32(table + 16 + 4, Ptr(imgArray));
            rom.write_u32(table + 16 + 8, Ptr(tsa1));
            rom.write_u32(table + 16 + 12, Ptr(pal1));

            // Terminator entry 2: u16(+2) != 0.
            rom.write_u16(table + 32 + 2, 0x0001);

            var list = new List<Address>();
            RebuildProducerCore.EmitImageCGFE7UAt(rom, list, pointer);

            Address main = list.FirstOrDefault(a => a.Addr == table && a.Info == "CG");
            Assert.NotNull(main);
            Assert.Equal(16u * (2u + 1u), main.Length); // block 16 * (count 2 + 1) = 48
            Assert.Equal(16u, main.BlockSize);

            // Entry 0 (16-color): one LZ77 @+4, HEADER-TSA @+8, 0x20*1 PAL @+12.
            Assert.Contains(list, a => a.Addr == img0 && a.DataType == Address.DataTypeEnum.LZ77IMG
                && a.Info == "CG 0x00 IMAGE");
            Assert.Contains(list, a => a.Addr == tsa0 && a.Length == tsa0Len
                && a.DataType == Address.DataTypeEnum.HEADERTSA && a.Info == "CG 0x00 TSA");
            Assert.Contains(list, a => a.Addr == pal0 && a.Length == 0x20 * 1
                && a.DataType == Address.DataTypeEnum.PAL && a.Info == "CG 0x00 PALETTE");

            // Entry 1 (10-split): 10 LZ77 columns, the POINTER header, HEADER-TSA, 0x20*8 PAL.
            int lzCount = list.Count(a => a.Addr == firstImg && a.DataType == Address.DataTypeEnum.LZ77IMG
                && a.Info == "CG 0x01 IMAGE");
            Assert.Equal(10, lzCount);
            Assert.Contains(list, a => a.Addr == imgArray && a.Length == 4 * 10
                && a.DataType == Address.DataTypeEnum.POINTER && a.Info == "CG 0x01 IMAGE_HEADER");
            Assert.Contains(list, a => a.Addr == tsa1 && a.Length == tsa1Len
                && a.DataType == Address.DataTypeEnum.HEADERTSA && a.Info == "CG 0x01 TSA");
            Assert.Contains(list, a => a.Addr == pal1 && a.Length == 0x20 * 8
                && a.DataType == Address.DataTypeEnum.PAL && a.Info == "CG 0x01 PALETTE");
        }

        [Fact]
        public void EmitWorldMapCountyBorder_PerEntry_EmitsPointerLZ77AndROMTCS()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // Block 12, rule isPointer(u32+0) && isPointer(u32+4). Entry 0: +0 -> LZ77 image (POINTER type)
            // AND -> ROMTCS (length via CalcRomTcsLength). +4 just needs to be a pointer for the rule.
            uint borderData = 0x2000;
            uint lz = WriteLz77AllLiteral(rom, borderData, 48);
            // Plant a ROMTCS terminator AFTER the LZ77 stream so CalcRomTcsLength finds it.
            byte[] term = new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x10, 0x00 };
            uint termAt = borderData + 0x80;
            for (int i = 0; i < term.Length; i++) rom.write_u8(termAt + (uint)i, term[i]);
            uint romtcsLen = (termAt + 4) - borderData;
            rom.write_u32(table + 0 + 0, Ptr(borderData));
            rom.write_u32(table + 0 + 4, Ptr(0x3000));
            rom.write_u32(0x3000, Ptr(0x3100)); // make +4's target irrelevant; just a valid pointer target

            // Terminator entry 1: +0 not a pointer.
            rom.write_u32(table + 12 + 0, 0x00000000);
            rom.write_u32(table + 12 + 4, 0x00000000);

            var list = new List<Address>();
            RebuildProducerCore.EmitWorldMapCountyBorder(rom, list, pointer);

            Address main = list.FirstOrDefault(a => a.Addr == table && a.Info == "WorldmapCountyBorder");
            Assert.NotNull(main);
            Assert.Equal(12u * (1u + 1u), main.Length);
            Assert.Equal(12u, main.BlockSize);

            // POINTER-typed LZ77 column @+0.
            Assert.Contains(list, a => a.Addr == borderData && a.DataType == Address.DataTypeEnum.POINTER
                && a.Info == "WorldmapCountyBorder 0x00 IMAGE");
            // ROMTCS column @+0.
            Assert.Contains(list, a => a.Addr == borderData && a.Length == romtcsLen
                && a.DataType == Address.DataTypeEnum.ROMTCS
                && a.Info == "WorldmapCountyBorder 0x00 ROMTCS");
        }

        [Fact]
        public void EmitWorldMapIconData_EmitsMainIfr()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // Block 16, rule isPointer(u32+4). Two valid entries then a terminator (+4 not a pointer).
            rom.write_u32(table + 0 + 4, Ptr(0x2000));
            rom.write_u32(table + 16 + 4, Ptr(0x2000));
            rom.write_u32(table + 32 + 4, 0x00000000); // terminator

            var list = new List<Address>();
            RebuildProducerCore.EmitWorldMapIconData(rom, list, pointer);

            Address main = list.FirstOrDefault(a => a.Addr == table && a.Info == "WorldMapIconData");
            Assert.NotNull(main);
            Assert.Equal(16u * (2u + 1u), main.Length);
            Assert.Equal(16u, main.BlockSize);
            Assert.Equal(new uint[] { 4 }, main.PointerIndexes);
        }

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2kCoveredForms_KeepsDeferredSiblings()
        {
            // slice 2k ports the header-TSA image forms -> no longer deferred.
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
            Assert.DoesNotContain("ImageBGForm", notYet);
            Assert.DoesNotContain("ImageSystemIconForm", notYet);
            Assert.DoesNotContain("ImageCGForm", notYet);
            Assert.DoesNotContain("ImageCGFE7UForm", notYet);
            Assert.DoesNotContain("WorldMapImageForm", notYet);

            // (ImageTSAAnime2Form / ImageTSAAnimeForm were the config-FILE TSA-anime siblings here; slice
            //  2q ports them — see GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("ImageTSAAnime2Form", notYet);
            Assert.DoesNotContain("ImageTSAAnimeForm", notYet);
            // Other header-less image forms blocked on a different subsystem still stay tracked.
            Assert.Contains("ImageBattleAnimeForm", notYet);      // ImageUtilOAM OAM frame walk
            Assert.Contains("ImagePortraitForm", notYet);         // IsHalfBodyFlag runtime header

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        [Fact]
        public void NestedPointerAt_Rule_RequiresDoubleIndirectionPointer()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // Entry 0: +0 is a pointer whose target's first u32 is ALSO a pointer -> valid.
            uint inner = 0x2000;
            rom.write_u32(inner, Ptr(0x3000)); // target's first u32 is a pointer
            rom.write_u32(table + 0, Ptr(inner));
            // Entry 1: +0 is a pointer but its target's first u32 is NOT a pointer (0) -> terminator.
            uint inner2 = 0x4000;
            rom.write_u32(inner2, 0x00000000);
            rom.write_u32(table + 4, Ptr(inner2));

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "NP",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.NestedPointerAt,
                RuleOffset = 0,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 1 -> length = 4 * (1 + 1) = 8.
            Assert.Equal(8u, list[0].Length);
        }

        [Fact]
        public void U16EqualAt_Rule_ContinuesWhileValueMatches()
        {
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));

            // Block 8, continue while u16(addr+2)==0. Two valid rows, then a row with u16(+2)!=0.
            rom.write_u16(table + 0 + 2, 0x0000);
            rom.write_u16(table + 8 + 2, 0x0000);
            rom.write_u16(table + 16 + 2, 0x0099); // terminator

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "U16Eq",
                PointerField = _ => pointer,
                BlockSize = 8,
                Rule = RebuildProducerCore.DataCountRule.U16EqualAt,
                RuleOffset = 2,
                RuleStopValue = 0,
                PointerIndexes = new uint[] { },
            };
            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Assert.Single(list);
            // dataCount = 2 -> length = 8 * (2 + 1) = 24.
            Assert.Equal(24u, list[0].Length);
        }

        // ---- slice 2l: EmitItemWeaponEffect + CalcProcsLengthAndCheck --------

        // Plant a minimal valid PROCS stream at `addr`: one 0x0B instruction (parg must be 0) then a
        // 0x00 EXIT instruction. Length consumed = 16. Returns the planted byte length.
        static uint PlantProcsStream(ROM rom, uint addr)
        {
            // instr 0: code 0x0B, sarg 0, parg 0 (parg-is-null contract).
            rom.write_u16(addr + 0, 0x000B);
            rom.write_u16(addr + 2, 0x0000);
            rom.write_u32(addr + 4, 0x00000000);
            // instr 1: code 0x00 EXIT (sarg 0, parg 0 -> arg-all-null OK, then EXIT break).
            rom.write_u16(addr + 8, 0x0000);
            rom.write_u16(addr + 10, 0x0000);
            rom.write_u32(addr + 12, 0x00000000);
            return 16;
        }

        [Fact]
        public void EmitItemWeaponEffectAt_EmitsMainIfr_AndPerEntryProcsSubBlocks()
        {
            // Two entries, each with a valid +8 PROCS pointer; the 3rd row is a 0xFFFF terminator.
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0400;
            uint procs0 = 0x2000;
            uint procs1 = 0x3000;
            rom.write_u32(pointer, Ptr(table));

            // entry 0: item id 0x10, mapAnime -> procs0.
            rom.write_u8(table + 0, 0x10);
            rom.write_u32(table + 8, Ptr(procs0));
            // entry 1: item id 0x2A, mapAnime -> procs1.
            rom.write_u8(table + 16 + 0, 0x2A);
            rom.write_u32(table + 16 + 8, Ptr(procs1));
            // entry 2: u16(addr)==0xFFFF -> count terminates at 2.
            rom.write_u16(table + 32, 0xFFFF);

            uint expect0 = PlantProcsStream(rom, procs0);
            uint expect1 = PlantProcsStream(rom, procs1);

            var list = new List<Address>();
            RebuildProducerCore.EmitItemWeaponEffectAt(rom, list, pointer);

            // main IFR: DataCount 2 -> length 16*(2+1)=48, pointerIndexes {8}.
            Address main = list.Single(a => a.DataType == Address.DataTypeEnum.InputFormRef && a.Info == "ItemWeaponEffect");
            Assert.Equal(table, main.Addr);
            Assert.Equal(pointer, main.Pointer);
            Assert.Equal(48u, main.Length);
            Assert.Equal(new uint[] { 8 }, main.PointerIndexes);

            // PROCS sub-block for entry 0.
            Address p0 = list.Single(a => a.DataType == Address.DataTypeEnum.PROCS && a.Addr == procs0);
            Assert.Equal(expect0, p0.Length);
            Assert.Equal(table + 8, p0.Pointer);
            Assert.Equal("ItemWeaponEffect_PROC_0x10", p0.Info);

            // PROCS sub-block for entry 1.
            Address p1 = list.Single(a => a.DataType == Address.DataTypeEnum.PROCS && a.Addr == procs1);
            Assert.Equal(expect1, p1.Length);
            Assert.Equal(table + 16 + 8, p1.Pointer);
            Assert.Equal("ItemWeaponEffect_PROC_0x2A", p1.Info);
        }

        [Fact]
        public void EmitItemWeaponEffectAt_NullMapAnime_EmitsOnlyMainIfr()
        {
            // entry 0 has +8 == 0 (not a safe offset) -> no PROCS sub-block; then terminator.
            var rom = CreateTestRom(0x10000);
            uint table = 0x1000;
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(table + 0, 0x05);
            rom.write_u32(table + 8, 0); // mapAnime == 0 -> not isSafetyOffset -> skipped
            rom.write_u16(table + 16, 0xFFFF); // count terminates at 1

            var list = new List<Address>();
            RebuildProducerCore.EmitItemWeaponEffectAt(rom, list, pointer);

            Assert.Single(list, a => a.DataType == Address.DataTypeEnum.InputFormRef);
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.PROCS);
        }

        [Fact]
        public void EmitItemWeaponEffectAt_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x2000);
            uint pointer = 0x0400;
            rom.write_u32(pointer, Ptr(0x1FF0)); // base near EOF
            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitItemWeaponEffectAt(rom, list, pointer));
            Assert.Null(ex);
        }

        [Fact]
        public void EmitItemWeaponEffect_RunsCleanly_OnEmptyFakeRom()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitItemWeaponEffect(fe8, list));
                Assert.Null(ex);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ---- CalcProcsLengthAndCheck (verbatim PROCS terminator walk) --------

        [Fact]
        public void CalcProcsLengthAndCheck_StopsAtExit00_ReturnsLength()
        {
            var rom = CreateTestRom(0x10000);
            uint addr = 0x1000;
            uint expect = PlantProcsStream(rom, addr); // 0x0B then 0x00 EXIT -> 16
            Assert.Equal(expect, RebuildProducerCore.CalcProcsLengthAndCheck(rom, addr));
        }

        [Fact]
        public void CalcProcsLengthAndCheck_OddStart_ReturnsNotFound()
        {
            var rom = CreateTestRom(0x10000);
            // An odd start address is rejected up-front (IsValueOdd).
            Assert.Equal(U.NOT_FOUND, RebuildProducerCore.CalcProcsLengthAndCheck(rom, 0x1001));
        }

        [Fact]
        public void CalcProcsLengthAndCheck_UnknownOpcode_ReturnsNotFound()
        {
            var rom = CreateTestRom(0x10000);
            uint addr = 0x1000;
            // code 0x07FF is not a known opcode (and not 0x800) -> contract violation -> NOT_FOUND.
            rom.write_u16(addr + 0, 0x07FF);
            rom.write_u16(addr + 2, 0x0000);
            rom.write_u32(addr + 4, 0x00000000);
            Assert.Equal(U.NOT_FOUND, RebuildProducerCore.CalcProcsLengthAndCheck(rom, addr));
        }

        [Fact]
        public void CalcProcsLengthAndCheck_BadArgContract_ReturnsNotFound()
        {
            var rom = CreateTestRom(0x10000);
            uint addr = 0x1000;
            // code 0x0B requires parg == 0; plant a non-zero parg -> contract violation.
            rom.write_u16(addr + 0, 0x000B);
            rom.write_u16(addr + 2, 0x0000);
            rom.write_u32(addr + 4, 0x12345678);
            Assert.Equal(U.NOT_FOUND, RebuildProducerCore.CalcProcsLengthAndCheck(rom, addr));
        }

        [Fact]
        public void CalcProcsLengthAndCheck_UnterminatedNearEof_NoThrow_ReturnsBounded()
        {
            // An unterminated valid-opcode stream that runs to EOF: the while (addr+8<=limit) clamp
            // returns the consumed length instead of throwing on any read.
            var rom = CreateTestRom(0x2000);
            uint addr = 0x1FE0; // (0x2000 - 0x1FE0) = 0x20 bytes = 4 PROCS slots
            for (uint i = 0; i < 4; i++)
            {
                rom.write_u16(addr + i * 8 + 0, 0x000B); // valid opcode, parg-is-null
                rom.write_u16(addr + i * 8 + 2, 0x0000);
                rom.write_u32(addr + i * 8 + 4, 0x00000000);
            }
            uint len = 0;
            var ex = Record.Exception(() => { len = RebuildProducerCore.CalcProcsLengthAndCheck(rom, addr); });
            Assert.Null(ex);
            Assert.True(len != U.NOT_FOUND); // bounded, no throw (exact value not asserted)
        }

        // ---- NotYetPorted coverage delta for slice 2l -----------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2lCoveredForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2l ports ItemWeaponEffectForm (PROCS sub-block via CalcProcsLengthAndCheck).
            Assert.DoesNotContain("ItemWeaponEffectForm", notYet);

            // AIScriptForm STAYS — its main-IFR count rule needs the config-file AI name lists
            // (EventUnitForm.AI1/AI2.Count), not in Core.
            Assert.Contains("AIScriptForm", notYet);
            // ProcsScriptForm itself STAYS — it is an ASM-path form (MakePatchStructDataList), out of
            // scope for this data-path producer; only its CalcLengthAndCheck length helper is reused.
            Assert.Contains("ProcsScriptForm", notYet);
            // ItemForm STAYS — StatBooster sub-block size needs un-ported PatchUtil detection.
            Assert.Contains("ItemForm", notYet);

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ====================================================================
        // slice 2m: TextForm (EmitText) + TextCharCodeForm (descriptor) +
        //           FE8SpellMenuExtendsForm (EmitFE8SpellMenuExtends)
        // ====================================================================

        // ---- TextCharCodeForm: flat U8NotEqual descriptor (mask_pointer) -----

        [Fact]
        public void TextCharCode_U8NotEqual255_StopsAtTerminator_EmptyPointerIndexes()
        {
            var rom = CreateTestRom();
            // WF TextCharCodeForm.Init: base mask_pointer, block 4, IsDataExists = u8(addr) != 255,
            // AddAddress(list, IFR, "TextCharCode", new uint[] {}) — empty PI, default InputFormRef type.
            uint table = 0x1000;
            uint pointer = 0x0240;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u8(table + 0 * 4, 0x41);  // valid
            rom.write_u8(table + 1 * 4, 0x42);  // valid
            rom.write_u8(table + 2 * 4, 0x43);  // valid
            rom.write_u8(table + 3 * 4, 0xFF);  // 255 terminator -> count 3

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "TextCharCode",
                PointerField = _ => pointer,
                BlockSize = 4,
                Rule = RebuildProducerCore.DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 255,
                PointerIndexes = new uint[] { },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            Address a = Assert.Single(list);
            Assert.Equal(table, a.Addr);
            Assert.Equal(pointer, a.Pointer);
            Assert.Equal(4u, a.BlockSize);
            Assert.Equal(4u * (3 + 1), a.Length);   // block 4, count 3 -> 16
            Assert.Equal(Address.DataTypeEnum.InputFormRef, a.DataType);
            Assert.Empty(a.PointerIndexes);
        }

        // ---- EmitTextAt: main TEXTPOINTERS IFR + per-entry BIN sub-walks ----

        [Fact]
        public void EmitTextAt_MainIfr_IsTextPointers_Block4_PI0_AndPerEntryBin()
        {
            var rom = CreateTestRom(0x8000);
            // No encoder -> the WF isPointerOnly path: per-entry BIN emits with size 0 (the slot is
            // still relocated and the target addr recorded). Avoids the Huffman decode (which needs a
            // valid mask_pointer tree, absent on a bare synthetic ROM).
            var savedEncoder = CoreState.SystemTextEncoder;
            CoreState.SystemTextEncoder = null;
            try
            {
                uint pointer = 0x0400;
                uint table = 0x1000;
                uint textNormal = 0x2000;      // normal-pointer text target
                rom.write_u32(pointer, Ptr(table));

                // entry 0: a normal ROM pointer -> isPointer branch.
                rom.write_u32(table + 0 * 4, Ptr(textNormal));
                // entry 1: an un-Huffman patch pointer (0x88000000..0x8A000000). 0x88001500 ->
                // ConvertUnHuffmanPatchToPointer -> 0x08001500 -> toOffset -> 0x1500.
                rom.write_u32(table + 1 * 4, 0x88001500);
                // entry 2: NOT a pointer / patch / RAM -> terminates the count (count = 2).
                rom.write_u32(table + 2 * 4, 0x00000005);

                var list = new List<Address>();
                RebuildProducerCore.EmitTextAt(rom, list, pointer);

                // Main IFR: TEXTPOINTERS, block 4, count 2 -> length 4*(2+1)=12, pointer = slot, PI {0}.
                Address main = list.Single(a => a.DataType == Address.DataTypeEnum.TEXTPOINTERS);
                Assert.Equal(table, main.Addr);
                Assert.Equal(pointer, main.Pointer);
                Assert.Equal(4u, main.BlockSize);
                Assert.Equal(12u, main.Length);
                Assert.Equal(new uint[] { 0 }, main.PointerIndexes);
                Assert.Equal("Text", main.Info);

                // entry 0 BIN: addr = toOffset(Ptr(textNormal)) = textNormal, pointer = slot table+0, size 0.
                Address e0 = list.Single(a => a.DataType == Address.DataTypeEnum.BIN && a.Pointer == table + 0);
                Assert.Equal(textNormal, e0.Addr);
                Assert.Equal(0u, e0.Length);
                Assert.Equal("Text " + U.ToHexString(0u), e0.Info);

                // entry 1 BIN: addr = 0x1500 (un-Huffman decoded), pointer = slot table+4, size 0.
                Address e1 = list.Single(a => a.DataType == Address.DataTypeEnum.BIN && a.Pointer == table + 4);
                Assert.Equal(0x1500u, e1.Addr);
                Assert.Equal(0u, e1.Length);
                Assert.Equal("Text " + U.ToHexString(1u), e1.Info);
            }
            finally
            {
                CoreState.SystemTextEncoder = savedEncoder;
            }
        }

        [Fact]
        public void EmitTextAt_RamPointerEntry_CountedButNotEmitted()
        {
            // WF Init's IsDataExists counts a RAM-pointer slot (Is_RAMPointerArea) toward DataCount, but
            // the per-entry loop emits NO AddAddress for it (the target lives in RAM, not ROM). Reproduce
            // exactly: the entry contributes to the main IFR length but produces no BIN.
            var rom = CreateTestRom(0x8000);
            var savedEncoder = CoreState.SystemTextEncoder;
            CoreState.SystemTextEncoder = null;
            try
            {
                uint pointer = 0x0400;
                uint table = 0x1000;
                rom.write_u32(pointer, Ptr(table));
                rom.write_u32(table + 0 * 4, 0x03000100);   // is_03RAMPointer -> counted, NOT emitted
                rom.write_u32(table + 1 * 4, 0x00000005);   // terminates -> count 1

                var list = new List<Address>();
                RebuildProducerCore.EmitTextAt(rom, list, pointer);

                Address main = list.Single(a => a.DataType == Address.DataTypeEnum.TEXTPOINTERS);
                Assert.Equal(4u * (1 + 1), main.Length);    // count 1 -> length 8
                // No BIN sub-block emitted for the RAM-pointer entry.
                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.BIN);
            }
            finally
            {
                CoreState.SystemTextEncoder = savedEncoder;
            }
        }

        [Fact]
        public void EmitTextAt_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            var savedEncoder = CoreState.SystemTextEncoder;
            CoreState.SystemTextEncoder = null;
            try
            {
                // Slot near EOF: the +3 guard before p32 must prevent any out-of-bounds read.
                uint pointer = (uint)rom.Data.Length - 2;
                var list = new List<Address>();
                Assert.Null(Record.Exception(() =>
                    RebuildProducerCore.EmitTextAt(rom, list, pointer)));
                Assert.Empty(list);
            }
            finally
            {
                CoreState.SystemTextEncoder = savedEncoder;
            }
        }

        [Fact]
        public void EmitTextAt_EncoderPresent_BrokenMaskTree_DoesNotThrow()
        {
            // Regression (PR #1285 review): with an encoder loaded, the per-entry huffman_decode throws
            // FETextException on a broken mask tree (real RomInfo whose mask_pointer dereferences to an
            // unsafe tree_base — here the versioned ROM's mask data is all-zero). EmitTextAt must CATCH it
            // and fall back to size 0 rather than aborting the whole producer run. (A bare CreateTestRom
            // can't exercise this — its RomInfo is null, so huffman_decode NREs before the mask check; a
            // real run always has a non-null RomInfo, so FETextException is the realistic failure mode.)
            var rom = MakeVersionedRom("BE8E01"); // FE8U: non-null RomInfo; mask tree is zero -> unsafe.
            var savedRom = CoreState.ROM;
            var savedEncoder = CoreState.SystemTextEncoder;
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            try
            {
                uint pointer = 0x0400, table = 0x1000, textNormal = 0x2000;
                rom.write_u32(pointer, Ptr(table));
                rom.write_u32(table + 0 * 4, Ptr(textNormal)); // normal pointer -> isPointer -> huffman_decode throws
                rom.write_u32(table + 1 * 4, 0x00000005);      // terminate -> count 1

                var list = new List<Address>();
                Assert.Null(Record.Exception(() => RebuildProducerCore.EmitTextAt(rom, list, pointer)));
                // The run completes: the main IFR is produced (the per-entry decode was caught -> size 0).
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.TEXTPOINTERS);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEncoder;
            }
        }

        // ---- EmitFE8SpellMenuExtendsAt: main IFR + per-entry NestedIfr -------

        [Fact]
        public void EmitFE8SpellMenuExtendsAt_MainIfr_AndPerEntryNestedIfr()
        {
            var rom = CreateTestRom(0x10000);
            uint assignLevelUpP = 0x0400;   // the patch-resolved pointer slot
            uint table = 0x1000;            // base = p32(assignLevelUpP)
            uint lv0 = 0x2000;              // entry 0 level-up list (N1 sub-table)
            uint lv1 = 0x3000;              // entry 1 level-up list
            rom.write_u32(assignLevelUpP, Ptr(table));

            // The main IFR rule is i < 0xFF (a pure count). To bound the synthetic test, plant level-up
            // pointers in only the first 2 slots and rely on the getBlockDataCount walk: with rule i<0xFF
            // it would count to 0xFF, so cap the table region by ROM size — instead verify the per-entry
            // NestedIfr emission for the two populated slots, and that the main IFR is present.
            rom.write_u32(table + 0 * 4, Ptr(lv0));   // entry 0 -> N1 @ table+0
            rom.write_u32(table + 1 * 4, Ptr(lv1));   // entry 1 -> N1 @ table+4

            // N1 sub-table block 2, rule u16 != 0xFFFF && != 0:
            // lv0: 3 valid u16 then 0x0000 terminator -> count 3 -> length 2*(3+1)=8.
            rom.write_u16(lv0 + 0, 0x0101);
            rom.write_u16(lv0 + 2, 0x0202);
            rom.write_u16(lv0 + 4, 0x0303);
            rom.write_u16(lv0 + 6, 0x0000);
            // lv1: 1 valid u16 then 0xFFFF terminator -> count 1 -> length 2*(1+1)=4.
            rom.write_u16(lv1 + 0, 0x0505);
            rom.write_u16(lv1 + 2, 0xFFFF);

            var list = new List<Address>();
            RebuildProducerCore.EmitFE8SpellMenuExtendsAt(rom, list, assignLevelUpP);

            // Main IFR: base = table, block 4, pointer = slot, PI {}, type InputFormRef.
            Address main = list.Single(a => a.Info == "SkillAssignmentUnitSkillSystem");
            Assert.Equal(table, main.Addr);
            Assert.Equal(assignLevelUpP, main.Pointer);
            Assert.Equal(4u, main.BlockSize);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);
            Assert.Empty(main.PointerIndexes);

            // Per-entry NestedIfr @ table+0 (lv0) and table+4 (lv1).
            Address n0 = list.Single(a => a.Info == "SkillAssignmentUnitSkillSystem.Levelup0");
            Assert.Equal(lv0, n0.Addr);
            Assert.Equal(table + 0, n0.Pointer);
            Assert.Equal(2u, n0.BlockSize);
            Assert.Equal(8u, n0.Length);          // count 3 -> 2*(3+1)
            Assert.Empty(n0.PointerIndexes);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, n0.DataType);

            Address n1 = list.Single(a => a.Info == "SkillAssignmentUnitSkillSystem.Levelup1");
            Assert.Equal(lv1, n1.Addr);
            Assert.Equal(table + 4, n1.Pointer);
            Assert.Equal(4u, n1.Length);          // count 1 -> 2*(1+1)
        }

        [Fact]
        public void EmitFE8SpellMenuExtendsAt_UnsafeLevelupList_ContinuesWithoutNested()
        {
            // WF: !isSafetyOffset(levelupList) -> continue (no nested table for that entry, but the loop
            // keeps going). A NULL level-up pointer must skip ONLY that entry's NestedIfr.
            var rom = CreateTestRom(0x10000);
            uint assignLevelUpP = 0x0400;
            uint table = 0x1000;
            uint lv1 = 0x3000;
            rom.write_u32(assignLevelUpP, Ptr(table));
            rom.write_u32(table + 0 * 4, 0x00000000);  // entry 0 NULL -> continue (no nested)
            rom.write_u32(table + 1 * 4, Ptr(lv1));    // entry 1 valid
            rom.write_u16(lv1 + 0, 0x0707);
            rom.write_u16(lv1 + 2, 0x0000);            // count 1

            var list = new List<Address>();
            RebuildProducerCore.EmitFE8SpellMenuExtendsAt(rom, list, assignLevelUpP);

            Assert.Contains(list, a => a.Info == "SkillAssignmentUnitSkillSystem");
            Assert.DoesNotContain(list, a => a.Info == "SkillAssignmentUnitSkillSystem.Levelup0");
            Assert.Contains(list, a => a.Info == "SkillAssignmentUnitSkillSystem.Levelup1");
        }

        [Fact]
        public void EmitFE8SpellMenuExtendsAt_NotFoundPointer_EmitsNothing()
        {
            var rom = CreateTestRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitFE8SpellMenuExtendsAt(rom, list, U.NOT_FOUND);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitFE8SpellMenuExtendsAt_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            uint assignLevelUpP = (uint)rom.Data.Length - 2;   // slot near EOF
            var list = new List<Address>();
            Assert.Null(Record.Exception(() =>
                RebuildProducerCore.EmitFE8SpellMenuExtendsAt(rom, list, assignLevelUpP)));
            Assert.Empty(list);
        }

        // ---- coverage tracker: slice 2m drops Text*/FE8SpellMenu, keeps siblings -

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2mCoveredForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2m ports TextForm + TextCharCodeForm + FE8SpellMenuExtendsForm.
            Assert.DoesNotContain("TextForm", notYet);
            Assert.DoesNotContain("TextCharCodeForm", notYet);
            Assert.DoesNotContain("FE8SpellMenuExtendsForm", notYet);

            // (OtherTextForm was the config-FILE sibling here; slice 2q ports it — see
            //  GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("OtherTextForm", notYet);
            // ImageUnitMoveIconFrom is PORTED in slice 2n (ImageUtilAPCore.CalcAPLength is in Core) ->
            // it must be GONE (see GetNotYetPortedForms_DropsSlice2nMoveIcon_KeepsDeferredSiblings).
            Assert.DoesNotContain("ImageUnitMoveIconFrom", notYet);
            // MapSettingForm STAYS — its count rule needs the WF cached text count.
            Assert.Contains("MapSettingForm", notYet);

            // the no-duplicates invariant still holds after the edits.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ---- real-FE8U: TextForm decodes real Huffman text sizes -------------

        [Fact]
        public void EmitText_FE8U_DecodesRealHuffmanTextSizes()
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
                // huffman_decode needs a SystemTextEncoder (it decodes the string while sizing it).
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                var list = new List<Address>();
                RebuildProducerCore.EmitText(rom, list);

                // Main TEXTPOINTERS IFR present at p32(text_pointer), block 4, PI {0}.
                uint textBase = rom.p32(rom.RomInfo.text_pointer);
                Address main = list.Single(a => a.DataType == Address.DataTypeEnum.TEXTPOINTERS);
                Assert.Equal(textBase, main.Addr);
                Assert.Equal(4u, main.BlockSize);
                Assert.Equal(new uint[] { 0 }, main.PointerIndexes);

                // With a real encoder the per-entry BIN sub-blocks must carry a NON-zero Huffman length
                // for at least some entries (proves huffman_decode was actually invoked, not the
                // pointer-only fallback).
                int sizedBin = list.Count(a => a.DataType == Address.DataTypeEnum.BIN
                    && a.Info != null && a.Info.StartsWith("Text ") && a.Length > 0);
                Assert.True(sizedBin > 0, "expected at least one Huffman-sized Text BIN sub-block");
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // ====================================================================
        // slice 2n: ImageUnitMoveIconFrom — AP (Animated-Parts) per-entry column
        // ====================================================================

        /// <summary>Hand-author a VALID, minimal AP (Animated-Parts) stream of a KNOWN length at
        /// <paramref name="baseOff"/> and return that length. The layout (all offsets relative to base,
        /// little-endian u16) is traced against ImageUtilAPCore.Parse / WF ImageUtilAP.Parse:
        /// <list type="bullet">
        ///   <item>+0  frameDataOffset = 8  (frame-pointer table at base+8)</item>
        ///   <item>+2  animeTableOffset = 10 (anime-pointer table at base+10)</item>
        ///   <item>+8  frame-ptr table: ONE u16 = 4 -> frame block at base+8+4 = base+12; minData = 4</item>
        ///   <item>+10 anime-ptr table: ONE u16 = 10 -> anime block at base+10+10 = base+20</item>
        ///   <item>+12 frame block: u16 count = 1, then 1 OAM (6 bytes) -> ends at base+20</item>
        ///   <item>+20 anime block: {wait=5,frame=0}, {wait=0,frame=0} terminator -> ends at base+28</item>
        /// </list>
        /// Max extent = 28 (already 4-aligned), so GetLength() = Padding4(28) = 28. The frame-ptr table
        /// [base+8,base+10) is exactly one u16; the anime-ptr table [base+10, base+8+minData=base+12) is
        /// exactly one u16.</summary>
        static uint WriteKnownAP(ROM rom, uint baseOff)
        {
            rom.write_u16(baseOff + 0, 8);    // frameDataOffset
            rom.write_u16(baseOff + 2, 10);   // animeTableOffset
            rom.write_u16(baseOff + 8, 4);    // frame-ptr table entry -> frame block @ base+12
            rom.write_u16(baseOff + 10, 10);  // anime-ptr table entry -> anime block @ base+20
            rom.write_u16(baseOff + 12, 1);   // frame count = 1
            for (uint k = 14; k < 20; k++) rom.write_u8(baseOff + k, 0x11); // 1 OAM (6 bytes)
            rom.write_u16(baseOff + 20, 5);   // anime rec1: wait=5
            rom.write_u16(baseOff + 22, 0);   //            frame=0
            rom.write_u16(baseOff + 24, 0);   // anime rec2: wait=0 -> terminator
            rom.write_u16(baseOff + 26, 0);   //            frame=0
            uint len = ImageUtilAPCore.CalcAPLength(rom.Data, baseOff);
            Assert.Equal(28u, len); // hand-traced known length (must match the Core/WF parser)
            return len;
        }

        [Fact]
        public void EmitApPointer_EmitsAP_WithCalcAPLength()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0240;
            uint apData = 0x2000;
            uint expectLen = WriteKnownAP(rom, apData); // 28
            rom.write_u32(pointer, Ptr(apData));

            var list = new List<Address>();
            RebuildProducerCore.EmitApPointer(rom, list, pointer, "MoveUnitIcon 0x0 AP");

            Address ap = Assert.Single(list);
            Assert.Equal(Address.DataTypeEnum.AP, ap.DataType);
            Assert.Equal(apData, ap.Addr);
            Assert.Equal(expectLen, ap.Length);
            Assert.Equal(ImageUtilAPCore.CalcAPLength(rom.Data, apData), ap.Length);
            Assert.Equal(pointer, ap.Pointer);
            Assert.Equal("MoveUnitIcon 0x0 AP", ap.Info);
        }

        [Fact]
        public void EmitApPointer_NullPointerSlot_NoEmit()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0240;
            rom.write_u32(pointer, 0); // NULL target -> isSafetyPointer false -> no emit

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitApPointer(rom, list, pointer, "AP"));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitApPointer_PointerSlotNearEof_NoThrow_NoEmit()
        {
            var rom = CreateTestRom(0x1000);
            // Pointer slot itself within 4 bytes of EOF -> the pointer+4 EOF guard rejects before the u32.
            uint pointer = (uint)rom.Data.Length - 2;

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitApPointer(rom, list, pointer, "AP"));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitApPointer_MalformedAPTarget_EmitsLengthZero_NoThrow()
        {
            var rom = CreateTestRom(0x8000);
            uint pointer = 0x0240;
            uint apData = 0x2000;
            // animeTableOffset huge -> frame-end (base+animeTableOffset) past EOF -> Parse returns false
            // -> CalcAPLength returns 0. The Address is still emitted (the slot is relocated), length 0.
            rom.write_u16(apData + 0, 4);       // frameDataOffset
            rom.write_u16(apData + 2, 0xFFFF);  // animeTableOffset -> base+0xFFFF past 0x8000 ROM end
            rom.write_u32(pointer, Ptr(apData));
            Assert.Equal(0u, ImageUtilAPCore.CalcAPLength(rom.Data, apData)); // confirm unparseable

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitApPointer(rom, list, pointer, "AP"));
            Assert.Null(ex);
            Address ap = Assert.Single(list);
            Assert.Equal(Address.DataTypeEnum.AP, ap.DataType);
            Assert.Equal(apData, ap.Addr);
            Assert.Equal(0u, ap.Length); // malformed -> length 0 (WF parity)
        }

        [Fact]
        public void SubWalk_ApPointer_EmitsAP_ThroughWalkAndAdd()
        {
            var rom = CreateTestRom(0x8000);
            // One-entry table at 0x1000, block 8. Entry +0 -> LZ77 image, entry +4 -> AP stream.
            uint table = 0x1000;
            uint pointer = 0x0240;
            uint imageData = 0x2000;
            uint apData = 0x3000;
            rom.write_u32(pointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(imageData)); // embedded image pointer @ +0
            rom.write_u32(table + 4, Ptr(apData));    // embedded AP pointer @ +4
            uint imgLen = WriteLz77AllLiteral(rom, imageData, 100);
            uint apLen = WriteKnownAP(rom, apData); // 28

            var d = new RebuildProducerCore.StructDescriptor
            {
                Name = "MoveUnitIcon",
                PointerField = _ => pointer,
                BlockSize = 8,
                Rule = RebuildProducerCore.DataCountRule.FixedCount,
                RuleFixedCount = 1,
                PointerIndexes = new uint[] { 0, 4 },
                SubWalks = new List<RebuildProducerCore.SubWalk>
                {
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 0,
                        Kind = RebuildProducerCore.SubKind.Lz77Pointer,
                        DataType = Address.DataTypeEnum.LZ77IMG,
                        Name = (r, i) => "MoveUnitIcon " + U.To0xHexString((uint)i),
                    },
                    new RebuildProducerCore.SubWalk
                    {
                        EmbeddedPointerOffset = 4,
                        Kind = RebuildProducerCore.SubKind.ApPointer,
                        Name = (r, i) => "MoveUnitIcon " + U.To0xHexString((uint)i) + " AP",
                    },
                },
            };

            var list = new List<Address>();
            RebuildProducerCore.WalkAndAdd(rom, list, d);

            // main IFR (block*(1+1)=16), the LZ77IMG image, and the AP stream.
            Address img = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG);
            Assert.Equal(imageData, img.Addr);
            Assert.Equal(imgLen, img.Length);
            Assert.Equal(table + 0, img.Pointer);

            Address ap = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.AP);
            Assert.Equal(apData, ap.Addr);
            Assert.Equal(apLen, ap.Length);
            Assert.Equal(table + 4, ap.Pointer); // embedded AP pointer field
            // WF per-entry name = U.To0xHexString(i) -> "0x00" for i=0 (2-digit min); + " AP".
            Assert.Equal("MoveUnitIcon 0x00 AP", ap.Info);

            // Per-entry WF order: LZ77 (image) is emitted BEFORE the AP column.
            int imgIdx = list.FindIndex(a => a.DataType == Address.DataTypeEnum.LZ77IMG);
            int apIdx = list.FindIndex(a => a.DataType == Address.DataTypeEnum.AP);
            Assert.True(imgIdx < apIdx, "WF emits AddLZ77Pointer before AddAPPointer per entry");
        }

        [Fact]
        public void MoveUnitIconDescriptor_HasExpectedShape()
        {
            // BuildBatchDescriptors reads rom.RomInfo.version (Class FE6-vs-FE78 branch), so it needs a
            // versioned ROM (CreateTestRom has a NULL RomInfo).
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                var descs = RebuildProducerCore.BuildBatchDescriptors(fe8);
                var mui = descs.Single(d => d.Name == "MoveUnitIcon");

                Assert.Equal(8u, mui.BlockSize);
                Assert.Equal(RebuildProducerCore.DataCountRule.MoveIconRule, mui.Rule);
                Assert.Equal(0u, mui.RuleOffset);
                Assert.Equal(new uint[] { 0, 4 }, mui.PointerIndexes);
                Assert.NotNull(mui.SubWalks);
                Assert.Equal(2, mui.SubWalks.Count);
                // @0 -> LZ77IMG (the sheet), @4 -> AP (the move-anime stream).
                var lz = mui.SubWalks.Single(s => s.EmbeddedPointerOffset == 0);
                Assert.Equal(RebuildProducerCore.SubKind.Lz77Pointer, lz.Kind);
                Assert.Equal(Address.DataTypeEnum.LZ77IMG, lz.DataType);
                var ap = mui.SubWalks.Single(s => s.EmbeddedPointerOffset == 4);
                Assert.Equal(RebuildProducerCore.SubKind.ApPointer, ap.Kind);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void MoveIconRule_ClassCountBounded_AndPointerOrNull()
        {
            // The MoveIcon table count is bounded by the class table (ClassDataCount, clamped/decremented),
            // and per entry (after 0) requires isPointerOrNULL(u32+0). Use a versioned ROM so RomInfo
            // (class_pointer/class_datasize/unit_move_icon_pointer) is populated.
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;

                // --- Plant a small class table so ClassDataCount returns a known small number. ---
                uint cblock = fe8.RomInfo.class_datasize;
                Assert.True(cblock > 0);
                uint classPtrLoc = U.toOffset(fe8.RomInfo.class_pointer);
                uint classTable = 0x300000;
                fe8.write_u32(classPtrLoc, Ptr(classTable));
                // entry 0 always; entry1 u8(+4)=1; entry2 u8(+4)=2; entry3 u8(+4)=0 -> stop => count 3.
                fe8.write_u8(classTable + 1 * cblock + 4, 1);
                fe8.write_u8(classTable + 2 * cblock + 4, 2);
                Assert.Equal(3u, RebuildProducerCore.ClassDataCount(fe8));
                // classCount=3 -> clamp keeps 3 (in [0x7f? no: 3 < 0x7f] => actually <=0 check only).
                // WF: if(<=0)0x7f; else if(>0xff)0xff. 3 stays 3. classCount-- => 2. So i>=2 stops:
                // entries i=0 and i=1 only.

                // --- Plant the move-icon table. ---
                uint muiPtrLoc = U.toOffset(fe8.RomInfo.unit_move_icon_pointer);
                uint muiTable = 0x310000;
                fe8.write_u32(muiPtrLoc, Ptr(muiTable));
                // entry0 (+0 image, +4 AP) — always counted (i==0).
                uint img0 = 0x320000, ap0 = 0x330000;
                fe8.write_u32(muiTable + 0 * 8 + 0, Ptr(img0));
                fe8.write_u32(muiTable + 0 * 8 + 4, Ptr(ap0));
                WriteLz77AllLiteral(fe8, img0, 40);
                WriteKnownAP(fe8, ap0);
                // entry1 (+0 image NULL is allowed by isPointerOrNULL) — counted (i<classCount=2).
                fe8.write_u32(muiTable + 1 * 8 + 0, 0); // NULL image pointer (isPointerOrNULL true)
                fe8.write_u32(muiTable + 1 * 8 + 4, 0); // NULL AP -> no AP emit for this entry
                // entry2 would be i>=2 -> stopped by the class cap regardless of content.
                fe8.write_u32(muiTable + 2 * 8 + 0, Ptr(0x340000));
                fe8.write_u32(muiTable + 2 * 8 + 4, Ptr(0x340000));

                var d = RebuildProducerCore.BuildBatchDescriptors(fe8).Single(x => x.Name == "MoveUnitIcon");
                var list = new List<Address>();
                RebuildProducerCore.WalkAndAdd(fe8, list, d);

                // Main IFR present with the move-icon base.
                Address main = Assert.Single(list, a => a.Info == "MoveUnitIcon");
                Assert.Equal(muiTable, main.Addr);
                // DataCount = 2 (entries 0,1) -> length = 8*(2+1) = 24.
                Assert.Equal(8u * (2u + 1u), main.Length);

                // Exactly ONE AP Address (entry0); entry1 AP is NULL (no emit); entry2 is past the cap.
                Address ap = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.AP);
                Assert.Equal(ap0, ap.Addr);
                Assert.Equal(28u, ap.Length);
                // Exactly ONE LZ77IMG (entry0); entry1 image is NULL.
                Address img = Assert.Single(list, a => a.DataType == Address.DataTypeEnum.LZ77IMG);
                Assert.Equal(img0, img.Addr);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // ---- coverage tracker: slice 2n drops ImageUnitMoveIconFrom ----------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2nMoveIcon_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2n ports ImageUnitMoveIconFrom (AP per-entry column).
            Assert.DoesNotContain("ImageUnitMoveIconFrom", notYet);

            // Sibling image forms blocked on an un-ported subsystem STAY:
            foreach (var kept in new[]
            {
                "ImageBattleAnimeForm",      // ImageUtilOAM (slice 2p ports the Magic/MapAction siblings)
                "ImageItemIconForm",         // out-of-scope icon-SHEET IFR
                "ImagePortraitForm",         // IsHalfBodyFlag runtime header
            })
            {
                Assert.Contains(kept, notYet);
            }
            // (ImageRomAnimeForm / ImageTSAAnimeForm / ImageTSAAnime2Form were the config-FILE-table
            //  siblings here; slice 2q ports them — see
            //  GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings.)
            Assert.DoesNotContain("ImageRomAnimeForm", notYet);
            Assert.DoesNotContain("ImageTSAAnimeForm", notYet);
            Assert.DoesNotContain("ImageTSAAnime2Form", notYet);

            // no-duplicates invariant still holds.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ---- slice 2o: SkillSystems skill-config / skill-assignment forms ---
        //
        // The patch-signature scanners (SkillSystemPatchScanner / SkillSystemTextScanner) are tested
        // separately; here we drive the IFR-building seams (EmitSkillAssignmentMainIfr /
        // EmitSkillAssignmentLevelUp / EmitSkillConfigFE8NAt) over synthetic ROMs to prove the
        // main-IFR + level-up-pointer-list + per-entry NestedIfr shapes reproduce the WF
        // AddAddress/AddAddressInstantIFR/N1 behaviour exactly.

        [Fact]
        public void EmitSkillAssignmentMainIfr_BuildsMainIfr_LengthPlusOne_AndReturnsDataCount()
        {
            var rom = CreateTestRom(0x10000);
            uint assignP = 0x0400;     // pointer slot
            uint table = 0x1000;       // base = p32(assignP)
            rom.write_u32(assignP, Ptr(table));
            // block 1, rule "u8(addr) != 0" — 5 valid bytes then a 0 terminator -> count 5.
            for (uint k = 0; k < 5; k++) rom.write_u8(table + k, (byte)(k + 1));
            rom.write_u8(table + 5, 0x00);

            var list = new List<Address>();
            uint count = RebuildProducerCore.EmitSkillAssignmentMainIfr(rom, list, assignP,
                "SkillAssignmentClassSkillSystem", (i, addr) => rom.u8(addr) != 0);

            Assert.Equal(5u, count);
            Address main = list.Single(a => a.Info == "SkillAssignmentClassSkillSystem");
            Assert.Equal(table, main.Addr);
            Assert.Equal(assignP, main.Pointer);          // BasePointer = the slot
            Assert.Equal(1u, main.BlockSize);
            Assert.Equal(6u, main.Length);                // block * (count + 1) = 1 * 6
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);
            Assert.Empty(main.PointerIndexes);
        }

        [Fact]
        public void EmitSkillAssignmentMainIfr_UnsafeBase_ReturnsCountZero_EmitsNothing()
        {
            // WF AddAddress returns (no emit) when !isSafetyOffset(BaseAddress); the DataCount is still 0.
            var rom = CreateTestRom(0x10000);
            uint assignP = 0x0400;
            rom.write_u32(assignP, 0x00000000);   // base resolves to 0 -> unsafe

            var list = new List<Address>();
            uint count = RebuildProducerCore.EmitSkillAssignmentMainIfr(rom, list, assignP,
                "SkillAssignmentClassSkillSystem", (i, addr) => true);

            Assert.Equal(0u, count);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitSkillAssignmentLevelUp_EmitsListIfr_AndPerEntryNestedIfr()
        {
            var rom = CreateTestRom(0x10000);
            uint assignLevelUpP = 0x0500;   // level-up pointer-list slot
            uint listTable = 0x1000;        // base of the pointer list = p32(assignLevelUpP)
            uint lv0 = 0x2000;
            uint lv1 = 0x3000;
            rom.write_u32(assignLevelUpP, Ptr(listTable));
            rom.write_u32(listTable + 0 * 4, Ptr(lv0));   // entry 0 -> N1 @ listTable+0
            rom.write_u32(listTable + 1 * 4, Ptr(lv1));   // entry 1 -> N1 @ listTable+4
            // lv0: 3 valid u16 then 0x0000 -> count 3 -> length 2*(3+1)=8.
            rom.write_u16(lv0 + 0, 0x0101);
            rom.write_u16(lv0 + 2, 0x0202);
            rom.write_u16(lv0 + 4, 0x0303);
            rom.write_u16(lv0 + 6, 0x0000);
            // lv1: 1 valid u16 then 0xFFFF -> count 1 -> length 2*(1+1)=4.
            rom.write_u16(lv1 + 0, 0x0505);
            rom.write_u16(lv1 + 2, 0xFFFF);

            var list = new List<Address>();
            uint mainDataCount = 2; // the MAIN IFR DataCount (loop bound + fixed count for the list IFR)
            RebuildProducerCore.EmitSkillAssignmentLevelUp(rom, list, assignLevelUpP, mainDataCount,
                "SkillAssignmentClassLeveList", "SkillAssignmentClassSkillSystem.Levelup");

            // The level-up POINTER-LIST IFR (AddAddressInstantIFR): base = listTable, block 4,
            // length = 4 * (mainDataCount + 1) = 4 * 3 = 12, pointer = the slot, PI {0}.
            Address leve = list.Single(a => a.Info == "SkillAssignmentClassLeveList");
            Assert.Equal(listTable, leve.Addr);
            Assert.Equal(assignLevelUpP, leve.Pointer);
            Assert.Equal(4u, leve.BlockSize);
            Assert.Equal(12u, leve.Length);
            Assert.Equal(new uint[] { 0 }, leve.PointerIndexes);

            // Per-entry NestedIfr @ listTable+0 (lv0) and listTable+4 (lv1).
            Address n0 = list.Single(a => a.Info == "SkillAssignmentClassSkillSystem.Levelup0");
            Assert.Equal(lv0, n0.Addr);
            Assert.Equal(listTable + 0, n0.Pointer);
            Assert.Equal(2u, n0.BlockSize);
            Assert.Equal(8u, n0.Length);
            Assert.Empty(n0.PointerIndexes);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, n0.DataType);

            Address n1 = list.Single(a => a.Info == "SkillAssignmentClassSkillSystem.Levelup1");
            Assert.Equal(lv1, n1.Addr);
            Assert.Equal(listTable + 4, n1.Pointer);
            Assert.Equal(4u, n1.Length);
        }

        [Fact]
        public void EmitSkillAssignmentLevelUp_UnsafeLevelupList_SkipsOnlyThatEntrysNested()
        {
            // WF: !isSafetyOffset(levelupList) -> continue (the loop continues; only that entry's
            // NestedIfr is skipped). The pointer-list IFR itself is still emitted.
            var rom = CreateTestRom(0x10000);
            uint assignLevelUpP = 0x0500;
            uint listTable = 0x1000;
            uint lv1 = 0x3000;
            rom.write_u32(assignLevelUpP, Ptr(listTable));
            rom.write_u32(listTable + 0 * 4, 0x00000000);  // entry 0 NULL -> continue (no nested)
            rom.write_u32(listTable + 1 * 4, Ptr(lv1));    // entry 1 valid
            rom.write_u16(lv1 + 0, 0x0707);
            rom.write_u16(lv1 + 2, 0x0000);                // count 1

            var list = new List<Address>();
            RebuildProducerCore.EmitSkillAssignmentLevelUp(rom, list, assignLevelUpP, 2,
                "SkillAssignmentClassLeveList", "SkillAssignmentClassSkillSystem.Levelup");

            Assert.Contains(list, a => a.Info == "SkillAssignmentClassLeveList");
            Assert.DoesNotContain(list, a => a.Info == "SkillAssignmentClassSkillSystem.Levelup0");
            Assert.Contains(list, a => a.Info == "SkillAssignmentClassSkillSystem.Levelup1");
        }

        [Fact]
        public void EmitSkillAssignmentLevelUp_NearEofSlot_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            uint assignLevelUpP = (uint)rom.Data.Length - 2;   // slot near EOF (p32 would overrun)
            var list = new List<Address>();
            Assert.Null(Record.Exception(() =>
                RebuildProducerCore.EmitSkillAssignmentLevelUp(rom, list, assignLevelUpP, 4,
                    "SkillAssignmentClassLeveList", "SkillAssignmentClassSkillSystem.Levelup")));
            Assert.Empty(list);
        }

        [Fact]
        public void EmitSkillAssignmentLevelUp_ListTableRunsOffEof_NoThrow_BreaksMidLoop()
        {
            // The loop bound is the MAIN IFR DataCount (here a large 0x100), NOT a getBlockDataCount over
            // the pointer list — so assignLevelUpAddr can advance past where a 4-byte read is in bounds.
            // Plant the list table at the very END of the ROM so the per-entry p32(assignLevelUpAddr) would
            // overrun within a few iterations; the break must fire instead of throwing.
            var rom = CreateTestRom(0x1000);
            uint assignLevelUpP = 0x0400;
            uint listTable = (uint)rom.Data.Length - 8; // only 2 full 4-byte slots fit before EOF
            rom.write_u32(assignLevelUpP, Ptr(listTable));
            // both slots NULL -> continue (no nested); the point is the loop must terminate via the +3 EOF
            // break once assignLevelUpAddr advances past Data.Length - 4.
            rom.write_u32(listTable + 0, 0x00000000);
            rom.write_u32(listTable + 4, 0x00000000);

            var list = new List<Address>();
            Assert.Null(Record.Exception(() =>
                RebuildProducerCore.EmitSkillAssignmentLevelUp(rom, list, assignLevelUpP, 0x100,
                    "SkillAssignmentClassLeveList", "SkillAssignmentClassSkillSystem.Levelup")));
            // The pointer-list IFR is still emitted (slot is safe); no nested entries (both NULL).
            Assert.Contains(list, a => a.Info == "SkillAssignmentClassLeveList");
            Assert.DoesNotContain(list, a => a.Info.StartsWith("SkillAssignmentClassSkillSystem.Levelup"));
        }

        [Fact]
        public void EmitSkillConfigFE8NAt_OnePerPointer_WithDataCountGreaterThanZero()
        {
            var rom = CreateTestRom(0x10000);
            uint p0 = 0x0400, p1 = 0x0404, p2 = 0x0408;
            uint t0 = 0x1000, t1 = 0x2000;     // t? = base of icon table; p2 -> empty table
            uint tEmpty = 0x3000;
            rom.write_u32(p0, Ptr(t0));
            rom.write_u32(p1, Ptr(t1));
            rom.write_u32(p2, Ptr(tEmpty));

            // block 32, rule u16 != 0xFFFF && != 0.
            // t0: 2 valid entries (u16(addr) non-zero non-FFFF) then a 0x0000 terminator -> count 2.
            rom.write_u16(t0 + 0 * 32, 0x1111);
            rom.write_u16(t0 + 1 * 32, 0x2222);
            rom.write_u16(t0 + 2 * 32, 0x0000);
            // t1: 1 valid then 0xFFFF -> count 1.
            rom.write_u16(t1 + 0 * 32, 0x3333);
            rom.write_u16(t1 + 1 * 32, 0xFFFF);
            // tEmpty: first entry terminator -> count 0 -> WF "DataCount <= 0 -> continue" -> NO emit.
            rom.write_u16(tEmpty + 0, 0x0000);

            var list = new List<Address>();
            RebuildProducerCore.EmitSkillConfigFE8NAt(rom, list, new uint[] { p0, p1, p2 });

            // p0 and p1 emit (count > 0); p2 (count 0) is skipped.
            Address a0 = list.Single(a => a.Info == "SkillConfigFE8N" + U.ToHexString(0));
            Assert.Equal(t0, a0.Addr);
            Assert.Equal(p0, a0.Pointer);
            Assert.Equal(32u, a0.BlockSize);
            Assert.Equal(32u * (2 + 1), a0.Length);   // count 2 -> 32*(2+1)=96
            Assert.Empty(a0.PointerIndexes);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, a0.DataType);

            Address a1 = list.Single(a => a.Info == "SkillConfigFE8N" + U.ToHexString(1));
            Assert.Equal(t1, a1.Addr);
            Assert.Equal(32u * (1 + 1), a1.Length);   // count 1 -> 64

            Assert.DoesNotContain(list, a => a.Info == "SkillConfigFE8N" + U.ToHexString(2));
        }

        [Fact]
        public void EmitSkillConfigFE8NAt_NullPointerArray_EmitsNothing()
        {
            var rom = CreateTestRom();
            var list = new List<Address>();
            RebuildProducerCore.EmitSkillConfigFE8NAt(rom, list, null);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitSkillConfigFE8NAt_NearEofPointer_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            uint nearEof = (uint)rom.Data.Length - 2;
            var list = new List<Address>();
            Assert.Null(Record.Exception(() =>
                RebuildProducerCore.EmitSkillConfigFE8NAt(rom, list, new uint[] { nearEof })));
            Assert.Empty(list);
        }

        [Fact]
        public void EmitSkillAssignmentClass_NonSkillSystemRom_EmitsNothing()
        {
            // On a real FE8U ROM with NO SkillSystems patch installed, SearchSkillSystem != SkillSystem,
            // so the form emits nothing (the WF early-return). MakeVersionedRom gives a real RomInfo but
            // a zeroed body -> no patch signature.
            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01"); // FE8U: version 8, is_multibyte == false
                CoreState.ROM = fe8;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(fe8);

                var list = new List<Address>();
                RebuildProducerCore.EmitSkillAssignmentClass(fe8, list);
                RebuildProducerCore.EmitSkillAssignmentUnit(fe8, list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void EmitSkillConfigFE8N_NonSkillSystemRom_EmitsNothing()
        {
            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var fe8j = MakeVersionedRom("BE8J01"); // FE8J: version 8, is_multibyte == true
                CoreState.ROM = fe8j;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(fe8j);

                var list = new List<Address>();
                RebuildProducerCore.EmitSkillConfigFE8N(fe8j, list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        [Fact]
        public void UnitDataCount_WalksUnitTable_FixedMaxCount()
        {
            // UnitDataCount = getBlockDataCount(p32(unit_pointer), unit_datasize, i < unit_maxcount).
            // On a real FE8U RomInfo, prove it returns a plausible count (or 0 on an empty body) and
            // never throws. (The synthetic seams above already prove the IFR shapes; this guards the
            // RomInfo-bound count helper.)
            var savedRom = CoreState.ROM;
            try
            {
                var fe8 = MakeVersionedRom("BE8E01");
                CoreState.ROM = fe8;
                Assert.Equal(8, fe8.RomInfo.version);
                uint count = 0;
                Assert.Null(Record.Exception(() => count = RebuildProducerCore.UnitDataCount(fe8)));
                // unit_pointer's slot in a zeroed body resolves to 0 -> base unsafe -> count 0.
                Assert.True(count <= fe8.RomInfo.unit_maxcount);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // ---- coverage tracker: slice 2o drops the 3 RecycleOldAnime-free Skill
        //      forms, keeps the anime-dependent siblings -------------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2oSkillForms_KeepsAnimeSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2o ports the RecycleOldAnime-FREE Skill subset.
            Assert.DoesNotContain("SkillAssignmentClassSkillSystemForm", notYet);
            Assert.DoesNotContain("SkillAssignmentUnitSkillSystemForm", notYet);
            Assert.DoesNotContain("SkillConfigFE8NSkillForm", notYet);

            // The RecycleOldAnime-dependent Skill siblings STAY (anime length walker not in Core).
            foreach (var kept in new[]
            {
                "SkillConfigSkillSystemForm",
                "SkillConfigFE8NVer2SkillForm",
                "SkillConfigFE8NVer3SkillForm",
            })
            {
                Assert.Contains(kept, notYet);
            }

            // The Group-3 OAM form ImageBattleAnimeForm STAYS (slice 2p ports the Magic/MapAction
            // RecycleOldAnime siblings — see GetNotYetPortedForms_DropsSlice2pAnimeForms_KeepsBattleAnime).
            Assert.Contains("ImageBattleAnimeForm", notYet);

            // no-duplicates invariant still holds.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // ---- slice 2p: OAM / battle-anime length forms -----------------------

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2pAnimeForms_KeepsBattleAnime()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2p ports the pure-ROM RecycleOldAnime forms (gate/count deps already in Core).
            Assert.DoesNotContain("ImageMapActionAnimationForm", notYet);
            Assert.DoesNotContain("ImageMagicFEditorForm", notYet);
            Assert.DoesNotContain("ImageMagicCSACreatorForm", notYet);

            // ImageBattleAnimeForm STAYS (needs ClassForm.MakeClassList + two IFRs + seat-dedup state).
            Assert.Contains("ImageBattleAnimeForm", notYet);

            // no-duplicates invariant still holds.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }

        // --- ImageMapActionAnimation ---------------------------------------

        [Fact]
        public void EmitImageMapActionAnimation_MainIfr_AndPerEntryRecycleAnime()
        {
            var rom = CreateTestRom(0x10000);
            // Anime pointer table: AnimeP slot -> base. Entries: [0]=empty 00 (skipped), [1]=valid ptr,
            // [2]=NULL terminator (stops the IFR walk at count 2).
            uint animeP = 0x0400;
            uint baseTbl = 0x1000;
            rom.write_u32(animeP, Ptr(baseTbl));
            rom.write_u32(baseTbl + 0 * 8, 0);            // entry 0: empty (skipped by the i=1 loop)
            uint anime1 = 0x2000;
            rom.write_u32(baseTbl + 1 * 8, Ptr(anime1));  // entry 1: valid anime pointer
            // entry 2 left 0 -> isSafetyPointerOrNull(0)==true continues the walk... so plant a NON-pointer
            // sentinel to STOP at count 2.
            rom.write_u32(baseTbl + 2 * 8, 0x12345678);   // not a pointer-or-null -> IFR walk stops

            // anime1 record stream: one 12-byte record (OBJ@+4, PAL@+8), then a 0/0 terminator record.
            uint obj1 = 0x3000;
            rom.write_u32(anime1 + 0, 0xAABBCCDD);        // term1 (non-zero) -> record continues
            rom.write_u32(anime1 + 4, Ptr(obj1));         // OBJ image pointer
            rom.write_u32(anime1 + 8, 0x4000);            // PAL slot value (any safe pointer for AddPointer)
            rom.write_u32(anime1 + 8, Ptr(0x4000));
            rom.write_u32(anime1 + 12, 0);                // term1 of record 2 == 0
            rom.write_u32(anime1 + 16, 0);                // p32(n+4) == 0 -> stop
            // a tiny LZ77 stream at obj1 so AddLZ77Pointer records a real (possibly 0) length without throwing
            rom.write_u32(obj1, 0x00000010);

            var list = new List<Address>();
            RebuildProducerCore.EmitImageMapActionAnimationAt(rom, list, animeP);

            // Main IFR: base=baseTbl, block 8, DataCount=2 (entries 0,1 exist; entry 2 stops),
            // length = 8 * (2 + 1) = 24, pointer = animeP, PI {0}.
            Address mainIfr = list.Single(a => a.Info == "MapActionAnimation");
            Assert.Equal(baseTbl, mainIfr.Addr);
            Assert.Equal(animeP, mainIfr.Pointer);
            Assert.Equal(8u, mainIfr.BlockSize);
            Assert.Equal(8u * 3u, mainIfr.Length);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, mainIfr.DataType);

            // Per-entry RecycleOldAnime for entry 1 (name "MapActionAnime:0x01 " — To0xHexString 2-pads):
            // the record IFR + OBJ + PAL.
            Address recIfr = list.Single(a => a.Info == "MapActionAnime:0x01 ");
            Assert.Equal(anime1, recIfr.Addr);
            Assert.Equal(12u, recIfr.BlockSize);
            // 1 record -> count = (12)/12 = 1, length = 12 * (1 + 1) = 24.
            Assert.Equal(12u * 2u, recIfr.Length);
            Assert.Equal(U.NOT_FOUND, recIfr.Pointer); // ReInit has no pointer.

            Assert.Contains(list, a => a.Info == "MapActionAnime:0x01 OBJ" && a.Addr == obj1);
            Assert.Contains(list, a => a.Info == "MapActionAnime:0x01 PAL" && a.Length == 0x20);
        }

        [Fact]
        public void EmitImageMapActionAnimation_NoSignature_EmitsNothing()
        {
            // FindMapActionAnimationPointer greps a 20-byte signature; a blank ROM has none.
            var rom = CreateTestRom(0x10000);
            var list = new List<Address>();
            RebuildProducerCore.EmitImageMapActionAnimation(rom, list);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitImageMapActionAnimationAt_NearEofSlot_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            // animeP slot within 4 bytes of EOF -> the pointer+3 guard returns without throwing.
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitImageMapActionAnimationAt(rom, list, (uint)rom.Data.Length - 2));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitMapActionRecycleOldAnime_NeverTerminates_EmitsNoIfr()
        {
            // A record stream that never hits the 0/0 terminator before the data end: WF returns without
            // emitting the IFR (n >= limitter guard). Here the clamp == Data.Length, so the loop breaks at
            // EOF with n >= limitter -> no IFR, but the per-record OBJ/PAL emitted so far stay.
            var rom = CreateTestRom(0x2000);
            uint anime = 0x1000;
            // Fill records with non-zero term1 + non-zero img so the terminator never fires, up to EOF.
            for (uint n = anime; n + 12 <= (uint)rom.Data.Length; n += 12)
            {
                rom.write_u32(n + 0, 0x11111111);
                rom.write_u32(n + 4, Ptr(0x1800));
                rom.write_u32(n + 8, Ptr(0x1900));
            }
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMapActionRecycleOldAnime(rom, list, anime, "X:"));
            Assert.Null(ex);
            // No record-table IFR (the walk hit the limiter/EOF without a terminator).
            Assert.DoesNotContain(list, a => a.Info == "X:" && a.DataType == Address.DataTypeEnum.InputFormRef);
        }

        // --- ImageMagic FEditor / CSA --------------------------------------

        [Fact]
        public void CalcMagicOamLength_WalksTo0x01Terminal()
        {
            var rom = CreateTestRom(0x4000);
            uint oamBase = 0x1000;
            uint maxOAM = 24;
            // From oamBase+maxOAM, 12-byte steps until a u32==0x01. Plant 2 steps then the terminal.
            uint start = oamBase + maxOAM;
            rom.write_u32(start + 0, 0xDEAD0000);   // step 1 (not 0x01)
            rom.write_u32(start + 12, 0xBEEF0000);  // step 2 (not 0x01)
            rom.write_u32(start + 24, 0x00000001);  // terminal frame
            // length = (start + 24 + 12) - oamBase = maxOAM + 36.
            uint len = RebuildProducerCore.CalcMagicOamLength(rom, Ptr(oamBase), maxOAM);
            Assert.Equal(maxOAM + 36u, len);
        }

        [Fact]
        public void CalcMagicOamLength_ZeroOffset_ReturnsZero()
        {
            var rom = CreateTestRom(0x1000);
            Assert.Equal(0u, RebuildProducerCore.CalcMagicOamLength(rom, 0, 0));
        }

        [Fact]
        public void CalcMagicOamLength_RunsPastLimiter_ReturnsZero_NoThrow()
        {
            var rom = CreateTestRom(0x4000);
            uint oamBase = 0x1000;
            // No 0x01 terminal within the 2K limiter window -> WF returns 0.
            var ex = Record.Exception(() =>
            {
                uint len = RebuildProducerCore.CalcMagicOamLength(rom, Ptr(oamBase), 0);
                Assert.Equal(0u, len);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void EmitMagicRecycleOldAnime_FEditorAdv_EmitsFrameAndFourOam()
        {
            var rom = CreateTestRom(0x10000);
            uint magicBase = 0x0800;     // the per-spell base (csaSpellTable + 20*i)
            uint frame = 0x1000;
            rom.write_u32(magicBase + 0, Ptr(frame));    // frameData
            rom.write_u32(magicBase + 4, Ptr(0x5000));   // objRtoL OAM ptr
            rom.write_u32(magicBase + 8, Ptr(0x5400));   // objLtoR OAM ptr
            rom.write_u32(magicBase + 12, Ptr(0x5800));  // bgRtoL OAM ptr
            rom.write_u32(magicBase + 16, Ptr(0x5C00));  // bgLtoR OAM ptr

            // One 0x86 record (28 bytes for FEditorAdv) then a 0x80 terminator.
            // record fields: +3 cmd=0x86; OBJ@+4, BG@+16, OBJPAL@+20, BGPAL@+24, OAM idx @+8/+12.
            rom.write_u8(frame + 3, 0x86);
            rom.write_u32(frame + 4, Ptr(0x6000));   // OBJ image
            rom.write_u32(frame + 8, 4);             // objOAM idx
            rom.write_u32(frame + 12, 6);            // bgOAM idx
            rom.write_u32(frame + 16, Ptr(0x6400));  // BG image
            rom.write_u32(frame + 20, Ptr(0x6800));  // OBJ palette
            rom.write_u32(frame + 24, Ptr(0x6C00));  // BG palette
            // terminator record at frame+28: cmd 0x80 (and +1 != 0x01).
            rom.write_u8(frame + 28 + 3, 0x80);

            // Plant OAM terminals so CalcMagicOamLength returns >0 for at least one (not required, but real).
            // objRtoL OAM @0x5000, maxObjOAM=4 -> terminal at 0x5000+4.
            rom.write_u32(0x5000 + 4, 0x00000001);

            var list = new List<Address>();
            RebuildProducerCore.EmitMagicRecycleOldAnime(rom, list, magicBase, "Magic:0x0 ", isCsa: false);

            // FRAME block (MAGICFRAME_FEITORADV), length = end-of-frame-walk - frameData.
            Address frameAddr = list.Single(a => a.Info == "Magic:0x0 FRAME");
            Assert.Equal(frame, frameAddr.Addr);
            Assert.Equal(Address.DataTypeEnum.MAGICFRAME_FEITORADV, frameAddr.DataType);
            // walk: i starts at frame; one 0x86 record advances i by 28; then i hits the 0x80 -> i+=4 -> 32.
            Assert.Equal(32u, frameAddr.Length);

            // Per-record columns.
            Assert.Contains(list, a => a.Info == "Magic:0x0 OBJ" && a.Addr == 0x6000);
            Assert.Contains(list, a => a.Info == "Magic:0x0 BG" && a.Addr == 0x6400);
            Assert.Contains(list, a => a.Info == "Magic:0x0 OBJ PAL" && a.Addr == 0x6800 && a.Length == 0x20);
            Assert.Contains(list, a => a.Info == "Magic:0x0 BG PAL" && a.Addr == 0x6C00 && a.Length == 0x20);
            // FEditorAdv has NO TSA column.
            Assert.DoesNotContain(list, a => a.Info == "Magic:0x0 TSA");

            // Four OAM blocks (MAGICOAM), names verbatim for FEditorAdv.
            Assert.Contains(list, a => a.Info == "Magic:0x0 RihtToLeftOAM" && a.DataType == Address.DataTypeEnum.MAGICOAM);
            Assert.Contains(list, a => a.Info == "Magic:0x0 LeftRightOAM" && a.DataType == Address.DataTypeEnum.MAGICOAM);
            Assert.Contains(list, a => a.Info == "Magic:0x0 OBJ OAM" && a.DataType == Address.DataTypeEnum.MAGICOAM);
            Assert.Contains(list, a => a.Info == "Magic:0x0 BG OAM" && a.DataType == Address.DataTypeEnum.MAGICOAM);
        }

        [Fact]
        public void EmitMagicRecycleOldAnime_TruncatedStream_NoTerminator_EmitsNothing()
        {
            // Regression (PR #1288 review): if the frame walk runs out of bytes for the 4-byte command
            // window (i+4 > limitter) WITHOUT a real 0x80 terminator, the stream is truncated -> treat as
            // over-limiter and emit NO FRAME/OAM (a length from a truncated stream would mis-relocate).
            uint size = 0x2000;
            var rom = CreateTestRom((int)size);
            uint magicBase = 0x0800;
            uint frame = size - 6; // 0x1FFA: one 0x85 'continue' then i+4 exceeds the limiter (= Data.Length)
            rom.write_u32(magicBase + 0, Ptr(frame));
            // 0x85 at frame+3 -> 'continue', i += 4 -> next iteration i+4 > limitter -> truncation break.
            rom.write_u8(frame + 3, 0x85);

            var list = new List<Address>();
            RebuildProducerCore.EmitMagicRecycleOldAnime(rom, list, magicBase, "Magic:0x0 ", isCsa: false);

            // Truncated -> over the limiter -> nothing emitted (no FRAME, no OAM).
            Assert.DoesNotContain(list, a => a.Info == "Magic:0x0 FRAME");
            Assert.Empty(list);
        }

        [Fact]
        public void EmitMagicRecycleOldAnime_Csa_HasTsaColumn_And32ByteStride()
        {
            var rom = CreateTestRom(0x10000);
            uint magicBase = 0x0800;
            uint frame = 0x1000;
            rom.write_u32(magicBase + 0, Ptr(frame));
            rom.write_u32(magicBase + 4, Ptr(0x5000));
            rom.write_u32(magicBase + 8, Ptr(0x5400));
            rom.write_u32(magicBase + 12, Ptr(0x5800));
            rom.write_u32(magicBase + 16, Ptr(0x5C00));

            // One 0x86 CSA record (32 bytes; adds TSA @+28) then a 0x80 terminator at frame+32.
            rom.write_u8(frame + 3, 0x86);
            rom.write_u32(frame + 4, Ptr(0x6000));
            rom.write_u32(frame + 16, Ptr(0x6400));
            rom.write_u32(frame + 20, Ptr(0x6800));
            rom.write_u32(frame + 24, Ptr(0x6C00));
            rom.write_u32(frame + 28, Ptr(0x7000));   // CSA TSA pointer
            rom.write_u8(frame + 32 + 3, 0x80);

            var list = new List<Address>();
            RebuildProducerCore.EmitMagicRecycleOldAnime(rom, list, magicBase, "Magic:0x0 ", isCsa: true);

            Address frameAddr = list.Single(a => a.Info == "Magic:0x0 FRAME");
            Assert.Equal(Address.DataTypeEnum.MAGICFRAME_CSA, frameAddr.DataType);
            // walk: one 32-byte record + 0x80 (i+=4) = 36.
            Assert.Equal(36u, frameAddr.Length);

            // CSA-only TSA column at +28.
            Assert.Contains(list, a => a.Info == "Magic:0x0 TSA" && a.Addr == 0x7000 && a.DataType == Address.DataTypeEnum.LZ77TSA);

            // CSA OAM names differ from FEditorAdv for the BG pair.
            Assert.Contains(list, a => a.Info == "Magic:0x0 RihtToLeftOAM");
            Assert.Contains(list, a => a.Info == "Magic:0x0 LeftRightOAM");
            Assert.Contains(list, a => a.Info == "Magic:0x0 RihtToLeftOAMBG");
            Assert.Contains(list, a => a.Info == "Magic:0x0 LeftRightOAMBG");
        }

        [Fact]
        public void EmitMagicRecycleOldAnime_NearEofBase_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            var list = new List<Address>();
            var ex = Record.Exception(() =>
                RebuildProducerCore.EmitMagicRecycleOldAnime(rom, list, (uint)rom.Data.Length - 8, "X:", isCsa: false));
            Assert.Null(ex);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitImageMagicCommon_NonMagicRom_EmitsNothing()
        {
            // A blank ROM has no magic-engine signature -> SearchMagicSystem returns No -> nothing emitted.
            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = MakeVersionedRom("BE8E01"); // FE8U
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                var list = new List<Address>();
                RebuildProducerCore.EmitImageMagicFEditor(rom, list);
                RebuildProducerCore.EmitImageMagicCsaCreator(rom, list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // ==== slice 2q: config-FILE-table forms ================================
        // OtherText / ImageTSAAnime / ImageTSAAnime2 / ImageRomAnime each load a config TSV (resolved by
        // U.ConfigDataFilename vs CoreState.BaseDirectory). These tests stage a temp config tree with
        // controlled table addresses, plant the corresponding ROM tables, and assert the emitted
        // Addresses; plus the empty-config (emit-nothing-no-throw) and near-EOF robustness paths.

        /// <summary>Stage a temp <c>config/data/{type}FE8.txt</c> with the given lines + a real FE8U ROM
        /// (RomInfo.TitleToFilename == "FE8") assigned to CoreState; runs <paramref name="body"/> with that
        /// ROM, then restores all mutated CoreState. The temp dir is deleted afterwards.</summary>
        static void WithConfig(string type, string[] lines, Action<ROM> body)
        {
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            var savedRom = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;

            string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "feb_s2q_" + Guid.NewGuid().ToString("N"));
            string dataDir = System.IO.Path.Combine(tempRoot, "config", "data");
            System.IO.Directory.CreateDirectory(dataDir);
            try
            {
                if (lines != null)
                {
                    // FE8U TitleToFilename == "FE8"; lang "en" -> ConfigDataFilename tries {type}FE8.en.txt
                    // then {type}FE8.txt. Write the plain .txt so it resolves to our file.
                    System.IO.File.WriteAllLines(
                        System.IO.Path.Combine(dataDir, type + "FE8.txt"), lines);
                }

                CoreState.BaseDirectory = tempRoot;
                CoreState.Language = "en";
                var rom = MakeVersionedRom("BE8E01"); // FE8U: version 8, TitleToFilename "FE8"
                CoreState.ROM = rom; // LoadTSVResource's OtherLangLine reads CoreState.ROM
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                body(rom);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                CoreState.ROM = savedRom;
                CoreState.SystemTextEncoder = savedEnc;
                try { System.IO.Directory.Delete(tempRoot, true); } catch { /* best effort */ }
            }
        }

        static void WriteAsciiZ(ROM rom, uint addr, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                rom.write_u8(addr + (uint)i, (byte)s[i]);
            }
            rom.write_u8(addr + (uint)s.Length, 0x00);
        }

        // ---- OtherText ----

        [Fact]
        public void EmitOtherText_PerEntryStringBin_NoPlusOne()
        {
            // other_text_ config: each line = ONE hex pointer SLOT. The slot holds a pointer to the
            // C string; the emitter records a BIN block of length == strlen (NO +1) behind it.
            uint slot0 = 0x1000;
            uint slot1 = 0x1100;
            WithConfig("other_text_", new[] { U.ToHexString(slot0), U.ToHexString(slot1) }, rom =>
            {
                uint str0 = 0x2000;
                uint str1 = 0x2100;
                rom.write_u32(slot0, Ptr(str0));
                rom.write_u32(slot1, Ptr(str1));
                WriteAsciiZ(rom, str0, "HELLO");  // strlen 5
                WriteAsciiZ(rom, str1, "AB");     // strlen 2

                var list = new List<Address>();
                RebuildProducerCore.EmitOtherText(rom, list);

                Assert.Equal(2, list.Count);
                Address b0 = list.Single(a => a.Addr == str0);
                Assert.Equal(5u, b0.Length);            // NO +1
                Assert.Equal(slot0, b0.Pointer);
                Assert.Equal(Address.DataTypeEnum.BIN, b0.DataType);
                Address b1 = list.Single(a => a.Addr == str1);
                Assert.Equal(2u, b1.Length);
                Assert.Equal(slot1, b1.Pointer);
            });
        }

        [Fact]
        public void EmitOtherText_NoEncoder_SkipsWithoutThrow()
        {
            WithConfig("other_text_", new[] { U.ToHexString(0x1000u) }, rom =>
            {
                rom.write_u32(0x1000, Ptr(0x2000));
                WriteAsciiZ(rom, 0x2000, "X");
                CoreState.SystemTextEncoder = null; // the critical condition (decode would NRE)

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitOtherText(rom, list));
                Assert.Null(ex);
                Assert.Empty(list); // skipped, not crashed
            });
        }

        [Fact]
        public void EmitOtherText_MissingConfig_EmitsNothing()
        {
            // No config file written (lines == null) -> MakeOtherTextList File.Exists is false -> empty
            // list -> nothing emitted, no throw (faithful headless behavior).
            WithConfig("other_text_", null, rom =>
            {
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitOtherText(rom, list));
                Assert.Null(ex);
                Assert.Empty(list);
            });
        }

        // ---- ImageTSAAnime ----

        [Fact]
        public void EmitImageTSAAnime_MainIfr_AndPerEntryLz77PalLz77()
        {
            // tsaanime_ config line: <slot>\t<count>\t<name>. The slot holds a pointer to the anime
            // record table (base); count records of block 12 (LZ77IMG@+0, PAL@+4, LZ77TSA@+8).
            uint slot = 0x1000;
            uint baseTbl = 0x2000;
            // 2 records.
            WithConfig("tsaanime_",
                new[] { U.ToHexString(slot) + "\t002\tMyAnime" }, rom =>
            {
                rom.write_u32(slot, Ptr(baseTbl));
                // record 0
                rom.write_u32(baseTbl + 0 + 0, Ptr(0x3000)); // IMAGE
                rom.write_u32(baseTbl + 0 + 4, Ptr(0x3100)); // PALETTE
                rom.write_u32(baseTbl + 0 + 8, Ptr(0x3200)); // TSA
                // record 1
                rom.write_u32(baseTbl + 12 + 0, Ptr(0x3300));
                rom.write_u32(baseTbl + 12 + 4, Ptr(0x3400));
                rom.write_u32(baseTbl + 12 + 8, Ptr(0x3500));
                // tiny LZ77 headers so getCompressedSize returns a real (possibly 0) length, no throw
                rom.write_u32(0x3000, 0x00000010);
                rom.write_u32(0x3200, 0x00000010);

                var list = new List<Address>();
                RebuildProducerCore.EmitImageTSAAnime(rom, list);

                // Main IFR: base baseTbl, block 12, count 2 (atoh("002")==2), length 12*(2+1)=36,
                // pointer slot, PI {0,4,8}.
                Address mainIfr = list.Single(a => a.Info == "TSAANIME MyAnime " && a.BlockSize == 12);
                Assert.Equal(baseTbl, mainIfr.Addr);
                Assert.Equal(slot, mainIfr.Pointer);
                Assert.Equal(12u * 3u, mainIfr.Length);
                Assert.Equal(Address.DataTypeEnum.InputFormRef, mainIfr.DataType);
                Assert.Equal(new uint[] { 0, 4, 8 }, mainIfr.PointerIndexes);

                // Per-record columns.
                Assert.Contains(list, a => a.Addr == 0x3100 && a.Length == 0x20 * 8 &&
                    a.DataType == Address.DataTypeEnum.PAL);
                Assert.Contains(list, a => a.Addr == 0x3000 &&
                    a.DataType == Address.DataTypeEnum.LZ77IMG);
                Assert.Contains(list, a => a.Addr == 0x3200 &&
                    a.DataType == Address.DataTypeEnum.LZ77TSA);
                // record 1 PALETTE present too.
                Assert.Contains(list, a => a.Addr == 0x3400 && a.Length == 0x20 * 8 &&
                    a.DataType == Address.DataTypeEnum.PAL);
            });
        }

        [Fact]
        public void EmitImageTSAAnime_MissingConfig_EmitsNothing()
        {
            // Empty config file (file exists, only a comment) -> empty dict -> nothing emitted, no throw.
            WithConfig("tsaanime_", new[] { "//empty" }, rom =>
            {
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitImageTSAAnime(rom, list));
                Assert.Null(ex);
                Assert.Empty(list);
            });
        }

        [Fact]
        public void EmitImageTSAAnime_NearEofSlot_NoThrow()
        {
            // A config slot WITHIN 4 bytes of EOF: the `pointer + 4 > Data.Length` guard skips that entry
            // rather than throwing inside p32(pointer). MakeVersionedRom is 32MB, so put the slot 2 bytes
            // before the end (a GBA offset of Data.Length-2 is a safe offset but its 4-byte read overruns).
            WithConfig("tsaanime_", null, rom =>
            {
                uint nearEof = (uint)rom.Data.Length - 2;
                // Re-stage the config with the near-EOF slot. (WithConfig wrote no file; write one now.)
                string dataDir = System.IO.Path.Combine(CoreState.BaseDirectory, "config", "data");
                System.IO.File.WriteAllLines(
                    System.IO.Path.Combine(dataDir, "tsaanime_FE8.txt"),
                    new[] { U.ToHexString(nearEof) + "\t001\tX" });

                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitImageTSAAnime(rom, list));
                Assert.Null(ex);
                Assert.Empty(list); // the near-EOF slot is guarded out
            });
        }

        // ---- ImageTSAAnime2 ----

        [Fact]
        public void EmitImageTSAAnime2_MainAndN1Ifrs_HeaderAndPerRecordTsa()
        {
            // tsaanime2_ config line: <slot>\t<name>. The slot holds a pointer to the header (addr);
            // N1 (block 20) is the 1-entry header IFR; main (block 12) walks records from addr+20.
            uint slot = 0x1000;
            uint addr = 0x2000;
            WithConfig("tsaanime2_", new[] { U.ToHexString(slot) + "\tMyAnime2" }, rom =>
            {
                rom.write_u32(slot, Ptr(addr));
                // HEADER fields: LZ77 image @ addr+16, palette @ addr+4.
                rom.write_u32(addr + 16, Ptr(0x3000)); // header IMAGE
                rom.write_u32(addr + 4, Ptr(0x3100));  // header PALETTE
                rom.write_u32(0x3000, 0x00000010);     // tiny LZ77 header

                // Record table at addr+20: block 12, IsDataExists isPointer(u32(a+8)). 1 record then stop.
                uint rec0 = addr + 20;
                rom.write_u32(rec0 + 8, Ptr(0x4000));  // record 0 valid (u32(a+8) is a pointer)
                // plant a header-TSA stream at the dereferenced target so CalcHeaderTsaLength is non-zero
                rom.write_u8(0x4000 + 0, 0x01); // x-1
                rom.write_u8(0x4000 + 1, 0x01); // y-1
                rom.write_u32((addr + 20) + 12 + 8, 0x12345678); // record 1 +8 not a pointer -> stop

                var list = new List<Address>();
                RebuildProducerCore.EmitImageTSAAnime2(rom, list);

                // Main IFR: base addr+20, block 12, count 1, length 12*(1+1)=24, pointer NOT_FOUND, PI {8}.
                Address mainIfr = list.Single(a => a.BlockSize == 12 && a.Info == "TSAANIME2 MyAnime2 ");
                Assert.Equal(addr + 20, mainIfr.Addr);
                Assert.Equal(U.NOT_FOUND, mainIfr.Pointer);
                Assert.Equal(12u * 2u, mainIfr.Length);
                Assert.Equal(new uint[] { 8 }, mainIfr.PointerIndexes);

                // N1 IFR: base addr, block 20, count 1, length 20*(1+1)=40, pointer slot, PI {4,16}.
                Address n1Ifr = list.Single(a => a.BlockSize == 20 && a.Info == "TSAANIME2 MyAnime2 ");
                Assert.Equal(addr, n1Ifr.Addr);
                Assert.Equal(slot, n1Ifr.Pointer);
                Assert.Equal(20u * 2u, n1Ifr.Length);
                Assert.Equal(new uint[] { 4, 16 }, n1Ifr.PointerIndexes);

                // WF emits the main IFR BEFORE the N1 IFR — assert that order.
                Assert.True(list.IndexOf(mainIfr) < list.IndexOf(n1Ifr),
                    "WF order: main IFR must be emitted before the N1 IFR");

                // HEADER pair (N1.DataCount >= 1): LZ77IMG @ addr+16, PAL 0x20 @ addr+4.
                Assert.Contains(list, a => a.Addr == 0x3000 && a.DataType == Address.DataTypeEnum.LZ77IMG);
                Assert.Contains(list, a => a.Addr == 0x3100 && a.Length == 0x20 &&
                    a.DataType == Address.DataTypeEnum.PAL);

                // Per-record header-TSA @ rec0+8 -> target 0x4000, length = 2 + (2*2*2) = 10.
                Assert.Contains(list, a => a.Addr == 0x4000 && a.Length == 10u &&
                    a.DataType == Address.DataTypeEnum.HEADERTSA);
            });
        }

        [Fact]
        public void EmitImageTSAAnime2_MissingConfig_EmitsNothing()
        {
            WithConfig("tsaanime2_", new[] { "//empty" }, rom =>
            {
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitImageTSAAnime2(rom, list));
                Assert.Null(ex);
                Assert.Empty(list);
            });
        }

        // ---- ImageRomAnime ----

        [Fact]
        public void EmitImageRomAnime_FramePointerListsAndPalette()
        {
            // romanime_ config line: <key>\t<width>\t<option>\t<framePtr>\t<tsaPtr>\t<imgPtr>\t<palPtr>\t<name>
            uint framePtr = 0x1000;
            uint tsaPtr = 0x1100;
            uint imgPtr = 0x1200;
            uint palPtr = 0x1300;
            string line = "0000\t30\tNNN\t" + U.ToHexString(framePtr) + "\t" + U.ToHexString(tsaPtr)
                + "\t" + U.ToHexString(imgPtr) + "\t" + U.ToHexString(palPtr) + "\tMyRom";
            WithConfig("romanime_", new[] { line }, rom =>
            {
                // FRAME table: {id, wait} 4-byte records, terminator id==0xFFFF. 2 frames.
                uint frameTbl = 0x2000;
                rom.write_u32(framePtr, Ptr(frameTbl));
                rom.write_u16(frameTbl + 0, 0x0000); rom.write_u16(frameTbl + 2, 0x0001);
                rom.write_u16(frameTbl + 4, 0x0001); rom.write_u16(frameTbl + 6, 0x0001);
                rom.write_u16(frameTbl + 8, 0xFFFF); // terminator -> frameCount 2

                // TSA pointer-list: base -> [ptrA, terminator]
                uint tsaListBase = 0x2400;
                rom.write_u32(tsaPtr, Ptr(tsaListBase));
                rom.write_u32(tsaListBase + 0, Ptr(0x3000)); // TSA target 0
                rom.write_u32(tsaListBase + 4, 0x00000000);  // not a pointer -> list ends (1 entry)
                rom.write_u32(0x3000, 0x00000010);           // tiny LZ77 header

                // Image pointer-list
                uint imgListBase = 0x2500;
                rom.write_u32(imgPtr, Ptr(imgListBase));
                rom.write_u32(imgListBase + 0, Ptr(0x3100));
                rom.write_u32(imgListBase + 4, 0x00000000);
                rom.write_u32(0x3100, 0x00000010);

                // Palette pointer-list
                uint palListBase = 0x2600;
                rom.write_u32(palPtr, Ptr(palListBase));
                rom.write_u32(palListBase + 0, Ptr(0x3200));
                rom.write_u32(palListBase + 4, 0x00000000);

                var list = new List<Address>();
                RebuildProducerCore.EmitImageRomAnime(rom, list);

                // FRAME Pointer (4-byte POINTER) + FRAME BIN (frameCount*4 == 8).
                Assert.Contains(list, a => a.Info == "MyRom FRAME Pointer" && a.Length == 4 &&
                    a.DataType == Address.DataTypeEnum.POINTER);
                Assert.Contains(list, a => a.Info == "MyRom FRAME" && a.Addr == frameTbl &&
                    a.Length == 8u && a.DataType == Address.DataTypeEnum.BIN);

                // TSA / Image / Palette pointers + per-list entries.
                Assert.Contains(list, a => a.Info == "MyRom TSA Pointer" && a.Length == 4);
                Assert.Contains(list, a => a.Info == "MyRom TSA" && a.Addr == 0x3000 &&
                    a.DataType == Address.DataTypeEnum.LZ77TSA);
                Assert.Contains(list, a => a.Info == "MyRom Image" && a.Addr == 0x3100 &&
                    a.DataType == Address.DataTypeEnum.LZ77IMG);
                Assert.Contains(list, a => a.Info == "MyRom Palette" && a.Addr == 0x3200 &&
                    a.Length == 2 * 16 && a.DataType == Address.DataTypeEnum.PAL);
            });
        }

        [Fact]
        public void EmitImageRomAnime_CommonPaletteFallback_EmitsSingleResolvedBase()
        {
            // option == "COMMONPALETTE": when the palette pointer-list resolves to ZERO entries, the
            // fallback adds the single resolved base (toOffset(a)) as the lone palette.
            uint framePtr = 0x1000;
            uint tsaPtr = 0x1100;
            uint imgPtr = 0x1200;
            uint palPtr = 0x1300;
            string line = "0000\t30\tCOMMONPALETTE\t" + U.ToHexString(framePtr) + "\t"
                + U.ToHexString(tsaPtr) + "\t" + U.ToHexString(imgPtr) + "\t" + U.ToHexString(palPtr)
                + "\tCommonRom";
            WithConfig("romanime_", new[] { line }, rom =>
            {
                uint frameTbl = 0x2000;
                rom.write_u32(framePtr, Ptr(frameTbl));
                rom.write_u16(frameTbl + 0, 0xFFFF); // 0 frames (terminator immediately) -> frameCount 0

                rom.write_u32(tsaPtr, Ptr(0x2400));
                rom.write_u32(0x2400, 0x00000000); // empty list -> fallback adds base 0x2400
                rom.write_u32(imgPtr, Ptr(0x2500));
                rom.write_u32(0x2500, 0x00000000);
                // Palette: base 0x2600 with NO pointer entries -> COMMONPALETTE fallback adds 0x2600.
                rom.write_u32(palPtr, Ptr(0x2600));
                rom.write_u32(0x2600, 0x00000000);

                var list = new List<Address>();
                RebuildProducerCore.EmitImageRomAnime(rom, list);

                // The COMMONPALETTE fallback: exactly one Palette at the resolved base 0x2600.
                var pals = list.Where(a => a.Info == "CommonRom Palette").ToList();
                Assert.Single(pals);
                Assert.Equal(0x2600u, pals[0].Addr);
                Assert.Equal(2u * 16u, pals[0].Length);
            });
        }

        [Fact]
        public void EmitImageRomAnime_FixedPaletteCountFallback_WhenFramePointerBelow0x100()
        {
            // framePointer < 0x100 (a FIXED per-frame palette COUNT, not a pointer) with an empty
            // palette list -> the fallback adds one palette per frame at a + i*(2*16).
            uint tsaPtr = 0x1100;
            uint imgPtr = 0x1200;
            uint palPtr = 0x1300;
            const uint frameCountFixed = 3; // < 0x100
            string line = "0000\t30\tNNN\t" + U.ToHexString(frameCountFixed) + "\t"
                + U.ToHexString(tsaPtr) + "\t" + U.ToHexString(imgPtr) + "\t" + U.ToHexString(palPtr)
                + "\tFixedRom";
            WithConfig("romanime_", new[] { line }, rom =>
            {
                // framePointer == 3 is NOT a safe offset -> checkPonters skips the frame slot, but the
                // tsa/img/pal slots must still hold safe pointers. frameCount stays NOT_FOUND ->
                // GetFrameCountLow returns NOT_FOUND for a non-safe framePointer... which would `continue`.
                // WF: when !isSafetyOffset(framePointer), the FRAME block is skipped AND frameCount stays
                // NOT_FOUND -> `if (frameCount == NOT_FOUND) continue`. So with framePointer < 0x100 the
                // WHOLE entry is skipped. Assert that faithfully (no Palette emitted).
                rom.write_u32(tsaPtr, Ptr(0x2400));
                rom.write_u32(0x2400, Ptr(0x3000));
                rom.write_u32(0x3000, 0x00000010);
                rom.write_u32(imgPtr, Ptr(0x2500));
                rom.write_u32(0x2500, Ptr(0x3100));
                rom.write_u32(0x3100, 0x00000010);
                rom.write_u32(palPtr, Ptr(0x2600));
                rom.write_u32(0x2600, 0x00000000);

                var list = new List<Address>();
                RebuildProducerCore.EmitImageRomAnime(rom, list);

                // framePointer 3 -> isSafetyOffset false -> frameCount NOT_FOUND -> entry skipped entirely.
                Assert.DoesNotContain(list, a => a.Info != null && a.Info.StartsWith("FixedRom"));
            });
        }

        [Fact]
        public void GetRomAnimePalettePointerListCount_FixedPaletteFallback_OnePerFrame()
        {
            // Direct test of the framePointer<0x100 palette fallback (the per-frame palette layout).
            WithConfig("romanime_", null, rom =>
            {
                uint palPtr = 0x1300;
                uint paletteBase = 0x2600;
                rom.write_u32(palPtr, Ptr(paletteBase));
                rom.write_u32(paletteBase, 0x00000000); // empty list -> fallback fires

                List<uint> pals = RebuildProducerCore.GetRomAnimePalettePointerListCount(
                    rom, palPtr, framePointer: 3, option: "NNN");
                // 3 entries at base + i*(2*16).
                Assert.Equal(3, pals.Count);
                Assert.Equal(paletteBase + 0u * 32u, pals[0]);
                Assert.Equal(paletteBase + 1u * 32u, pals[1]);
                Assert.Equal(paletteBase + 2u * 32u, pals[2]);
            });
        }

        [Fact]
        public void GetRomAnimePalettePointerListCount_ElseFallback_SingleBase_WhenFramePointerLarge()
        {
            // framePointer >= 0x100 and NOT COMMONPALETTE and empty list -> single resolved base.
            WithConfig("romanime_", null, rom =>
            {
                uint palPtr = 0x1300;
                uint paletteBase = 0x2600;
                rom.write_u32(palPtr, Ptr(paletteBase));
                rom.write_u32(paletteBase, 0x00000000);

                List<uint> pals = RebuildProducerCore.GetRomAnimePalettePointerListCount(
                    rom, palPtr, framePointer: 0x1000, option: "NNN");
                Assert.Single(pals);
                Assert.Equal(paletteBase, pals[0]);
            });
        }

        [Fact]
        public void EmitImageRomAnime_MissingConfig_EmitsNothing()
        {
            WithConfig("romanime_", new[] { "//empty" }, rom =>
            {
                var list = new List<Address>();
                var ex = Record.Exception(() => RebuildProducerCore.EmitImageRomAnime(rom, list));
                Assert.Null(ex);
                Assert.Empty(list);
            });
        }

        [Fact]
        public void GetRomAnimeFrameCountLow_NearEof_NoThrow()
        {
            var rom = CreateTestRom(0x1000);
            // framePointer slot near EOF -> the +4 guard returns NOT_FOUND, no throw.
            var ex = Record.Exception(() =>
                RebuildProducerCore.GetRomAnimeFrameCountLow(rom, (uint)rom.Data.Length - 2));
            Assert.Null(ex);
        }

        [Fact]
        public void CheckRomAnimePointers_ReadsRawU32_NotP32()
        {
            // Faithfulness: WF checkPonters reads u32(slot) and isSafetyPointer(...) — it does NOT
            // p32-normalize before the check. A slot holding a NON-pointer value must fail the gate.
            var rom = CreateTestRom(0x4000);
            uint tsaPtr = 0x1100, imgPtr = 0x1200, palPtr = 0x1300;
            rom.write_u32(tsaPtr, 0x12345678); // NOT a GBA pointer -> isSafetyPointer false -> gate fails
            rom.write_u32(imgPtr, Ptr(0x2000));
            rom.write_u32(palPtr, Ptr(0x2100));
            // framePointer 0 -> isSafetyOffset(0) false -> the frame branch is skipped (WF).
            bool ok = RebuildProducerCore.CheckRomAnimePointers(rom, 0, tsaPtr, imgPtr, palPtr);
            Assert.False(ok);

            // Now make the TSA slot a real pointer -> gate passes.
            rom.write_u32(tsaPtr, Ptr(0x2200));
            Assert.True(RebuildProducerCore.CheckRomAnimePointers(rom, 0, tsaPtr, imgPtr, palPtr));
        }

        // ---- NotYetPorted coverage delta for slice 2q ----

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2qConfigForms_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();

            // slice 2q ports the four config-FILE-table forms.
            Assert.DoesNotContain("OtherTextForm", notYet);
            Assert.DoesNotContain("ImageTSAAnimeForm", notYet);
            Assert.DoesNotContain("ImageTSAAnime2Form", notYet);
            Assert.DoesNotContain("ImageRomAnimeForm", notYet);

            // The remaining image-anime siblings STAY (each blocked on a Core gap):
            //  ImageBattleAnimeForm (ImageUtilOAM + ClassForm.MakeClassList + seat-dedup),
            //  ImagePortraitForm (IsHalfBodyFlag runtime header), ImageItemIconForm (out of scope).
            Assert.Contains("ImageBattleAnimeForm", notYet);
            Assert.Contains("ImagePortraitForm", notYet);
            Assert.Contains("ImageItemIconForm", notYet);

            // no-duplicates invariant still holds.
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            Assert.Equal(raw.Length, raw.Distinct().Count());
        }
    }
}
