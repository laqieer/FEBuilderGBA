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
            /// <summary>Continue while <c>u32(addr+Offset) &lt; <see cref="StructDescriptor.RuleStopValue"/></c>.
            /// Matches StatusUnitsMenuForm (<c>u32(addr+0) &lt; 0xFF</c>).</summary>
            U32LessThan,
            /// <summary>SoundBossBGM rule: stop when <c>u16(addr) == 0xFFFF</c>, OR when
            /// <c>i &gt; 10 &amp;&amp; rom.IsEmpty(addr, BlockSize*10)</c>. Reproduces
            /// <c>SoundBossBGMForm.Init</c> verbatim (the trailing-empty guard prevents the
            /// walk from running off the end of an un-terminated table).</summary>
            SoundBossBGMRule,
            /// <summary>Continue while <c>U.isPointer(u32(addr+Offset))</c> (a non-NULL ROM
            /// pointer). Matches <c>StatusParamForm.Init</c> (<c>U.isPointer(u32(addr+12))</c>).
            /// Distinct from <see cref="U32IsPointerOrNull"/>: a NULL slot terminates here.</summary>
            PointerAt,
            /// <summary>Continue while <c>U.isPointerOrNULL(u32(addr+Offset))</c>. Matches
            /// <c>MapTerrainNameForm.Init</c> (<c>U.isPointerOrNULL(u32(addr+0))</c>). NULL is
            /// accepted (unlike <see cref="PointerAt"/>).</summary>
            PointerOrNullAt,
        }

        /// <summary>How a <see cref="SubWalk"/> reproduces the per-entry embedded-data
        /// <see cref="Address"/> that the WinForms <c>MakeAllDataLength</c> emits behind an
        /// embedded pointer field (<c>p32(p + EmbeddedPointerOffset)</c>).</summary>
        public enum SubKind
        {
            /// <summary>A NUL-terminated C string. Emits via <see cref="Address.AddCString"/>
            /// verbatim (length = <c>strlen + 1</c>, type <see cref="Address.DataTypeEnum.CSTRING"/>).
            /// Matches <c>StatusParamForm</c> (<c>Address.AddCString(list, p + 12)</c>).</summary>
            CString,
            /// <summary>A string-derived BIN block whose length is the decoded string length with
            /// <b>NO</b> trailing-NUL +1 (type <see cref="Address.DataTypeEnum.BIN"/>). Matches
            /// <c>MapTerrainNameForm</c>/<c>OtherTextForm</c>
            /// (<c>AddAddress(nameAddr, (uint)length, p + 0, name, BIN)</c>) — deliberately
            /// distinct from <see cref="CString"/>, which adds the +1.</summary>
            BinString,
            /// <summary>A fixed-size BIN block (type <see cref="Address.DataTypeEnum.BIN"/>).
            /// Matches <c>ClassForm</c> MoveCost (<c>AddPointer(p + 56, 66, "MoveCost ...", BIN)</c>).</summary>
            BinFixed,
        }

        /// <summary>One per-entry embedded-data sub-walk applied to every entry of a
        /// <see cref="StructDescriptor"/>'s table (after the main IFR <see cref="Address"/> is
        /// emitted). Reproduces the embedded-pointer expansion in the WinForms
        /// <c>MakeAllDataLength</c>: for entry base <c>p</c> the embedded pointer at
        /// <c>p + EmbeddedPointerOffset</c> targets a CString / string-BIN / fixed-BIN block, and
        /// <b>both</b> the pointer FIELD (tracked via the descriptor's <see cref="StructDescriptor.PointerIndexes"/>)
        /// AND the pointed-at DATA (this sub-walk) must be relocated during a rebuild — missing
        /// either dangles the pointer (silent corruption).</summary>
        public sealed class SubWalk
        {
            /// <summary>Byte offset inside the entry block holding the embedded pointer
            /// (the WinForms <c>p + N</c>).</summary>
            public uint EmbeddedPointerOffset;
            /// <summary>What kind of data the embedded pointer targets.</summary>
            public SubKind Kind;
            /// <summary>For <see cref="SubKind.BinFixed"/>: the fixed block length
            /// (e.g. MoveCost = 66). Ignored for the string kinds.</summary>
            public uint FixedLength;
            /// <summary>Builds the <see cref="Address.Info"/> label for entry <c>i</c>
            /// (was the WinForms per-entry name string). For <see cref="SubKind.CString"/> the
            /// label is taken from the decoded string itself (matching
            /// <see cref="Address.AddCString"/>), so this is only used by the BIN kinds.</summary>
            public Func<ROM, uint, string> Name;
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
            /// <summary>Optional per-entry embedded-data sub-walks (null = none, back-compat with
            /// the slice-2a/2b flat descriptors). Applied to every table entry AFTER the main IFR
            /// <see cref="Address"/> is emitted.</summary>
            public List<SubWalk> SubWalks;
            /// <summary>Optional standalone fixed-size BIN pointers emitted ONCE per descriptor
            /// (not per entry). Each is a <c>RomInfo</c> pointer whose target is a fixed-length
            /// BIN block — reproduces <c>ClassForm</c>'s three全クラス共通 terrain pointers
            /// (terrain_recovery / terrain_bad_status_recovery / terrain_show_infomation), each a
            /// 66-byte BIN via <c>Address.AddPointer(..., 66, name, BIN)</c>.</summary>
            public ExtraFixedPointer[] ExtraFixedPointers;
        }

        /// <summary>A standalone fixed-size BIN pointer emitted once per descriptor (see
        /// <see cref="StructDescriptor.ExtraFixedPointers"/>).</summary>
        public sealed class ExtraFixedPointer
        {
            /// <summary>Resolves the <c>RomInfo</c> pointer field (e.g.
            /// <c>r =&gt; r.RomInfo.terrain_recovery_pointer</c>).</summary>
            public Func<ROM, uint> PointerField;
            /// <summary>Fixed BIN block length (e.g. 66).</summary>
            public uint FixedLength;
            /// <summary>The <see cref="Address.Info"/> label.</summary>
            public string Name;
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

            // Standalone fixed-size BIN pointers emitted ONCE per descriptor (not per entry).
            // ClassForm: after the per-entry MoveCost loop, three 全クラス共通 terrain pointers
            // (terrain_recovery / terrain_bad_status_recovery / terrain_show_infomation), each a
            // 66-byte BIN via Address.AddPointer. Their pointer FIELDS live in RomInfo (relocated
            // by the rebuild's RomInfo-pointer pass), and their DATA is tracked here.
            if (d.ExtraFixedPointers != null)
            {
                foreach (ExtraFixedPointer ep in d.ExtraFixedPointers)
                {
                    Address.AddPointer(list, ep.PointerField(rom), ep.FixedLength, ep.Name,
                        Address.DataTypeEnum.BIN);
                }
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

            // Per-entry embedded-data sub-walks (slice 2c). The WinForms MakeAllDataLength runs
            // these inside the `for (i < DataCount)` loop right after the main AddAddress, over the
            // SAME dataCount getBlockDataCount just returned — so the un-tracked embedded blocks
            // (MoveCost / CString / string-BIN) behind the entry pointer fields get relocated too.
            if (d.SubWalks != null)
            {
                EmitSubWalks(rom, list, d, baseAddr, dataCount);
            }
        }

        /// <summary>
        /// For each table entry (<c>p = baseAddr + i*BlockSize</c>, <c>i &lt; dataCount</c>) and each
        /// <see cref="SubWalk"/>, emit the embedded-data <see cref="Address"/> behind the entry's
        /// embedded pointer (<c>p32(p + EmbeddedPointerOffset)</c>). Reproduces the WinForms
        /// per-entry expansion VERBATIM per <see cref="SubKind"/> (CString = strlen+1 CSTRING;
        /// BinString = strlen BIN, no +1; BinFixed = FixedLength BIN).
        /// </summary>
        static void EmitSubWalks(ROM rom, List<Address> list, StructDescriptor d, uint baseAddr, uint dataCount)
        {
            for (uint i = 0; i < dataCount; i++)
            {
                uint p = baseAddr + i * d.BlockSize;
                foreach (SubWalk sw in d.SubWalks)
                {
                    uint pfield = p + sw.EmbeddedPointerOffset;
                    switch (sw.Kind)
                    {
                        case SubKind.CString:
                            // StatusParamForm: Address.AddCString(list, p + 12). AddCString does its
                            // own pointer-safety + getString and adds (len + 1, CSTRING) verbatim.
                            Address.AddCString(list, pfield);
                            break;

                        case SubKind.BinString:
                        {
                            // MapTerrainNameForm/OtherTextForm: read the embedded pointer, decode the
                            // string, and add a BIN block of length == strlen (NO trailing-NUL +1).
                            uint nameAddr = rom.p32(pfield);
                            if (!U.isSafetyOffset(nameAddr))
                            {
                                continue;
                            }
                            int len;
                            string name = rom.getString(nameAddr, out len);
                            Address.AddAddress(list, nameAddr, (uint)len, pfield, name,
                                Address.DataTypeEnum.BIN);
                            break;
                        }

                        case SubKind.BinFixed:
                        {
                            // ClassForm MoveCost: if the embedded pointer is a safe offset, add a
                            // fixed-length BIN block. WF uses Address.AddPointer (pointer-slot form),
                            // which resolves p32 itself and length-checks — mirror it exactly.
                            uint target = rom.p32(pfield);
                            if (!U.isSafetyOffset(target))
                            {
                                continue;
                            }
                            Address.AddPointer(list, pfield, sw.FixedLength,
                                sw.Name != null ? sw.Name(rom, i) : d.Name,
                                Address.DataTypeEnum.BIN);
                            break;
                        }

                        default:
                            // A new/bad SubKind is a programming error — fail loudly rather than
                            // silently skip an embedded block (which would dangle on rebuild).
                            throw new ArgumentOutOfRangeException(nameof(sw),
                                "Unhandled SubKind: " + sw.Kind);
                    }
                }
            }
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
                case DataCountRule.U32LessThan:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        return rom.u32(addr + d.RuleOffset) < d.RuleStopValue;
                    };
                case DataCountRule.SoundBossBGMRule:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // SoundBossBGMForm.Init: stop at the 0xFFFF terminator, and (after the
                        // first 10 entries) stop once a run of 10 empty blocks is hit.
                        if (rom.u16(addr) == 0xFFFF) return false;
                        if (i > 10 && rom.IsEmpty(addr, d.BlockSize * 10)) return false;
                        return true;
                    };
                case DataCountRule.PointerAt:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // StatusParamForm.Init: U.isPointer(u32(addr+12)) — a NULL slot terminates
                        // (isPointer, NOT isPointerOrNULL).
                        return U.isPointer(rom.u32(addr + d.RuleOffset));
                    };
                case DataCountRule.PointerOrNullAt:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // MapTerrainNameForm.Init: U.isPointerOrNULL(u32(addr+0)) — NULL is accepted.
                        return U.isPointerOrNULL(rom.u32(addr + d.RuleOffset));
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

            // ClassForm.MakeAllDataLength (slice 2c) — main IFR (block class_datasize,
            // U8NotZeroIndex0Always @+4, max 0x100, pointerIndexes {52,56,60,64,68,72,76}) PLUS a
            // per-entry MoveCost sub-walk: six 66-byte BIN blocks behind the embedded pointers at
            // offsets 56/60/64/68/72/76. Offset 52 is IN pointerIndexes (the battle-animation
            // pointer FIELD is relocated) but is NOT a MoveCost sub-walk — its TARGET is tracked by
            // the battle-anime form, so adding a 52 sub-walk here would double-track it. After the
            // per-entry loop, three 全クラス共通 terrain pointers (66-byte BIN each) are emitted once.
            l.Add(new StructDescriptor
            {
                Name = "Class",
                PointerField = r => r.RomInfo.class_pointer,
                FixedCountField = null,
                BlockSize = rom.RomInfo.class_datasize,
                Rule = DataCountRule.U8NotZeroIndex0Always,
                RuleOffset = 4,
                MaxCount = 0x100,
                PointerIndexes = new uint[] { 52, 56, 60, 64, 68, 72, 76 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 56, Kind = SubKind.BinFixed, FixedLength = 66, Name = (r, i) => "MoveCost Clear" },
                    new SubWalk { EmbeddedPointerOffset = 60, Kind = SubKind.BinFixed, FixedLength = 66, Name = (r, i) => "MoveCost Rain" },
                    new SubWalk { EmbeddedPointerOffset = 64, Kind = SubKind.BinFixed, FixedLength = 66, Name = (r, i) => "MoveCost Snow" },
                    new SubWalk { EmbeddedPointerOffset = 68, Kind = SubKind.BinFixed, FixedLength = 66, Name = (r, i) => "MoveCost avoid" },
                    new SubWalk { EmbeddedPointerOffset = 72, Kind = SubKind.BinFixed, FixedLength = 66, Name = (r, i) => "MoveCost def" },
                    new SubWalk { EmbeddedPointerOffset = 76, Kind = SubKind.BinFixed, FixedLength = 66, Name = (r, i) => "MoveCost ref" },
                },
                ExtraFixedPointers = new[]
                {
                    new ExtraFixedPointer { PointerField = r => r.RomInfo.terrain_recovery_pointer, FixedLength = 66, Name = "MoveCost ref" },
                    new ExtraFixedPointer { PointerField = r => r.RomInfo.terrain_bad_status_recovery_pointer, FixedLength = 66, Name = "MoveCost recovery bad status" },
                    new ExtraFixedPointer { PointerField = r => r.RomInfo.terrain_show_infomation_pointer, FixedLength = 66, Name = "MoveCost show infomation" },
                },
            });

            // StatusParamForm.MakeAllDataLength (slice 2c) — FOUR pointers (param1/2/3w/3m), block
            // 16, IsDataExists = U.isPointer(u32(addr+12)) (NULL terminates -> DataCountRule.PointerAt
            // @ off 12), main pointerIndexes {0,4,12}. Per entry: a CString behind the embedded
            // pointer at offset 12 (Address.AddCString(p+12)). The WF Info per pointer is
            // "StatusParam0".."StatusParam3"; faithfully one descriptor per pointer with that label.
            for (uint sp = 0; sp < 4; sp++)
            {
                uint spIndex = sp; // capture
                l.Add(new StructDescriptor
                {
                    Name = "StatusParam" + spIndex,
                    PointerField = r =>
                    {
                        switch (spIndex)
                        {
                            case 0: return r.RomInfo.status_param1_pointer;
                            case 1: return r.RomInfo.status_param2_pointer;
                            case 2: return r.RomInfo.status_param3w_pointer;
                            default: return r.RomInfo.status_param3m_pointer;
                        }
                    },
                    BlockSize = 16,
                    Rule = DataCountRule.PointerAt,
                    RuleOffset = 12,
                    MaxCount = 0x10000,
                    PointerIndexes = new uint[] { 0, 4, 12 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 12, Kind = SubKind.CString },
                    },
                });
            }

            // SoundBossBGMForm.MakeAllDataLength — called UNCONDITIONALLY in WinForms
            // (before the version branches). Info label "BossBGM". The Init IsDataExists is a
            // 0xFFFF terminator PLUS a trailing-empty guard (see DataCountRule.SoundBossBGMRule).
            l.Add(new StructDescriptor
            {
                Name = "BossBGM",
                PointerField = r => r.RomInfo.sound_boss_bgm_pointer,
                BlockSize = 8,
                Rule = DataCountRule.SoundBossBGMRule,
                PointerIndexes = new uint[] { },
            });

            // ---- version==8 (FE8) section ----
            // These forms are called ONLY inside the WinForms `if (version == 8)` branch, in this
            // order. Their data pointers are 0 on FE6/FE7 (EmitOne skips a 0/unsafe pointer), but
            // we gate them to match the WF call structure exactly and avoid scanning junk on a
            // non-FE8 ROM.
            //
            // MapTileAnimation1/2Form, MonsterWMapProbabilityForm, CCBranchForm, and the
            // MapTerrain*LookupTable forms are deliberately NOT here:
            //   * MapTileAnimation1/2 expand a per-entry embedded IMG/BIN sub-block (rule 3).
            //   * MonsterWMapProbabilityForm also runs an EventScriptForm.ScanScript event-scan
            //     in the same MakeAllDataLength (event-scan expansion, not a pure table walk).
            //   * CCBranchForm's entry count is ClassForm.DataCount() (a dynamic class-table walk),
            //     not a RomInfo field.
            //   * MapTerrain{Floor,BG}LookupTableForm enumerate a PatchUtil-dependent GetPointers()
            //     set (extends-battle-BG patch detection), not a fixed RomInfo table.
            // They stay in GetNotYetPortedForms for a later slice.
            if (rom.RomInfo.version == 8)
            {
                // StatusUnitsMenuForm.MakeAllDataLength — Info "UnitsMenu", block 16,
                // IsDataExists = u32(addr+0) < 0xFF.
                l.Add(new StructDescriptor
                {
                    Name = "UnitsMenu",
                    PointerField = r => r.RomInfo.status_units_menu_pointer,
                    BlockSize = 16,
                    Rule = DataCountRule.U32LessThan,
                    RuleOffset = 0,
                    RuleStopValue = 0xFF,
                    PointerIndexes = new uint[] { },
                });

                // LinkArenaDenyUnitForm.MakeAllDataLength — Info "LinkAreaDenyUnitForm" (WF typo
                // preserved verbatim), block 2, IsDataExists = u8(addr) != 0x00.
                l.Add(new StructDescriptor
                {
                    Name = "LinkAreaDenyUnitForm",
                    PointerField = r => r.RomInfo.link_arena_deny_unit_pointer,
                    BlockSize = 2,
                    Rule = DataCountRule.U8NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0x00,
                    PointerIndexes = new uint[] { },
                });

                // MonsterItemForm.MakeAllDataLength — THREE tables (Init / N1_Init / N2_Init),
                // each its own AddAddress with a distinct Info string, in this order. All
                // IsDataExists = u8(addr) != 0xFF.
                l.Add(new StructDescriptor
                {
                    Name = "MonsterItemForm",
                    PointerField = r => r.RomInfo.monster_item_item_pointer,
                    BlockSize = 5,
                    Rule = DataCountRule.U8NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0xFF,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "MonsterItemFormProbability",
                    PointerField = r => r.RomInfo.monster_item_probability_pointer,
                    BlockSize = 5,
                    Rule = DataCountRule.U8NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0xFF,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "MonsterItemFormTable",
                    PointerField = r => r.RomInfo.monster_item_table_pointer,
                    BlockSize = 32,
                    Rule = DataCountRule.U8NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0xFF,
                    PointerIndexes = new uint[] { },
                });

                // MonsterProbabilityForm.MakeAllDataLength — Info "MonsterProbabilityForm",
                // block 12, IsDataExists = u8(addr) != 0xFF.
                l.Add(new StructDescriptor
                {
                    Name = "MonsterProbabilityForm",
                    PointerField = r => r.RomInfo.monster_probability_pointer,
                    BlockSize = 12,
                    Rule = DataCountRule.U8NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0xFF,
                    PointerIndexes = new uint[] { },
                });
            }

            // ---- trailing is_multibyte branch: MapTerrainName(Eng) ----
            // WinForms: is_multibyte -> MapTerrainNameForm (per-entry embedded string-BIN sub-walk,
            //                           slice 2c);
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
            else
            {
                // MapTerrainNameForm.MakeAllDataLength (slice 2c) — main IFR (block 4, base
                // map_terrain_name_pointer, IsDataExists = U.isPointerOrNULL(u32(addr+0)) ->
                // DataCountRule.PointerOrNullAt @ off 0, pointerIndexes {0}). Per entry: a string-BIN
                // block (length == strlen, NO +1) behind the embedded pointer at offset 0. Info
                // label "Terrain" (the per-entry sub-block names come from the decoded string).
                l.Add(new StructDescriptor
                {
                    Name = "Terrain",
                    PointerField = r => r.RomInfo.map_terrain_name_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.PointerOrNullAt,
                    RuleOffset = 0,
                    PointerIndexes = new uint[] { 0 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.BinString },
                    },
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
                // (MapTerrainNameForm ported in slice 2c — per-entry string-BIN sub-walk; OtherTextForm
                //  STAYS: it iterates a config file `other_text_*` (U.ConfigDataFilename), not a
                //  RomInfo table, so it has a headless config-file dependency, not ROM-derived data.)
                "TextForm", "TextCharCodeForm", "TextDicForm", "OtherTextForm",
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
                // (ClassForm + StatusParamForm ported in slice 2c — per-entry MoveCost / CString
                //  sub-walks. ItemForm STAYS: its StatBooster sub-block SIZE depends on un-ported
                //  PatchUtil patch detection (SearchGrowsMod / ItemUsingExtendsPatch -> 12/16/20)
                //  and the ItemEffectiveness rework variant on SearchClassType — a wrong size would
                //  relocate the wrong bytes, so it must wait until those detectors reach Core.)
                "ItemForm",
                // The other slice-2c embedded forms also stay — their sub-block LENGTH needs a
                // subsystem not yet in Core (a wrong length relocates the wrong bytes = corruption):
                //   StatusRMenuForm     — recursive 28-byte MIX tree (visited-set) + ASM AddFunction (disasm).
                //   MenuDefinitionForm  — recursive MenuCommandForm sub-table + 6 ASM ptrs (disasm).
                //   ItemWeaponEffectForm— PROCS sub-block length = ProcsScriptForm.CalcLengthAndCheck (disasm).
                "ItemShopForm", "StatusRMenuForm", "MenuDefinitionForm",
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
                // (StatusUnitsMenuForm + LinkArenaDenyUnitForm ported in slice 2b.)
                "StatusOptionForm", "StatusOptionOrderForm",
                "MantAnimationForm", "MapTileAnimation1Form",
                "MapTileAnimation2Form", "MapTerrainFloorLookupTableForm",
                "MapTerrainBGLookupTableForm",
                // units / classes per-version with extra reads
                "UnitForm", "UnitFE7Form", "UnitFE6Form", "UnitCustomBattleAnimeForm",
                "ExtraUnitForm", "ExtraUnitFE8UForm", "SummonUnitForm", "SummonsDemonKingForm",
                "EventUnitForm(RecycleReserveUnits)", "EventForceSortieForm",
                // monster / world map / ED / support (FE8/FE7/FE6 variants)
                // (MonsterItemForm + MonsterProbabilityForm ported in slice 2b.
                //  MonsterWMapProbabilityForm stays — it runs an EventScriptForm.ScanScript
                //  event-scan in the same MakeAllDataLength, not a pure table walk.)
                "MonsterWMapProbabilityForm",
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
