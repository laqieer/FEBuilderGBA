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
        // WF auto-tracking widening tables — both VERBATIM from
        // FEBuilderGBA/PointerToolForm.Designer.cs.
        //
        // SizeTable is the TestMatchDataSizeComboBox.Items (lines 472-493),
        // parsed by WF minusAtoH (hex value, dropping the "=NNNbyte" suffix):
        //   "100=512byte","FF=256byte","80=128byte","60=96byte","40=64byte",
        //   "30=48byte","20=32byte","1E=30byte","1C=28byte","1A=26byte",
        //   "18=24byte","16=22byte","14=20byte","12=18byte","10=16byte",
        //   "0E=14byte","0C=12byte","0A=10byte","08=8byte","06=6byte","04=4byte".
        // WF AutoSearch resets TestMatchDataSizeComboBox.SelectedIndex = 0
        // (= 0x100 = 512 bytes — the "default 32 bytes" code comment in WF is
        // misleading) and then widens the search by setting SelectedIndex =
        // deepSearch (1,2,3…), so the match WINDOW SHRINKS across iterations.
        // This helper indexes the table by deepSearch identically.
        static readonly int[] SizeTable =
        {
            0x100, 0xFF, 0x80, 0x60, 0x40, 0x30, 0x20, 0x1E, 0x1C, 0x1A,
            0x18, 0x16, 0x14, 0x12, 0x10, 0x0E, 0x0C, 0x0A, 0x08, 0x06, 0x04,
        };
        // SlideTable is the SlideComboBox.Items (lines 380-388), verbatim.
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

                // WF SearchASMMap finds the ref via plain U.Grep with DEFAULTS
                // (start 0x100, blocksize 1, exact) — deliberately NOT the
                // mode-aware DGrep that FindOtherROMData uses. Keep this path on
                // GrepBigEndianPointerRef (U.Grep defaults) for WF parity.
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
                // WF FindOtherROMData finds the ref via DGrep (mode-aware:
                // exact/pattern, start 0, blocksize 2) — NOT the name path's
                // defaults U.Grep. Route through DGrep with the SAME grepPattern +
                // isCodeType flags so the warningLevel==1 "accept if referenced"
                // gate matches WF and a ref below 0x100 is now findable.
                uint refaddress = DGrep(targetData, BigEndianPointerBytes(addr), grepPattern, isCodeType);

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
            // Original signature: build the LDR maps internally (passing null ->
            // the overload builds them). Existing Core tests + non-caching callers
            // are unaffected.
            return AutoSearch(sourceData, targetData, address, autoTrackLevel, sourceAsmMap, targetAsmMap, warningLevel, null, null);
        }

        /// <summary>
        /// Precomputed-LDR-map overload of <see cref="AutoSearch(byte[],byte[],uint,uint,IAsmMapFile,IAsmMapFile,int)"/>
        /// (#1118). Identical behavior, but the caller may pass cached
        /// <paramref name="sourceLdr"/> / <paramref name="targetLdr"/> maps to
        /// avoid a full-ROM <see cref="DisassemblerTrumb.MakeLDRMap"/> rescan on
        /// every call (the Avalonia VM caches them per loaded ROM — repeated Auto
        /// Search clicks otherwise stall the UI thread). A null map is built
        /// internally (still safe). WF builds/caches these on target-ROM load.
        /// </summary>
        public static AutoSearchResult AutoSearch(byte[] sourceData, byte[] targetData, uint address, uint autoTrackLevel, IAsmMapFile sourceAsmMap, IAsmMapFile targetAsmMap, int warningLevel, List<DisassemblerTrumb.LDRPointer> sourceLdr, List<DisassemblerTrumb.LDRPointer> targetLdr)
        {
            try
            {
                if (sourceData == null || targetData == null) return AutoSearchResult.NotFound;
                if (sourceData.Length < 0x400 || targetData.Length < 0x400) return AutoSearchResult.NotFound;
                if (address == 0 || address == U.NOT_FOUND) return AutoSearchResult.NotFound;

                // Normalize: a Thumb function pointer is odd (bit0=1) — it can end
                // in ...01 (4-aligned base) OR ...03 (2-aligned-but-not-4 base).
                // Mask bit0 to recover the even base, then grep ASM as code-type
                // so makeSkipDataByCode applies on pattern grep. Strictly more
                // correct than WF's `%4==1`, which missed the ...03 halfword-
                // aligned case (treating a valid Thumb pointer as data).
                bool isCode = (address & 1) == 1;
                if (isCode) address -= 1;
                uint pointer = U.toPointer(address);

                // (a) NAME search first.
                if (sourceAsmMap != null && targetAsmMap != null)
                {
                    var nameHit = SearchAsmMapName(pointer, sourceAsmMap, targetAsmMap, targetData);
                    if (nameHit.Found) return nameHit;
                }

                // Use the caller-supplied LDR maps when provided (cached); else
                // build them here (WU2b-hardened, never throws). A passed-in null
                // map is treated as "not yet built" and built internally.
                if (sourceLdr == null)
                {
                    try { sourceLdr = DisassemblerTrumb.MakeLDRMap(sourceData, 0x100, 0); }
                    catch { sourceLdr = new List<DisassemblerTrumb.LDRPointer>(); }
                }
                if (targetLdr == null)
                {
                    try { targetLdr = DisassemblerTrumb.MakeLDRMap(targetData, 0x100, 0); }
                    catch { targetLdr = new List<DisassemblerTrumb.LDRPointer>(); }
                }

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
                        // WF leaves GrepType=1 (pattern) when entering the skipSearch
                        // slide loop (it never resets it after the slide-0 pattern
                        // attempt above), so slid attempts are pattern-masked. Use
                        // grepPattern:true to match — exact+slide alone misses
                        // matches that need both the slide offset AND pointer/code
                        // masking.
                        var hitSlid = TryOnce(sourceData, targetData, sourceLdr, targetLdr, pointer, address,
                            slide: slide, testMatchSize: testMatchSize, grepPattern: true, isCodeType: isCode, warningLevel: warningLevel);
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

        /// <summary>
        /// Build the full-buffer LDR literal-pool map (#1118) for callers that
        /// cache it (the Avalonia VM caches the source + target maps per loaded
        /// ROM so repeated Auto Search clicks don't re-scan the whole ROM on the
        /// UI thread). WU2b-hardened; never throws (returns an empty list on a
        /// null buffer or any disassembly fault).
        /// </summary>
        public static List<DisassemblerTrumb.LDRPointer> BuildLdrMap(byte[] data)
        {
            if (data == null) return new List<DisassemblerTrumb.LDRPointer>();
            try { return DisassemblerTrumb.MakeLDRMap(data, 0x100, 0); }
            catch { return new List<DisassemblerTrumb.LDRPointer>(); }
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

        // Build the 4 on-ROM (little-endian) bytes of a GBA pointer. WF byte-swaps
        // the pointer then writes the swapped value big-endian into a 4-byte
        // buffer — the net effect is the pointer's natural little-endian on-ROM
        // representation. Shared by both ref-search paths (name vs direct-data).
        static byte[] BigEndianPointerBytes(uint pointerAddr)
        {
            uint little = ((pointerAddr >> 24) & 0xFF)
                        + (((pointerAddr >> 16) & 0xFF) << 8)
                        + (((pointerAddr >> 8) & 0xFF) << 16)
                        + (((pointerAddr) & 0xFF) << 24);
            byte[] b = new byte[4];
            b[0] = (byte)((little >> 24) & 0xFF);
            b[1] = (byte)((little >> 16) & 0xFF);
            b[2] = (byte)((little >> 8) & 0xFF);
            b[3] = (byte)((little) & 0xFF);
            return b;
        }

        // NAME-search ref grep ONLY. Mirrors WF SearchASMMap, which finds the ref
        // via plain U.Grep with DEFAULTS (start 0x100, blocksize 1, exact). The
        // direct-data path in FindOtherROMData deliberately differs (it uses the
        // mode-aware DGrep — start 0, blocksize 2, exact/pattern) to match WF
        // FindOtherROMData. Do NOT unify these two paths.
        static uint GrepBigEndianPointerRef(byte[] data, uint pointerAddr)
        {
            if (data == null) return U.NOT_FOUND;
            return U.Grep(data, BigEndianPointerBytes(pointerAddr));
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
