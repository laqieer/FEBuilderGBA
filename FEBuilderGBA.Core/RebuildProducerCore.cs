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
    ///   small data-driven <c>IsDataExists</c> rule, no Form/Drawing/event-scan
    ///   dependency AND <b>no embedded per-entry sub-pointer expansion</b>
    ///   (ItemForm/ClassForm are excluded for exactly that reason — see
    ///   <see cref="BuildBatchDescriptors"/>). They are expressed as a declarative
    ///   <see cref="StructDescriptor"/> table walked by <see cref="WalkAndAdd"/>.</item>
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
        /// Result of <see cref="MakeAllStructPointers"/>: the produced <see cref="Address"/> list
        /// plus the explicit coverage state so a future wiring slice can gate on it.
        /// </summary>
        /// <remarks>
        /// COMPLETENESS-SAFETY: a producer that omits forms (<see cref="IsComplete"/> false) must NOT
        /// be fed to <see cref="RebuildMakeCore.Make"/> for a real defragment — relocating only SOME
        /// structs while leaving others un-tracked would leave the un-tracked pointers dangling
        /// (silent ROM corruption). The wiring slice MUST refuse (or loudly warn on) a rebuild while
        /// <see cref="IsComplete"/> is false / <see cref="NotYetPorted"/> is non-empty.
        /// </remarks>
        public sealed class ProducerResult
        {
            /// <summary>The accumulated known-struct list (may be partial if <see cref="Cancelled"/>).</summary>
            public List<Address> List { get; }
            /// <summary>The <c>MakeAllDataLength</c> statics NOT yet ported (see <see cref="GetNotYetPortedForms"/>).</summary>
            public IReadOnlyList<string> NotYetPorted { get; }
            /// <summary>True only when every WinForms producer static has a Core equivalent
            /// (<see cref="NotYetPorted"/> empty). While false the result is UNSAFE to feed to a
            /// real <see cref="RebuildMakeCore.Make"/> defragment.</summary>
            public bool IsComplete => NotYetPorted.Count == 0;
            /// <summary>True if cancellation was observed mid-run (the list is partial).</summary>
            public bool Cancelled { get; }

            public ProducerResult(List<Address> list, IReadOnlyList<string> notYetPorted, bool cancelled)
            {
                List = list;
                NotYetPorted = notYetPorted;
                Cancelled = cancelled;
            }
        }

        /// <summary>
        /// Build the known-struct <see cref="Address"/> list for the loaded <paramref name="rom"/>.
        /// Port of <c>U.MakeAllStructPointersList</c> (the batch ported so far; see class remarks).
        /// </summary>
        /// <param name="rom">The ROM to scan. MUST be the loaded <see cref="CoreState.ROM"/>
        /// (see <see cref="MakeAllStructPointers"/> for why).</param>
        /// <param name="progress">Optional progress reporter (was the <c>DoEvents</c> messages).</param>
        /// <param name="ct">Cancellation token; on cancel the partial list is returned (was the
        /// <c>DoEvents</c> early-<c>return list</c>).</param>
        /// <returns>The accumulated <see cref="Address"/> list.</returns>
        public static List<Address> MakeAllStructPointersList(ROM rom, IProgress<string> progress = null, CancellationToken ct = default)
        {
            return MakeAllStructPointers(rom, progress, ct).List;
        }

        /// <summary>
        /// Like <see cref="MakeAllStructPointersList"/> but returns a <see cref="ProducerResult"/> that
        /// also surfaces the coverage state (<see cref="ProducerResult.NotYetPorted"/> /
        /// <see cref="ProducerResult.IsComplete"/>) so a future wiring slice can gate the rebuild.
        /// </summary>
        /// <param name="rom">
        /// The ROM to scan. MUST be the same instance as <see cref="CoreState.ROM"/>. The emitted
        /// <see cref="Address"/> objects validate their length via <c>U.isSafetyLength</c>, which is
        /// bound to <c>CoreState.ROM.Data.Length</c> (as is the <see cref="Address"/> ctor's
        /// <c>Debug.Assert</c>), so a <paramref name="rom"/> that is not <see cref="CoreState.ROM"/>
        /// cannot be scanned correctly. The producer's job is to enumerate the LOADED ROM being
        /// rebuilt — which is always <see cref="CoreState.ROM"/> (same posture as the
        /// patch/custom-build cores that require the loaded ROM for their undo). Passing any other
        /// instance throws <see cref="ArgumentException"/>.
        /// </param>
        public static ProducerResult MakeAllStructPointers(ROM rom, IProgress<string> progress = null, CancellationToken ct = default)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (!ReferenceEquals(rom, CoreState.ROM))
            {
                throw new ArgumentException(
                    "RebuildProducerCore must scan the loaded CoreState.ROM (Address length/offset "
                    + "validation is bound to CoreState.ROM, so a different ROM cannot be scanned "
                    + "safely).", nameof(rom));
            }

            var list = new List<Address>(50000);
            string[] notYet = GetNotYetPortedForms();

            // A pre-cancelled token short-circuits before any work (mirrors the WinForms
            // first DoEvents returning the empty list).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
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
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report(d.Name);
                WalkAndAdd(rom, list, d);
            }

            // Surface — never silently drop — the statics this slice does not yet cover.
            progress?.Report("MakeAllStructPointersList: ported batch=" + batch.Count
                + " descriptors; not-yet-ported=" + notYet.Length + " forms (deferred to later slices)");

            return new ProducerResult(list, notYet, cancelled: false);
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
            // BlockSize 0 (e.g. an uninitialized descriptor) would make getBlockDataCount loop
            // forever (addr += 0 never advances). A zero block is a descriptor bug, not data —
            // skip it rather than hang. (DataCountRule.FixedCount is fine with block 0 in theory,
            // but every real table has a positive block, so reject defensively.)
            if (d.BlockSize == 0)
            {
                return;
            }

            // rom is guaranteed == CoreState.ROM by the MakeAllStructPointers guard, so the
            // (addr, rom) safety overloads agree with the Address ctor's CoreState.ROM-bound checks.
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
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
                    // An unhandled DataCountRule is a PROGRAMMING ERROR (a bad/new descriptor),
                    // not a 0-entry table. Returning always-false would silently emit a 1-block
                    // Address and hide the bug — fail loudly instead.
                    throw new ArgumentOutOfRangeException(nameof(d),
                        "Unhandled DataCountRule: " + d.Rule);
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
            //
            // NOTE: ItemForm and ClassForm are deliberately NOT here. Their MakeAllDataLength
            // does the main IFR AddAddress AND an embedded per-entry sub-pointer expansion
            // (Item: StatBooster + ItemEffectiveness BIN blocks; Class: 7 MoveCost BIN blocks
            // behind offsets 56/60/64/68/72/76). A descriptor only emits the main table — porting
            // the main walk alone would leave those embedded blocks un-tracked, dangling their
            // pointers during a rebuild (silent ROM corruption). They stay in GetNotYetPortedForms
            // until the sub-walk is extracted to Core (a later slice).

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

            // AITargetForm.MakeAllDataLength (fixed 8) — WinForms Info label is "AI3"
            l.Add(new StructDescriptor
            {
                Name = "AI3",
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

            // ArenaClassForm.MakeAllDataLength emits THREE SEPARATE base tables (near/far/magic),
            // each via its own ReInitPointer + AddAddress with a DISTINCT Info string — NOT one
            // table with 3 pointer columns. So they are 3 single-pointer descriptors with the
            // verbatim WF Info strings, in WF order (near, far, magic). Collapsing them into one
            // multi-pointer descriptor would give all three the same Info (faithfulness break).
            l.Add(new StructDescriptor
            {
                Name = "AreaClassForm near weapon",
                PointerField = r => r.RomInfo.arena_class_near_weapon_pointer,
                BlockSize = 1,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            });
            l.Add(new StructDescriptor
            {
                Name = "AreaClassForm far weapon",
                PointerField = r => r.RomInfo.arena_class_far_weapon_pointer,
                BlockSize = 1,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0x00,
                PointerIndexes = new uint[] { },
            });
            l.Add(new StructDescriptor
            {
                Name = "AreaClassForm magic weapon",
                PointerField = r => r.RomInfo.arena_class_magic_weapon_pointer,
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

            // (ClassForm is NOT here — see the embedded-sub-pointer note above; it stays in
            //  GetNotYetPortedForms until its MoveCost sub-walk is ported.)

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
            // Defensively de-dup: duplicates would inflate the count and make the IsComplete
            // gate ("empty == safe to wire into a real defragment") unreliable. The
            // RebuildProducerCoreTests.GetNotYetPortedForms_HasNoDuplicates test also asserts
            // the raw literal itself stays duplicate-free.
            return System.Linq.Enumerable.ToArray(
                   System.Linq.Enumerable.Distinct(NotYetPortedRaw));
        }

        /// <summary>The un-deduplicated source list (exposed so a test can assert it has no
        /// duplicates — keeping the literal clean, not just the public dedup'd view).</summary>
        public static string[] GetNotYetPortedFormsRaw() => (string[])NotYetPortedRaw.Clone();

        static readonly string[] NotYetPortedRaw = new[]
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
                // (ItemForm = main IFR + per-entry StatBooster/ItemEffectiveness BIN sub-blocks;
                //  ClassForm = main IFR + per-entry 7x MoveCost BIN sub-blocks @ off 56..76 —
                //  the main walk is trivial but the sub-blocks need porting before either is safe.)
                "ItemForm", "ClassForm",
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
                "OPPrologueForm", "EventHaikuForm", "SupportTalkForm",
                "SupportUnitForm", "WorldMapPointForm", "MapSettingForm",
                "OPClassFontForm", "OPClassDemoForm", "FE8SpellMenuExtendsForm",
                "TacticianAffinityFE7", "EventFinalSerifFE7Form",
                "EDSensekiCommentForm", "OPClassDemoFE7Form", "ImageCGFE7UForm",
                // patch / procs / ASM (AppendAllASMStructPointersList)
                "PatchForm(MakePatchStructDataList)", "ProcsScriptForm",
            };
    }
}
