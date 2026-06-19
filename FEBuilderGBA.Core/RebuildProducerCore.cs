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
            /// <summary>General "terminator value(s) + trailing-empty guard" rule. Reads a
            /// <see cref="StructDescriptor.RuleWidth"/>-byte value at <c>addr + RuleOffset</c>; stops
            /// when it equals <see cref="StructDescriptor.RuleStopValue"/> (or, when set, also
            /// <see cref="StructDescriptor.RuleStopValue2"/>); and, when
            /// <see cref="StructDescriptor.HasEmptyGuard"/> is set, ALSO stops once
            /// <c>i &gt; 10 &amp;&amp; rom.IsEmpty(addr, BlockSize*10)</c>. Reproduces the common
            /// <c>SupportTalk*/EventBattleTalk*/EventHaikuFE6/SoundRoom*</c> "read until 0xFFFF /
            /// 0x0000 / 0xFFFFFFFF, but ignore stray 0x00 holes" idiom VERBATIM. The width selects the
            /// read: 1 = <c>u8</c>, 2 = <c>u16</c>, 4 = <c>u32</c>. This is the generalization of
            /// <see cref="SoundBossBGMRule"/> (which is the width=2, stop=0xFFFF, guard=on special
            /// case at offset 0; kept distinct for back-compat / its existing tests).</summary>
            TerminatorWithEmptyGuard,
            /// <summary>Fixed count read from a <c>RomInfo</c> <i>count address</i> (NOT a count field):
            /// <c>count = rom.u8(CountAddressField(rom))</c>, then <c>i &lt; count</c>. Matches
            /// <c>StatusOptionOrderForm.Init</c>
            /// (<c>i &lt; u8(status_game_option_order_count_address)</c>).</summary>
            FixedCountU8Address,
            /// <summary>SummonsDemonKing rule: <c>max = u8(CountAddressField(rom)); if (max &gt;= 100)
            /// stop; return i &lt;= max</c>. Reproduces <c>SummonsDemonKingForm.Init</c> verbatim
            /// (note the <c>&lt;=</c>, i.e. count = max+1, and the &gt;=100 sanity cap).</summary>
            SummonsDemonKingRule,
            /// <summary>Continue while <c>u32(addr+Offset)</c> is in the inclusive range
            /// [<see cref="StructDescriptor.RuleRangeLo"/>, <see cref="StructDescriptor.RuleRangeHi"/>].
            /// Matches <c>EventFinalSerifFE7Form.Init</c>
            /// (<c>u32(addr+0) &lt;= 0xff &amp;&amp; &gt;= 0x1</c>).</summary>
            U32InRangeAt,
            /// <summary>Triple pointer-or-NULL AND: continue while
            /// <c>isPointerOrNULL(u32(addr+12)) &amp;&amp; isPointerOrNULL(u32(addr+16)) &amp;&amp;
            /// isPointerOrNULL(u32(addr+20))</c>. Matches <c>WorldMapPointForm.Init</c> (the offsets
            /// are fixed at 12/16/20, mirroring the WF lambda).</summary>
            TripleU32PointerOrNullAt121620,
            /// <summary>WorldMapBGM rule: stop when the two u16 song ids at <c>addr+0</c>/<c>addr+2</c>
            /// are <c>(1,0)</c> or <c>(0,0)</c>; otherwise continue (no terminator record exists).
            /// Reproduces <c>WorldMapBGMForm.Init</c> verbatim.</summary>
            WorldMapBGMRule,
            /// <summary>TextDic main rule: stop when <c>u16(addr+2) &lt;= 0 || u16(addr+4) &lt;= 0</c>.
            /// Reproduces <c>TextDicForm.Init</c> (the <c>dic_main</c> table) verbatim.</summary>
            DicMainRule,
            /// <summary>Continue while <c>U.isPointer(u32(addr+0)) &amp;&amp; U.isPointer(u32(addr+4))</c>
            /// (BOTH non-NULL ROM pointers). Matches <c>ImageBattleBGForm.Init</c> (its IsDataExists is
            /// "0 と 4 がポインタであればデータがある"). The offsets are fixed at 0 and 4.</summary>
            TwoU32PointerAt04,
            /// <summary><c>ImageUnitWaitIconFrom.Init</c> rule: entry 0 always exists; otherwise the
            /// <c>+4</c> field decides — a <c>U.isPointer(u32(addr+4))</c> continues; a <c>u32(addr+4)==0</c>
            /// continues ONLY while <c>u32(addr+0) != 0</c> (both zero = terminator); any other (non-zero,
            /// non-pointer) <c>+4</c> value terminates. Reproduced VERBATIM.</summary>
            WaitIconRule,
            /// <summary><c>ImageUnitPaletteForm.Init</c> rule: a <c>U.isPointer(u32(addr+12))</c> continues;
            /// a <c>u32(addr+12)==0</c> terminates ONLY when <c>u32(addr+0)==0</c> too (name also NULL);
            /// any other value continues. Reproduced VERBATIM (the +12 palette-pointer field, with the
            /// +0 name-NULL tie-break, distinct from <see cref="PointerAt"/> which terminates on any
            /// non-pointer).</summary>
            UnitPaletteRule,
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
            /// <summary>An embedded ASM-routine pointer. Emits via <see cref="Address.AddFunction"/>
            /// verbatim (reads <c>u32(p + EmbeddedPointerOffset)</c>, <c>ProgramAddrToPlain</c>s it, and
            /// adds a length-0 <see cref="Address.DataTypeEnum.ASM"/> block whose extent the
            /// disassembler determines at rebuild time). Matches <c>StatusOptionForm</c>
            /// (<c>Address.AddFunction(list, p + 40, name)</c>). The label is non-load-bearing for
            /// relocation, so a static name is used (the WinForms <c>isPointerOnly</c> flag only swaps
            /// the label between <c>""</c> and a Huffman-decoded string — neither affects addr/length).</summary>
            AsmFunction,
            /// <summary>An embedded LZ77-compressed block (image / TSA / palette) behind a pointer.
            /// Emits via <see cref="Address.AddLZ77Pointer"/> VERBATIM (reads <c>u32(p +
            /// EmbeddedPointerOffset)</c>, <c>isSafetyPointer</c>-checks, then adds a block whose length
            /// is <c>LZ77.getCompressedSize(rom.Data, addr)</c> — the producer always scans with
            /// <c>isPointerOnly = false</c>, so a real length is computed, NOT 0). The block's
            /// <see cref="Address.DataTypeEnum"/> is taken from <see cref="SubWalk.DataType"/>
            /// (LZ77IMG / LZ77TSA / LZ77PAL). Matches the per-entry <c>AddLZ77Pointer</c> in
            /// <c>ImageBattleBGForm</c>/<c>ImageBattleTerrainForm</c>/<c>ImageUnitWaitIconFrom</c>/
            /// <c>ImageUnitPaletteForm</c>/<c>ImageChapterTitleForm</c> etc. EOF-safe: a near-EOF /
            /// malformed stream yields <c>getCompressedSize == 0</c> (length 0), never throws.</summary>
            Lz77Pointer,
            /// <summary>An embedded FIXED-size block (palette / uncompressed image) behind a pointer.
            /// Emits via <see cref="Address.AddPointer"/> VERBATIM (<c>u32(p + EmbeddedPointerOffset)</c>
            /// -&gt; addr, <c>isSafetyPointer</c>-check, add a <see cref="SubWalk.FixedLength"/>-byte block
            /// of <see cref="SubWalk.DataType"/>). Distinct from <see cref="BinFixed"/> only in that the
            /// data type is configurable (PAL / IMG / LZ77PAL), not hard-wired to BIN. Matches the
            /// per-entry <c>AddPointer(p + N, 0x20*K, name, PAL)</c> palette/image columns.</summary>
            FixedPointer,
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
            /// <summary>For <see cref="SubKind.BinFixed"/> / <see cref="SubKind.FixedPointer"/>: the
            /// fixed block length (e.g. MoveCost = 66, a 16-color palette = 0x20). Ignored for the
            /// string / LZ77 kinds (LZ77 length comes from <c>getCompressedSize</c>).</summary>
            public uint FixedLength;
            /// <summary>For <see cref="SubKind.Lz77Pointer"/> / <see cref="SubKind.FixedPointer"/>: the
            /// <see cref="Address.DataTypeEnum"/> the emitted block is tagged with (LZ77IMG / LZ77TSA /
            /// LZ77PAL / PAL / IMG). Reproduces the WinForms per-entry call's last argument VERBATIM.
            /// Ignored for the other kinds (which have a fixed type: CSTRING / BIN / ASM).</summary>
            public Address.DataTypeEnum DataType;
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
            /// <summary>For <see cref="DataCountRule.TerminatorWithEmptyGuard"/>: the read width in
            /// bytes (1 = u8, 2 = u16, 4 = u32). Default 2 (the most common, u16).</summary>
            public uint RuleWidth = 2;
            /// <summary>For <see cref="DataCountRule.TerminatorWithEmptyGuard"/>: an OPTIONAL SECOND
            /// terminator value (e.g. EventBattleTalkFE6: stop on <c>0x0000</c> OR <c>0xFFFF</c>).
            /// <c>null</c> = only <see cref="RuleStopValue"/> terminates.</summary>
            public uint? RuleStopValue2;
            /// <summary>For <see cref="DataCountRule.TerminatorWithEmptyGuard"/>: whether to ALSO apply
            /// the <c>i &gt; 10 &amp;&amp; IsEmpty(addr, BlockSize*10)</c> trailing-empty guard. Some
            /// terminator tables have it (SupportTalk*, SoundRoomFE6), some do not (SoundRoomCG).</summary>
            public bool HasEmptyGuard;
            /// <summary>For <see cref="DataCountRule.FixedCountU8Address"/> /
            /// <see cref="DataCountRule.SummonsDemonKingRule"/>: resolves the <c>RomInfo</c> COUNT
            /// ADDRESS (a ROM address holding a u8 count, e.g.
            /// <c>status_game_option_order_count_address</c>), NOT a count value.</summary>
            public Func<ROM, uint> CountAddressField;
            /// <summary>For <see cref="DataCountRule.U32InRangeAt"/>: inclusive lower bound.</summary>
            public uint RuleRangeLo;
            /// <summary>For <see cref="DataCountRule.U32InRangeAt"/>: inclusive upper bound.</summary>
            public uint RuleRangeHi;
            /// <summary>Optional per-entry embedded-data sub-walks (null = none, back-compat with
            /// the slice-2a/2b flat descriptors). Applied to every table entry AFTER the main IFR
            /// <see cref="Address"/> is emitted.</summary>
            public List<SubWalk> SubWalks;
            /// <summary>First entry index the <see cref="SubWalks"/> apply to (default 0 = every
            /// entry). <c>ClassFE6Form.MakeAllDataLength</c> SKIPS class 0 (its MoveCost loop starts
            /// at <c>cid=1, addr=BaseAddress+BlockSize</c>), so its descriptor sets this to 1. Does
            /// NOT affect the main IFR <see cref="Address"/> length (still block×(count+1)).</summary>
            public uint SubWalkStartIndex = 0;
            /// <summary>Optional standalone fixed-size BIN pointers emitted ONCE per descriptor
            /// (not per entry). Each is a <c>RomInfo</c> pointer whose target is a fixed-length
            /// BIN block — reproduces <c>ClassForm</c>'s three全クラス共通 terrain pointers
            /// (terrain_recovery / terrain_bad_status_recovery / terrain_show_infomation), each a
            /// 66-byte BIN via <c>Address.AddPointer(..., 66, name, BIN)</c>.</summary>
            public ExtraFixedPointer[] ExtraFixedPointers;
            /// <summary>Whether to emit the main IFR <see cref="Address"/> (block × (DataCount+1)).
            /// Default <c>true</c> (every flat / sub-walk descriptor so far). A few WinForms image
            /// forms (e.g. <c>ImageGenericEnemyPortraitForm</c>) run the per-entry loop but emit NO
            /// <c>AddressWinForms.AddAddress(list, IFR, ...)</c> — only the standalone header pointer +
            /// the per-entry columns. Setting this <c>false</c> suppresses the main IFR Address while
            /// still walking <see cref="SubWalks"/> over the same <c>getBlockDataCount</c>.</summary>
            public bool EmitMainIfr = true;
        }

        /// <summary>A standalone fixed-size pointer emitted once per descriptor (see
        /// <see cref="StructDescriptor.ExtraFixedPointers"/>). Default type is BIN (the ClassForm
        /// terrain pointers); <see cref="DataType"/> overrides it (e.g. the GenericEnemyPortrait
        /// header is a POINTER block).</summary>
        public sealed class ExtraFixedPointer
        {
            /// <summary>Resolves the <c>RomInfo</c> pointer field (e.g.
            /// <c>r =&gt; r.RomInfo.terrain_recovery_pointer</c>).</summary>
            public Func<ROM, uint> PointerField;
            /// <summary>Fixed block length (e.g. 66 for MoveCost BIN, 8*2*4 for the GenericEnemyPortrait
            /// POINTER header).</summary>
            public uint FixedLength;
            /// <summary>The <see cref="Address.Info"/> label.</summary>
            public string Name;
            /// <summary>The emitted block's <see cref="Address.DataTypeEnum"/>. Defaults to
            /// <see cref="Address.DataTypeEnum.BIN"/> (the ClassForm terrain pointers); set to e.g.
            /// <see cref="Address.DataTypeEnum.POINTER"/> for the GenericEnemyPortrait header.</summary>
            public Address.DataTypeEnum DataType = Address.DataTypeEnum.BIN;
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

            // ---- slice 2d: forms that are NOT a flat descriptor walk ----
            // SoundFootStepsForm, StatusRMenuForm, MenuDefinitionForm are called UNCONDITIONALLY in
            // the WinForms producer (version-agnostic section), but none fit the StructDescriptor
            // pointer-slot model: SoundFootSteps is Switch2-gated with a count read from a RomInfo
            // *address*; StatusRMenu is a recursive 28-byte MIX tree with a visited-set; MenuDefinition
            // recurses into a MenuCommand sub-table. They get dedicated walkers, each with its own
            // cancel-check (mirroring the WF DoEvents checkpoints) so a cancel returns the partial list.
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("SoundFootStepsPointer");
            EmitSoundFootSteps(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MenuDefinition");
            EmitMenuDefinition(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("RMENU");
            EmitStatusRMenuTree(rom, list);

            // ---- slice 2e: flat LZ77-image + palette forms that are NOT a descriptor walk ----
            // ImageBattleScreenForm is version-agnostic (called in the WF unconditional section);
            // WorldMapImageFE6Form / WorldMapImageFE7Form are version-gated (the FE8 WorldMapImageForm
            // is DEFERRED — it uses AddHeaderTSAPointer / AddROMTCSPointer, both ImageUtil-backed and
            // not yet in Core). Each gets its own cancel-check, mirroring the WF DoEvents checkpoints.
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageBattleScreen");
            EmitImageBattleScreen(rom, list);

            if (rom.RomInfo.version == 7)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("WorldMapImageFE7");
                EmitWorldMapImageFE7(rom, list);
            }
            else if (rom.RomInfo.version == 6)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("WorldMapImageFE6");
                EmitWorldMapImageFE6(rom, list);
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
                        ep.DataType);
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

            // Most descriptors emit a main IFR Address (AddressWinForms.AddAddress(list, IFR, ...)).
            // A few image forms (ImageGenericEnemyPortraitForm) run the per-entry loop but emit NO main
            // IFR Address — EmitMainIfr==false suppresses it while still walking the SubWalks over the
            // SAME getBlockDataCount (so the dataCount that drives the sub-walk loop is identical).
            if (d.EmitMainIfr)
            {
                list.Add(new Address(baseAddr, length, pointer, d.Name, d.DataType, d.BlockSize, d.PointerIndexes));
            }

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
            // The string-decoding sub-walks (CString/BinString) read ROM.getString, which needs a
            // CoreState.SystemTextEncoder. In the real app + CLI InitFull it is always set, but the
            // producer must NOT NullReferenceException if a caller scans without one. Skip those
            // sub-walks gracefully (the BinFixed/fixed-length kinds do not decode strings, so they
            // still run). NOTE: skipping leaves those embedded string blocks un-tracked — the
            // ProducerResult is already IsComplete==false in any partial scan, so the wiring slice
            // must not feed such a list to a real defragment.
            bool hasEncoder = CoreState.SystemTextEncoder != null;

            // Most forms walk every entry from 0. ClassFE6Form skips class 0 (its MoveCost loop
            // starts at cid=1, addr=BaseAddress+BlockSize) — SubWalkStartIndex models that verbatim.
            for (uint i = d.SubWalkStartIndex; i < dataCount; i++)
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
                            // Needs an encoder; skip (don't NRE) if none is loaded.
                            if (!hasEncoder)
                            {
                                continue;
                            }
                            Address.AddCString(list, pfield);
                            break;

                        case SubKind.BinString:
                        {
                            // MapTerrainNameForm/OtherTextForm: read the embedded pointer, decode the
                            // string, and add a BIN block of length == strlen (NO trailing-NUL +1).
                            // Needs an encoder; skip (don't NRE) if none is loaded.
                            if (!hasEncoder)
                            {
                                continue;
                            }
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

                        case SubKind.AsmFunction:
                            // StatusOptionForm: Address.AddFunction(list, p + 40, name). AddFunction
                            // reads u32(pfield), isSafetyPointer-checks, ProgramAddrToPlain-resolves,
                            // and adds a length-0 ASM block (extent determined by the disassembler at
                            // rebuild). No encoder needed — the label is static (see SubKind.AsmFunction).
                            Address.AddFunction(list, pfield,
                                sw.Name != null ? sw.Name(rom, i) : d.Name);
                            break;

                        case SubKind.Lz77Pointer:
                            // ImageBattleBGForm/ImageBattleTerrainForm/ImageUnitWaitIconFrom/
                            // ImageUnitPaletteForm/ImageChapterTitle(FE7)Form per-entry:
                            // Address.AddLZ77Pointer(list, p + N, name, isPointerOnly, type).
                            // AddLZ77Pointer reads u32(pfield), isSafetyPointer-checks, then
                            // AddLZ77Address computes length = LZ77.getCompressedSize(rom.Data, addr).
                            // The producer scans real lengths, so isPointerOnly: false (WF passes the
                            // caller's isPointerOnly; a defragment scan is always !isPointerOnly).
                            // No encoder needed (no string decode). EOF-safe (getCompressedSize == 0
                            // on a malformed/near-EOF stream — never throws).
                            Address.AddLZ77Pointer(list, pfield,
                                sw.Name != null ? sw.Name(rom, i) : d.Name,
                                false, sw.DataType);
                            break;

                        case SubKind.FixedPointer:
                            // The fixed-size palette/image columns: Address.AddPointer(list, p + N,
                            // <constant>, name, <PAL/IMG/LZ77PAL>). AddPointer reads u32(pfield),
                            // isSafetyPointer-checks, and adds a FixedLength block of DataType. Same
                            // shape as BinFixed but with a configurable (non-BIN) data type.
                            Address.AddPointer(list, pfield, sw.FixedLength,
                                sw.Name != null ? sw.Name(rom, i) : d.Name,
                                sw.DataType);
                            break;

                        default:
                            // A new/bad SubKind is a programming error — fail loudly rather than
                            // silently skip an embedded block (which would dangle on rebuild).
                            throw new ArgumentOutOfRangeException(nameof(sw) + "." + nameof(sw.Kind),
                                sw.Kind, "Unhandled SubKind.");
                    }
                }
            }
        }

        /// <summary>
        /// The class-table entry count = <c>ClassForm.DataCount()</c> (WinForms). Several forms
        /// (<c>CCBranchForm</c>, <c>OPClassAlphaNameForm</c>) size their table by it. Computed
        /// VERBATIM from <c>ClassForm.Init</c>: walk <c>class_pointer</c>'s table with block
        /// <c>class_datasize</c> and the <see cref="DataCountRule.U8NotZeroIndex0Always"/> rule
        /// (<c>i==0 -&gt; true; i&gt;0xff -&gt; false; else u8(addr+4)!=0</c>). Returns 0 on an
        /// unsafe/zero pointer (so a missing table yields an empty count, not a throw).
        /// </summary>
        public static uint ClassDataCount(ROM rom)
        {
            uint pointer = U.toOffset(rom.RomInfo.class_pointer);
            if (!U.isSafetyOffset(pointer, rom)) return 0;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint block = rom.RomInfo.class_datasize;
            if (block == 0) return 0;
            return rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                if (i == 0) return true;
                if (i > 0xff) return false;
                return rom.u8(addr + 4) != 0;
            });
        }

        /// <summary>
        /// <c>SoundFootStepsForm.MakeAllDataLength</c> (slice 2d). This is NOT a flat RomInfo
        /// pointer-slot table: the WinForms <c>ReInit</c> derives the base + count from a Switch2
        /// jump-table, gated by a patch detector. Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>base = <c>p32(sound_foot_steps_pointer)</c>; block = 4;</item>
        ///   <item>count = <c>u8(sound_foot_steps_switch2_address + 2)</c>; entries = count + 1;</item>
        ///   <item>GATE: only if <c>IsSwitch2Enable(sound_foot_steps_switch2_address)</c> AND the base
        ///   is a safe offset (else WF's <c>ReInit</c> returns <c>NOT_FOUND</c> and emits nothing).</item>
        /// </list>
        /// Then: the main IFR <see cref="Address"/> (block × (entries + 1), type
        /// <see cref="Address.DataTypeEnum.InputFormRef_ASM"/>, pointerIndexes {0}) + one
        /// <see cref="Address.AddFunction"/> per entry at the entry block address itself (offset 0 —
        /// each 4-byte slot is an ASM-routine pointer; the WF <c>AddFunctions(list, MakeList(), 0,
        /// name)</c>). The per-entry label is the class name (Huffman) in WF; a static
        /// "SoundFootStepsPointer" label is used here (non-load-bearing for relocation; headless-safe).
        /// <para>The Switch2-enable byte pattern + the count read are pure ROM reads
        /// (<see cref="ItemUsagePointerCore.IsSwitch2Enable"/>), so this is faithfully headless.</para>
        /// </summary>
        public static void EmitSoundFootSteps(ROM rom, List<Address> list)
        {
            EmitSoundFootStepsAt(rom, list,
                rom.RomInfo.sound_foot_steps_switch2_address,
                rom.RomInfo.sound_foot_steps_pointer);
        }

        /// <summary>SoundFootSteps walk from explicit switch2 + base-pointer addresses (test seam —
        /// lets a synthetic ROM supply the addresses without populating RomInfo). See
        /// <see cref="EmitSoundFootSteps"/> for the verbatim WF reproduction.</summary>
        public static void EmitSoundFootStepsAt(ROM rom, List<Address> list, uint switch2Addr, uint rawPointer)
        {
            // WF ReInit: enable-gate FIRST, then base-safety. Either failing => NOT_FOUND => no emit.
            if (!ItemUsagePointerCore.IsSwitch2Enable(rom, switch2Addr))
            {
                return;
            }
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return; // WF ReInit: !isSafetyOffset(addr) -> NOT_FOUND.
            }

            const uint block = 4;
            uint count = rom.u8(switch2Addr + 2);
            uint dataCount = count + 1; // WF: ifr.ReInit(addr, count + 1) -> DataCount = count + 1.

            // Main IFR Address: AddressWinForms.AddAddress(ifr, name, {0}, InputFormRef_ASM) ->
            // length = BlockSize * (DataCount + 1). CRITICAL: the IFR's BasePointer is the Init's 3rd
            // arg = 0 (SoundFootStepsForm.Init passes basepointer 0; ReInit(addr,count) sets only
            // BaseAddress, NOT BasePointer). So in AddAddress, `pointer = BasePointer = 0` is not a
            // safe offset -> it becomes U.NOT_FOUND. The base is reached via the Switch2 LDR, NOT a
            // plain RomInfo pointer slot, so the table's pointer FIELD is intentionally untracked
            // (NOT_FOUND); only the per-entry +0 ASM columns (pointerIndexes {0}) are relocated.
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, U.NOT_FOUND, "SoundFootStepsPointer",
                Address.DataTypeEnum.InputFormRef_ASM, block, new uint[] { 0 }));

            // AddFunctions(list, ifr.MakeList(), 0, name): one ASM function per entry at the entry
            // block address itself (offset 0). ifr.MakeList() yields one AddrResult per entry at
            // baseAddr + i*block. AddFunction reads u32(entryAddr), isSafetyPointer-checks, and
            // ProgramAddrToPlain-resolves — identical to the BinFixed/AsmFunction sub-walks.
            for (uint i = 0; i < dataCount; i++)
            {
                uint entryAddr = baseAddr + i * block;
                // Faithful to WF InputFormRef.MakeList(): it stops yielding the moment a block would
                // run past EOF (`addr + BlockSize > Data.Length` -> break), so AddFunctions never
                // reads past EOF. AddFunction reads u32(entryAddr) whose check_safety THROWS past EOF,
                // so without this same bound a corrupted/too-large count near EOF would throw instead
                // of truncating gracefully. block is the BlockSize and equals the u32 read width.
                if (entryAddr + block > (uint)rom.Data.Length)
                {
                    break;
                }
                Address.AddFunction(list, entryAddr, "SoundFootStepsPointer");
            }
        }

        /// <summary>
        /// <c>StatusRMenuForm.MakeAllDataLength</c> (slice 2d) — a recursive 28-byte MIX-tree walk
        /// with a shared visited-set. Reproduced VERBATIM from the WinForms
        /// <c>MakeAllDataLength</c> + <c>MakeAllDataLengthSub</c>:
        /// <list type="bullet">
        ///   <item>6 roots (<c>status_rmenu_unit/game/3/4/5/6_pointer</c>); for each non-0 root
        ///   <c>pointer</c>, walk from <c>p = p32(pointer)</c> with the root <c>pointer</c> as the
        ///   incoming pointer slot.</item>
        ///   <item>The <c>foundDic</c> visited-set is SHARED across all 6 roots (a node reachable from
        ///   two roots is emitted once), and is the cycle-guard: a node already in the set is neither
        ///   re-emitted nor re-descended (stops self-referential / cyclic trees).</item>
        ///   <item>Per node <c>p</c>: if <c>!isSafetyOffset(p + 27)</c> bail (full 28-byte node — WF's
        ///   verbatim guard is <c>p + 18</c>, widened to <c>p + 27</c> to cover the u16(p+18) + the two
        ///   AddFunction u32 reads at p+20/p+24 so malformed near-EOF nodes skip instead of throwing);
        ///   name = "RMENU " +
        ///   <c>u16(p+18)</c>; emit a 28-byte MIX <see cref="Address"/> (pointer = NOT_FOUND, block 28,
        ///   pointerIndexes {0,4,8,12,20,24}) only if not yet visited; mark visited; then recurse into
        ///   the 4 sub-pointers at offsets 0/4/8/12 (each guarded by isSafetyOffset + not-visited);
        ///   then 2 ASM functions at offsets 20/24.</item>
        /// </list>
        /// The visited-check is keyed on the NODE ADDRESS <c>p</c> (matching WF's
        /// <c>foundDic.ContainsKey(p)</c>), so the emit-once + cycle-guard are byte-faithful.
        /// </summary>
        public static void EmitStatusRMenuTree(ROM rom, List<Address> list)
        {
            uint[] roots = new uint[]
            {
                rom.RomInfo.status_rmenu_unit_pointer,
                rom.RomInfo.status_rmenu_game_pointer,
                rom.RomInfo.status_rmenu3_pointer,
                rom.RomInfo.status_rmenu4_pointer,
                rom.RomInfo.status_rmenu5_pointer,
                rom.RomInfo.status_rmenu6_pointer,
            };
            EmitStatusRMenuRoots(rom, list, roots);
        }

        /// <summary>Walk the StatusRMenu MIX tree from an explicit set of root RomInfo pointers
        /// (test seam — lets a synthetic ROM supply roots without populating RomInfo). The
        /// <c>foundDic</c> visited-set is SHARED across all roots (dedup + cycle-guard, verbatim WF);
        /// each non-0 root <c>pointer</c> starts the walk at <c>p = p32(pointer)</c>.</summary>
        public static void EmitStatusRMenuRoots(ROM rom, List<Address> list, uint[] roots)
        {
            uint[] pointerIndexes = new uint[] { 0, 4, 8, 12, 20, 24 };
            var foundDic = new Dictionary<uint, bool>();
            foreach (uint root in roots)
            {
                // Guard the FULL 4-byte pointer slot before p32: U.isSafetyOffset(root) alone leaves
                // root+1..root+3 unchecked, and ROM.p32 only short-circuits when root >= Data.Length
                // (a root in [Len-3, Len-1] still reaches u32 -> check_safety throws). Matches the
                // Core-wide convention (MakeVarsIDArrayCore.CollectStatusRMenu). On valid ROMs roots
                // are never near EOF, so this only hardens synthetic/corrupted ROMs (WF throws here too).
                if (root == 0 || !U.isSafetyOffset(root + 3, rom))
                {
                    continue;
                }
                uint p = rom.p32(root + 0);
                EmitStatusRMenuSub(rom, list, p, root, foundDic, pointerIndexes);
            }
        }

        /// <summary>One recursive node of <see cref="EmitStatusRMenuTree"/> — the Core port of
        /// <c>StatusRMenuForm.MakeAllDataLengthSub</c>. <paramref name="pointer"/> is the incoming
        /// pointer slot (the +0/4/8/12 field of the parent, or the root RomInfo pointer).</summary>
        public static void EmitStatusRMenuSub(ROM rom, List<Address> list, uint p, uint pointer,
            Dictionary<uint, bool> foundDic, uint[] pointerIndexes)
        {
            // Guard the FULL 28-byte node, not just p+18: this method reads u16(p+18) (-> p+19) and two
            // AddFunction u32 reads at p+20/p+24 (the latter -> p+27, the deepest read). isSafetyOffset(
            // p+18) alone leaves p+19..p+27 unchecked, so a node near EOF would throw in u16 / AddFunction
            // -> u32 -> check_safety. On valid ROMs every 28-byte node is fully in-bounds, so this changes
            // nothing there (WF's verbatim p+18 guard crashes on such malformed near-EOF nodes); it only
            // hardens synthetic/corrupted ROMs to skip gracefully. p+27 covers the recursion p32 reads too.
            if (!U.isSafetyOffset(p + 27, rom))
            {
                return;
            }

            string name = "RMENU " + U.To0xHexString(rom.u16(p + 18));
            if (!foundDic.ContainsKey(p))
            {
                // WF: new Address(p, 28, NOT_FOUND, name, MIX, 28, pointerIndexes). Note the incoming
                // `pointer` slot is NOT recorded on the node itself (WF passes NOT_FOUND); the parent's
                // pointer FIELD is relocated via the parent node's pointerIndexes {0,4,8,12}.
                list.Add(new Address(p, 28, U.NOT_FOUND, name,
                    Address.DataTypeEnum.MIX, 28, pointerIndexes));
            }
            foundDic[p] = true;

            // Recurse the 4 child sub-pointers at offsets 0/4/8/12 (verbatim guard order).
            uint pp;
            pp = rom.p32(p + 0);
            if (U.isSafetyOffset(pp, rom) && !foundDic.ContainsKey(pp))
            {
                EmitStatusRMenuSub(rom, list, pp, p + 0, foundDic, pointerIndexes);
            }
            pp = rom.p32(p + 4);
            if (U.isSafetyOffset(pp, rom) && !foundDic.ContainsKey(pp))
            {
                EmitStatusRMenuSub(rom, list, pp, p + 4, foundDic, pointerIndexes);
            }
            pp = rom.p32(p + 8);
            if (U.isSafetyOffset(pp, rom) && !foundDic.ContainsKey(pp))
            {
                EmitStatusRMenuSub(rom, list, pp, p + 8, foundDic, pointerIndexes);
            }
            pp = rom.p32(p + 12);
            if (U.isSafetyOffset(pp, rom) && !foundDic.ContainsKey(pp))
            {
                EmitStatusRMenuSub(rom, list, pp, p + 12, foundDic, pointerIndexes);
            }

            // 2 ASM functions at offsets 20/24 (extent determined by the disassembler at rebuild).
            Address.AddFunction(list, p + 20, name + "+P20");
            Address.AddFunction(list, p + 24, name + "+P24");
        }

        /// <summary>
        /// <c>MenuDefinitionForm.MakeAllDataLength</c> (slice 2d). Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>6 pointers (<c>menu_definiton</c>, <c>menu_promotion</c>,
        ///   <c>menu_promotion_branch</c>, <c>menu_definiton_split</c>,
        ///   <c>menu_definiton_worldmap</c>, <c>menu_definiton_worldmap_shop</c>); skip 0.</item>
        ///   <item>Per pointer: <c>ReInitPointer</c> the main IFR (block 36, base = <c>p32(pointer)</c>,
        ///   IsDataExists = <c>isPointer(u32(addr+8))</c>), emit it with length <b>NOT +1</b>
        ///   (<c>AddAddressButDoNotLengthPuls1</c> = block × DataCount), type
        ///   <see cref="Address.DataTypeEnum.InputFormRef_1"/>, pointerIndexes {8,12,16,20,24,28,32}.</item>
        ///   <item>Per entry (<c>p = base + i*36</c>): if <c>!isSafetyOffset(p32(8+p))</c> skip; else
        ///   recurse into a MenuCommand sub-table at <c>8+p</c> (<see cref="EmitMenuCommandSubTable"/>),
        ///   then 6 ASM <see cref="Address"/>es at offsets 12/16/20/24/28/32 (each
        ///   <c>AddAddress(ProgramAddrToPlain(p32(off+p)), 0, p+off, name, ASM)</c>).</item>
        /// </list>
        /// </summary>
        public static void EmitMenuDefinition(ROM rom, List<Address> list)
        {
            uint[] pointers = new uint[]
            {
                rom.RomInfo.menu_definiton_pointer,
                rom.RomInfo.menu_promotion_pointer,
                rom.RomInfo.menu_promotion_branch_pointer,
                rom.RomInfo.menu_definiton_split_pointer,
                rom.RomInfo.menu_definiton_worldmap_pointer,
                rom.RomInfo.menu_definiton_worldmap_shop_pointer,
            };
            EmitMenuDefinitionPointers(rom, list, pointers);
        }

        /// <summary>MenuDefinition walk from an explicit set of RomInfo table pointers (test seam —
        /// lets a synthetic ROM supply pointers without populating RomInfo). See
        /// <see cref="EmitMenuDefinition"/> for the verbatim WF reproduction.</summary>
        public static void EmitMenuDefinitionPointers(ROM rom, List<Address> list, uint[] pointers)
        {
            const uint block = 36;
            foreach (uint rawPointer in pointers)
            {
                if (rawPointer == 0)
                {
                    continue;
                }
                uint pointer = U.toOffset(rawPointer);
                if (!U.isSafetyOffset(pointer, rom))
                {
                    continue;
                }
                uint baseAddr = rom.p32(pointer);
                if (!U.isSafetyOffset(baseAddr, rom))
                {
                    continue;
                }

                // Main IFR IsDataExists = U.isPointer(u32(addr+8)) -> a NULL slot terminates.
                uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                    U.isPointer(rom.u32(addr + 8)));

                // AddAddressButDoNotLengthPuls1: length = block * DataCount (NO +1), type
                // InputFormRef_1, pointerIndexes {8,12,16,20,24,28,32}.
                uint length = block * dataCount;
                list.Add(new Address(baseAddr, length, pointer, "MenuDefinition",
                    Address.DataTypeEnum.InputFormRef_1, block,
                    new uint[] { 8, 12, 16, 20, 24, 28, 32 }));

                for (uint i = 0; i < dataCount; i++)
                {
                    uint p = baseAddr + i * block;
                    string name = "MenuDef" + i + "_";

                    uint paddr = rom.p32(8 + p);
                    if (!U.isSafetyOffset(paddr, rom))
                    {
                        continue;
                    }
                    // Recurse into the MenuCommand sub-table behind the +8 pointer.
                    EmitMenuCommandSubTable(rom, list, 8 + p, name);

                    // 6 ASM blocks at offsets 12/16/20/24/28/32. WF does NOT pre-check isSafetyPointer
                    // before ProgramAddrToPlain here (unlike AddFunction); Address.AddAddress applies
                    // the safety guard AFTER (return-early on an unsafe resolved addr) — byte-faithful.
                    EmitMenuDefAsm(rom, list, p, 12, name + "_HandleBPress");
                    EmitMenuDefAsm(rom, list, p, 16, name + "_HandleRPress");
                    EmitMenuDefAsm(rom, list, p, 20, name + "_Construction");
                    EmitMenuDefAsm(rom, list, p, 24, name + "_Destruction");
                    EmitMenuDefAsm(rom, list, p, 28, name + "_UnkP28");
                    EmitMenuDefAsm(rom, list, p, 32, name + "_Unk32");
                }
            }
        }

        /// <summary>One MenuDefinition/MenuCommand per-entry ASM block: WF
        /// <c>Address.AddAddress(list, DisassemblerTrumb.ProgramAddrToPlain(p32(off+p)), 0, p+off,
        /// name, ASM)</c>. Note <c>ProgramAddrToPlain</c> (= <c>U.Padding2Before</c>, clears the thumb
        /// LSB only) is applied to the RAW <c>p32</c> result with NO prior pointer-safety check — this
        /// is the WF behaviour, deliberately preserved (distinct from <see cref="Address.AddFunction"/>,
        /// which DOES pre-check). The length is 0 (the disassembler determines the routine extent at
        /// rebuild).
        /// <para>RELEASE-SAFETY (the #1261 no-divergence audit): an out-of-range
        /// <c>ProgramAddrToPlain</c> result does NOT throw in Release (nor Debug). Core
        /// <see cref="Address.AddAddress"/> early-returns (<c>if (!isSafetyOffset(addr)) return;</c>)
        /// BEFORE the <see cref="Address"/> ctor — byte-identical to WF's AddAddress. Because the
        /// producer requires <c>rom == CoreState.ROM</c>, that early-return and the ctor's
        /// <c>Debug.Assert(isSafetyOffset)</c> test the SAME <c>CoreState.ROM.Data.Length</c>, so the
        /// ctor is never reached with an unsafe addr in either build. A junk/NULL <c>p32</c> therefore
        /// silently skips that ASM block in Core exactly as it does in WF — no throw, no divergence.</para>
        /// </summary>
        static void EmitMenuDefAsm(ROM rom, List<Address> list, uint p, uint off, string name)
        {
            uint paddr = rom.p32(off + p);
            Address.AddAddress(list, DisassemblerTrumb.ProgramAddrToPlain(paddr), 0,
                p + off, name, Address.DataTypeEnum.ASM);
        }

        /// <summary>
        /// <c>MenuCommandForm.MakeAllDataLengthP</c> (slice 2d) — the MenuDefinition sub-table.
        /// Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>base = <c>p32(pointer)</c>; block = <c>MENU_SIZE</c> = 36; IsDataExists =
        ///   <c>isPointer(u32(addr+0xc))</c>.</item>
        ///   <item>Main IFR <see cref="Address"/>: length +1 (block × (DataCount+1)), type
        ///   <see cref="Address.DataTypeEnum.InputFormRef_MIX"/>, pointerIndexes
        ///   {0,12,16,20,24,28,32}, Info "MENU".</item>
        ///   <item>Per entry: a CString behind the embedded pointer at offset 0
        ///   (<c>AddCString(0+p)</c>; needs an encoder — gracefully skipped if none, like the other
        ///   string sub-walks), then 6 ASM blocks at offsets 12/16/20/24/28/32 (same
        ///   <c>AddAddress(ProgramAddrToPlain(...), 0, ...)</c> shape as MenuDefinition).</item>
        /// </list>
        /// This is the only nesting under MenuDefinition and it bottoms out cleanly (no further
        /// un-ported recursion). The WF per-entry ASM labels are localized strings; static labels are
        /// used here (non-load-bearing for relocation).
        /// </summary>
        public static void EmitMenuCommandSubTable(ROM rom, List<Address> list, uint pointer, string name)
        {
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

            const uint block = 36; // MenuCommandForm.MENU_SIZE
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                U.isPointer(rom.u32(addr + 0xc)));

            // Main IFR: AddAddress(ifr, "MENU", {0,12,16,20,24,28,32}, InputFormRef_MIX) ->
            // length = block * (DataCount + 1) (the +1 form).
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "MENU",
                Address.DataTypeEnum.InputFormRef_MIX, block,
                new uint[] { 0, 12, 16, 20, 24, 28, 32 }));

            bool hasEncoder = CoreState.SystemTextEncoder != null;
            for (uint i = 0; i < dataCount; i++)
            {
                uint p = baseAddr + i * block;

                // AddCString(0+p): the menu-name C string behind the +0 pointer. Needs an encoder;
                // skip (don't NRE) if none is loaded (the wiring slice gates on IsComplete anyway).
                if (hasEncoder)
                {
                    Address.AddCString(list, 0 + p);
                }

                // 6 ASM blocks at offsets 12/16/20/24/28/32 (verbatim WF, raw ProgramAddrToPlain).
                EmitMenuDefAsm(rom, list, p, 12, name + "_p12");
                EmitMenuDefAsm(rom, list, p, 16, name + "_p16");
                EmitMenuDefAsm(rom, list, p, 20, name + "_p20");
                EmitMenuDefAsm(rom, list, p, 24, name + "_p24");
                EmitMenuDefAsm(rom, list, p, 28, name + "_p28");
                EmitMenuDefAsm(rom, list, p, 32, name + "_p32");
            }
        }

        /// <summary>
        /// <c>ImageBattleScreenForm.MakeAllDataLength</c> (slice 2e) — a FLAT sequence of fixed-size
        /// TSA + palette blocks and LZ77 images, all from RomInfo pointer slots (no per-entry table
        /// walk). Reproduced VERBATIM: five <c>battle_screen_TSA{1..5}</c> blocks of the WF constant
        /// lengths (type TSA), one <c>battle_screen_palette</c> (0x20*4, PAL), five
        /// <c>battle_screen_image{1..5}</c> LZ77 images via <see cref="Address.AddLZ77Pointer"/>. The WF
        /// image labels are all "battle_screen_image1" (a copy/paste in the original) — preserved
        /// verbatim (the label is non-load-bearing).
        /// </summary>
        public static void EmitImageBattleScreen(ROM rom, List<Address> list)
        {
            // Each TSA: tsa = p32(ptr); AddAddress(list, tsa, <const>, ptr, name, TSA). AddAddress
            // re-checks addr/pointer safety and computes nothing — the length is a pure constant.
            uint tsa;
            tsa = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            Address.AddAddress(list, tsa, (5 + 1) * ((15 + 1) - 1) * 2,
                rom.RomInfo.battle_screen_TSA1_pointer, "battle_screen_TSA1", Address.DataTypeEnum.TSA);
            tsa = rom.p32(rom.RomInfo.battle_screen_TSA2_pointer);
            Address.AddAddress(list, tsa, (5 + 1) * ((30 + 16) - 1) * 2,
                rom.RomInfo.battle_screen_TSA2_pointer, "battle_screen_TSA2", Address.DataTypeEnum.TSA);
            tsa = rom.p32(rom.RomInfo.battle_screen_TSA3_pointer);
            Address.AddAddress(list, tsa, ((19 + 1) - 13) * ((15 + 1) - 1) * 2,
                rom.RomInfo.battle_screen_TSA3_pointer, "battle_screen_TSA3", Address.DataTypeEnum.TSA);
            tsa = rom.p32(rom.RomInfo.battle_screen_TSA4_pointer);
            Address.AddAddress(list, tsa, ((19 + 1) - 13) * ((31 + 1) - 16) * 2,
                rom.RomInfo.battle_screen_TSA4_pointer, "battle_screen_TSA4", Address.DataTypeEnum.TSA);
            tsa = rom.p32(rom.RomInfo.battle_screen_TSA5_pointer);
            Address.AddAddress(list, tsa, ((19 + 1) - 0) * ((32 + 1) - 31) * 2,
                rom.RomInfo.battle_screen_TSA5_pointer, "battle_screen_TSA5", Address.DataTypeEnum.TSA);

            uint pal = rom.p32(rom.RomInfo.battle_screen_palette_pointer);
            Address.AddAddress(list, pal, 0x20 * 4,
                rom.RomInfo.battle_screen_palette_pointer, "battle_screen_palette", Address.DataTypeEnum.PAL);

            // Five LZ77 images via AddLZ77Pointer (length = getCompressedSize, isPointerOnly: false —
            // the producer always computes real lengths for a defragment). WF labels are all
            // "battle_screen_image1" verbatim.
            Address.AddLZ77Pointer(list, rom.RomInfo.battle_screen_image1_pointer, "battle_screen_image1", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, rom.RomInfo.battle_screen_image2_pointer, "battle_screen_image1", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, rom.RomInfo.battle_screen_image3_pointer, "battle_screen_image1", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, rom.RomInfo.battle_screen_image4_pointer, "battle_screen_image1", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, rom.RomInfo.battle_screen_image5_pointer, "battle_screen_image1", false, Address.DataTypeEnum.LZ77IMG);
        }

        /// <summary>
        /// <c>WorldMapImageFE6Form.MakeAllDataLength</c> (slice 2e, FE6 only) — a FLAT sequence of
        /// <see cref="Address.AddLZ77Pointer"/> calls: five (image, palette) pairs at offsets 0/8/16/
        /// 24/32 off the two RomInfo pointers <c>worldmap_big_image_pointer</c> /
        /// <c>worldmap_big_palette_pointer</c> (image = LZ77IMG, palette = LZ77PAL). Reproduced
        /// VERBATIM (same pointer-slot offsets, labels, types, order).
        /// </summary>
        public static void EmitWorldMapImageFE6(ROM rom, List<Address> list)
        {
            uint imgP = rom.RomInfo.worldmap_big_image_pointer;
            uint palP = rom.RomInfo.worldmap_big_palette_pointer;
            Address.AddLZ77Pointer(list, imgP + 0, "worldmap_big_image", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, palP + 0, "worldmap_big_palette", false, Address.DataTypeEnum.LZ77PAL);
            Address.AddLZ77Pointer(list, imgP + 8, "worldmap_big_imageNW", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, palP + 8, "worldmap_big_paletteNW", false, Address.DataTypeEnum.LZ77PAL);
            Address.AddLZ77Pointer(list, imgP + 16, "worldmap_big_imageNE", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, palP + 16, "worldmap_big_paletteNE", false, Address.DataTypeEnum.LZ77PAL);
            Address.AddLZ77Pointer(list, imgP + 24, "worldmap_big_imageSW", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, palP + 24, "worldmap_big_paletteSW", false, Address.DataTypeEnum.LZ77PAL);
            Address.AddLZ77Pointer(list, imgP + 32, "worldmap_big_imageSE", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, palP + 32, "worldmap_big_paletteSE", false, Address.DataTypeEnum.LZ77PAL);
        }

        /// <summary>
        /// <c>WorldMapImageFE7Form.MakeAllDataLength</c> (slice 2e, FE7 only) — the big-map block: a
        /// PAL (0x20*4), then a FIXED 12-entry loop reading <c>imagemap = p32(worldmap_big_image_pointer)</c>
        /// (advancing 4 bytes per entry) for a constant-size IMG (256/2*256), and a TSA (256/8*256/8)
        /// read from <c>tsamap = p32(worldmap_big_palettemap_pointer)</c>. NOTE: the WF loop reads
        /// <c>tsa = p32(tsamap)</c> WITHOUT advancing <c>tsamap</c> — every TSA entry points at the
        /// SAME slot; this (likely-WF-quirk) is reproduced VERBATIM to keep the produced Address list
        /// byte-identical. Then the three flat <c>worldmap_event_*</c> blocks (LZ77 image, LZ77 TSA,
        /// fixed PAL 0x20*4).
        /// </summary>
        public static void EmitWorldMapImageFE7(ROM rom, List<Address> list)
        {
            uint imagemap = rom.p32(rom.RomInfo.worldmap_big_image_pointer);
            uint palette = rom.p32(rom.RomInfo.worldmap_big_palette_pointer);
            uint tsamap = rom.p32(rom.RomInfo.worldmap_big_palettemap_pointer);

            Address.AddAddress(list, palette, 0x20 * 4,
                rom.RomInfo.worldmap_big_palette_pointer, "worldmap_big_palette", Address.DataTypeEnum.PAL);

            uint pointer = imagemap;
            for (int i = 0; i < 12; i++, pointer += 4)
            {
                uint image = rom.p32(pointer);
                uint imagelength = 256 / 2 * 256;
                Address.AddAddress(list, image, imagelength, pointer, "worldmap_big_image" + i, Address.DataTypeEnum.IMG);

                uint tsa = rom.p32(tsamap); // WF does NOT advance tsamap — verbatim.
                uint tsalength = 256 / 8 * 256 / 8;
                Address.AddAddress(list, tsa, tsalength, tsamap, "worldmap_big_tsa" + i, Address.DataTypeEnum.TSA);
            }

            Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_event_image_pointer, "worldmap_event_image", false, Address.DataTypeEnum.LZ77IMG);
            Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_event_tsa_pointer, "worldmap_event_tsa", false, Address.DataTypeEnum.LZ77TSA);
            Address.AddPointer(list, rom.RomInfo.worldmap_event_palette_pointer, 0x20 * 4, "worldmap_event_palette", Address.DataTypeEnum.PAL);
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
                case DataCountRule.TerminatorWithEmptyGuard:
                {
                    // General "read until terminator value(s), but ignore stray empty holes" idiom.
                    // Width selects the read; one or two stop values terminate; the empty-guard is
                    // optional. Reproduces SupportTalk*/EventBattleTalkFE6/EventHaikuFE6/SoundRoom*.
                    uint width = d.RuleWidth;
                    // Only 1/2/4 are valid read widths; anything else (0/3/...) is a
                    // descriptor bug — fail loudly rather than silently treat it as u16.
                    if (width != 1 && width != 2 && width != 4)
                        throw new ArgumentOutOfRangeException(
                            nameof(d) + "." + nameof(d.RuleWidth), width,
                            "RuleWidth must be 1, 2, or 4.");
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        uint v;
                        if (width == 1) v = rom.u8(addr + d.RuleOffset);
                        else if (width == 4) v = rom.u32(addr + d.RuleOffset);
                        else v = rom.u16(addr + d.RuleOffset); // width == 2
                        if (v == d.RuleStopValue) return false;
                        if (d.RuleStopValue2.HasValue && v == d.RuleStopValue2.Value) return false;
                        if (d.HasEmptyGuard && i > 10 && rom.IsEmpty(addr, d.BlockSize * 10)) return false;
                        return true;
                    };
                }
                case DataCountRule.FixedCountU8Address:
                    return (i, addr) =>
                    {
                        // StatusOptionOrderForm.Init: count = u8(status_game_option_order_count_address).
                        uint count = rom.u8(d.CountAddressField(rom));
                        return i < count;
                    };
                case DataCountRule.SummonsDemonKingRule:
                    return (i, addr) =>
                    {
                        // SummonsDemonKingForm.Init: max = u8(count_addr); if (max>=100) stop;
                        // return i <= max (note <=, so count = max+1).
                        uint max = rom.u8(d.CountAddressField(rom));
                        if (max >= 100) return false;
                        return i <= max;
                    };
                case DataCountRule.U32InRangeAt:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // EventFinalSerifFE7Form.Init: u32(addr+0) <= 0xff && >= 0x1.
                        uint v = rom.u32(addr + d.RuleOffset);
                        return v >= d.RuleRangeLo && v <= d.RuleRangeHi;
                    };
                case DataCountRule.TripleU32PointerOrNullAt121620:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // WorldMapPointForm.Init: the 12/16/20 fields are each pointer-or-NULL.
                        return U.isPointerOrNULL(rom.u32(addr + 12))
                            && U.isPointerOrNULL(rom.u32(addr + 16))
                            && U.isPointerOrNULL(rom.u32(addr + 20));
                    };
                case DataCountRule.WorldMapBGMRule:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // WorldMapBGMForm.Init: stop on (song1,song2) == (1,0) or (0,0).
                        uint song1 = rom.u16(addr + 0);
                        uint song2 = rom.u16(addr + 2);
                        if (song1 == 1 && song2 == 0) return false;
                        if (song1 == 0 && song2 == 0) return false;
                        return true;
                    };
                case DataCountRule.DicMainRule:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // TextDicForm.Init (dic_main): stop when u16(+2) <= 0 || u16(+4) <= 0.
                        uint text1 = rom.u16(addr + 2);
                        uint text2 = rom.u16(addr + 4);
                        if (text1 <= 0 || text2 <= 0) return false;
                        return true;
                    };
                case DataCountRule.TwoU32PointerAt04:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // ImageBattleBGForm.Init: 0 と 4 がポインタであればデータがある.
                        return U.isPointer(rom.u32(addr + 0))
                            && U.isPointer(rom.u32(addr + 4));
                    };
                case DataCountRule.WaitIconRule:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // ImageUnitWaitIconFrom.Init verbatim: entry 0 always; then the +4 field.
                        if (i == 0) return true;
                        uint a = rom.u32(addr + 4);
                        if (U.isPointer(a)) return true;
                        if (a == 0)
                        {
                            uint flags = rom.u32(addr + 0);
                            if (flags == 0) return false; // both 0 -> terminator
                            return true;
                        }
                        return false; // non-zero non-pointer -> terminator
                    };
                case DataCountRule.UnitPaletteRule:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // ImageUnitPaletteForm.Init verbatim: +12 palette pointer, +0 name tie-break.
                        uint p = rom.u32(addr + 12);
                        if (U.isPointer(p)) return true;
                        if (p == 0)
                        {
                            uint name = rom.u32(addr + 0);
                            if (name == 0) return false; // name also NULL -> terminator
                        }
                        return true; // any other value -> valid
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

            // ---- slice 2e: flat LZ77-image + palette IFR-loop forms (version-agnostic) ----
            // Each is the Core port of an ImageXxxForm.MakeAllDataLength that emits a main IFR Address
            // (AddressWinForms.AddAddress(list, IFR, name, pointerIndexes) -> length = block ×
            // (DataCount+1)) PLUS a per-entry loop of AddLZ77Pointer / AddPointer columns. The
            // per-entry columns are the SubWalks (Lz77Pointer / FixedPointer). EVERY length is either
            // LZ77.getCompressedSize (EOF-safe, 0 on malformed) or a CONSTANT palette/image size — no
            // ImageUtil/TSA-header/frame-walk dependency. The forms that DO need such a subsystem
            // (AddHeaderTSAPointer, ImageUtil*.RecycleOldAnime, config-file g_TSAAnime/g_ROMAnime,
            // ImageUtilOAM, IsHalfBodyFlag, AddAPPointer) stay in GetNotYetPortedForms.

            // ImageBattleBGForm.MakeAllDataLength — Info "BattleBG", block 12, base battle_bg_pointer,
            // IsDataExists = isPointer(u32+0) && isPointer(u32+4) (TwoU32PointerAt04), pointerIndexes
            // {0,4,8}. Per entry: LZ77IMG @0, LZ77IMG @4 (the WF "_tsa" column is tagged LZ77IMG, NOT
            // LZ77TSA — reproduced verbatim), LZ77PAL @8.
            l.Add(new StructDescriptor
            {
                Name = "BattleBG",
                PointerField = r => r.RomInfo.battle_bg_pointer,
                BlockSize = 12,
                Rule = DataCountRule.TwoU32PointerAt04,
                PointerIndexes = new uint[] { 0, 4, 8 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "BattleBG " + U.To0xHexString((uint)i) + "_img" },
                    new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "BattleBG " + U.To0xHexString((uint)i) + "_tsa" },
                    new SubWalk { EmbeddedPointerOffset = 8, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77PAL, Name = (r, i) => "BattleBG " + U.To0xHexString((uint)i) + "_lz77pal" },
                },
            });

            // ImageBattleTerrainForm.MakeAllDataLength — Info "BattleTerrain", block 24, base
            // battle_terrain_pointer, IsDataExists = isPointer(u32+12) (PointerAt @12), pointerIndexes
            // {12,16}. Per entry: LZ77IMG @12, fixed PAL (0x20) @16.
            l.Add(new StructDescriptor
            {
                Name = "BattleTerrain",
                PointerField = r => r.RomInfo.battle_terrain_pointer,
                BlockSize = 24,
                Rule = DataCountRule.PointerAt,
                RuleOffset = 12,
                PointerIndexes = new uint[] { 12, 16 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 12, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "BattleTerrain 0x" + U.ToHexString((int)i) },
                    new SubWalk { EmbeddedPointerOffset = 16, Kind = SubKind.FixedPointer, FixedLength = 0x20 * 1, DataType = Address.DataTypeEnum.PAL, Name = (r, i) => "BattleTerrain 0x" + U.ToHexString((int)i) },
                },
            });

            // ImageUnitWaitIconFrom.MakeAllDataLength — Info "WaitUnitIcon", block 8, base
            // unit_wait_icon_pointer, IsDataExists = WaitIconRule, pointerIndexes {4}. Per entry:
            // LZ77IMG @4.
            l.Add(new StructDescriptor
            {
                Name = "WaitUnitIcon",
                PointerField = r => r.RomInfo.unit_wait_icon_pointer,
                BlockSize = 8,
                Rule = DataCountRule.WaitIconRule,
                PointerIndexes = new uint[] { 4 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "WaitUnitIcon " + U.To0xHexString((uint)i) },
                },
            });

            // ImageUnitPaletteForm.MakeAllDataLength — Info "UnitPalette", block 16, base
            // image_unit_palette_pointer, IsDataExists = UnitPaletteRule, pointerIndexes {12}. Per
            // entry: LZ77PAL @12. (NOTE: the WF per-entry label uses i+1, faithfully reproduced.)
            l.Add(new StructDescriptor
            {
                Name = "UnitPalette",
                PointerField = r => r.RomInfo.image_unit_palette_pointer,
                BlockSize = 16,
                Rule = DataCountRule.UnitPaletteRule,
                PointerIndexes = new uint[] { 12 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 12, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77PAL, Name = (r, i) => "UnitPalette " + U.To0xHexString((uint)i + 1) },
                },
            });

            // ImageGenericEnemyPortraitForm.MakeAllDataLength — Info "GenericEnemyPortait" (WF
            // spelling preserved). This form is SPECIAL: it emits NO main IFR Address — instead a
            // standalone POINTER (8*2*4 = 64-byte) at generic_enemy_portrait_pointer ONCE, then a
            // per-entry loop (count = generic_enemy_portrait_count, block 4): a fixed IMG
            // (4*8/2)*(4*8)=512 @0 and a fixed PAL (0x20) @16. Modeled as a FixedCount descriptor with
            // EmitMainIfr=false + an ExtraFixedPointer for the standalone header + per-entry SubWalks.
            l.Add(new StructDescriptor
            {
                Name = "GenericEnemyPortait",
                PointerField = r => r.RomInfo.generic_enemy_portrait_pointer,
                BlockSize = 4,
                Rule = DataCountRule.FixedCount,
                FixedCountField = r => r.RomInfo.generic_enemy_portrait_count,
                PointerIndexes = new uint[] { },
                EmitMainIfr = false, // WF emits no main IFR AddAddress for this form
                ExtraFixedPointers = new[]
                {
                    new ExtraFixedPointer { PointerField = r => r.RomInfo.generic_enemy_portrait_pointer, FixedLength = 8 * 2 * 4, Name = "GenericEnemyPortait", DataType = Address.DataTypeEnum.POINTER },
                },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.FixedPointer, FixedLength = (4 * 8 / 2) * (4 * 8), DataType = Address.DataTypeEnum.IMG, Name = (r, i) => "GenericEnemyPortait 0x" + U.ToHexString((int)i) },
                    new SubWalk { EmbeddedPointerOffset = 16, Kind = SubKind.FixedPointer, FixedLength = 0x20 * 1, DataType = Address.DataTypeEnum.PAL, Name = (r, i) => "GenericEnemyPortait 0x" + U.ToHexString((int)i) },
                },
            });

            // ClassForm vs ClassFE6Form (slice 2c) — VERSION-SPECIFIC. The WinForms
            // MakeAllStructPointersList calls ClassForm.MakeAllDataLength ONLY inside the version==8
            // and version==7 branches, and ClassFE6Form.MakeAllDataLength inside version==6. The two
            // forms have DIFFERENT MoveCost offsets/lengths and pointerIndexes, so running the FE7/8
            // descriptor on a FE6 ROM would relocate the wrong bytes (silent corruption). We mirror
            // the WF version branch exactly.
            if (rom.RomInfo.version != 6)
            {
                // ClassForm (FE7/FE8).MakeAllDataLength — main IFR (block class_datasize,
                // U8NotZeroIndex0Always @+4, max 0x100, pointerIndexes {52,56,60,64,68,72,76}) PLUS a
                // per-entry MoveCost sub-walk: six 66-byte BIN blocks behind the embedded pointers at
                // offsets 56/60/64/68/72/76. Offset 52 is IN pointerIndexes (the battle-animation
                // pointer FIELD is relocated) but is NOT a MoveCost sub-walk — its TARGET is tracked
                // by the battle-anime form, so adding a 52 sub-walk here would double-track it. After
                // the per-entry loop, three 全クラス共通 terrain pointers (66-byte BIN each) are emitted once.
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
            }
            else
            {
                // ClassFE6Form.MakeAllDataLength — SAME main IFR Init (block class_datasize, base
                // class_pointer, U8NotZeroIndex0Always @+4, max 0x100) but FE6-specific:
                //   * pointerIndexes {48,52,56,60,64} (NOT {52..76}).
                //   * MoveCost sub-walk: FOUR 52-byte BIN blocks @ off {48,52,56,60}, names
                //     "MoveCost Clear/avoid/def/ref" (FE7/8 are SIX 66-byte @ {56..76}).
                //   * The MoveCost loop SKIPS class 0 (cid starts at 1) -> SubWalkStartIndex = 1.
                //   * Off 64 is in pointerIndexes (pointer FIELD relocated) but is NOT a MoveCost
                //     sub-walk (target owned by another form) — same don't-double-track posture as
                //     the FE7/8 off-52.
                //   * Three 全クラス共通 terrain pointers are 52-byte BIN (NOT 66), same names.
                l.Add(new StructDescriptor
                {
                    Name = "Class",
                    PointerField = r => r.RomInfo.class_pointer,
                    FixedCountField = null,
                    BlockSize = rom.RomInfo.class_datasize,
                    Rule = DataCountRule.U8NotZeroIndex0Always,
                    RuleOffset = 4,
                    MaxCount = 0x100,
                    PointerIndexes = new uint[] { 48, 52, 56, 60, 64 },
                    SubWalkStartIndex = 1, // FE6 skips class 0
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 48, Kind = SubKind.BinFixed, FixedLength = 52, Name = (r, i) => "MoveCost Clear" },
                        new SubWalk { EmbeddedPointerOffset = 52, Kind = SubKind.BinFixed, FixedLength = 52, Name = (r, i) => "MoveCost avoid" },
                        new SubWalk { EmbeddedPointerOffset = 56, Kind = SubKind.BinFixed, FixedLength = 52, Name = (r, i) => "MoveCost def" },
                        new SubWalk { EmbeddedPointerOffset = 60, Kind = SubKind.BinFixed, FixedLength = 52, Name = (r, i) => "MoveCost ref" },
                    },
                    ExtraFixedPointers = new[]
                    {
                        new ExtraFixedPointer { PointerField = r => r.RomInfo.terrain_recovery_pointer, FixedLength = 52, Name = "MoveCost ref" },
                        new ExtraFixedPointer { PointerField = r => r.RomInfo.terrain_bad_status_recovery_pointer, FixedLength = 52, Name = "MoveCost recovery bad status" },
                        new ExtraFixedPointer { PointerField = r => r.RomInfo.terrain_show_infomation_pointer, FixedLength = 52, Name = "MoveCost show infomation" },
                    },
                });
            }

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
            // MapTileAnimation1/2Form, MonsterWMapProbabilityForm, and the MapTerrain*LookupTable
            // forms are deliberately NOT here:
            //   * MapTileAnimation1/2 expand a per-entry embedded IMG/BIN sub-block (rule 3).
            //   * MonsterWMapProbabilityForm also runs an EventScriptForm.ScanScript event-scan
            //     in the same MakeAllDataLength (event-scan expansion, not a pure table walk).
            //   * MapTerrain{Floor,BG}LookupTableForm enumerate a PatchUtil-dependent GetPointers()
            //     set (extends-battle-BG patch detection), not a fixed RomInfo table.
            // They stay in GetNotYetPortedForms for a later slice. (CCBranchForm IS ported below now
            // — its ClassForm.DataCount() count is reproduced by ClassDataCount(rom), a pure walk.)
            //
            // StatusOptionOrderForm is called in the WF version==8 AND version==7 branches (its data
            // pointer is 0 elsewhere). Emit it for both; EmitOne skips a 0/unsafe pointer.
            if (rom.RomInfo.version == 8 || rom.RomInfo.version == 7)
            {
                // StatusOptionForm.MakeAllDataLength (slice 2d) — called in the WF version==8 AND
                // version==7 branches (right before StatusOptionOrderForm), so it is gated to v7||8
                // here in the same order. Main IFR: base status_game_option_pointer, block 44,
                // IsDataExists = U.isPointer(u32(addr+40)) -> DataCountRule.PointerAt @ off 40,
                // pointerIndexes {40}, DataType InputFormRef (default). Per entry: an ASM-function
                // sub-walk behind the embedded pointer at offset 40 (Address.AddFunction(p+40, name)).
                // The WF per-entry label is "GameOption " + GetNameFast(p) when !isPointerOnly (Huffman
                // text) or "" when isPointerOnly — neither affects addr/length, so a static
                // "GameOption" label is used (headless-safe, relocation-identical).
                l.Add(new StructDescriptor
                {
                    Name = "GameOption",
                    PointerField = r => r.RomInfo.status_game_option_pointer,
                    BlockSize = 44,
                    Rule = DataCountRule.PointerAt,
                    RuleOffset = 40,
                    PointerIndexes = new uint[] { 40 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 40, Kind = SubKind.AsmFunction, Name = (r, i) => "GameOption" },
                    },
                });

                // StatusOptionOrderForm.MakeAllDataLength — Info "GameOptionOrder", block 1,
                // IsDataExists = i < u8(status_game_option_order_count_address).
                l.Add(new StructDescriptor
                {
                    Name = "GameOptionOrder",
                    PointerField = r => r.RomInfo.status_game_option_order_pointer,
                    BlockSize = 1,
                    Rule = DataCountRule.FixedCountU8Address,
                    CountAddressField = r => r.RomInfo.status_game_option_order_count_address,
                    PointerIndexes = new uint[] { },
                });
            }

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

                // EDForm.MakeAllDataLength (FE8) — FOUR tables in WF order, each its own AddAddress
                // with a distinct Info suffix, empty pointerIndexes. The ed_* tables terminate on a
                // zero word/byte (NOT a pointer test), so the generic terminator rule (no empty-guard)
                // reproduces each lambda's count exactly:
                //   ed_1 (block 4, u32(addr)!=0)   "_1"   -> width 4, stop 0
                //   ed_2 (block 8, u8(addr)!=0)    "_2"   -> width 1, stop 0   (N1_Init)
                //   ed_3a(block 8, u32(addr)!=0)   "_3a"  -> width 4, stop 0   (N2_Init)
                //   ed_3b(block 8, u32(addr)!=0)   "_3b"  -> width 4, stop 0   (N2_Init ReInit ed_3b)
                l.Add(new StructDescriptor
                {
                    Name = "EDForm_1",
                    PointerField = r => r.RomInfo.ed_1_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDForm_2",
                    PointerField = r => r.RomInfo.ed_2_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 1, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDForm_3a",
                    PointerField = r => r.RomInfo.ed_3a_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDForm_3b",
                    PointerField = r => r.RomInfo.ed_3b_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });

                // CCBranchForm.MakeAllDataLength — Info "CCBranch", block 2, base ccbranch_pointer,
                // IsDataExists = i < ClassForm.DataCount(). Reproduced via ClassDataCount(rom).
                l.Add(new StructDescriptor
                {
                    Name = "CCBranch",
                    PointerField = r => r.RomInfo.ccbranch_pointer,
                    BlockSize = 2,
                    Rule = DataCountRule.FixedCount,
                    FixedCountField = r => ClassDataCount(r),
                    PointerIndexes = new uint[] { },
                });

                // OPClassAlphaNameForm.MakeAllDataLength — Info "CCClassAlphaName", block 20, base
                // class_alphaname_pointer, IsDataExists = i < ClassForm.DataCount().
                l.Add(new StructDescriptor
                {
                    Name = "CCClassAlphaName",
                    PointerField = r => r.RomInfo.class_alphaname_pointer,
                    BlockSize = 20,
                    Rule = DataCountRule.FixedCount,
                    FixedCountField = r => ClassDataCount(r),
                    PointerIndexes = new uint[] { },
                });

                // WorldMapPointForm.MakeAllDataLength — Info "WorldMapPoint", block 32, base
                // worldmap_point_pointer, IsDataExists = isPointerOrNULL(u32+12)&&(+16)&&(+20),
                // pointerIndexes {12,16,20}.
                l.Add(new StructDescriptor
                {
                    Name = "WorldMapPoint",
                    PointerField = r => r.RomInfo.worldmap_point_pointer,
                    BlockSize = 32,
                    Rule = DataCountRule.TripleU32PointerOrNullAt121620,
                    PointerIndexes = new uint[] { 12, 16, 20 },
                });

                // WorldMapBGMForm.MakeAllDataLength — Info "WorldMapBGM", block 4, base
                // worldmap_bgm_pointer, IsDataExists = WorldMapBGMRule.
                l.Add(new StructDescriptor
                {
                    Name = "WorldMapBGM",
                    PointerField = r => r.RomInfo.worldmap_bgm_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.WorldMapBGMRule,
                    PointerIndexes = new uint[] { },
                });

                // TextDicForm.MakeAllDataLength — THREE tables in WF order:
                //   dic_main   (block 12, DicMainRule)        "dic_main"
                //   dic_chaptor(block 4,  i < 9)              "dic_chaptor"
                //   dic_title  (block 2,  i < 12)             "dic_title"
                l.Add(new StructDescriptor
                {
                    Name = "dic_main",
                    PointerField = r => r.RomInfo.dic_main_pointer,
                    BlockSize = 12,
                    Rule = DataCountRule.DicMainRule,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "dic_chaptor",
                    PointerField = r => r.RomInfo.dic_chaptor_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.FixedCount,
                    RuleFixedCount = 9,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "dic_title",
                    PointerField = r => r.RomInfo.dic_title_pointer,
                    BlockSize = 2,
                    Rule = DataCountRule.FixedCount,
                    RuleFixedCount = 12,
                    PointerIndexes = new uint[] { },
                });

                // EventForceSortieForm.MakeAllDataLength (FE8) — Info "ForceSorite" (WF spelling),
                // block 4, base event_force_sortie_pointer, IsDataExists = u16(addr) != 0xFFFF.
                l.Add(new StructDescriptor
                {
                    Name = "ForceSorite",
                    PointerField = r => r.RomInfo.event_force_sortie_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.U16NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0xFFFF,
                    PointerIndexes = new uint[] { },
                });

                // SummonUnitForm.MakeAllDataLength — Info "Summon", block 2, base summon_unit_pointer,
                // IsDataExists = u8(addr) != 0x00.
                l.Add(new StructDescriptor
                {
                    Name = "Summon",
                    PointerField = r => r.RomInfo.summon_unit_pointer,
                    BlockSize = 2,
                    Rule = DataCountRule.U8NotEqual,
                    RuleOffset = 0,
                    RuleStopValue = 0x00,
                    PointerIndexes = new uint[] { },
                });

                // SummonsDemonKingForm.MakeAllDataLength — Info "Summons", block 20, base
                // summons_demon_king_pointer, IsDataExists = SummonsDemonKingRule
                // (max = u8(summons_demon_king_count_address); if max>=100 stop; i <= max).
                l.Add(new StructDescriptor
                {
                    Name = "Summons",
                    PointerField = r => r.RomInfo.summons_demon_king_pointer,
                    BlockSize = 20,
                    Rule = DataCountRule.SummonsDemonKingRule,
                    CountAddressField = r => r.RomInfo.summons_demon_king_count_address,
                    PointerIndexes = new uint[] { },
                });

                // UnitForm.MakeAllDataLength (FE8) — Info "UnitForm", block unit_datasize, base
                // unit_pointer, IsDataExists = i < unit_maxcount, pointerIndexes {44}. PLUS a per-entry
                // support sub-pointer: behind the embedded pointer at offset 44, a fixed-size BIN block
                // whose length is the support-struct size (24 on FE8 — the version==6 path that uses 32
                // is never hit here, this descriptor is FE8-only). WF guards `unitSupport>0`; BinFixed's
                // isSafetyOffset(target) guard is equivalent (a 0/NULL pointer is not a safe offset).
                l.Add(new StructDescriptor
                {
                    Name = "UnitForm",
                    PointerField = r => r.RomInfo.unit_pointer,
                    BlockSize = rom.RomInfo.unit_datasize,
                    Rule = DataCountRule.FixedCount,
                    FixedCountField = r => r.RomInfo.unit_maxcount,
                    PointerIndexes = new uint[] { 44 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 44, Kind = SubKind.BinFixed, FixedLength = 24, Name = (r, i) => "unitSupport " + U.To0xHexString((uint)i) },
                    },
                });

                // SupportTalkForm.MakeAllDataLength (FE8) — Info "SupportTalk", block 16, base
                // support_talk_pointer, IsDataExists = u16(addr)==0xFFFF terminator + empty-guard.
                l.Add(new StructDescriptor
                {
                    Name = "SupportTalk",
                    PointerField = r => r.RomInfo.support_talk_pointer,
                    BlockSize = 16,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 2, RuleOffset = 0, RuleStopValue = 0xFFFF, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { },
                });

                // ImageChapterTitleForm.MakeAllDataLength (FE8, slice 2e) — Info "ChapterTitleImage",
                // block 12, base image_chapter_title_pointer, IsDataExists = isPointer(u32+0)
                // (PointerAt @0), pointerIndexes {0,4,8}. Per entry: THREE LZ77IMG columns at 0/4/8
                // ("_Save"/"_Number"/"_Title"). (The FE7/FE6 path uses ImageChapterTitleFE7Form below —
                // block 4, ONE column.)
                l.Add(new StructDescriptor
                {
                    Name = "ChapterTitleImage",
                    PointerField = r => r.RomInfo.image_chapter_title_pointer,
                    BlockSize = 12,
                    Rule = DataCountRule.PointerAt,
                    RuleOffset = 0,
                    PointerIndexes = new uint[] { 0, 4, 8 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "ChapterTitleImage_Save" },
                        new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "ChapterTitleImage_Number" },
                        new SubWalk { EmbeddedPointerOffset = 8, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "ChapterTitleImage_Title" },
                    },
                });
            }
            else if (rom.RomInfo.version == 7)
            {
                // ---- version==7 (FE7) section ----
                // These forms are called ONLY inside the WF `else if (version == 7)` branch. Their
                // data pointers are 0 elsewhere (EmitOne skips a 0/unsafe pointer), but we gate per the
                // WF call structure exactly. Forms in that branch needing un-ported subsystems
                // (EDFE7Form's N3 has pointer 0 -> emitted as a 0-skip; the LZ77/Demo/ScanScript ones)
                // stay deferred.

                // EDSensekiCommentForm.MakeAllDataLength — see the shared v6+v7 block below.

                // EDFE7Form.MakeAllDataLength — FOUR tables in WF order:
                //   N3 (pointer 0 — "ポインタ指定ができない", block 12, u32!=0)  "_1"  -> a 0 pointer,
                //       EmitOne skips it (faithful: WF's AddAddress on a base-0 IFR adds nothing useful).
                //   N1 (ed_2,  block 8, u32!=0)  "_2"
                //   N2 (ed_3a, block 8, u32!=0)  "_3a"  + ed_3b ReInitPointer  "_3b"
                //   N4 (ed_1,  block 4, u32!=0)  "_4"
                l.Add(new StructDescriptor
                {
                    Name = "EDFE7Form_1",
                    PointerField = r => 0u, // N3: WF passes pointer 0 ("ポインタ指定ができない")
                    BlockSize = 12,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDFE7Form_2",
                    PointerField = r => r.RomInfo.ed_2_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDFE7Form_3a",
                    PointerField = r => r.RomInfo.ed_3a_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDFE7Form_3b",
                    PointerField = r => r.RomInfo.ed_3b_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EDFE7Form_4",
                    PointerField = r => r.RomInfo.ed_1_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });

                // UnitFE7Form.MakeAllDataLength — Info "Unit", block unit_datasize, base unit_pointer,
                // IsDataExists = i < unit_maxcount, pointerIndexes {44}. FLAT table — NO support
                // sub-walk (unlike the FE8 UnitForm). The FE6 variant (UnitFE6Form) is DEFERRED: its
                // base is p32(unit_pointer)+unit_datasize (skip class 0 via a direct ReInit), which the
                // pointer-slot descriptor model cannot express faithfully.
                l.Add(new StructDescriptor
                {
                    Name = "Unit",
                    PointerField = r => r.RomInfo.unit_pointer,
                    BlockSize = rom.RomInfo.unit_datasize,
                    Rule = DataCountRule.FixedCount,
                    FixedCountField = r => r.RomInfo.unit_maxcount,
                    PointerIndexes = new uint[] { 44 },
                });

                // SupportTalkFE7Form.MakeAllDataLength — Info "SupportTalkFE7", block 20, base
                // support_talk_pointer, IsDataExists = u16(addr)==0x0000 terminator + empty-guard.
                l.Add(new StructDescriptor
                {
                    Name = "SupportTalkFE7",
                    PointerField = r => r.RomInfo.support_talk_pointer,
                    BlockSize = 20,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 2, RuleOffset = 0, RuleStopValue = 0x0000, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { },
                });

                // SoundRoomCGForm.MakeAllDataLength — Info "SoundRoomCG", block 4, base
                // sound_room_cg_pointer, IsDataExists = u32(addr)==0xFFFFFFFF terminator (NO guard).
                l.Add(new StructDescriptor
                {
                    Name = "SoundRoomCG",
                    PointerField = r => r.RomInfo.sound_room_cg_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0xFFFFFFFF, HasEmptyGuard = false,
                    PointerIndexes = new uint[] { },
                });

                // TacticianAffinityFE7.MakeAllDataLength — Info "TacticianAffinity", block 4, base
                // tactician_affinity_pointer, IsDataExists = i < (is_multibyte ? 48 : 12).
                l.Add(new StructDescriptor
                {
                    Name = "TacticianAffinity",
                    PointerField = r => r.RomInfo.tactician_affinity_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.FixedCount,
                    FixedCountField = r => r.RomInfo.is_multibyte ? 48u : 12u,
                    PointerIndexes = new uint[] { },
                });

                // EventFinalSerifFE7Form.MakeAllDataLength — Info "EventFinalserif", block 8, base
                // event_final_serif_pointer, IsDataExists = u32(addr+0) >= 1 && <= 0xff.
                l.Add(new StructDescriptor
                {
                    Name = "EventFinalserif",
                    PointerField = r => r.RomInfo.event_final_serif_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.U32InRangeAt,
                    RuleOffset = 0,
                    RuleRangeLo = 0x1,
                    RuleRangeHi = 0xff,
                    PointerIndexes = new uint[] { },
                });

                // ImageChapterTitleFE7Form.MakeAllDataLength (FE7, slice 2e) — called in the WF FE7
                // branch ONLY inside `if (is_multibyte)` (the FE7U non-multibyte path uses a different
                // CG/title set). Gated identically here. Block 4, base image_chapter_title_pointer,
                // IsDataExists = isPointer(u32+0) (PointerAt @0), pointerIndexes {0}. Per entry: ONE
                // LZ77IMG column at 0.
                if (rom.RomInfo.is_multibyte)
                {
                    l.Add(MakeImageChapterTitleFE7Descriptor());
                }
            }
            else if (rom.RomInfo.version == 6)
            {
                // ---- version==6 (FE6) section ----
                // Forms called ONLY inside the WF `else if (version == 6)` branch. UnitFE6Form,
                // WorldMapEventPointerFE6Form (PLIST event-scan), MapSettingFE6Form (IsMapSettingEnd +
                // CString), SupportUnitFE6Form (GetUnitIDWhereSupportAddr) stay deferred.
                // ImagePortraitFE6Form STAYS too — its Init has a stateful nullContinuousCount
                // terminator + GetFEditorLengthHint, not faithfully reproducible by the stateless
                // descriptor rule model. The clean tables below are ported; ImageChapterTitleFE7Form
                // (flat LZ77) and WorldMapImageFE6Form (flat LZ77) are ported in slice 2e (see the
                // ImageChapterTitleFE7Form descriptor at the end of this branch + EmitWorldMapImageFE6).

                // EDFE6Form.MakeAllDataLength — Info "EDFE6Form", single table N2 (ed_3a, block 8),
                // IsDataExists = i < 0x42 (fixed cap).
                l.Add(new StructDescriptor
                {
                    Name = "EDFE6Form",
                    PointerField = r => r.RomInfo.ed_3a_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.FixedCount,
                    RuleFixedCount = 0x42,
                    PointerIndexes = new uint[] { },
                });

                // EventBattleTalkFE6Form.MakeAllDataLength — TWO tables in WF order:
                //   Init  (event_ballte_talk_pointer,  block 12) "EventBattleTalkFE6Form"
                //   N_Init(event_ballte_talk2_pointer, block 16) "EventBattleTalkFE6Form_2"
                // Both: u16(addr)==0x0 || ==0xFFFF terminator + empty-guard.
                l.Add(new StructDescriptor
                {
                    Name = "EventBattleTalkFE6Form",
                    PointerField = r => r.RomInfo.event_ballte_talk_pointer,
                    BlockSize = 12,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 2, RuleOffset = 0, RuleStopValue = 0x0, RuleStopValue2 = 0xFFFF, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { },
                });
                l.Add(new StructDescriptor
                {
                    Name = "EventBattleTalkFE6Form_2",
                    PointerField = r => r.RomInfo.event_ballte_talk2_pointer,
                    BlockSize = 16,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 2, RuleOffset = 0, RuleStopValue = 0x0, RuleStopValue2 = 0xFFFF, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { },
                });

                // EventHaikuFE6Form.MakeAllDataLength — Info "Haiku", single table event_haiku_pointer,
                // block 16, IsDataExists = u8(addr)==0x0 terminator + empty-guard.
                l.Add(new StructDescriptor
                {
                    Name = "Haiku",
                    PointerField = r => r.RomInfo.event_haiku_pointer,
                    BlockSize = 16,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 1, RuleOffset = 0, RuleStopValue = 0x0, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { },
                });

                // SupportTalkFE6Form.MakeAllDataLength — Info "SupportTalkFE6", block 16, base
                // support_talk_pointer, IsDataExists = u16(addr)==0x0000 terminator + empty-guard.
                l.Add(new StructDescriptor
                {
                    Name = "SupportTalkFE6",
                    PointerField = r => r.RomInfo.support_talk_pointer,
                    BlockSize = 16,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 2, RuleOffset = 0, RuleStopValue = 0x0000, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { },
                });

                // SoundRoomFE6Form.MakeAllDataLength — Info "SoundRoomFE6", block sound_room_datasize,
                // base sound_room_pointer, IsDataExists = u32(addr)==0xFFFFFFFF terminator + empty-guard,
                // pointerIndexes {4,8}.
                l.Add(new StructDescriptor
                {
                    Name = "SoundRoomFE6",
                    PointerField = r => r.RomInfo.sound_room_pointer,
                    BlockSize = rom.RomInfo.sound_room_datasize,
                    Rule = DataCountRule.TerminatorWithEmptyGuard,
                    RuleWidth = 4, RuleOffset = 0, RuleStopValue = 0xFFFFFFFF, HasEmptyGuard = true,
                    PointerIndexes = new uint[] { 4, 8 },
                });

                // OPClassAlphaNameFE6Form.MakeAllDataLength — Info "OPClassAlphaName", block 4, base
                // class_alphaname_pointer, IsDataExists = isPointerOrNULL(u32(addr+0)) -> PointerOrNullAt
                // @ off 0, pointerIndexes {0}. Per entry: a string-BIN block (length == strlen, NO +1)
                // behind the embedded pointer at offset 0 (same shape as MapTerrainNameForm).
                l.Add(new StructDescriptor
                {
                    Name = "OPClassAlphaName",
                    PointerField = r => r.RomInfo.class_alphaname_pointer,
                    BlockSize = 4,
                    Rule = DataCountRule.PointerOrNullAt,
                    RuleOffset = 0,
                    PointerIndexes = new uint[] { 0 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.BinString },
                    },
                });

                // ImageChapterTitleFE7Form.MakeAllDataLength (FE6, slice 2e) — the WF FE6 branch calls
                // it UNCONDITIONALLY (no is_multibyte gate, unlike FE7). Same descriptor as the FE7
                // multibyte path: block 4, base image_chapter_title_pointer, PointerAt @0, one LZ77IMG
                // column at 0.
                l.Add(MakeImageChapterTitleFE7Descriptor());
            }

            // EDSensekiCommentForm.MakeAllDataLength — called in the WF version==7 AND version==6
            // branches (NOT version==8). Info "EDSensekiForm", block 16, base senseki_comment_pointer,
            // IsDataExists = u16(addr) != 0x0.
            if (rom.RomInfo.version == 7 || rom.RomInfo.version == 6)
            {
                l.Add(new StructDescriptor
                {
                    Name = "EDSensekiForm",
                    PointerField = r => r.RomInfo.senseki_comment_pointer,
                    BlockSize = 16,
                    Rule = DataCountRule.U16NotZero,
                    RuleOffset = 0,
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

        /// <summary>The <c>ImageChapterTitleFE7Form.MakeAllDataLength</c> descriptor (slice 2e), shared
        /// by the FE7 (multibyte-gated) and FE6 (unconditional) branches: block 4, base
        /// <c>image_chapter_title_pointer</c>, IsDataExists = <c>isPointer(u32+0)</c>
        /// (<see cref="DataCountRule.PointerAt"/> @0), pointerIndexes {0}, one LZ77IMG column at 0.
        /// (Distinct from the FE8 <c>ImageChapterTitleForm</c>: block 12, THREE columns.)</summary>
        static StructDescriptor MakeImageChapterTitleFE7Descriptor()
        {
            return new StructDescriptor
            {
                Name = "ChapterTitleImage",
                PointerField = r => r.RomInfo.image_chapter_title_pointer,
                BlockSize = 4,
                Rule = DataCountRule.PointerAt,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 0 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "ChapterTitleImage" + U.To0xHexString((uint)i) },
                },
            };
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
                // (MapTerrainNameForm ported in slice 2c — per-entry string-BIN sub-walk; TextDicForm
                //  ported in this sweep — 3 clean tables (dic_main/chaptor/title). OtherTextForm STAYS:
                //  it iterates a config file `other_text_*` (U.ConfigDataFilename), not a RomInfo table,
                //  so it has a headless config-file dependency, not ROM-derived data.)
                "TextForm", "TextCharCodeForm", "OtherTextForm",
                // images (LZ77/TSA length calc)
                // (slice 2e ported the FLAT LZ77-image + palette forms whose every length is
                //  LZ77.getCompressedSize (EOF-safe) or a CONSTANT palette/image size:
                //    ImageBattleBGForm, ImageBattleTerrainForm, ImageUnitWaitIconFrom,
                //    ImageUnitPaletteForm, ImageGenericEnemyPortraitForm, ImageChapterTitleForm [FE8]
                //    + ImageChapterTitleFE7Form [FE7-multibyte/FE6] (StructDescriptor + Lz77Pointer/
                //    FixedPointer SubWalks); ImageBattleScreenForm, WorldMapImageFE6Form [FE6],
                //    WorldMapImageFE7Form [FE7] (dedicated flat emitters). The FE8 WorldMapImageForm
                //    STAYS (AddHeaderTSAPointer + AddROMTCSPointer — both ImageUtil-backed, not in Core).
                //  The rest STAY, each blocked on a subsystem not yet in Core (a wrong length relocates
                //  the wrong bytes = silent corruption):
                //    ImageBGForm/ImageCGForm/ImageSystemIconForm/ImageTSAAnime2Form/WorldMapImageForm —
                //      AddHeaderTSAPointer (ImageUtil.CalcByteLengthForHeaderTSAData).
                //    ImageMagicFEditorForm/ImageMagicCSACreatorForm/ImageMapActionAnimationForm —
                //      ImageUtil*.RecycleOldAnime (ImageUtil anime length helpers).
                //    ImageBattleAnimeForm — ImageUtilOAM.MakeAllDataLength (LZ77-embedded OAM pointers).
                //    ImageUnitMoveIconFrom — AddAPPointer (ImageUtilAP.CalcAPLength).
                //    ImageRomAnimeForm/ImageTSAAnimeForm — config-file tables (U.ConfigDataFilename
                //      "romanime_"/"tsaanime_") + dynamic pointer-list frame walks, not RomInfo slots.
                //    ImagePortraitForm — IsHalfBodyFlag runtime header inspection (+ ImagePortraitFE6Form
                //      has a stateful nullContinuousCount terminator + GetFEditorLengthHint).
                //    ImageItemIconForm — out of slice scope: a flat 128-byte icon-SHEET IFR (no LZ77
                //      image / palette to relocate) whose count rule reads a hardcoded FE7U magic addr.
                //    MapMiniMapTerrainImageForm — InputFormRef_ASM + AddFunctions, called from the
                //      AppendAllASMStructPointersList ASM path, not this producer's data path.)
                "ImageBattleAnimeForm", "ImageBGForm",
                "ImageMagicFEditorForm", "ImageMagicCSACreatorForm",
                "ImageItemIconForm", "ImageUnitMoveIconFrom",
                "ImageSystemIconForm", "ImageRomAnimeForm",
                "ImageMapActionAnimationForm", "ImageTSAAnimeForm",
                "ImageTSAAnime2Form", "ImagePortraitForm", "ImageCGForm",
                "MapMiniMapTerrainImageForm", "WorldMapImageForm",
                // songs / sound (recycle, embedded inst)
                // (SoundRoomCGForm [FE7, clean u32-FFFFFFFF table], SoundRoomFE6Form [FE6, clean],
                //  and WorldMapBGMForm [FE8, clean] ported in this sweep. SoundFootStepsForm ported in
                //  slice 2d — Switch2-gated dedicated walker (base/count from sound_foot_steps_switch2_address,
                //  IsSwitch2Enable gate) + per-entry ASM AddFunction. SoundRoomForm STAYS — its FE7
                //  path adds a per-entry getString C-string sub-walk + InputFormRef_MIX type.)
                "SongTableForm", "SoundRoomForm",
                // embedded sub-pointer / event-scan / CString expansion
                // (ClassForm + StatusParamForm ported in slice 2c — per-entry MoveCost / CString
                //  sub-walks. ItemForm STAYS: its StatBooster sub-block SIZE depends on un-ported
                //  PatchUtil patch detection (SearchGrowsMod / ItemUsingExtendsPatch -> 12/16/20)
                //  and the ItemEffectiveness rework variant on SearchClassType — a wrong size would
                //  relocate the wrong bytes, so it must wait until those detectors reach Core.)
                "ItemForm",
                // The other slice-2c embedded forms also stay — their sub-block LENGTH needs a
                // subsystem not yet in Core (a wrong length relocates the wrong bytes = corruption):
                //   ItemWeaponEffectForm— PROCS sub-block length = ProcsScriptForm.CalcLengthAndCheck (disasm).
                // (StatusRMenuForm [recursive 28-byte MIX tree, shared visited-set + 2 ASM AddFunction]
                //  and MenuDefinitionForm [recursive MenuCommandForm sub-table — InputFormRef_MIX + per-entry
                //  CString + 6 ASM ptrs — + its own 6 ASM ptrs] ported in slice 2d via dedicated recursive
                //  walkers; MenuCommandForm.MakeAllDataLengthP is reproduced by EmitMenuCommandSubTable.)
                "ItemShopForm",
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
                // (StatusUnitsMenuForm + LinkArenaDenyUnitForm ported in slice 2b.
                //  StatusOptionOrderForm [v7+v8, count-address fixed table] ported in this sweep.
                //  StatusOptionForm ported in slice 2d [v7+v8, PointerAt@40 main IFR + per-entry
                //  SubKind.AsmFunction @40]. NOTE: "MapMiniMapTerrainForm" does NOT exist as a distinct
                //  ASM form — the only file is MapMiniMapTerrainImageForm (an LZ77 image form, already
                //  listed above), so there is nothing extra to port for it.)
                "MantAnimationForm", "MapTileAnimation1Form",
                "MapTileAnimation2Form", "MapTerrainFloorLookupTableForm",
                "MapTerrainBGLookupTableForm",
                // units / classes per-version with extra reads
                // (UnitForm [FE8, flat + 24-byte support BinFixed sub-walk @44] and UnitFE7Form
                //  [FE7, flat, no sub-walk] ported in this sweep; SummonUnitForm + SummonsDemonKingForm
                //  + EventForceSortieForm [FE8 clean tables] ported too. UnitFE6Form STAYS — its base is
                //  p32(unit_pointer)+unit_datasize via a direct ReInit, which the pointer-slot descriptor
                //  model cannot express faithfully.)
                "UnitFE6Form", "UnitCustomBattleAnimeForm",
                "ExtraUnitForm", "ExtraUnitFE8UForm",
                "EventUnitForm(RecycleReserveUnits)",
                // monster / world map / ED / support (FE8/FE7/FE6 variants)
                // (MonsterItemForm + MonsterProbabilityForm ported in slice 2b.
                //  This sweep ported the CLEAN per-version tables: EDForm [FE8 ×4], EDFE7Form [FE7 ×5],
                //  EDFE6Form [FE6], EDSensekiCommentForm [v6+v7], CCBranchForm + OPClassAlphaNameForm
                //  [FE8, count = ClassDataCount] + OPClassAlphaNameFE6Form [FE6 string-BIN sub-walk],
                //  WorldMapPointForm [FE8], SupportTalkForm/FE6/FE7, EventBattleTalkFE6Form [FE6 ×2],
                //  EventHaikuFE6Form [FE6], TacticianAffinityFE7, EventFinalSerifFE7Form.
                //  STILL deferred (event-scan / LZ77 / recursive / GetUnitIDWhereSupportAddr / dynamic
                //  base): MonsterWMapProbabilityForm + WorldMapEventPointerForm [ScanScript],
                //  EventBattleTalkForm + EventHaikuForm [FE8, ScanScript per-entry], SupportUnitForm
                //  [GetUnitIDWhereSupportAddr], WorldMapPathForm [CalcPathDataLength], EDStaffRollForm +
                //  OPPrologueForm + OPClassFontForm + OPClassDemoForm/FE7Form [LZ77], MapSettingForm
                //  [IsMapSettingEnd + CString], FE8SpellMenuExtendsForm [FindFE8SpellPatchPointer],
                //  ImageCGFE7UForm [LZ77].)
                "MonsterWMapProbabilityForm",
                "EventBattleTalkForm",
                "WorldMapPathForm", "WorldMapEventPointerForm", "EDStaffRollForm",
                "OPPrologueForm", "EventHaikuForm",
                "SupportUnitForm", "MapSettingForm",
                "OPClassFontForm", "OPClassDemoForm", "FE8SpellMenuExtendsForm",
                "OPClassDemoFE7Form", "ImageCGFE7UForm",
                // patch / procs / ASM (AppendAllASMStructPointersList)
                "PatchForm(MakePatchStructDataList)", "ProcsScriptForm",
            };
    }
}
