using System;
using System.Collections.Generic;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform ROM-rebuild struct-pointer <b>PRODUCER</b> (slice 2a of #1261).
    /// <para>
    /// This is the Core port of <c>U.MakeAllStructPointersList</c>
    /// (<c>FEBuilderGBA/U.cs</c>). The WinForms original walks ~150
    /// <c>XxxForm.MakeAllDataLength(list)</c> statics to enumerate every known
    /// data/pointer struct location in the ROM, producing the <see cref="Address"/>
    /// list that <see cref="RebuildMakeCore.Make"/> consumes.
    /// </para>
    /// <para><b>What this slice ports</b></para>
    /// <list type="bullet">
    ///   <item>The list-assembly plumbing and the 5 progress/cancel checkpoints
    ///   (the WinForms <c>21× InputFormRef.DoEvents(null,...)</c> calls become
    ///   <see cref="IProgress{T}"/> reports + a <see cref="CancellationToken"/>;
    ///   on cancel the partial list is returned, matching the WinForms
    ///   <c>return list;</c> behaviour).</item>
    ///   <item>A first batch of the <b>simplest</b> <c>MakeAllDataLength</c> statics
    ///   — the ones that are a pure "walk a <c>RomInfo</c> pointer table of
    ///   N entries × blockSize, emit one IFR <see cref="Address"/>" with a
    ///   small data-driven <c>IsDataExists</c> rule and no Form/Drawing/event-scan
    ///   dependency. They are expressed as a declarative <see cref="StructDescriptor"/>
    ///   table walked by <see cref="WalkAndAdd"/> — one table replaces ~16 hand-ports.</item>
    /// </list>
    /// <para><b>What is intentionally deferred</b></para>
    /// <para>
    /// The remaining ~130 statics need their editor's data-read logic (Huffman text,
    /// LZ77/TSA image length, event/AI/procs disassembly, song recycle, battle-anime
    /// frame walkers, patch/ASM LDR maps, embedded sub-pointer expansion). They are
    /// <b>not silently omitted</b>: <see cref="GetNotYetPortedForms"/> enumerates them
    /// and the producer reports the count through <paramref name="progress"/> so a later
    /// slice can diff coverage. <see cref="RebuildMakeCore.Make"/>'s signature is
    /// unchanged — this producer is made available for a later slice to wire in.
    /// </para>
    /// <para>
    /// WinForms touches replaced: <c>Program.ROM</c> -&gt; the <c>rom</c> parameter;
    /// each <c>XxxForm.Init(null)</c>'s <c>{BasePointer, BlockSize, IsDataExists}</c>
    /// is captured in the descriptor; <c>InputFormRef.DoEvents</c> -&gt;
    /// <c>progress</c>/<c>ct</c>.
    /// </para>
    /// </summary>
    public static class RebuildProducerCore
    {
        /// <summary>How a descriptor reproduces the per-form <c>IsDataExists</c> callback
        /// that drives <see cref="ROM.getBlockDataCount(uint,uint,Func{int,uint,bool})"/>.</summary>
        public enum DataCountRule
        {
            /// <summary>Fixed entry count (e.g. <c>i &lt; unit_maxcount</c>, <c>i &lt; 8</c>).</summary>
            FixedCount,
            /// <summary>Stop when <c>u8(addr+Offset)</c> equals <see cref="StructDescriptor.RuleStopValue"/>.</summary>
            U8NotEqual,
            /// <summary>Stop when <c>u16(addr+Offset)</c> equals <see cref="StructDescriptor.RuleStopValue"/>.</summary>
            U16NotEqual,
            /// <summary>Continue while <c>u8(addr+Offset) != 0</c> — but entry 0 always exists.
            /// Matches ClassForm (<c>i==0 -&gt; true</c>, else <c>u8(addr+4)!=0</c>).</summary>
            U8NotZeroIndex0Always,
            /// <summary>Continue while <c>u32(addr+Offset)</c> is a pointer-or-NULL.</summary>
            U32IsPointerOrNull,
            /// <summary>Continue while <c>u16(addr+Offset) != 0</c>.</summary>
            U16NotZero,
            /// <summary>Item rule: <c>u32(addr+12)</c> pointer-or-null, plus <c>u32(addr+16)</c>
            /// pointer-or-null EXCEPT on FE8U (version 8 &amp;&amp; !multibyte). Capped at i&lt;=0xFF.</summary>
            ItemRule,
        }

        /// <summary>A declarative description of one simple "table walk + emit IFR Address" form.</summary>
        public sealed class StructDescriptor
        {
            /// <summary>Label emitted into the <see cref="Address.Info"/> (was the form's name string).</summary>
            public string Name;
            /// <summary>Resolves the <c>RomInfo</c> base pointer (e.g. <c>r =&gt; r.RomInfo.item_pointer</c>).
            /// For multi-pointer forms (ItemPromotion, ArenaClass) use <see cref="PointerFields"/>.</summary>
            public Func<ROM, uint> PointerField;
            /// <summary>Multi-pointer variant: emit one Address per non-zero pointer.
            /// When set, <see cref="PointerField"/> is ignored.</summary>
            public Func<ROM, uint[]> PointerFields;
            public uint BlockSize;
            public DataCountRule Rule;
            /// <summary>Byte offset inside the block the rule inspects.</summary>
            public uint RuleOffset;
            /// <summary>For <see cref="DataCountRule.FixedCount"/>: the count (or use <see cref="FixedCountField"/>).</summary>
            public uint RuleFixedCount;
            /// <summary>For <see cref="DataCountRule.FixedCount"/> when the count comes from RomInfo
            /// (e.g. <c>unit_maxcount</c>); takes precedence over <see cref="RuleFixedCount"/>.</summary>
            public Func<ROM, uint> FixedCountField;
            /// <summary>For the <c>*NotEqual</c> rules: the terminator value.</summary>
            public uint RuleStopValue;
            /// <summary>Byte offsets inside the block that hold pointers (the WinForms <c>pointerIndexes</c>).</summary>
            public uint[] PointerIndexes;
            public Address.DataTypeEnum DataType = Address.DataTypeEnum.InputFormRef;
            /// <summary>Optional safety cap on entry count (the WinForms <c>i &gt; 0xff</c> guards).</summary>
            public uint MaxCount = 0x10000;
        }

        /// <summary>
        /// Build the known-struct <see cref="Address"/> list for <paramref name="rom"/>.
        /// Port of <c>U.MakeAllStructPointersList</c> (the batch ported so far; see class remarks).
        /// </summary>
        /// <param name="rom">The ROM to scan (was <c>Program.ROM</c>).</param>
        /// <param name="progress">Optional progress reporter (was the <c>DoEvents</c> messages).</param>
        /// <param name="ct">Cancellation token; on cancel the partial list is returned (was the
        /// <c>DoEvents</c> early-<c>return list</c>).</param>
        /// <returns>The accumulated <see cref="Address"/> list.</returns>
        public static List<Address> MakeAllStructPointersList(ROM rom, IProgress<string> progress = null, CancellationToken ct = default)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));

            var list = new List<Address>(50000);

            // A pre-cancelled token short-circuits before any work (mirrors the WinForms
            // first DoEvents returning the empty list).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return list;
            }

            // The WinForms producer interleaves DoEvents checkpoints between groups of
            // statics. We keep the SAME checkpoint boundaries so a later parity slice can
            // line them up. ct.IsCancellationRequested mirrors a DoEvents cancel.
            List<StructDescriptor> batch = BuildBatchDescriptors(rom);
            foreach (StructDescriptor d in batch)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return list;
                }
                progress?.Report(d.Name);
                WalkAndAdd(rom, list, d);
            }

            // Surface — never silently drop — the statics this slice does not yet cover.
            string[] notYet = GetNotYetPortedForms();
            progress?.Report("MakeAllStructPointersList: ported batch=" + batch.Count
                + " descriptors; not-yet-ported=" + notYet.Length + " forms (deferred to later slices)");

            return list;
        }

        /// <summary>
        /// Walk one descriptor's table(s) and emit the IFR <see cref="Address"/>(es),
        /// reproducing <c>InputFormRef.Init</c> + <c>AddressWinForms.AddAddress</c>
        /// (length = blockSize × (dataCount + 1)) entirely from the passed <paramref name="rom"/>.
        /// </summary>
        public static void WalkAndAdd(ROM rom, List<Address> list, StructDescriptor d)
        {
            if (d.PointerFields != null)
            {
                foreach (uint pointer in d.PointerFields(rom))
                {
                    if (pointer == 0)
                    {
                        continue;
                    }
                    EmitOne(rom, list, d, pointer);
                }
            }
            else
            {
                uint pointer = d.PointerField(rom);
                EmitOne(rom, list, d, pointer);
            }
        }

        static void EmitOne(ROM rom, List<Address> list, StructDescriptor d, uint pointer)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr))
            {
                return;
            }

            uint dataCount = rom.getBlockDataCount(baseAddr, d.BlockSize, MakeIsDataExists(rom, d));
            // WinForms AddressWinForms.AddAddress: length = BlockSize * (DataCount + 1).
            uint length = d.BlockSize * (dataCount + 1);

            list.Add(new Address(baseAddr, length, pointer, d.Name, d.DataType, d.BlockSize, d.PointerIndexes));
        }

        /// <summary>Turn a descriptor's <see cref="DataCountRule"/> into the
        /// <c>is_data_exists_callback</c> that <see cref="ROM.getBlockDataCount(uint,uint,Func{int,uint,bool})"/> expects.</summary>
        static Func<int, uint, bool> MakeIsDataExists(ROM rom, StructDescriptor d)
        {
            switch (d.Rule)
            {
                case DataCountRule.FixedCount:
                {
                    uint count = d.FixedCountField != null ? d.FixedCountField(rom) : d.RuleFixedCount;
                    return (i, addr) => i < count;
                }
                case DataCountRule.U8NotEqual:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        return rom.u8(addr + d.RuleOffset) != d.RuleStopValue;
                    };
                case DataCountRule.U16NotEqual:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        return rom.u16(addr + d.RuleOffset) != d.RuleStopValue;
                    };
                case DataCountRule.U8NotZeroIndex0Always:
                    return (i, addr) =>
                    {
                        if (i == 0) return true;
                        if (i >= d.MaxCount) return false;
                        return rom.u8(addr + d.RuleOffset) != 0;
                    };
                case DataCountRule.U32IsPointerOrNull:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        return U.isPointerOrNULL(rom.u32(addr + d.RuleOffset));
                    };
                case DataCountRule.U16NotZero:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        return rom.u16(addr + d.RuleOffset) != 0;
                    };
                case DataCountRule.ItemRule:
                    return (i, addr) =>
                    {
                        if (i > 0xff) return false;
                        if (rom.RomInfo.version == 8 && rom.RomInfo.is_multibyte == false)
                        {
                            // FE8U: only the +12 stat-booster pointer is checked (the +16
                            // effectiveness pointer is left to SkillSystems, per ItemForm.Init).
                            return U.isPointerOrNULL(rom.u32(addr + 12));
                        }
                        return U.isPointerOrNULL(rom.u32(addr + 12))
                            && U.isPointerOrNULL(rom.u32(addr + 16));
                    };
                default:
                    return (i, addr) => false;
            }
        }

        /// <summary>
        /// The first-batch descriptor table. These are the <c>MakeAllDataLength</c> statics that
        /// are a pure table-walk with a simple <c>IsDataExists</c> and no editor-specific
        /// (Huffman/LZ77/disasm/event-scan/embedded-sub-pointer) logic. Order follows the
        /// WinForms <c>U.MakeAllStructPointersList</c> call order where these forms appear.
        /// </summary>
        public static List<StructDescriptor> BuildBatchDescriptors(ROM rom)
        {
            var l = new List<StructDescriptor>();

            // ---- version-agnostic section (called unconditionally in WinForms) ----

            // ItemForm.MakeAllDataLength
            l.Add(new StructDescriptor
            {
                Name = "Item",
                PointerField = r => r.RomInfo.item_pointer,
                BlockSize = rom.RomInfo.item_datasize,
                Rule = DataCountRule.ItemRule,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { 12, 16 },
            });

            // ItemPromotionForm.MakeAllDataLength (10 cc_* pointers, blockSize 1, u8!=0)
            l.Add(new StructDescriptor
            {
                Name = "CCItem",
                PointerFields = r => new uint[]
                {
                    r.RomInfo.cc_item_hero_crest_pointer,
                    r.RomInfo.cc_item_knight_crest_pointer,
                    r.RomInfo.cc_item_orion_bolt_pointer,
                    r.RomInfo.cc_elysian_whip_pointer,
                    r.RomInfo.cc_guiding_ring_pointer,
                    r.RomInfo.cc_fallen_contract_pointer,
                    r.RomInfo.cc_master_seal_pointer,
                    r.RomInfo.cc_ocean_seal_pointer,
                    r.RomInfo.cc_moon_bracelet_pointer,
                    r.RomInfo.cc_sun_bracelet_pointer,
                },
                BlockSize = 1,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            });

            // SupportAttributeForm.MakeAllDataLength
            l.Add(new StructDescriptor
            {
                Name = "SupportAttribute",
                PointerField = r => r.RomInfo.support_attribute_pointer,
                BlockSize = 8,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            });

            // UnitPaletteForm.MakeAllDataLength (color + class palette tables; fixed unit_maxcount)
            l.Add(new StructDescriptor
            {
                Name = "UnitPalette",
                PointerField = r => r.RomInfo.unit_palette_color_pointer,
                BlockSize = 7,
                Rule = DataCountRule.FixedCount,
                FixedCountField = r => r.RomInfo.unit_maxcount,
                PointerIndexes = new uint[] { },
            });
            l.Add(new StructDescriptor
            {
                Name = "UnitPalette",
                PointerField = r => r.RomInfo.unit_palette_class_pointer,
                BlockSize = 7,
                Rule = DataCountRule.FixedCount,
                FixedCountField = r => r.RomInfo.unit_maxcount,
                PointerIndexes = new uint[] { },
            });

            // AITargetForm.MakeAllDataLength (fixed 8)
            l.Add(new StructDescriptor
            {
                Name = "AITarget",
                PointerField = r => r.RomInfo.ai3_pointer,
                BlockSize = 20,
                Rule = DataCountRule.FixedCount,
                RuleFixedCount = 8,
                PointerIndexes = new uint[] { },
            });

            // AIStealItemForm.MakeAllDataLength (u8 != 0xFF)
            l.Add(new StructDescriptor
            {
                Name = "AIStealItem",
                PointerField = r => r.RomInfo.ai_steal_item_pointer,
                BlockSize = 2,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0xFF,
                PointerIndexes = new uint[] { },
            });

            // ArenaClassForm.MakeAllDataLength (3 weapon-class pointers, blockSize 1, u8!=0)
            l.Add(new StructDescriptor
            {
                Name = "AreaClassForm weapon",
                PointerFields = r => new uint[]
                {
                    r.RomInfo.arena_class_near_weapon_pointer,
                    r.RomInfo.arena_class_far_weapon_pointer,
                    r.RomInfo.arena_class_magic_weapon_pointer,
                },
                BlockSize = 1,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            });

            // ItemWeaponTriangleForm.MakeAllDataLength (u8 != 255)
            l.Add(new StructDescriptor
            {
                Name = "ItemWeaponTriangle",
                PointerField = r => r.RomInfo.item_cornered_pointer,
                BlockSize = 4,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0xFF,
                PointerIndexes = new uint[] { },
            });

            // ---- per-version Class table (same descriptor for v6/7/8) ----
            // ClassForm.MakeAllDataLength: i==0 -> true, else u8(addr+4)!=0, cap 0xFF.
            l.Add(new StructDescriptor
            {
                Name = "Class",
                PointerField = r => r.RomInfo.class_pointer,
                BlockSize = rom.RomInfo.class_datasize,
                Rule = DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { },
            });

            // ---- trailing is_multibyte branch: MapTerrainName(Eng) ----
            // WinForms: is_multibyte -> MapTerrainNameForm (embedded CString sub-walk, deferred);
            //           else        -> MapTerrainNameEngForm (clean u16!=0 table).
            if (rom.RomInfo.is_multibyte == false)
            {
                l.Add(new StructDescriptor
                {
                    Name = "TerrainEng",
                    PointerField = r => r.RomInfo.map_terrain_name_pointer,
                    BlockSize = 2,
                    Rule = DataCountRule.U16NotZero,
                    RuleOffset = 0,
                    PointerIndexes = new uint[] { },
                });
            }

            return l;
        }

        /// <summary>
        /// The <c>MakeAllDataLength</c> statics from <c>U.MakeAllStructPointersList</c> /
        /// <c>U.AppendAllASMStructPointersList</c> that this slice does <b>not</b> yet port.
        /// Tracked explicitly so coverage is auditable and nothing is silently dropped.
        /// Each needs editor-specific logic (Huffman text, LZ77/TSA image length, event/AI/procs
        /// disasm, song/instrument recycle, battle-anime frame walk, patch/ASM LDR map, or
        /// embedded sub-pointer / event-scan expansion) to be extracted into Core first.
        /// </summary>
        public static string[] GetNotYetPortedForms()
        {
            return new[]
            {
                // event conditions / scripts
                "EventCondForm", "EventScript(MakeEventASMMAPList)", "EventFunctionPointerForm",
                "Command85PointerForm",
                // text (Huffman)
                "TextForm", "TextCharCodeForm", "TextDicForm", "OtherTextForm", "MapTerrainNameForm",
                // images (LZ77/TSA length calc)
                "ImageBattleAnimeForm", "ImageBattleBGForm", "ImageBattleTerrainForm", "ImageBGForm",
                "ImageMagicFEditorForm", "ImageMagicCSACreatorForm", "ImageBattleScreenForm",
                "ImageItemIconForm", "ImageUnitMoveIconFrom", "ImageUnitWaitIconFrom",
                "ImageUnitPaletteForm", "ImageSystemIconForm", "ImageRomAnimeForm",
                "ImageGenericEnemyPortraitForm", "ImageMapActionAnimationForm", "ImageTSAAnimeForm",
                "ImageTSAAnime2Form", "ImageChapterTitleForm", "ImagePortraitForm", "ImageCGForm",
                "MapMiniMapTerrainImageForm", "WorldMapImageForm",
                // songs / sound (recycle, embedded inst)
                "SongTableForm", "SoundFootStepsForm", "SoundRoomForm", "SoundRoomCGForm",
                "WorldMapBGMForm",
                // embedded sub-pointer / event-scan / CString expansion
                "ItemShopForm", "StatusParamForm", "StatusRMenuForm", "MenuDefinitionForm",
                "ItemWeaponEffectForm", "ItemUsagePointerForm", "UnitActionPointerForm",
                "MapChangeForm", "MapExitPointForm", "MapPointerForm", "FontForm",
                // AI scripts (disasm)
                "AIScriptForm", "AIMapSettingForm", "AIPerformStaffForm", "AIPerformItemForm",
                "ArenaEnemyWeaponForm",
                // skills (version/patch dependent)
                "SkillAssignmentClassSkillSystemForm", "SkillAssignmentUnitSkillSystemForm",
                "SkillConfigSkillSystemForm", "SkillConfigFE8NSkillForm",
                "SkillConfigFE8NVer2SkillForm", "SkillConfigFE8NVer3SkillForm",
                // status / menu definition / misc tables needing extra logic
                "StatusOptionForm", "StatusOptionOrderForm", "StatusUnitsMenuForm",
                "LinkArenaDenyUnitForm", "MantAnimationForm", "MapTileAnimation1Form",
                "MapTileAnimation2Form", "MapTerrainFloorLookupTableForm",
                "MapTerrainBGLookupTableForm",
                // units / classes per-version with extra reads
                "UnitForm", "UnitFE7Form", "UnitFE6Form", "UnitCustomBattleAnimeForm",
                "ExtraUnitForm", "ExtraUnitFE8UForm", "SummonUnitForm", "SummonsDemonKingForm",
                "EventUnitForm(RecycleReserveUnits)", "EventForceSortieForm",
                // monster / world map / ED / support (FE8/FE7/FE6 variants)
                "MonsterItemForm", "MonsterProbabilityForm", "MonsterWMapProbabilityForm",
                "EDForm", "EventBattleTalkForm", "CCBranchForm", "OPClassAlphaNameForm",
                "WorldMapPathForm", "WorldMapEventPointerForm", "EDStaffRollForm",
                "OPPrologueForm", "EventHaikuForm", "SoundRoomForm", "SupportTalkForm",
                "SupportUnitForm", "WorldMapPointForm", "MapSettingForm",
                "OPClassFontForm", "OPClassDemoForm", "FE8SpellMenuExtendsForm",
                "WorldMapPointForm", "TacticianAffinityFE7", "EventFinalSerifFE7Form",
                "EDSensekiCommentForm", "OPClassDemoFE7Form", "ImageCGFE7UForm",
                // patch / procs / ASM (AppendAllASMStructPointersList)
                "PatchForm(MakePatchStructDataList)", "ProcsScriptForm",
            };
        }
    }
}
