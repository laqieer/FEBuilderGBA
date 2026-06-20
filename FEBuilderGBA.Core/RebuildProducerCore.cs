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
            /// <summary><c>ImageUnitMoveIconFrom.Init</c> rule: the table is class-count-bounded. The cap
            /// is <c>classCount = ClassForm.DataCount()</c> with ONLY the extremes coerced
            /// (<c>&lt;=0 -&gt; 0x7f</c>, <c>&gt;0xff -&gt; 0xff</c>; values 1..0xff are left UNCHANGED — this is
            /// NOT a clamp to <c>[0x7f, 0xff]</c>), then decremented; the rule is <c>i &gt;= classCount ?
            /// false : i == 0 ? true : U.isPointerOrNULL(u32(addr+0))</c> (entry 0 always exists; afterward
            /// the <c>+0</c> image pointer must be a ROM pointer or NULL). Reproduced VERBATIM; the cap is
            /// computed via <see cref="ClassDataCount"/> (the Core port of <c>ClassForm.DataCount()</c>).
            /// <see cref="StructDescriptor.RuleOffset"/> selects the checked field (MoveIcon uses 0).</summary>
            MoveIconRule,
            /// <summary><c>ImageCGForm.Init</c> rule (the 10-split big-CG table): continue while the entry's
            /// <c>+0</c> field is a ROM pointer AND the FIRST u32 at its target is ALSO a ROM pointer
            /// (<c>p = u32(addr+0); isPointer(p) &amp;&amp; isSafetyPointer(p); p2 = u32(toOffset(p));
            /// isPointer(p2) &amp;&amp; isSafetyPointer(p2)</c>). The double-indirection is how WF
            /// distinguishes the 10-image-pointer-array CG entries from stray pointers. EOF-hardened on the
            /// inner <c>u32(toOffset(p))</c> read. <see cref="StructDescriptor.RuleOffset"/> selects the
            /// entry field (ImageCGForm uses 0).</summary>
            NestedPointerAt,
            /// <summary><c>ImageCGFE7UForm.Init</c> rule: continue while <c>u16(addr + RuleOffset) ==
            /// <see cref="StructDescriptor.RuleStopValue"/></c> (NOT "!="). Distinct from
            /// <see cref="U16NotEqual"/> (which CONTINUES while the value differs); this CONTINUES while the
            /// value MATCHES (ImageCGFE7U: <c>u16(addr+2) == 0</c> — entries are 16-byte-aligned so the
            /// padding u16 at +2 stays 0 within the table).</summary>
            U16EqualAt,
            /// <summary><c>ImageItemIconForm.Init</c> rule: continue while <c>i &lt;= itemMax</c>, where
            /// <c>itemMax = GetIconMax(rom)</c> (the item-icon SHEET count). <c>GetIconMax</c> is a pure-ROM
            /// inspection reproduced VERBATIM in <see cref="GetIconMax"/> (a repoint check via
            /// <c>p32(icon_pointer) != icon_orignal_address</c> -&gt; 0xFE; an FE7U-specific FEditorAdv
            /// AutoPatch probe at the hardcoded magic addr <c>u32(0xCB51A) == 0x18404902</c> -&gt;
            /// <c>icon_orignal_max - 1</c>; else <c>icon_orignal_max</c>). The <c>&lt;=</c> means count =
            /// itemMax + 1. EOF-hardened on the two raw reads (<c>p32(icon_pointer)</c> /
            /// <c>u32(0xCB51A)</c>).</summary>
            ItemIconMaxRule,
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
            /// <summary>A NESTED count-walked IFR sub-table behind an embedded pointer. Reproduces the
            /// WinForms <c>N_IFR.ReInitPointer(p + EmbeddedPointerOffset) + AddAddress(N_IFR, name, {})</c>
            /// idiom: resolves <c>subBase = p32(p + EmbeddedPointerOffset)</c>, walks it with
            /// <see cref="SubWalk.SubBlockSize"/> and <see cref="SubWalk.SubRule"/> via
            /// <c>getBlockDataCount</c>, and emits ONE IFR <see cref="Address"/> whose length is
            /// <c>SubBlockSize × (subCount + 1)</c>, whose pointer is the embedded FIELD (the
            /// <c>p + EmbeddedPointerOffset</c>, or <see cref="U.NOT_FOUND"/> if that is not a safe
            /// offset — matching <c>AddressWinForms.AddAddress</c>'s <c>BasePointer</c> fallback), whose
            /// type is <see cref="Address.DataTypeEnum.InputFormRef"/>, whose blockSize is
            /// <see cref="SubWalk.SubBlockSize"/>, and whose pointerIndexes are EMPTY (the WinForms
            /// <c>new uint[] {}</c>). Unlike every other <see cref="SubKind"/> (which emits a flat block of
            /// a known length), this one runs its OWN inner <c>getBlockDataCount</c>. Used by the
            /// OPClassDemo forms (N1/N2 sub-tables); EOF-safe (the embedded-pointer read and the inner
            /// walk are both bounds-guarded, never throw). IS handled by the flat <see cref="SubWalk"/>
            /// loop in <see cref="EmitSubWalks"/> (available to any future single-nested-sub-table form);
            /// the OPClassDemo forms, however, have form-specific per-entry guard ordering, so they call
            /// <see cref="EmitNestedIfrSub"/> directly from a dedicated emitter rather than the flat loop.</summary>
            NestedIfr,
            /// <summary>An embedded HEADER-TSA pointer (a 2-byte <c>{x,y}</c> master-header stream behind a
            /// pointer). Emits via <see cref="EmitHeaderTsaPointer"/> VERBATIM (reads <c>u32(p +
            /// EmbeddedPointerOffset)</c>, <c>isSafetyPointer</c>-checks, then adds a
            /// <see cref="Address.DataTypeEnum.HEADERTSA"/> block whose length is
            /// <see cref="CalcHeaderTsaLength"/> over the dereferenced target). Matches the per-entry
            /// <c>AddHeaderTSAPointer(list, p + N, name, isPointerOnly)</c> in the header-TSA image forms
            /// (ImageBGForm / ImageCGForm / ImageCGFE7UForm). The label is taken from
            /// <see cref="SubWalk.Name"/>; <see cref="SubWalk.FixedLength"/> / <see cref="SubWalk.DataType"/>
            /// are unused (the type is fixed HEADERTSA, the length is computed). EOF-safe (the dereference is
            /// full-extent-guarded; the length calc rejects a header that does not fit).</summary>
            HeaderTsaPointer,
            /// <summary>An embedded AP (Animated-Parts) pointer (the unit move-icon animation stream behind a
            /// pointer). Emits via <see cref="EmitApPointer"/> VERBATIM (reproduces WF
            /// <c>AddressWinForms.AddAPPointer</c>: reads <c>u32(p + EmbeddedPointerOffset)</c>,
            /// <c>isSafetyPointer</c>-checks, then adds a <see cref="Address.DataTypeEnum.AP"/> block whose
            /// length is <see cref="ImageUtilAPCore.CalcAPLength"/> — the AP frame/anime stream parser —
            /// over the dereferenced target). Matches the per-entry <c>AddAPPointer(list, p + 4, name + " AP",
            /// isPointerOnly)</c> in <c>ImageUnitMoveIconFrom</c>. The label is taken from
            /// <see cref="SubWalk.Name"/>; <see cref="SubWalk.FixedLength"/> / <see cref="SubWalk.DataType"/>
            /// are unused (the type is fixed AP, the length is computed). EOF-safe (the dereference is
            /// full-extent-guarded; <see cref="ImageUtilAPCore.CalcAPLength"/> returns 0 on a malformed /
            /// near-EOF stream, never throws).</summary>
            ApPointer,
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
            /// <summary>For <see cref="SubKind.NestedIfr"/>: the nested sub-table's block size (the
            /// WinForms <c>N_IFR.Init</c> BlockSize, e.g. OPClassDemo N1 = 1, N2 = 2). Drives both the
            /// inner <c>getBlockDataCount</c> stride and the emitted length <c>SubBlockSize × (count + 1)</c>.
            /// Ignored by the non-nested kinds.</summary>
            public uint SubBlockSize;
            /// <summary>For <see cref="SubKind.NestedIfr"/>: the nested sub-table's <c>IsDataExists</c>
            /// callback (the WinForms <c>N_IFR.Init</c> rule lambda), reproduced VERBATIM as a
            /// <c>(i, addr) =&gt; bool</c> over the SUB-table (e.g. OPClassDemo N1 =
            /// <c>i &gt;= 16 ? false : u8(addr) != 0xFF</c>, N2 = <c>u8(addr) != 0</c>). The inner
            /// <c>getBlockDataCount</c> only fires this while <c>addr + SubBlockSize &lt;= Length</c>, so
            /// a read of width &lt;= <see cref="SubBlockSize"/> at <c>addr</c> is always in bounds.
            /// Ignored by the non-nested kinds.</summary>
            public Func<int, uint, bool> SubRule;
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
            // WorldMapImageFE6Form / WorldMapImageFE7Form are version-gated (the FE8 WorldMapImageForm is
            // ported in slice 2k — EmitWorldMapImageFE8, now that CalcRomTcsLength is in Core).
            // Each gets its own cancel-check, mirroring the WF DoEvents checkpoints.
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageBattleScreen");
            EmitImageBattleScreen(rom, list);

            // ---- slice 2k: header-TSA image forms (version-agnostic call site) ----
            // ImageBGForm (BG256-aware per-entry HEADER-TSA branch) and ImageSystemIconForm (flat
            // version-gated LZ77/PAL/HEADER-TSA sequence) are both called in the WF unconditional section.
            // The FE8/FE7-multibyte ImageCGForm, FE7U ImageCGFE7UForm, and FE8 WorldMapImageForm are
            // version-gated and wired into their respective version branches below. Each gets its own
            // cancel-check, mirroring the WF DoEvents posture. (The config-FILE-table TSA-anime forms —
            // ImageTSAAnime2Form / ImageTSAAnimeForm, g_TSAAnime from U.LoadTSVResource("tsaanime2_"/
            // "tsaanime_") — are ported in slice 2q and wired into their version branches below.)
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageBG");
            EmitImageBG(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageSystemIcon");
            EmitImageSystemIcon(rom, list);

            // ---- slice 2f: ItemUsagePointerForm (version-agnostic) ----
            // ItemUsagePointerForm.MakeAllDataLength is NOT a flat descriptor walk: it loops the 10
            // usage tables, each Switch2-gated with its base/count read from a RomInfo *address* (the
            // count is u8(switch2_address+2), the base is p32(usage_pointer)), then emits a main
            // InputFormRef_ASM Address + one ASM AddFunction per entry. A dedicated walker reproduces it
            // verbatim (its own cancel-check mirrors the WF DoEvents posture).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ItemUsagePointer");
            EmitItemUsagePointer(rom, list);

            // ---- slice 2g: per-map PLIST forms (version-agnostic) ----
            // ItemShop / MapChange / MapExitPoint / MapTileAnimation1 / MapTileAnimation2 are all
            // version-agnostic in WF MakeAllStructPointersList. None is a flat StructDescriptor walk:
            // each is a per-map enumeration (MakeMapIDList / event-cond shop scan / PLIST resolve) with
            // verbatim per-entry length reproduction. Each gets a dedicated walker with its own
            // cancel-check, mirroring the WF DoEvents posture.
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ItemShop");
            EmitItemShop(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapChange");
            EmitMapChange(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapExitPoint");
            EmitMapExitPoint(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapTileAnimation1");
            EmitMapTileAnimation1(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapTileAnimation2");
            EmitMapTileAnimation2(rom, list);

            // ---- slice 2j: MapTerrain Floor/BG lookup tables + MapPointer (version-agnostic) ----
            // All three are called in the WF unconditional section. None is a flat StructDescriptor walk:
            // the MapTerrain forms loop a per-form pointer array (MapTerrainLookupCore.GetPointers — the
            // vanilla 21-slot RomInfo list OR the extends-patch table) emitting one flat IFR per non-zero
            // pointer with the index baked into the name; MapPointer emits 6-7 MAPPOINTERS IFR tables plus a
            // per-map PLIST sweep (MapPListResolverCore.GetMapPListsWhereAddr). Each gets a dedicated walker
            // with its own cancel-check, mirroring the WF DoEvents posture. WF order: Floor THEN BG.
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapTerrainFloorLookupTable");
            EmitMapTerrainLookup(rom, list, isFloor: true);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapTerrainBGLookupTable");
            EmitMapTerrainLookup(rom, list, isFloor: false);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("MapPointer");
            EmitMapPointer(rom, list);

            // ---- slice 2l: ItemWeaponEffect (version-agnostic) ----
            // ItemWeaponEffectForm.MakeAllDataLength is in the WF unconditional section (NOT
            // version/multibyte-gated). It is NOT a flat StructDescriptor walk: its main IFR (base
            // p32(item_effect_pointer), block 16, IsDataExists = u16(addr)==0xFFFF stop / i>10 &&
            // IsEmpty(addr,16*10) stop — both pure ROM reads) is followed by a per-entry PROCS
            // sub-block behind the embedded pointer at +8, whose length is a PROCS-bytecode
            // terminator walk (ProcsScriptForm.CalcLengthAndCheck — pure u16/u32/getString reads,
            // reproduced VERBATIM as CalcProcsLengthAndCheck). A dedicated emitter reproduces it
            // (its own cancel-check mirrors the WF DoEvents posture).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ItemWeaponEffect");
            EmitItemWeaponEffect(rom, list);

            // ---- slice 2s: AIScript (AI1 / AI2 bytecode tables; version-agnostic) ----
            // AIScriptForm.MakeAllDataLength is in the WF unconditional section (NOT version-gated). Its
            // per-entry length walks (CalcAIScriptLength / CalcAIUnitsLength) are pure-ROM terminator
            // scans, and its main-IFR DataCount caps an un-extended table at the ai{1,2}_ config-line
            // count (CountConfigDataLines — the same IsComment/OtherLangLine filter PreLoadResourceAI{1,2}
            // uses, degrading to 0 when the config tree is absent). The ONLY divergence is the per-entry
            // NAME (WF's GetAIName1/2 -> a config name list OR InputFormRef.GetCommentSA, neither in Core):
            // the producer uses a STATIC name, which is relocation-identical (the established
            // ItemWeaponEffect precedent). A dedicated emitter reproduces it (cancel-check mirrors the WF
            // DoEvents posture).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("AIScript");
            EmitAIScript(rom, list);

            // ---- slice 2m: TextForm (Huffman text; version-agnostic) ----
            // TextForm.MakeAllDataLength is in the WF unconditional section (NOT version-gated). It is NOT
            // a flat StructDescriptor walk: its main IFR has a multi-branch IsDataExists (isPointer ||
            // IsUnHuffmanPatchPointer || Is_RAMPointerArea) with a text_recover_address ReInit fallback,
            // and each entry's BIN length is a Huffman/UnHuffman decode (FETextDecode.huffman_decode /
            // UnHffmanPatchDecode — both headless Core methods, called directly, internally EOF-hardened).
            // A dedicated emitter reproduces it; the dispatch below cancel-checks BEFORE calling it
            // (mirroring the WF DoEvents posture — EmitText itself takes no token / has no inner check).
            // TextCharCodeForm is a flat U8NotEqual descriptor in BuildBatchDescriptors (version-agnostic).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("Text");
            EmitText(rom, list);

            // ---- slice 2r: SongTableForm (songs + instruments; version-agnostic call site) ----
            // SongTableForm.MakeAllDataLength is in the WF unconditional section (NOT version-gated). It is
            // NOT a flat StructDescriptor walk: its base is GetSoundTablePointer (RomInfo slot OR a
            // signature scan that returns the SLOT, reproduced verbatim), its main IFR is block 8 /
            // isPointer(u32(addr)) / {0}, and each entry expands the SONG score
            // (EmitRecycleOldSong = SongUtil.RecycleOldSong: a SONGTRACK header + per-track SONGSCORE blocks
            // sized from SongMidiCore.ParseTracks' FINE-terminated walk) AND the recursive instrument tree
            // (EmitRecycleOldInstrument = SongInstrumentForm.RecycleOldInstrument: a block-12 IFR + per-type
            // DirectSound/Wave/Drum/Multi blocks with a shared visited-list dedup). All pure-ROM walks
            // (SongDirectSoundWavCore lengths + ParseTracks), NO Drawing / MIDI-import. A dedicated emitter
            // reproduces it; the dispatch cancel-checks BEFORE calling it (WF DoEvents posture).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("SongTable");
            EmitSongTable(rom, list);

            // ---- slice 2p: OAM / battle-anime length forms (version-agnostic call site) ----
            // ImageMapActionAnimationForm and the two ImageUtilMagic forms (FEditor / CSA) are all in
            // the WF unconditional section, each internally gated (FindAnimationPointer for MapAction;
            // ImageUtilMagicCore.SearchMagicSystem FEDITOR_ADV / CSA_CREATOR for the Magic pair), so a
            // ROM without the relevant patch emits nothing. None is a flat StructDescriptor walk: each
            // expands every table entry via a verbatim ImageUtil*.RecycleOldAnime length walk (a pure-ROM
            // anime-stream terminator scan; the OAM column length is the verbatim CalcMagicOamLength port
            // of WF ImageUtilMagicFEditor.calcOAMLength). Each gets a dedicated emitter with its own
            // cancel-check, mirroring the WF DoEvents posture. The RecycleOldAnime-dependent SkillConfig
            // siblings STAY deferred (see GetNotYetPortedForms). ImageBattleAnimeForm is PORTED in slice 2s
            // (EmitImageBattleAnime — wired just below).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageMapActionAnimation");
            EmitImageMapActionAnimation(rom, list);

            // ---- slice 2s: ImageBattleAnime (battle-anime OAM tables; version-agnostic) ----
            // ImageBattleAnimeForm.MakeAllDataLength is in the WF unconditional section (NOT version-gated;
            // the per-class GetBattleAnimeAddrWhereAddr is version-gated internally — +48 FE6 / +52 FE7+FE8).
            // Three parts: the per-class "BattleAnimeSeting" IFRs (ClassForm.MakeClassList reproduced via
            // MakeClassListAddrs), the N_ "BattleAnime" animelist IFR (FEditorHint via CoreState.Config), and
            // the per-anime OAM walk (EmitImageBattleAnimeOAM = ImageUtilOAM.MakeAllDataLength — section BIN +
            // 4 LZ77 pointers + the UnCompressFrame-embedded seat-image sub-walk, dedup'd across entries via a
            // shared seatNumberList). All deps are Core (UnCompressFrame = FETextEncode/LZ77/getBinaryData +
            // CalcUnCompressFrameLength; AddLZ77Pointer/Address). The ONLY divergence is the per-anime OAM
            // `info` (WF decodes the anime name via getString — relocation-irrelevant; the producer uses a
            // static name). A dedicated emitter reproduces it (cancel-check mirrors the WF DoEvents posture).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageBattleAnime");
            EmitImageBattleAnime(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageMagicFEditor");
            EmitImageMagicFEditor(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageMagicCSACreator");
            EmitImageMagicCsaCreator(rom, list);

            // ---- slice 2q: config-FILE-table forms (version-agnostic call site) ----
            // OtherTextForm and ImageRomAnimeForm are both in the WF unconditional section (NOT
            // version-gated). Neither is a flat StructDescriptor walk: each loads a config TSV
            // (other_text_ / romanime_) via U.ConfigDataFilename / U.LoadTSVResource (now in Core,
            // resolving against CoreState.BaseDirectory) — a config-FILE table, not a RomInfo slot —
            // and emits per-entry blocks whose lengths are all Core-backed (getString / LZ77
            // getCompressedSize / pure-ROM frame & pointer-list terminator walks; NO ImageUtilOAM /
            // Drawing / disasm). A missing config tree -> empty table -> nothing emitted (faithful to
            // WF's empty-dict headless behavior). Each gets its own cancel-check (WF DoEvents posture).
            // The version-gated ImageTSAAnimeForm (v8 + v7) / ImageTSAAnime2Form (v8) are wired into
            // their respective version branches below.
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("OtherText");
            EmitOtherText(rom, list);

            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("ImageRomAnime");
            EmitImageRomAnime(rom, list);

            // ---- slice 2h: SupportUnit (all versions) + WorldMapPath (FE8-only) ----
            // SupportUnitForm is called in the WF version==8 AND version==7 branches (block 24); the
            // SupportUnitFE6Form variant in version==6 (block 32). EmitSupportUnit auto-selects the
            // per-version block/first-field/name. Its count rule needs the owner-lookahead
            // (UnitForm.GetUnitIDWhereSupportAddr), reproduced via SupportUnitNavigation, so it is a
            // dedicated emitter rather than a flat descriptor. WorldMapPathForm is version==8-only and
            // has per-entry computed-length sub-blocks (CalcPath{,Move}DataLength, pure ROM walks).
            if (ct.IsCancellationRequested)
            {
                progress?.Report("MakeAllStructPointersList cancelled");
                return new ProducerResult(list, notYet, cancelled: true);
            }
            progress?.Report("SupportUnit");
            EmitSupportUnit(rom, list);

            // ---- slice 2o: SkillSystems skill-config / skill-assignment forms ----
            // WF (U.MakeAllStructPointersList) runs these in the version-agnostic section, gated on
            // is_multibyte: `is_multibyte == false` (FE8U) emits the two skill-ASSIGNMENT tables (Class +
            // Unit); `else` (FE8J) emits SkillConfigFE8N. The RecycleOldAnime-dependent siblings
            // (SkillConfigSkillSystemForm on FE8U; FE8NVer2/FE8NVer3 on FE8J) STAY deferred (anime length
            // walker + GUI state not in Core). Each emitter ALSO re-checks SearchSkillSystem (mirroring the
            // WF MakeAllDataLength early-return), so a non-SkillSystem ROM emits nothing. Gate EXACTLY as
            // WF: a wrong is_multibyte branch would emit the wrong tables = corruption.
            if (rom.RomInfo.is_multibyte == false)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("SkillAssignmentClass");
                EmitSkillAssignmentClass(rom, list);

                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("SkillAssignmentUnit");
                EmitSkillAssignmentUnit(rom, list);
            }
            else
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("SkillConfigFE8N");
                EmitSkillConfigFE8N(rom, list);
            }

            if (rom.RomInfo.version == 8)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("WorldMapPath");
                EmitWorldMapPath(rom, list);

                // ---- slice 2i: OPClassDemo (FE8-multibyte ONLY) ----
                // WF calls OPClassDemoForm.MakeAllDataLength inside `version==8 && is_multibyte` (the
                // FE8U non-multibyte path uses OPClassDemoFE8UForm, still deferred). Per-entry nested
                // N1/N2 IFR sub-tables (block 1/2) reached via embedded pointers @ +8/+24 — a dedicated
                // emitter (EmitOPClassDemo) reproduces the form-specific dual-guard ordering.
                if (rom.RomInfo.is_multibyte)
                {
                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("OPClassDemo");
                    EmitOPClassDemo(rom, list);
                }

                // ---- slice 2j: ExtraUnit (FE8-only; multibyte/non-multibyte split) ----
                // WF calls ExtraUnitForm (FE8J) inside `version==8 && is_multibyte` and ExtraUnitFE8UForm
                // (FE8U) inside `version==8 && !is_multibyte` — a version/multibyte split (the FE8J path is
                // an if-chain at hardcoded 0x37EE4 / flags @ i*0x14+0x37E10; the FE8U path is a table at
                // 0x37D88, block 8). Both expand each entry via EventUnitForm.RecycleOldUnits (reproduced
                // by EmitRecycleOldUnits — the EventUnit IFR + the FE8 per-entry COORD sub-blocks). Gate
                // EXACTLY as WF: a wrong-shape port on the wrong FE8 variant corrupts (cf. the #1274 FE6
                // bug). is_multibyte == FE8J, !is_multibyte == FE8U.
                if (rom.RomInfo.is_multibyte)
                {
                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("ExtraUnit");
                    EmitExtraUnit(rom, list);
                }
                else
                {
                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("ExtraUnitFE8U");
                    EmitExtraUnitFE8U(rom, list);

                    // ---- slice 2m: FE8SpellMenuExtends (FE8U ONLY) ----
                    // WF calls FE8SpellMenuExtendsForm.MakeAllDataLength inside `version==8 &&
                    // !is_multibyte` (alongside OPClassDemoFE8U/ExtraUnitFE8U). Its base is resolved by a
                    // patch-signature scan (FE8SpellMenuPatchScanner — the Core port of WF
                    // FindFE8SpellPatchPointer; both OldSystem .dmp grep + hard-coded SkillSystems202201
                    // signature, version/multibyte-gated, NOT_FOUND on a non-patched ROM). Per entry it
                    // emits a NestedIfr (the slice-2i primitive). Gate EXACTLY as WF.
                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("FE8SpellMenuExtends");
                    EmitFE8SpellMenuExtends(rom, list);
                }

                // ---- slice 2t: ImagePortraitForm (FE8 + FE7 call sites) ----
                // WF calls ImagePortraitForm.MakeAllDataLength in BOTH the version==8 and version==7 branches
                // (the version==6 branch uses ImagePortraitFE6Form — wired into the v6 branch below). The main
                // IFR "Portrait" {0,4,8,12,16} + the per-entry RecyclePortrait (FACE LZ77/IMG/HALFBODY by
                // header byte, MAP FACE / PAL / MOUTH / CLASS CARD) — IsHalfBodyFlag is a pure-ROM
                // u32(seet)==0x00200400 inspection. Same emitter both versions (the FE8-only halfbody branch
                // is internally version-gated). Its own cancel-check mirrors the WF DoEvents posture.
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImagePortrait");
                EmitImagePortrait(rom, list);

                // ---- slice 2k: WorldMapImageForm + ImageCGForm (FE8) ----
                // WF (version==8) calls WorldMapImageForm.MakeAllDataLength and ImageCGForm.MakeAllDataLength
                // UNCONDITIONALLY within the version==8 branch (NOT multibyte-gated). EmitWorldMapImageFE8 is
                // the FE8 big-map/event/mini/icon/border(ROMTCS)/icondata sequence; EmitImageCG is the
                // 12-byte big-CG IFR with the per-entry 10-image-pointer array + HEADER-TSA.
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("WorldMapImageFE8");
                EmitWorldMapImageFE8(rom, list);

                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImageCG");
                EmitImageCG(rom, list);

                // ---- slice 2q: ImageTSAAnime2Form (FE8 ONLY) + ImageTSAAnimeForm (FE8 + FE7) ----
                // WF calls ImageTSAAnime2Form.MakeAllDataLength ONLY in the version==8 branch (its
                // g_TSAAnime / tsaanime2_ config exists only for FE8); ImageTSAAnimeForm.MakeAllDataLength
                // is called in BOTH the version==8 AND version==7 branches (NOT version==6). Both load a
                // config TSV (tsaanime2_ / tsaanime_) and emit an IFR + LZ77/PAL/HEADER-TSA columns —
                // all Core-backed lengths. EmitImageTSAAnime is wired into the version==7 branch below too.
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImageTSAAnime2");
                EmitImageTSAAnime2(rom, list);

                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImageTSAAnime");
                EmitImageTSAAnime(rom, list);

                // ---- slice 2r: SoundRoomForm (FE8 path) + MapSettingForm (FE8 only) ----
                // WF calls SoundRoomForm.MakeAllDataLength in BOTH the version==8 and version==7 branches;
                // its FE8 path has NO per-entry sub-walk (the C-string name BIN is FE7-only). MapSettingForm
                // is version==8-only; its per-entry CSTRING name + the IsMapSettingEnd count rule (which
                // needs the cached TextForm.GetDataCount, now reproduced byte-faithfully by TextDataCount)
                // are both Core-backed. Each gets its own cancel-check (WF DoEvents posture).
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("SoundRoom");
                EmitSoundRoom(rom, list);

                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("MapSetting");
                EmitMapSetting(rom, list);
            }

            if (rom.RomInfo.version == 7)
            {
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("WorldMapImageFE7");
                EmitWorldMapImageFE7(rom, list);

                // ---- slice 2t: ImagePortraitForm (FE7 path) ----
                // WF calls ImagePortraitForm.MakeAllDataLength in the version==7 branch too (same emitter as
                // the v8 path; the halfbody FACE branch is internally gated on version==8 so it never fires
                // on FE7). Its own cancel-check mirrors the WF DoEvents posture.
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImagePortrait");
                EmitImagePortrait(rom, list);

                // ---- slice 2r: SoundRoomForm (FE7 path) ----
                // WF calls SoundRoomForm.MakeAllDataLength in the version==7 branch too; the FE7 path ADDS
                // the per-entry C-string song-name BIN (length == strlen, NO +1) behind the +12 embedded
                // pointer (EmitSoundRoom selects it by version). MapSettingForm is NOT in the FE7 branch
                // (the FE7-multibyte MapSettingFE7Form variant stays deferred — different layout).
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("SoundRoom");
                EmitSoundRoom(rom, list);

                // ---- slice 2q: ImageTSAAnimeForm (FE8 + FE7) ----
                // WF calls ImageTSAAnimeForm.MakeAllDataLength in the version==7 branch too (NOT FE6).
                // Same emitter as the version==8 path (tsaanime_ config + IFR + LZ77/PAL/LZ77TSA columns).
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImageTSAAnime");
                EmitImageTSAAnime(rom, list);

                // ---- slice 2i: OPClassDemoFE7 (FE7-multibyte ONLY) ----
                // WF calls OPClassDemoFE7Form.MakeAllDataLength inside `version==7 && is_multibyte` (the
                // FE7U non-multibyte path uses OPClassDemoFE7UForm, still deferred). ONE nested N2 IFR
                // sub-table @ +28 (block 2), an LZ77 column @ +8, and a trailing absolute common-palette
                // pointer — a dedicated emitter (EmitOPClassDemoFE7) reproduces the shape verbatim.
                // ---- slice 2k: ImageCGForm (FE7-multibyte) / ImageCGFE7UForm (FE7U non-multibyte) ----
                // WF (version==7) calls ImageCGForm.MakeAllDataLength inside `is_multibyte` and
                // ImageCGFE7UForm.MakeAllDataLength inside `!is_multibyte` — a version/multibyte split (the
                // two forms differ in block size + per-entry shape: ImageCG = block 12, NestedPointer rule,
                // always-10-split; ImageCGFE7U = block 16, u16(+2)==0 rule, per-entry flag@+0 16-color-vs-
                // 10-split). Gate EXACTLY as WF (a wrong-shape port corrupts; cf. the #1274 FE6 bug).
                if (rom.RomInfo.is_multibyte)
                {
                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("OPClassDemoFE7");
                    EmitOPClassDemoFE7(rom, list);

                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("ImageCG");
                    EmitImageCG(rom, list);
                }
                else
                {
                    if (ct.IsCancellationRequested)
                    {
                        progress?.Report("MakeAllStructPointersList cancelled");
                        return new ProducerResult(list, notYet, cancelled: true);
                    }
                    progress?.Report("ImageCGFE7U");
                    EmitImageCGFE7U(rom, list);
                }
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

                // ---- slice 2f: UnitFE6Form (FE6-only) ----
                // UnitFE6Form.MakeAllDataLength is FE6-only (the WF version==6 branch). Its IFR base is
                // p32(unit_pointer)+unit_datasize (one block past the table start — class 0 is skipped via
                // a direct ReInit), and its BasePointer is 0 (-> NOT_FOUND). The pointer-slot descriptor
                // model assumes baseAddr = p32(PointerField), so this needs a dedicated emitter.
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("UnitFE6");
                EmitUnitFE6(rom, list);

                // ---- slice 2t: ImagePortraitFE6Form (FE6 path) ----
                // WF calls ImagePortraitFE6Form.MakeAllDataLength in the version==6 branch (a distinct, simpler
                // form: null-run cap 10, no halfbody, FACE LZ77 / MAP FACE IMG@+4 / PAL@+8). Its own
                // cancel-check mirrors the WF DoEvents posture.
                if (ct.IsCancellationRequested)
                {
                    progress?.Report("MakeAllStructPointersList cancelled");
                    return new ProducerResult(list, notYet, cancelled: true);
                }
                progress?.Report("ImagePortraitFE6");
                EmitImagePortraitFE6(rom, list);
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

                        case SubKind.NestedIfr:
                            // A nested count-walked IFR sub-table behind the embedded pointer
                            // (N_IFR.ReInitPointer(pfield) + AddAddress(N_IFR, name, {})). EmitNestedIfrSub
                            // resolves p32(pfield), walks it with SubBlockSize/SubRule, and emits one IFR
                            // Address (length = SubBlockSize*(count+1), pointer = pfield, pointerIndexes {}).
                            // EOF-safe; the OPClassDemo forms drive this from a dedicated emitter (their
                            // per-entry guard ordering differs from this flat loop), but it is available to
                            // the flat SubWalk loop for any future single-nested-sub-table form.
                            EmitNestedIfrSub(rom, list, pfield, sw.SubBlockSize, sw.SubRule,
                                sw.Name != null ? sw.Name(rom, i) : d.Name);
                            break;

                        case SubKind.HeaderTsaPointer:
                            // The header-TSA image forms' per-entry TSA column:
                            // Address.AddHeaderTSAPointer(list, p + N, name, isPointerOnly). Reads
                            // u32(pfield), isSafetyPointer-checks, then adds a HEADERTSA block whose length
                            // is CalcHeaderTsaLength over the dereferenced target. No encoder needed (no
                            // string decode). EOF-safe (full-extent-guarded dereference).
                            EmitHeaderTsaPointer(rom, list, pfield,
                                sw.Name != null ? sw.Name(rom, i) : d.Name);
                            break;

                        case SubKind.ApPointer:
                            // ImageUnitMoveIconFrom per-entry AP column:
                            // AddressWinForms.AddAPPointer(list, p + 4, name + " AP", isPointerOnly).
                            // Reads u32(pfield), isSafetyPointer-checks, then adds an AP block whose length
                            // is ImageUtilAPCore.CalcAPLength (the AP frame/anime stream parser) over the
                            // dereferenced target. No encoder needed (no string decode). EOF-safe (the
                            // dereference is full-extent-guarded; CalcAPLength returns 0 on a malformed /
                            // near-EOF stream — never throws).
                            EmitApPointer(rom, list, pfield,
                                sw.Name != null ? sw.Name(rom, i) : d.Name);
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
        /// The unit-table entry count = <c>UnitForm.DataCount()</c> (WinForms). The FE8U skill
        /// Assignment "personal" table (<c>SkillAssignmentUnitSkillSystemForm</c>) sizes its block-1
        /// table by it. Computed VERBATIM from <c>UnitForm.Init</c> (the FE8 path — the only path the
        /// skill forms reach, since they are <c>!is_multibyte</c>/FE8U-gated; the FE6 <c>+unit_datasize</c>
        /// ReInit at <c>version == 6</c> does not apply here): walk <c>unit_pointer</c>'s table with block
        /// <c>unit_datasize</c> and the fixed-count rule <c>i &lt; unit_maxcount</c>. Returns 0 on an
        /// unsafe/zero pointer (so a missing table yields an empty count, not a throw).
        /// </summary>
        public static uint UnitDataCount(ROM rom)
        {
            uint pointer = U.toOffset(rom.RomInfo.unit_pointer);
            // Guard the FULL pointer slot (root+3) before p32 so a near-EOF unit_pointer skips instead of
            // throwing (stricter than the ClassDataCount model, which reads a RomInfo slot always well
            // within bounds — behaviour-neutral on real ROMs, EOF-robust on synthetic ones).
            if (!U.isSafetyOffset(pointer + 3, rom)) return 0;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint block = rom.RomInfo.unit_datasize;
            if (block == 0) return 0;
            uint maxcount = rom.RomInfo.unit_maxcount;
            return rom.getBlockDataCount(baseAddr, block, (i, addr) => i < maxcount);
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

        // -------------------------------------------------------------------------------------------
        // slice 2k: header-TSA image primitives (the header-TSA image forms)
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Byte-exact port of WinForms <c>ImageUtil.CalcByteLengthForHeaderTSAData(data, pos)</c>: a
        /// header-TSA stream begins with a 2-byte <c>{x, y}</c> master-header (each stored value is the
        /// dimension minus one), and the body is <c>(x+1) * (y+1)</c> 16-bit cells; total length =
        /// <c>2 + (x+1)*(y+1)*2</c>. Returns 0 (degenerate / "no data") when the 2-byte header itself does
        /// not fit (<c>pos + 2 &gt;= Length</c>) — VERBATIM, including the WF <c>&gt;=</c> (so a header
        /// ending exactly at <c>Length</c> is rejected), which keeps the producer's emitted lengths
        /// byte-identical to the WF rebuild manifest. EOF-safe: never reads past the 2-byte header.
        /// </summary>
        public static uint CalcHeaderTsaLength(ROM rom, uint pos)
        {
            // WF: if (pos + 2 >= data.Length) return 0;  (the 2-byte header must fit AND not end at EOF)
            if (pos + 2 >= (uint)rom.Data.Length)
            {
                return 0;
            }
            uint master_headerx = rom.u8(pos) + 1;
            uint master_headery = rom.u8(pos + 1) + 1;
            return 2 + (master_headerx * master_headery * 2);
        }

        /// <summary>
        /// Reproduces WinForms <c>Address.AddHeaderTSAPointer(list, pointer, info, isPointerOnly)</c>: the
        /// <paramref name="pointer"/> slot holds a 32-bit ROM pointer to a header-TSA stream; emit a
        /// <see cref="Address.DataTypeEnum.HEADERTSA"/> block whose length is
        /// <see cref="CalcHeaderTsaLength"/> over the dereferenced target. The producer always scans real
        /// lengths (a defragment is never <c>isPointerOnly</c>), so the WF <c>isPointerOnly ? 0 : …</c>
        /// branch is fixed to the real-length side.
        /// <para>EOF-HARDENING: WF reads <c>u32(pointer)</c> after only a single-byte
        /// <c>isSafetyOffset(pointer)</c> check, so a <paramref name="pointer"/> in the last 3 bytes would
        /// throw inside <c>u32</c>. The producer guards the FULL 4-byte read extent (<c>pointer + 4 &lt;=
        /// Length</c>) before dereferencing — valid-ROM-equivalent (WF would throw there on a malformed
        /// near-EOF ROM), matching the established producer discipline on every raw computed read.</para>
        /// </summary>
        public static void EmitHeaderTsaPointer(ROM rom, List<Address> list, uint pointer, string info)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer, rom))
            {
                return;
            }
            // EOF-harden: u32(pointer) reads pointer..pointer+3 — guard the full extent (WF relies on a
            // valid ROM where isSafetyOffset(pointer) implies the 4 bytes are present).
            if (pointer + 4 > (uint)rom.Data.Length)
            {
                return;
            }
            uint addr = rom.u32(pointer);
            if (!U.isSafetyPointer(addr, rom))
            {
                return;
            }
            uint length = CalcHeaderTsaLength(rom, U.toOffset(addr));
            list.Add(new Address(addr, length, pointer, info, Address.DataTypeEnum.HEADERTSA));
        }

        /// <summary>
        /// Byte-exact port of WinForms <c>ImageUtilAP.CalcROMTCSLength(addr, rom)</c>: a ROMTCS (worldmap
        /// county-border AP) stream's length is found by <c>U.Grep</c>-scanning forward from
        /// <paramref name="addr"/> (capped at <c>addr + 20000</c> or EOF) for the EARLIEST of five known
        /// terminator byte-patterns; the length is <c>(matchAddr + plusOffset) - addr</c> using that
        /// pattern's trailer size. Returns 0 when no terminator is found (no data to relocate). All reads
        /// are pure ROM byte-pattern <c>U.Grep</c>s — no System.Drawing / disasm / PatchUtil — so this is
        /// faithfully headless. EOF-safe (the scan is clamped to <c>rom.Data.Length</c>).
        /// </summary>
        public static uint CalcRomTcsLength(ROM rom, uint addr)
        {
            byte[][] needArray = new byte[][]
            {
                 new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF }
                ,new byte[] { 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF }
                ,new byte[] { 0x05, 0x00, 0x00, 0x00, 0xFF, 0xFF }
                ,new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x10, 0x00 }
                ,new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0x10, 0x00 }
            };
            uint[] plusOffsetArray = new uint[] { 14, 8, 6, 6, 4 };

            uint limit = addr + 20000;
            if (limit > (uint)rom.Data.Length)
            {
                limit = (uint)rom.Data.Length;
            }

            uint endAddr = U.NOT_FOUND;
            uint plusOffset = 0;
            for (int i = 0; i < needArray.Length; i++)
            {
                uint a = U.Grep(rom.Data, needArray[i], addr, limit, 2);
                if (endAddr > a)
                {
                    endAddr = a;
                    plusOffset = plusOffsetArray[i];
                }
            }

            if (endAddr == U.NOT_FOUND)
            {
                return 0;
            }
            uint apLen = (endAddr + plusOffset) - addr;
            return apLen;
        }

        /// <summary>
        /// Reproduces WinForms <c>Address.AddROMTCSPointer(list, pointer, info, isPointerOnly)</c>: the
        /// <paramref name="pointer"/> slot holds a 32-bit ROM pointer to a ROMTCS AP stream; emit a
        /// <see cref="Address.DataTypeEnum.ROMTCS"/> block whose length is <see cref="CalcRomTcsLength"/>
        /// over the dereferenced target. As with <see cref="EmitHeaderTsaPointer"/>, the producer always
        /// scans real lengths. EOF-HARDENING: guard the full 4-byte <c>u32(pointer)</c> read extent before
        /// dereferencing (WF reads it after only a single-byte <c>isSafetyOffset(pointer)</c> check).
        /// </summary>
        public static void EmitRomTcsPointer(ROM rom, List<Address> list, uint pointer, string info)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer, rom))
            {
                return;
            }
            if (pointer + 4 > (uint)rom.Data.Length)
            {
                return;
            }
            uint addr = rom.u32(pointer);
            if (!U.isSafetyPointer(addr, rom))
            {
                return;
            }
            // WF AddROMTCSPointer(pointer-slot form) -> AddROMTCSPointer(addr, pointer, ...): addr is
            // toOffset'd + isSafetyOffset-checked inside, then length = CalcROMTCSLength(addr).
            uint target = U.toOffset(addr);
            if (!U.isSafetyOffset(target, rom))
            {
                return;
            }
            uint length = CalcRomTcsLength(rom, target);
            list.Add(new Address(target, length, pointer, info, Address.DataTypeEnum.ROMTCS));
        }

        /// <summary>
        /// Reproduces WinForms <c>AddressWinForms.AddAPPointer(list, pointer, info, isPointerOnly)</c>: the
        /// <paramref name="pointer"/> slot holds a 32-bit ROM pointer to an AP (Animated-Parts, unit
        /// move-icon animation) stream; emit a <see cref="Address.DataTypeEnum.AP"/> block whose length is
        /// <see cref="ImageUtilAPCore.CalcAPLength"/> (the AP frame/anime stream parser) over the
        /// dereferenced target. Structurally identical to <see cref="EmitRomTcsPointer"/> — the WF
        /// <c>AddAPPointer</c>/<c>AddAPAddress</c> pair differs from <c>AddROMTCSPointer</c>/
        /// <c>AddROMTCSAddress</c> only in <c>CalcAPLength</c> vs <c>CalcROMTCSLength</c> and the
        /// <c>AP</c> vs <c>ROMTCS</c> data type. The producer always scans real lengths (isPointerOnly
        /// false). <see cref="ImageUtilAPCore.CalcAPLength"/> is the verbatim Core port of WF
        /// <c>ImageUtilAP.CalcAPLength</c> (= <c>Parse</c> + <c>GetLength</c>), already covered by the Core
        /// AP-length tests. EOF-HARDENING: guard the full 4-byte <c>u32(pointer)</c> read extent before
        /// dereferencing (WF reads it after only a single-byte <c>isSafetyOffset(pointer)</c> check);
        /// <see cref="ImageUtilAPCore.CalcAPLength"/> itself returns 0 on any malformed / near-EOF stream
        /// (never throws).
        /// </summary>
        public static void EmitApPointer(ROM rom, List<Address> list, uint pointer, string info)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer, rom))
            {
                return;
            }
            if (pointer + 4 > (uint)rom.Data.Length)
            {
                return;
            }
            uint addr = rom.u32(pointer);
            if (!U.isSafetyPointer(addr, rom))
            {
                return;
            }
            // WF AddAPPointer(pointer-slot form) -> AddAPAddress(addr, pointer, ...): addr is toOffset'd +
            // isSafetyOffset-checked inside, then length = CalcAPLength(addr).
            uint target = U.toOffset(addr);
            if (!U.isSafetyOffset(target, rom))
            {
                return;
            }
            uint length = ImageUtilAPCore.CalcAPLength(rom.Data, target);
            list.Add(new Address(target, length, pointer, info, Address.DataTypeEnum.AP));
        }

        // -------------------------------------------------------------------------------------------
        // slice 2q: config-FILE-table forms (the table base/count come from a config TSV loaded via
        // U.ConfigDataFilename / U.LoadTSVResource, NOT a RomInfo pointer slot, so none fits the flat
        // StructDescriptor model — each gets a dedicated emitter). All four are pure-ROM + config: every
        // per-entry length is LZ77.getCompressedSize, a constant palette size, CalcHeaderTsaLength
        // (slice 2k), ROM.getString, or a pure-ROM frame/pointer-list terminator walk — NO ImageUtilOAM,
        // System.Drawing, or disasm. The config files (other_text_/tsaanime_/tsaanime2_/romanime_) ship
        // under config/data/; ConfigDataFilename resolves them against CoreState.BaseDirectory. In a
        // headless scan with the config tree absent the load returns an EMPTY dict/list (no throw — WF's
        // LoadTSVResource / MakeOtherTextMap exact behavior), so the emitter contributes nothing, which
        // is faithful (the same as a vanilla ROM with no entries).

        /// <summary>
        /// Load a config TSV table (<see cref="U.LoadTSVResource"/>) for a slice-2q anime form, never
        /// throwing. WF's <c>PreLoadResource</c> always runs with a valid <see cref="CoreState.BaseDirectory"/>
        /// (set during app/CLI init), but the producer may be invoked by a test / tool before the config
        /// tree is staged: <see cref="U.ConfigDataFilename"/> does <c>Path.Combine(CoreState.BaseDirectory,
        /// …)</c>, which throws <see cref="ArgumentNullException"/> when BaseDirectory is null. Wrap BOTH the
        /// path build and the load in a try/catch so a missing/unconfigured config tree degrades to an EMPTY
        /// table (emit nothing) — the same faithful headless behavior as a present-but-empty config file
        /// (WF's <see cref="U.LoadTSVResource"/> returns an empty dict for a missing file).
        /// </summary>
        static Dictionary<uint, string[]> LoadConfigTableSafe(string type, ROM rom)
        {
            try
            {
                // isRequired:false — a missing config file degrades to an empty dict WITHOUT the
                // ShowError dialog + Debug.Assert(false) that U.IsRequiredFileExist raises on the default
                // isRequired:true path (which would pop an error for every missing optional config during a
                // rebuild). The try/catch still covers a null BaseDirectory (ArgumentNullException).
                return U.LoadTSVResource(U.ConfigDataFilename(type, rom), isRequired: false);
            }
            catch (Exception)
            {
                return new Dictionary<uint, string[]>();
            }
        }

        /// <summary>
        /// <c>OtherTextForm.MakeAllDataLength</c> (slice 2q, version-agnostic call site). WF iterates the
        /// per-entry list from <c>MakeOtherTextMap()</c> (the <c>other_text_</c> config file, ONE hex
        /// pointer per line, comment/other-lang filtered, <c>isSafetyOffset</c> gated — reproduced by
        /// <see cref="TextSourceListCore.MakeOtherTextList"/>), and for each entry <c>p = textlist[i].addr</c>
        /// reads <c>nameAddr = p32(p + 0)</c>; if <c>isSafetyOffset(nameAddr)</c> it decodes the C string
        /// (<c>getString(nameAddr, out length)</c>) and emits a BIN block of that exact decoded length
        /// (<c>AddAddress(nameAddr, length, p + 0, name, BIN)</c> — the byte length of the string WITHOUT a
        /// trailing-NUL +1, like the MapTerrainName/Text BinString sub-walks). VERBATIM.
        /// <para>The decode needs a <see cref="CoreState.SystemTextEncoder"/> (set in the real app + CLI
        /// <c>InitFull</c>). With none loaded the producer SKIPS the per-entry block rather than NRE — the
        /// embedded string is then un-tracked, but a partial scan is already <c>IsComplete == false</c>
        /// (mirrors the <see cref="EmitSubWalks"/> encoder-null guard).</para>
        /// <para>EOF-HARDENING: WF reads <c>p32(p)</c> after only the <c>isSafetyOffset(p)</c> the entry
        /// list already guarantees; the producer guards the full 4-byte read extent before dereferencing.</para>
        /// </summary>
        public static void EmitOtherText(ROM rom, List<Address> list)
        {
            // BOTH the entry-list builder (TextSourceListCore.MakeOtherTextList decodes each entry's name
            // via ROM.getString) AND the per-entry block decode need a CoreState.SystemTextEncoder (set in
            // the real app + CLI InitFull). Check it FIRST and skip the whole form (don't NRE) when none is
            // loaded — WF's MakeOtherTextMap would equally need one; mirrors the EmitSubWalks guard.
            if (CoreState.SystemTextEncoder == null)
            {
                return;
            }

            // MakeOtherTextList reproduces MakeOtherTextMap (the entry's .addr is the config-file offset
            // p; its .name is irrelevant here — MakeAllDataLength re-decodes the name from nameAddr).
            // Missing config tree -> empty list -> nothing emitted (faithful headless behavior).
            List<AddrResult> textlist = TextSourceListCore.MakeOtherTextList(rom);

            for (int i = 0; i < textlist.Count; i++)
            {
                uint p = textlist[i].addr;
                // EOF-harden: p32(p) reads p..p+3. The entry list already isSafetyOffset(p)-gated p, but
                // guard the full extent so a near-EOF p skips rather than throws.
                if (p + 4 > (uint)rom.Data.Length)
                {
                    continue;
                }
                uint nameAddr = rom.p32(0 + p);
                if (U.isSafetyOffset(nameAddr, rom))
                {
                    int length;
                    string name = rom.getString(nameAddr, out length);
                    Address.AddAddress(list, nameAddr, (uint)length, p + 0, name,
                        Address.DataTypeEnum.BIN);
                }
            }
        }

        /// <summary>
        /// <c>ImageTSAAnimeForm.MakeAllDataLength</c> (slice 2q; WF calls it in the <c>version==8</c> AND
        /// <c>version==7</c> branches — NOT FE6). WF loads <c>g_TSAAnime = LoadTSVResource(ConfigDataFilename(
        /// "tsaanime_"))</c> (key = a 4-byte pointer SLOT, value = [count, name, …]). For each entry: the
        /// slot is <c>pointer = toOffset(key)</c>; <c>addr = p32(pointer)</c> is the anime-record table base;
        /// <c>count = atoh(value[0])</c> is its FIXED entry count. The main IFR is <c>ReInit(addr, count)</c>
        /// (block <c>4*3 = 12</c>, pointerIndexes {0,4,8}). Per entry <c>i &lt; count</c> (addr += 12): an
        /// LZ77 image @ +0, a fixed <c>0x20*8</c>-byte palette @ +4, and an LZ77 TSA @ +8. Reproduced VERBATIM.
        /// <para>EOF-HARDENING: WF reads <c>p32(pointer)</c> after only <c>isSafetyOffset(pointer)</c>; the
        /// producer guards the full 4-byte extent. The per-column <c>Address.Add*</c> helpers re-check pointer
        /// safety and compute the real length (<c>getCompressedSize</c> — 0/never-throws on a malformed stream).</para>
        /// </summary>
        public static void EmitImageTSAAnime(ROM rom, List<Address> list)
        {
            // WF PreLoadResource(): g_TSAAnime = LoadTSVResource(ConfigDataFilename("tsaanime_")). Load it
            // fresh here (the producer has no live form to hold the static). Missing config -> empty dict.
            const uint block = 4 * 3; // ImageTSAAnimeForm.Init BlockSize.
            var g_TSAAnime = LoadConfigTableSafe("tsaanime_", rom);
            foreach (var pair in g_TSAAnime)
            {
                uint pointer = pair.Key;
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer, rom))
                {
                    continue;
                }
                // EOF-harden: p32(pointer) reads pointer..pointer+3.
                if (pointer + 4 > (uint)rom.Data.Length)
                {
                    continue;
                }
                uint addr = rom.p32(pointer);
                if (!U.isSafetyOffset(addr, rom))
                {
                    continue;
                }
                uint count = U.atoh(U.at(pair.Value, 0));
                string basename = "TSAANIME " + U.at(pair.Value, 1) + " ";

                // Main IFR: ReInit(addr, count) -> BaseAddress addr, DataCount count; AddAddress(IFR, ...)
                // length = block * (count + 1), pointer = BasePointer (= pointer here, safe).
                uint length = block * (count + 1);
                list.Add(new Address(addr, length, pointer, basename,
                    Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 4, 8 }));

                for (uint i = 0; i < count; i++, addr += block)
                {
                    string name = basename + "" + U.To0xHexString(i);

                    Address.AddLZ77Pointer(list, addr + 0, name + " IMAGE", false,
                        Address.DataTypeEnum.LZ77IMG);
                    Address.AddPointer(list, addr + 4, 0x20 * 8, name + " PALETTE",
                        Address.DataTypeEnum.PAL);
                    Address.AddLZ77Pointer(list, addr + 8, name + " TSA", false,
                        Address.DataTypeEnum.LZ77TSA);
                }
            }
        }

        /// <summary>
        /// <c>ImageTSAAnime2Form.MakeAllDataLength</c> (slice 2q; WF calls it in the <c>version==8</c> branch
        /// ONLY). WF loads <c>g_TSAAnime = LoadTSVResource(ConfigDataFilename("tsaanime2_"))</c> (key = a
        /// 4-byte pointer SLOT, value = [name, …]). The main IFR is block 12 with IsDataExists =
        /// <c>isPointer(u32(addr+8))</c>; the N1 IFR is block 20 with IsDataExists = <c>i &lt; 1</c>. Per entry:
        /// <c>pointer = toOffset(key)</c>; <c>addr = p32(pointer)</c>. N1 <c>ReInitPointer(pointer, 1)</c> makes
        /// N1.BasePointer = pointer, N1.BaseAddress = addr, N1.DataCount = 1; main <c>ReInit(addr + 20)</c>
        /// walks the TSA records starting 20 bytes past the header (VARIABLE count via getBlockDataCount).
        /// WF emits: the main IFR (pointerIndexes {8}), the N1 IFR (pointerIndexes {4,16}); if N1.DataCount
        /// &gt;= 1 a HEADER pair (LZ77 image @ addr+16, fixed 0x20-byte palette @ addr+4); then it skips the
        /// 20-byte header (<c>addr += 20</c>) and per record <c>i &lt; main.DataCount</c> (addr += 12) emits a
        /// header-TSA pointer @ addr+8. Reproduced VERBATIM.
        /// <para>EOF-HARDENING: the <c>p32(pointer)</c> slot read and the <c>u32(a+8)</c> IsDataExists read
        /// guard their full extents (getBlockDataCount already bounds addr+block &lt;= Length; the explicit
        /// guard covers the +8 sub-read and the near-EOF slot). <see cref="EmitHeaderTsaPointer"/> is EOF-safe.</para>
        /// </summary>
        public static void EmitImageTSAAnime2(ROM rom, List<Address> list)
        {
            const uint block = 12;   // ImageTSAAnime2Form.Init BlockSize.
            const uint n1Block = 20; // ImageTSAAnime2Form.N1_Init BlockSize.
            // WF ImageTSAAnime2Form.PreLoadResource(): g_TSAAnime = LoadTSVResource("tsaanime2_").
            var g_TSAAnime = LoadConfigTableSafe("tsaanime2_", rom);
            foreach (var pair in g_TSAAnime)
            {
                uint pointer = pair.Key;
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer, rom))
                {
                    continue;
                }
                // EOF-harden: p32(pointer) reads pointer..pointer+3.
                if (pointer + 4 > (uint)rom.Data.Length)
                {
                    continue;
                }
                uint addr = rom.p32(pointer);
                if (!U.isSafetyOffset(addr, rom))
                {
                    continue;
                }
                string basename = "TSAANIME2 " + U.at(pair.Value, 0) + " ";

                // WF: N1_InputFormRef.ReInitPointer(pointer, 1) -> N1.BasePointer = pointer, N1.BaseAddress
                // = p32(pointer) = addr, N1.DataCount = 1. InputFormRef.ReInit(addr + 20) -> main.BaseAddress
                // = toOffset(addr + 20), main.DataCount = getBlockDataCount(IsDataExists = isPointer(u32(a+8))),
                // main.BasePointer is the form default (unset/0).
                uint mainAddr = U.toOffset(addr + 20);
                uint mainCount;
                if (U.isSafetyOffset(mainAddr, rom))
                {
                    mainCount = rom.getBlockDataCount(mainAddr, block, (i, a) =>
                    {
                        // WF Init IsDataExists: isPointer(u32(a + 8)). getBlockDataCount already bounds
                        // a + block(12) <= Length, so a + 8 .. a + 11 is in range; guard anyway.
                        if (a + 12 > (uint)rom.Data.Length)
                        {
                            return false;
                        }
                        return U.isPointer(rom.u32(a + 8));
                    });
                }
                else
                {
                    mainCount = 0;
                }

                // WF order: AddAddress(main IFR, {8}) FIRST, then AddAddress(N1 IFR, {4,16}).
                // WF main AddAddress: returns WITHOUT emitting when !isSafetyOffset(main.BaseAddress).
                if (U.isSafetyOffset(mainAddr, rom))
                {
                    uint mainLength = block * (mainCount + 1);
                    // WF main IFR BasePointer is unset (0) -> AddAddress sets pointer = NOT_FOUND.
                    list.Add(new Address(mainAddr, mainLength, U.NOT_FOUND, basename,
                        Address.DataTypeEnum.InputFormRef, block, new uint[] { 8 }));
                }

                // N1 IFR: ReInitPointer(pointer, 1) -> BasePointer pointer, BaseAddress addr, DataCount 1.
                // (addr is already isSafetyOffset-checked above, so WF's N1 AddAddress always emits.)
                uint n1Length = n1Block * (1 + 1);
                list.Add(new Address(addr, n1Length, pointer, basename,
                    Address.DataTypeEnum.InputFormRef, n1Block, new uint[] { 4, 16 }));

                if (1 >= 1) // WF: if (N1_InputFormRef.DataCount >= 1) — DataCount is fixed 1 here.
                {
                    string name = basename + " HEADER";

                    Address.AddLZ77Pointer(list, addr + 16, name + " IMAGE", false,
                        Address.DataTypeEnum.LZ77IMG);
                    Address.AddPointer(list, addr + 4, 0x20, name + " PALETTE",
                        Address.DataTypeEnum.PAL);
                }

                uint recAddr = addr + 20; // WF: addr += 20 (SkipHeader).
                for (uint i = 0; i < mainCount; i++, recAddr += block)
                {
                    string name = basename + "" + U.To0xHexString(i);

                    EmitHeaderTsaPointer(rom, list, recAddr + 8, name + " TSA");
                }
            }
        }

        /// <summary>
        /// <c>ImageRomAnimeForm.MakeAllDataLength</c> (slice 2q, version-agnostic call site). WF loads
        /// <c>g_ROMAnime = LoadTSVResource(ConfigDataFilename("romanime_"))</c> (value = [imageWidth, option,
        /// framePointer, tsaPointer, imagePointer, palettePointer, name, …]). Per entry it gates on
        /// <see cref="CheckRomAnimePointers"/> (pure-ROM), then emits: a 4-byte FRAME pointer + (frameCount*4)
        /// BIN frame table (frameCount from <see cref="GetRomAnimeFrameCountLow"/>, an <c>u16==0xFFFF</c>
        /// terminator walk; if NOT_FOUND the whole entry is skipped); a 4-byte TSA pointer + one LZ77 TSA per
        /// <see cref="GetRomAnimePointerListCount"/> entry; a 4-byte Image pointer + one LZ77 image per list
        /// entry; a 4-byte Palette pointer + one fixed <c>2*16</c>-byte PAL per
        /// <see cref="GetRomAnimePalettePointerListCount"/> entry. The per-frame pointer slot is verified
        /// (<c>p32(baseAddr + i*4) == a</c>) before being used as the relocation pointer (else NOT_FOUND).
        /// All helpers are reproduced VERBATIM (pure-ROM, EOF-hardened).
        /// </summary>
        public static void EmitImageRomAnime(ROM rom, List<Address> list)
        {
            // WF ImageRomAnimeForm.PreLoadResource(): g_ROMAnime = LoadTSVResource("romanime_").
            var g_ROMAnime = LoadConfigTableSafe("romanime_", rom);
            foreach (var pair in g_ROMAnime)
            {
                string[] sp = pair.Value;
                string option = U.at(sp, 1);
                uint framePointer = U.atoh(U.at(sp, 2));
                uint tsaPointer = U.atoh(U.at(sp, 3));
                uint imagePointer = U.atoh(U.at(sp, 4));
                uint palettePointer = U.atoh(U.at(sp, 5));
                string name = U.at(sp, 6);

                if (!CheckRomAnimePointers(rom, framePointer, tsaPointer, imagePointer, palettePointer))
                {
                    continue;
                }

                uint frameCount = U.NOT_FOUND;
                if (U.isSafetyOffset(framePointer, rom))
                {
                    Address.AddAddress(list, framePointer, 4, U.NOT_FOUND,
                        name + " FRAME Pointer", Address.DataTypeEnum.POINTER);
                    // WF: the !isPointerOnly branch always runs for a producer scan.
                    frameCount = GetRomAnimeFrameCountLow(rom, framePointer);
                    if (frameCount != U.NOT_FOUND)
                    {
                        // EOF-harden the p32(framePointer) slot read (isSafetyOffset(framePointer) above
                        // guards the offset but not the full 4-byte extent on a near-EOF slot).
                        if (framePointer + 4 <= (uint)rom.Data.Length)
                        {
                            uint p = rom.p32(framePointer);
                            Address.AddAddress(list, p, frameCount * 4, framePointer,
                                name + " FRAME", Address.DataTypeEnum.BIN);
                        }
                    }
                }

                if (frameCount == U.NOT_FOUND)
                {
                    continue;
                }

                Address.AddAddress(list, tsaPointer, 4, U.NOT_FOUND,
                    name + " TSA Pointer", Address.DataTypeEnum.POINTER);
                EmitRomAnimePointerList(rom, list, tsaPointer,
                    GetRomAnimePointerListCount(rom, tsaPointer),
                    name + " TSA", Address.DataTypeEnum.LZ77TSA, isPalette: false);

                Address.AddAddress(list, imagePointer, 4, U.NOT_FOUND,
                    name + " Image Pointer", Address.DataTypeEnum.POINTER);
                EmitRomAnimePointerList(rom, list, imagePointer,
                    GetRomAnimePointerListCount(rom, imagePointer),
                    name + " Image", Address.DataTypeEnum.LZ77IMG, isPalette: false);

                Address.AddAddress(list, palettePointer, 4, U.NOT_FOUND,
                    name + " Palette Pointer", Address.DataTypeEnum.POINTER);
                EmitRomAnimePointerList(rom, list, palettePointer,
                    GetRomAnimePalettePointerListCount(rom, palettePointer, framePointer, option),
                    name + " Palette", Address.DataTypeEnum.PAL, isPalette: true);
            }
        }

        /// <summary>Shared per-pointer-list emission for <see cref="EmitImageRomAnime"/>: walk the resolved
        /// target list, and for each entry verify the per-entry pointer slot (<c>p32(baseAddr + i*4) == a</c>;
        /// else relocation pointer = NOT_FOUND) and emit an Address. LZ77 entries use
        /// <c>LZ77.getCompressedSize</c>; the palette list uses a fixed <c>2*16</c> length. VERBATIM
        /// reproduction of the three WF column loops (which differ only in length + data type).</summary>
        static void EmitRomAnimePointerList(ROM rom, List<Address> list, uint listPointer,
            List<uint> targets, string name, Address.DataTypeEnum type, bool isPalette)
        {
            // WF: baseAddr = p32(listPointer). EOF-harden the slot read.
            uint baseAddr = 0;
            if (U.isSafetyOffset(listPointer, rom) && listPointer + 4 <= (uint)rom.Data.Length)
            {
                baseAddr = rom.p32(listPointer);
            }
            for (int i = 0; i < targets.Count; i++)
            {
                uint p = baseAddr + ((uint)i * 4);
                uint a = targets[i];
                // WF: if (p32(p) != a) p = NOT_FOUND; — the per-entry pointer slot must actually point at
                // this target to be used as the relocation pointer. EOF-harden the p32(p) read.
                if (p + 4 > (uint)rom.Data.Length || rom.p32(p) != a)
                {
                    p = U.NOT_FOUND;
                }
                uint length = isPalette
                    ? (uint)(2 * 16)
                    : LZ77.getCompressedSize(rom.Data, U.toOffset(a));
                Address.AddAddress(list, a, length, p, name, type);
            }
        }

        /// <summary>VERBATIM port of WF <c>ImageRomAnimeForm.checkPonters</c>: a pure-ROM gate — if a frame
        /// pointer slot is present it must hold a safe pointer, and the TSA/image/palette pointer slots must
        /// each be a safe offset holding a safe pointer. Returns false (skip the entry) otherwise.
        /// EOF-hardened on every <c>u32</c> slot read.</summary>
        public static bool CheckRomAnimePointers(ROM rom, uint framePointer, uint tsaPointer,
            uint imagePointer, uint palettePointer)
        {
            if (U.isSafetyOffset(framePointer, rom))
            {
                if (framePointer + 4 > (uint)rom.Data.Length)
                {
                    return false;
                }
                uint frameAddress = rom.u32(framePointer);
                if (!U.isSafetyPointer(frameAddress, rom))
                {
                    return false;
                }
            }

            if (!U.isSafetyOffset(tsaPointer, rom) || tsaPointer + 4 > (uint)rom.Data.Length)
            {
                return false;
            }
            uint tsaAddress = rom.u32(tsaPointer);
            if (!U.isSafetyPointer(tsaAddress, rom))
            {
                return false;
            }

            if (!U.isSafetyOffset(imagePointer, rom) || imagePointer + 4 > (uint)rom.Data.Length)
            {
                return false;
            }
            uint imageAddress = rom.u32(imagePointer);
            if (!U.isSafetyPointer(imageAddress, rom))
            {
                return false;
            }

            if (!U.isSafetyOffset(palettePointer, rom) || palettePointer + 4 > (uint)rom.Data.Length)
            {
                return false;
            }
            uint paletteAddress = rom.u32(palettePointer);
            if (!U.isSafetyPointer(paletteAddress, rom))
            {
                return false;
            }
            return true;
        }

        /// <summary>VERBATIM port of WF <c>ImageRomAnimeForm.GetFrameCountLow</c>: the frame table behind
        /// <paramref name="framePointer"/> is an uncompressed stream of 4-byte {id, wait} records; the count
        /// is the number of records before an <c>id == 0xFFFF</c> terminator. A 1 MB limiter (clamped to
        /// <c>Data.Length</c>) prevents a runaway scan. Returns NOT_FOUND when the pointer / its target is
        /// not a safe offset. EOF-hardened on every <c>u16</c> read.</summary>
        public static uint GetRomAnimeFrameCountLow(ROM rom, uint framePointer)
        {
            if (!U.isSafetyOffset(framePointer, rom))
            {
                return U.NOT_FOUND;
            }
            if (framePointer + 4 > (uint)rom.Data.Length)
            {
                return U.NOT_FOUND;
            }
            uint addr = rom.p32(framePointer);
            if (!U.isSafetyOffset(addr, rom))
            {
                return U.NOT_FOUND;
            }

            // WF limitter = addr + 1MB, clamped to Data.Length.
            uint limitter = addr + 1024 * 1024;
            uint dataLen = (uint)rom.Data.Length;
            if (limitter > dataLen)
            {
                limitter = dataLen;
            }

            uint i;
            for (i = 0; addr < limitter; i++, addr += 4)
            {
                // WF reads u16(addr) and u16(addr+2); guard the 4-byte record extent.
                if (addr + 4 > dataLen)
                {
                    break;
                }
                uint id = rom.u16(addr);
                if (id == 0xFFFF)
                {
                    break;
                }
            }
            return i;
        }

        /// <summary>VERBATIM port of WF <c>ImageRomAnimeForm.GetPointerListCount</c>: the list behind
        /// <paramref name="p"/> is a NULL/non-pointer-terminated array of 4-byte ROM pointers; walk
        /// <c>a = p32(p)</c> forward (step 4) collecting <c>toOffset</c> targets while the slot is a safe
        /// pointer. If the list resolves to ZERO entries WF still adds a single <c>toOffset(a)</c> fallback
        /// (the resolved base IS the lone target). EOF-hardened on every <c>u32</c> read.</summary>
        public static List<uint> GetRomAnimePointerListCount(ROM rom, uint p)
        {
            var ret = new List<uint>();

            if (!U.isSafetyOffset(p, rom) || p + 4 > (uint)rom.Data.Length)
            {
                return ret;
            }
            uint a = rom.p32(p);
            if (!U.isSafetyOffset(a, rom))
            {
                return ret;
            }

            uint length = (uint)rom.Data.Length - 4;
            for (; a < length; a += 4)
            {
                // WF reads u32(a); a < Data.Length-4 keeps a..a+3 in range.
                uint p2 = rom.u32(a);
                if (!U.isSafetyPointer(p2, rom))
                {
                    break;
                }
                ret.Add(U.toOffset(p2));
            }

            if (ret.Count <= 0)
            {
                ret.Add(U.toOffset(a));
            }
            return ret;
        }

        /// <summary>VERBATIM port of WF <c>ImageRomAnimeForm.GetPalettePointerListCount</c>: like
        /// <see cref="GetRomAnimePointerListCount"/>, but when the list resolves to ZERO entries the
        /// fallback depends on <paramref name="option"/>/<paramref name="framePointer"/>: "COMMONPALETTE"
        /// (or <paramref name="framePointer"/> &gt;= 0x100) adds the single resolved base; a
        /// <paramref name="framePointer"/> &lt; 0x100 (a FIXED per-frame palette count, not a pointer) adds
        /// one entry per frame at <c>a + i*(2*16)</c>. EOF-hardened on the <c>u32</c> read.</summary>
        public static List<uint> GetRomAnimePalettePointerListCount(ROM rom, uint p, uint framePointer,
            string option)
        {
            var ret = new List<uint>();

            if (!U.isSafetyOffset(p, rom) || p + 4 > (uint)rom.Data.Length)
            {
                return ret;
            }
            uint a = rom.p32(p);
            if (!U.isSafetyOffset(a, rom))
            {
                return ret;
            }

            uint length = (uint)rom.Data.Length - 4;
            for (; a < length; a += 4)
            {
                uint p2 = rom.u32(a);
                if (!U.isSafetyPointer(p2, rom))
                {
                    break;
                }
                ret.Add(U.toOffset(p2));
            }

            if (ret.Count <= 0)
            {
                if (option == "COMMONPALETTE")
                {
                    ret.Add(U.toOffset(a));
                }
                else if (framePointer < 0x100)
                {
                    for (uint i = 0; i < framePointer; i++)
                    {
                        ret.Add(U.toOffset(a + (i * (2 * 16))));
                    }
                }
                else
                {
                    ret.Add(U.toOffset(a));
                }
            }
            return ret;
        }

        // -------------------------------------------------------------------------------------------
        // slice 2k: the header-TSA image form emitters
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// <c>ImageSystemIconForm.MakeAllDataLength</c> (slice 2k, version-agnostic call site; internally
        /// version-gated). A long FLAT sequence of system-icon LZ77 images + PALs + HEADER-TSA pointers,
        /// reading RomInfo pointers directly (each <c>image = p32(pointer)</c>, then
        /// <see cref="Address.AddAddress"/> with the precomputed addr). The <c>version &gt;= 7</c>,
        /// <c>version == 8</c>, and <c>version &gt;= 8</c> internal gates are reproduced VERBATIM. All reads
        /// are pure ROM (<c>p32</c> / <c>LZ77.getCompressedSize</c> via AddAddress/AddLZ77Pointer /
        /// <see cref="EmitHeaderTsaPointer"/>); the producer always computes real lengths (isPointerOnly
        /// false), so the WF <c>isPointerOnly ? 0 : getCompressedSize</c> branch is fixed to the real side.
        /// </summary>
        public static void EmitImageSystemIcon(ROM rom, List<Address> list)
        {
            uint image, palette;

            image = rom.p32(rom.RomInfo.system_icon_pointer);
            palette = rom.p32(rom.RomInfo.system_icon_palette_pointer);
            Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                rom.RomInfo.system_icon_pointer, "system_icon image", Address.DataTypeEnum.LZ77IMG);
            Address.AddAddress(list, palette, 0x20 * 2,
                rom.RomInfo.system_icon_palette_pointer, "system_icon pal", Address.DataTypeEnum.PAL);

            image = rom.p32(rom.RomInfo.system_move_allowicon_pointer);
            palette = rom.p32(rom.RomInfo.system_move_allowicon_palette_pointer);
            Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                rom.RomInfo.system_move_allowicon_pointer, "system_icon image", Address.DataTypeEnum.LZ77IMG);
            Address.AddAddress(list, palette, 0x20,
                rom.RomInfo.system_move_allowicon_palette_pointer, "system_move_allowicon pal", Address.DataTypeEnum.LZ77PAL);

            image = rom.p32(rom.RomInfo.system_weapon_icon_pointer);
            palette = rom.p32(rom.RomInfo.system_weapon_icon_palette_pointer);
            Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                rom.RomInfo.system_weapon_icon_pointer, "system_weapon image", Address.DataTypeEnum.LZ77IMG);
            Address.AddAddress(list, palette, 0x20,
                rom.RomInfo.system_weapon_icon_palette_pointer, "system_weapon pal", Address.DataTypeEnum.PAL);

            image = rom.p32(rom.RomInfo.system_music_icon_pointer);
            palette = rom.p32(rom.RomInfo.system_music_icon_palette_pointer);
            Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                rom.RomInfo.system_music_icon_pointer, "system_music image", Address.DataTypeEnum.LZ77IMG);
            Address.AddAddress(list, palette, 0x20,
                rom.RomInfo.system_music_icon_palette_pointer, "system_music pal", Address.DataTypeEnum.PAL);

            Address.AddAddress(list, rom.RomInfo.unit_icon_palette_address, 0x20,
                U.NOT_FOUND, "unit_icon_play pal", Address.DataTypeEnum.PAL);
            Address.AddAddress(list, rom.RomInfo.unit_icon_enemey_palette_address, 0x20,
                U.NOT_FOUND, "unit_icon_enemey pal", Address.DataTypeEnum.PAL);
            Address.AddAddress(list, rom.RomInfo.unit_icon_npc_palette_address, 0x20,
                U.NOT_FOUND, "unit_icon_npc pal", Address.DataTypeEnum.PAL);
            Address.AddAddress(list, rom.RomInfo.unit_icon_gray_palette_address, 0x20,
                U.NOT_FOUND, "unit_icon_gray pal", Address.DataTypeEnum.PAL);
            Address.AddAddress(list, rom.RomInfo.unit_icon_four_palette_address, 0x20,
                U.NOT_FOUND, "unit_icon_for pal", Address.DataTypeEnum.PAL);

            if (rom.RomInfo.version >= 7)
            {
                image = rom.p32(rom.RomInfo.systemmenu_common_image_pointer);
                palette = rom.p32(rom.RomInfo.systemmenu_common_palette_pointer);
                Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                    rom.RomInfo.systemmenu_common_image_pointer, "systemmenu_goal image", Address.DataTypeEnum.LZ77IMG);
                EmitHeaderTsaPointer(rom, list, rom.RomInfo.systemmenu_goal_tsa_pointer, "systemmenu_goal tsa");
                Address.AddAddress(list, palette, 0x20 * 4,
                    rom.RomInfo.systemmenu_common_palette_pointer, "systemmenu_goal pal", Address.DataTypeEnum.PAL);
            }

            image = rom.p32(rom.RomInfo.systemmenu_common_image_pointer);
            palette = rom.p32(rom.RomInfo.systemmenu_common_palette_pointer);
            Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                rom.RomInfo.systemmenu_common_image_pointer, "systemmenu_common image", Address.DataTypeEnum.LZ77IMG);
            EmitHeaderTsaPointer(rom, list, rom.RomInfo.systemmenu_terrain_tsa_pointer, "systemmenu_common tsa");
            Address.AddAddress(list, palette, 0x20 * 4,
                rom.RomInfo.systemmenu_common_palette_pointer, "systemmenu_common", Address.DataTypeEnum.LZ77IMG);

            EmitHeaderTsaPointer(rom, list, rom.RomInfo.systemmenu_name_tsa_pointer, "systemmenu_name tsa");

            image = rom.p32(rom.RomInfo.systemmenu_battlepreview_image_pointer);
            palette = rom.p32(rom.RomInfo.systemmenu_battlepreview_palette_pointer);
            Address.AddAddress(list, image, LZ77.getCompressedSize(rom.Data, image),
                rom.RomInfo.systemmenu_battlepreview_image_pointer, "systemmenu_battlepreview image", Address.DataTypeEnum.LZ77IMG);
            EmitHeaderTsaPointer(rom, list, rom.RomInfo.systemmenu_battlepreview_tsa_pointer, "systemmenu_battlepreview tsa");
            Address.AddAddress(list, palette, 0x20 * 4,
                rom.RomInfo.systemmenu_battlepreview_palette_pointer, "systemmenu_battlepreview pal", Address.DataTypeEnum.PAL);
            if (rom.RomInfo.version == 8)
            {//FE8の場合、画像イメージは4つのポインタがあります。
                uint other_image_p = rom.RomInfo.systemmenu_battlepreview_image_pointer;
                Address.AddLZ77Pointer(list, other_image_p + 4, "systemmenu_battlepreview_enemy", false, Address.DataTypeEnum.LZ77IMG);
                Address.AddLZ77Pointer(list, other_image_p + 8, "systemmenu_battlepreview_npc", false, Address.DataTypeEnum.LZ77IMG);
                Address.AddLZ77Pointer(list, other_image_p + 12, "systemmenu_battlepreview_4th", false, Address.DataTypeEnum.LZ77IMG);
            }

            palette = rom.p32(rom.RomInfo.systemarea_move_gradation_palette_pointer);
            Address.AddAddress(list, palette, 0x20 * 3,
                rom.RomInfo.systemarea_move_gradation_palette_pointer, "systemarea_move_gradation", Address.DataTypeEnum.PAL);
            palette = rom.p32(rom.RomInfo.systemarea_attack_gradation_palette_pointer);
            Address.AddAddress(list, palette, 0x20 * 3,
                rom.RomInfo.systemarea_attack_gradation_palette_pointer, "systemarea_attack_gradation", Address.DataTypeEnum.PAL);
            palette = rom.p32(rom.RomInfo.systemarea_staff_gradation_palette_pointer);
            Address.AddAddress(list, palette, 0x20 * 3,
                rom.RomInfo.systemarea_staff_gradation_palette_pointer, "systemarea_staff_gradation", Address.DataTypeEnum.PAL);

            if (rom.RomInfo.version >= 8)
            {//FE8
                image = rom.p32(rom.RomInfo.systemmenu_badstatus_image_pointer);
                Address.AddAddress(list, image, 40 * (8 * 9) / 2,
                    rom.RomInfo.systemmenu_badstatus_image_pointer, "systemmenu_badstatus", Address.DataTypeEnum.LZ77IMG);
            }
            else if (rom.RomInfo.version >= 7)
            {//FE7
                image = rom.p32(rom.RomInfo.systemmenu_badstatus_image_pointer);
                Address.AddAddress(list, image, 40 * (8 * 4) / 2,
                    rom.RomInfo.systemmenu_badstatus_image_pointer, "systemmenu_badstatus", Address.DataTypeEnum.LZ77IMG);
            }
            else
            {//FE6
            }

            palette = rom.p32(rom.RomInfo.systemmenu_badstatus_palette_pointer);
            Address.AddAddress(list, palette, 0x20,
                rom.RomInfo.systemmenu_badstatus_palette_pointer, "systemmenu_badstatus_palette", Address.DataTypeEnum.PAL);

            EmitHeaderTsaPointer(rom, list, rom.RomInfo.system_tsa_16color_304x240_pointer, "system_tsa_16color_304x240");
        }

        /// <summary>
        /// <c>ImageBGForm.MakeAllDataLength</c> (slice 2k, version-agnostic). A 12-byte IFR table at
        /// <c>bg_pointer</c> (rule <see cref="DataCountRule.NestedPointerAt"/>'s sibling — actually
        /// <c>ImageBGCore.IsValidEntry</c>, the BG256-aware pointer-or-NULL rule), PI <c>{0,4,8}</c>. Per
        /// entry the WF emits one of three shapes depending on the <c>+4</c> field under the
        /// <c>BG256Color</c> patch: a 255-color cutscene (<c>tsa==0</c>) / 224-color (<c>tsa==1</c>) entry
        /// is LZ77 image + a 0x20*16 PAL (NO TSA); a normal entry is LZ77 image + a HEADER-TSA + a 0x20*8
        /// PAL. Reproduced VERBATIM (the per-entry branch matches WF, NOT the renderer).
        /// </summary>
        public static void EmitImageBG(ROM rom, List<Address> list)
        {
            EmitImageBGAt(rom, list, rom.RomInfo.bg_pointer);
        }

        /// <summary>ImageBG walk from an explicit pointer slot (test seam — lets a synthetic ROM supply it
        /// without populating RomInfo). See <see cref="EmitImageBG"/>.</summary>
        public static void EmitImageBGAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 12;

            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // IsDataExists = ImageBGForm.Init (the BG256-aware pointer-or-NULL rule). getBlockDataCount
            // only fires while addr+block(12)<=Length, so the +0/+4 u32 reads are always in bounds.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                ImageBGCore.IsValidEntry(rom, rom.u32(addr + 0), rom.u32(addr + 4)));

            // Main IFR: AddAddress(IFR, "BG", {0,4,8}).
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "BG",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 4, 8 }));

            bool isBG256ColorPatch = PatchDetection.HasBG256ColorPatch(rom);

            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                string name = "BG " + U.To0xHexString((int)i);
                uint tsa = rom.u32(p + 4);
                if (isBG256ColorPatch)
                {
                    if (tsa == 0)
                    {//255色画像
                        Address.AddLZ77Pointer(list, p + 0, name + " 255 color IMAGE", false, Address.DataTypeEnum.LZ77IMG);
                        Address.AddPointer(list, p + 8, 0x20 * 16, name + " PALETTE", Address.DataTypeEnum.PAL);
                        continue;
                    }
                    else if (tsa == 1)
                    {//224色画像
                        Address.AddLZ77Pointer(list, p + 0, name + " 224 color IMAGE", false, Address.DataTypeEnum.LZ77IMG);
                        Address.AddPointer(list, p + 8, 0x20 * 16, name + " PALETTE", Address.DataTypeEnum.PAL);
                        continue;
                    }
                }

                {//普通の画像
                    Address.AddLZ77Pointer(list, p + 0, name + " IMAGE", false, Address.DataTypeEnum.LZ77IMG);
                    EmitHeaderTsaPointer(rom, list, p + 4, name + " TSA");
                    Address.AddPointer(list, p + 8, 0x20 * 8, name + " PALETTE", Address.DataTypeEnum.PAL);
                }
            }
        }

        /// <summary>
        /// <c>ImageCGForm.MakeAllDataLength</c> (slice 2k; FE8 always, FE7-multibyte). A 12-byte IFR table
        /// at <c>bigcg_pointer</c> (rule <see cref="DataCountRule.NestedPointerAt"/>@0, PI <c>{0,4,8}</c>).
        /// Per entry: a 10-image-pointer array behind the <c>+0</c> pointer (ten <see cref="Address.AddLZ77Pointer"/>
        /// LZ77IMG, then a 4*10 POINTER header block via <see cref="Address.AddAddress"/>), then a
        /// HEADER-TSA at <c>+4</c> and a 0x20*8 PAL at <c>+8</c>. Reproduced VERBATIM.
        /// </summary>
        public static void EmitImageCG(ROM rom, List<Address> list)
        {
            EmitImageCGAt(rom, list, rom.RomInfo.bigcg_pointer);
        }

        /// <summary>ImageCG walk from an explicit pointer slot (test seam). See <see cref="EmitImageCG"/>.</summary>
        public static void EmitImageCGAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 12;

            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // IsDataExists = ImageCGForm.Init: +0 pointer whose target's first u32 is also a pointer.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                uint pp = rom.u32(addr + 0);
                if (!U.isPointer(pp) || !U.isSafetyPointer(pp, rom)) return false;
                uint ppOff = U.toOffset(pp);
                if (ppOff + 4 > (uint)rom.Data.Length) return false;
                uint p2 = rom.u32(ppOff);
                if (!U.isPointer(p2) || !U.isSafetyPointer(p2, rom)) return false;
                return true;
            });

            // Main IFR: AddAddress(IFR, "CG", {0,4,8}).
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "CG",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 4, 8 }));

            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                string name = "CG " + U.To0xHexString((int)i);

                // image = p32(0 + addr): the 10-image-pointer array base.
                uint image = rom.p32(p + 0);
                {
                    uint imageSPZ = image;
                    for (int n = 0; n < 10; n++, imageSPZ += 4)
                    {
                        Address.AddLZ77Pointer(list, imageSPZ, name + " IMAGE@" + n, false, Address.DataTypeEnum.LZ77IMG);
                    }
                }
                Address.AddAddress(list, image, 4 * 10, p + 0, name + " IMAGE_HEADER", Address.DataTypeEnum.POINTER);

                EmitHeaderTsaPointer(rom, list, p + 4, name + " TSA");
                Address.AddPointer(list, p + 8, 0x20 * 8, name + " PALETTE", Address.DataTypeEnum.PAL);
            }
        }

        /// <summary>
        /// <c>ImageCGFE7UForm.MakeAllDataLength</c> (slice 2k; FE7 non-multibyte ONLY). A 16-byte IFR table
        /// at <c>bigcg_pointer</c> (rule <see cref="DataCountRule.U16EqualAt"/> @2 == 0, PI <c>{4,8,12}</c>).
        /// Per entry a <c>flag = u8(+0)</c> selects: <c>flag != 1</c> -&gt; 16-color (LZ77 image @+4, HEADER-TSA
        /// @+8, 0x20*1 PAL @+12); <c>flag == 1</c> -&gt; 10-split (a 10-image-pointer array behind <c>+4</c>,
        /// a 4*10 POINTER header, HEADER-TSA @+8, 0x20*8 PAL @+12). Reproduced VERBATIM.
        /// </summary>
        public static void EmitImageCGFE7U(ROM rom, List<Address> list)
        {
            EmitImageCGFE7UAt(rom, list, rom.RomInfo.bigcg_pointer);
        }

        /// <summary>ImageCGFE7U walk from an explicit pointer slot (test seam). See <see cref="EmitImageCGFE7U"/>.</summary>
        public static void EmitImageCGFE7UAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 16;

            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // IsDataExists = ImageCGFE7UForm.Init: u16(addr+2) == 0. getBlockDataCount fires while
            // addr+16<=Length, so the +2 u16 read is always in bounds.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                rom.u16(addr + 2) == 0x00);

            // Main IFR: AddAddress(IFR, "CG", {4,8,12}).
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "CG",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 4, 8, 12 }));

            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                string name = "CG " + U.To0xHexString((int)i);
                uint flag = rom.u8(p + 0);

                uint image = rom.p32(p + 4);
                if (flag != 1)
                {//16色
                    Address.AddLZ77Pointer(list, p + 4, name + " IMAGE", false, Address.DataTypeEnum.LZ77IMG);
                    EmitHeaderTsaPointer(rom, list, p + 8, name + " TSA");
                    Address.AddPointer(list, p + 12, 0x20 * 1, name + " PALETTE", Address.DataTypeEnum.PAL);
                }
                else
                {//10分割
                    {
                        uint imageSPZ = image;
                        for (int n = 0; n < 10; n++, imageSPZ += 4)
                        {
                            Address.AddLZ77Pointer(list, imageSPZ, name + " IMAGE", false, Address.DataTypeEnum.LZ77IMG);
                        }
                    }
                    Address.AddAddress(list, image, 4 * 10, p + 4, name + " IMAGE_HEADER", Address.DataTypeEnum.POINTER);
                    EmitHeaderTsaPointer(rom, list, p + 8, name + " TSA");
                    Address.AddPointer(list, p + 12, 0x20 * 8, name + " PALETTE", Address.DataTypeEnum.PAL);
                }
            }
        }

        /// <summary>
        /// <c>WorldMapImageForm.MakeAllDataLength</c> (slice 2k; FE8 ONLY — the FE6/FE7 siblings are the
        /// slice-2e <see cref="EmitWorldMapImageFE6"/>/<see cref="EmitWorldMapImageFE7"/>). A flat sequence of
        /// the big-map BIN image + two PAL + LZ77 palettemap, the event image/tsa/palette, the mini image +
        /// PAL, two icon images + PAL, the road tile, then a 12-byte <c>WorldmapCountyBorder</c> IFR (rule
        /// <see cref="DataCountRule.TwoU32PointerAt04"/>, PI <c>{0,4}</c>) whose per-entry columns are a
        /// POINTER LZ77 @+0 and a ROMTCS @+0, and finally a 16-byte <c>WorldMapIconData</c> IFR (rule
        /// <see cref="DataCountRule.PointerAt"/> @4, PI <c>{4}</c>). Reproduced VERBATIM.
        /// </summary>
        public static void EmitWorldMapImageFE8(ROM rom, List<Address> list)
        {
            {
                uint image = rom.u32(rom.RomInfo.worldmap_big_image_pointer);
                uint palette = rom.u32(rom.RomInfo.worldmap_big_palette_pointer);
                uint dpalette = rom.u32(rom.RomInfo.worldmap_big_dpalette_pointer);

                Address.AddAddress(list, image, (uint)(480 / 2 * 320),
                    rom.RomInfo.worldmap_big_image_pointer, "worldmap_big_image", Address.DataTypeEnum.BIN);
                Address.AddAddress(list, palette, 0x20 * 4,
                    rom.RomInfo.worldmap_big_palette_pointer, "worldmap_big_palette", Address.DataTypeEnum.PAL);
                Address.AddAddress(list, dpalette, 0x20 * 4,
                    rom.RomInfo.worldmap_big_dpalette_pointer, "worldmap_big_dpalette", Address.DataTypeEnum.PAL);
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_big_palettemap_pointer,
                    "worldmap_big_palettemap", false, Address.DataTypeEnum.LZ77IMG);
            }
            {
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_event_image_pointer,
                    "worldmap_event_image", false, Address.DataTypeEnum.LZ77IMG);
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_event_tsa_pointer,
                    "worldmap_event_image", false, Address.DataTypeEnum.LZ77TSA);
                Address.AddPointer(list, rom.RomInfo.worldmap_event_palette_pointer, 0x20 * 4,
                    "worldmap_event_palette", Address.DataTypeEnum.PAL);
            }
            {
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_mini_image_pointer,
                    "worldmap_mini_image", false, Address.DataTypeEnum.LZ77IMG);
                Address.AddPointer(list, rom.RomInfo.worldmap_mini_palette_pointer, 0x20 * 1,
                    "worldmap_mini_palette", Address.DataTypeEnum.PAL);
            }
            {
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_icon1_pointer,
                    "worldmap_icon1", false, Address.DataTypeEnum.LZ77IMG);
                Address.AddPointer(list, rom.RomInfo.worldmap_icon_palette_pointer, 0x20 * 2,
                    "worldmap_icon_palette", Address.DataTypeEnum.PAL);
            }
            {
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_icon2_pointer,
                    "worldmap_icon2", false, Address.DataTypeEnum.LZ77IMG);
            }
            {
                Address.AddLZ77Pointer(list, rom.RomInfo.worldmap_road_tile_pointer,
                    "worldmap_road_tile_image", false, Address.DataTypeEnum.LZ77IMG);
            }
            EmitWorldMapCountyBorder(rom, list, rom.RomInfo.worldmap_county_border_pointer);
            EmitWorldMapIconData(rom, list, rom.RomInfo.worldmap_icon_data_pointer);
        }

        /// <summary>The WorldmapCountyBorder IFR (FE8) walk from an explicit pointer slot (test seam): a
        /// 12-byte IFR (rule <c>isPointer(u32+0) &amp;&amp; isPointer(u32+4)</c>, PI <c>{0,4}</c>); per entry
        /// a POINTER LZ77 column @+0 and a ROMTCS column @+0. See <see cref="EmitWorldMapImageFE8"/>.</summary>
        public static void EmitWorldMapCountyBorder(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 12;

            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // Border_Init rule: isPointer(u32(addr)) && isPointer(u32(addr+4)).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                U.isPointer(rom.u32(addr + 0)) && U.isPointer(rom.u32(addr + 4)));

            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "WorldmapCountyBorder",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 4 }));

            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                string name = "WorldmapCountyBorder " + U.To0xHexString((int)i);
                // WF: AddLZ77Pointer(0 + addr, name + " IMAGE", isPointerOnly, POINTER) — note the POINTER
                // DataType (a LZ77 length over a POINTER-typed block), reproduced VERBATIM.
                Address.AddLZ77Pointer(list, p + 0, name + " IMAGE", false, Address.DataTypeEnum.POINTER);
                EmitRomTcsPointer(rom, list, p + 0, name + " ROMTCS");
            }
        }

        /// <summary>The WorldMapIconData IFR (FE8) walk from an explicit pointer slot (test seam): a 16-byte
        /// IFR (rule <c>isPointer(u32+4)</c>, PI <c>{4}</c>), main IFR only (no per-entry columns). See
        /// <see cref="EmitWorldMapImageFE8"/>.</summary>
        public static void EmitWorldMapIconData(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 16;

            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // ICON_Init rule: isPointer(u32(addr+4)) (no terminator data exists otherwise).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                U.isPointer(rom.u32(addr + 4)));

            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "WorldMapIconData",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 4 }));
        }

        /// <summary>One ItemUsage usage-table descriptor (the 10 Switch2-gated usage arrays). Captures
        /// the WF <c>ReInit(n, ...)</c> case verbatim: the RomInfo data pointer, the Switch2 enable/count
        /// address, and the WF Info name "ItemUsageP&lt;n&gt;". Public so a synthetic-ROM test can supply
        /// explicit addresses (the RomInfo fields are not settable in tests).</summary>
        public sealed class ItemUsageTable
        {
            public Func<ROM, uint> Pointer;
            public Func<ROM, uint> Switch2Address;
            public string Name;
        }

        /// <summary>
        /// <c>ItemUsagePointerForm.MakeAllDataLength</c> (slice 2f). NOT a flat RomInfo pointer-slot
        /// table: the WF <c>MakeAllDataLength</c> loops the 10 usage tables, each re-initialized by
        /// <c>ReInit(n, ifr)</c> which derives its base + count from a Switch2-gated RomInfo *address*.
        /// Reproduced VERBATIM per usage <c>n</c>:
        /// <list type="bullet">
        ///   <item>pointer = <c>usage_pointer</c>; addr = <c>p32(pointer)</c>; count = <c>u8(switch2_address+2)</c>;</item>
        ///   <item>GATE: only if <c>IsSwitch2Enable(switch2_address)</c> AND <c>isSafetyOffset(addr)</c>
        ///   (else WF's <c>ReInit</c> returns <c>NOT_FOUND</c> and <c>MakeAllDataLength</c> <c>continue</c>s
        ///   — emits nothing for that usage);</item>
        ///   <item><c>ReInitPointer(pointer, count + 1)</c> sets BasePointer = pointer, BaseAddress =
        ///   p32(pointer), DataCount = <c>count + 1</c> (the count is set EXPLICITLY here, NOT via a
        ///   getBlockDataCount walk — the IFR's IsDataExists returns false);</item>
        ///   <item>main IFR Address: <c>AddAddress(ifr, "ItemUsageP"+n, {0}, InputFormRef_ASM)</c> -&gt;
        ///   length = block(4) × (DataCount + 1) = 4 × (count + 2), pointer = BasePointer;</item>
        ///   <item>then <c>AddFunctions(MakeList(), 0, " ItemUsageP"+n)</c>: one ASM AddFunction per entry
        ///   at offset 0 (block address itself), bounded by <c>MakeList()</c>'s EOF cutoff.</item>
        /// </list>
        /// The Switch2 enable byte-pattern + the count read are pure ROM reads
        /// (<see cref="ItemUsagePointerCore.IsSwitch2Enable"/>), so this is faithfully headless. The WF
        /// per-entry ASM label is the IFR display name + " ItemUsageP&lt;n&gt;"; a static label is used
        /// here (non-load-bearing for relocation, same convention as the other AsmFunction sub-walks).
        /// </summary>
        public static void EmitItemUsagePointer(ROM rom, List<Address> list)
        {
            // The 10 usage tables in WF ReInit case order (0..9). Each is a RomInfo data pointer + a
            // RomInfo Switch2 address; the WF "config file" reads in ReInit are GUI-only (the combo box)
            // and do NOT affect the produced Address list, so they are intentionally omitted here.
            ItemUsageTable[] tables = new ItemUsageTable[]
            {
                new ItemUsageTable { Pointer = r => r.RomInfo.item_usability_array_pointer,    Switch2Address = r => r.RomInfo.item_usability_array_switch2_address,    Name = "ItemUsageP0" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_effect_array_pointer,       Switch2Address = r => r.RomInfo.item_effect_array_switch2_address,       Name = "ItemUsageP1" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_promotion1_array_pointer,   Switch2Address = r => r.RomInfo.item_promotion1_array_switch2_address,   Name = "ItemUsageP2" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_promotion2_array_pointer,   Switch2Address = r => r.RomInfo.item_promotion2_array_switch2_address,   Name = "ItemUsageP3" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_staff1_array_pointer,       Switch2Address = r => r.RomInfo.item_staff1_array_switch2_address,       Name = "ItemUsageP4" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_staff2_array_pointer,       Switch2Address = r => r.RomInfo.item_staff2_array_switch2_address,       Name = "ItemUsageP5" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_statbooster1_array_pointer, Switch2Address = r => r.RomInfo.item_statbooster1_array_switch2_address, Name = "ItemUsageP6" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_statbooster2_array_pointer, Switch2Address = r => r.RomInfo.item_statbooster2_array_switch2_address, Name = "ItemUsageP7" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_errormessage_array_pointer, Switch2Address = r => r.RomInfo.item_errormessage_array_switch2_address, Name = "ItemUsageP8" },
                new ItemUsageTable { Pointer = r => r.RomInfo.item_name_article_pointer,       Switch2Address = r => r.RomInfo.item_name_article_switch2_address,       Name = "ItemUsageP9" },
            };
            EmitItemUsagePointerTables(rom, list, tables);
        }

        /// <summary>ItemUsagePointer walk from an explicit set of usage tables (test seam — lets a
        /// synthetic ROM supply the pointer/switch2 addresses without populating RomInfo, which has no
        /// public setters). See <see cref="EmitItemUsagePointer"/> for the verbatim WF reproduction.</summary>
        public static void EmitItemUsagePointerTables(ROM rom, List<Address> list, ItemUsageTable[] tables)
        {
            const uint block = 4;
            foreach (ItemUsageTable t in tables)
            {
                uint switch2Addr = t.Switch2Address(rom);
                // WF ReInit: enable-gate, then base-safety. Either failing => NOT_FOUND => continue.
                if (!ItemUsagePointerCore.IsSwitch2Enable(rom, switch2Addr))
                {
                    continue;
                }
                uint pointer = U.toOffset(t.Pointer(rom));
                // Guard the FULL 4-byte slot before p32: isSafetyOffset(pointer) alone leaves
                // pointer+1..pointer+3 unchecked, and ROM.p32 only short-circuits when pointer >=
                // Data.Length (a pointer in [Len-3, Len-1] still reaches u32 -> check_safety throws).
                // Mirrors EmitUnitFE6At / the StatusRMenu root+3 guard. On valid ROMs the RomInfo slot
                // is never near EOF, so this only hardens synthetic/corrupted ROMs.
                if (!U.isSafetyOffset(pointer + 3, rom))
                {
                    continue; // p32(pointer) below would read junk; WF's ReInitPointer->p32 needs a safe slot.
                }
                uint baseAddr = rom.p32(pointer);
                // WF ReInit: !isSafetyOffset(addr) -> NOT_FOUND -> continue (no emit for this usage).
                if (!U.isSafetyOffset(baseAddr, rom))
                {
                    continue;
                }

                // WF: count = u8(switch2_address + 2); ReInitPointer(pointer, count + 1) -> DataCount =
                // count + 1 (set EXPLICITLY, not via getBlockDataCount). The +2 read is in-bounds because
                // IsSwitch2Enable already validated the full switch2 opcode region at switch2Addr.
                uint count = rom.u8(switch2Addr + 2);
                uint dataCount = count + 1;

                // Main IFR Address: AddAddress(ifr, name, {0}, InputFormRef_ASM) ->
                // length = block * (DataCount + 1). BasePointer = the RomInfo pointer slot (safe here).
                uint length = block * (dataCount + 1);
                list.Add(new Address(baseAddr, length, pointer, t.Name,
                    Address.DataTypeEnum.InputFormRef_ASM, block, new uint[] { 0 }));

                // AddFunctions(MakeList(), 0, " " + name): one ASM AddFunction per entry at offset 0
                // (the entry block address itself). MakeList() yields DataCount entries from baseAddr,
                // stepping by block, with an EOF cutoff (addr + block > Data.Length -> stop). Mirror that
                // bound exactly so a corrupted/too-large count near EOF truncates instead of throwing
                // (AddFunction's u32 read check_safety would throw past EOF).
                for (uint i = 0; i < dataCount; i++)
                {
                    uint entryAddr = baseAddr + i * block;
                    if (entryAddr + block > (uint)rom.Data.Length)
                    {
                        break;
                    }
                    Address.AddFunction(list, entryAddr, " " + t.Name);
                }
            }
        }

        /// <summary>
        /// <c>UnitFE6Form.MakeAllDataLength</c> (slice 2f, FE6 only). NOT a flat pointer-slot table: the
        /// WF <c>Init</c> builds an IFR with BasePointer 0 and then <c>ReInit(p32(unit_pointer) +
        /// unit_datasize)</c> sets BaseAddress DIRECTLY (one block past the table start — FE6 skips unit
        /// 0). Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>base = <c>p32(unit_pointer) + unit_datasize</c>; block = <c>unit_datasize</c>;
        ///   IsDataExists = <c>i &lt; unit_maxcount</c> (FixedCount);</item>
        ///   <item>BasePointer = 0 -&gt; <c>isSafetyOffset(0)</c> false -&gt; pointer slot = NOT_FOUND
        ///   (the table is reached via the +unit_datasize computed base, NOT a plain RomInfo pointer
        ///   slot, so its pointer FIELD is intentionally untracked — matching WF's AddAddress on a
        ///   BasePointer-0 IFR);</item>
        ///   <item>main IFR Address: <c>AddAddress(ifr, "Unit", {44})</c> -&gt; length =
        ///   block × (DataCount + 1), type InputFormRef, pointerIndexes {44}.</item>
        /// </list>
        /// FLAT — no per-entry sub-walk (unlike the FE8 UnitForm's +44 support BinFixed). The per-entry
        /// embedded pointer at offset 44 is tracked via pointerIndexes {44} on the main Address (its
        /// target is relocated by the rebuild's pointer-index pass), exactly as the FE7 UnitFE7Form
        /// descriptor does.
        /// </summary>
        public static void EmitUnitFE6(ROM rom, List<Address> list)
        {
            EmitUnitFE6At(rom, list, rom.RomInfo.unit_pointer, rom.RomInfo.unit_datasize,
                rom.RomInfo.unit_maxcount);
        }

        /// <summary>UnitFE6 walk from explicit unit-pointer slot / block / maxcount (test seam — lets a
        /// synthetic ROM supply them without populating RomInfo, which has no public setters). See
        /// <see cref="EmitUnitFE6"/> for the verbatim WF reproduction.</summary>
        public static void EmitUnitFE6At(ROM rom, List<Address> list, uint rawUnitPointer, uint block, uint maxcount)
        {
            if (block == 0)
            {
                return; // a zero block would make getBlockDataCount spin; not real data.
            }

            // WF Init: ifr.ReInit(p32(unit_pointer) + unit_datasize). Guard the full pointer slot
            // before p32 (root+3) so a near-EOF unit_pointer slot skips instead of throwing.
            uint unitPointerSlot = U.toOffset(rawUnitPointer);
            if (!U.isSafetyOffset(unitPointerSlot + 3, rom))
            {
                return;
            }
            uint tableStart = rom.p32(unitPointerSlot);
            // baseAddr = tableStart + one block (FE6 skips unit 0 via the +unit_datasize ReInit).
            uint baseAddr = U.toOffset(tableStart + block);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return; // WF ReInit on an unsafe addr -> AddAddress early-returns (isSafetyOffset(addr) false).
            }

            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) => i < maxcount);

            // AddAddress(ifr, "Unit", {44}): length = block * (DataCount + 1); pointer = BasePointer = 0
            // -> NOT_FOUND (the FE6 unit table's pointer slot is intentionally untracked).
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, U.NOT_FOUND, "Unit",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 44 }));
        }

        // ===================================================================
        // slice 2h — flat IFR stragglers whose count rule needs a Core helper
        // (SupportUnit's owner-lookahead; WorldMapPath's per-entry computed-length
        // sub-blocks). Each gets a dedicated emitter + an explicit-address test
        // seam that supplies the resolved work items without RomInfo.
        // ===================================================================

        /// <summary>
        /// <c>SupportUnitForm.MakeAllDataLength</c> (FE7/FE8, block 24) and
        /// <c>SupportUnitFE6Form.MakeAllDataLength</c> (FE6, block 32) — slice 2h. Both are a PLAIN flat
        /// IFR emit (<c>AddAddress(ifr, name, {})</c>, no per-entry sub-walk), but their
        /// <c>InputFormRef.Init</c> IsDataExists is the "owner-lookahead" terminator that a flat
        /// <see cref="StructDescriptor"/> rule cannot express: continue while the first field is
        /// non-zero (<c>u16(addr)!=0</c> for FE7/8, <c>u8(addr)!=0</c> for FE6) OR — even when it is
        /// zero — any of the entry + next 3 blocks is OWNED, i.e. some unit's <c>+44</c> support
        /// pointer points at it (<c>UnitForm.GetUnitIDWhereSupportAddr != NOT_FOUND</c>, reproduced by
        /// <see cref="SupportUnitNavigation.GetUnitIdAtSupportAddr"/>). The owner lookahead is a pure
        /// ROM walk of the unit table, so this is faithfully headless.
        /// <para>This emits the SAME single IFR <see cref="Address"/> as
        /// <c>AddressWinForms.AddAddress(list, ifr, name, {})</c>: base = <c>p32(support_unit_pointer)</c>,
        /// length = <c>block × (DataCount + 1)</c>, pointer = <c>support_unit_pointer</c> (the RomInfo
        /// slot), pointerIndexes = <c>{}</c>, type InputFormRef — driven by the SAME
        /// <see cref="ROM.getBlockDataCount(uint,uint,Func{int,uint,bool})"/> every other form uses, so
        /// the count is byte-identical to WF's IFR walk.</para>
        /// </summary>
        public static void EmitSupportUnit(ROM rom, List<Address> list)
        {
            // FE7/FE8: block 24, first-field u16, Info "SupportUnit". FE6: block 32, first-field u8,
            // Info "SupportUnitFE6". The pointer slot is support_unit_pointer in all versions.
            bool fe6 = rom.RomInfo.version == 6;
            EmitSupportUnitAt(rom, list, rom.RomInfo.support_unit_pointer,
                block: fe6 ? 32u : 24u,
                firstFieldWidth: fe6 ? 1u : 2u,
                name: fe6 ? "SupportUnitFE6" : "SupportUnit");
        }

        /// <summary>SupportUnit walk from an explicit pointer slot / block / first-field width / name
        /// (test seam — lets a synthetic ROM supply them without populating RomInfo, which has no
        /// public setters). See <see cref="EmitSupportUnit"/> for the verbatim WF reproduction.</summary>
        public static void EmitSupportUnitAt(ROM rom, List<Address> list, uint rawPointer,
            uint block, uint firstFieldWidth, string name)
        {
            if (block == 0)
            {
                return; // a zero block would make getBlockDataCount spin; not real data.
            }

            // AddAddress(ifr): addr = ifr.BaseAddress = p32(toOffset(BasePointer)); pointer = BasePointer.
            // Guard the full pointer slot before p32 (root+3) so a near-EOF slot skips, not throws.
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return; // AddAddress early-returns when !isSafetyOffset(addr).
            }

            // WF SupportUnit*Form.Init IsDataExists (owner-lookahead), driven by the SAME getBlockDataCount.
            // getBlockDataCount only fires the callback while addr+block<=Length, so the first-field read
            // (u8/u16 @ addr) is always in bounds. The owner lookahead reads the unit table via
            // GetUnitIdAtSupportAddr, which is itself fully bounds-checked.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                bool firstFieldNonZero = firstFieldWidth == 1
                    ? rom.u8(addr) != 0
                    : rom.u16(addr) != 0;
                if (firstFieldNonZero)
                {
                    return true; // 0ではないのでまだデータがある.
                }
                // 飛び地になっていることがあるらしい — 4ブロックほど検索してみる (WF: n=0..3, found+=block).
                uint found = addr;
                for (int n = 0; n < 4; n++, found += block)
                {
                    if (SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, found) != null)
                    {
                        return true; // 発見!
                    }
                }
                return false; // 見つからない.
            });

            // AddAddress(ifr, name, {}): length = block × (DataCount + 1); pointer = the RomInfo slot.
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, name,
                Address.DataTypeEnum.InputFormRef, block, new uint[] { }));
        }

        /// <summary>
        /// <c>WorldMapPathForm.MakeAllDataLength</c> (FE8-only, slice 2h). A flat IFR (base
        /// <c>worldmap_road_pointer</c>, block 12, IsDataExists = <c>isPointer(u32(addr+0))</c> →
        /// PointerAt @0, pointerIndexes {0,8}) PLUS a per-entry pair of variable-length sub-blocks
        /// behind the embedded pointers at +0 and +8, whose lengths come from two pure terminator
        /// walks (<see cref="CalcPathDataLength"/> / <see cref="CalcPathMoveDataLength"/>). Those
        /// length walks are pure ROM reads (no PatchUtil/disasm/config), so they are reproduced
        /// VERBATIM here rather than in a separate Core helper. Reproduced per entry (<c>p = base +
        /// i*12</c>):
        /// <list type="bullet">
        ///   <item><c>a0 = p32(p+0)</c>; if <c>a0 &gt; 0</c>: <c>AddAddress(a0, CalcPathDataLength(a0),
        ///   p+0, "WorldMapPath:0x&lt;i&gt;", BIN)</c>;</item>
        ///   <item><c>a8 = p32(p+8)</c>; if <c>a8 &gt; 0</c>: <c>AddAddress(a8, CalcPathMoveDataLength(a8),
        ///   p+8, "WorldMapPathMove:0x&lt;i&gt;", POINTER)</c>.</item>
        /// </list>
        /// The main IFR Address uses pointerIndexes {0,8} (the two embedded pointer FIELDS are
        /// relocated by the rebuild's pointer-index pass; their DATA is relocated by the two
        /// sub-Addresses here).
        /// </summary>
        public static void EmitWorldMapPath(ROM rom, List<Address> list)
        {
            EmitWorldMapPathAt(rom, list, rom.RomInfo.worldmap_road_pointer);
        }

        /// <summary>WorldMapPath walk from an explicit pointer slot (test seam — lets a synthetic ROM
        /// supply it without populating RomInfo). See <see cref="EmitWorldMapPath"/> for the verbatim
        /// WF reproduction.</summary>
        public static void EmitWorldMapPathAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 12;

            // Main IFR (AddAddress(ifr, "WorldMapPath", {0,8})): base = p32(toOffset(slot)),
            // pointer = slot. Guard the full slot before p32 (root+3).
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // IsDataExists = isPointer(u32(addr+0)) — PointerAt @0.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                U.isPointer(rom.u32(addr + 0)));

            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "WorldMapPath",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 8 }));

            // Per-entry sub-blocks. getBlockDataCount guarantees p+block(12)<=Length for i<dataCount,
            // so p32(p+0) (deepest p+3) and p32(p+8) (deepest p+11) are both in bounds.
            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                uint a0 = rom.p32(p + 0);
                if (a0 > 0)
                {
                    Address.AddAddress(list, a0, CalcPathDataLength(rom, a0), p + 0,
                        "WorldMapPath:" + U.To0xHexString(i), Address.DataTypeEnum.BIN);
                }

                uint a8 = rom.p32(p + 8);
                if (a8 > 0)
                {
                    Address.AddAddress(list, a8, CalcPathMoveDataLength(rom, a8), p + 8,
                        "WorldMapPathMove:" + U.To0xHexString(i), Address.DataTypeEnum.POINTER);
                }
            }
        }

        /// <summary>VERBATIM port of <c>WorldMapPathForm.CalcPathDataLength</c>: walk a path-chip stream
        /// from <paramref name="addr"/> — each 4-byte header is <c>(x8,y8,count,?)</c> followed by
        /// <c>count*2</c> chip bytes; stop at the first header whose <c>x8 == 0xFF</c> (consuming that
        /// header), or when the next 4-byte header would run past a safe offset. Returns the byte length
        /// consumed (0 on an unsafe start). Pure ROM reads — every dereference is bounds-guarded.</summary>
        public static uint CalcPathDataLength(ROM rom, uint addr)
        {
            if (!U.isSafetyOffset(addr, rom))
            {
                return 0;
            }
            uint p = addr;
            while (true)
            {
                if (!U.isSafetyOffset(p + 4, rom))
                {
                    return p - addr;
                }

                uint x8 = rom.u8(p + 0);
                // y8 (p+1) and count (p+2) are read in WF; count drives the advance.
                uint count = rom.u8(p + 2);

                p += 4;
                if (x8 == 0xFF)
                {
                    return p - addr;
                }
                p += count * 2;
            }
        }

        /// <summary>VERBATIM port of <c>WorldMapPathForm.CalcPathMoveDataLength</c>: walk a u32 list from
        /// <paramref name="addr"/>, stopping at (and consuming) the first <c>0xFFFFFFFF</c> terminator,
        /// or when the next u32 would run past a safe offset. Returns the byte length consumed (0 on an
        /// unsafe start). Pure ROM reads — every dereference is bounds-guarded.</summary>
        public static uint CalcPathMoveDataLength(ROM rom, uint addr)
        {
            if (!U.isSafetyOffset(addr, rom))
            {
                return 0;
            }
            uint p = addr;
            while (true)
            {
                // WF guards only `isSafetyOffset(p)` (p < Length) before `u32(p)`, which would THROW
                // when p is in [Length-3, Length-1] (u32 needs p+4 <= Length). Guard the FULL 4-byte
                // read (root+3) so a malformed/unterminated stream near EOF returns the length consumed
                // so far instead of throwing — never changes the result on a well-formed (0xFFFFFFFF-
                // terminated) stream, where the terminator is always reached before EOF. (WF has a
                // Debug.Assert(false) on the unsafe path; headless returns p - addr.)
                if (!U.isSafetyOffset(p + 3, rom))
                {
                    return p - addr;
                }
                uint a = rom.u32(p);

                p += 4;
                if (a == 0xFFFFFFFF)
                {
                    return p - addr;
                }
            }
        }

        // ===================================================================
        // slice 2l — ItemWeaponEffectForm (version-agnostic). A flat IFR (base
        // p32(item_effect_pointer), block 16, IsDataExists = u16(addr)==0xFFFF
        // stop / i>10 && IsEmpty(addr,16*10) stop) PLUS, per entry, a PROCS
        // sub-block behind the embedded pointer at +8 whose length is the
        // PROCS-bytecode terminator walk ProcsScriptForm.CalcLengthAndCheck
        // (pure u16/u32/getString reads), reproduced VERBATIM below.
        // ===================================================================

        /// <summary>
        /// <c>ItemWeaponEffectForm.MakeAllDataLength</c> (version-agnostic, slice 2l). Reproduces the
        /// WF main IFR (<c>AddressWinForms.AddAddress(list, ItemWeaponEffectForm.Init, "ItemWeaponEffect",
        /// {8})</c>) plus the per-entry PROCS sub-block:
        /// <list type="bullet">
        ///   <item>base = <c>p32(item_effect_pointer)</c>, block 16, pointerIndexes {8}, type
        ///   InputFormRef, length = <c>16*(DataCount+1)</c>;</item>
        ///   <item>count rule (<c>ItemWeaponEffectForm.Init</c> IsDataExists) = stop when
        ///   <c>u16(addr)==0xFFFF</c>, or when <c>i>10 &amp;&amp; IsEmpty(addr,16*10)</c> — both pure ROM
        ///   reads (<c>getBlockDataCount</c> guards <c>addr+16&lt;=Length</c>, so the u16 read is always
        ///   in-bounds; <c>IsEmpty</c> is itself EOF-safe);</item>
        ///   <item>per entry <c>p = base + i*16</c>: <c>mapAnime = p32(p+8)</c>; if it is a safe offset,
        ///   <c>AddPointer(p+8, CalcProcsLengthAndCheck(mapAnime), "ItemWeaponEffect_PROC_0x&lt;itemid&gt;",
        ///   PROCS)</c> (itemid = <c>u8(p+0)</c>; the WF label's localized item NAME is dropped — it does
        ///   not affect relocation).</item>
        /// </list>
        /// The producer always scans real lengths (no isPointerOnly) and is fully bounds-guarded.
        /// </summary>
        public static void EmitItemWeaponEffect(ROM rom, List<Address> list)
        {
            EmitItemWeaponEffectAt(rom, list, rom.RomInfo.item_effect_pointer);
        }

        /// <summary>ItemWeaponEffect walk from an explicit pointer slot (test seam — lets a synthetic ROM
        /// supply it without populating RomInfo). See <see cref="EmitItemWeaponEffect"/> for the verbatim
        /// WF reproduction.</summary>
        public static void EmitItemWeaponEffectAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 16;

            // Main IFR: BasePointer = toOffset(slot), BaseAddress = p32(BasePointer). Guard the full
            // slot before p32 (root+3). Matches InputFormRef.Init (toOffset then p32, both safety-checked).
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // IsDataExists (ItemWeaponEffectForm.Init): stop at u16(addr)==0xFFFF, or i>10 &&
            // IsEmpty(addr, 16*10). getBlockDataCount guards addr+block(16)<=Length, so u16(addr) is
            // always in-bounds; IsEmpty is itself EOF-safe (returns false past EOF).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                if (rom.u16(addr) == 0xFFFF)
                {
                    return false;
                }
                if (i > 10 && rom.IsEmpty(addr, block * 10))
                {
                    return false;
                }
                return true;
            });

            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "ItemWeaponEffect",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 8 }));

            // Per-entry PROCS sub-blocks. getBlockDataCount guarantees p+block(16)<=Length for
            // i<dataCount, so p32(p+8) (deepest p+11) and u8(p+0) are both in bounds.
            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                uint mapAnime = rom.p32(p + 8);
                if (!U.isSafetyOffset(mapAnime, rom))
                {
                    continue;
                }
                uint itemid = rom.u8(p + 0);
                string procName = "ItemWeaponEffect_PROC_0x" + itemid.ToString("X");

                Address.AddPointer(list, p + 8, CalcProcsLengthAndCheck(rom, mapAnime),
                    procName, Address.DataTypeEnum.PROCS);
            }
        }

        /// <summary>VERBATIM port of <c>ProcsScriptForm.CalcLengthAndCheck</c>: walk a PROCS (map-anime)
        /// bytecode stream of 8-byte fixed instructions <c>(code:u16, sarg:u16, parg:u32)</c> from
        /// <paramref name="addr"/>, validating each opcode's argument contract and stopping at the EXIT
        /// codes (0x00 / 0x800). Returns the byte length consumed, or <see cref="U.NOT_FOUND"/> when the
        /// stream violates the contract (an odd start, an unknown opcode, or a bad arg). NOTE (verbatim
        /// WF): after advancing <c>addr += 8</c> the loop breaks on <c>if (addr + 8 &gt; limit) break;</c>
        /// BEFORE the opcode-specific contract switch, so the final 8-byte slot that sits within
        /// <c>[limit-8, limit)</c> is counted into the length WITHOUT running its contract/terminator
        /// check — i.e. the per-opcode validation covers every slot except a trailing one at EOF (WF does
        /// the same). Pure ROM reads — every dereference is bounds-guarded by the WF
        /// <c>while (addr + 8 &lt;= limit)</c> clamp (which guarantees <c>addr+7 &lt; Length</c> so the
        /// u16/u16/u32 triplet is always in-bounds), plus the inner <c>getString</c> (code 0x01) and the
        /// GOTO-0 look-ahead recursion (code 0x0C) are both themselves EOF-safe. Line-for-line faithful
        /// to the WinForms method.</summary>
        public static uint CalcProcsLengthAndCheck(ROM rom, uint addr)
        {
            if (U.IsValueOdd(addr))
            {//奇数から始まるのはどう考えてもおかしい.
                return U.NOT_FOUND;
            }

            uint start = addr;
            uint limit = (uint)rom.Data.Length;

            while (addr + 8 <= limit)
            {
                uint code = rom.u16(addr + 0);
                uint sarg = rom.u16(addr + 2);
                uint parg = rom.u32(addr + 4);

                addr += 8; //命令は8バイト固定.
                if (addr + 8 > limit)
                {
                    break;
                }
                if (code == 0x00)
                {//arg all null
                    if (sarg == 0)
                    {
                        if (parg != 0)
                        {//規約違反
                            return U.NOT_FOUND;
                        }
                    }
                    else if (sarg <= 10)
                    {//10だったときは、何か値が入ることがあるようだ
                     //例: 0000 1000 08001800
                        if (parg == 0)
                        {//規約違反
                            return U.NOT_FOUND;
                        }
                    }
                }
                else if (code == 0x10 || code == 0x11 || code == 0x12 || code == 0x13 || code == 0x15 || code == 0x17 || code == 0x19)
                {//arg all null
                    if (sarg != 0 || parg != 0)
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x01)
                {//sarg is null, parg is pointer
                    if (sarg != 0 || !U.isSafetyPointer(parg, rom))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                    //文字列参照 ASCIIである必要あり
                    string name = rom.getString(U.toOffset(parg));
                    if (!U.isAsciiString(name))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x02 || code == 0x03 || code == 0x04 || code == 0x14 || code == 0x16)
                {//sarg is null, parg is pointer
                    if (sarg != 0 || !U.isSafetyPointer(parg, rom))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                    if (U.IsValueOdd(parg) == false)
                    {//関数呼び出しなので絶対に奇数でなければならない.
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x05 || code == 0x0D)
                {//sarg is null, parg is pointer
                    if (sarg != 0 || !U.isSafetyPointer(parg, rom))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                    if (U.IsValueOdd(parg))
                    {//6C呼び出しなので絶対に偶数でなければならない.
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x06)
                {//parg is pointer, sargは1になることがあるらしい
                    if (!U.isPointer(parg))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                    if (U.IsValueOdd(parg))
                    {//6C呼び出しなので絶対に偶数でなければならない.
                        return U.NOT_FOUND;
                    }
                    //Debug.Assert(sarg == 1);
                    if (sarg >= 2)
                    {
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x07 || code == 0x08 || code == 0x09 || code == 0x0A)
                {
                    if (!U.isPointer(parg))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                    if (U.IsValueOdd(parg))
                    {//6C呼び出しなので絶対に偶数でなければならない.
                        return U.NOT_FOUND;
                    }
                    if (sarg != 0)
                    {//必ずsarg引数は0
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x0B || code == 0x0E || code == 0x0F)
                {//parg is null
                    if (parg != 0)
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x0C)
                {//parg is null
                    if (sarg == 0)
                    {//GOTO LABEL 0があった
                        //Goto 0の時だけは、pargにゴミが入るときがある.

                        //先読みをしてみる.
                        uint sakiyomi = CalcProcsLengthAndCheck(rom, addr + 8);
                        if (sakiyomi == U.NOT_FOUND)
                        {//この先が壊れているなら、自分が終端.
                            if (start == addr)
                            {//自分が終端なのに、それが最初に出てくるのはおかしいよね.
                                return U.NOT_FOUND;
                            }
                            break;
                        }
                    }
                    else if (parg != 0)
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x18)
                {// parg is pointer
                    if (!U.isSafetyPointer(parg, rom))
                    {//規約違反
                        return U.NOT_FOUND;
                    }
                }
                else if (code == 0x800)
                {//EXIT その3
                    break;
                }
                else
                {
                    return U.NOT_FOUND;
                }

                if (code == 0x00)
                {//EXIT
                    break;
                }
            }
            return addr - start;
        }

        // ===================================================================
        // slice 2s — AIScriptForm (AI1 / AI2 bytecode tables; version-agnostic
        // call site). A dedicated emitter (EmitAIScript) reproduces
        // AIScriptForm.MakeAllDataLength VERBATIM:
        //   - the main IFR per AI table (block 4, IsDataExists reproduced
        //     inline from AIScriptForm.Init, pointerIndexes {0}),
        //   - the per-table "ClonePointer" slots (AISomeByte, length 0),
        //   - the per-entry AISCRIPT block (length = CalcAIScriptLength, the
        //     verbatim AIScriptForm.CalcLength 16-byte-opcode terminator walk),
        //   - and per 16-byte AISCRIPT slot the embedded +8/+12 pointers: a
        //     thumb function (AddFunction) for an odd +8, else an AIUNITS BIN
        //     block (length = CalcAIUnitsLength, the verbatim AIUnitsForm.CalcLength
        //     u16==0 terminator walk).
        // STATIC NAME divergence (the ONLY divergence, relocation-identical — see
        // EmitItemWeaponEffect): WF labels each entry with the per-entry AI name
        // (EventUnitForm.GetAIName1/2 -> a config name list OR
        // InputFormRef.GetCommentSA, neither in Core). The Address `name` is
        // COSMETIC (does not affect addr/length/pointer/DataType/order, i.e. the
        // relocation manifest is byte-identical), so the producer uses a STATIC
        // name "AI<n> 0x<i>" — dropping ONLY the GetAIName1/2 part. The
        // length/count/pointer/DataType are reproduced byte-faithfully.
        //
        // COUNT rule: the main IFR DataCount (AIScriptForm.Init's IsDataExists)
        // caps an un-extended AI table at EventUnitForm.AI{1,2}.Count — which, at
        // the moment IsDataExists reads it, equals the number of DATA LINES in the
        // ai1_/ai2_ config TSV (PreLoadResourceAI{1,2} loads those lines BEFORE
        // padding the list up to DataCount, so the un-extended cap resolves to the
        // config-line count). The producer recomputes that count headlessly from
        // the ai1_/ai2_ config files (CountConfigDataLines, the same
        // IsComment/OtherLangLine filter PreLoadResourceAI{1,2} uses), gracefully
        // degrading to 0 when the config tree is absent.
        // ===================================================================

        /// <summary>VERBATIM port of <c>AIScriptForm.CalcLength</c>: walk the AI bytecode of
        /// 16-byte fixed instructions from <paramref name="addr"/>, stopping at the EXIT opcode
        /// (<c>u8(addr+0)==0x03</c>) UNLESS the next instruction's first byte is a 0x1B/0x1C label
        /// (then it continues past one more instruction). Returns the consumed byte length. Pure ROM
        /// reads; the WF <c>while (addr + 16 &lt;= limit)</c> clamp guarantees <c>u8(addr+0)</c> is
        /// in-bounds. EOF-equivalent to WF (a near-EOF stream simply stops at the clamp).</summary>
        public static uint CalcAIScriptLength(ROM rom, uint addr)
        {
            // A NULL/unsafe entry (a 0 table slot, or a pointer outside the ROM) must NOT trigger a full
            // scan from offset 0 + a huge computed length. WF passes p32(slot) straight into CalcLength; on
            // a valid ROM every AI entry is a real pointer so this never fires, but guarding the start makes
            // a malformed/NULL entry return 0 -> the AISCRIPT AddAddress is skipped (addr unsafe) AND the
            // downstream AIUNITS loop (end == start) doesn't run. Output-equivalent to WF on valid ROMs;
            // far cheaper + no spurious offset-0 emissions on malformed ones.
            if (!U.isSafetyOffset(addr, rom))
            {
                return 0;
            }
            uint start = addr;
            uint limit = (uint)rom.Data.Length;

            while (addr + 16 <= limit)
            {
                uint code = rom.u8(addr + 0);
                addr += 16; //命令は16バイト固定.
                if (addr + 16 > limit)
                {
                    break;
                }
                if (code == 0x03)
                {//EXIT
                    //1命令先読みして1Bラベルがあるかどうかを見るあるならまだ続く
                    uint nextcode = rom.u8(addr + 0);
                    if (!(nextcode == 0x1B || nextcode == 0x1C))
                    {//1B or 1Cではないので終わり.
                        break;
                    }
                    addr += 16;
                }
            }
            return addr - start;
        }

        /// <summary>VERBATIM port of <c>AIUnitsForm.CalcLength</c>: from <c>toOffset(addr)</c>, walk
        /// 2 bytes at a time until <c>u16==0x00</c>; returns the consumed byte length. Pure ROM reads.
        /// EOF-HARDENING: WF's loop bound is <c>addr &lt; Data.Length</c>, so the final iteration could
        /// read <c>u16(Length-1)</c> (1 byte OOB) on a non-terminated stream; the producer clamps the
        /// bound to <c>addr + 2 &lt;= Length</c> (valid-ROM-equivalent: a real AIUNITS stream is
        /// 0-terminated well before EOF, so the clamp only differs on a malformed unterminated tail).</summary>
        public static uint CalcAIUnitsLength(ROM rom, uint addr)
        {
            addr = U.toOffset(addr);
            // Same start guard as CalcAIScriptLength: the caller isPointer-checks pp, but isPointer does not
            // bound it to Data.Length, so guard an out-of-range start to avoid a scan past EOF (return 0 ->
            // the AIUNITS AddAddress is skipped). Valid-ROM-equivalent.
            if (!U.isSafetyOffset(addr, rom))
            {
                return 0;
            }
            uint start = addr;
            uint length = (uint)rom.Data.Length;
            for (; addr + 2 <= length; addr += 2)
            {
                if (rom.u16(addr) == 0x00)
                {
                    break;
                }
            }
            return addr - start;
        }

        /// <summary>
        /// Count the DATA lines of an <c>ai1_</c>/<c>ai2_</c> config TSV (the per-entry AI name list),
        /// reproducing the line filter <c>EventUnitForm.PreLoadResourceAI{1,2}</c> uses
        /// (<c>!(U.IsComment(line) || U.OtherLangLine(line))</c>) — every other line increments the count
        /// (the WF <c>sp.Length &lt;= 0</c> guard is never true). This is the un-extended-table cap the
        /// AIScript main-IFR <c>IsDataExists</c> compares <c>i</c> against (<c>AI{1,2}.Count</c>). Never
        /// throws: a missing/unconfigured config tree (null BaseDirectory, missing file) yields 0 — the
        /// same faithful headless behavior as an empty config (WF's <c>IsRequiredFileExist</c> false path
        /// leaves <c>AI{1,2}</c> empty before padding).
        /// </summary>
        static int CountConfigDataLines(string type, ROM rom)
        {
            try
            {
                string fullfilename = U.ConfigDataFilename(type, rom);
                if (!System.IO.File.Exists(fullfilename))
                {
                    return 0;
                }
                int count = 0;
                using (System.IO.StreamReader reader = System.IO.File.OpenText(fullfilename))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Verbatim PreLoadResourceAI{1,2} filter: skip comment / other-lang lines,
                        // count everything else (sp.Length <= 0 is never true after Split('\t')). Use the
                        // (line, rom) OtherLangLine overload so the is_multibyte language filter consults the
                        // passed rom, not CoreState.ROM (the producer's rom==CoreState.ROM invariant makes
                        // these identical, but the explicit rom keeps the count correct if they ever differ).
                        if (U.IsComment(line) || U.OtherLangLine(line, rom))
                        {
                            continue;
                        }
                        count++;
                    }
                }
                return count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// <c>AIScriptForm.MakeAllDataLength</c> (slice 2s, version-agnostic call site). For each of the
        /// two AI tables (<c>ai1_pointer</c>, <c>ai2_pointer</c>): skip if the slot is 0, else emit
        /// <see cref="EmitAIScriptSub"/> (the main IFR + per-entry AISCRIPT / AIUNITS sub-blocks) and
        /// <see cref="EmitAIScriptSomeByte"/> (the two ClonePointer slots). Reproduced VERBATIM; only the
        /// entry NAME is static (relocation-identical — see the slice-2s header above).
        /// </summary>
        public static void EmitAIScript(ROM rom, List<Address> list)
        {
            // WF: addlist = { ai1_pointer, ai2_pointer }.
            uint[] addlist = new uint[] { rom.RomInfo.ai1_pointer, rom.RomInfo.ai2_pointer };
            // The IsDataExists count cap reads AI{1,2}.Count == the ai{1,2}_ config-line count.
            int ai1Count = CountConfigDataLines("ai1_", rom);
            int ai2Count = CountConfigDataLines("ai2_", rom);

            for (int aiType = 0; aiType < addlist.Length; aiType++)
            {
                uint aiAddr = addlist[aiType];
                if (aiAddr == 0)
                {//WF: if (aiAddr == 0) continue;
                    continue;
                }
                EmitAIScriptSub(rom, list, aiAddr, aiType, aiType == 0 ? ai1Count : ai2Count, ai1Count, ai2Count);
                EmitAIScriptSomeByte(rom, list, aiAddr, aiType);
            }
        }

        /// <summary>VERBATIM port of <c>AIScriptForm.MakeAllDataLengthAISomeByte</c>: for slots
        /// <c>i=1,2</c> at <c>aiAddr + i*4</c>, if <c>isPointer(u32(slot))</c> emit a length-0 POINTER
        /// "ClonePointer" Address (<c>AddPointer(slot, 0, ...)</c>). EOF-HARDENING: WF reads
        /// <c>u32(aiAddr+i*4)</c> unguarded; the producer guards the 4-byte extent first.</summary>
        public static void EmitAIScriptSomeByte(ROM rom, List<Address> list, uint aiAddr, int aiType)
        {
            for (uint i = 1; i < 3; i++)
            {
                uint addr = aiAddr + (i * 4);
                // EOF-harden: u32(addr) reads addr..addr+3.
                if (addr + 4 > (uint)rom.Data.Length)
                {
                    continue;
                }
                uint p = rom.u32(addr);
                if (!U.isPointer(p))
                {
                    continue;
                }
                Address.AddPointer(list, addr, 0, "AI" + (aiType + 1) + "_ClonePointer" + i,
                    Address.DataTypeEnum.POINTER);
            }
        }

        /// <summary>VERBATIM port of <c>AIScriptForm.MakeAllDataLengthSub</c>: the main IFR
        /// (<c>AddressWinForms.AddAddress(IFR, "AI<n>", {0})</c>) followed by the per-entry AISCRIPT
        /// block + its embedded +8/+12 AIUNITS / CallASM pointers. The IFR DataCount reproduces
        /// <c>AIScriptForm.Init</c>'s <c>IsDataExists</c> inline. Only the per-entry NAME is static
        /// ("AI&lt;n&gt; 0x&lt;i&gt;") — see the slice-2s header (relocation-identical).</summary>
        static void EmitAIScriptSub(ROM rom, List<Address> list, uint aiAddr, int aiType,
            int thisCount, int ai1Count, int ai2Count)
        {
            // WF: InputFormRef.ReInitPointer(aiAddr) -> BasePointer = toOffset(aiAddr),
            // BaseAddress = p32(BasePointer). Reproduce that, then the AddressWinForms.AddAddress(IFR)
            // emission (addr = BaseAddress, length = block*(DataCount+1), pointer = BasePointer or
            // NOT_FOUND, type InputFormRef, block 4, pointerIndexes {0}).
            const uint block = 4;
            uint basePointer = U.toOffset(aiAddr);
            // InputFormRef.Init guards BasePointer; an unsafe slot -> BaseAddress 0, emit nothing.
            if (!U.isSafetyOffset(basePointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(basePointer);
            // WF Init: if !isSafetyOffset(BaseAddress) -> BaseAddress = 0, BasePointer = 0 (DataCount 0,
            // AddAddress returns without emitting). Mirror by bailing out here.
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            uint p32ai1 = ResolveAiTableBase(rom, rom.RomInfo.ai1_pointer);
            uint p32ai2 = ResolveAiTableBase(rom, rom.RomInfo.ai2_pointer);
            uint extendsBase = U.toOffset(rom.RomInfo.extends_address);

            // DataCount: getBlockDataCount(BaseAddress, 4, IsDataExists). IsDataExists reproduces
            // AIScriptForm.Init verbatim (with the AI{1,2}.Count cap == config-line count).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                uint a = rom.u32(addr); // getBlockDataCount guarantees addr+4<=Length.
                if (!U.isPointerOrNULL(a))
                {
                    return false;
                }
                // U.isExtrendsROMArea(addr) == addr >= toOffset(extends_address).
                if (addr >= extendsBase)
                {//拡張済みなのでサイズは終端まで
                }
                else
                {//未拡張
                    uint comparebase = addr - (uint)(block * i);
                    if (comparebase == p32ai1)
                    {
                        if (i >= ai1Count)
                        {
                            return false;
                        }
                    }
                    else if (comparebase == p32ai2)
                    {
                        if (i >= ai2Count)
                        {
                            return false;
                        }
                    }
                }
                return true;
            });

            // Main IFR Address (AddressWinForms.AddAddress(list, IFR, "AI<n>", {0})).
            uint mainLength = block * (dataCount + 1);
            uint mainPointer = U.isSafetyOffset(basePointer, rom) ? basePointer : U.NOT_FOUND;
            list.Add(new Address(baseAddr, mainLength, mainPointer, "AI" + (aiType + 1),
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0 }));

            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                if (!U.isSafetyOffset(p, rom))
                {
                    continue;
                }

                // STATIC NAME (the only divergence): WF appends GetAIName1/2(i) here.
                string name = "AI" + (aiType + 1) + " " + U.To0xHexString(i);

                uint aiscript = rom.p32(p);
                uint length = CalcAIScriptLength(rom, aiscript);

                Address.AddAddress(list, aiscript, length, p, name, Address.DataTypeEnum.AISCRIPT);

                uint end = aiscript + length;
                for (uint k = aiscript; k < end; k += 16)
                {
                    // EOF-harden: u32(k+8)/u32(k+12) read up to k+15. CalcAIScriptLength bounds each
                    // 16-byte block within [aiscript, aiscript+length) by addr+16<=Length, so k+15 <
                    // k+16 <= Length already holds for k < end; guard anyway for robustness.
                    if (k + 16 > (uint)rom.Data.Length)
                    {
                        break;
                    }
                    uint pp;
                    pp = rom.u32(k + 8);
                    if (U.isPointer(pp))
                    {
                        if ((pp % 2) == 1)
                        {//thumbプログラムコード
                            Address.AddFunction(list, k + 8, name + " CallASM");
                        }
                        else
                        {//データ
                            Address.AddAddress(list, pp, CalcAIUnitsLength(rom, pp), k + 8, name,
                                Address.DataTypeEnum.BIN);
                        }
                    }
                    pp = rom.u32(k + 12);
                    if (U.isPointer(pp))
                    {
                        Address.AddAddress(list, pp, CalcAIUnitsLength(rom, pp), k + 12, name,
                            Address.DataTypeEnum.BIN);
                    }
                }
            }
        }

        /// <summary>Resolve an AI table base the way <c>AIScriptForm.Init</c>'s IsDataExists does
        /// (<c>Program.ROM.p32(ai{1,2}_pointer)</c>) for the per-entry <c>baseaddr ==</c> comparison.
        /// Returns <see cref="U.NOT_FOUND"/> when the slot is an unsafe offset (so the comparison can
        /// never match — the same effect as WF reading a garbage slot, since a real table base is a safe
        /// offset).</summary>
        static uint ResolveAiTableBase(ROM rom, uint slot)
        {
            uint s = U.toOffset(slot);
            if (!U.isSafetyOffset(s + 3, rom))
            {
                return U.NOT_FOUND;
            }
            return rom.p32(s);
        }

        // ===================================================================
        // slice 2s — ImageBattleAnimeForm (battle-animation OAM tables;
        // version-agnostic call site). A dedicated emitter (EmitImageBattleAnime)
        // reproduces ImageBattleAnimeForm.MakeAllDataLength VERBATIM:
        //   PART A (per-class BattleAnimeSeting IFR): for each class
        //     (ClassForm.MakeClassList -> the class-table addrs), resolve
        //     GetBattleAnimeAddrWhereAddr (version-gated +48 FE6 / +52 FE7+FE8),
        //     and if the anime-setting addr is safe emit the "BattleAnimeSeting"
        //     main IFR (block 4, IsDataExists = u32(addr)!=0, pointerIndexes {}).
        //   PART B (the N_ animelist IFR): the "BattleAnime" main IFR (base
        //     image_battle_animelist_pointer, block 32, IsDataExists =
        //     (isPointer(u32+12)&&isPointer(u32+20)&&isPointer(u32+24)) ||
        //     (FEditorHint!=NOT_FOUND && i<FEditorHint), pointerIndexes
        //     {12,16,20,24,28}). FEditorHint = GetFEditorLengthHint (config-gated;
        //     reproduced via CoreState.Config "func_lookup_feditor", default off).
        //   PART C (per-anime OAM walk): per anime entry, ImageUtilOAM.MakeAllDataLength
        //     (EmitImageBattleAnimeOAM) — a fixed section BIN + 4 LZ77 pointers
        //     (frame / 2x OAM / palette) + the UnCompressFrame-embedded seat-image
        //     sub-walk (dedup'd across entries via a shared seatNumberList).
        // STATIC NAME divergence (the ONLY divergence, relocation-identical — see
        // EmitItemWeaponEffect): WF's per-anime ImageUtilOAM name reads
        // ROM.getString(base,12) (the anime name, needs a SystemTextEncoder); the
        // producer drops ONLY that decoded name (the `info` string is COSMETIC —
        // does not affect addr/length/pointer/DataType/order). All lengths/counts/
        // pointers/DataTypes are reproduced byte-faithfully.
        // ===================================================================

        /// <summary>The list of class-table entry addresses = <c>ClassForm.MakeClassList()</c>
        /// (<c>Init(null).MakeList()</c>). Reproduces <c>ClassForm.Init</c> VERBATIM (base
        /// <c>class_pointer</c>, block <c>class_datasize</c>, count = the <c>i==0||(i&lt;=0xff &amp;&amp;
        /// u8(addr+4)!=0)</c> rule — the same as <see cref="ClassDataCount"/>) and yields one addr per
        /// entry (<c>baseAddr + i*block</c>). The name (slot 2 of the WF IFR) is irrelevant —
        /// <c>MakeAllDataLength</c> only consumes the addrs. Returns an empty list on an unsafe/zero
        /// pointer (faithful headless behavior).</summary>
        static List<uint> MakeClassListAddrs(ROM rom)
        {
            var result = new List<uint>();
            uint pointer = U.toOffset(rom.RomInfo.class_pointer);
            if (!U.isSafetyOffset(pointer, rom)) return result;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;
            uint block = rom.RomInfo.class_datasize;
            if (block == 0) return result;
            uint count = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                if (i == 0) return true;
                if (i > 0xff) return false;
                return rom.u8(addr + 4) != 0;
            });
            uint p = baseAddr;
            for (uint i = 0; i < count; i++, p += block)
            {
                result.Add(p);
            }
            return result;
        }

        /// <summary>VERBATIM port of <c>ClassForm.GetBattleAnimeAddrWhereAddr</c>: from a class addr,
        /// the anime-setting pointer slot is at <c>+48</c> (FE6) / <c>+52</c> (FE7+FE8); returns
        /// <c>p32(slot)</c> as the anime-setting addr and reports the slot via <paramref name="outPointer"/>.
        /// EOF-HARDENING: WF reads <c>p32(addr+48/52)</c> after only <c>isSafetyOffset(addr)</c>; the
        /// producer guards the full slot extent (slot+3) so a near-EOF class addr yields NOT_FOUND.</summary>
        static uint GetBattleAnimeAddrWhereAddr(ROM rom, uint classAddr, out uint outPointer)
        {
            if (!U.isSafetyOffset(classAddr, rom))
            {
                outPointer = U.NOT_FOUND;
                return U.NOT_FOUND;
            }
            uint slot = (rom.RomInfo.version == 6) ? classAddr + 48 : classAddr + 52;
            outPointer = slot;
            if (slot + 4 > (uint)rom.Data.Length)
            {
                return U.NOT_FOUND;
            }
            return rom.p32(slot);
        }

        /// <summary>
        /// <c>ImagePortraitForm.MakeAllDataLength</c> (slice 2t, FE8 + FE7 call sites). Reproduces the
        /// WinForms <c>Init</c> walk (base <c>p32(portrait_pointer)</c>, block <c>portrait_datasize</c>, the
        /// stateful <c>nullContinuousCount</c>/<c>FEditorHint</c> IsDataExists), the main "Portrait" IFR
        /// Address (pointerIndexes <c>{0,4,8,12,16}</c>), and the per-entry <c>RecyclePortrait</c>. The
        /// per-entry FACE column has THREE branches (LZ77IMG when <c>u8(seet)==0x10</c>; a fixed IMG
        /// 0x4+0x2000 "HALFBODY" when <c>version==8 &amp;&amp; IsHalfBodyFlag(seet)</c>; else a fixed IMG
        /// 0x4+0x1000), and the PAL column emits at a DIFFERENT offset/length/type for halfbody (+0 len 0x40
        /// IMG) vs normal (+8 len 0x20 PAL) — neither expressible by the flat SubWalk loop, hence a dedicated
        /// emitter. The per-entry <c>info</c> labels use a static "Portrait:0x.." prefix (WF appends a
        /// getString-decoded name — cosmetic / relocation-identical; the <c>ItemWeaponEffect</c> precedent).
        /// EOF-safe: the IsDataExists reads (+0/+4/+8) are bounded by getBlockDataCount's
        /// <c>addr+blocksize&lt;=Length</c> guard (block 20 &gt; +8+4), and every per-entry Add* helper
        /// re-checks pointer safety. The producer is always a defragment scan, so LZ77 lengths are computed
        /// (the WF <c>isPointerOnly</c> flag is fixed to <c>false</c>, matching every sibling emitter).
        /// </summary>
        public static void EmitImagePortrait(ROM rom, List<Address> list)
        {
            EmitPortraitTable(rom, list, "Portrait",
                new uint[] { 0, 4, 8, 12, 16 }, nullLimit: 1000, RecyclePortrait);
        }

        /// <summary>
        /// <c>ImagePortraitFE6Form.MakeAllDataLength</c> (slice 2t, FE6 call site). Same shape as
        /// <see cref="EmitImagePortrait"/> but: the main IFR is "PortraitFE6" with pointerIndexes
        /// <c>{0,4,8}</c>; the null-run terminator cap is <b>10</b> (not 1000); and the per-entry
        /// <c>RecyclePortrait</c> is the simpler FE6 form (FACE = LZ77IMG @+0; MAP FACE = fixed IMG @+4 len
        /// <c>mapface(32×32)/2 = 0x200</c>; PAL = fixed PAL @+8 len 0x20 — no header-byte branch, no
        /// halfbody). Reproduced VERBATIM (pure-ROM; the FE6 <c>IsHalfBodyFlag</c> path does not exist).
        /// </summary>
        public static void EmitImagePortraitFE6(ROM rom, List<Address> list)
        {
            EmitPortraitTable(rom, list, "PortraitFE6",
                new uint[] { 0, 4, 8 }, nullLimit: 10, RecyclePortraitFE6);
        }

        /// <summary>Shared driver for the three ImagePortrait* forms: reproduce the WinForms
        /// <c>InputFormRef.Init</c> walk (the stateful nullContinuousCount + FEditorHint IsDataExists), emit
        /// the main IFR Address (length = block × (count+1), pointer = portrait_pointer if safe else
        /// NOT_FOUND — matching <c>AddressWinForms.AddAddress</c>), then run the per-form per-entry recycle.
        /// The <paramref name="nullLimit"/> is the form's null-run cutoff (FE7/FE8 = 1000, FE6 = 10).</summary>
        static void EmitPortraitTable(ROM rom, List<Address> list, string mainName,
            uint[] pointerIndexes, int nullLimit, Action<ROM, List<Address>, string, uint> recycle)
        {
            uint basePointer = rom.RomInfo.portrait_pointer;
            uint block = rom.RomInfo.portrait_datasize;
            // block == 0 would make getBlockDataCount spin (addr += 0); a zero datasize is a descriptor
            // bug, not data — bail (WF's InputFormRef never has a 0 BlockSize here).
            if (block == 0)
            {
                return;
            }
            // baseAddr = p32(portrait_pointer). ROM.p32 returns 0 for addr >= Data.Length but the underlying
            // u32 throws when portrait_pointer is within 3 bytes of EOF; full-extent-guard the read and fall
            // back to p32's own 0-return (a 0 baseAddr -> getBlockDataCount returns 0, AddAddress bails).
            uint basePointerOffset = U.toOffset(basePointer);
            uint baseAddr = (basePointerOffset + 4 <= (uint)rom.Data.Length) ? rom.p32(basePointer) : 0;

            // InputFormRef.Init: FEditorHint = GetFEditorLengthHint(p32(portrait_pointer)); the per-walk
            // nullContinuousCount is captured per call (a closure over a local, exactly as WF's lambda).
            uint feditorHint = GetFEditorLengthHint(rom, baseAddr);
            int nullContinuousCount = 0;
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                // VERBATIM ImagePortraitForm.Init IsDataExists (FE6 differs only in nullLimit).
                if (i <= 0)
                {
                    return true;
                }
                // 0/4/8 がポインタであればデータがあると考える. (the deepest read is u32(addr+8) = addr+8..+11,
                // so block >= 12 guarantees it is in bounds — getBlockDataCount bounds addr+block <= Length.
                // All variants satisfy this: FE6 portrait_datasize=16, FE8/FE7=20.)
                uint u0 = rom.u32(addr + 0);
                uint u4 = rom.u32(addr + 4);
                uint u8 = rom.u32(addr + 8);
                if (U.isPointerOrNULL(u0) && U.isPointerOrNULL(u4) && U.isPointerOrNULL(u8))
                {
                    if (u0 == 0 && u4 == 0 && u8 == 0)
                    {//NULLデータ. 怪しいがとりあえずOK
                        nullContinuousCount++;
                        if (nullContinuousCount >= nullLimit)
                        {//NULLデータが連続して nullLimit 個出てきたら打ち切る.
                            return false;
                        }
                    }
                    else
                    {
                        nullContinuousCount = 0;
                    }
                    return true;
                }
                if (feditorHint != U.NOT_FOUND && i < feditorHint)
                {//不明なデータではあるがFEditorがあるというので信用する.
                    nullContinuousCount = 0;
                    return true;
                }
                return false;
            });

            // AddressWinForms.AddAddress: addr = BaseAddress; length = block*(count+1); pointer =
            // BasePointer if safe else NOT_FOUND. AddAddress emits the main IFR ONLY when baseAddr is a safe
            // offset (it bails otherwise); the per-entry RecyclePortrait loop in MakeAllDataLength runs
            // REGARDLESS (over the same DataCount). Reproduced VERBATIM (an unsafe baseAddr yields a 0-count
            // walk -> no per-entry emits anyway, but the ordering matches WF exactly).
            if (U.isSafetyOffset(baseAddr, rom))
            {
                uint length = block * (dataCount + 1);
                uint pointer = U.isSafetyOffset(basePointer, rom) ? basePointer : U.NOT_FOUND;
                list.Add(new Address(baseAddr, length, pointer, mainName,
                    Address.DataTypeEnum.InputFormRef, block, pointerIndexes));
            }

            // Per-entry recycle, over the SAME dataCount (WF loops i<DataCount, addr += BlockSize).
            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                string name = "Portrait:" + U.To0xHexString(i);
                recycle(rom, list, name, p);
            }
        }

        /// <summary>VERBATIM port of <c>ImagePortraitForm.IsHalfBodyFlag</c>: a portrait sheet is a
        /// half-body extended sheet iff <c>u32(seet) == 0x00200400</c>. Pure-ROM; the dereference is
        /// full-extent-guarded (WF guards <c>isSafetyOffset(seet+4)</c> too).</summary>
        static bool IsHalfBodyFlag(ROM rom, uint unitFace)
        {
            unitFace = U.toOffset(unitFace);
            if (!U.isSafetyOffset(unitFace + 4, rom))
            {
                return false;
            }
            uint faceHeader = rom.u32(unitFace);
            return (faceHeader == 0x00200400);
        }

        /// <summary>VERBATIM port of <c>ImagePortraitForm.RecyclePortrait</c> (FE7/FE8): the per-entry
        /// FACE/MAP-FACE/PAL/MOUTH/CLASS-CARD columns behind the portrait entry's +0/+4/+8/+12/+16 pointer
        /// fields. FACE is LZ77 / IMG / HALFBODY by header byte; PAL emits at +0 (IMG 0x40) on halfbody or
        /// +8 (PAL 0x20) otherwise. (parts_width=32, parts_height=16 -> MOUTH = (32/2)*16*6 = 0x600.)
        /// The producer is always a defragment scan, so the WF <c>isPointerOnly</c> LZ77 arg is fixed
        /// <c>false</c> (real getCompressedSize lengths), matching every sibling emitter.</summary>
        static void RecyclePortrait(ROM rom, List<Address> list, string basename, uint portraitAddr)
        {
            const bool isPointerOnly = false;
            const uint parts_width = 8 * 4;
            const uint parts_height = 8 * 2;
            if (!U.isSafetyOffset(portraitAddr, rom))
            {
                return;
            }
            uint seetImage = rom.p32(portraitAddr + 0);
            uint mapFace = rom.p32(portraitAddr + 4);
            uint paletteFace = rom.p32(portraitAddr + 8);
            uint mouthFace = rom.p32(portraitAddr + 12);
            uint classFace = rom.p32(portraitAddr + 16);
            bool isHalfBodyExtends = false;

            if (U.isSafetyOffset(seetImage, rom))
            {
                isHalfBodyExtends = (rom.RomInfo.version == 8 && IsHalfBodyFlag(rom, seetImage));

                uint header00 = rom.u8(seetImage);
                if (header00 == 0x10)
                {//圧縮ヘッダがるので圧縮されてる
                    Address.AddLZ77Pointer(list, portraitAddr + 0, basename + "FACE",
                        isPointerOnly, Address.DataTypeEnum.LZ77IMG);
                }
                else if (isHalfBodyExtends)
                {//HalfBody
                    Address.AddPointer(list, portraitAddr + 0, 0x4 + 0x2000,
                        basename + "FACE HALFBODY", Address.DataTypeEnum.IMG);
                }
                else
                {//無圧縮 FE7 FE8 FE8U
                    Address.AddPointer(list, portraitAddr + 0, 0x4 + 0x1000,
                        basename + "FACE", Address.DataTypeEnum.IMG);
                }
            }
            if (U.isSafetyOffset(mapFace, rom))
            {
                Address.AddLZ77Pointer(list, portraitAddr + 4, basename + "MAP FACE",
                    isPointerOnly, Address.DataTypeEnum.LZ77IMG);
            }
            if (U.isSafetyOffset(paletteFace, rom))
            {
                if (isHalfBodyExtends)
                {//HalfBody
                    Address.AddPointer(list, portraitAddr + 0, 0x40,
                        basename + "PAL HALFBODY", Address.DataTypeEnum.IMG);
                }
                else
                {
                    Address.AddPointer(list, portraitAddr + 8, 0x20,
                        basename + "PAL", Address.DataTypeEnum.PAL); //16色パレット
                }
            }
            if (U.isSafetyOffset(mouthFace, rom))
            {
                Address.AddPointer(list, portraitAddr + 12, (parts_width / 2) * parts_height * 6,
                    basename + "MOUTH", Address.DataTypeEnum.IMG);
            }
            if (U.isSafetyOffset(classFace, rom))
            {
                Address.AddLZ77Pointer(list, portraitAddr + 16, basename + "CLASS CARD",
                    isPointerOnly, Address.DataTypeEnum.LZ77IMG);
            }
        }

        /// <summary>VERBATIM port of <c>ImagePortraitFE6Form.RecyclePortrait</c>: the FE6 per-entry columns
        /// behind +0/+4/+8 (FACE = LZ77IMG; MAP FACE = fixed IMG @+4 len <c>mapface(32×32)/2 = 0x200</c>;
        /// PAL = fixed PAL @+8 len 0x20). No header-byte branch, no halfbody. The producer is always a
        /// defragment scan, so the WF <c>isPointerOnly</c> LZ77 arg is fixed <c>false</c>.</summary>
        static void RecyclePortraitFE6(ROM rom, List<Address> list, string basename, uint portraitAddr)
        {
            const bool isPointerOnly = false;
            const uint mapface_width = 4 * 8;
            const uint mapface_height = 4 * 8;
            if (!U.isSafetyOffset(portraitAddr, rom))
            {
                return;
            }
            uint a0 = rom.p32(portraitAddr + 0);
            uint a4 = rom.p32(portraitAddr + 4);
            uint a8 = rom.p32(portraitAddr + 8);
            //顔画像は圧縮されている.
            if (U.isSafetyOffset(a0, rom))
            {
                Address.AddLZ77Pointer(list, portraitAddr + 0, basename + "FACE",
                    isPointerOnly, Address.DataTypeEnum.LZ77IMG);
            }
            if (U.isSafetyOffset(a4, rom))
            {
                Address.AddPointer(list, portraitAddr + 4, mapface_width * mapface_height / 2,
                    basename + "MAP FACE", Address.DataTypeEnum.IMG); //(/2は16色のため)
            }
            if (U.isSafetyOffset(a8, rom))
            {
                Address.AddPointer(list, portraitAddr + 8, 0x20,
                    basename + "PAL", Address.DataTypeEnum.PAL); //16色パレット
            }
        }

        /// <summary>VERBATIM port of <c>ImageItemIconForm.GetIconMax</c>: the item-icon SHEET count.
        /// Returns 0xFE when the icon table has been repointed (<c>p32(icon_pointer) !=
        /// icon_orignal_address</c>); on FE7U (version 7, non-multibyte) probes the hardcoded FEditorAdv
        /// AutoPatch magic at <c>u32(0xCB51A)</c> — if it equals <c>0x18404902</c> the autopatch occupies one
        /// icon slot, so the count is <c>icon_orignal_max - 1</c>; otherwise returns <c>icon_orignal_max</c>.
        /// Pure-ROM; both raw reads are EOF-hardened (a near-EOF / tiny synthetic ROM never throws — the WF
        /// reads are unconditional but only reached on a real FE7U ROM large enough to hold them).</summary>
        static uint GetIconMax(ROM rom)
        {
            // ImageItemIconForm.GetIconMax: repoint check `p32(icon_pointer) != icon_orignal_address`.
            // ROM.p32 already toOffsets + short-circuits addr >= Data.Length to 0, but the underlying u32
            // throws when icon_pointer is within 3 bytes of EOF; full-extent-guard the read and fall back to
            // p32's own 0-return so the comparison VALUE is identical to WF (0 vs icon_orignal_address).
            uint iconPointerOffset = U.toOffset(rom.RomInfo.icon_pointer);
            uint iconValue = (iconPointerOffset + 4 <= (uint)rom.Data.Length)
                ? rom.p32(rom.RomInfo.icon_pointer) : 0;
            if (iconValue != rom.RomInfo.icon_orignal_address)
            {//リポイント済み
                return 0xFE;
            }
            if (rom.RomInfo.version == 7)
            {
                if (rom.RomInfo.is_multibyte == false)
                {//FE7Uでは、アイテムアイコンの中にFEditorAdv AutoPatchのデータがある
                    // The WF magic addr 0xCB51A is unconditional; EOF-guard it for tiny synthetic ROMs.
                    if (0xCB51A + 4 <= (uint)rom.Data.Length)
                    {
                        uint code = rom.u32(0xCB51A);
                        if (code == 0x18404902)
                        {//そのため、FE7UでFEditorAdv AutoPatchがあれば、個数は一つ下げる
                            return rom.RomInfo.icon_orignal_max - 1;
                        }
                    }
                }
            }

            return rom.RomInfo.icon_orignal_max;
        }

        /// <summary>VERBATIM port of <c>InputFormRef.GetFEditorLengthHint</c>: the FEditor-Adv list-length
        /// hint stored 4 bytes BEFORE <paramref name="dataOffset"/>. Gated on the config option
        /// <c>func_lookup_feditor</c> (default "0" = None -> always NOT_FOUND; the same default the WF
        /// OptionForm reads); when enabled, returns <c>u32(dataOffset-4)</c> if it is in <c>[100, 1024)</c>,
        /// else NOT_FOUND. Pure ROM read + a CoreState.Config lookup (the config is set in the real app /
        /// CLI; a null Config degrades to the default-off path).</summary>
        static uint GetFEditorLengthHint(ROM rom, uint dataOffset)
        {
            // OptionForm.lookup_feditor() == (lookup_feditor_enum)atoi(Config.at("func_lookup_feditor","0"));
            // Lookup == 1. Default "0" (None) -> do NOT use the hint.
            string cfg = CoreState.Config?.at("func_lookup_feditor", "0") ?? "0";
            if (U.atoi(cfg) != 1)
            {
                return U.NOT_FOUND;
            }
            if (!U.isSafetyOffset(dataOffset - 4, rom))
            {
                return U.NOT_FOUND;
            }
            uint value = rom.u32(dataOffset - 4);
            if (value >= 1024)
            {//大きすぎる
                return U.NOT_FOUND;
            }
            if (value < 100)
            {//小さすぎる、別の値では？
                return U.NOT_FOUND;
            }
            return value;
        }

        /// <summary>
        /// <c>ImageBattleAnimeForm.MakeAllDataLength</c> (slice 2s, version-agnostic call site). See the
        /// slice-2s header for the three-part structure. Reproduced VERBATIM; the ONLY divergence is the
        /// per-anime OAM `info` string (WF decodes the anime name, the producer uses a static name —
        /// relocation-identical).
        /// </summary>
        public static void EmitImageBattleAnime(ROM rom, List<Address> list)
        {
            const uint classBlock = 4; // ImageBattleAnimeForm.Init BlockSize (per-class BattleAnimeSeting).

            // ---- PART A: per-class "BattleAnimeSeting" IFRs ----
            List<uint> classList = MakeClassListAddrs(rom);
            for (uint cid = 0; cid < classList.Count; cid++)
            {
                uint pointer;
                uint classAddr = classList[(int)cid];
                uint addr = GetBattleAnimeAddrWhereAddr(rom, classAddr, out pointer);
                if (!U.isSafetyOffset(addr, rom))
                {//WF: if (!isSafetyOffset(addr)) continue;
                    continue;
                }

                // WF: InputFormRef.ReInitPointer(pointer) -> BasePointer = toOffset(pointer),
                // BaseAddress = p32(BasePointer), DataCount from Init's IsDataExists (u32(addr)!=0).
                uint basePointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(basePointer + 3, rom))
                {
                    continue;
                }
                uint baseAddr = rom.p32(basePointer);
                if (!U.isSafetyOffset(baseAddr, rom))
                {//Init: unsafe BaseAddress -> 0/0 -> AddAddress emits nothing.
                    continue;
                }
                uint dataCount = rom.getBlockDataCount(baseAddr, classBlock, (i, a) =>
                {//ImageBattleAnimeForm.Init IsDataExists: u32(a+0) != 0. getBlockDataCount guards a+4<=Length.
                    return rom.u32(a + 0) != 0;
                });

                string selfname = "BattleAnimeSeting:" + U.To0xHexString(cid);
                uint length = classBlock * (dataCount + 1);
                uint mainPointer = U.isSafetyOffset(basePointer, rom) ? basePointer : U.NOT_FOUND;
                // WF AddAddress(IFR, name, new uint[] { }) -> pointerIndexes empty.
                list.Add(new Address(baseAddr, length, mainPointer, selfname,
                    Address.DataTypeEnum.InputFormRef, classBlock, new uint[] { }));
            }

            // ---- PART B: the N_ "BattleAnime" animelist IFR ----
            const uint nBlock = 32; // N_Init BlockSize.
            uint nBasePointer = U.toOffset(rom.RomInfo.image_battle_animelist_pointer);
            if (!U.isSafetyOffset(nBasePointer + 3, rom))
            {
                return;
            }
            uint nBaseAddr = rom.p32(nBasePointer);
            if (!U.isSafetyOffset(nBaseAddr, rom))
            {
                return;
            }

            // N_Init: FEditorHint = GetFEditorLengthHint(p32(image_battle_animelist_pointer)); if it is
            // >= 0xFF (too big) it is discarded.
            uint feditorHint = GetFEditorLengthHint(rom, nBaseAddr);
            if (feditorHint >= 0xFF)
            {//余りにでかいヒントは信じない
                feditorHint = U.NOT_FOUND;
            }

            uint nDataCount = rom.getBlockDataCount(nBaseAddr, nBlock, (i, a) =>
            {//N_Init IsDataExists. getBlockDataCount guards a+32<=Length, so u32(a+12/20/24) is in range.
                if (U.isPointer(rom.u32(a + 12))
                    && U.isPointer(rom.u32(a + 20))
                    && U.isPointer(rom.u32(a + 24)))
                {
                    return true;
                }
                if (feditorHint != U.NOT_FOUND && i < feditorHint)
                {//不明なデータではあるがFEditorがあるというので信用する.
                    return true;
                }
                return false;
            });

            uint nLength = nBlock * (nDataCount + 1);
            uint nMainPointer = U.isSafetyOffset(nBasePointer, rom) ? nBasePointer : U.NOT_FOUND;
            list.Add(new Address(nBaseAddr, nLength, nMainPointer, "BattleAnime",
                Address.DataTypeEnum.InputFormRef, nBlock, new uint[] { 12, 16, 20, 24, 28 }));

            // ---- PART C: per-anime OAM walk (ImageUtilOAM.MakeAllDataLength) ----
            // 戦闘アニメーションはlz77圧縮の中にポインタがある特殊形式です — the seatNumberList dedups
            // shared seat images ACROSS anime entries (a seat image referenced by 2 animes is emitted once).
            var seatNumberList = new List<uint>(256);
            uint walkAddr = nBaseAddr;
            for (int i = 0; i < nDataCount; i++, walkAddr += nBlock)
            {
                if (!U.isSafetyOffset(12 + walkAddr + 4, rom))
                {//WF: if (!isSafetyOffset(12 + addr + 4)) break;
                    break;
                }
                uint section = rom.p32(12 + walkAddr);
                if (!U.isSafetyOffset(section, rom))
                {//WF: if (!isSafetyOffset(section)) break;
                    break;
                }
                string selfname = "BattleAnime:" + U.To0xHexString((uint)(i + 1));
                EmitImageBattleAnimeOAM(rom, list, selfname, walkAddr, seatNumberList);
            }
        }

        /// <summary>VERBATIM port of <c>ImageUtilOAM.MakeAllDataLength</c> (the producer always scans real
        /// lengths, isPointerOnly==false). For one anime at <paramref name="battleanimeBaseaddress"/>:
        /// a fixed <c>0xC*4</c>-byte section BIN @ +12, four LZ77 pointers (frame @ +16, 2× OAM @ +20/+24,
        /// palette @ +28), then the UnCompressFrame-decompressed frame stream's embedded seat-image
        /// pointers (each a 0x86-tagged 4-byte field at +4 of a 12-byte record) emitted as
        /// BATTLEFRAMEIMG LZ77 addresses, dedup'd via the shared <paramref name="seatNumberList"/>.
        /// STATIC NAME (the only divergence): WF prefixes the info with ROM.getString(base,12) — the
        /// producer drops that decoded anime name (cosmetic / relocation-identical).
        /// EOF-HARDENING: the <c>isSafetyZArray(base+32-1)</c> guard (WF) covers the +12..+31 header reads;
        /// the frame-stream loop is bounded by the decompressed-array length + an explicit i+8 ZArray check.</summary>
        public static void EmitImageBattleAnimeOAM(ROM rom, List<Address> list, string info,
            uint battleanimeBaseaddress, List<uint> seatNumberList)
        {
            // WF: if (!isSafetyZArray(base + 32 - 1)) return; — guards the +12..+31 p32 reads.
            if (!U.isSafetyZArray(battleanimeBaseaddress + 32 - 1, rom.Data))
            {
                return;
            }

            // WF prefixes info with " " + getString(base,12) (the anime name) before the per-column
            // " section"/" frame"/... suffixes. STATIC-NAME divergence: drop the decoded name entirely
            // (cosmetic / relocation-identical) and append the suffixes directly to the passed-in name.
            uint sectionData = rom.p32(battleanimeBaseaddress + 12); //セクションデータ 固定長
            uint frameData_offset = rom.p32(battleanimeBaseaddress + 16);
            // (rightToLeftOAM / leftToRightOAM / palettes offsets at +20/+24/+28 are read by the
            //  AddLZ77Pointer calls below; WF reads them into locals only for the magic-motion path.)

            // 解凍する. 固定長のsectionData以外はLZ77で圧縮されている.
            byte[] frameData_UZ = UnCompressBattleFrame(rom, frameData_offset);

            Address.AddAddress(list, sectionData, 0xC * 4, battleanimeBaseaddress + 12,
                info + " section", Address.DataTypeEnum.BIN);
            Address.AddLZ77Pointer(list, battleanimeBaseaddress + 16, info + " frame", false,
                Address.DataTypeEnum.BATTLEFRAME);
            Address.AddLZ77Pointer(list, battleanimeBaseaddress + 20, info + " rightToLeftOAM", false,
                Address.DataTypeEnum.BATTLEOAM);
            Address.AddLZ77Pointer(list, battleanimeBaseaddress + 24, info + " leftToRightOAM", false,
                Address.DataTypeEnum.BATTLEOAM);
            Address.AddLZ77Pointer(list, battleanimeBaseaddress + 28, info + " palettes", false,
                Address.DataTypeEnum.LZ77PAL);

            int number = 0;
            for (int i = 0; i < frameData_UZ.Length; i += 4)
            {
                if (!U.isSafetyZArray((uint)(i + 8), frameData_UZ))
                {
                    break;
                }
                if (frameData_UZ[i + 3] != 0x86)
                {
                    continue;
                }
                //シート画像をリサイクルリストに突っ込む.
                uint imageOffset = U.u32(frameData_UZ, (uint)(i + 4));
                if (U.isPointer(imageOffset))
                {
                    imageOffset = U.toOffset(imageOffset);
                    if (seatNumberList.IndexOf(imageOffset) < 0)
                    {
                        //lz77された中にあるのでポインタは存在しない.
                        Address.AddLZ77Address(list, imageOffset, U.NOT_FOUND,
                            info + " seat" + number, false, Address.DataTypeEnum.BATTLEFRAMEIMG);
                        number++;
                        seatNumberList.Add(imageOffset);
                    }
                }
                i = i + 4 + 4; // 4+4+ 4 = 12
            }
        }

        /// <summary>VERBATIM port of <c>ImageUtilOAM.UnCompressFrame</c>: decompress a battle-anime frame
        /// stream. If the frame pointer is an UnHuffman-patch pointer, read the raw (uncompressed) frame
        /// bytes (length = <see cref="CalcUnCompressFrameLength"/>); else LZ77-decompress. Returns an empty
        /// array on an out-of-range / undecompressable pointer (LZ77.decompress and getBinaryData are
        /// EOF-safe). All deps are Core (FETextEncode / LZ77 / getBinaryData).</summary>
        static byte[] UnCompressBattleFrame(ROM rom, uint frameAddr)
        {
            //FEの戦闘アニメフレームはlz77で圧縮されています。
            //無圧縮フレームというデータ構造もある.
            if (FETextEncode.IsUnHuffmanPatchPointer(frameAddr))
            {
                frameAddr = FETextEncode.ConvertUnHuffmanPatchToPointer(frameAddr);
                frameAddr = U.toOffset(frameAddr);
                uint length = CalcUnCompressFrameLength(rom, frameAddr);
                return rom.getBinaryData(frameAddr, length);
            }
            else
            {
                frameAddr = U.toOffset(frameAddr);
                return LZ77.decompress(rom.Data, frameAddr);
            }
        }

        /// <summary>VERBATIM port of <c>ImageUtilOAM.CalcUnCompressFrameLength</c>: walk an uncompressed
        /// frame stream of 4-byte records from <paramref name="frameDataOffset"/>, counting 0x80-terminated
        /// frames (up to 0xC), skipping 0x86 image records (+8) and 0x85 / unknown records (+0). A 1MB
        /// limiter caps a runaway. EOF-HARDENING: WF's <c>frameData[i+3]</c> read is bounded only by
        /// <c>i &lt; limitter = min(start+1MB, Length)</c>, so on a non-terminated tail it could read up to
        /// <c>Length+2</c> (OOB); the producer clamps the loop bound to <c>i + 4 &lt;= limitter</c>
        /// (valid-ROM-equivalent: a real frame stream is 0x80-terminated well within the ROM, so the clamp
        /// only differs on a malformed unterminated tail).</summary>
        public static uint CalcUnCompressFrameLength(ROM rom, uint frameDataOffset)
        {
            //圧縮されていないデータなので、事故防止のため リミッターをかける.
            uint limitter = frameDataOffset + 1024 * 1024; //1MBサーチしたらもうあきらめる.
            limitter = (uint)Math.Min(limitter, rom.Data.Length);
            byte[] frameData = rom.Data;
            uint i;
            uint frameCount = 0;
            for (i = frameDataOffset; i + 4 <= limitter; i += 4)
            {
                if (frameData[i + 3] == 0x80)
                {//終端データ
                    frameCount++;
                    i += 4;
                    if (frameCount > 0xC)
                    {//アニメはフレーム0xCまである
                        break;
                    }
                    continue;
                }
                else if (frameData[i + 3] != 0x86)
                {
                    if (frameData[i + 3] == 0x85)
                    {
                        continue;
                    }
                    //不明な命令.
                    continue;
                }
                i += 8;
            }
            return i - frameDataOffset;
        }

        // ===================================================================
        // slice 2m — now-tractable subsystem forms: Huffman text (TextForm) +
        // the FE8U Gaiden-style spell-menu (FE8SpellMenuExtendsForm). Both are
        // dedicated emitters: TextForm's main IFR has a custom multi-branch
        // IsDataExists + a text_recover_address ReInit fallback and a per-entry
        // Huffman/UnHuffman length sub-walk; FE8SpellMenuExtends resolves its base
        // via a patch-signature scan (FE8SpellMenuPatchScanner) and emits a
        // NestedIfr per entry. (TextCharCodeForm is a flat U8NotEqual descriptor
        // in BuildBatchDescriptors.)
        // ===================================================================

        /// <summary>
        /// <c>TextForm.MakeAllDataLength</c> (slice 2m, version-agnostic call site). The Huffman text
        /// pointer table. Reproduces <c>TextForm.Init</c> + the per-entry length expansion VERBATIM:
        /// <list type="bullet">
        ///   <item>Main IFR: base = <c>p32(text_pointer)</c> (or, when that is an unsafe offset,
        ///   <c>text_recover_address</c> — the WF <c>Init</c> ReInit fallback for a broken text pointer),
        ///   block 4, pointer = the <c>text_pointer</c> slot, type
        ///   <see cref="Address.DataTypeEnum.TEXTPOINTERS"/>, pointerIndexes <c>{0}</c>. IsDataExists =
        ///   <c>isPointer(u32(addr)) || FETextEncode.IsUnHuffmanPatchPointer(u32(addr)) ||
        ///   TextForm.Is_RAMPointerArea(u32(addr))</c> (reproduced via <see cref="IsTextEntryExists"/>).</item>
        ///   <item>Per entry <c>i</c> at slot <c>arlistAddr = base + i*4</c>: read
        ///   <c>paddr = u32(arlistAddr)</c>. If <c>IsUnHuffmanPatchPointer(paddr)</c> -&gt; the BIN block at
        ///   <c>toOffset(ConvertUnHuffmanPatchToPointer(paddr))</c> with length =
        ///   <c>FETextDecode.UnHffmanPatchDecode(addr, out size)</c>; else if <c>isPointer(paddr)</c> -&gt;
        ///   the BIN block at <c>toOffset(paddr)</c> with length =
        ///   <c>FETextDecode.huffman_decode(addr, out size)</c>. Both via
        ///   <c>Address.AddAddress(list, addr, size, arlistAddr, "Text " + ToHexString(i), BIN)</c>
        ///   VERBATIM. (The WF <c>Is_RAMPointerArea</c> branch in IsDataExists has no matching AddAddress
        ///   in the per-entry loop — a RAM-pointer slot counts toward DataCount but its target lives in RAM,
        ///   not ROM, so nothing is relocated. Reproduced exactly: counted, not emitted.)</item>
        /// </list>
        /// The length helpers (<c>huffman_decode</c> / <c>UnHffmanPatchDecode</c>) are CALLED directly
        /// (both are headless Core methods, batch 10) — not reproduced. They are both internally
        /// EOF-hardened (each <c>isSafetyOffset</c>-checks the addr and guards every raw read against
        /// <c>Data.Length</c>) and return a truncated size near EOF rather than throwing. They DO decode
        /// the string into text (via <c>SystemTextEncoder</c>) as a side effect of computing the size, so
        /// when no encoder is loaded the producer falls back to a pointer-only emit (size 0) — exactly the
        /// WF <c>isPointerOnly</c> branch (which sets <c>size = 0</c> but still emits the AddAddress). A
        /// pointer-only emit still relocates the text-pointer slot AND records the target addr; the wiring
        /// slice's <see cref="ProducerResult.IsComplete"/> gate already refuses a partial scan, so a
        /// no-encoder run is never fed to a real defragment.
        /// </summary>
        public static void EmitText(ROM rom, List<Address> list)
        {
            EmitTextAt(rom, list, rom.RomInfo.text_pointer);
        }

        /// <summary>TextForm walk from an explicit <c>text_pointer</c> slot (test seam — lets a synthetic
        /// ROM supply it without populating RomInfo). See <see cref="EmitText"/> for the verbatim WF
        /// reproduction.</summary>
        public static void EmitTextAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 4;

            // Main IFR: BasePointer = toOffset(slot), BaseAddress = p32(BasePointer). Guard the full
            // slot before p32 (root+3). Matches InputFormRef.Init (toOffset then p32, both safety-checked).
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                // WF TextForm.Init: a broken text pointer ReInits to text_recover_address.
                baseAddr = U.toOffset(rom.RomInfo.text_recover_address);
                if (!U.isSafetyOffset(baseAddr, rom))
                {
                    return;
                }
            }

            // IsDataExists (TextForm.Init): isPointer(u32(addr)) || IsUnHuffmanPatchPointer(u32(addr)) ||
            // Is_RAMPointerArea(u32(addr)). getBlockDataCount guards addr+block(4)<=Length, so u32(addr)
            // is always in-bounds.
            uint dataCount = rom.getBlockDataCount(baseAddr, block,
                (i, addr) => IsTextEntryExists(rom, rom.u32(addr)));

            // AddressWinForms.AddAddress(list, IFR, "Text", {0}, TEXTPOINTERS): length = block*(count+1).
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "Text",
                Address.DataTypeEnum.TEXTPOINTERS, block, new uint[] { 0 }));

            // Per-entry expansion. getBlockDataCount guarantees addr+block(4)<=Length for i<dataCount,
            // so u32(arlistAddr) (deepest arlistAddr+3) is in bounds. The decoders compute the size only
            // when an encoder is loaded (they decode the string as a side effect of sizing); without one
            // the WF isPointerOnly path is taken (size 0). The slot is relocated either way.
            bool hasEncoder = CoreState.SystemTextEncoder != null;
            FETextDecode textdecoder = hasEncoder ? new FETextDecode(rom, CoreState.SystemTextEncoder) : null;

            uint arlistAddr = baseAddr;
            for (uint i = 0; i < dataCount; i++, arlistAddr += block)
            {
                uint paddr = rom.u32(arlistAddr);
                if (FETextEncode.IsUnHuffmanPatchPointer(paddr))
                {//un-huffman patch?
                    uint unhuffmanAddr = U.toOffset(FETextEncode.ConvertUnHuffmanPatchToPointer(paddr));
                    int size = 0;
                    if (hasEncoder)
                    {
                        // The decoders throw FETextException on a broken mask/tree (malformed ROM). The
                        // producer must NOT abort the whole run — fall back to size 0 (the slot is still
                        // relocated + target recorded, matching the no-encoder isPointerOnly path).
                        try { textdecoder.UnHffmanPatchDecode(unhuffmanAddr, out size); }
                        catch (FETextDecode.FETextException) { size = 0; }
                    }
                    Address.AddAddress(list, unhuffmanAddr, (uint)size, arlistAddr,
                        "Text " + U.ToHexString(i), Address.DataTypeEnum.BIN);
                }
                else if (U.isPointer(paddr))
                {
                    uint addr = U.toOffset(paddr);
                    int size = 0;
                    if (hasEncoder)
                    {
                        try { textdecoder.huffman_decode(addr, out size); }
                        catch (FETextDecode.FETextException) { size = 0; }
                    }
                    Address.AddAddress(list, addr, (uint)size, arlistAddr,
                        "Text " + U.ToHexString(i), Address.DataTypeEnum.BIN);
                }
                // else (Is_RAMPointerArea-only): counted toward DataCount but target is in RAM — WF emits
                // no AddAddress for it, so neither do we (nothing to relocate in ROM).
            }
        }

        /// <summary>VERBATIM port of the <c>TextForm.Init</c> IsDataExists predicate + the
        /// <c>TextForm.Is_RAMPointerArea</c> helper it calls: a text-table slot value <paramref name="p"/>
        /// "exists" when it is a ROM pointer, an un-Huffman patch pointer, OR a RAM-pointer-area value
        /// (<c>is_03RAMPointer || IsUnHuffmanPatch_IW_RAMPointer || is_02RAMPointer ||
        /// IsUnHuffmanPatch_EW_RAMPointer</c>). All pure value tests (no ROM read), so EOF-safe.</summary>
        public static bool IsTextEntryExists(ROM rom, uint p)
        {
            if (U.isPointer(p))
            {
                return true;
            }
            if (FETextEncode.IsUnHuffmanPatchPointer(p))
            {//海外改造によくある unHuffman patch
                return true;
            }
            // TextForm.Is_RAMPointerArea(p): RAM-resident text data.
            return U.is_03RAMPointer(p)
                || FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(p)
                || U.is_02RAMPointer(p)
                || FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(p);
        }

        // =====================================================================================
        // slice 2p: OAM / battle-anime length forms (version-agnostic call sites)
        // =====================================================================================

        /// <summary>
        /// <c>ImageMapActionAnimationForm.MakeAllDataLength</c> (slice 2p, version-agnostic call site;
        /// internally gated by <see cref="FindMapActionAnimationPointer"/>). The map-attack animation
        /// table: a main IFR (base = <c>p32(AnimeP)</c>, block 8, IsDataExists =
        /// <c>isSafetyPointerOrNull(u32(addr))</c>, pointerIndexes {0}) plus, for each entry <c>i</c> from
        /// 1 (the WF loop SKIPS entry 0 — the empty 00 slot) to <c>DataCount</c>, a
        /// <see cref="EmitMapActionRecycleOldAnime"/> expansion when the entry pointer
        /// <c>p32(animeBaseAddress)</c> is a safe offset. Reproduced VERBATIM (the entry-0 skip, the
        /// per-entry <c>!isSafetyOffset(addr) -&gt; continue</c>). The per-entry RecycleOldAnime length is a
        /// pure-ROM 12-byte-record terminator walk (no OAM-length calc, no decompress, no System.Drawing).
        /// The WF IFR name lambda reads a config file (<c>MapActionAnimation_</c>) — names are
        /// non-load-bearing for relocation, so the producer uses the WF per-entry
        /// <c>"MapActionAnime:0x&lt;i&gt;"</c> label directly (matching the WF RecycleOldAnime call site).
        /// EOF-safe (all reads guard the full extent; the IFR walk is <c>getBlockDataCount</c>-bounded).
        /// </summary>
        public static void EmitImageMapActionAnimation(ROM rom, List<Address> list)
        {
            // WF FindAnimationPointer(): NOT_FOUND on a ROM without the map-action-anime table.
            uint animeP = FindMapActionAnimationPointer(rom);
            if (animeP == U.NOT_FOUND)
            {
                return;
            }
            EmitImageMapActionAnimationAt(rom, list, animeP);
        }

        /// <summary>MapActionAnimation emission from the resolved table pointer <paramref name="animeP"/>
        /// (test seam — lets a synthetic ROM supply the pointer slot directly without planting the
        /// version-gated signature <see cref="FindMapActionAnimationPointer"/> greps for). See
        /// <see cref="EmitImageMapActionAnimation"/> for the verbatim WF reproduction. <paramref name="animeP"/>
        /// is the 4-byte pointer slot whose <c>p32</c> is the anime-table base (WF
        /// <c>InputFormRef Init(null, AnimeP)</c> BasePointer).</summary>
        public static void EmitImageMapActionAnimationAt(ROM rom, List<Address> list, uint animeP)
        {
            const uint block = 8; // ImageMapActionAnimationForm.Init BlockSize.

            // WF Init: BasePointer = animePointer, BaseAddress = p32(animePointer). Guard the full 4-byte
            // slot (animeP+3) before p32 so a near-EOF slot skips rather than throws.
            uint pointer = U.toOffset(animeP);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            // WF AddAddress early-returns when !isSafetyOffset(BaseAddress); the per-entry loop also needs
            // a safe base to walk.
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // Init IsDataExists = (addr+4 <= Length) && isSafetyPointerOrNull(u32(addr)). getBlockDataCount
            // already guards addr+block(8) <= Length, so the addr+4 read is always in-bounds; the explicit
            // WF length check is subsumed and harmless to reproduce.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
            {
                if (addr + 4 > (uint)rom.Data.Length)
                {
                    return false;
                }
                return U.isSafetyPointerOrNull(rom.u32(addr));
            });

            // Main IFR: AddAddress(IFR, "MapActionAnimation", {0}) -> length = 8 * (DataCount + 1),
            // pointer = BasePointer (safe here), pointerIndexes {0}.
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "MapActionAnimation",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0 }));

            // Per-entry expansion. WF: animeBaseAddress += BlockSize (skip empty 00), then for i=1..DataCount.
            uint animeBaseAddress = baseAddr + block; // WF "skip empty 00"
            for (uint i = 1; i < dataCount; i++, animeBaseAddress += block)
            {
                // WF reads p32(animeBaseAddress). getBlockDataCount bounded i<DataCount to addr+8<=Length, so
                // the 4-byte p32 read is in-bounds; guard anyway for robustness on a corrupted DataCount.
                if (animeBaseAddress + 4 > (uint)rom.Data.Length)
                {
                    break;
                }
                uint addr = rom.p32(animeBaseAddress);
                if (!U.isSafetyOffset(addr, rom))
                {
                    continue; // WF: !isSafetyOffset(addr) -> continue.
                }
                string name = "MapActionAnime:" + U.To0xHexString(i) + " ";
                EmitMapActionRecycleOldAnime(rom, list, addr, name);
            }
        }

        /// <summary>
        /// VERBATIM port of WF <c>ImageUtilMapActionAnimation.RecycleOldAnime</c>: walk 12-byte records
        /// from <paramref name="animeAddress"/> until a record with <c>u32(n) == 0 &amp;&amp; p32(n+4) == 0</c>;
        /// per record emit an LZ77 OBJ image pointer at <c>n+4</c> and a fixed 0x20-byte PAL at <c>n+8</c>;
        /// then emit the main record-table IFR (base = <paramref name="animeAddress"/>, block 12, count =
        /// <c>(n - animeAddress) / 12</c>, pointerIndexes {4,8}). All reads are pure ROM; the 1MB limiter is
        /// reproduced (a never-terminating stream past the limiter emits NO IFR — the WF
        /// <c>n &gt;= limitter -&gt; return</c> guard). EOF-safe: every read guards its full extent.
        /// </summary>
        public static void EmitMapActionRecycleOldAnime(ROM rom, List<Address> list, uint animeAddress, string basename)
        {
            animeAddress = U.toOffset(animeAddress);
            if (!U.isSafetyOffset(animeAddress, rom))
            {
                return;
            }

            uint dataLen = (uint)rom.Data.Length;
            // WF limitter = animeAddress + 1MB, clamped to Data.Length.
            uint limitter = animeAddress + 1024 * 1024;
            if (limitter > dataLen)
            {
                limitter = dataLen;
            }

            uint n = animeAddress;
            bool terminated = false; // true once the 0/0 terminator record is seen (WF emits the IFR only then).
            for (; n < limitter; n += 12)
            {
                // WF reads u32(n) and p32(n+4) directly (it relies on the limiter clamp leaving room). Those
                // reads reach n+7, so a full-extent EOF guard is needed to never throw on a stream whose
                // limiter == Data.Length. This does NOT change the IFR-emission decision (which is driven by
                // the `terminated` flag below): an un-terminated stream that runs off the end leaves
                // `terminated == false`, exactly like the WF `n >= limitter -> return` path.
                if (n + 8 > dataLen)
                {
                    break;
                }
                uint term1 = rom.u32(n);
                uint imgOffset = rom.p32(n + 4);
                if (term1 == 0 && imgOffset == 0)
                {
                    terminated = true;
                    break;
                }

                // OBJ image (LZ77) at n+4.
                Address.AddLZ77Pointer(list, n + 4, basename + "OBJ", false, Address.DataTypeEnum.LZ77IMG);
                // Palette (0x20 = 16 colors * 2 bytes) at n+8.
                Address.AddPointer(list, n + 8, 0x20, basename + "PAL", Address.DataTypeEnum.PAL);
            }
            if (!terminated)
            {
                // WF: hit the limiter / ran off the data without a 0/0 terminator -> no IFR (unsafe to reuse).
                return;
            }

            // WF: ifr.ReInit(anime_address, (n - anime_address)/12); AddAddress(ifr, basename, {4,8}).
            // ReInit sets BaseAddress = anime_address, BasePointer = NOT_FOUND (ReInit has no pointer),
            // DataCount = (n - anime_address)/12. AddAddress length = block * (DataCount + 1).
            uint count = (n - animeAddress) / 12;
            uint length = 12 * (count + 1);
            list.Add(new Address(animeAddress, length, U.NOT_FOUND, basename,
                Address.DataTypeEnum.InputFormRef, 12, new uint[] { 4, 8 }));
        }

        /// <summary>
        /// VERBATIM port of WF <c>ImageMapActionAnimationForm.FindAnimationPointerLow</c>: a version-gated
        /// (<c>is_multibyte</c> FE8J vs FE8U) 20-byte signature <see cref="U.GrepEnd"/> from
        /// <c>compress_image_borderline_address</c>; on a hit, back up to the pointer slot
        /// (<c>p - bin.Length - 4</c>) and require it to be a ROM pointer. Returns
        /// <see cref="U.NOT_FOUND"/> when not found / not a pointer. Pure ROM + RomInfo (no caching — the
        /// WF static cache is a GUI concern).
        /// </summary>
        public static uint FindMapActionAnimationPointer(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            {
                return U.NOT_FOUND;
            }

            byte[] bin;
            if (rom.RomInfo.is_multibyte)
            {//FE8J
                bin = new byte[] { 0x54, 0x3C, 0x08, 0x08, 0xEC, 0xE1, 0x03, 0x02, 0xE8, 0xA4, 0x03, 0x02, 0x68, 0xA5, 0x03, 0x02, 0xFF, 0xFF, 0x00, 0x00 };
            }
            else
            {//FE8U
                bin = new byte[] { 0x14, 0x19, 0x08, 0x08, 0xF0, 0xE1, 0x03, 0x02, 0xEC, 0xA4, 0x03, 0x02, 0x6C, 0xA5, 0x03, 0x02, 0xFF, 0xFF, 0x00, 0x00 };
            }
            uint p = U.GrepEnd(rom.Data, bin, rom.RomInfo.compress_image_borderline_address, 0, 4, 0, true);
            if (p == U.NOT_FOUND)
            {
                return U.NOT_FOUND;
            }
            p = p - (uint)bin.Length - 4;
            // WF reads u32(p) after the grep — GrepEnd's needPointer already guaranteed a pointer-or-null at
            // (p + bin.Length + 4), i.e. 4 bytes past the grep end; p itself is bin.Length+4 before that, so
            // p..p+3 is in-bounds whenever the grep end was. Guard explicitly to never throw on a corrupt ROM.
            if (p + 4 > (uint)rom.Data.Length)
            {
                return U.NOT_FOUND;
            }
            uint a = rom.u32(p);
            if (!U.isPointer(a))
            {
                return U.NOT_FOUND;
            }
            return p;
        }

        /// <summary>
        /// <c>ImageMagicFEditorForm.MakeAllDataLength</c> (slice 2p, version-agnostic; gated on the
        /// FEditorAdv magic engine). Emits: the main spell IFR (base = <c>magic_effect_pointer</c>, block 4,
        /// DataCount = <c>GetSpellDataCount</c>, pointerIndexes {0}); the <c>Magic_Append_SpellTable</c>
        /// pointer block (<c>csaSpellTable</c>, length <c>DataCount * 4 * 5</c>, pointer
        /// <c>csaSpellTablePointer</c>); and, per spell whose <c>p32(addr) == dimaddr || no_dimaddr</c>, the
        /// <see cref="EmitMagicRecycleOldAnime"/> expansion (the FEditorAdv 28-byte-record variant). All
        /// gate/count dependencies (<see cref="ImageUtilMagicCore.SearchMagicSystem"/> /
        /// <see cref="ImageUtilMagicCore.FindCSASpellTable"/> / <see cref="ImageUtilMagicCore.GetSpellDataCount"/>)
        /// are headless Core helpers. Reproduced VERBATIM.
        /// </summary>
        public static void EmitImageMagicFEditor(ROM rom, List<Address> list)
        {
            EmitImageMagicCommon(rom, list, ImageUtilMagicCore.MagicSystem.FEditorAdv);
        }

        /// <summary>
        /// <c>ImageMagicCSACreatorForm.MakeAllDataLength</c> (slice 2p, version-agnostic; gated on the
        /// CsaCreator magic engine). Structurally identical to <see cref="EmitImageMagicFEditor"/> but
        /// gated on CSA_CREATOR and using the CSA 32-byte-record RecycleOldAnime variant (which adds the
        /// extra BG-TSA LZ77 pointer at <c>+28</c> and tags the frame block
        /// <see cref="Address.DataTypeEnum.MAGICFRAME_CSA"/>). Reproduced VERBATIM.
        /// </summary>
        public static void EmitImageMagicCsaCreator(ROM rom, List<Address> list)
        {
            EmitImageMagicCommon(rom, list, ImageUtilMagicCore.MagicSystem.CsaCreator);
        }

        /// <summary>Shared body of <see cref="EmitImageMagicFEditor"/> / <see cref="EmitImageMagicCsaCreator"/>
        /// — the two WF forms' <c>MakeAllDataLength</c> are byte-for-byte identical apart from the
        /// <paramref name="system"/> gate and the per-entry RecycleOldAnime variant (FEditorAdv 28-byte vs
        /// CSA 32-byte). Reproduces the WF logic VERBATIM.</summary>
        static void EmitImageMagicCommon(ROM rom, List<Address> list, ImageUtilMagicCore.MagicSystem system)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            {
                return;
            }

            // WF: if SearchMagicSystem(...) != <system> return. (The Core SearchMagicSystem also requires the
            // CSA spell table to be findable — same posture as the WF cache, which keeps scanning otherwise.)
            uint baseaddr, dimaddr, no_dimaddr;
            if (ImageUtilMagicCore.SearchMagicSystem(rom, out baseaddr, out dimaddr, out no_dimaddr) != system)
            {
                return;
            }

            uint spellDataCount = ImageUtilMagicCore.GetSpellDataCount(rom);
            uint csaSpellTablePointer;
            uint csaSpellTable = ImageUtilMagicCore.FindCSASpellTable(rom, system, out csaSpellTablePointer);
            if (csaSpellTable == U.NOT_FOUND)
            {
                return; // WF: csaSpellTable == NOT_FOUND -> return.
            }

            // WF Init: BasePointer = magic_effect_pointer, BaseAddress = p32(magic_effect_pointer), block 4,
            // IsDataExists = (csaSpellTable != NOT_FOUND) && i < spellDataCount && i < 0xFE. The lambda does
            // NOT read the ROM, so DataCount = min(spellDataCount, 0xFE). GetSpellDataCount caps at 0xFD, so
            // DataCount == spellDataCount; reproduce the cap exactly via getBlockDataCount for parity.
            uint magicPointer = rom.RomInfo.magic_effect_pointer;
            uint pointer = U.toOffset(magicPointer);
            // Guard the FULL 4-byte slot (pointer+3) before p32 — ROM.p32 reads a u32 and would throw on a
            // near-EOF slot (matches the MapAction emitter's pointer+3 guard; on valid ROMs the RomInfo
            // slot is always in-bounds).
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return; // WF AddAddress early-returns when the base/pointer is unsafe.
            }
            uint baseAddress = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddress, rom))
            {
                return;
            }
            const uint block = 4;
            uint dataCount = rom.getBlockDataCount(baseAddress, block,
                (i, addr) => i < spellDataCount && i < 0xFE);

            // Main IFR: AddAddress(IFR, "Magic", {0}) -> length 4 * (DataCount + 1).
            uint mainLength = block * (dataCount + 1);
            list.Add(new Address(baseAddress, mainLength, pointer, "Magic",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0 }));

            // Append spell table: AddAddress(list, csaSpellTable, DataCount*4*5, csaSpellTablePointer,
            // "Magic_Append_SpellTable", MAGIC_APPEND_SPELLTABLE).
            Address.AddAddress(list, csaSpellTable, dataCount * 4 * 5, csaSpellTablePointer,
                "Magic_Append_SpellTable", Address.DataTypeEnum.MAGIC_APPEND_SPELLTABLE);

            // Per-entry: for i in [0, DataCount), addr = baseAddress + 4*i; dataaddr = p32(addr); skip 0;
            // if dataaddr == dimaddr || no_dimaddr -> RecycleOldAnime(csaSpellTable + 20*i).
            bool isCsa = system == ImageUtilMagicCore.MagicSystem.CsaCreator;
            uint addrEntry = baseAddress;
            for (uint i = 0; i < dataCount; i++, addrEntry += block)
            {
                uint csaaddress = csaSpellTable + (20 * i);

                // getBlockDataCount bounded i<DataCount to addrEntry+4<=Length, so the p32 read is in-bounds.
                uint dataaddr = rom.p32(addrEntry);
                if (dataaddr == 0)
                {
                    continue;
                }
                if (dataaddr == dimaddr || dataaddr == no_dimaddr)
                {
                    string name = "Magic:" + U.To0xHexString(i);
                    EmitMagicRecycleOldAnime(rom, list, csaaddress, name, isCsa);
                }
            }
        }

        /// <summary>
        /// VERBATIM port of WF <c>ImageUtilMagicFEditor.RecycleOldAnime</c> (<paramref name="isCsa"/> false)
        /// / <c>ImageUtilMagicCSACreator.RecycleOldAnime</c> (<paramref name="isCsa"/> true). Walks the
        /// uncompressed magic frame-data array behind <c>magic_baseaddress + 0</c> (each 0x86 record is
        /// 28 bytes for FEditorAdv, 32 for CSA — the CSA record also carries a BG-TSA LZ77 pointer at +28),
        /// accumulates the max OBJ/BG OAM index, then (when the frame walk terminated inside the 1MB
        /// limiter) emits the FRAME block plus four OAM blocks whose lengths are
        /// <see cref="CalcMagicOamLength"/>. The four OAM pointers are emitted UNCONDITIONALLY (matching WF —
        /// even a 0-length <see cref="CalcMagicOamLength"/> result is emitted; <see cref="Address.AddPointer"/>
        /// itself drops a slot whose dereferenced target is not a safe pointer). EOF-safe (every read guards
        /// its full extent; the 1MB limiter bounds the frame walk).
        /// </summary>
        public static void EmitMagicRecycleOldAnime(ROM rom, List<Address> list, uint magicBaseAddress, string basename, bool isCsa)
        {
            uint dataLen = (uint)rom.Data.Length;

            // WF reads p32(base+0) and u32(base+4/8/12/16) unconditionally up front. Normalize the base to
            // an offset (baseOff) and use it for BOTH the bounds guard AND every read + pointer-slot below,
            // so a GBA-pointer magicBaseAddress cannot slip past the guard or mis-record a slot (in practice
            // the base is already an offset, so baseOff == magicBaseAddress). Guard base+19 (the furthest,
            // base+16+3) so a near-EOF base skips rather than throws.
            uint baseOff = U.toOffset(magicBaseAddress);
            if (baseOff + 20 > dataLen)
            {
                return;
            }
            uint frameData_offset = rom.p32(baseOff + 0);
            uint objRtoL = rom.u32(baseOff + 4);   // OBJ OAM
            uint objLtoR = rom.u32(baseOff + 8);   // OBJ OAM
            uint bgRtoL  = rom.u32(baseOff + 12);  // BG OAM
            uint bgLtoR  = rom.u32(baseOff + 16);  // BG OAM

            if (frameData_offset == 0)
            {
                return;
            }
            // WF does NOT toOffset frameData_offset (it is already a ROM offset via p32); but p32 returns a
            // GBA pointer. WF p32 returns the raw u32; the frame walk indexes Program.ROM.Data directly with
            // it, so it MUST be an offset. WF p32 in this codebase returns toOffset'd value — reproduce by
            // toOffset here (no-op if already an offset). Guard against an out-of-range base.
            uint frameDataOff = U.toOffset(frameData_offset);
            if (!U.isSafetyOffset(frameDataOff, rom))
            {
                return;
            }

            // WF limitter = frameData_offset + 1MB, clamped to Data.Length.
            uint limitter = frameDataOff + 1024 * 1024;
            if (limitter > dataLen)
            {
                limitter = dataLen;
            }

            uint recordStride = isCsa ? 32u : 28u; // CSA = 32 bytes/record, FEditorAdv = 28.
            byte[] d = rom.Data;
            uint maxObjOAM = 0;
            uint maxBGOAM = 0;
            uint i;
            for (i = frameDataOff; i < limitter; i += 4)
            {
                // WF reads d[i+1] and d[i+3] (the terminator/command bytes) — guard i+3 < limitter so the
                // 4-byte stride read never crosses the clamp. Hitting this is NOT a real 0x80 terminator —
                // the stream ran out of bytes (truncated/past-EOF), so push i past the limiter to trigger
                // the post-loop "over the limiter -> do NOT reuse" guard (no FRAME/OAM emitted from a
                // truncated stream).
                if (i + 4 > limitter)
                {
                    i = limitter + 1;
                    break;
                }
                if (d[i + 3] == 0x80)
                {//終端データ
                    if (d[i + 1] == 0x01) //0x00 0x01 0x00 0x80 may continue.
                    {
                        continue;
                    }
                    i += 4;
                    break;
                }
                else if (d[i + 3] != 0x86)
                {
                    if (d[i + 3] == 0x85)
                    {
                        continue;
                    }
                    //unknown command.
                    break;
                }

                // A 0x86 record spans recordStride bytes (the deepest field is +24/+28 + 4). Guard it before
                // emitting the per-record columns (WF relies on the limiter; harden against a record that
                // straddles the clamp).
                if (i + recordStride > dataLen)
                {
                    break;
                }

                // OBJ image (LZ77) at i+4.
                Address.AddLZ77Pointer(list, i + 4, basename + "OBJ", false, Address.DataTypeEnum.LZ77IMG);
                // BG image (LZ77) at i+16.
                Address.AddLZ77Pointer(list, i + 16, basename + "BG", false, Address.DataTypeEnum.LZ77IMG);
                // OBJ palette (0x20 bytes) at i+20.
                Address.AddPointer(list, i + 20, 0x20, basename + "OBJ PAL", Address.DataTypeEnum.PAL);
                // BG palette (0x20 bytes) at i+24.
                Address.AddPointer(list, i + 24, 0x20, basename + "BG PAL", Address.DataTypeEnum.PAL);
                if (isCsa)
                {
                    // BG TSA (LZ77) at i+28 — CSA ONLY.
                    Address.AddLZ77Pointer(list, i + 28, basename + "TSA", false, Address.DataTypeEnum.LZ77TSA);
                }

                // Max OAM index from i+8 / i+12.
                uint objOAM = U.u32(d, i + 8);
                uint bgOAM = U.u32(d, i + 12);
                if (objOAM > maxObjOAM) maxObjOAM = objOAM;
                if (bgOAM > maxBGOAM) maxBGOAM = bgOAM;

                // WF advances i by recordStride-4 here (the outer loop's += 4 completes the stride).
                i += recordStride - 4;
            }

            if (i > limitter)
            {
                // WF: over the limiter -> do NOT reuse frame/OAM (unsafe).
                return;
            }

            // FRAME block: AddPointer(base+0, i - frameData_offset, "...FRAME", MAGICFRAME_*).
            Address.DataTypeEnum frameType = isCsa
                ? Address.DataTypeEnum.MAGICFRAME_CSA
                : Address.DataTypeEnum.MAGICFRAME_FEITORADV;
            Address.AddPointer(list, baseOff + 0, i - frameDataOff, basename + "FRAME", frameType);

            // Four OAM blocks (emitted UNCONDITIONALLY, matching WF — the length may be 0). The WF per-block
            // names differ slightly between FEditorAdv and CSA; reproduce each VERBATIM. Pointer slots use
            // baseOff (the ROM offset) to stay consistent with the bounds guard + reads above.
            Address.AddPointer(list, baseOff + 4, CalcMagicOamLength(rom, objRtoL, maxObjOAM),
                basename + "RihtToLeftOAM", Address.DataTypeEnum.MAGICOAM);
            Address.AddPointer(list, baseOff + 8, CalcMagicOamLength(rom, objLtoR, maxObjOAM),
                basename + "LeftRightOAM", Address.DataTypeEnum.MAGICOAM); // FEditorAdv + CSA share this name
            Address.AddPointer(list, baseOff + 12, CalcMagicOamLength(rom, bgRtoL, maxBGOAM),
                basename + (isCsa ? "RihtToLeftOAMBG" : "OBJ OAM"), Address.DataTypeEnum.MAGICOAM);
            Address.AddPointer(list, baseOff + 16, CalcMagicOamLength(rom, bgLtoR, maxBGOAM),
                basename + (isCsa ? "LeftRightOAMBG" : "BG OAM"), Address.DataTypeEnum.MAGICOAM);
        }

        /// <summary>
        /// VERBATIM port of WF <c>ImageUtilMagicFEditor.calcOAMLength(duumyOAMoffset, maxOAM)</c>: from
        /// <c>toOffset(duumyOAMoffset) + maxOAM</c>, scan forward in 12-byte steps until a step whose
        /// leading u32 is <c>0x01</c> (the terminal frame), returning the byte length from the OAM base to
        /// the end of that step. Returns 0 when the offset is 0 or the scan overruns the limiter
        /// (<c>off + maxOAM + 2048</c>, clamped to Data.Length). EOF-safe (the u32 read is guarded by the
        /// limiter; never throws).
        /// </summary>
        public static uint CalcMagicOamLength(ROM rom, uint duumyOAMoffset, uint maxOAM)
        {
            duumyOAMoffset = U.toOffset(duumyOAMoffset);
            if (duumyOAMoffset == 0)
            {
                return 0;
            }
            uint dataLen = (uint)rom.Data.Length;
            uint limitter = duumyOAMoffset + maxOAM + 2048; // give up after a 2K search.
            if (limitter > dataLen)
            {
                limitter = dataLen;
            }

            uint oam = duumyOAMoffset + maxOAM;
            while (true)
            {
                // WF: if (oam >= limitter) return 0. Also guard the 4-byte u32 read so it never crosses the
                // clamp (WF's limitter is already <= Data.Length, but oam can be limitter-1..limitter-1+3).
                if (oam >= limitter || oam + 4 > dataLen)
                {//dangerous — ran past ROM end.
                    return 0;
                }
                uint a = rom.u32(oam);
                oam += 12;
                if (a == 0x01)
                {//terminal frame
                    break;
                }
            }
            return oam - duumyOAMoffset;
        }

        /// <summary>
        /// <c>FE8SpellMenuExtendsForm.MakeAllDataLength</c> (slice 2m; FE8U ONLY — gated at the WF
        /// <c>version == 8 &amp;&amp; !is_multibyte</c> call site, alongside OPClassDemoFE8U/ExtraUnitFE8U).
        /// The Gaiden-style per-unit spell (level-up) menu. The table base is NOT a RomInfo slot: it is
        /// resolved by a patch-signature scan, reproduced by
        /// <see cref="FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer"/> (the Core port of WF
        /// <c>FindFE8SpellPatchPointer</c> — both the OldSystem <c>SpellsGetter.dmp</c> grep and the
        /// hard-coded SkillSystems202201 signature; it also reproduces WF's version/multibyte gate and
        /// returns <see cref="U.NOT_FOUND"/> when neither path matches, so a non-patched ROM emits
        /// nothing). VERBATIM:
        /// <list type="bullet">
        ///   <item><c>assignLevelUpP = FindFE8SpellPatchPointer()</c>; if NOT_FOUND return.</item>
        ///   <item><c>assignLevelUpAddr = p32(assignLevelUpP)</c>; if NOT_FOUND return.</item>
        ///   <item>Main IFR: base <c>assignLevelUpP</c>, block 4, IsDataExists = <c>i &lt; 0xFF</c>, name
        ///   "SkillAssignmentUnitSkillSystem", pointerIndexes EMPTY (WF <c>new uint[] {}</c>).</item>
        ///   <item>Per main entry <c>i &lt; DataCount</c>, <c>assignLevelUpAddr += 4</c>: if
        ///   <c>!isSafetyOffset(assignLevelUpAddr)</c> BREAK; <c>levelupList = p32(assignLevelUpAddr)</c>;
        ///   if <c>!isSafetyOffset(levelupList)</c> CONTINUE; else a nested IFR @ <c>assignLevelUpAddr</c>
        ///   (the N1 table: block 2, IsDataExists = <c>u16(addr) != 0xFFFF &amp;&amp; u16(addr) != 0</c>),
        ///   name "SkillAssignmentUnitSkillSystem.Levelup" + i.</item>
        /// </list>
        /// The nested IFR is emitted via <see cref="EmitNestedIfrSub"/> (the slice-2i primitive). The WF
        /// loop's break/continue order is reproduced exactly (note: the break tests
        /// <c>assignLevelUpAddr</c> AFTER it was advanced, mirroring the WF <c>for</c> increment).
        /// </summary>
        public static void EmitFE8SpellMenuExtends(ROM rom, List<Address> list)
        {
            uint assignLevelUpP = FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, CoreState.BaseDirectory);
            EmitFE8SpellMenuExtendsAt(rom, list, assignLevelUpP);
        }

        /// <summary>FE8SpellMenuExtends walk from an explicit <c>assignLevelUpP</c> pointer slot (test
        /// seam — lets a synthetic ROM supply it without the patch-signature scan / RomInfo). See
        /// <see cref="EmitFE8SpellMenuExtends"/> for the verbatim WF reproduction.</summary>
        public static void EmitFE8SpellMenuExtendsAt(ROM rom, List<Address> list, uint assignLevelUpP)
        {
            const uint block = 4;

            if (assignLevelUpP == U.NOT_FOUND)
            {
                return;
            }
            // WF reads p32(assignLevelUpP) directly after the NOT_FOUND check. p32 toOffsets + reads; guard
            // the full slot (root+3) first so a near-EOF pointer emits nothing instead of throwing.
            uint slot = U.toOffset(assignLevelUpP);
            if (!U.isSafetyOffset(slot + 3, rom))
            {
                return;
            }
            uint assignLevelUpAddr = rom.p32(slot);
            if (assignLevelUpAddr == U.NOT_FOUND)
            {
                return;
            }

            // Main IFR: base = p32(assignLevelUpP) (already in assignLevelUpAddr — but Init's BaseAddress is
            // ALSO p32(slot); WF AddAddress uses the IFR BaseAddress). IsDataExists = i < 0xFF (a pure
            // count, no ROM read). pointerIndexes EMPTY. Skip if the resolved base is unsafe.
            if (!U.isSafetyOffset(assignLevelUpAddr, rom))
            {
                return;
            }
            uint dataCount = rom.getBlockDataCount(assignLevelUpAddr, block, (i, addr) => i < 0xFF);
            uint length = block * (dataCount + 1);
            list.Add(new Address(assignLevelUpAddr, length, slot, "SkillAssignmentUnitSkillSystem",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { }));

            // Per main entry: advance assignLevelUpAddr by 4 FIRST (WF for-increment), then the break/
            // continue guards. The nested N1 table (block 2, u16 != 0xFFFF && != 0) is emitted via the
            // slice-2i NestedIfr primitive (ReInitPointer(assignLevelUpAddr) + AddAddress(N1, ..., {})).
            for (uint i = 0; i < dataCount; i++, assignLevelUpAddr += block)
            {
                if (!U.isSafetyOffset(assignLevelUpAddr, rom))
                {
                    break;
                }
                uint levelupList = rom.p32(assignLevelUpAddr);
                if (!U.isSafetyOffset(levelupList, rom))
                {
                    continue;
                }
                // N1_Init: block 2, IsDataExists = u16(addr) != 0xFFFF && u16(addr) != 0.
                EmitNestedIfrSub(rom, list, assignLevelUpAddr, 2,
                    (j, addr) =>
                    {
                        uint a = rom.u16(addr);
                        return a != 0xFFFF && a != 0;
                    },
                    "SkillAssignmentUnitSkillSystem.Levelup" + i);
            }
        }

        // ===================================================================
        // slice 2o — SkillSystems skill-config / skill-assignment forms
        // (the RecycleOldAnime-FREE subset). The other Skill forms
        // (SkillConfigSkillSystemForm / FE8NVer2 / FE8NVer3) STAY deferred:
        // they call ImageUtilSkillSystemsAnimeCreator.RecycleOldAnime, an
        // anime length walker not yet in Core (same blocker as the Group-3
        // ImageUtilOAM/anime forms), plus the Ver2/Ver3 forms read GUI session
        // state (g_SkillBaseAddress / g_AnimeBaseAddress / g_ICON_LIST_SIZE).
        //
        // All base/count scanners are ALREADY in Core, faithfully:
        //   - SkillSystemTextScanner.SearchSkillSystem  (= WF PatchUtil.SearchSkillSystemLow)
        //   - SkillSystemPatchScanner.Find{Assign*,Skill}PointerLocation
        //       (= WF SkillConfigSkillSystemForm.FindSkillPointer, incl. the
        //        LEVELUP triple-pointer validation + MakeMaskData)
        //   - SkillSystemTextScanner.FindSkillFE8NVer1/Ver2IconPointers
        //       (= WF SkillConfigFE8N{,Ver2}SkillForm.FindSkillFE8NVer*IconPointersLow)
        // and the IFR shapes reuse EmitNestedIfrSub (slice-2i) +
        // Address.AddAddressInstantIFR + ClassDataCount/UnitDataCount.
        //
        // VERSION GATE (corruption-critical): WF (U.MakeAllStructPointersList)
        // runs these inside `if (is_multibyte == false)` (FE8U) vs `else` (FE8J):
        //   is_multibyte == false -> SkillAssignmentClass + SkillAssignmentUnit
        //                            (+ SkillConfigSkillSystem, deferred)
        //   is_multibyte == true  -> SkillConfigFE8N
        //                            (+ FE8NVer2, FE8NVer3, deferred)
        // The producer dispatches on is_multibyte at the call site (in
        // MakeAllStructPointers), EXACTLY mirroring WF; each emitter ALSO
        // re-checks SearchSkillSystem (as WF MakeAllDataLength does), so a
        // non-SkillSystem ROM emits nothing.
        // ===================================================================

        /// <summary>
        /// <c>SkillAssignmentClassSkillSystemForm.MakeAllDataLength</c> (slice 2o; FE8U ONLY — gated at
        /// the WF <c>is_multibyte == false</c> call site). The class -&gt; class-skill assignment table
        /// plus the per-class level-up skill lists. Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>bail unless <see cref="SkillSystemTextScanner.SkillSystemEnum.SkillSystem"/>;</item>
        ///   <item>resolve the four pointer SLOTs via the Core scanners
        ///   (<c>FindSkillPointerLocation("ICON"/"TEXT",0)</c>,
        ///   <c>FindAssignClassSkillPointerLocation</c>,
        ///   <c>FindAssignClassLevelUpSkillPointerLocation</c>); if ANY is
        ///   <see cref="U.NOT_FOUND"/> return (WF checks iconP/textP/assignClassP/assignLevelUpP);</item>
        ///   <item>main IFR (<c>Init(null, assignClassP)</c>): base <c>p32(assignClassP)</c>, block 1,
        ///   IsDataExists = <c>i &lt; ClassForm.DataCount() AND (i &lt; 0xFE OR u8(addr) != 0xFF)</c>,
        ///   name "SkillAssignmentClassSkillSystem", pointerIndexes EMPTY;</item>
        ///   <item>a level-up POINTER-LIST IFR via <c>AddAddressInstantIFR(assignLevelUpP, 4,
        ///   mainDataCount, "SkillAssignmentClassLeveList", {0})</c> (fixed count = the MAIN DataCount);</item>
        ///   <item>per main entry (<c>i &lt; mainDataCount</c>, <c>assignLevelUpAddr = p32(assignLevelUpP) +
        ///   4*i</c>): if <c>!isSafetyOffset(assignLevelUpAddr)</c> BREAK; <c>levelupList =
        ///   p32(assignLevelUpAddr)</c>; if <c>!isSafetyOffset(levelupList)</c> CONTINUE; else a nested N1
        ///   IFR @ <c>assignLevelUpAddr</c> (block 2, rule <c>u16(addr) != 0xFFFF &amp;&amp; != 0</c>),
        ///   name "SkillAssignmentClassSkillSystem.Levelup" + i.</item>
        /// </list>
        /// The N1 nested table is emitted via <see cref="EmitNestedIfrSub"/> (the slice-2i primitive); the
        /// WF for-loop break/continue ordering is reproduced exactly (the break tests
        /// <c>assignLevelUpAddr</c> AFTER the for-increment).
        /// </summary>
        public static void EmitSkillAssignmentClass(ROM rom, List<Address> list)
        {
            if (SkillSystemTextScanner.SearchSkillSystem(rom)
                != SkillSystemTextScanner.SkillSystemEnum.SkillSystem)
            {
                return;
            }

            uint iconP = SkillSystemPatchScanner.FindSkillPointerLocation(rom, "ICON", 0);
            uint textP = SkillSystemPatchScanner.FindSkillPointerLocation(rom, "TEXT", 0);
            uint assignClassP = SkillSystemPatchScanner.FindAssignClassSkillPointerLocation(rom);
            uint assignLevelUpP = SkillSystemPatchScanner.FindAssignClassLevelUpSkillPointerLocation(rom);

            if (iconP == U.NOT_FOUND) return;
            if (textP == U.NOT_FOUND) return;
            if (assignClassP == U.NOT_FOUND) return;
            if (assignLevelUpP == U.NOT_FOUND) return;

            // Main IFR: Init(null, assignClassP). IsDataExists = i < classDataCount, with a 0xFE+
            // terminator-byte stop (u8(addr) == 0xFF). classDataCount is captured ONCE (WF captures it
            // outside the IFR lambda), mirroring the WF closure.
            uint classDataCount = ClassDataCount(rom);
            uint mainDataCount = EmitSkillAssignmentMainIfr(rom, list, assignClassP,
                "SkillAssignmentClassSkillSystem",
                (i, addr) =>
                {
                    if (i >= classDataCount) return false;
                    if (i >= 0xFE)
                    {
                        if (rom.u8(addr) == 0xFF) return false;
                    }
                    return true;
                });

            EmitSkillAssignmentLevelUp(rom, list, assignLevelUpP, mainDataCount,
                "SkillAssignmentClassLeveList", "SkillAssignmentClassSkillSystem.Levelup");
        }

        /// <summary>
        /// <c>SkillAssignmentUnitSkillSystemForm.MakeAllDataLength</c> (slice 2o; FE8U ONLY — gated at the
        /// WF <c>is_multibyte == false</c> call site). The unit -&gt; personal-skill assignment table plus
        /// the per-unit level-up skill lists. Same shape as <see cref="EmitSkillAssignmentClass"/> with
        /// the unit scanners, count rule, and names. Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>bail unless <see cref="SkillSystemTextScanner.SkillSystemEnum.SkillSystem"/>;</item>
        ///   <item><c>assignUnitP = FindAssignPersonalSkillPointerLocation</c>; if
        ///   <see cref="U.NOT_FOUND"/> return;</item>
        ///   <item>main IFR (<c>Init(null, assignUnitP)</c>): base <c>p32(assignUnitP)</c>, block 1,
        ///   IsDataExists = <c>i &lt; UnitForm.DataCount()</c>, name "SkillAssignmentUnitSkillSystem",
        ///   pointerIndexes EMPTY;</item>
        ///   <item><c>assignLevelUpP = FindAssignUnitLevelUpSkillPointerLocation</c>; if
        ///   <see cref="U.NOT_FOUND"/> return (WF checks this AFTER emitting the main IFR — so a missing
        ///   level-up pointer still emits the main assignment table);</item>
        ///   <item>level-up POINTER-LIST IFR via <c>AddAddressInstantIFR(assignLevelUpP, 4, mainDataCount,
        ///   "SkillAssignmentUnitLeveList", {0})</c>;</item>
        ///   <item>per main entry: nested N1 IFR @ <c>assignLevelUpAddr</c> (block 2, rule
        ///   <c>u16(addr) != 0xFFFF &amp;&amp; != 0</c>), name "SkillAssignmentUnitSkillSystem.Levelup" + i.</item>
        /// </list>
        /// NOTE the WF ordering difference vs the Class form: the unit form resolves
        /// <c>assignLevelUpP</c> and returns on NOT_FOUND <i>after</i> emitting the main IFR (the Class
        /// form resolves all four up front). Reproduced exactly.
        /// </summary>
        public static void EmitSkillAssignmentUnit(ROM rom, List<Address> list)
        {
            if (SkillSystemTextScanner.SearchSkillSystem(rom)
                != SkillSystemTextScanner.SkillSystemEnum.SkillSystem)
            {
                return;
            }

            uint assignUnitP = SkillSystemPatchScanner.FindAssignPersonalSkillPointerLocation(rom);
            if (assignUnitP == U.NOT_FOUND) return;

            // Main IFR: Init(null, assignUnitP). IsDataExists = i < unitDataCount.
            uint unitDataCount = UnitDataCount(rom);
            uint mainDataCount = EmitSkillAssignmentMainIfr(rom, list, assignUnitP,
                "SkillAssignmentUnitSkillSystem",
                (i, addr) => i < unitDataCount);

            // WF resolves the level-up pointer AFTER the main IFR and returns on NOT_FOUND.
            uint assignLevelUpP = SkillSystemPatchScanner.FindAssignUnitLevelUpSkillPointerLocation(rom);
            if (assignLevelUpP == U.NOT_FOUND) return;

            EmitSkillAssignmentLevelUp(rom, list, assignLevelUpP, mainDataCount,
                "SkillAssignmentUnitLeveList", "SkillAssignmentUnitSkillSystem.Levelup");
        }

        /// <summary>Shared helper for the Class/Unit skill-assignment MAIN IFR — reproduces
        /// <c>Init(null, assignP)</c> + <c>AddressWinForms.AddAddress(list, IFR, name, new uint[] {})</c>
        /// (base = <c>p32(assignP)</c>, block 1, length = <c>1 × (count + 1)</c>, pointer = the slot, or
        /// <see cref="U.NOT_FOUND"/> if unsafe; pointerIndexes EMPTY). Returns the resolved
        /// <c>DataCount</c> (the WF <c>InputFormRef.DataCount</c>) so the caller's level-up loop bound and
        /// <c>AddAddressInstantIFR</c> fixed count match the WF closure — even when the base is unsafe and
        /// no main Address is emitted (WF would still compute DataCount = 0 over an empty base). The slot
        /// is guarded (<c>+3</c>) before the <c>p32</c> read.</summary>
        public static uint EmitSkillAssignmentMainIfr(ROM rom, List<Address> list, uint assignP,
            string name, Func<int, uint, bool> rule)
        {
            const uint block = 1;
            uint slot = U.toOffset(assignP);
            if (!U.isSafetyOffset(slot + 3, rom)) return 0;
            uint baseAddr = rom.p32(slot);

            // WF Init computes DataCount via getBlockDataCount over the (possibly unsafe) base; an unsafe
            // base yields count 0 (getBlockDataCount returns 0 for addr 0/NOT_FOUND and clamps at EOF).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, rule);

            // AddAddress: emit ONLY when the base is a safe offset (else WF returns without adding).
            if (U.isSafetyOffset(baseAddr, rom))
            {
                uint length = block * (dataCount + 1);
                uint pointer = U.isSafetyOffset(slot, rom) ? slot : U.NOT_FOUND;
                list.Add(new Address(baseAddr, length, pointer, name,
                    Address.DataTypeEnum.InputFormRef, block, new uint[] { }));
            }
            return dataCount;
        }

        /// <summary>Shared helper for the Class/Unit skill-assignment LEVEL-UP pointer list — reproduces
        /// the WF tail VERBATIM:
        /// <c>AddAddressInstantIFR(list, assignLevelUpP, 4, mainDataCount, listName, {0})</c> followed by
        /// the per-entry loop (<c>assignLevelUpAddr = p32(assignLevelUpP)</c>, <c>i &lt; mainDataCount</c>,
        /// <c>+= 4</c>; break on unsafe <c>assignLevelUpAddr</c>, continue on unsafe <c>p32(addr)</c>, else
        /// a block-2 nested N1 IFR via <see cref="EmitNestedIfrSub"/> named <paramref name="levelupName"/>
        /// + i). The slot is guarded (<c>+3</c>) before <c>p32(assignLevelUpP)</c>.</summary>
        public static void EmitSkillAssignmentLevelUp(ROM rom, List<Address> list, uint assignLevelUpP,
            uint mainDataCount, string listName, string levelupName)
        {
            const uint block = 4;

            // EOF guard FIRST: BOTH the AddAddressInstantIFR (which p32s the slot) and the WF
            // p32(assignLevelUpP) below read the full 4-byte slot. AddAddressInstantIFR's own check is
            // only isSafetyOffset(pointer) (the START byte) — it would throw on a slot at Data.Length-2.
            // Guard root+3 once, up front, so a near-EOF slot emits nothing instead of throwing. (WF never
            // hits this — its slot comes from a valid patch scan — so guarding first is behaviour-neutral
            // for real ROMs while preserving the WF emit ORDER when the guard passes.)
            uint slot = U.toOffset(assignLevelUpP);
            if (!U.isSafetyOffset(slot + 3, rom)) return;

            // AddAddressInstantIFR reproduces the WF "SkillAssignment*LeveList" pointer-list IFR
            // (fixed count = mainDataCount). It reads CoreState.ROM (== rom by the MakeAllStructPointers
            // guard) and internally guards the resolved base.
            Address.AddAddressInstantIFR(list, assignLevelUpP, block, mainDataCount, listName, new uint[] { 0 });

            // Per main entry: WF reads p32(assignLevelUpP) up front, then walks i < mainDataCount.
            // NOTE the loop bound is the MAIN IFR's DataCount (not a getBlockDataCount over THIS pointer
            // list), so assignLevelUpAddr can advance past where a 4-byte read is in bounds — guard the
            // FULL slot (root+3) in the break (WF only checks isSafetyOffset(addr) — the start byte — and
            // would throw here; the +3 extension is behaviour-neutral on a real ROM whose level-up list is
            // allocated to mainDataCount entries, and EOF-robust on a truncated/synthetic one).
            uint assignLevelUpAddr = rom.p32(slot);
            for (uint i = 0; i < mainDataCount; i++, assignLevelUpAddr += block)
            {
                if (!U.isSafetyOffset(assignLevelUpAddr + 3, rom)) break;
                uint levelupList = rom.p32(assignLevelUpAddr);
                if (!U.isSafetyOffset(levelupList, rom)) continue;

                // N1_Init: block 2, IsDataExists = u16(addr) != 0xFFFF && u16(addr) != 0.
                EmitNestedIfrSub(rom, list, assignLevelUpAddr, 2,
                    (j, addr) =>
                    {
                        uint a = rom.u16(addr);
                        return a != 0xFFFF && a != 0;
                    },
                    levelupName + i);
            }
        }

        /// <summary>
        /// <c>SkillConfigFE8NSkillForm.MakeAllDataLength</c> (slice 2o; FE8J ONLY — gated at the WF
        /// <c>is_multibyte == true</c> / <c>else</c> call site). The FE8N (Ver1) / yugudora / FE8N_ver2
        /// skill-config icon tables. (Ver3 contributes nothing here — WF returns; the FE8NVer2/Ver3 FORMS
        /// stay deferred on RecycleOldAnime + GUI state.) Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item><c>skill = SearchSkillSystem()</c>; FE8N/yugudora -&gt; Ver1 icon pointers; FE8N_ver3
        ///   -&gt; return; FE8N_ver2 -&gt; Ver2 icon pointers; else -&gt; return. If the pointer array is
        ///   null return;</item>
        ///   <item>main IFR shape (<c>Init(null)</c>): block 32, IsDataExists = <c>u16(addr) != 0xFFFF
        ///   &amp;&amp; u16(addr) != 0</c>;</item>
        ///   <item>for each <c>pointer[i]</c>: <c>ReInitPointer(pointer[i])</c> (base = <c>p32(pointer[i])</c>);
        ///   if the resolved <c>DataCount &lt;= 0</c> CONTINUE (skip — no Address); else
        ///   <c>AddAddress(list, IFR, "SkillConfigFE8N" + ToHexString(i), {})</c>.</item>
        /// </list>
        /// The pointer arrays come from the Core scanners
        /// (<see cref="SkillSystemTextScanner.FindSkillFE8NVer1IconPointers"/> /
        /// <see cref="SkillSystemTextScanner.FindSkillFE8NVer2IconPointers"/>), faithful EOF-hardened ports
        /// of the WF <c>FindSkillFE8NVer*IconPointersLow</c> grep-and-walk. Each per-pointer IFR is emitted
        /// via <see cref="EmitNestedIfrSub"/> (the slice-2i primitive: base = <c>p32(pointer[i])</c>,
        /// block 32, length = <c>32 × (count + 1)</c>, pointer = <c>pointer[i]</c>) — but ONLY when the
        /// computed count is &gt; 0, to reproduce the WF <c>DataCount &lt;= 0 -&gt; continue</c> skip.
        /// </summary>
        public static void EmitSkillConfigFE8N(ROM rom, List<Address> list)
        {
            uint[] pointer;
            SkillSystemTextScanner.SkillSystemEnum skill = SkillSystemTextScanner.SearchSkillSystem(rom);
            if (skill == SkillSystemTextScanner.SkillSystemEnum.FE8N
                || skill == SkillSystemTextScanner.SkillSystemEnum.yugudora)
            {
                pointer = SkillSystemTextScanner.FindSkillFE8NVer1IconPointers(rom);
            }
            else if (skill == SkillSystemTextScanner.SkillSystemEnum.FE8N_ver3)
            {
                return; // Ver3 does not use this form
            }
            else if (skill == SkillSystemTextScanner.SkillSystemEnum.FE8N_ver2)
            {
                pointer = SkillSystemTextScanner.FindSkillFE8NVer2IconPointers(rom, out _);
            }
            else
            {
                return;
            }

            EmitSkillConfigFE8NAt(rom, list, pointer);
        }

        /// <summary>SkillConfigFE8N walk from an explicit icon-pointer array (test seam — lets a synthetic
        /// ROM supply the pointer slots without the FE8N icon-pointer grep / RomInfo). See
        /// <see cref="EmitSkillConfigFE8N"/> for the verbatim WF reproduction. Emits one block-32 IFR per
        /// pointer slot whose resolved <c>DataCount &gt; 0</c> (WF's <c>DataCount &lt;= 0 -&gt; continue</c>
        /// skip), named "SkillConfigFE8N" + ToHexString(i).</summary>
        public static void EmitSkillConfigFE8NAt(ROM rom, List<Address> list, uint[] pointer)
        {
            if (pointer == null) return;

            // Init(null): block 32, IsDataExists = u16(addr) != 0xFFFF && u16(addr) != 0. The N00-N03
            // unionPrefixList in WF is UI-only (multi-tab editor) and does not affect relocation.
            const uint block = 32;
            for (int i = 0; i < pointer.Length; i++)
            {
                // WF: ifr.ReInitPointer(pointer[i]); if (ifr.DataCount <= 0) continue;. Reproduce the
                // DataCount<=0 skip — EmitNestedIfrSub would otherwise emit a length-32 (count 0 -> *1)
                // Address that WF suppresses.
                uint slot = U.toOffset(pointer[i]);
                if (!U.isSafetyOffset(slot + 3, rom)) continue;
                uint baseAddr = rom.p32(slot);
                if (!U.isSafetyOffset(baseAddr, rom)) continue;
                uint dataCount = rom.getBlockDataCount(baseAddr, block,
                    (j, addr) =>
                    {
                        uint pp = rom.u16(addr);
                        return pp != 0xFFFF && pp != 0;
                    });
                if (dataCount <= 0) continue;

                EmitNestedIfrSub(rom, list, pointer[i], block,
                    (j, addr) =>
                    {
                        uint pp = rom.u16(addr);
                        return pp != 0xFFFF && pp != 0;
                    },
                    "SkillConfigFE8N" + U.ToHexString(i));
            }
        }

        // ===================================================================
        // slice 2i — nested count-walked IFR sub-table (SubKind.NestedIfr) +
        // the OPClassDemo / OPClassDemoFE7 forms (version-gated multibyte).
        // Each form runs, per main-table entry, one or two N_IFR sub-tables
        // reached via an embedded pointer, each with its OWN getBlockDataCount-
        // walked variable count — a shape no flat block-length SubKind expresses.
        // The per-entry guard ordering is form-specific (FE8 guards BOTH embedded
        // pointers before emitting EITHER nested table), so each form gets a
        // dedicated emitter rather than the flat EmitSubWalks loop.
        // ===================================================================

        /// <summary>
        /// Emit ONE nested count-walked IFR <see cref="Address"/> for the embedded pointer at
        /// <paramref name="pfield"/>. Reproduces the WinForms
        /// <c>N_IFR.ReInitPointer(pfield); AddressWinForms.AddAddress(list, N_IFR, name, new uint[] {})</c>
        /// VERBATIM:
        /// <list type="bullet">
        ///   <item><c>subBase = p32(pfield)</c> — the nested table's base
        ///   (<c>ReInitPointer</c> -&gt; <c>ReInit(p32(pfield))</c>);</item>
        ///   <item><c>subCount = getBlockDataCount(subBase, subBlock, subRule)</c> — the inner walk
        ///   (<c>ReInit</c>'s <c>getBlockDataCount</c>);</item>
        ///   <item>length = <c>subBlock × (subCount + 1)</c>; pointer = <paramref name="pfield"/>
        ///   (the embedded FIELD = <c>N_IFR.BasePointer</c>), or <see cref="U.NOT_FOUND"/> if that is
        ///   not a safe offset (matching <c>AddAddress</c>'s <c>BasePointer</c> fallback); type
        ///   <see cref="Address.DataTypeEnum.InputFormRef"/>; blockSize <paramref name="subBlock"/>;
        ///   pointerIndexes EMPTY (the WinForms <c>new uint[] {}</c>).</item>
        /// </list>
        /// EOF-safe: emits nothing when <paramref name="subBlock"/> is 0, when the embedded-pointer slot
        /// is near EOF (<c>!isSafetyOffset(pfield+3)</c>, guarded before <c>p32</c>), or when <c>subBase</c>
        /// is unsafe (mirroring <c>AddAddress</c>'s <c>!isSafetyOffset(addr)</c> early-return). A
        /// <paramref name="pfield"/> that is in-bounds for the read but not itself a safe BasePointer does
        /// NOT suppress emission — it only makes the emitted Address pointer <see cref="U.NOT_FOUND"/>
        /// (matching <c>AddAddress</c>'s BasePointer fallback). <c>getBlockDataCount</c>'s own callback
        /// reads are <c>addr+subBlock</c>-bounded. Requires a non-null <paramref name="subRule"/> — a
        /// misconfigured <see cref="SubKind.NestedIfr"/> with a null rule throws
        /// <see cref="System.ArgumentNullException"/> (a programming error, not a ROM-data condition).
        /// </summary>
        public static void EmitNestedIfrSub(ROM rom, List<Address> list, uint pfield,
            uint subBlock, Func<int, uint, bool> subRule, string name)
        {
            if (subRule == null)
            {
                // A NestedIfr SubWalk with no SubRule is a descriptor misconfiguration; fail loudly here
                // rather than NRE deep inside getBlockDataCount's callback invocation (consistent with the
                // producer treating other invalid SubKind/DataCountRule configs as programming errors).
                throw new System.ArgumentNullException(nameof(subRule),
                    "SubKind.NestedIfr requires a non-null SubRule (the nested IsDataExists walk).");
            }
            if (subBlock == 0)
            {
                return; // a zero block would make getBlockDataCount spin; not real data.
            }
            // ReInitPointer reads p32(pfield); guard the FULL 4-byte field (root+3) before the read so a
            // near-EOF embedded pointer emits nothing instead of throwing.
            uint field = U.toOffset(pfield);
            if (!U.isSafetyOffset(field + 3, rom))
            {
                return;
            }
            uint subBase = rom.p32(field);
            if (!U.isSafetyOffset(subBase, rom))
            {
                return; // AddAddress early-returns when !isSafetyOffset(addr).
            }

            uint subCount = rom.getBlockDataCount(subBase, subBlock, subRule);
            uint length = subBlock * (subCount + 1);

            // AddAddress's BasePointer fallback: pointer = BasePointer if a safe offset else NOT_FOUND.
            uint pointer = U.isSafetyOffset(field, rom) ? field : U.NOT_FOUND;
            list.Add(new Address(subBase, length, pointer, name,
                Address.DataTypeEnum.InputFormRef, subBlock, new uint[] { }));
        }

        /// <summary>
        /// <c>OPClassDemoForm.MakeAllDataLength</c> (slice 2i; FE8-multibyte ONLY — gated at the
        /// <c>version == 8 &amp;&amp; is_multibyte</c> call site, like OPClassFont; the FE8U non-multibyte
        /// path is <c>OPClassDemoFE8UForm</c>, still deferred). Main IFR: base
        /// <c>op_class_demo_pointer</c>, block 28, IsDataExists = <c>u8(addr+0xF) &lt;= 4</c>,
        /// emitted via <c>AddAddress(list, IFR, "OPClassDemo", {0, 8, 24})</c>. Per main-table entry
        /// (<c>addr = base + i*28</c>, <c>i &lt; DataCount</c>), VERBATIM:
        /// <list type="bullet">
        ///   <item><c>AddCString(list, addr + 0)</c> — the entry's class-name pointer at +0;</item>
        ///   <item>guard <c>jpName = p32(addr + 8)</c>; if <c>!isSafetyOffset(jpName)</c> -&gt; continue
        ///   (skips BOTH nested tables);</item>
        ///   <item>guard <c>anime = p32(addr + 24)</c>; if <c>!isSafetyOffset(anime)</c> -&gt; continue
        ///   (the WF order: jpName guard FIRST, anime guard SECOND, then BOTH nested tables);</item>
        ///   <item>N1 nested IFR @ <c>addr + 8</c>: block 1, rule <c>i &gt;= 16 ? false : u8(addr) != 0xFF</c>,
        ///   name "OPClassDemo_JPName";</item>
        ///   <item>N2 nested IFR @ <c>addr + 24</c>: block 2, rule <c>u8(addr) != 0</c>,
        ///   name "OPClassDemo_Anime".</item>
        /// </list>
        /// The main IFR's pointerIndexes {0, 8, 24} relocate the three embedded pointer FIELDS; the two
        /// nested-IFR sub-Addresses (and the CString) relocate the pointed-at DATA.
        /// </summary>
        public static void EmitOPClassDemo(ROM rom, List<Address> list)
        {
            EmitOPClassDemoAt(rom, list, rom.RomInfo.op_class_demo_pointer);
        }

        /// <summary>OPClassDemo (FE8-multibyte) walk from an explicit pointer slot (test seam — lets a
        /// synthetic ROM supply it without populating RomInfo). See <see cref="EmitOPClassDemo"/> for the
        /// verbatim WF reproduction.</summary>
        public static void EmitOPClassDemoAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 28;

            // Main IFR (AddAddress(ifr, "OPClassDemo", {0,8,24})): base = p32(toOffset(slot)),
            // pointer = slot. Guard the full slot before p32 (root+3).
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // IsDataExists = u8(addr+0xF) <= 4. getBlockDataCount only fires while addr+28<=Length, so
            // the +0xF read is always in bounds.
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) =>
                rom.u8(addr + 0xF) <= 4);

            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "OPClassDemo",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 8, 24 }));

            // Per-entry expansion. getBlockDataCount guarantees addr+block(28)<=Length for i<dataCount,
            // so p32(addr+8) (deepest addr+11) and p32(addr+24) (deepest addr+27) are both in bounds.
            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                // Address.AddCString(list, 0 + addr): the class-name CString pointer at entry offset +0.
                Address.AddCString(list, p + 0);

                // WF guards BOTH embedded pointers (jpName @ +8, anime @ +24) before emitting EITHER
                // nested table; a NULL/unsafe either one skips both N1 and N2 for this entry.
                uint jpName = rom.p32(p + 8);
                if (!U.isSafetyOffset(jpName, rom))
                {
                    continue;
                }
                uint anime = rom.p32(p + 24);
                if (!U.isSafetyOffset(anime, rom))
                {
                    continue;
                }

                // N1_InputFormRef.ReInitPointer(addr + 8); AddAddress(N1, "OPClassDemo_JPName", {}).
                // N1_Init: block 1, IsDataExists = i >= 16 ? false : u8(addr) != 0xFF.
                EmitNestedIfrSub(rom, list, p + 8, 1,
                    (j, addr) => j >= 16 ? false : rom.u8(addr) != 0xFF,
                    "OPClassDemo_JPName");

                // N2_InputFormRef.ReInitPointer(addr + 24); AddAddress(N2, "OPClassDemo_Anime", {}).
                // N2_Init: block 2, IsDataExists = u8(addr) != 0x00.
                EmitNestedIfrSub(rom, list, p + 24, 2,
                    (j, addr) => rom.u8(addr) != 0x00,
                    "OPClassDemo_Anime");
            }
        }

        /// <summary>
        /// <c>OPClassDemoFE7Form.MakeAllDataLength</c> (slice 2i; FE7-multibyte ONLY — gated at the
        /// <c>version == 7 &amp;&amp; is_multibyte</c> call site; the FE7U non-multibyte path is
        /// <c>OPClassDemoFE7UForm</c>, still deferred). Differs from the FE8 form: ONE nested table (N2),
        /// an LZ77 column at +8 (not a nested table), a FIXED main count (<c>i &lt;= 0x41</c>), and a
        /// trailing standalone common-palette pointer. Main IFR: base <c>op_class_demo_pointer</c>,
        /// block 32, IsDataExists = <c>i &lt;= 0x41</c>, emitted via
        /// <c>AddAddress(list, IFR, "OPClassDemo", {0, 8, 28})</c>. Per main-table entry
        /// (<c>addr = base + i*32</c>, <c>i &lt; DataCount</c>), VERBATIM:
        /// <list type="bullet">
        ///   <item><c>AddCString(list, addr + 0)</c> — class-name pointer at +0;</item>
        ///   <item><c>AddLZ77Pointer(list, addr + 8, "OPClassDemo_Anime_&lt;i&gt;_JP_NAME_IMG", false,
        ///   LZ77IMG)</c> — the JP-name image (producer always scans real lengths, isPointerOnly=false);</item>
        ///   <item>guard <c>anime = p32(addr + 28)</c>; if <c>!isSafetyOffset(anime)</c> -&gt; continue;</item>
        ///   <item>N2 nested IFR @ <c>addr + 28</c>: block 2, rule <c>u8(addr) != 0</c>,
        ///   name "OPClassDemo_Anime_&lt;i&gt;_Anime".</item>
        /// </list>
        /// AFTER the loop (once): <c>AddPointer(list, 0x0B0038, 2*16, "OPClassDemo_CommonPalette", PAL)</c>
        /// — the absolute <c>JP_FONT_PALETTE_POINTER</c> common-palette pointer (a fixed ROM location, NOT
        /// a RomInfo field; its DATA is a 32-byte palette). The main IFR's pointerIndexes {0, 8, 28}
        /// relocate the three embedded pointer FIELDS.
        /// </summary>
        public static void EmitOPClassDemoFE7(ROM rom, List<Address> list)
        {
            EmitOPClassDemoFE7At(rom, list, rom.RomInfo.op_class_demo_pointer);
        }

        /// <summary>OPClassDemo (FE7-multibyte) walk from an explicit pointer slot (test seam). See
        /// <see cref="EmitOPClassDemoFE7"/> for the verbatim WF reproduction.</summary>
        public static void EmitOPClassDemoFE7At(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 32;
            // WF OPClassDemoFE7Form: const uint JP_FONT_PALETTE_POINTER = 0x0B0038.
            const uint JP_FONT_PALETTE_POINTER = 0x0B0038;

            // Main IFR (AddAddress(ifr, "OPClassDemo", {0,8,28})): base = p32(toOffset(slot)),
            // pointer = slot. Guard the full slot before p32 (root+3).
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                // Even on a missing main table, WF still emits the trailing common-palette pointer
                // (it is outside the `{ ... }` block but unconditional). Reproduce that.
                Address.AddPointer(list, JP_FONT_PALETTE_POINTER, 2 * 16,
                    "OPClassDemo_CommonPalette", Address.DataTypeEnum.PAL);
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                Address.AddPointer(list, JP_FONT_PALETTE_POINTER, 2 * 16,
                    "OPClassDemo_CommonPalette", Address.DataTypeEnum.PAL);
                return;
            }

            // IsDataExists = i <= 0x41 (a pure fixed count; addr is ignored). DataCount caps at 0x42
            // entries (i = 0..0x41) or sooner if addr+32 runs past EOF (getBlockDataCount's own guard).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) => i <= 0x41);

            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "OPClassDemo",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0, 8, 28 }));

            // Per-entry expansion. getBlockDataCount guarantees addr+block(32)<=Length for i<dataCount,
            // so p32(addr+28) (deepest addr+31) is in bounds. AddCString/AddLZ77Pointer self-guard.
            uint p = baseAddr;
            for (uint i = 0; i < dataCount; i++, p += block)
            {
                string name = "OPClassDemo_Anime_" + U.ToHexString(i);

                // Address.AddCString(list, 0 + addr): class-name CString pointer at +0.
                Address.AddCString(list, p + 0);

                // Address.AddLZ77Pointer(list, addr + 8, name + "_JP_NAME_IMG", isPointerOnly, LZ77IMG).
                // The producer always scans real lengths (isPointerOnly: false). Self-guards + EOF-safe.
                Address.AddLZ77Pointer(list, p + 8, name + "_JP_NAME_IMG", false,
                    Address.DataTypeEnum.LZ77IMG);

                // WF guards anime @ +28 before emitting the N2 nested table.
                uint anime = rom.p32(p + 28);
                if (!U.isSafetyOffset(anime, rom))
                {
                    continue;
                }

                // N2_InputFormRef.ReInitPointer(addr + 28); AddAddress(N2, name + "_Anime", {}).
                // N2_Init: block 2, IsDataExists = u8(addr) != 0x00.
                EmitNestedIfrSub(rom, list, p + 28, 2,
                    (j, addr) => rom.u8(addr) != 0x00,
                    name + "_Anime");
            }

            // After the loop, unconditional (outside the `{ ... }` block in WF):
            // Address.AddPointer(list, JP_FONT_PALETTE_POINTER, 2*16, "OPClassDemo_CommonPalette", PAL).
            Address.AddPointer(list, JP_FONT_PALETTE_POINTER, 2 * 16,
                "OPClassDemo_CommonPalette", Address.DataTypeEnum.PAL);
        }

        // ===================================================================
        // slice 2g — per-map PLIST forms (version-agnostic section of WF
        // MakeAllStructPointersList). Each is a per-map walk that a flat
        // StructDescriptor cannot express, so each gets a dedicated emitter +
        // a test seam that supplies the resolved work items without RomInfo.
        // ===================================================================

        /// <summary>
        /// <c>ItemShopForm.MakeAllDataLength</c> (slice 2g, version-agnostic). For each shop
        /// enumerated by <see cref="ItemShopCore.MakeShopList"/> (hensei preparation shop, FE8
        /// worldmap shops, per-map event-cond shops — verbatim WF <c>MakeShopListLow</c> scan
        /// order), WF re-inits an IFR at the shop item-list address and emits one BIN
        /// <see cref="Address"/> whose length is <c>(DataCount + 1) * BlockSize</c> with
        /// BlockSize = 2 and DataCount = the count of non-zero 2-byte item entries
        /// (<c>ItemShopForm.Init</c> IsDataExists = <c>u8(addr) != 0x00</c>). Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>shop_addr = <c>AddrResult.addr</c>; tag (pointer slot) = <c>AddrResult.tag</c>;</item>
        ///   <item>DataCount = <c>getBlockDataCount(shop_addr, 2, u8(addr)!=0)</c> (the WF IFR walk;
        ///   NOT <see cref="ItemShopCore.CountShopItems"/>, whose 0x200 cap WF's IFR lacks);</item>
        ///   <item><c>AddAddress(list, shop_addr, (DataCount+1)*2, tag, "Shop", BIN)</c>.</item>
        /// </list>
        /// The shop enumeration depends on the event-script OBJECT-condition scan
        /// (<see cref="ItemShopCore.MakeShopList"/> via <c>MapEventUnitCore.GetCondSlots</c> /
        /// <c>GetEventAddrForMap</c>), which are pure ROM reads — so this is faithfully headless.
        /// </summary>
        public static void EmitItemShop(ROM rom, List<Address> list)
        {
            EmitItemShopList(rom, list, ItemShopCore.MakeShopList(rom));
        }

        /// <summary>ItemShop emission from an explicit shop list (test seam — lets a synthetic ROM
        /// supply the shop addr/tag without driving the full event-cond scan). See
        /// <see cref="EmitItemShop"/> for the verbatim WF reproduction. Each
        /// <see cref="AddrResult.addr"/> is the shop's item-list address and
        /// <see cref="AddrResult.tag"/> is the inbound 4-byte pointer slot.</summary>
        public static void EmitItemShopList(ROM rom, List<Address> list, List<AddrResult> shopList)
        {
            if (shopList == null)
            {
                return;
            }
            const uint block = 2; // ItemShopForm.Init BlockSize.
            foreach (AddrResult shop in shopList)
            {
                uint shopAddr = U.toOffset(shop.addr);
                // WF ReInit(shop_addr) -> AddAddress early-returns on an unsafe base. getBlockDataCount
                // itself returns 0 on an unsafe/zero addr, but guard explicitly so the AddAddress below
                // (which would otherwise emit a 1-block length on a NOT_FOUND base) mirrors WF.
                if (!U.isSafetyOffset(shopAddr, rom))
                {
                    continue;
                }

                // WF IFR DataCount: getBlockDataCount(shop_addr, 2, u8(addr) != 0). The 2-arg overload
                // stops at the 0x00 terminator OR at addr+block > Data.Length (EOF) — no extra guard is
                // needed (the callback only reads u8(addr), fully covered by the loop's addr+block<=Len).
                uint dataCount = rom.getBlockDataCount(shopAddr, block, (i, addr) => rom.u8(addr) != 0x00);
                uint length = block * (dataCount + 1);
                // AddAddress(list, shop_addr, length, tag, "Shop", BIN). The tag (pointer slot) is the
                // address of the 4-byte pointer that references the shop — relocated by the rebuild.
                Address.AddAddress(list, shopAddr, length, shop.tag, "Shop", Address.DataTypeEnum.BIN);
            }
        }

        /// <summary>
        /// <c>MapChangeForm.MakeAllDataLength</c> (slice 2g, version-agnostic). Iterates every map
        /// (<c>mapid &lt; MapSettingForm.GetDataCount()</c>), resolves the per-map mapchange PLIST via
        /// <see cref="MapChangeCore.GetMapChangeAddrWhereMapID"/> (= WF
        /// <c>MapSettingForm.GetMapChangeAddrWhereMapID(mapid, out pointer)</c> +
        /// <c>MapPointerForm.PlistToOffsetAddrFast(CHANGE, ...)</c>), and for each resolved map emits:
        /// <list type="bullet">
        ///   <item>the main IFR <see cref="Address"/> (<c>N_Init</c>: BlockSize 12, IsDataExists
        ///   <c>u8(addr) != 0xFF</c>): <c>AddAddress(N_IFR, "MapChange map:0x&lt;id&gt;", {8})</c>
        ///   via <c>ReInitPointer(pointer)</c> — base = <c>p32(pointer)</c>, length =
        ///   12 × (DataCount + 1), pointer = the PLIST slot, pointerIndexes {8};</item>
        ///   <item>per entry (<c>p = base + i*12</c>): <c>w = u8(p+3)</c>, <c>h = u8(p+4)</c>,
        ///   <c>change_mar = p32(p+8)</c>; if <c>isSafetyOffset(change_mar)</c> a BIN
        ///   <see cref="Address"/> of length <c>w*h*2</c> at <c>change_mar</c>, pointer slot
        ///   <c>p+8</c>.</item>
        /// </list>
        /// The map iteration uses <see cref="MapSettingCore.MakeMapIDList"/> (= WF
        /// <c>Init().MakeList()</c>) whose entries carry the sequential mapid in <c>tag</c>; the loop
        /// is bounded by its <c>.Count</c> (= WF <c>GetDataCount()</c>). All reads are pure ROM reads
        /// — faithfully headless.
        /// </summary>
        public static void EmitMapChange(ROM rom, List<Address> list)
        {
            // WF: for (mapid = 0; mapid < GetDataCount(); mapid++). MakeMapIDList(rom) yields one entry
            // per map with tag == mapid (sequential), Count == the WF IFR DataCount.
            foreach (AddrResult map in MapSettingCore.MakeMapIDList(rom))
            {
                uint mapid = map.tag;
                uint change_addr = MapChangeCore.GetMapChangeAddrWhereMapID(rom, mapid, out uint pointer);
                if (change_addr == U.NOT_FOUND)
                {
                    continue; // WF: change_addr == NOT_FOUND -> continue.
                }
                EmitMapChangeAt(rom, list, mapid, pointer);
            }
        }

        /// <summary>MapChange emission for one map from its resolved CHANGE PLIST slot
        /// <paramref name="pointer"/> (test seam — lets a synthetic ROM supply the slot directly
        /// without populating the map-setting / PLIST tables). See <see cref="EmitMapChange"/> for the
        /// verbatim WF reproduction. <paramref name="pointer"/> is the 4-byte PLIST-table slot whose
        /// <c>p32</c> is the change-data base (WF <c>N_InputFormRef.ReInitPointer(pointer)</c>).</summary>
        public static void EmitMapChangeAt(ROM rom, List<Address> list, uint mapid, uint pointer)
        {
            const uint block = 12; // MapChangeForm.N_Init BlockSize.

            // WF N_InputFormRef.ReInitPointer(pointer): BasePointer = pointer, BaseAddress = p32(pointer).
            // Guard the full 4-byte slot before p32 (pointer+3) so a near-EOF slot skips, not throws
            // (matches the EmitUnitFE6At / ItemUsage root+3 convention).
            uint pointerSlot = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointerSlot + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointerSlot);
            // WF AddAddress early-returns when !isSafetyOffset(BaseAddress) (the pointer slot then
            // becomes NOT_FOUND); without a safe base there is also nothing to walk.
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // N_Init IsDataExists = u8(addr) != 0xFF. The 2-arg getBlockDataCount stops at the 0xFF
            // terminator OR at addr+12 > Data.Length (EOF) — the callback reads only u8(addr).
            uint dataCount = rom.getBlockDataCount(baseAddr, block, (i, addr) => rom.u8(addr) != 0xFF);

            // Main IFR: AddAddress(N_IFR, "MapChange map:0x<id>", {8}) -> length = 12 * (DataCount + 1),
            // pointer = BasePointer = the PLIST slot (safe here), pointerIndexes {8}.
            uint length = block * (dataCount + 1);
            string name = "MapChange map:" + U.To0xHexString(mapid);
            list.Add(new Address(baseAddr, length, pointerSlot, name,
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 8 }));

            // Per-entry change-map BIN blocks (WF inner loop): for each entry p = base + i*12 read the
            // w/h size bytes and the +8 change pointer, then add a w*h*2 BIN block behind it.
            for (uint i = 0; i < dataCount; i++)
            {
                uint p = baseAddr + i * block;
                // Guard the full 12-byte record (the deepest read is p32(p+8) -> p+11) before any read,
                // so a corrupted/too-large DataCount near EOF skips instead of throwing. On valid ROMs
                // every record is fully in-bounds (getBlockDataCount already bounded by addr+12<=Len),
                // so this only hardens synthetic/corrupted ROMs — but the p32(p+8) read below reaches
                // p+11, one byte past getBlockDataCount's addr+block-1, so the explicit guard is needed.
                if (p + block > (uint)rom.Data.Length)
                {
                    break;
                }
                uint w = rom.u8(p + 3); // size w
                uint h = rom.u8(p + 4); // size h
                uint change_mar = rom.p32(p + 8); // change-map pointer
                if (!U.isSafetyOffset(change_mar, rom))
                {
                    continue; // WF: !isSafetyOffset(change_mar) -> continue (no emit for this entry).
                }
                string entryName = "MapChange map:" + U.To0xHexString(mapid) + " n:" + U.To0xHexString(i);
                Address.AddAddress(list, change_mar, w * h * 2, p + 8, entryName,
                    Address.DataTypeEnum.BIN);
            }
        }

        /// <summary>
        /// <c>MapExitPointForm.MakeAllDataLength</c> (slice 2g, version-agnostic). Emits TWO main IFR
        /// tables (enemy + NPC escape-point slot tables) plus a per-map sub-table for each:
        /// <list type="bullet">
        ///   <item><b>enemy main</b> (<c>Init</c>: BasePointer = <c>map_exit_point_pointer</c>, BlockSize
        ///   4, IsDataExists <c>isPointerOrNULL(u32(addr)) &amp;&amp; i &lt; npc_blockadd</c>):
        ///   <c>AddAddress(IFR, "MapExit", {0})</c> — length 4 × (DataCount + 1), pointer =
        ///   <c>map_exit_point_pointer</c>, pointerIndexes {0};</item>
        ///   <item><b>enemy per-map</b> (<c>mapid &lt; GetDataCount()</c>): <c>exit_addr =
        ///   IDToAddr(mapid)</c> (= enemy base + mapid*4, bounded by the enemy IFR DataCount); skip when
        ///   out of range or <c>p32(exit_addr) == NOT_FOUND</c>; else <c>N_ReInitPointer(exit_addr)</c>
        ///   (<c>N_Init</c>: BlockSize 4, <c>u8(addr) != 0xFF</c>) and
        ///   <c>AddAddress(N_IFR, "MapExit map:0x&lt;id&gt;", {})</c> — length 4 × (N_DataCount + 1),
        ///   pointer = <c>exit_addr</c>, empty pointerIndexes;</item>
        ///   <item><b>NPC main</b>: <c>ReInit(p32(map_exit_point_pointer) + 4*npc_blockadd)</c> then
        ///   <c>AddAddressButIgnorePointer(IFR, "MapExit NPC", {0})</c> — pointer FORCED to NOT_FOUND
        ///   (the NPC base is reached by a computed offset, not a RomInfo slot), type InputFormRef;</item>
        ///   <item><b>NPC per-map</b>: identical to enemy per-map but rooted at the NPC base, name
        ///   "MapExit map:0x&lt;id&gt; NPC".</item>
        /// </list>
        /// Reproduced VERBATIM (the enemy/NPC bases, the <c>i &lt; npc_blockadd</c> enemy cap, the
        /// <c>ButIgnorePointer</c> NPC main). All reads are pure ROM reads — faithfully headless.
        /// </summary>
        public static void EmitMapExitPoint(ROM rom, List<Address> list)
        {
            uint mapCount = (uint)MapSettingCore.MakeMapIDList(rom).Count; // WF GetDataCount().
            EmitMapExitPointAt(rom, list, rom.RomInfo.map_exit_point_pointer,
                rom.RomInfo.map_exit_point_npc_blockadd, mapCount);
        }

        /// <summary>MapExitPoint emission from explicit RomInfo addresses (test seam — lets a synthetic
        /// ROM supply the pointer slot / npc_blockadd / map count without populating RomInfo). See
        /// <see cref="EmitMapExitPoint"/> for the verbatim WF reproduction.</summary>
        public static void EmitMapExitPointAt(ROM rom, List<Address> list, uint rawMainPointer,
            uint npcBlockAdd, uint mapCount)
        {
            const uint block = 4; // Init / N_Init BlockSize.

            // --- enemy table ---
            // WF Init: BasePointer = map_exit_point_pointer, BaseAddress = p32(map_exit_point_pointer).
            uint mainPointer = U.toOffset(rawMainPointer);
            if (!U.isSafetyOffset(mainPointer + 3, rom))
            {
                return; // p32(mainPointer) would read junk past EOF.
            }
            uint enemyBase = rom.p32(mainPointer);

            // Enemy IsDataExists = isPointerOrNULL(u32(addr)) && i < npc_blockadd.
            Func<int, uint, bool> enemyRule = (i, addr) =>
                U.isPointerOrNULL(rom.u32(addr)) && i < npcBlockAdd;

            // Enemy main: AddAddress(IFR, "MapExit", {0}) — pointer = BasePointer (map_exit_point_pointer).
            EmitMapExitMain(rom, list, enemyBase, block, enemyRule, mainPointer, "MapExit",
                new uint[] { 0 }, ignorePointer: false);

            // Enemy per-map sub-tables: exit_addr = enemyBase + mapid*4 (IDToAddr bounded by the enemy
            // IFR DataCount). The DataCount is the getBlockDataCount over the enemy rule.
            EmitMapExitSubTables(rom, list, enemyBase, block, enemyRule, mapCount, "");

            // --- NPC table ---
            // WF: InputFormRef.ReInit(p32(map_exit_point_pointer) + 4 * npc_blockadd). BasePointer is
            // UNCHANGED by ReInit (still map_exit_point_pointer), but AddAddressButIgnorePointer forces
            // the emitted pointer to NOT_FOUND, so the NPC main's pointer slot is intentionally untracked.
            uint npcBase = enemyBase + block * npcBlockAdd;

            // NPC main: AddAddressButIgnorePointer(IFR, "MapExit NPC", {0}) — pointer forced NOT_FOUND.
            EmitMapExitMain(rom, list, npcBase, block, enemyRule, mainPointer, "MapExit NPC",
                new uint[] { 0 }, ignorePointer: true);

            // NPC per-map sub-tables: exit_addr = npcBase + mapid*4 (IDToAddr bounded by the NPC IFR
            // DataCount, computed from the same enemy rule over the NPC base). Name suffix " NPC".
            EmitMapExitSubTables(rom, list, npcBase, block, enemyRule, mapCount, " NPC");
        }

        /// <summary>The MapExitPoint main-IFR emit (enemy or NPC). Reproduces
        /// <c>AddressWinForms.AddAddress</c> (or <c>AddAddressButIgnorePointer</c> when
        /// <paramref name="ignorePointer"/>): addr = BaseAddress, length = block × (DataCount + 1),
        /// type InputFormRef. The pointer is <paramref name="basePointer"/> for the enemy table and
        /// forced NOT_FOUND for the NPC table.</summary>
        static void EmitMapExitMain(ROM rom, List<Address> list, uint baseAddr, uint block,
            Func<int, uint, bool> rule, uint basePointer, string name, uint[] pointerIndexes,
            bool ignorePointer)
        {
            // WF AddAddress: early-return when !isSafetyOffset(BaseAddress).
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }
            uint dataCount = rom.getBlockDataCount(baseAddr, block, rule);
            uint length = block * (dataCount + 1);
            // AddAddress: pointer = BasePointer (-> NOT_FOUND if unsafe). ButIgnorePointer: pointer
            // ALWAYS NOT_FOUND. basePointer (the RomInfo slot) is already verified safe by the caller.
            uint pointer = ignorePointer ? U.NOT_FOUND : basePointer;
            list.Add(new Address(baseAddr, length, pointer, name,
                Address.DataTypeEnum.InputFormRef, block, pointerIndexes));
        }

        /// <summary>The MapExitPoint per-map sub-table loop (enemy or NPC). For each
        /// <c>mapid &lt; mapCount</c>, WF computes <c>exit_addr = IDToAddr(mapid)</c> (= base + mapid*4,
        /// NOT_FOUND once <c>mapid &gt;= mainDataCount</c>), checks <c>p32(exit_addr) != NOT_FOUND</c>,
        /// then <c>N_ReInitPointer(exit_addr)</c> + <c>AddAddress(N_IFR, "MapExit map:0x&lt;id&gt;" +
        /// suffix, {})</c> — block × (N_DataCount + 1), pointer = exit_addr, empty pointerIndexes, type
        /// InputFormRef.</summary>
        static void EmitMapExitSubTables(ROM rom, List<Address> list, uint base_, uint block,
            Func<int, uint, bool> mainRule, uint mapCount, string nameSuffix)
        {
            if (!U.isSafetyOffset(base_, rom))
            {
                return; // base unsafe -> IDToAddr's IFR DataCount is 0 -> no sub-tables.
            }
            // The main IFR DataCount bounds IDToAddr (WF: id >= DataCount -> NOT_FOUND). Compute it once.
            uint mainDataCount = rom.getBlockDataCount(base_, block, mainRule);

            for (uint mapid = 0; mapid < mapCount; mapid++)
            {
                // WF IDToAddr(mapid): NOT_FOUND when mapid >= mainDataCount.
                if (mapid >= mainDataCount)
                {
                    continue;
                }
                uint exitAddr = base_ + mapid * block;
                // Guard the full 4-byte slot before p32(exitAddr) (exitAddr+3). On valid ROMs the slot is
                // in-bounds (mapid < mainDataCount, which getBlockDataCount bounded by addr+block<=Len);
                // this only hardens corrupted ROMs (p32 reads exitAddr..exitAddr+3).
                if (!U.isSafetyOffset(exitAddr + 3, rom))
                {
                    continue;
                }
                // WF: a = p32(exit_addr); if (a == U.NOT_FOUND) continue; then N_ReInitPointer(exit_addr)
                // (N BasePointer = exit_addr, N BaseAddress = p32(exit_addr)). Read p32 ONCE into nBase
                // and use it for both. ROM.p32 DOES return U.NOT_FOUND (0xFFFFFFFF) when the dword is
                // 0xFFFFFFFF — toOffset leaves it as-is (0xFFFFFFFF is not a valid 0x08-pointer) — i.e.
                // an unset/empty exit slot, so this check meaningfully skips those maps (matching WF —
                // it is NOT merely structural).
                uint nBase = rom.p32(exitAddr);
                if (nBase == U.NOT_FOUND)
                {
                    continue;
                }
                // N_Init IsDataExists = u8(addr) != 0xFF. AddAddress early-returns on an unsafe N base.
                if (!U.isSafetyOffset(nBase, rom))
                {
                    continue;
                }
                uint nDataCount = rom.getBlockDataCount(nBase, block, (i, addr) => rom.u8(addr) != 0xFF);
                uint length = block * (nDataCount + 1);
                string name = "MapExit map:" + U.To0xHexString(mapid) + nameSuffix;
                // AddAddress(N_IFR, name, {}) -> pointer = N BasePointer = exit_addr, empty pointerIndexes.
                list.Add(new Address(nBase, length, exitAddr, name,
                    Address.DataTypeEnum.InputFormRef, block, new uint[] { }));
            }
        }

        /// <summary>
        /// <c>MapTileAnimation1Form.MakeAllDataLength</c> (slice 2g, version-agnostic). Builds the
        /// dedup'd anime1-PLIST list (WF <c>MakeTileAnimation1</c>: every map's <c>anime1_plist</c> at
        /// map-setting +9, skip 0 / already-seen, resolved via
        /// <c>MapPointerForm.PlistToOffsetAddr(ANIMATION, plist)</c> =
        /// <see cref="MapChangeCore.PlistToOffsetAddr"/> with
        /// <see cref="MapChangeCore.PlistType.ANIMATION"/>), then for each resolved PLIST emits:
        /// <list type="bullet">
        ///   <item>the main IFR <see cref="Address"/> (<c>Init</c>: BlockSize 8, IsDataExists
        ///   <c>isPointer(u32(addr+4))</c>): <c>AddAddress(IFR, name, {4})</c> via
        ///   <c>ReInit(addr)</c> — base = addr, BasePointer = 0 -&gt; NOT_FOUND, length =
        ///   8 × (DataCount + 1), pointerIndexes {4};</item>
        ///   <item>per entry (<c>p = base + i*8</c>): <c>img = p32(p+4)</c>, <c>len = u16(p+2)</c>;
        ///   if <c>isSafetyOffset(img)</c> an IMG <see cref="Address"/> of length <c>len</c> at
        ///   <c>img</c>, pointer slot <c>p+4</c>.</item>
        /// </list>
        /// Reproduced VERBATIM. Uses <see cref="MapChangeCore.PlistToOffsetAddr"/> (NOT the
        /// <c>MapTileAnimation1Core.BuildPlistList</c> resolution, which omits the version PLIST-limit
        /// gate) so the resolved data set is byte-identical to WF; a resolution that returns 0/NOT_FOUND
        /// emits no <see cref="Address"/> (WF <c>ReInit</c> on an unsafe base early-returns).
        /// </summary>
        public static void EmitMapTileAnimation1(ROM rom, List<Address> list)
        {
            EmitMapTileAnimationN(rom, list, atOffset: 9, MapChangeCore.PlistType.ANIMATION,
                imgAtPlus4: true);
        }

        /// <summary>
        /// <c>MapTileAnimation2Form.MakeAllDataLength</c> (slice 2g, version-agnostic). Same shape as
        /// <see cref="EmitMapTileAnimation1"/> but: the per-map PLIST is <c>anime2_plist</c> at
        /// map-setting +10, the PLIST type is <see cref="MapChangeCore.PlistType.ANIMATION2"/>, the
        /// main IFR IsDataExists is <c>isPointer(u32(addr+0))</c> (image pointer at +0, NOT +4), the
        /// main IFR pointerIndexes is {0}, and each per-entry block is a BIN of length <c>u8(p+5) * 2</c>
        /// behind the +0 pointer (<c>count = u8(p+5)</c>; palette rows are 2 bytes each). Reproduced
        /// VERBATIM.
        /// </summary>
        public static void EmitMapTileAnimation2(ROM rom, List<Address> list)
        {
            EmitMapTileAnimationN(rom, list, atOffset: 10, MapChangeCore.PlistType.ANIMATION2,
                imgAtPlus4: false);
        }

        /// <summary>Shared MapTileAnimation1/2 emitter. <paramref name="atOffset"/> is the map-setting
        /// byte holding the per-map PLIST (9 = anime1, 10 = anime2); <paramref name="plistType"/> is the
        /// PLIST table; <paramref name="imgAtPlus4"/> selects the anime1 schema (image pointer at +4,
        /// IMG length = <c>u16(p+2)</c>, pointerIndexes {4}) vs the anime2 schema (image pointer at +0,
        /// BIN length = <c>u8(p+5) * 2</c>, pointerIndexes {0}). Reproduces WF <c>MakeTileAnimationN</c>
        /// (dedup by PLIST) + the per-entry inner loop VERBATIM.</summary>
        static void EmitMapTileAnimationN(ROM rom, List<Address> list, uint atOffset,
            MapChangeCore.PlistType plistType, bool imgAtPlus4)
        {
            var seen = new HashSet<uint>();
            foreach (AddrResult map in MapSettingCore.MakeMapIDList(rom))
            {
                // anime1_plist = u8(mapAddr+9) / anime2_plist = u8(mapAddr+10).
                uint mapAddr = map.addr;
                if (mapAddr + atOffset + 1 > (uint)rom.Data.Length)
                {
                    continue;
                }
                uint plist = rom.u8(mapAddr + atOffset);
                if (plist == 0)
                {
                    continue; // WF: plist == 0 -> continue.
                }
                if (!seen.Add(plist))
                {
                    continue; // WF: already-found dedup.
                }

                // WF: addr = PlistToOffsetAddr(ANIMATION/ANIMATION2, plist). Returns the resolved data
                // offset (or 0/NOT_FOUND when broken). MapChangeCore.PlistToOffsetAddr applies the same
                // version PLIST-limit + safety gate as the WF MapPointerForm path. A broken resolution
                // (NOT_FOUND) means WF marks the row "(破損)" and ReInit on it emits nothing — so skip.
                uint dataAddr = MapChangeCore.PlistToOffsetAddr(rom, plistType, plist, out uint _);
                EmitMapTileAnimationFor(rom, list, dataAddr, plist, imgAtPlus4);
            }
        }

        /// <summary>MapTileAnimation emission for one resolved PLIST data address (test seam — lets a
        /// synthetic ROM supply the entry-table base + plist directly without populating the
        /// map-setting / PLIST tables). <paramref name="dataAddr"/> is the WF <c>urList[n].addr</c>
        /// (the PLIST-resolved entry-table base); <paramref name="imgAtPlus4"/> selects the anime1 vs
        /// anime2 schema. See <see cref="EmitMapTileAnimation1"/> / <see cref="EmitMapTileAnimation2"/>
        /// for the verbatim WF reproduction.</summary>
        public static void EmitMapTileAnimationFor(ROM rom, List<Address> list, uint dataAddr,
            uint plist, bool imgAtPlus4)
        {
            const uint block = 8; // Init BlockSize (both anime1 and anime2).

            // WF: InputFormRef.ReInit(urList[n].addr). On an unsafe/NOT_FOUND base the IFR DataCount is 0
            // and AddAddress early-returns — emit nothing (the "(破損)"/0-resolution case).
            uint baseAddr = U.toOffset(dataAddr);
            if (dataAddr == U.NOT_FOUND || !U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // Main IFR IsDataExists: anime1 = isPointer(u32(addr+4)); anime2 = isPointer(u32(addr+0)).
            uint imgOffset = imgAtPlus4 ? 4u : 0u;
            uint dataCount = rom.getBlockDataCount(baseAddr, block,
                (i, addr) => U.isPointer(rom.u32(addr + imgOffset)));

            // Main IFR Address: AddAddress(IFR, name, {imgOffset}). BasePointer = 0 -> NOT_FOUND (the
            // entry table is reached via the PLIST resolution, not a plain RomInfo pointer slot). The WF
            // name is the localized "タイルアニメーション1/2:0x<plist>" string; the label is non-load-
            // bearing for relocation (same convention as SoundFootSteps), so a stable ASCII label is
            // used here ("MapTileAnime1/2:0x<plist>") — headless and locale-independent.
            uint length = block * (dataCount + 1);
            string animeKind = imgAtPlus4 ? "1" : "2";
            string name = "MapTileAnime" + animeKind + ":" + U.To0xHexString(plist);
            list.Add(new Address(baseAddr, length, U.NOT_FOUND, name,
                Address.DataTypeEnum.InputFormRef, block, new uint[] { imgOffset }));

            // Per-entry image/palette blocks (WF inner loop).
            for (uint i = 0; i < dataCount; i++)
            {
                uint p = baseAddr + i * block;
                // Guard the full 8-byte record before any read. The deepest read is p32(p+4) -> p+7
                // (anime1) or u8(p+5) (anime2); getBlockDataCount already bounded addr+block<=Len, but
                // guard explicitly so a corrupted DataCount near EOF skips instead of throwing.
                if (p + block > (uint)rom.Data.Length)
                {
                    break;
                }
                if (imgAtPlus4)
                {
                    // anime1: addr = p32(p+4); len = u16(p+2); IMG block of length len at addr, slot p+4.
                    uint addr = rom.p32(p + 4);
                    uint imgLen = rom.u16(p + 2);
                    if (U.isSafetyOffset(addr, rom))
                    {
                        Address.AddAddress(list, addr, imgLen, p + 4,
                            name + "_" + U.ToHexString(i), Address.DataTypeEnum.IMG);
                    }
                }
                else
                {
                    // anime2: addr = p32(p+0); count = u8(p+5); BIN block of length count*2 at addr, slot p+0.
                    uint addr = rom.p32(p + 0);
                    uint count = rom.u8(p + 5);
                    if (U.isSafetyOffset(addr, rom))
                    {
                        Address.AddAddress(list, addr, count * 2, p + 0,
                            name + "_" + U.To0xHexString(i), Address.DataTypeEnum.BIN);
                    }
                }
            }
        }

        // ====================================================================
        // slice 2j — misc self-contained stragglers (MapTerrain lookup tables,
        // MapPointer, ExtraUnit FE8J/FE8U). Each was DEFERRED on a "helper not in
        // Core" claim that is now STALE — MapTerrainLookupCore.GetPointers,
        // MapPListResolverCore.GetMapPListsWhereAddr (+ its PatchDetection gate),
        // and MapSettingCore.MakeMapIDList were all added for the Avalonia
        // gap-sweep (#441/#442) and are pure ROM-reads. ExtraUnit's only blocker
        // (EventUnitForm.RecycleOldUnits) is reproduced verbatim by
        // EmitRecycleOldUnits below (a pure EventUnit-IFR + FE8 COORD walk).
        // ====================================================================

        /// <summary>
        /// <c>MapTerrain{BG,Floor}LookupTableForm.MakeAllDataLength</c> (slice 2j, version-agnostic).
        /// Both forms share the identical shape — only the pointer SET differs (BG vs Floor) — so one
        /// emitter covers both via <paramref name="isFloor"/>. WF: <c>pointers = GetPointers()</c> (the
        /// vanilla 21-slot RomInfo list, or the extends-patch 8-byte-stride table when the ExtendsBattleBG
        /// patch is installed — reproduced VERBATIM by <see cref="MapTerrainLookupCore.GetPointers"/>),
        /// then for each non-zero pointer <c>InputFormRef.ReInitPointer(pointers[i])</c> +
        /// <c>AddAddress(IFR, name + ToHexString(i), {})</c>. The IFR is block 1, IsDataExists
        /// <c>i &lt; map_terrain_type_count</c> (a FixedCount walk), pointerIndexes EMPTY. The index is
        /// baked into the per-pointer name (so this is a dedicated emitter, not a flat
        /// <see cref="StructDescriptor.PointerFields"/> descriptor whose name is shared).
        /// </summary>
        public static void EmitMapTerrainLookup(ROM rom, List<Address> list, bool isFloor)
        {
            EmitMapTerrainLookupAt(rom, list, MapTerrainLookupCore.GetPointers(rom, isFloor), isFloor);
        }

        /// <summary>MapTerrain lookup emission from an explicit pointer array (test seam — lets a
        /// synthetic ROM supply the pointer set directly without populating the 21 RomInfo slots / the
        /// extends-patch table). See <see cref="EmitMapTerrainLookup"/> for the verbatim WF
        /// reproduction. <paramref name="pointers"/> is the WF <c>GetPointers()</c> array; each non-zero
        /// element is a 4-byte pointer SLOT whose <c>p32</c> is the lookup-table base.</summary>
        public static void EmitMapTerrainLookupAt(ROM rom, List<Address> list, uint[] pointers, bool isFloor)
        {
            const uint block = 1; // MapTerrain{BG,Floor}LookupTableForm.Init BlockSize.
            // WF Init IsDataExists = i < map_terrain_type_count (a FixedCount walk; BOTH the BG and the
            // Floor form use map_terrain_type_count — there is no separate floor count field).
            uint count = rom.RomInfo.map_terrain_type_count;
            string baseName = isFloor ? "MapTerrainFloorLookupTable" : "MapTerrainBGLookupTable";

            if (pointers == null)
            {
                return;
            }
            for (int i = 0; i < pointers.Length; i++)
            {
                uint pointerRaw = pointers[i];
                if (pointerRaw == 0)
                {
                    continue; // WF: pointers[i] == 0 -> continue.
                }
                // WF InputFormRef.ReInitPointer(pointers[i]): BasePointer = pointers[i],
                // BaseAddress = p32(pointers[i]). Guard the full 4-byte slot before p32 (slot+3) so a
                // near-EOF pointer slot emits nothing instead of throwing.
                uint pointer = U.toOffset(pointerRaw);
                if (!U.isSafetyOffset(pointer + 3, rom))
                {
                    continue;
                }
                uint baseAddr = rom.p32(pointer);
                if (!U.isSafetyOffset(baseAddr, rom))
                {
                    continue; // WF AddAddress early-returns when !isSafetyOffset(BaseAddress).
                }

                // IsDataExists = i < count (FixedCount); getBlockDataCount stops at addr+1 > Len (EOF) too.
                uint dataCount = rom.getBlockDataCount(baseAddr, block, (j, addr) => j < count);
                uint length = block * (dataCount + 1);
                list.Add(new Address(baseAddr, length, pointer, baseName + U.ToHexString(i),
                    Address.DataTypeEnum.InputFormRef, block, new uint[] { }));
            }
        }

        /// <summary>
        /// <c>MapPointerForm.MakeAllDataLength</c> (slice 2j, version-agnostic). Emits the 6-7
        /// MAPPOINTERS PLIST-table IFRs (CONFIG / ANIMATION / OBJECT / MAP / EVENT / CHANGE, plus a FE6
        /// WMAP_EVENT alias) and then a per-map sweep that resolves each map's PLIST columns
        /// (<see cref="MapPListResolverCore.GetMapPListsWhereAddr"/>) and adds the pointed-at map-chipset
        /// LZ77 / MAR / palette / OBJECT blocks. Reproduced VERBATIM:
        /// <list type="bullet">
        ///   <item>each PLIST table is a block-4 IFR, IsDataExists <c>i==0 ? true : i &lt; limit</c> where
        ///   <c>limit = IsPlistSplits() ? 256 : map_map_pointer_list_default_size</c>, pointerIndexes
        ///   {0}; the base pointers come from <see cref="MapPListResolverCore"/>'s RomInfo slots;</item>
        ///   <item>the per-map columns index into the CONFIG / MAP / OBJECT tables by PLIST id (the WF
        ///   <c>configList[plist].addr == configBase + plist*4</c>), each adding an LZ77 (config / MAR /
        ///   obj-low / obj-high) or fixed-PAL (palette / palette2) block behind the indexed slot.</item>
        /// </list>
        /// The deferral claim ("palette2 gated on SearchFlag0x28ToMapSecondPalettePatch — not in Core")
        /// is STALE: that gate now lives inside <see cref="MapPListResolverCore.GetMapPListsWhereAddr"/>.
        /// The producer always scans real lengths, so the LZ77 columns pass <c>isPointerOnly: false</c>.
        /// </summary>
        public static void EmitMapPointer(ROM rom, List<Address> list)
        {
            // IsPlistSplits(): the PLIST tables are split iff CONFIG's base differs from every other
            // table's base. Pure RomInfo p32 reads (no PatchUtil) — reproduced from MapPointerForm.
            bool isSplit = IsPlistSplits(rom);
            // limit = split ? 256 : map_map_pointer_list_default_size (the WF Init local).
            uint limit = isSplit ? 256u : rom.RomInfo.map_map_pointer_list_default_size;

            // The base POINTER SLOTS (RomInfo fields), reproducing MapPointerForm.GetBasePointer.
            uint configPtr = rom.RomInfo.map_config_pointer;
            uint animePtr  = rom.RomInfo.map_tileanime1_pointer;
            uint objPtr    = rom.RomInfo.map_obj_pointer;
            uint mapPtr    = rom.RomInfo.map_map_pointer_pointer;
            uint eventPtr  = rom.RomInfo.map_event_pointer;
            uint changePtr = rom.RomInfo.map_mapchange_pointer;

            // The 6 (FE6: 7) main MAPPOINTERS IFR tables. WF emits AddAddress(IFR, name, {0}) for each.
            // configBase/mapBase/objBase are the resolved table bases we also index into per-map below.
            uint configBase = EmitMapPointerTable(rom, list, configPtr, limit, "MAPPOINTERS");
            EmitMapPointerTable(rom, list, animePtr, limit, "MAPPOINTERS_ANIMATION"); // ANIMATION2 shares.
            uint objBase = EmitMapPointerTable(rom, list, objPtr, limit, "MAPPOINTERS_OBJECT"); // PAL shares.
            uint mapBase = EmitMapPointerTable(rom, list, mapPtr, limit, "MAPPOINTERS_MAP");
            EmitMapPointerTable(rom, list, eventPtr, limit, "MAPPOINTERS_EVENT");
            EmitMapPointerTable(rom, list, changePtr, limit, "MAPPOINTERS_CHANGE");
            if (rom.RomInfo.version == 6)
            {
                // WF FE6: a second alias over the CHANGE base ("MAPPOINTERS_WMAP_EVENT").
                EmitMapPointerTable(rom, list, changePtr, limit, "MAPPOINTERS_WMAP_EVENT");
            }

            // The per-map column counts: WF indexes configList[plist] (plist < configList.Count). The IFR
            // DataCount bounds that list, so compute each table's DataCount and require plist < count.
            uint configCount = MapPointerTableDataCount(rom, configBase, limit);
            uint objCount     = MapPointerTableDataCount(rom, objBase, limit);
            uint mapCount     = MapPointerTableDataCount(rom, mapBase, limit);

            // WF: 0x20 * MapStyleEditorForm.MAX_MAP_PALETTE_COUNT. MAX_MAP_PALETTE_COUNT is a WinForms UI
            // constant (PARTS_MAP_PALETTE_COUNT=5 * 2 = 10), NOT a RomInfo field — reproduce the literal.
            const uint MAX_MAP_PALETTE_COUNT = 10;
            const uint palSize = 0x20 * MAX_MAP_PALETTE_COUNT;

            foreach (AddrResult map in MapSettingCore.MakeMapIDList(rom))
            {
                MapPListResolverCore.PLists plists =
                    MapPListResolverCore.GetMapPListsWhereAddr(rom, map.addr);
                uint mapid = map.tag;

                // config_plist -> LZ77MAPCONFIG behind configList[config_plist].addr (= configBase + plist*4).
                if (plists.config_plist > 0 && plists.config_plist < configCount)
                {
                    uint pointer = configBase + plists.config_plist * 4;
                    Address.AddLZ77Pointer(list, pointer + 0,
                        "MAP:" + U.To0xHexString(mapid) + " MAP_CHIPSET" + U.ToHexString(plists.config_plist),
                        false, Address.DataTypeEnum.LZ77MAPCONFIG);
                }
                // mappointer_plist -> LZ77MAPMAR behind mapList[mappointer_plist].addr.
                if (plists.mappointer_plist > 0 && plists.mappointer_plist < mapCount)
                {
                    uint pointer = mapBase + plists.mappointer_plist * 4;
                    Address.AddLZ77Pointer(list, pointer + 0,
                        "MAP:" + U.To0xHexString(mapid) + " MAP MAR:" + U.ToHexString(plists.mappointer_plist),
                        false, Address.DataTypeEnum.LZ77MAPMAR);
                }
                // palette_plist -> fixed PAL behind objList[palette_plist].addr (OBJECT and PAL share base).
                if (plists.palette_plist > 0 && plists.palette_plist < objCount)
                {
                    uint pointer = objBase + plists.palette_plist * 4;
                    Address.AddPointer(list, pointer + 0, palSize,
                        "MAP:" + U.ToHexString(mapid) + " PALETTE:" + U.ToHexString(plists.palette_plist),
                        Address.DataTypeEnum.PAL);
                }
                // palette2_plist -> fixed PAL (second palette; the value is 0 unless the patch is installed,
                // gated inside GetMapPListsWhereAddr).
                if (plists.palette2_plist > 0 && plists.palette2_plist < objCount)
                {
                    uint pointer = objBase + plists.palette2_plist * 4;
                    Address.AddPointer(list, pointer + 0, palSize,
                        "MAP:" + U.ToHexString(mapid) + " SECOND PALETTE:" + U.ToHexString(plists.palette2_plist),
                        Address.DataTypeEnum.PAL);
                }
                // obj_plist (a packed u16): low and high bytes each -> LZ77IMG behind objList[byte].addr.
                uint objLow = plists.obj_plist & 0xFF;
                uint objHigh = (plists.obj_plist >> 8) & 0xFF;
                if (objLow > 0 && objLow < objCount)
                {
                    uint pointer = objBase + objLow * 4;
                    Address.AddLZ77Pointer(list, pointer + 0,
                        "MAP:" + U.ToHexString(mapid) + " OBJ:" + U.ToHexString(objLow),
                        false, Address.DataTypeEnum.LZ77IMG);
                }
                if (objHigh > 0 && objHigh < objCount)
                {
                    uint pointer = objBase + objHigh * 4;
                    Address.AddLZ77Pointer(list, pointer + 0,
                        "MAP:" + U.ToHexString(mapid) + " OBJ:" + U.ToHexString(objHigh),
                        false, Address.DataTypeEnum.LZ77IMG);
                }
            }
        }

        /// <summary>Emit one MAPPOINTERS PLIST-table IFR (block 4, IsDataExists
        /// <c>i==0 ? true : i &lt; limit</c>, pointerIndexes {0}) for the RomInfo pointer slot
        /// <paramref name="rawPointer"/>. Returns the resolved table BASE (<c>p32(slot)</c>) so the
        /// caller can index per-map columns into it, or <see cref="U.NOT_FOUND"/> when the slot is
        /// unsafe (the per-map index guards on that). Reproduces the WF
        /// <c>InputFormRef.ReInitPointer(GetBasePointer(...)) + AddAddress(IFR, name, {0})</c>.</summary>
        static uint EmitMapPointerTable(ROM rom, List<Address> list, uint rawPointer, uint limit, string name)
        {
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return U.NOT_FOUND; // p32(slot) would read junk past EOF.
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return U.NOT_FOUND; // WF AddAddress early-returns; nothing to index either.
            }
            uint dataCount = MapPointerDataCountAt(rom, baseAddr, limit);
            uint length = 4 * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, name,
                Address.DataTypeEnum.InputFormRef, 4, new uint[] { 0 }));
            return baseAddr;
        }

        /// <summary>The MapPointer IFR DataCount for a resolved table base, or 0 when the base is
        /// unsafe/NOT_FOUND. IsDataExists = <c>i==0 ? true : i &lt; limit</c> (the WF Init rule).</summary>
        static uint MapPointerTableDataCount(ROM rom, uint baseAddr, uint limit)
        {
            if (baseAddr == U.NOT_FOUND || !U.isSafetyOffset(baseAddr, rom))
            {
                return 0;
            }
            return MapPointerDataCountAt(rom, baseAddr, limit);
        }

        /// <summary>getBlockDataCount over the MapPointer IFR rule (block 4,
        /// <c>i==0 ? true : i &lt; limit</c>). The callback reads nothing — it is a pure index walk —
        /// so getBlockDataCount's only extra cutoff is addr+4 &gt; Len (EOF), which is harmless.</summary>
        static uint MapPointerDataCountAt(ROM rom, uint baseAddr, uint limit)
        {
            return rom.getBlockDataCount(baseAddr, 4, (i, addr) => i == 0 || (uint)i < limit);
        }

        /// <summary>Reproduces <c>MapPointerForm.IsPlistSplits</c>: the PLIST tables are "split" iff the
        /// CONFIG base differs from every other table's base (a vanilla ROM shares one base across all
        /// PLIST types). Pure RomInfo <c>p32</c> reads — no PatchUtil dependency.</summary>
        static bool IsPlistSplits(ROM rom)
        {
            // EOF-harden (Copilot #1282): on synthetic/truncated ROMs a RomInfo PLIST slot can sit near
            // EOF, where ROM.p32 -> u32 -> check_safety throws. On valid ROMs these header slots are
            // always in-bounds, so guarding slot+3 before each p32 never changes the result; an unsafe
            // slot is treated as "not equal to config" (the comparison simply does not fire). A missing
            // config base -> not split (the conservative smaller limit).
            uint cfg = rom.RomInfo.map_config_pointer;
            if (!U.isSafetyOffset(cfg + 3, rom)) return false;
            uint a = rom.p32(cfg);
            if (PlistBaseEquals(rom, a, rom.RomInfo.map_tileanime1_pointer)) return false;
            if (PlistBaseEquals(rom, a, rom.RomInfo.map_obj_pointer)) return false;
            if (PlistBaseEquals(rom, a, rom.RomInfo.map_map_pointer_pointer)) return false;
            if (PlistBaseEquals(rom, a, rom.RomInfo.map_mapchange_pointer)) return false;
            if (PlistBaseEquals(rom, a, rom.RomInfo.map_event_pointer)) return false;
            if (rom.RomInfo.version == 6)
            {
                if (PlistBaseEquals(rom, a, rom.RomInfo.map_worldmapevent_pointer)) return false;
            }
            return true;
        }

        /// <summary>Guarded <c>a == p32(slot)</c> for <see cref="IsPlistSplits"/>: returns false (treat
        /// as "not equal", i.e. keep checking) when the 4-byte slot is near EOF, so a synthetic/truncated
        /// ROM never throws in <c>p32</c>. On valid ROMs the slot is always in-bounds, so the result is
        /// identical to the verbatim WF comparison.</summary>
        static bool PlistBaseEquals(ROM rom, uint a, uint slot)
        {
            if (!U.isSafetyOffset(slot + 3, rom)) return false;
            return a == rom.p32(slot);
        }

        /// <summary>
        /// <c>ExtraUnitForm.MakeAllDataLength</c> (slice 2j; FE8J = <c>version==8 &amp;&amp; is_multibyte</c>
        /// ONLY). The FE8J editor uses an if-chain at a HARDCODED base: <c>ifr.ReInit(0x37EE4)</c> (a
        /// DIRECT address, NOT a pointer slot — so the IFR BasePointer stays 0 -&gt; NOT_FOUND), block 4,
        /// IsDataExists <c>isSafetyPointer(u32(addr))</c>. WF: <c>AddAddress(IFR, "ExtraUnit", {})</c>
        /// then per entry <c>RecycleOldUnits("ExtraUnit", addr + 0)</c> + a 1-byte BIN flag at
        /// <c>i*0x14 + 0x37E10</c> (pointer NOT_FOUND, name "ExtraUnit Flag"). Reproduced VERBATIM.
        /// </summary>
        public static void EmitExtraUnit(ROM rom, List<Address> list)
        {
            EmitExtraUnitAt(rom, list, 0x37EE4);
        }

        /// <summary>ExtraUnit (FE8J) walk from an explicit table base (test seam — lets a synthetic ROM
        /// supply the base directly without the hardcoded 0x37EE4). <paramref name="baseAddrRaw"/> is the
        /// WF <c>ReInit</c> DIRECT base address (the unit-pointer entry table). The flag addresses are the
        /// WF hardcoded <c>i*0x14 + 0x37E10</c> — those are absolute FE8J locations, so the test base is
        /// only used to vary the entry table.</summary>
        public static void EmitExtraUnitAt(ROM rom, List<Address> list, uint baseAddrRaw)
        {
            const uint block = 4;
            const uint flagBase = 0x37E10; // WF GetFlagAddr(i) = i*0x14 + 0x37E10.
            const uint flagStride = 0x14;

            // WF ifr.ReInit(0x37EE4): BaseAddress = 0x37EE4 (DIRECT), BasePointer = 0 -> NOT_FOUND.
            uint baseAddr = U.toOffset(baseAddrRaw);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return; // WF AddAddress early-returns when !isSafetyOffset(BaseAddress).
            }
            // IsDataExists = isSafetyPointer(u32(addr)). getBlockDataCount stops at addr+4 > Len too.
            uint dataCount = rom.getBlockDataCount(baseAddr, block,
                (i, addr) => U.isSafetyPointer(rom.u32(addr)));

            // Main IFR: pointer = NOT_FOUND (BasePointer 0), pointerIndexes {}.
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, U.NOT_FOUND, "ExtraUnit",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { }));

            for (uint i = 0; i < dataCount; i++)
            {
                uint addr = baseAddr + i * block;
                // Guard the full 4-byte entry before RecycleOldUnits reads u32(addr) (addr+3).
                if (!U.isSafetyOffset(addr + 3, rom))
                {
                    break;
                }
                EmitRecycleOldUnits(rom, list, "ExtraUnit", addr + 0);

                // 1-byte BIN flag at the absolute FE8J location i*0x14 + 0x37E10 (pointer NOT_FOUND). WF
                // calls Address.AddAddress directly (no pre-guard); the Core AddAddress safety-guards the
                // addr internally and early-returns when out of range (e.g. a smaller synthetic ROM).
                uint flagAddr = i * flagStride + flagBase;
                Address.AddAddress(list, flagAddr, 1, U.NOT_FOUND, "ExtraUnit Flag",
                    Address.DataTypeEnum.BIN);
            }
        }

        /// <summary>
        /// <c>ExtraUnitFE8UForm.MakeAllDataLength</c> (slice 2j; FE8U = <c>version==8 &amp;&amp;
        /// !is_multibyte</c> ONLY). The FE8U editor is a TABLE at a HARDCODED pointer slot
        /// <c>0x37D88</c> (constructed as the IFR BasePointer, so BaseAddress = <c>p32(0x37D88)</c>),
        /// block 8, IsDataExists <c>isSafetyPointer(u32(addr+4))</c>. WF:
        /// <c>AddAddress(IFR, "ExtraUnit", {})</c> then per entry
        /// <c>RecycleOldUnits("ExtraUnit", addr + 4)</c> — NO flag block (the flag is the +0 field, an
        /// in-table u32, not a separate region). Reproduced VERBATIM.
        /// </summary>
        public static void EmitExtraUnitFE8U(ROM rom, List<Address> list)
        {
            EmitExtraUnitFE8UAt(rom, list, 0x37D88);
        }

        /// <summary>ExtraUnit (FE8U) walk from an explicit pointer slot (test seam). See
        /// <see cref="EmitExtraUnitFE8U"/> for the verbatim WF reproduction. <paramref name="rawPointer"/>
        /// is the 4-byte pointer SLOT (WF BasePointer 0x37D88) whose <c>p32</c> is the table base.</summary>
        public static void EmitExtraUnitFE8UAt(ROM rom, List<Address> list, uint rawPointer)
        {
            const uint block = 8;

            // WF Init(basepointer=0x37D88): BasePointer = slot, BaseAddress = p32(slot). Guard slot+3.
            uint pointer = U.toOffset(rawPointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return; // WF AddAddress early-returns.
            }
            // IsDataExists = isSafetyPointer(u32(addr+4)). getBlockDataCount only fires while addr+8<=Len,
            // so the +4 read is always in bounds.
            uint dataCount = rom.getBlockDataCount(baseAddr, block,
                (i, addr) => U.isSafetyPointer(rom.u32(addr + 4)));

            // Main IFR: pointer = BasePointer (0x37D88, safe here), pointerIndexes {}.
            uint length = block * (dataCount + 1);
            list.Add(new Address(baseAddr, length, pointer, "ExtraUnit",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { }));

            for (uint i = 0; i < dataCount; i++)
            {
                uint addr = baseAddr + i * block;
                // Guard the full 8-byte entry before RecycleOldUnits reads u32(addr+4) (addr+7).
                if (!U.isSafetyOffset(addr + 7, rom))
                {
                    break;
                }
                EmitRecycleOldUnits(rom, list, "ExtraUnit", addr + 4);
            }
        }

        /// <summary>
        /// Reproduces <c>EventUnitForm.RecycleOldUnits(ref list, basename, script_pointer)</c> +
        /// <c>RecycleOldUnitsLow</c> (slice 2j). The WF helper recovers the EVENT-UNIT region behind an
        /// embedded script pointer (the <c>script_pointer</c> field of an ExtraUnit entry): it
        /// dereferences <c>script_addr = u32(script_pointer)</c> (a pointer; NULL/unsafe -&gt; emit
        /// nothing), then walks an EventUnit IFR rooted at <paramref name="scriptPointer"/> — block
        /// <c>eventunit_data_size</c>, IsDataExists <c>u8(addr) != 0</c>. VERBATIM per version:
        /// <list type="bullet">
        ///   <item><b>version &lt;= 7</b>: one IFR Address (<c>AddAddress(IFR, basename + " EVENT UNIT",
        ///   {})</c>) — pointer = the script-pointer field, pointerIndexes EMPTY.</item>
        ///   <item><b>version 8</b>: one IFR Address with pointerIndexes <c>{8}</c>, PLUS for each entry
        ///   whose <c>u8(addr+7) &gt; 0</c> a <c>count*8</c>-byte BIN block at <c>p32(addr+8)</c> behind
        ///   the <c>addr+8</c> field (the FE8 "after-coordinate" placement list), named
        ///   <c>basename + " EVENT UNIT COORD " + i</c>.</item>
        /// </list>
        /// All reads are pure ROM reads (no Form / disasm / session state), so this is faithfully
        /// headless. EOF-safe: the script-pointer field and every per-entry read are bounds-guarded.
        /// </summary>
        public static void EmitRecycleOldUnits(ROM rom, List<Address> list, string basename, uint scriptPointer)
        {
            // WF: script_addr = u32(script_pointer); if (!isPointer(script_addr)) return; toOffset;
            // if (!isSafetyOffset(script_addr)) return. Guard the full 4-byte field (field+3) first.
            uint field = U.toOffset(scriptPointer);
            if (!U.isSafetyOffset(field + 3, rom))
            {
                return;
            }
            uint scriptAddrRaw = rom.u32(field);
            if (!U.isPointer(scriptAddrRaw))
            {
                return;
            }
            uint scriptAddr = U.toOffset(scriptAddrRaw);
            if (!U.isSafetyOffset(scriptAddr, rom))
            {
                return;
            }

            // WF InputFormRef.ReInitPointer(script_pointer): BasePointer = field, BaseAddress = scriptAddr.
            // EventUnitForm.Init: block = eventunit_data_size, IsDataExists = u8(addr) != 0.
            uint block = rom.RomInfo.eventunit_data_size;
            if (block == 0)
            {
                return; // a zero block would spin getBlockDataCount; not real data.
            }
            uint dataCount = rom.getBlockDataCount(scriptAddr, block, (i, addr) => rom.u8(addr) != 0);
            uint length = block * (dataCount + 1);

            if (rom.RomInfo.version <= 7)
            {
                // v<=7: AddAddress(IFR, basename + " EVENT UNIT", {}) — pointer = field, indexes EMPTY.
                list.Add(new Address(scriptAddr, length, field, basename + " EVENT UNIT",
                    Address.DataTypeEnum.InputFormRef, block, new uint[] { }));
                return;
            }

            // v8: AddAddress(IFR, basename + " EVENT UNIT", {8}) — pointerIndexes {8} (the +8 after-coord
            // pointer field in each EventUnit record).
            list.Add(new Address(scriptAddr, length, field, basename + " EVENT UNIT",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 8 }));

            // v8 per-entry: for each entry with u8(addr+7) > 0 add a count*8 BIN at p32(addr+8).
            for (uint i = 0; i < dataCount; i++)
            {
                uint addr = scriptAddr + i * block;
                // Guard the full record's deepest read (p32(addr+8) -> addr+11) before any read so a
                // corrupted DataCount near EOF skips instead of throwing.
                if (addr + 12 > (uint)rom.Data.Length)
                {
                    break;
                }
                uint count = rom.u8(addr + 7);
                if (count == 0)
                {
                    continue; // WF: count > 0 gate.
                }
                uint afterAddress = rom.p32(addr + 8);
                // WF Address.AddAddress(after_address, count*8, addr+8, name, BIN). The Core AddAddress
                // early-returns when !isSafetyOffset(after_address), matching WF's downstream behaviour.
                Address.AddAddress(list, afterAddress, count * 8, addr + 8,
                    basename + " EVENT UNIT COORD " + i, Address.DataTypeEnum.BIN);
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
                case DataCountRule.MoveIconRule:
                {
                    // ImageUnitMoveIconFrom.Init verbatim. The class-count cap is computed ONCE (WF
                    // reads ClassForm.DataCount() once in Init); ONLY the extremes are coerced (<=0 ->
                    // 0x7f, >0xFF -> 0xFF; values 1..0xFF stay AS-IS — not a clamp to [0x7f,0xff]), then
                    // decremented. The per-entry rule: i>=cap -> stop; i==0 -> always; else the +0
                    // (RuleOffset) image pointer must be a ROM pointer OR NULL.
                    uint classCount = ClassDataCount(rom);
                    if (classCount <= 0) classCount = 0x7f;
                    else if (classCount > 0xFF) classCount = 0xFF;
                    classCount--;
                    return (i, addr) =>
                    {
                        if (i >= classCount) return false;
                        if (i == 0) return true;
                        return U.isPointerOrNULL(rom.u32(addr + d.RuleOffset));
                    };
                }
                case DataCountRule.NestedPointerAt:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // ImageCGForm.Init verbatim: +RuleOffset is a pointer whose target's first u32 is
                        // ALSO a pointer (the 10-image-pointer-array signature). EOF-harden the inner read.
                        uint p = rom.u32(addr + d.RuleOffset);
                        if (!U.isPointer(p) || !U.isSafetyPointer(p, rom)) return false;
                        uint pOff = U.toOffset(p);
                        if (pOff + 4 > (uint)rom.Data.Length) return false;
                        uint p2 = rom.u32(pOff);
                        if (!U.isPointer(p2) || !U.isSafetyPointer(p2, rom)) return false;
                        return true;
                    };
                case DataCountRule.U16EqualAt:
                    return (i, addr) =>
                    {
                        if (i >= d.MaxCount) return false;
                        // ImageCGFE7UForm.Init verbatim: continue while u16(addr+RuleOffset) == StopValue.
                        return rom.u16(addr + d.RuleOffset) == (ushort)d.RuleStopValue;
                    };
                case DataCountRule.ItemIconMaxRule:
                {
                    // ImageItemIconForm.Init verbatim: itemMax = GetIconMax() read ONCE in Init; the
                    // per-entry rule is `i <= itemMax` (so count = itemMax + 1).
                    uint itemMax = GetIconMax(rom);
                    return (i, addr) => i <= itemMax;
                }
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

            // ---- slice 2f: version-agnostic AI / Arena-weapon / Mant flat forms ----
            // These are called UNCONDITIONALLY in the WF MakeAllStructPointersList (between
            // DoEvents checkpoints 3 and 4). Each is a pure RomInfo table walk whose count + every
            // length is a pure ROM read (no Huffman/LZ77/disasm/PatchUtil/config-file), expressible
            // with the existing descriptor + AsmFunction/FixedPointer SubWalk machinery.

            // AIMapSettingForm.MakeAllDataLength — Info "AIMapSetting", block 4, base
            // ai_map_setting_pointer, IsDataExists = u8(addr)!=0xFF (U8NotEqual @0), pointerIndexes {}.
            // Flat — no per-entry sub-walk.
            l.Add(new StructDescriptor
            {
                Name = "AIMapSetting",
                PointerField = r => r.RomInfo.ai_map_setting_pointer,
                BlockSize = 4,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 0xFF,
                PointerIndexes = new uint[] { },
            });

            // AIPerformStaffForm.MakeAllDataLength — Info "AIPerformStaff", block 8, base
            // ai_preform_staff_pointer, IsDataExists = u16(addr)!=0 (U16NotZero @0), pointerIndexes {4}.
            // PLUS Address.AddFunctions(MakeList(), 4, "AIPerformStaff_ASM_"): one ASM AddFunction per
            // entry at offset 4. Reproduced as a SubKind.AsmFunction sub-walk @4 (reads u32(p+4),
            // ProgramAddrToPlain, length-0 ASM block — extent at rebuild). The WF per-entry label is the
            // IFR display name + "_ASM_"; the producer uses a static label (non-load-bearing for
            // relocation, same convention as StatusOption/SoundFootSteps AsmFunction sub-walks).
            // EOF-safe: getBlockDataCount guarantees p+8<=Length, so u32(p+4) (deepest p+7) is in bounds.
            l.Add(new StructDescriptor
            {
                Name = "AIPerformStaff",
                PointerField = r => r.RomInfo.ai_preform_staff_pointer,
                BlockSize = 8,
                Rule = DataCountRule.U16NotZero,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 4 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.AsmFunction, Name = (r, i) => "AIPerformStaff_ASM_" },
                },
            });

            // AIPerformItemForm.MakeAllDataLength — identical shape to AIPerformStaff but base
            // ai_preform_item_pointer, Info "AIPerformItem", per-entry ASM label "AIPerformItem_ASM_".
            l.Add(new StructDescriptor
            {
                Name = "AIPerformItem",
                PointerField = r => r.RomInfo.ai_preform_item_pointer,
                BlockSize = 8,
                Rule = DataCountRule.U16NotZero,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 4 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.AsmFunction, Name = (r, i) => "AIPerformItem_ASM_" },
                },
            });

            // MantAnimationForm.MakeAllDataLength — Info "Mant", block 4, base mant_command_pointer,
            // IsDataExists = U.isPointer(u32(addr+0)) (PointerAt @0 — a NULL slot terminates),
            // pointerIndexes {0}. PLUS per entry: Address.AddPointer(p+0, 0x10, "MANT_P:0x<i>", POINTER)
            // — a fixed 0x10-byte POINTER block behind the +0 pointer (SubKind.FixedPointer @0). The
            // per-entry label "MANT_P:0x<i>" is reproduced verbatim via the sub-walk Name callback.
            l.Add(new StructDescriptor
            {
                Name = "Mant",
                PointerField = r => r.RomInfo.mant_command_pointer,
                BlockSize = 4,
                Rule = DataCountRule.PointerAt,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 0 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.FixedPointer, FixedLength = 0x10, DataType = Address.DataTypeEnum.POINTER, Name = (r, i) => "MANT_P:" + U.To0xHexString((int)i) },
                },
            });

            // ArenaEnemyWeaponForm.MakeAllDataLength — Info "ArenaEnemyWeapon", block 1, base
            // arena_enemy_weapon_basic_pointer, IsDataExists = i<8 (FixedCount 8), pointerIndexes {}.
            // VERBATIM QUIRK: WF emits this descriptor TWICE — the second block reads `Init(null)`
            // (the BASIC table again), NOT `N_Init` (the rankup table), so the rankup_pointer / i<0x1A
            // table is never emitted. The duplicate-basic emit is a WF copy/paste, reproduced exactly
            // (two identical descriptors) to keep the produced Address list byte-identical.
            for (int dup = 0; dup < 2; dup++)
            {
                l.Add(new StructDescriptor
                {
                    Name = "ArenaEnemyWeapon",
                    PointerField = r => r.RomInfo.arena_enemy_weapon_basic_pointer,
                    BlockSize = 1,
                    Rule = DataCountRule.FixedCount,
                    RuleFixedCount = 8,
                    PointerIndexes = new uint[] { },
                });
            }

            // ---- slice 2e: flat LZ77-image + palette IFR-loop forms (version-agnostic) ----
            // Each is the Core port of an ImageXxxForm.MakeAllDataLength that emits a main IFR Address
            // (AddressWinForms.AddAddress(list, IFR, name, pointerIndexes) -> length = block ×
            // (DataCount+1)) PLUS a per-entry loop of AddLZ77Pointer / AddPointer columns. The
            // per-entry columns are the SubWalks (Lz77Pointer / FixedPointer). EVERY length is either
            // LZ77.getCompressedSize (EOF-safe, 0 on malformed) or a CONSTANT palette/image size — no
            // ImageUtil/TSA-header/frame-walk dependency. (slice 2k added the AddHeaderTSAPointer header-TSA
            // forms; slice 2n adds ImageUnitMoveIconFrom — its per-entry AP column uses SubKind.ApPointer /
            // EmitApPointer, whose length is the verbatim Core ImageUtilAPCore.CalcAPLength frame/anime walk.)
            // The forms that DO still need an un-ported subsystem (ImageUtil*.RecycleOldAnime, config-file
            // g_TSAAnime/g_ROMAnime, ImageUtilOAM, IsHalfBodyFlag) stay in GetNotYetPortedForms.

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

            // ImageItemIconForm.MakeAllDataLength (slice 2t) — Info "ItemIcon", block 128
            // ((2*8*2*8)/2, 16-color icon sheet), base icon_pointer, IsDataExists = `i <= GetIconMax()`
            // (ItemIconMaxRule), pointerIndexes {} (empty — the icon SHEET has no embedded LZ77 image or
            // palette pointers to relocate; the palette lives at the separate icon_palette_pointer). NO
            // SubWalks. Version-agnostic call site (WF line 2453, unconditional). GetIconMax is a verbatim
            // pure-ROM port (repoint -> 0xFE; FE7U FEditorAdv AutoPatch probe at 0xCB51A -> max-1).
            l.Add(new StructDescriptor
            {
                Name = "ItemIcon",
                PointerField = r => r.RomInfo.icon_pointer,
                BlockSize = (2 * 8 * 2 * 8) / 2,
                Rule = DataCountRule.ItemIconMaxRule,
                PointerIndexes = new uint[] { },
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

            // ImageUnitMoveIconFrom.MakeAllDataLength (slice 2n) — Info "MoveUnitIcon", block 8, base
            // unit_move_icon_pointer, IsDataExists = MoveIconRule (class-count-bounded; i==0 always; else
            // isPointerOrNULL(u32+0)), pointerIndexes {0,4}. Per entry: LZ77IMG @0 (the move-icon sheet)
            // + AP @4 (the move-anime stream). The per-entry WF label is name = "MoveUnitIcon " +
            // To0xHexString(i); the LZ77 column uses that name verbatim and the AP column uses name + " AP".
            // The AP block's length is ImageUtilAPCore.CalcAPLength (the verbatim Core port of WF
            // ImageUtilAP.CalcAPLength = Parse + GetLength) over the dereferenced +4 target — see
            // SubKind.ApPointer / EmitApPointer.
            l.Add(new StructDescriptor
            {
                Name = "MoveUnitIcon",
                PointerField = r => r.RomInfo.unit_move_icon_pointer,
                BlockSize = 8,
                Rule = DataCountRule.MoveIconRule,
                RuleOffset = 0,
                PointerIndexes = new uint[] { 0, 4 },
                SubWalks = new List<SubWalk>
                {
                    new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "MoveUnitIcon " + U.To0xHexString((uint)i) },
                    new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.ApPointer, Name = (r, i) => "MoveUnitIcon " + U.To0xHexString((uint)i) + " AP" },
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

            // TextCharCodeForm.MakeAllDataLength (slice 2m) — the Huffman char-code (mask) table.
            // WF Init: base mask_pointer, block 4, IsDataExists = u8(addr) != 255, then
            // AddAddress(list, IFR, "TextCharCode", new uint[] {}) — EMPTY pointerIndexes, default
            // (InputFormRef) DataType. This is a flat single-table walk with no embedded sub-pointer,
            // so it maps 1:1 onto the DataCountRule.U8NotEqual descriptor (@ offset 0, stop 255).
            // The per-entry getString/FETextEncode name in WF's display callback is non-load-bearing
            // for relocation (the producer needs only addr/length/pointer), so it is not reproduced.
            l.Add(new StructDescriptor
            {
                Name = "TextCharCode",
                PointerField = r => r.RomInfo.mask_pointer,
                BlockSize = 4,
                Rule = DataCountRule.U8NotEqual,
                RuleOffset = 0,
                RuleStopValue = 255,
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

                // EDStaffRollForm.MakeAllDataLength (FE8, slice 2h) — Info "EDStaffRoll", block 8, base
                // ed_staffroll_image_pointer, IsDataExists = isPointer(u32(addr+0)) && i < 12. The WF
                // lambda returns false on a non-pointer OR once i reaches 12, so this is PointerAt @0
                // with MaxCount 12 (the PointerAt rule checks `i >= MaxCount` before the pointer, so the
                // combined result is `i < 12 && isPointer(u32+0)` — identical to WF). pointerIndexes
                // {0,4}. Per entry: LZ77IMG @0, LZ77TSA @4 (both labelled "EDStaffRoll" verbatim — WF
                // reuses the single `name` string for the main IFR and both columns).
                l.Add(new StructDescriptor
                {
                    Name = "EDStaffRoll",
                    PointerField = r => r.RomInfo.ed_staffroll_image_pointer,
                    BlockSize = 8,
                    Rule = DataCountRule.PointerAt,
                    RuleOffset = 0,
                    MaxCount = 12,
                    PointerIndexes = new uint[] { 0, 4 },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "EDStaffRoll" },
                        new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77TSA, Name = (r, i) => "EDStaffRoll" },
                    },
                });

                // OPPrologueForm.MakeAllDataLength (FE8, slice 2h) — Info "OPPrologue", block 12, base
                // op_prologue_image_pointer, IsDataExists = isPointer(u32(addr+0)) (PointerAt @0),
                // pointerIndexes {0,4}. ALSO a standalone palette pointer: AddPointer(
                // op_prologue_palette_color_pointer, 2*16=0x20, "OPPrologue Palette", PAL) emitted ONCE
                // (not per entry) — modelled as an ExtraFixedPointer (DataType override PAL). Per entry:
                // LZ77IMG @0 ("OPPrologue image"), LZ77TSA @4 ("OPPrologue tsa").
                l.Add(new StructDescriptor
                {
                    Name = "OPPrologue",
                    PointerField = r => r.RomInfo.op_prologue_image_pointer,
                    BlockSize = 12,
                    Rule = DataCountRule.PointerAt,
                    RuleOffset = 0,
                    PointerIndexes = new uint[] { 0, 4 },
                    ExtraFixedPointers = new[]
                    {
                        new ExtraFixedPointer { PointerField = r => r.RomInfo.op_prologue_palette_color_pointer, FixedLength = 2 * 16, Name = "OPPrologue Palette", DataType = Address.DataTypeEnum.PAL },
                    },
                    SubWalks = new List<SubWalk>
                    {
                        new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "OPPrologue image" },
                        new SubWalk { EmbeddedPointerOffset = 4, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77TSA, Name = (r, i) => "OPPrologue tsa" },
                    },
                });

                if (rom.RomInfo.is_multibyte)
                {
                    // OPClassFontForm.MakeAllDataLength (FE8, slice 2h) — called in the WF version==8
                    // branch ONLY inside `if (is_multibyte)` (the FE8U non-multibyte path uses
                    // OPClassFontFE8UForm, which is a different — still-deferred — form). Gated
                    // identically here. Info "OPClassFont", block 4, base op_class_font_pointer,
                    // IsDataExists = isPointer(u32(addr+0)) (PointerAt @0), pointerIndexes {0}. Per entry:
                    // ONE LZ77IMG column at 0, labelled "OPClassFont 0x<i>" (U.To0xHexString) verbatim.
                    l.Add(new StructDescriptor
                    {
                        Name = "OPClassFont",
                        PointerField = r => r.RomInfo.op_class_font_pointer,
                        BlockSize = 4,
                        Rule = DataCountRule.PointerAt,
                        RuleOffset = 0,
                        PointerIndexes = new uint[] { 0 },
                        SubWalks = new List<SubWalk>
                        {
                            new SubWalk { EmbeddedPointerOffset = 0, Kind = SubKind.Lz77Pointer, DataType = Address.DataTypeEnum.LZ77IMG, Name = (r, i) => "OPClassFont " + U.To0xHexString((uint)i) },
                        },
                    });
                }
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
                // Forms called ONLY inside the WF `else if (version == 6)` branch. UnitFE6Form is ported
                // in slice 2f via the dedicated EmitUnitFE6 walker (its base is p32(unit_pointer)+
                // unit_datasize with a NOT_FOUND pointer slot, which the pointer-slot descriptor model
                // cannot express). SupportUnitFE6Form is ported in slice 2h via the version-aware
                // EmitSupportUnit walker (FE6 block 32, first-field u8, name "SupportUnitFE6"; its
                // owner-lookahead count rule = SupportUnitNavigation.GetUnitIdAtSupportAddr).
                // WorldMapEventPointerFE6Form (PLIST event-scan) and MapSettingFE6Form (IsMapSettingEnd +
                // CString — needs the WF cached text count, not in Core) stay deferred.
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

        // =====================================================================================
        // slice 2r: SoundRoom + SongTable + MapSetting (misc tractable forms)
        // =====================================================================================

        /// <summary>
        /// <c>SoundRoomForm.MakeAllDataLength</c> (slice 2r). Called in the WF version==8 AND version==7
        /// branches (NOT FE6). Main IFR: base <c>p32(sound_room_pointer)</c>, block
        /// <c>sound_room_datasize</c>, IsDataExists = <c>u32(addr)==0xFFFFFFFF</c> stop OR
        /// <c>i&gt;10 &amp;&amp; IsEmpty(addr, datasize*10)</c> stop (both pure ROM reads), pointerIndexes
        /// {8,12}, type <see cref="Address.DataTypeEnum.InputFormRef_MIX"/>. On FE7 ONLY (the WF
        /// <c>version==7</c> guard), each entry additionally tracks its C-string song name: a BIN block
        /// (length == decoded strlen, NO +1) behind the embedded pointer at +12 — reproduced VERBATIM via
        /// the existing <see cref="SubKind.BinString"/> sub-walk (WF: <c>getString(p32(addr+12))</c> +
        /// <c>Address.AddAddress(songname, len, addr+12, BIN)</c>; <see cref="Address.AddAddress"/> already
        /// filters an unsafe/NULL target, so the BinString <c>isSafetyOffset</c> pre-guard is
        /// byte-equivalent and harder against a near-EOF read). The name BIN needs a SystemTextEncoder; the
        /// flat sub-walk loop skips it gracefully (and relocates only the main IFR) when none is loaded.
        /// </summary>
        public static void EmitSoundRoom(ROM rom, List<Address> list)
        {
            // FE7 adds the per-entry C-string name BIN @ +12; FE8 has no per-entry sub-walk.
            bool isFE7 = rom.RomInfo.version == 7;
            var d = new StructDescriptor
            {
                Name = "SoundRoom",
                PointerField = r => r.RomInfo.sound_room_pointer,
                BlockSize = rom.RomInfo.sound_room_datasize,
                Rule = DataCountRule.TerminatorWithEmptyGuard,
                RuleOffset = 0,
                RuleWidth = 4,
                RuleStopValue = 0xFFFFFFFF,
                HasEmptyGuard = true,
                PointerIndexes = new uint[] { 8, 12 },
                DataType = Address.DataTypeEnum.InputFormRef_MIX,
                SubWalks = isFE7
                    ? new List<SubWalk>
                      {
                          new SubWalk { EmbeddedPointerOffset = 12, Kind = SubKind.BinString },
                      }
                    : null,
            };
            WalkAndAdd(rom, list, d);
        }

        /// <summary>
        /// <c>SongTableForm.MakeAllDataLength</c> (slice 2r). Called UNCONDITIONALLY (all versions). The
        /// song table base is <c>GetSoundTablePointer()</c> (reproduced VERBATIM by
        /// <see cref="GetSoundTablePointer"/>): the <c>sound_table_pointer</c> RomInfo slot when it
        /// dereferences to a ROM pointer, else the signature-scanned slot (<see cref="FindSongTablePointer"/>
        /// — returns the SLOT, not the dereferenced table, so the producer keeps the pointer field to
        /// relocate), else 0. Main IFR: block 8, IsDataExists = <c>isPointer(u32(addr))</c>
        /// (<see cref="DataCountRule.PointerAt"/> @0), pointerIndexes {0}. Per entry the song SCORE and the
        /// instrument tree are tracked by <see cref="EmitRecycleOldSong"/> /
        /// <see cref="EmitRecycleOldInstrument"/> (the verbatim ports of <c>SongUtil.RecycleOldSong</c> /
        /// <c>SongInstrumentForm.RecycleOldInstrument</c> — both pure-ROM walks, NO Drawing/MIDI-import).
        /// </summary>
        public static void EmitSongTable(ROM rom, List<Address> list)
        {
            EmitSongTableAt(rom, list, GetSoundTablePointer(rom));
        }

        /// <summary>SongTable walk from an explicit base-pointer SLOT (test seam — lets a synthetic ROM
        /// drive the per-entry recycle without the RomInfo/signature base resolution).</summary>
        public static void EmitSongTableAt(ROM rom, List<Address> list, uint pointer)
        {
            const uint block = 8;
            pointer = U.toOffset(pointer);
            // HARDEN: guard the slot's full u32 read (root+3) before p32 — a base-pointer slot within 4
            // bytes of EOF passes isSafetyOffset but its 4-byte read overruns (ROM.p32 only guards addr,
            // not addr+3). Valid-ROM-equivalent (the real slot is well inside the ROM).
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // SongTableForm.Init IsDataExists: U.isPointer(u32(addr)). getBlockDataCount bounds
            // addr+block(8)<=Length, so u32(addr) (addr+3) is always in-bounds.
            uint dataCount = rom.getBlockDataCount(baseAddr, block,
                (i, addr) => U.isPointer(rom.u32(addr)));

            // AddressWinForms.AddAddress(list, IFR, "SongTable", {0}): length = block*(count+1).
            list.Add(new Address(baseAddr, block * (dataCount + 1), pointer, "SongTable",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0 }));

            // Per entry: MakeAllDataLength_Song_And_Inst — the SONG score + the instrument tree.
            uint songpointer = baseAddr;
            for (int i = 0; i < dataCount; i++, songpointer += block)
            {
                uint songaddr = rom.p32(songpointer);
                if (!U.isSafetyOffset(songaddr, rom))
                {
                    continue;
                }

                EmitRecycleOldSong(rom, list, "Song" + U.ToHexString(i) + " ", songpointer);

                // instpointer = songaddr + 4 (the voicegroup pointer slot lives 4 bytes into the header).
                EmitRecycleOldInstrument(rom, list, "SongInst" + U.ToHexString(i) + " ", songaddr + 4);
            }
        }

        /// <summary>VERBATIM port of <c>SongTableForm.GetSoundTablePointer()</c>: the RomInfo
        /// <c>sound_table_pointer</c> slot when <c>u32(slot)</c> is a ROM pointer, else the
        /// signature-scanned slot (<see cref="FindSongTablePointer"/>) when it is a safe offset whose
        /// <c>u32</c> is a ROM pointer, else 0. Returns the pointer SLOT (the dereference is the IFR's job).</summary>
        public static uint GetSoundTablePointer(ROM rom)
        {
            uint p = rom.RomInfo.sound_table_pointer;
            // Guard the slot's full u32 read (root+3) before reading (WF reads it raw on a valid ROM).
            if (U.isSafetyOffset(p + 3, rom))
            {
                uint a = rom.u32(p);
                if (U.isPointer(a))
                {
                    return p;
                }
            }
            p = FindSongTablePointer(rom);
            if (U.isSafetyOffset(p, rom) && U.isSafetyOffset(p + 3, rom))
            {
                uint a = rom.u32(p);
                if (U.isPointer(a))
                {
                    return p;
                }
            }
            return 0;
        }

        /// <summary>VERBATIM port of <c>SongUtil.FindSongTablePointer(byte[])</c>: grep the 30-byte
        /// engine signature, the song-table pointer SLOT lives at <c>found + signatureLen + 10</c>; return
        /// that slot offset when its <c>u32</c> is a ROM pointer, else <c>U.NOT_FOUND</c>. (Distinct from
        /// Core <c>SongExchangeCore.FindSongTablePointerByScan</c>, which DEREFERENCES and returns the
        /// TABLE START — the producer needs the SLOT so the pointer field gets relocated.)</summary>
        public static uint FindSongTablePointer(ROM rom)
        {
            byte[] data = rom.Data;
            byte[] search = new byte[] {
                0x00, 0xB5, 0x00, 0x04, 0x07, 0x4A, 0x08, 0x49,
                0x40, 0x0B, 0x40, 0x18, 0x83, 0x88, 0x59, 0x00,
                0xC9, 0x18, 0x89, 0x00, 0x89, 0x18, 0x0A, 0x68,
                0x01, 0x68, 0x10, 0x1C, 0x00, 0xF0
            };
            uint foundPoint = U.Grep(data, search);
            if (foundPoint == U.NOT_FOUND)
            {
                return U.NOT_FOUND;
            }
            uint songpointer = foundPoint + (uint)search.Length + 10;
            songpointer = U.toOffset(songpointer);
            // HARDEN: WF reads u32(songpointer) raw; guard the full read extent (root+3) so a signature
            // match near EOF cannot read past the array (valid-ROM-equivalent — the slot is well inside).
            if (!U.isSafetyZArray(songpointer + 3, data))
            {
                return U.NOT_FOUND;
            }
            uint songlist = U.u32(data, songpointer);
            if (!U.isPointer(songlist))
            {
                return U.NOT_FOUND;
            }
            return songpointer;
        }

        /// <summary>VERBATIM port of <c>SongUtil.RecycleOldSong(ref list, basename, track_basepointer)</c>:
        /// the song HEADER (length <c>8 + trackcount*4</c>, type SONGTRACK) behind the embedded pointer at
        /// <paramref name="trackBasePointer"/>, plus one SONGSCORE block per track (length
        /// <c>Padding4(fineaddr - startaddr + 1)</c>, computed from the parsed track stream). The track
        /// parse is <see cref="SongMidiCore.ParseTracks"/> (the Core port of <c>SongUtil.ParseTrack</c> —
        /// the same FINE-terminated bytecode walk; only the per-track START / FINE byte addresses are used,
        /// which the inserted loop-label codes do NOT shift). Pure ROM; no encoder.</summary>
        public static void EmitRecycleOldSong(ROM rom, List<Address> list, string basename, uint trackBasePointer)
        {
            // HARDEN: guard the embedded-pointer slot's full u32 read (root+3) — WF reads it raw after an
            // isSafetyOffset(slot) check, which does not cover slot+3. EmitSongTableAt always passes an
            // in-bounds slot (getBlockDataCount bounds base+i*8+8<=Length), but guard for any caller.
            if (!U.isSafetyOffset(trackBasePointer + 3, rom))
            {
                return;
            }
            uint trackBaseAddress = rom.u32(trackBasePointer);
            if (!U.isPointer(trackBaseAddress))
            {
                return;
            }
            trackBaseAddress = U.toOffset(trackBaseAddress);
            if (!U.isSafetyOffset(trackBaseAddress, rom))
            {
                return;
            }

            uint trackcount = rom.u8(trackBaseAddress);
            Address.AddPointer(list, trackBasePointer, 8 + trackcount * 4,
                basename + "HEADER", Address.DataTypeEnum.SONGTRACK);

            List<SongMidiCore.Track> tracks = SongMidiCore.ParseTracks(rom, trackBaseAddress, trackcount);
            for (int i = 0; i < tracks.Count; i++)
            {
                int len = tracks[i].codes.Count;
                if (len <= 0)
                {
                    continue;
                }
                uint startaddr = tracks[i].codes[0].addr;
                uint fineaddr = tracks[i].codes[len - 1].addr;
                Address.AddPointer(list, tracks[i].basepointer,
                    U.Padding4(fineaddr - startaddr + 1),
                    basename + "TRACK " + U.To0xHexString(i),
                    Address.DataTypeEnum.SONGSCORE);
            }
        }

        /// <summary>VERBATIM port of <c>SongInstrumentForm.RecycleOldInstrument(ref list, basename,
        /// voca_basepointer)</c>: the instrument-list IFR (block 12, pointerIndexes {4,8}) behind the
        /// embedded pointer, plus per-entry DirectSound / Wave / Drum / MultiSample blocks. RECURSIVE
        /// (drum @0x80 and multisample @0x40 recurse into a nested voice table) with a shared
        /// visited-list dedup (skip when <c>list</c> already holds an Address at the same target) — both
        /// reproduced exactly. The DirectSound length comes from
        /// <see cref="FEBuilderGBA.Core.SongDirectSoundWavCore"/> (the Core ports of
        /// <c>SongUtil.GetDirectSoundWaveDataLength</c> / <c>IsDirectSoundData</c> /
        /// <c>IsDirectSoundWaveCompressedDPCM</c>); Wave is a fixed 16, MultiSample's sample table is a
        /// fixed 128. Pure ROM; no encoder. The IFR count rule is the verbatim
        /// <see cref="SongInstrumentExists"/>.</summary>
        public static void EmitRecycleOldInstrument(ROM rom, List<Address> list, string basename, uint vocaBasePointer)
        {
            // HARDEN: guard the embedded-pointer slot's full u32 read (root+3) — WF reads it raw after an
            // isSafetyOffset(slot) check, which does not cover slot+3 (a near-EOF slot would throw in
            // ROM.u32's bounds check). Valid-ROM-equivalent. The recursive calls (addr+4 with addr+12<=
            // Length from getBlockDataCount) are already in-bounds; this covers the top-level entry slot.
            if (!U.isSafetyOffset(vocaBasePointer + 3, rom))
            {
                return;
            }
            uint vocaBaseAddress = rom.u32(vocaBasePointer);
            if (!U.isPointer(vocaBaseAddress))
            {
                return;
            }
            vocaBaseAddress = U.toOffset(vocaBaseAddress);
            if (!U.isSafetyOffset(vocaBaseAddress, rom))
            {
                return;
            }

            // Already-recorded dedup (the WF `recycle[i].Addr == voca_baseaddress` scan over the global
            // list — prevents the same shared voice table from being relocated twice / infinite recursion).
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Addr == vocaBaseAddress)
                {
                    return;
                }
            }

            const uint block = 12;
            // SongInstrumentForm.Init IsDataExists (verbatim) — block 12, cap i>=128.
            uint dataCount = rom.getBlockDataCount(vocaBaseAddress, block,
                (i, addr) => SongInstrumentExists(rom, i, addr));

            // AddressWinForms.AddAddress(list, IFR, basename, {4,8}): length = block*(count+1).
            list.Add(new Address(vocaBaseAddress, block * (dataCount + 1), vocaBasePointer, basename,
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 4, 8 }));

            uint addr = vocaBaseAddress;
            for (uint i = 0; i < dataCount; i++, addr += block)
            {
                uint type = rom.u8(addr);
                if (type == 0x00 || type == 0x08 || type == 0x10 || type == 0x18)
                {//directsound wave
                    uint songdataAddr = rom.p32(addr + 4);
                    if (!U.isSafetyOffset(songdataAddr, rom))
                    {
                        continue;
                    }
                    uint sampleLength = FEBuilderGBA.Core.SongDirectSoundWavCore.GetDirectSoundWaveDataLength(rom, songdataAddr);
                    if (!U.isSafetyLength(songdataAddr + 12 + 4, sampleLength)
                        || !FEBuilderGBA.Core.SongDirectSoundWavCore.IsDirectSoundData(rom, songdataAddr))
                    {//broken — record a length-0 marker (verbatim)
                        Address.AddPointer(list, addr + 4, 0,
                            basename + U.To0xHexString((int)i) + "DIRECTSOUND(BROKEN)",
                            Address.DataTypeEnum.SONGINSTDIRECTSOUND);
                        continue;
                    }

                    string name = FEBuilderGBA.Core.SongDirectSoundWavCore.IsDirectSoundWaveCompressedDPCM(rom.Data, songdataAddr)
                        ? "DIRECTSOUND(DPCM COMPRESSED)"
                        : "DIRECTSOUND";
                    Address.AddPointer(list, addr + 4, 12 + 4 + sampleLength,
                        basename + U.To0xHexString((int)i) + name,
                        Address.DataTypeEnum.SONGINSTDIRECTSOUND);
                }
                else if (type == 0x03 || type == 0x0B)
                {//wave
                    uint songdataAddr = rom.p32(addr + 4);
                    if (!U.isSafetyOffset(songdataAddr, rom))
                    {
                        continue;
                    }
                    Address.AddPointer(list, addr + 4, 16,
                        basename + U.To0xHexString((int)i) + "WAVE",
                        Address.DataTypeEnum.SONGINSTWAVE);
                }
                else if (type == 0x80)
                {//drum (recurse)
                    uint drumVoices = rom.p32(addr + 4);
                    if (!U.isSafetyOffset(drumVoices, rom))
                    {
                        continue;
                    }
                    EmitRecycleOldInstrument(rom, list, basename + U.To0xHexString((int)i) + "DRUM ", addr + 4);
                }
                else if (type == 0x40)
                {//multisample (recurse + a fixed 128-byte sample table)
                    uint multisampleVoices = rom.p32(addr + 4);
                    uint sampleLocation = rom.p32(addr + 8);
                    if (!U.isSafetyOffset(multisampleVoices, rom))
                    {
                        continue;
                    }
                    if (!U.isSafetyOffset(sampleLocation, rom))
                    {
                        continue;
                    }
                    EmitRecycleOldInstrument(rom, list, basename + U.To0xHexString((int)i) + "MULTI ", addr + 4);
                    Address.AddPointer(list, addr + 8, 128,
                        basename + U.To0xHexString((int)i) + "MULTI2",
                        Address.DataTypeEnum.BIN);
                }
            }
        }

        /// <summary>VERBATIM port of the <c>SongInstrumentForm.Init</c> IsDataExists predicate: an
        /// instrument slot "exists" while <c>i &lt; 128</c> AND the type byte at +0 is a known instrument
        /// type whose data pointer(s) are valid ROM pointers. DirectSound/Wave/Drum (0x00/08/10/18/03/0B/80)
        /// need <c>u32(addr+4)</c> to be a safe pointer; MultiSample (0x40) needs BOTH <c>u32(addr+4)</c>
        /// and <c>u32(addr+8)</c>; the "without data" square/noise types (0x01..0x0C subset) always exist;
        /// any other type terminates. <c>getBlockDataCount</c> guards <c>addr+block(12)&lt;=Length</c>, so
        /// the <c>+0/+4/+8</c> reads are in-bounds.</summary>
        public static bool SongInstrumentExists(ROM rom, int i, uint addr)
        {
            if (i >= 128)
            {
                return false;
            }
            uint type = rom.u8(addr + 0);
            if (type == 0x00 || type == 0x08 || type == 0x10 || type == 0x18 // directsound
                || type == 0x03 || type == 0x0B // wave
                || type == 0x80) // drum
            {
                uint p = rom.u32(addr + 4);
                if (!U.isSafetyPointer(p, rom))
                {
                    return false;
                }
                return true;
            }
            if (type == 0x40) // multisamples
            {
                uint p = rom.u32(addr + 4);
                if (!U.isSafetyPointer(p, rom))
                {
                    return false;
                }
                p = rom.u32(addr + 8);
                if (!U.isSafetyPointer(p, rom))
                {
                    return false;
                }
                return true;
            }
            if (type == 0x01 || type == 0x02 || type == 0x03 // square wave (without data)
                || type == 0x04 // noise (without data)
                || type == 0x09 || type == 0x0A || type == 0x0C) // square wave (without data)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// <c>MapSettingForm.MakeAllDataLength</c> (slice 2r). FE8-only (the WF <c>version==8</c> branch).
        /// Main IFR: base <c>p32(map_setting_pointer)</c>, block <c>map_setting_datasize</c>, IsDataExists
        /// = <see cref="IsMapSettingEnd"/> (the verbatim <c>MapSettingForm.IsMapSettingEnd</c>, which needs
        /// the cached text count <c>TextForm.GetDataCount()</c> — reproduced byte-faithfully by
        /// <see cref="TextDataCount"/>, the same TextForm IFR count walk WF caches via
        /// <c>UpdateDataCountCache</c> right before MapSetting runs), pointerIndexes {0}. Per entry: a
        /// CSTRING block (strlen+1) behind the embedded pointer at +0 (the map-setting name) — emitted by
        /// this dedicated walker's own per-entry loop calling <see cref="Address.AddCString"/> directly
        /// (same emission as <c>SubKind.CString</c>, but EmitMapSetting is not a descriptor/SubWalk form).
        /// NOTE: <c>MapSettingCore.IsMapSettingValid</c>
        /// is NOT used (its <c>textmax==0</c> guard diverges from WF); this reproduces
        /// <c>IsMapSettingEnd</c> directly.
        /// </summary>
        public static void EmitMapSetting(ROM rom, List<Address> list)
        {
            uint block = rom.RomInfo.map_setting_datasize;
            if (block == 0)
            {
                return; // a zero block would make getBlockDataCount spin; not real data.
            }
            uint pointer = U.toOffset(rom.RomInfo.map_setting_pointer);
            // HARDEN: guard the slot's full u32 read (root+3) before p32 (a near-EOF RomInfo pointer would
            // throw in ROM.u32). Valid-ROM-equivalent (map_setting_pointer is a constant well inside ROM).
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return;
            }

            // The text-count cap inside IsMapSettingEnd is constant across the whole walk (WF caches it
            // once); compute it ONCE so the closure does not re-walk the text table per entry.
            uint textmax = TextDataCount(rom);
            uint dataCount = rom.getBlockDataCount(baseAddr, block,
                (i, addr) => IsMapSettingEnd(rom, addr, textmax));

            // AddressWinForms.AddAddress(list, IFR, "MapSetting", {0}): length = block*(count+1).
            list.Add(new Address(baseAddr, block * (dataCount + 1), pointer, "MapSetting",
                Address.DataTypeEnum.InputFormRef, block, new uint[] { 0 }));

            // Per entry: Address.AddCString(list, addr + 0) — the map-setting name C-string (strlen+1).
            // Needs a SystemTextEncoder; skip gracefully (don't NRE) when none is loaded — the main IFR
            // is still emitted and the slot relocated.
            if (CoreState.SystemTextEncoder == null)
            {
                return;
            }
            uint entry = baseAddr;
            for (uint i = 0; i < dataCount; i++, entry += block)
            {
                Address.AddCString(list, entry + 0);
            }
        }

        /// <summary>VERBATIM port of <c>MapSettingForm.IsMapSettingEnd(addr)</c> — the per-entry
        /// "this slot still holds a valid map setting" predicate (WF's name is misleading; it returns TRUE
        /// while data exists). A <c>u32(addr+0)</c> that is a ROM pointer always exists; otherwise the
        /// weather / PLIST / four text-id (map name + clear condition) fields must all be in range, where
        /// the upper bound for the text ids is <paramref name="textmax"/> (= <c>TextForm.GetDataCount()</c>).
        /// All pure-ROM reads; <c>getBlockDataCount</c> guards <c>addr+block(148)&lt;=Length</c>, so the
        /// deepest read (<c>u16(addr+0x8A)</c>) is in-bounds.</summary>
        public static bool IsMapSettingEnd(ROM rom, uint addr, uint textmax)
        {
            uint a = rom.u32(addr + 0);
            if (U.isPointer(a))
            {
                return true;
            }

            // CP-zeroed map guard.
            uint weather = rom.u8(addr + 12);
            if (weather >= 0xE)
            {
                return false;
            }
            uint plistDirect = rom.u32(addr + 4);
            if (plistDirect == 0 || plistDirect == 0xFFFFFFFF)
            {
                plistDirect = rom.u32(addr + 8);
                if (plistDirect == 0 || plistDirect == 0xFFFFFFFF)
                {
                    return false;
                }
            }
            uint map1 = rom.u16(addr + 0x70);
            if (map1 >= textmax)
            {
                return false;
            }
            uint map2 = rom.u16(addr + 0x72);
            if (map2 >= textmax)
            {
                return false;
            }
            uint clearcond1 = rom.u16(addr + 0x88);
            if (clearcond1 >= textmax)
            {
                return false;
            }
            uint clearcond2 = rom.u16(addr + 0x8A);
            if (clearcond2 >= textmax)
            {
                return false;
            }
            return true;
        }

        /// <summary>Byte-faithful reproduction of <c>TextForm.GetDataCount()</c> (the cached text-table
        /// entry count). WF caches it via <c>TextForm.UpdateDataCountCache</c> inside
        /// <c>TextForm.MakeAllDataLength</c>, which runs (line 2428) BEFORE <c>MapSettingForm</c> (line
        /// 2524) — so the cached value equals the TextForm IFR's DataCount computed via the SAME walk
        /// <see cref="EmitTextAt"/> uses. Returns 0 when the text pointer is unrecoverable (no entries).</summary>
        public static uint TextDataCount(ROM rom)
        {
            const uint block = 4;
            uint pointer = U.toOffset(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(pointer + 3, rom))
            {
                return 0;
            }
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                // WF TextForm.Init: a broken text pointer ReInits to text_recover_address.
                baseAddr = U.toOffset(rom.RomInfo.text_recover_address);
                if (!U.isSafetyOffset(baseAddr, rom))
                {
                    return 0;
                }
            }
            return rom.getBlockDataCount(baseAddr, block,
                (i, addr) => IsTextEntryExists(rom, rom.u32(addr)));
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
                // (EventCondForm: EventScriptForm.ScanScript event-scan, not in Core.
                //  EventScript(MakeEventASMMAPList), EventFunctionPointerForm, Command85PointerForm are
                //  OUT OF SCOPE: emitted by U.AppendAllASMStructPointersList — the ASM/LDR-map path, NOT
                //  this producer's U.MakeAllStructPointersList data path.)
                "EventCondForm", "EventScript(MakeEventASMMAPList)", "EventFunctionPointerForm",
                "Command85PointerForm",
                // text (Huffman)
                // (MapTerrainNameForm ported in slice 2c — per-entry string-BIN sub-walk; TextDicForm
                //  ported in this sweep — 3 clean tables (dic_main/chaptor/title).
                //  TextForm + TextCharCodeForm ported in slice 2m:
                //    TextCharCodeForm — a flat U8NotEqual descriptor (base mask_pointer, block 4, stop
                //      255, pointerIndexes {}).
                //    TextForm — a dedicated emitter (EmitText): main IFR (base p32(text_pointer) with the
                //      text_recover_address ReInit fallback, block 4, multi-branch IsDataExists =
                //      isPointer || IsUnHuffmanPatchPointer || Is_RAMPointerArea via IsTextEntryExists,
                //      TEXTPOINTERS, {0}) + a per-entry BIN sub-walk whose length is the Huffman/UnHuffman
                //      decode (FETextDecode.huffman_decode / UnHffmanPatchDecode — both headless Core
                //      methods, batch 10, called directly and internally EOF-hardened). The decoders need
                //      a SystemTextEncoder to size (they decode the string as a side effect); without one
                //      the producer falls back to the WF isPointerOnly side (size 0) — the slot is still
                //      relocated and the target addr recorded, and IsComplete already gates a partial scan.
                //  OtherTextForm ported in slice 2q (EmitOtherText): its entry list comes from a config
                //  file (other_text_, one hex pointer per line) via TextSourceListCore.MakeOtherTextList
                //  (= WF MakeOtherTextMap), and per entry it emits a string-BIN block (length == decoded
                //  strlen, NO +1) behind the embedded pointer at +0. U.ConfigDataFilename / the line
                //  reader are in Core (resolving against CoreState.BaseDirectory); a missing config tree
                //  -> empty list -> nothing emitted (faithful headless behavior).)
                // images (LZ77/TSA length calc)
                // (slice 2e ported the FLAT LZ77-image + palette forms whose every length is
                //  LZ77.getCompressedSize (EOF-safe) or a CONSTANT palette/image size:
                //    ImageBattleBGForm, ImageBattleTerrainForm, ImageUnitWaitIconFrom,
                //    ImageUnitPaletteForm, ImageGenericEnemyPortraitForm, ImageChapterTitleForm [FE8]
                //    + ImageChapterTitleFE7Form [FE7-multibyte/FE6] (StructDescriptor + Lz77Pointer/
                //    FixedPointer SubWalks); ImageBattleScreenForm, WorldMapImageFE6Form [FE6],
                //    WorldMapImageFE7Form [FE7] (dedicated flat emitters).
                //  slice 2k ported the header-TSA image forms (now CalcHeaderTsaLength =
                //  ImageUtil.CalcByteLengthForHeaderTSAData + EmitHeaderTsaPointer = Address.AddHeaderTSAPointer
                //  are in Core; CalcRomTcsLength = ImageUtilAP.CalcROMTCSLength too):
                //    ImageBGForm [version-agnostic, BG256-aware per-entry HEADER-TSA branch],
                //    ImageSystemIconForm [version-agnostic, flat version-gated LZ77/PAL/HEADER-TSA],
                //    ImageCGForm [FE8 + FE7-multibyte, 12-byte IFR + per-entry 10-image-pointer array +
                //      HEADER-TSA], ImageCGFE7UForm [FE7U, 16-byte IFR + per-entry flag@+0 16-color-vs-
                //      10-split + HEADER-TSA], WorldMapImageForm [FE8, big-map BIN/PAL + event/mini/icon
                //      LZ77 + WorldmapCountyBorder IFR with per-entry ROMTCS + WorldMapIconData IFR].
                //  The rest STAY, each blocked on a subsystem not yet in Core (a wrong length relocates
                //  the wrong bytes = silent corruption):
                //  (slice 2q ported the config-FILE-table TSA-anime forms — once U.ConfigDataFilename /
                //   U.LoadTSVResource reached Core (resolving config/data/ vs CoreState.BaseDirectory) the
                //   config-LOAD stopped being a blocker; every per-entry LENGTH is Core-backed (LZ77
                //   getCompressedSize / const PAL / CalcHeaderTsaLength). EmitImageTSAAnime [v8 + v7] —
                //   per tsaanime_ entry: ReInit(addr, count=atoh(v[0])) main IFR block 12 PI {0,4,8} +
                //   per i: LZ77IMG@+0 / PAL 0x20*8@+4 / LZ77TSA@+8. EmitImageTSAAnime2 [v8 ONLY] —
                //   per tsaanime2_ entry: main block 12 ReInit(addr+20) isPointer(u32(a+8)) PI {8},
                //   N1 block 20 ReInitPointer(ptr,1) PI {4,16}, HEADER LZ77IMG@addr+16 + PAL 0x20@addr+4,
                //   then per record HeaderTsa@+8.)
                //  (slice 2p ported the RecycleOldAnime forms whose length walk is PURE ROM (no Drawing, no
                //   config) and whose gate/count deps are already in Core:
                //    ImageMapActionAnimationForm — EmitImageMapActionAnimation: FindMapActionAnimationPointer
                //      (version-gated 20-byte grep) + a main IFR (base p32(AnimeP), block 8, IsDataExists
                //      isSafetyPointerOrNull(u32), PI {0}) + per-entry (i=1..) EmitMapActionRecycleOldAnime
                //      (a 12-byte-record terminator walk; LZ77 OBJ @n+4 + PAL @n+8 per record, then a main
                //      record IFR ReInit(base,(n-base)/12) PI {4,8}). NO calcOAMLength, NO decompress.
                //    ImageMagicFEditorForm / ImageMagicCSACreatorForm — EmitImageMagicFEditor /
                //      EmitImageMagicCsaCreator (shared EmitImageMagicCommon): the ImageUtilMagicCore gate
                //      (SearchMagicSystem FEDITOR_ADV/CSA_CREATOR) + FindCSASpellTable + GetSpellDataCount,
                //      a main IFR (base magic_effect_pointer, block 4, count GetSpellDataCount, PI {0}), the
                //      Magic_Append_SpellTable block, and per spell (p32==dim/no_dim) EmitMagicRecycleOldAnime
                //      (the verbatim WF RecycleOldAnime: FEditorAdv 28-byte / CSA 32-byte+TSA records; the OAM
                //      column length is the verbatim CalcMagicOamLength port of WF calcOAMLength).
                //  ImageBattleAnimeForm PORTED in slice 2s (EmitImageBattleAnime) — all its deps reached Core:
                //  the per-class "BattleAnimeSeting" IFRs (ClassForm.MakeClassList = MakeClassListAddrs +
                //  GetBattleAnimeAddrWhereAddr version-gated +48/+52), the N_ "BattleAnime" animelist IFR
                //  (FEditorHint via CoreState.Config), and the per-anime OAM walk (EmitImageBattleAnimeOAM =
                //  ImageUtilOAM.MakeAllDataLength — section BIN + 4 LZ77 pointers + the UnCompressFrame-embedded
                //  seat-image sub-walk, dedup'd across entries via a shared seatNumberList; UnCompressFrame =
                //  FETextEncode/LZ77/getBinaryData + CalcUnCompressFrameLength, all Core). Only the per-anime
                //  OAM `info` name is static (WF's getString-decoded anime name is cosmetic / relocation-
                //  identical — the ItemWeaponEffect precedent). See GetNotYetPortedForms_DropsSlice2sForms.)
                //  (slice 2n ported ImageUnitMoveIconFrom — its per-entry AP column uses SubKind.ApPointer /
                //   EmitApPointer, length = ImageUtilAPCore.CalcAPLength [the verbatim Core port of WF
                //   ImageUtilAP.CalcAPLength = Parse + GetLength, already a tested Core helper]. The count
                //   rule is MoveIconRule [class-count-bounded]. So it is no longer deferred.)
                //  (slice 2q ported ImageRomAnimeForm [all versions] — EmitImageRomAnime: per romanime_
                //   config entry, CheckRomAnimePointers gate (pure-ROM), then FRAME ptr + BIN
                //   (frameCount*4, frameCount = GetRomAnimeFrameCountLow u16==0xFFFF terminator walk),
                //   TSA/Image ptr + per GetRomAnimePointerListCount entry LZ77 (getCompressedSize,
                //   pointer-verified), Palette ptr + per GetRomAnimePalettePointerListCount entry PAL
                //   (2*16, with the COMMONPALETTE / framePointer<0x100 / else fallbacks). All pure-ROM
                //   walks, no Drawing/disasm. ImageTSAAnimeForm ported alongside it — see above.)
                //    (ImagePortraitForm + ImagePortraitFE6Form + ImageItemIconForm PORTED in slice 2t:
                //      ImageItemIconForm — a flat 128-byte icon-SHEET StructDescriptor (base icon_pointer,
                //        block (2*8*2*8)/2, rule ItemIconMaxRule = `i <= GetIconMax()`, pointerIndexes {});
                //        GetIconMax is the verbatim pure-ROM count (repoint -> 0xFE; FE7U FEditorAdv
                //        AutoPatch probe at the hardcoded 0xCB51A -> max-1; else icon_orignal_max).
                //      ImagePortraitForm [FE8 + FE7] — EmitImagePortrait (shared EmitPortraitTable): the main
                //        "Portrait" IFR {0,4,8,12,16} (the stateful nullContinuousCount>=1000 + GetFEditor
                //        LengthHint IsDataExists, pure-ROM) + per-entry RecyclePortrait (FACE LZ77/IMG/
                //        HALFBODY by header byte — IsHalfBodyFlag = u32(seet)==0x00200400, version==8-gated;
                //        MAP FACE / PAL@+8-or-+0-halfbody / MOUTH / CLASS CARD). The per-entry info name is
                //        STATIC ("Portrait:0x..") — WF appends a getString-decoded name (cosmetic /
                //        relocation-identical; the ItemWeaponEffect precedent).
                //      ImagePortraitFE6Form [FE6] — EmitImagePortraitFE6 (same driver, nullContinuousCount>=10,
                //        pointerIndexes {0,4,8}, simpler RecyclePortraitFE6: FACE LZ77 / MAP FACE IMG@+4 /
                //        PAL@+8; no halfbody). See GetNotYetPortedForms_DropsSlice2tForms.)
                //    MapMiniMapTerrainImageForm — InputFormRef_ASM + AddFunctions, called from the
                //      AppendAllASMStructPointersList ASM path, not this producer's data path.)
                "MapMiniMapTerrainImageForm",
                // songs / sound (recycle, embedded inst)
                // (SoundRoomCGForm [FE7, clean u32-FFFFFFFF table], SoundRoomFE6Form [FE6, clean],
                //  and WorldMapBGMForm [FE8, clean] ported in earlier sweeps. SoundFootStepsForm ported in
                //  slice 2d — Switch2-gated dedicated walker (base/count from sound_foot_steps_switch2_address,
                //  IsSwitch2Enable gate) + per-entry ASM AddFunction.
                //  SoundRoomForm ported in slice 2r (EmitSoundRoom) — FE8 + FE7 (NOT FE6): main IFR base
                //  p32(sound_room_pointer), block sound_room_datasize, IsDataExists u32==0xFFFFFFFF /
                //  i>10 && IsEmpty(addr, datasize*10) (= TerminatorWithEmptyGuard width 4), PI {8,12},
                //  InputFormRef_MIX. FE7-only per-entry: a string-BIN (length == strlen, NO +1) behind the
                //  +12 embedded pointer (SubKind.BinString — getString-backed, encoder-skipped gracefully).
                //  SongTableForm ported in slice 2r (EmitSongTable) — base GetSoundTablePointer (RomInfo slot
                //  OR a verbatim signature scan returning the SLOT), main IFR block 8 / isPointer(u32(addr))
                //  / {0}; per entry EmitRecycleOldSong (= SongUtil.RecycleOldSong: SONGTRACK header [8+tc*4]
                //  + per-track SONGSCORE [Padding4(fine-start+1)] from SongMidiCore.ParseTracks) AND
                //  EmitRecycleOldInstrument (= SongInstrumentForm.RecycleOldInstrument: a recursive block-12
                //  IFR with shared visited-list dedup + per-type DirectSound [SongDirectSoundWavCore length] /
                //  Wave [16] / Drum / Multi [128] blocks). ALL pure-ROM walks — the Song*Core files DO hold
                //  the parse + DirectSound-length ports the deferral once thought were MIDI-only.)
                // embedded sub-pointer / event-scan / CString expansion
                // (ClassForm + StatusParamForm ported in slice 2c — per-entry MoveCost / CString
                //  sub-walks. ItemForm STAYS: its StatBooster sub-block SIZE depends on un-ported
                //  PatchUtil patch detection (SearchGrowsMod / ItemUsingExtendsPatch -> 12/16/20)
                //  and the ItemEffectiveness rework variant on SearchClassType — a wrong size would
                //  relocate the wrong bytes, so it must wait until those detectors reach Core.)
                "ItemForm",
                // (ItemWeaponEffectForm ported in slice 2l — EmitItemWeaponEffect: a flat IFR (base
                //  p32(item_effect_pointer), block 16, IsDataExists u16==0xFFFF / i>10 && IsEmpty(addr,160)
                //  — both pure ROM reads) + a per-entry PROCS sub-block behind the embedded pointer at +8,
                //  length = the PROCS-bytecode terminator walk ProcsScriptForm.CalcLengthAndCheck,
                //  reproduced verbatim as CalcProcsLengthAndCheck — a pure u16/u32/getString walk whose
                //  only non-ROM dep (getString + isAsciiString for opcode 0x01) is in Core.)
                // (StatusRMenuForm [recursive 28-byte MIX tree, shared visited-set + 2 ASM AddFunction]
                //  and MenuDefinitionForm [recursive MenuCommandForm sub-table — InputFormRef_MIX + per-entry
                //  CString + 6 ASM ptrs — + its own 6 ASM ptrs] ported in slice 2d via dedicated recursive
                //  walkers; MenuCommandForm.MakeAllDataLengthP is reproduced by EmitMenuCommandSubTable.)
                // (ItemShopForm ported in slice 2g — EmitItemShop walks ItemShopCore.MakeShopList [hensei +
                //  FE8 worldmap + per-map event-cond OBJECT-slot shops] and emits one BIN Address per shop,
                //  length = (count of non-zero 2-byte item entries + 1) * 2.)
                // UnitActionPointerForm STAYS — its base = SearchActionPointer(), gated on
                //   PatchUtil.SearchUnitActionReworkPatch() (PatchUtil patch detection not in Core).
                // (ItemUsagePointerForm ported in slice 2f — dedicated EmitItemUsagePointer walker over
                //  the 10 Switch2-gated usage tables, base/count from each xxx_array_switch2_address via
                //  the Core ItemUsagePointerCore.IsSwitch2Enable + per-entry ASM AddFunction @0.)
                "UnitActionPointerForm",
                // (MapChangeForm + MapExitPointForm ported in slice 2g via dedicated per-map walkers:
                //  EmitMapChange [per map: MapChangeCore.GetMapChangeAddrWhereMapID -> main 12-byte IFR
                //  {8} + per-entry w*h*2 BIN behind +8] and EmitMapExitPoint [enemy + NPC 4-byte slot
                //  tables, each with a per-map N-table; NPC main via ButIgnorePointer].
                //  MapPointerForm ported in slice 2j (EmitMapPointer): the 6-7 MAPPOINTERS PLIST-table
                //  IFRs (block 4, rule i==0||i<limit, limit = IsPlistSplits()?256:map_map_pointer_list_
                //  default_size, PI {0}) + a per-map column sweep. Its only blocker was the palette2_plist
                //  read, gated on PatchUtil.SearchFlag0x28ToMapSecondPalettePatch — that gate now lives in
                //  Core (PatchDetection.SearchFlag0x28ToMapSecondPalettePatch) inside
                //  MapPListResolverCore.GetMapPListsWhereAddr, and MapSettingCore.MakeMapIDList +
                //  RomInfo-only GetBasePointer/IsPlistSplits complete the dependency set.)
                "FontForm",
                // AI scripts (disasm)
                // (AIMapSettingForm [flat u8!=0xFF table], AIPerformStaffForm + AIPerformItemForm
                //  [flat u16!=0 table + per-entry ASM AddFunction @4 via SubKind.AsmFunction] ported in
                //  slice 2f. AIScriptForm PORTED in slice 2s (EmitAIScript) — per AI table (ai1_/ai2_):
                //  the main IFR (block 4, IsDataExists = isPointerOrNULL(u32) with the un-extended cap =
                //  CountConfigDataLines("ai{1,2}_") [isExtrendsROMArea = addr >= toOffset(extends_address)],
                //  PI {0}) + the two ClonePointer slots (AISomeByte, length-0 POINTER) + per entry an
                //  AISCRIPT block (length = CalcAIScriptLength = the verbatim AIScriptForm.CalcLength
                //  16-byte-opcode terminator walk) and per 16-byte slot the embedded +8/+12 pointers
                //  (odd +8 -> AddFunction CallASM; else an AIUNITS BIN block of length CalcAIUnitsLength =
                //  the verbatim AIUnitsForm.CalcLength u16==0 walk). Only the per-entry NAME is STATIC
                //  ("AI<n> 0x<i>") — WF appends GetAIName1/2 (a config name list OR InputFormRef.GetCommentSA,
                //  neither in Core), which is COSMETIC / relocation-identical (the ItemWeaponEffect
                //  static-name precedent). See GetNotYetPortedForms_DropsSlice2sForms.)
                // skills (version/patch dependent)
                // (slice 2o ported the RecycleOldAnime-FREE subset, all dependencies already in Core:
                //  SkillAssignmentClassSkillSystemForm + SkillAssignmentUnitSkillSystemForm [FE8U,
                //    is_multibyte==false] — EmitSkillAssignmentClass / EmitSkillAssignmentUnit: the
                //    SearchSkillSystem gate + Find{Assign*,Skill}PointerLocation scanners (Core
                //    SkillSystemTextScanner / SkillSystemPatchScanner, verbatim WF ports), a main block-1
                //    IFR (count = ClassDataCount / UnitDataCount, the Core ClassForm/UnitForm.DataCount
                //    ports), an AddAddressInstantIFR level-up pointer list, and a per-entry block-2 nested
                //    N1 IFR via the slice-2i EmitNestedIfrSub primitive.
                //  SkillConfigFE8NSkillForm [FE8J, is_multibyte==true] — EmitSkillConfigFE8N: the FE8N
                //    (Ver1)/yugudora/FE8N_ver2 icon tables (Ver3 contributes nothing), pointers from
                //    SkillSystemTextScanner.FindSkillFE8NVer1/Ver2IconPointers, each a per-pointer block-32
                //    IFR with the WF DataCount<=0 skip.
                //  The RecycleOldAnime-DEPENDENT siblings STAY: each calls
                //  ImageUtilSkillSystemsAnimeCreator.RecycleOldAnime (an anime length walker not yet in
                //  Core — same blocker class as the Group-3 ImageUtilOAM forms):
                //    SkillConfigSkillSystemForm [FE8U] — Init(null, basetextP) IFR + a per-anime loop that
                //      calls RecycleOldAnime on each p32(g_AnimeBaseAddress + 4*i).
                //    SkillConfigFE8NVer2SkillForm / SkillConfigFE8NVer3SkillForm [FE8J] — RecycleOldAnime
                //      AND GUI session state (g_SkillBaseAddress / g_AnimeBaseAddress / g_ICON_LIST_SIZE set
                //      by the Find* scans) + N1..N5 icon sub-tables. Deferred until RecycleOldAnime is in
                //      Core.)
                "SkillConfigSkillSystemForm",
                "SkillConfigFE8NVer2SkillForm", "SkillConfigFE8NVer3SkillForm",
                // status / menu definition / misc tables needing extra logic
                // (StatusUnitsMenuForm + LinkArenaDenyUnitForm ported in slice 2b.
                //  StatusOptionOrderForm [v7+v8, count-address fixed table] ported in this sweep.
                //  StatusOptionForm ported in slice 2d [v7+v8, PointerAt@40 main IFR + per-entry
                //  SubKind.AsmFunction @40]. NOTE: "MapMiniMapTerrainForm" does NOT exist as a distinct
                //  ASM form — the only file is MapMiniMapTerrainImageForm (an LZ77 image form, already
                //  listed above), so there is nothing extra to port for it.
                //  MantAnimationForm ported in slice 2f — PointerAt@0 main IFR ("Mant") + per-entry
                //  SubKind.FixedPointer @0 (0x10-byte POINTER block "MANT_P:0x<i>").
                //  (MapTileAnimation1Form + MapTileAnimation2Form ported in slice 2g — EmitMapTileAnimation1
                //   / EmitMapTileAnimation2 build the dedup'd per-map PLIST list [anime1_plist@+9 /
                //   anime2_plist@+10, resolved via MapChangeCore.PlistToOffsetAddr ANIMATION/ANIMATION2 with
                //   the version PLIST-limit gate], emit the main 8-byte IFR [isPointer(u32+4)/{4} for anime1,
                //   isPointer(u32+0)/{0} for anime2], then per-entry IMG [u16(p+2) @ p32(p+4)] / BIN
                //   [u8(p+5)*2 @ p32(p+0)] columns.)
                //  MapTerrainFloorLookupTableForm/MapTerrainBGLookupTableForm ported in slice 2j
                //  (EmitMapTerrainLookup): one flat block-1 IFR (rule i<map_terrain_type_count) per
                //  non-zero pointer, name + ToHexString(i). Their pointer set comes from
                //  GetPointersExtendsPatch (PatchUtil.SearchExtendsBattleBG + hardcoded FE8 offsets) —
                //  now in Core as MapTerrainLookupCore.GetPointers + PatchDetection.SearchExtendsBattleBG
                //  (added for the Avalonia #441/#442 gap-sweep), so the pointer set IS reproducible.)
                // units / classes per-version with extra reads
                // (UnitForm [FE8, flat + 24-byte support BinFixed sub-walk @44] and UnitFE7Form
                //  [FE7, flat, no sub-walk] ported in this sweep; SummonUnitForm + SummonsDemonKingForm
                //  + EventForceSortieForm [FE8 clean tables] ported too. UnitFE6Form ported in slice 2f
                //  via the dedicated EmitUnitFE6 walker — base p32(unit_pointer)+unit_datasize, BasePointer
                //  0 -> NOT_FOUND, i < unit_maxcount, pointerIndexes {44}.)
                // (ExtraUnitForm [FE8J] + ExtraUnitFE8UForm [FE8U] ported in slice 2j (EmitExtraUnit /
                //  EmitExtraUnitFE8U — gated EXACTLY as WF: version==8 && is_multibyte = FE8J at hardcoded
                //  0x37EE4 + flag BINs @ i*0x14+0x37E10; version==8 && !is_multibyte = FE8U table @ 0x37D88
                //  block 8). Both expand each entry via EventUnitForm.RecycleOldUnits, reproduced verbatim
                //  by EmitRecycleOldUnits (an EventUnit IFR + the v8 per-entry COORD BIN sub-blocks — pure
                //  ROM reads, no Form/disasm). EventUnitForm.RecycleReserveUnits STAYS — it iterates the
                //  static NewAllocData list, which is EDITOR SESSION STATE (newly-allocated unit regions
                //  recorded during live editing), NOT a ROM-derived table; it is always empty headless, so
                //  the producer emits nothing for it — kept listed so the coverage gate stays honest.)
                "UnitCustomBattleAnimeForm",
                "EventUnitForm(RecycleReserveUnits)",
                // monster / world map / ED / support (FE8/FE7/FE6 variants)
                // (MonsterItemForm + MonsterProbabilityForm ported in slice 2b.
                //  This sweep ported the CLEAN per-version tables: EDForm [FE8 ×4], EDFE7Form [FE7 ×5],
                //  EDFE6Form [FE6], EDSensekiCommentForm [v6+v7], CCBranchForm + OPClassAlphaNameForm
                //  [FE8, count = ClassDataCount] + OPClassAlphaNameFE6Form [FE6 string-BIN sub-walk],
                //  WorldMapPointForm [FE8], SupportTalkForm/FE6/FE7, EventBattleTalkFE6Form [FE6 ×2],
                //  EventHaikuFE6Form [FE6], TacticianAffinityFE7, EventFinalSerifFE7Form.
                //  slice 2h ported the OP/ED LZ77 stragglers + the SupportUnit / WorldMapPath flat-IFR
                //  forms whose only blocker was a count rule needing a Core helper:
                //    EDStaffRollForm [FE8] / OPPrologueForm [FE8] / OPClassFontForm [FE8-multibyte] —
                //      StructDescriptor PointerAt@0 (EDStaffRoll caps at MaxCount 12) + Lz77Pointer
                //      SubWalks (IMG/TSA) (+ OPPrologue's standalone PAL via ExtraFixedPointer).
                //    SupportUnitForm [FE7/8] + SupportUnitFE6Form [FE6] — EmitSupportUnit, a flat IFR
                //      whose owner-lookahead count rule (UnitForm.GetUnitIDWhereSupportAddr) is
                //      reproduced via SupportUnitNavigation.GetUnitIdAtSupportAddr (pure unit-table walk).
                //    WorldMapPathForm [FE8] — EmitWorldMapPath, main IFR PointerAt@0/{0,8} + per-entry
                //      BIN/POINTER sub-blocks whose lengths are the pure CalcPath{,Move}DataLength
                //      terminator walks (reproduced verbatim, EOF-hardened on the u32 read).
                //  slice 2i ported the two OPClassDemo forms via the new SubKind.NestedIfr mechanism
                //  (EmitNestedIfrSub: an embedded pointer whose target is ITSELF a getBlockDataCount-
                //  walked variable-count IFR sub-table, length = subBlock*(subCount+1), pointer = field,
                //  pointerIndexes {}):
                //    OPClassDemoForm [FE8-multibyte] — EmitOPClassDemo: main block 28, rule u8(+0xF)<=4,
                //      PI {0,8,24}; per entry CString@+0, dual-guard (jpName@+8 AND anime@+24), then N1
                //      NestedIfr@+8 (block 1, i>=16?false:u8!=0xFF) + N2 NestedIfr@+24 (block 2, u8!=0).
                //    OPClassDemoFE7Form [FE7-multibyte] — EmitOPClassDemoFE7: main block 32, rule i<=0x41,
                //      PI {0,8,28}; per entry CString@+0, Lz77@+8 (LZ77IMG), anime-guard@+28, then N2
                //      NestedIfr@+28 (block 2, u8!=0); + a trailing absolute AddPointer(0x0B0038, 0x20,
                //      PAL) common-palette pointer. (FE8U/FE7U non-multibyte variants STAY deferred.)
                //  STILL deferred (event-scan / recursive / dynamic base / LZ77 CG):
                //  MonsterWMapProbabilityForm STAYS — although its 5 IFR probability/stage tables
                //    are flat ROM reads, its MakeAllDataLength ALSO emits EventScriptForm.ScanScript over
                //    the two skirmish start/end event pointers (no Core ScanScript primitive yet);
                //    porting only the flat tables would drop those skirmish-event regions = corruption.
                //  WorldMapEventPointerForm [ScanScript],
                //  EventBattleTalkForm + EventHaikuForm [FE8, ScanScript per-entry],
                //  MapSettingForm [FE8] ported in slice 2r (EmitMapSetting) — its count rule
                //    (IsMapSettingEnd, reproduced VERBATIM) needs the WF cached text count
                //    TextForm.GetDataCount(); that is now byte-faithfully reproduced by TextDataCount (the
                //    SAME TextForm IFR count walk EmitText uses, which WF caches via UpdateDataCountCache at
                //    line 2428, BEFORE MapSetting at line 2524). It does NOT use MapSettingCore.IsMapSetting-
                //    Valid (whose textmax==0 guard diverges from WF). Main IFR block map_setting_datasize,
                //    PI {0}; per entry a CSTRING (strlen+1) behind the +0 embedded pointer (SubKind.CString).
                //  (FE8SpellMenuExtendsForm ported in slice 2m — EmitFE8SpellMenuExtends, FE8U-only: the
                //   base is resolved by the patch-signature scan FE8SpellMenuPatchScanner.FindFE8Spell-
                //   PatchPointer (the Core port of WF FindFE8SpellPatchPointer — OldSystem .dmp grep +
                //   hard-coded SkillSystems202201 signature, version/multibyte-gated, NOT_FOUND on a
                //   non-patched ROM), main IFR (block 4, rule i<0xFF, PI {}), per entry a NestedIfr @
                //   assignLevelUpAddr (block 2, u16 != 0xFFFF && != 0) via the slice-2i primitive.)
                //  (ImageCGFE7UForm ported in slice 2k — EmitImageCGFE7U, the FE7U 16-byte big-CG IFR
                //   with the per-entry flag@+0 16-color-vs-10-split + HEADER-TSA.))
                "MonsterWMapProbabilityForm",
                "EventBattleTalkForm",
                "WorldMapEventPointerForm",
                "EventHaikuForm",
                // patch / procs / ASM — OUT OF SCOPE for this data-path producer: these are emitted by
                // U.AppendAllASMStructPointersList (the ASM/LDR-map path), NOT U.MakeAllStructPointersList.
                // (EventFunctionPointerForm / Command85PointerForm above are likewise ASM-path forms.)
                "PatchForm(MakePatchStructDataList)", "ProcsScriptForm",
            };
    }
}
