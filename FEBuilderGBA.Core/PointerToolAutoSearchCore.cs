// SPDX-License-Identifier: GPL-3.0-or-later
// PointerToolAutoSearchCore (#1113) — cross-platform port of the WinForms
// PointerToolForm.AutoSearch cross-ROM auto-tracking, for the Avalonia Pointer
// Tool. Three heuristics, in WF order:
//   (a) ASM-map symbol NAME search    — SearchAsmMapName
//   (b) source<->target LDR-literal-pool-map symmetry — FindOtherROMDataWithLDR
//   (c) auto-tracking retry (widen match size / slide) — AutoSearch loop
//
// STRICTLY READ-ONLY: never mutates a ROM, takes ALL data as parameters (NO
// CoreState.ROM / Program.ROM reads), and NEVER throws — every buffer access is
// guarded or wrapped, returning AutoSearchResult.NotFound on any fault.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Outcome of a cross-ROM Pointer Tool AutoSearch (#1113). All address
    /// fields are GBA pointers (0x08xxxxxx) or <see cref="U.NOT_FOUND"/>.
    /// </summary>
    public sealed class AutoSearchResult
    {
        /// <summary>True when at least one heuristic produced an accepted match.</summary>
        public bool Found;

        /// <summary>Direct-match target data address (WF <c>OtherROMAddress2</c>),
        /// or the NAME-resolved address. <see cref="U.NOT_FOUND"/> when no direct
        /// / name match.</summary>
        public uint DirectAddr = U.NOT_FOUND;

        /// <summary>Reference offset to <see cref="DirectAddr"/> in the target ROM
        /// (WF <c>OtherROMRefPointer2</c>). May be <see cref="U.NOT_FOUND"/> even on
        /// a found name match (WF parity).</summary>
        public uint DirectRef = U.NOT_FOUND;

        /// <summary>LDR-tracked target data address (WF
        /// <c>OtherROMAddressWithLDR</c>), or <see cref="U.NOT_FOUND"/>.</summary>
        public uint LdrAddr = U.NOT_FOUND;

        /// <summary>LDR-literal-pool slot offset that holds <see cref="LdrAddr"/>
        /// in the target ROM (WF <c>OtherROMAddressWithLDRRef</c>).</summary>
        public uint LdrRef = U.NOT_FOUND;

        /// <summary>Which heuristic produced the match: "name" / "ldr" / "direct"
        /// / "none".</summary>
        public string Hit = "none";

        /// <summary>Symbol name for a "name" hit (else "").</summary>
        public string SymbolName = "";

        /// <summary>The sentinel no-match result.</summary>
        public static AutoSearchResult NotFound => new AutoSearchResult();
    }

    /// <summary>
    /// Cross-ROM Pointer Tool AutoSearch heuristics (#1113), ported from WF
    /// <c>PointerToolForm</c>. See file header for the contract.
    /// </summary>
    public static class PointerToolAutoSearchCore
    {
        // WF auto-tracking widening tables.
        //
        // SlideTable is VERBATIM WF (PointerToolForm SlideComboBox values):
        //   { 0, 2, 4, 6, 8, 0xC, 0x10, 0x14 }.
        //
        // SizeTable is APPROXIMATE: the WF designer populates the
        // TestMatchDataSizeComboBox text values and AutoSearch widens by setting
        // SelectedIndex = deepSearch (0,1,2,...). The exact designer strings could
        // not be confirmed from source, so this uses a monotonically-decreasing
        // set matching the documented WF shape (index 0 = 0x20 = "default 32 bytes"
        // per the WF AutoSearch comment, then narrower windows). The ALGORITHM
        // SHAPE — widen the search by shrinking the match window across deepSearch
        // iterations — is what matters; the precise byte counts are a tuning knob.
        static readonly int[] SizeTable = { 0x20, 0x18, 0x10, 0x0C, 0x08 };
        static readonly int[] SlideTable = { 0, 2, 4, 6, 8, 0xC, 0x10, 0x14 };

        const int SEARCH_PUSH_MAX = 0x10 * 2;

        // ----- (a) ASM-map symbol NAME search --------------------------------

        /// <summary>
        /// WF <c>PointerToolForm.SearchASMMap</c> port: resolve the SOURCE symbol
        /// name at <paramref name="sourcePointer"/> via
        /// <paramref name="sourceAsmMap"/>, look it up in
        /// <paramref name="targetAsmMap"/>, and locate a byte-swapped reference to
        /// the found target pointer inside <paramref name="targetData"/>.
        ///
        /// <para>Returns Found=true (Hit="name") when the name resolves in BOTH
        /// maps — even when no reference exists in <paramref name="targetData"/>
        /// (DirectRef stays <see cref="U.NOT_FOUND"/>), matching WF which sets
        /// OtherROMAddress2 regardless. Returns NotFound when either map is null,
        /// the source name is empty, or the target name is unknown.</para>
        /// </summary>
        public static AutoSearchResult SearchAsmMapName(uint sourcePointer, IAsmMapFile sourceAsmMap, IAsmMapFile targetAsmMap, byte[] targetData)
        {
            try
            {
                if (sourceAsmMap == null || targetAsmMap == null || targetData == null)
                    return AutoSearchResult.NotFound;

                string name = sourceAsmMap.GetName(sourcePointer);
                if (string.IsNullOrEmpty(name)) return AutoSearchResult.NotFound;

                uint foundAddr = targetAsmMap.SearchName(name);
                if (foundAddr == U.NOT_FOUND) return AutoSearchResult.NotFound;

                uint refaddress = GrepBigEndianPointerRef(targetData, foundAddr);

                return new AutoSearchResult
                {
                    Found = true,
                    Hit = "name",
                    SymbolName = name,
                    DirectAddr = foundAddr,
                    DirectRef = refaddress,
                    LdrAddr = U.NOT_FOUND,
                    LdrRef = U.NOT_FOUND,
                };
            }
            catch
            {
                return AutoSearchResult.NotFound;
            }
        }

        // ----- (c) direct grep -----------------------------------------------

        /// <summary>
        /// WF <c>PointerToolForm.FindOtherROMData</c> + <c>DGrep</c> port: read
        /// <paramref name="testMatchSize"/> bytes from <paramref name="sourceData"/>
        /// at the offset of <paramref name="sourcePointer"/> (+ <paramref name="slide"/>),
        /// grep them into <paramref name="targetData"/> (exact or pattern-masked),
        /// subtract the slide back, and locate a byte-swapped reference. Sets
        /// <paramref name="outAddr"/> (GBA pointer) + <paramref name="outRef"/>
        /// (target offset) and returns true on a hit; false on any guard failure
        /// or no-match. Never throws.
        /// </summary>
        public static bool FindOtherROMData(byte[] sourceData, byte[] targetData, uint sourcePointer, int slide, int testMatchSize, bool grepPattern, bool isCodeType, out uint outAddr, out uint outRef)
        {
            outAddr = U.NOT_FOUND;
            outRef = U.NOT_FOUND;
            try
            {
                if (sourceData == null || targetData == null) return false;
                if (testMatchSize <= 0) return false;
                if (!U.isPointer(sourcePointer)) return false;

                long srcOff = U.toOffset(sourcePointer);
                srcOff += slide;
                if (srcOff < 0) return false;
                if (srcOff >= sourceData.Length) return false;

                byte[] need = U.getBinaryData(sourceData, (uint)srcOff, testMatchSize);
                if (need == null || need.Length < 4) return false;

                uint hit = DGrep(targetData, need, grepPattern, isCodeType);
                if (hit == U.NOT_FOUND) return false;

                // Undo the slide so the reported address is the un-slid data start.
                long unslid = (long)hit - slide;
                if (unslid < 0) return false;

                uint addr = U.toPointer((uint)unslid);
                uint refaddress = GrepBigEndianPointerRef(targetData, addr);

                outAddr = addr;
                outRef = refaddress;
                return true;
            }
            catch
            {
                outAddr = U.NOT_FOUND;
                outRef = U.NOT_FOUND;
                return false;
            }
        }

        // ----- (b) LDR-literal-pool symmetry ---------------------------------

        /// <summary>
        /// WF <c>PointerToolForm.FindOtherROMDataWithLDR</c> + <c>MakeOtherROMLDRFuncList</c>
        /// port. Builds the SOURCE LDR-func list (functions whose literal pool
        /// loads <paramref name="sourcePointer"/>), greps each function body into
        /// the target, and maps the matched target function offset (+ the
        /// recorded back-size) back to a TARGET LDR literal-pool entry, returning
        /// its <c>ldr_data</c> (outAddr) + <c>ldr_data_address</c> (outRef). Never
        /// throws.
        /// </summary>
        public static bool FindOtherROMDataWithLDR(byte[] sourceData, byte[] targetData, List<DisassemblerTrumb.LDRPointer> sourceLdrMap, List<DisassemblerTrumb.LDRPointer> targetLdrMap, uint sourcePointer, int slide, int testMatchSize, bool grepPattern, bool isCodeType, out uint outAddr, out uint outRef)
        {
            outAddr = U.NOT_FOUND;
            outRef = U.NOT_FOUND;
            try
            {
                if (sourceData == null || targetData == null) return false;
                if (sourceLdrMap == null || targetLdrMap == null) return false;

                var funcList = MakeOtherROMLDRFuncList(sourceData, sourceLdrMap, sourcePointer);
                if (funcList.Count == 0) return false;

                for (int i = 0; i < funcList.Count; i++)
                {
                    uint funcAddr = funcList[i].FuncAddr;
                    uint backSize = funcList[i].BackSize;

                    if (!FindOtherROMData(sourceData, targetData, funcAddr, slide, testMatchSize, grepPattern, isCodeType, out uint otherAddr, out uint _))
                    {
                        continue;
                    }

                    // The LDR instruction in the target sits backSize bytes past
                    // the matched function start.
                    uint targetLdrAddr = U.toOffset(otherAddr + backSize);
                    for (int n = 0; n < targetLdrMap.Count; n++)
                    {
                        DisassemblerTrumb.LDRPointer otherldr = targetLdrMap[n];
                        if (otherldr == null) continue;
                        if (otherldr.ldr_address != targetLdrAddr) continue;

                        outRef = otherldr.ldr_data_address;
                        outAddr = otherldr.ldr_data;
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                outAddr = U.NOT_FOUND;
                outRef = U.NOT_FOUND;
                return false;
            }
        }

        sealed class LdrFunc
        {
            public uint FuncAddr;   // GBA pointer of the PUSH that starts the func
            public uint BackSize;   // ldr_address - func_offset (distance to the LDR)
        }

        // WF MakeOtherROMLDRFuncList: for every source LDR entry that loads the
        // search pointer, walk back from the LDR by -2 (up to SEARCH_PUSH_MAX) to
        // the function-prologue PUSH, recording {FuncAddr, BackSize}. Stops the
        // walk at a BXJMP (previous function end).
        static List<LdrFunc> MakeOtherROMLDRFuncList(byte[] sourceData, List<DisassemblerTrumb.LDRPointer> sourceLdrMap, uint sourcePointer)
        {
            var list = new List<LdrFunc>();
            if (sourceData == null || sourceLdrMap == null) return list;

            // WF compares ldr.ldr_data against the POINTER form of the search
            // address (MakeOtherROMLDRFuncList is called with U.toPointer(addr)).
            uint search = U.toPointer(sourcePointer);

            var disasm = new DisassemblerTrumb();
            var vm = new DisassemblerTrumb.VM();
            uint progLen = (uint)sourceData.Length;

            for (int i = 0; i < sourceLdrMap.Count; i++)
            {
                DisassemblerTrumb.LDRPointer ldr = sourceLdrMap[i];
                if (ldr == null) continue;
                if (ldr.ldr_data != search) continue;
                if (ldr.ldr_address <= SEARCH_PUSH_MAX) continue;

                uint limit = ldr.ldr_address - SEARCH_PUSH_MAX;
                for (uint n = ldr.ldr_address - 2; n >= limit; n -= 2)
                {
                    if (n + 2 > progLen) break; // out-of-bounds guard
                    DisassemblerTrumb.Code code;
                    try
                    {
                        code = disasm.Disassembler(sourceData, n, progLen, vm);
                    }
                    catch
                    {
                        break;
                    }
                    if (code.Type == DisassemblerTrumb.CodeType.PUSH)
                    {
                        list.Add(new LdrFunc
                        {
                            FuncAddr = U.toPointer(n),
                            BackSize = ldr.ldr_address - n,
                        });
                    }
                    else if (code.Type == DisassemblerTrumb.CodeType.BXJMP)
                    {
                        break;
                    }

                    if (n < 2) break; // avoid uint underflow on n -= 2
                }
            }
            return list;
        }

        // ----- orchestration --------------------------------------------------

        /// <summary>
        /// WF <c>PointerToolForm.AutoSearch</c> port. Order: (1) NAME search when
        /// both asm-maps are non-null; (2) at autoTrackLevel==0, a single direct +
        /// LDR pass (exact grep, no slide); (3) otherwise the auto-tracking retry
        /// loop that widens the match window (<see cref="SizeTable"/>) and slides
        /// (<see cref="SlideTable"/>), trying exact then pattern grep, accepting
        /// the first match that clears the WF <c>IsDataFound</c> gate.
        ///
        /// <para>autoTrackLevel is the WF-hex level: maxDeepSearch =
        /// ((level&gt;&gt;8)&amp;0xF)+2, maxSkipSearch = (level&amp;0xF)+1 (so the
        /// WF default 0x102 = 3 deep, 3 skip). warningLevel mirrors WF
        /// WarningLevelComboBox (0=warn-as-error, 1=accept-if-referenced,
        /// 2=ignore-warnings).</para>
        ///
        /// <para>Guards: null / short (&lt;0x400) source or target buffer →
        /// NotFound. Never throws.</para>
        /// </summary>
        public static AutoSearchResult AutoSearch(byte[] sourceData, byte[] targetData, uint address, uint autoTrackLevel, IAsmMapFile sourceAsmMap, IAsmMapFile targetAsmMap, int warningLevel = 1)
        {
            try
            {
                if (sourceData == null || targetData == null) return AutoSearchResult.NotFound;
                if (sourceData.Length < 0x400 || targetData.Length < 0x400) return AutoSearchResult.NotFound;
                if (address == 0 || address == U.NOT_FOUND) return AutoSearchResult.NotFound;

                // Normalize: an odd address is ASM code (WF addr--). Always grep
                // ASM as code-type so makeSkipDataByCode applies on pattern grep.
                bool isCode = (address % 4) == 1;
                if (isCode) address -= 1;
                uint pointer = U.toPointer(address);

                // (a) NAME search first.
                if (sourceAsmMap != null && targetAsmMap != null)
                {
                    var nameHit = SearchAsmMapName(pointer, sourceAsmMap, targetAsmMap, targetData);
                    if (nameHit.Found) return nameHit;
                }

                // Build the source + target LDR maps (WU2b-hardened, never throws).
                List<DisassemblerTrumb.LDRPointer> sourceLdr;
                List<DisassemblerTrumb.LDRPointer> targetLdr;
                try { sourceLdr = DisassemblerTrumb.MakeLDRMap(sourceData, 0x100, 0); }
                catch { sourceLdr = new List<DisassemblerTrumb.LDRPointer>(); }
                try { targetLdr = DisassemblerTrumb.MakeLDRMap(targetData, 0x100, 0); }
                catch { targetLdr = new List<DisassemblerTrumb.LDRPointer>(); }

                int maxDeepSearch = (int)((autoTrackLevel >> 8) & 0xF) + 2;
                int maxSkipSearch = (int)(autoTrackLevel & 0xF) + 1;

                if (autoTrackLevel == 0)
                {
                    // No auto-tracking: single pass, size index 0, exact grep, no slide.
                    var single = TryOnce(sourceData, targetData, sourceLdr, targetLdr, pointer, address,
                        slide: 0, testMatchSize: SizeTable[0], grepPattern: false, isCodeType: isCode, warningLevel: warningLevel);
                    return single ?? AutoSearchResult.NotFound;
                }

                // Auto-tracking retry loop (WF: widen match window, slide).
                for (int deepSearch = 1; deepSearch < maxDeepSearch; deepSearch++)
                {
                    int testMatchSize = SizeTable[Math.Min(deepSearch, SizeTable.Length - 1)];

                    // Exact grep at slide 0.
                    var hitExact = TryOnce(sourceData, targetData, sourceLdr, targetLdr, pointer, address,
                        slide: 0, testMatchSize: testMatchSize, grepPattern: false, isCodeType: isCode, warningLevel: warningLevel);
                    if (hitExact != null) return hitExact;

                    // Pattern grep at slide 0.
                    var hitPattern = TryOnce(sourceData, targetData, sourceLdr, targetLdr, pointer, address,
                        slide: 0, testMatchSize: testMatchSize, grepPattern: true, isCodeType: isCode, warningLevel: warningLevel);
                    if (hitPattern != null) return hitPattern;

                    // Sliding window (WF: SlideComboBox.SelectedIndex = skipSearch).
                    for (int skipSearch = 1; skipSearch < maxSkipSearch; skipSearch++)
                    {
                        int slide = SlideTable[Math.Min(skipSearch, SlideTable.Length - 1)];
                        var hitSlid = TryOnce(sourceData, targetData, sourceLdr, targetLdr, pointer, address,
                            slide: slide, testMatchSize: testMatchSize, grepPattern: false, isCodeType: isCode, warningLevel: warningLevel);
                        if (hitSlid != null) return hitSlid;
                    }
                }

                return AutoSearchResult.NotFound;
            }
            catch
            {
                return AutoSearchResult.NotFound;
            }
        }

        // One direct + LDR comparison at a fixed (slide, size, grep mode); returns
        // an accepted AutoSearchResult or null when neither sub-result clears the
        // WF IsDataFound gate. `address` is the un-pointered source address used
        // by checkVeryFar to gauge distance.
        static AutoSearchResult TryOnce(byte[] sourceData, byte[] targetData, List<DisassemblerTrumb.LDRPointer> sourceLdr, List<DisassemblerTrumb.LDRPointer> targetLdr, uint pointer, uint address, int slide, int testMatchSize, bool grepPattern, bool isCodeType, int warningLevel)
        {
            uint directAddr = U.NOT_FOUND, directRef = U.NOT_FOUND;
            uint ldrAddr = U.NOT_FOUND, ldrRef = U.NOT_FOUND;

            FindOtherROMDataWithLDR(sourceData, targetData, sourceLdr, targetLdr, pointer, slide, testMatchSize, grepPattern, isCodeType, out ldrAddr, out ldrRef);
            FindOtherROMData(sourceData, targetData, pointer, slide, testMatchSize, grepPattern, isCodeType, out directAddr, out directRef);

            // WF IsDataFound: evaluate the DIRECT result first, then the LDR.
            if (IsAccepted(targetData, address, directAddr, directRef, warningLevel))
            {
                return new AutoSearchResult
                {
                    Found = true,
                    Hit = "direct",
                    DirectAddr = directAddr,
                    DirectRef = directRef,
                    LdrAddr = ldrAddr,
                    LdrRef = ldrRef,
                };
            }
            if (IsAccepted(targetData, address, ldrAddr, ldrRef, warningLevel))
            {
                return new AutoSearchResult
                {
                    Found = true,
                    Hit = "ldr",
                    DirectAddr = directAddr,
                    DirectRef = directRef,
                    LdrAddr = ldrAddr,
                    LdrRef = ldrRef,
                };
            }
            return null;
        }

        // ----- WF IsDataFound accept gate ------------------------------------

        // Port of WF PointerToolForm.IsDataFound's per-address branch.
        // foundaddress / refaddress are GBA pointers (or NOT_FOUND).
        static bool IsAccepted(byte[] targetData, uint searchAddress, uint foundaddress, uint refaddress, int warningLevel)
        {
            if (!IsFoundAddress(foundaddress)) return false;

            bool veryFar = CheckVeryFar(searchAddress, foundaddress);
            bool zero = CheckZeroData(targetData, U.toOffset(foundaddress), U.toOffset(foundaddress) + 0x200);

            if (warningLevel == 0)
            {
                // Warnings are errors: accept only when neither warning fires.
                return !veryFar && !zero;
            }
            if (warningLevel == 1)
            {
                // Accept when referenced, OR when no warning fired.
                if (IsFoundAddress(refaddress)) return true;
                return !veryFar && !zero;
            }
            // warningLevel >= 2: ignore warnings entirely.
            return true;
        }

        static bool IsFoundAddress(uint p) => p != U.NOT_FOUND && p != 0;

        // Port of WF checkZeroData: a region [start, end) is "zero" when more than
        // half its bytes are 0x00. Bounds-safe.
        static bool CheckZeroData(byte[] data, uint start, uint end)
        {
            if (data == null) return false;
            if (data.Length < start) return false;
            if (data.Length < end) end = (uint)data.Length;
            if (start >= end) return false;

            int zeroCount = 0;
            for (uint i = start; i < end; i++)
            {
                if (data[i] == 0x0) zeroCount++;
            }
            return zeroCount > (int)(end - start) / 2;
        }

        // Port of WF checkVeryFar: the match is "very far" when its offset
        // distance from the search address exceeds the address-band tolerance.
        static bool CheckVeryFar(uint searchAddr, uint addr)
        {
            int diff = Math.Abs((int)U.toOffset(addr) - (int)U.toOffset(searchAddr));
            int tol = GetYoninGosa(Math.Min(U.toPointer(addr), U.toPointer(searchAddr)));
            return diff >= tol;
        }

        // Port of WF GetYoninGosa: empirical tolerance band (later ROM regions
        // drift more between versions).
        static int GetYoninGosa(uint p)
        {
            if (p < 0x02040000) return 0x100;
            if (p < 0x08001000) return 0x400;
            if (p < 0x08008000) return 0x800;
            if (p < 0x08010000) return 0x2000;
            if (p < 0x08040000) return 0x5000;
            if (p < 0x08080000) return 0x8000;
            if (p < 0x08100000) return 0x20000;
            if (p < 0x08200000) return 0x40000;
            return 0x100000;
        }

        // ----- grep helpers ---------------------------------------------------

        // Port of WF DGrep: exact (U.Grep blocksize 2) or masked pattern match.
        static uint DGrep(byte[] data, byte[] need, bool grepPattern, bool isCodeType)
        {
            if (!grepPattern)
            {
                return U.Grep(data, need, 0, 0, 2);
            }
            bool[] isSkip = isCodeType ? MakeSkipDataByCode(need) : MakeSkipDataByPointer(need);
            return U.GrepPatternMatch(data, need, isSkip, 0, 0, 2);
        }

        // Build a big-endian-stored pointer (the on-ROM little-endian byte order
        // of a GBA pointer) and grep the target for it. Returns the reference
        // offset or U.NOT_FOUND. Mirrors WF's byte-swap-then-grep in
        // FindOtherROMData / SearchASMMap.
        static uint GrepBigEndianPointerRef(byte[] data, uint pointerAddr)
        {
            if (data == null) return U.NOT_FOUND;
            uint little = ((pointerAddr >> 24) & 0xFF)
                        + (((pointerAddr >> 16) & 0xFF) << 8)
                        + (((pointerAddr >> 8) & 0xFF) << 16)
                        + (((pointerAddr) & 0xFF) << 24);
            byte[] b = new byte[4];
            b[0] = (byte)((little >> 24) & 0xFF);
            b[1] = (byte)((little >> 16) & 0xFF);
            b[2] = (byte)((little >> 8) & 0xFF);
            b[3] = (byte)((little) & 0xFF);
            return U.Grep(data, b);
        }

        // Port of WF makeSkipDataByPointer: mark each 4-byte word that looks like
        // a GBA pointer as a wildcard (it differs between ROM versions).
        public static bool[] MakeSkipDataByPointer(byte[] need)
        {
            uint length = (uint)need.Length;
            bool[] isSkip = new bool[U.Padding4(length)];
            if (length < 4) return isSkip;
            for (uint i = 0; i + 4 <= length; i += 4)
            {
                if (U.isPointer(U.u32(need, i)))
                {
                    isSkip[i + 0] = true;
                    isSkip[i + 1] = true;
                    isSkip[i + 2] = true;
                    isSkip[i + 3] = true;
                }
            }
            return isSkip;
        }

        // Port of WF makeSkipDataByCode: disassemble `need` as Thumb code; mark
        // function-call operands, long-distance LDR sites, and literal-pool
        // pointer words as wildcards (they shift between ROM versions).
        public static bool[] MakeSkipDataByCode(byte[] need)
        {
            var disasm = new DisassemblerTrumb();
            var vm = new DisassemblerTrumb.VM();
            uint length = (uint)need.Length;
            bool[] isSkip = new bool[U.Padding4(length)];
            if (length < 4) return isSkip;

            bool isFunctionEnd = false;
            uint ldrCount = 0;

            for (uint i = 0; i + 4 <= length; )
            {
                if (isFunctionEnd)
                {
                    // Past the function body — only literal-pool pointer words remain.
                    if (ldrCount <= 0)
                    {
                        isFunctionEnd = false;
                        continue;
                    }
                    ldrCount--;
                    if (U.isPointer(U.u32(need, i)))
                    {
                        isSkip[i + 0] = true;
                        isSkip[i + 1] = true;
                        isSkip[i + 2] = true;
                        isSkip[i + 3] = true;
                    }
                    i += 4;
                    continue;
                }

                DisassemblerTrumb.Code code;
                try { code = disasm.Disassembler(need, i, length, vm); }
                catch { break; }

                if (code.Type == DisassemblerTrumb.CodeType.CALL)
                {
                    isSkip[i + 0] = true;
                    isSkip[i + 1] = true;
                    isSkip[i + 2] = true;
                    isSkip[i + 3] = true;
                }
                if (code.Type == DisassemblerTrumb.CodeType.LDR)
                {
                    uint a = U.u16(need, i);
                    uint imm = DisassemblerTrumb.LDR_IMM(a);
                    if (imm >= 0x64)
                    {
                        // A very long LDR distance drifts between versions; mask
                        // the load and its following instruction.
                        isSkip[i + 0] = true;
                        isSkip[i + 1] = true;
                        isSkip[i + 2] = true;
                        isSkip[i + 3] = true;
                    }
                    else
                    {
                        isSkip[i + 0] = true;
                        ldrCount++;
                    }
                }

                i += code.GetLength();

                if (code.Type == DisassemblerTrumb.CodeType.BXJMP)
                {
                    isFunctionEnd = true;
                    i = U.Padding4(i);
                }
            }
            return isSkip;
        }
    }
}
