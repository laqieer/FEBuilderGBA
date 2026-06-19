using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free in-place <b>Uninstall</b> for an applied Event Assembler patch
    /// (#1242, follow-up to #1170's <see cref="EventAssemblerCompileCore"/>). Ported
    /// from the WinForms patch-uninstall path
    /// (<c>EventAssemblerForm.UninstallButton_Click</c> →
    /// <c>PatchForm.MakeInstantEAToPatch</c> → <c>PatchForm.UnInstallPatch</c> →
    /// <c>TracePatchedMapping</c>/<c>TraceEAPatchedMapping</c> →
    /// <c>UninstallPatchInner</c>), keeping the exact trace semantics, the
    /// clean-original-ROM restore, the auto-length fallback and the ROM-tail strip.
    ///
    /// Flow: parse the EA file with <see cref="EAUtilCore"/> into written ranges
    /// (<see cref="BinMapping"/>s) against the CURRENT (patched) ROM, then overwrite
    /// each traced byte with the corresponding byte from a user-supplied
    /// <b>clean original</b> ROM (the ROM as it was before the patch), recording every
    /// write into an explicit <see cref="Undo.UndoData"/> so the uninstall is fully
    /// undoable. Never throws on bad input — a wrong / mismatched clean ROM returns a
    /// localized error string instead (the live ROM is left untouched in that case).
    ///
    /// Scope vs the WinForms patch uninstall (intentional, documented):
    ///  - This operates on a SINGLE loaded .event (the Avalonia EA tool's input),
    ///    matching <c>MakeInstantEAToPatch</c> which builds a bare
    ///    <c>{TYPE=EA, EA=filename}</c> patch with NO extra params. The WinForms
    ///    trailing helpers driven by patch params — <c>TraceEditPatch</c> (EDIT_PATCH),
    ///    <c>AppendMenuPatch</c> (MENU), <c>AppendNewTargetSelectionStruct</c>,
    ///    <c>AppnedInstallMapping</c> (PATCHED_IF/IF) — have no params to act on for an
    ///    instant-EA patch, so they add nothing and are omitted here.
    ///  - PROCS (<c>HINT=PROCS</c>) length detection uses the WinForms-only
    ///    <c>ProcsScriptForm.CalcLengthAndCheck</c>; this Core path skips a PROCS range
    ///    whose length it cannot determine — identical observable behaviour to the
    ///    WinForms tracer, which <c>continue</c>s when that returns NOT_FOUND.
    ///
    /// NEVER silently incomplete: any block the trace cannot reconstruct (a GREP miss,
    /// the guarded PROCS, an un-hinted inline PNG raster) is recorded on
    /// <see cref="TraceResult.Untraceable"/> / <see cref="UninstallResult.UntraceableBlocks"/>
    /// and flips <see cref="UninstallResult.FullyTraced"/> to false. The View MUST warn
    /// the user when FullyTraced is false — a revert that quietly leaves patch residue
    /// while reporting success is the one outcome this design avoids.
    /// </summary>
    public static class EventAssemblerUninstallCore
    {
        /// <summary>
        /// One traced ROM range written by the EA patch. Mirrors WinForms
        /// <c>PatchForm.BinMapping</c> (the subset the EA path populates).
        /// </summary>
        public sealed class BinMapping
        {
            public string key;
            public string filename;
            public uint addr;
            public uint length;     // 0 == unknown (auto-length on revert)
            public Address.DataTypeEnum type;
            public byte[] bin;
            public bool[] mask;
        }

        /// <summary>
        /// Result of tracing an EA file: the ranges we COULD reconstruct, plus notes
        /// about blocks we could NOT (so an uninstall is never silently incomplete).
        /// </summary>
        public sealed class TraceResult
        {
            /// <summary>The ranges the EA patch wrote that we reconstructed.</summary>
            public List<BinMapping> Mappings { get; } = new List<BinMapping>();
            /// <summary>
            /// Human-readable descriptions of blocks we could NOT trace (GREP miss,
            /// guarded PROCS length, un-hinted inline PNG raster, …). Each entry is one
            /// piece of patch residue a revert will leave behind.
            /// </summary>
            public List<string> Untraceable { get; } = new List<string>();
            /// <summary>True when every emitting block was reconstructed (no residue).</summary>
            public bool FullyTraced => Untraceable.Count == 0;
            /// <summary>
            /// Count of WRITE-BEARING blocks the trace could not reconstruct (a GREP
            /// miss, the guarded PROCS, an un-hinted inline PNG raster). Empty/comment
            /// blocks that write nothing are NOT counted — they leave no residue.
            /// Equals <see cref="Untraceable"/>.Count.
            /// </summary>
            public int UntracedCount => Untraceable.Count;
        }

        /// <summary>Structured result of an uninstall run.</summary>
        public sealed class UninstallResult
        {
            /// <summary>True when the patch ranges were restored from the clean ROM.</summary>
            public bool Success { get; set; }
            /// <summary>Localized error (set when <see cref="Success"/> is false).</summary>
            public string ErrorMessage { get; set; } = "";
            /// <summary>Number of traced ranges that were reverted.</summary>
            public int RangeCount { get; set; }
            /// <summary>Total bytes written back from the clean ROM.</summary>
            public uint BytesReverted { get; set; }
            /// <summary>
            /// False when the trace could NOT account for every block the EA patch
            /// wrote, so the revert may leave patch residue. The View MUST warn the user
            /// when this is false (a "success" with residue is the outcome to avoid).
            /// </summary>
            public bool FullyTraced { get; set; } = true;
            /// <summary>
            /// Count of WRITE-BEARING blocks the trace could not revert (a GREP miss,
            /// the guarded PROCS, an un-hinted inline PNG raster). > 0 means the revert
            /// is INCOMPLETE — those bytes remain patched. <see cref="Success"/> can
            /// still be true (the traced ranges WERE reverted); the View must surface
            /// this so the user can confirm/verify. Equals <see cref="UntraceableBlocks"/>.Count.
            /// </summary>
            public int UntracedCount { get; set; }
            /// <summary>Descriptions of the blocks that could not be traced (residue).</summary>
            public List<string> UntraceableBlocks { get; set; } = new List<string>();
        }

        /// <summary>
        /// Trace the ranges an EA file writes, against the CURRENT (patched) ROM in
        /// <see cref="CoreState.ROM"/>. Faithful port of the EA branch of
        /// <c>TraceEAPatchedMapping</c>, walking a single .event's
        /// <see cref="EAUtilCore"/> DataList. The result carries BOTH the reconstructed
        /// ranges AND notes about any block that could NOT be traced (GREP miss, guarded
        /// PROCS, un-hinted inline PNG raster) so a revert is never silently incomplete.
        /// Returns an empty result (with one Untraceable note) when the ROM is not loaded
        /// or the file cannot be parsed.
        /// </summary>
        public static TraceResult TraceEAFile(string eaFilePath)
        {
            var result = new TraceResult();
            List<BinMapping> binMappings = result.Mappings;
            ROM rom = CoreState.ROM;
            // RomInfo is needed for the GREP search baseline
            // (compress_image_borderline_address); it is null only for a not-yet-
            // identified ROM, which the EA tool never uninstalls against. Guard so a
            // bad caller gets an empty trace (→ clean "could not trace" error) rather
            // than an NRE.
            if (rom == null || rom.RomInfo == null
                || string.IsNullOrEmpty(eaFilePath) || !File.Exists(eaFilePath))
            {
                result.Untraceable.Add(R._("No identified ROM, or the event file is missing."));
                return result;
            }
            if (EAUtilCore.IsFBGTemp(eaFilePath))
            {
                result.Untraceable.Add(R._("Temporary wrapper file is not a traceable patch."));
                return result;
            }

            EAUtilCore ea;
            try
            {
                ea = new EAUtilCore(eaFilePath);
            }
            catch (Exception ex)
            {
                // The parser can throw more than IOException (UnauthorizedAccessException,
                // ArgumentException for a bad path, parse errors). Honour the "never
                // throws" contract: turn any failure into an untraceable note + empty
                // trace, which the caller surfaces as a clean "could not trace" error.
                result.Untraceable.Add(R._("Could not read the event file: {0}", ex.Message));
                return result;
            }

            // Carry over parser-level untraceable blocks (un-hinted inline PNG rasters).
            result.Untraceable.AddRange(ea.UntraceableNotes);

            uint lastMatchAddr = rom.RomInfo.compress_image_borderline_address;

            for (int n = 0; n < ea.DataList.Count; n++)
            {
                EAUtilCore.Data data = ea.DataList[n];
                if (data.DataType == EAUtilCore.DataEnum.ORG)
                {
                    uint addr = data.ORGAddr;

                    var b = new BinMapping
                    {
                        key = "ORG",
                        filename = "",
                        addr = addr,
                        length = 0, //不明
                        type = Address.DataTypeEnum.MIX,
                    };

                    if (U.isSafetyOffset(addr + 64))
                    {//長さが不明なので比較するとき困るので適当に64バイトほど取得します.
                        b.bin = rom.getBinaryData(addr, 64);
                        b.mask = MakeMaskAddress(b.bin, addr);
                    }
                    else
                    {
                        b.bin = new byte[0] { };
                        b.mask = new bool[0] { };
                    }

                    binMappings.Add(b);
                    lastMatchAddr = addr;
                }
                else if (data.DataType == EAUtilCore.DataEnum.ASM
                    || data.DataType == EAUtilCore.DataEnum.MIX)
                {
                    if (data.BINData == null || data.BINData.Length == 0)
                    {//empty data
                        continue;
                    }
                    //展開されるものを生成して、GREP検索する必要があります.
                    bool[] isSkip;
                    byte[] mod = ReadMod(data.BINData, out isSkip);

                    //可変なので、maskパターンを作って検索します.
                    uint addr = U.GrepPatternMatch(rom.Data, mod, isSkip, lastMatchAddr, 0, 4);
                    if (addr == U.NOT_FOUND)
                    {//パッチが見つからなかった — 復元できない残骸として記録する.
                        result.Untraceable.Add(R._("{0} block not found in ROM: {1}",
                            data.DataType.ToString(), BlockName(data.Name)));
                        continue;
                    }

                    uint length = (uint)mod.Length;

                    var b = new BinMapping
                    {
                        key = data.DataType.ToString(),
                        filename = data.Name,
                        addr = addr,
                        length = length,
                        bin = rom.getBinaryData(addr, length),
                        mask = isSkip,
                        type = data.DataType == EAUtilCore.DataEnum.ASM
                            ? Address.DataTypeEnum.PATCH_ASM
                            : Address.DataTypeEnum.MIX,
                    };

                    EraseORG(binMappings, b);
                    binMappings.Add(b);

                    lastMatchAddr = addr + length;
                }
                else if (data.DataType == EAUtilCore.DataEnum.LYN)
                {
                    if (data.BINData == null || data.BINData.Length == 0)
                    {//empty data
                        continue;
                    }
                    //展開されるものを生成して、GREP検索する必要があります.
                    bool[] isSkip = MakeLynMaskPattern(data.BINData);

                    //可変なので、maskパターンを作って検索します.
                    uint addr = U.GrepPatternMatch(rom.Data, data.BINData, isSkip, lastMatchAddr, 0, 4);
                    if (addr == U.NOT_FOUND)
                    {//LYN ELFが見つからなかった — 復元できない残骸として記録する.
                        result.Untraceable.Add(R._("LYN block not found in ROM: {0}", BlockName(data.Name)));
                        continue;
                    }
                    uint length = (uint)data.BINData.Length;

                    var b = new BinMapping
                    {
                        key = data.DataType.ToString(),
                        filename = data.Name,
                        addr = addr,
                        length = length,
                        bin = rom.getBinaryData(addr, length),
                        mask = isSkip,
                        type = Address.DataTypeEnum.PATCH_ASM,
                    };

                    EraseORG(binMappings, b);
                    binMappings.Add(b);

                    lastMatchAddr = addr + length;
                }
                else if (data.DataType == EAUtilCore.DataEnum.LYNHOOK)
                {
                    uint addr = data.ORGAddr;
                    // 20 bytes (NOT the 16-byte hook stub): matches WF
                    // PatchForm.TraceEAPatchedMapping, which reverts the 16-byte lyn hook
                    // stub plus the 4-byte aligned slot lyn may also overwrite. See the
                    // EAUtilCore.DataEnum.LYNHOOK comment.
                    uint length = (uint)20;

                    var b = new BinMapping
                    {
                        key = "ORG",
                        filename = "",
                        addr = data.ORGAddr,
                        length = length,
                        bin = rom.getBinaryData(addr, length),
                        type = Address.DataTypeEnum.PATCH_ASM,
                    };
                    b.mask = MakeMaskAddress(b.bin, addr);

                    binMappings.Add(b);

                    lastMatchAddr = addr + length;
                }
                else if (data.DataType == EAUtilCore.DataEnum.POINTER_ARRAY)
                {
                    //最後に書き込んだ部分から、ポインタと思われる部分を連続して検出する.
                    lastMatchAddr += data.Append;
                    lastMatchAddr = U.Padding4(lastMatchAddr);

                    uint addr = lastMatchAddr;
                    for (; addr + 3 < rom.Data.Length; addr += 4)
                    {
                        uint a = rom.u32(addr);
                        if (!U.isSafetyPointer(a))
                        {
                            break;
                        }
                    }
                    uint length = addr - lastMatchAddr;
                    if (length <= 0)
                    {
                        continue;
                    }
                    addr = lastMatchAddr;

                    var b = new BinMapping
                    {
                        key = data.DataType.ToString(),
                        filename = data.Name,
                        addr = addr,
                        length = length,
                        bin = rom.getBinaryData(addr, length),
                        mask = MakeFullMask(length),
                        type = Address.DataTypeEnum.POINTER_ARRAY,
                    };

                    EraseORG(binMappings, b);
                    binMappings.Add(b);

                    lastMatchAddr = addr + length;
                }
                else if (data.DataType == EAUtilCore.DataEnum.PROCS)
                {
                    // Advance the GREP baseline BEFORE skipping — exactly as WF
                    // PatchForm.TraceEAPatchedMapping does (lastMatchAddr += Append;
                    // Padding4) at the TOP of its PROCS branch, before the
                    // CalcLengthAndCheck==NOT_FOUND `continue`. Omitting this would leave
                    // lastMatchAddr pointing BEFORE the PROCS region, so the next block's
                    // GREP could match an earlier address and revert the WRONG bytes.
                    lastMatchAddr += data.Append;
                    lastMatchAddr = U.Padding4(lastMatchAddr);

                    // PROCS length detection (ProcsScriptForm.CalcLengthAndCheck) is
                    // WinForms-only; without it the length is unknown, so we skip the
                    // range itself — identical to the WinForms tracer's `continue` when
                    // CalcLengthAndCheck returns NOT_FOUND. Record it as residue so the
                    // caller can warn. (See class doc scope note.)
                    result.Untraceable.Add(R._("PROCS table (length detection is WinForms-only): {0}",
                        string.IsNullOrEmpty(data.Name) ? R._("(label)") : data.Name));
                    continue;
                }
                else
                {
                    //展開されるものを生成して、GREP検索する必要があります.
                    if (data.BINData == null || data.BINData.Length == 0)
                    {//EAは何も書き込んでいない — 残骸ではない.
                        continue;
                    }
                    uint addr = U.Grep(rom.Data, data.BINData, lastMatchAddr);
                    if (addr == U.NOT_FOUND)
                    {//BINが見つからなかった — 復元できない残骸として記録する.
                        result.Untraceable.Add(R._("BIN block not found in ROM: {0}", BlockName(data.Name)));
                        continue;
                    }
                    uint length = (uint)data.BINData.Length;

                    var b = new BinMapping
                    {
                        key = data.DataType.ToString(),
                        filename = data.Name,
                        addr = addr,
                        length = length,
                        bin = data.BINData,
                        type = Address.DataTypeEnum.BIN,
                    };
                    if (data.DataType == EAUtilCore.DataEnum.BIN)
                    {
                        b.mask = new bool[length];
                    }
                    else
                    {
                        b.mask = MakeMaskAddress(b.bin, addr);
                    }

                    EraseORG(binMappings, b);
                    binMappings.Add(b);

                    lastMatchAddr = addr + length;
                }
            }

            return result;
        }

        /// <summary>
        /// Trace <paramref name="eaFilePath"/> against the live ROM and revert every
        /// traced range to the bytes in <paramref name="cleanOriginalRom"/> (the ROM as
        /// it was before the patch), recording all writes into <paramref name="undo"/>.
        ///
        /// Validation (mirrors the WinForms uninstall dialog's checks — never throws):
        ///  - a ROM must be loaded;
        ///  - the EA file must exist and trace at least one range;
        ///  - the clean ROM must be non-empty and at least as large as the highest
        ///    traced offset, so a byte-for-byte restore is well-defined.
        /// On any failure the live ROM is left untouched and a localized error is
        /// returned; the caller rolls back <paramref name="undo"/>.
        /// </summary>
        public static UninstallResult Uninstall(string eaFilePath, byte[] cleanOriginalRom, Undo.UndoData undo)
        {
            var result = new UninstallResult();

            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                result.ErrorMessage = R._("No ROM is loaded.");
                return result;
            }
            if (string.IsNullOrEmpty(eaFilePath) || !File.Exists(eaFilePath))
            {
                result.ErrorMessage = R._("Event file not found: {0}", eaFilePath ?? "");
                return result;
            }
            if (cleanOriginalRom == null || cleanOriginalRom.Length <= 0)
            {
                result.ErrorMessage = R._("The selected clean ROM is empty.");
                return result;
            }
            if (undo == null)
            {
                result.ErrorMessage = R._("Undo manager unavailable.");
                return result;
            }

            TraceResult trace = TraceEAFile(eaFilePath);
            List<BinMapping> binmap = trace.Mappings;

            // Surface untraceable blocks on the result so the View can warn the user
            // (a "success" that leaves patch residue is the outcome to avoid).
            result.FullyTraced = trace.FullyTraced;
            result.UntraceableBlocks = trace.Untraceable;
            result.UntracedCount = trace.UntracedCount;

            if (binmap.Count == 0)
            {
                // Nothing reconstructible. If the trace recorded untraceable blocks,
                // say so specifically (residue we can't revert) rather than the generic
                // "not a patch" message.
                result.ErrorMessage = trace.Untraceable.Count > 0
                    ? R._("This event file's patched ranges cannot be traced for in-place uninstall ({0} block(s)). Uninstall it via the WinForms patch manager, or revert with a backup ROM.",
                        trace.Untraceable.Count.ToString())
                    : R._("Could not trace any patched ranges from this event file. It may not be an applied EA patch, or it uses constructs this in-place uninstall does not support.");
                return result;
            }

            // The clean ROM must cover the highest traced offset, otherwise we cannot
            // restore those bytes — reject up front (mirrors the WF size sanity check).
            uint highest = 0;
            foreach (BinMapping map in binmap)
            {
                uint end = map.addr + (map.length == 0 ? 1u : map.length);
                if (end > highest) highest = end;
            }
            if (highest > cleanOriginalRom.Length)
            {
                result.ErrorMessage = R._("The selected clean ROM is too small ({0} bytes) for this patch (needs at least {1} bytes). Did you pick the wrong file?",
                    cleanOriginalRom.Length.ToString(), highest.ToString());
                return result;
            }

            string error = UninstallPatchInner(binmap, cleanOriginalRom, undo, result);
            if (error != "")
            {
                result.ErrorMessage = error;
                return result;
            }

            // Reverted the ranges we COULD trace. Success is true, but FullyTraced may be
            // false (residue remains) — the View MUST warn the user in that case.
            result.Success = true;
            result.RangeCount = binmap.Count;
            return result;
        }

        // Faithful port of PatchForm.UninstallPatchInner (EA path): overwrite each
        // traced byte with the clean-original byte, under undo; auto-length unknown
        // ranges; then strip a zero-filled extended ROM tail.
        static string UninstallPatchInner(List<BinMapping> binmap, byte[] orignalROM, Undo.UndoData undodata, UninstallResult result)
        {
            ROM rom = CoreState.ROM;
            uint current_rom_length = (uint)rom.Data.Length;
            uint reverted = 0;

            for (int n = 0; n < binmap.Count; n++)
            {
                BinMapping map = binmap[n];

                if (map.length == 0)
                {//サイズがわからないので、自動的に求めます.
                    map.length = CalcAutoLength(map.addr, orignalROM);
                    map.bin = rom.getBinaryData(map.addr, map.length);
                }

                for (int i = 0; i < map.length; i++)
                {
                    uint addr = map.addr + (uint)i;
                    CoreState.CommentCache?.Remove(addr);
                    if (addr >= current_rom_length)
                    {
                        continue;
                    }

                    uint o = AtByte(orignalROM, addr); //パッチを含んでいないROMの内容
                    rom.write_u8(addr, o, undodata);
                    reverted++;
                }
                // (The WinForms MENU key fixup via MenuCommandForm is BIN-patch only and
                // does not arise for an instant-EA patch — omitted, see class doc.)
            }

            StripROM(binmap, undodata);

            result.BytesReverted = reverted;
            return "";
        }

        // Port of PatchForm.StripROM: if reverting leaves the extended ROM tail all
        // zero, shrink the ROM back down (recorded in undo as a resize position).
        static void StripROM(List<BinMapping> binmap, Undo.UndoData undodata)
        {
            ROM rom = CoreState.ROM;
            uint extendsAddr = U.toOffset(rom.RomInfo.extends_address);
            int length = rom.Data.Length;

            //終端が0x00で埋まるならROMサイズを小さくする.
            uint stripSize = U.NOT_FOUND;
            for (int n = 0; n < binmap.Count; n++)
            {
                BinMapping map = binmap[n];
                uint addr = map.addr;
                if (addr < extendsAddr)
                {//拡張領域ではないのでstripできない.
                    continue;
                }

                for (int i = (int)addr; i < length; i++, addr++)
                {
                    if (rom.Data[i] != 0x00)
                    {
                        break;
                    }
                }
                if (addr == length)
                {
                    if (stripSize > map.addr)
                    {
                        stripSize = map.addr;
                    }
                }
            }
            if (rom.Data.Length > stripSize
                && stripSize >= extendsAddr)
            {
                undodata.list.Add(new Undo.UndoPostion(stripSize, (uint)rom.Data.Length - stripSize));
                rom.write_resize_data(stripSize);
            }
        }

        // Port of PatchForm.CalcAutoLength: how far the live ROM diverges from the
        // clean ROM starting at addr, tolerating up to RecoverMissMatch matching bytes.
        static uint CalcAutoLength(uint addr, byte[] other, int RecoverMissMatch = 10)
        {
            ROM rom = CoreState.ROM;
            int length = Math.Min(rom.Data.Length, other.Length);
            int i;
            for (i = (int)addr; i < length; i++)
            {
                if (rom.Data[i] != other[i])
                {
                    continue;
                }

                i++;
                int missCount = 0;
                for (; i < length; i++)
                {
                    if (rom.Data[i] != other[i])
                    {
                        i -= missCount;
                        break;
                    }

                    if (missCount >= RecoverMissMatch)
                    {
                        i -= missCount;

                        return ((uint)i) - addr;
                    }

                    missCount++;
                }
            }

            return ((uint)i) - addr;
        }

        // ---- Trace helpers (ports of the PatchForm private statics) ---------------

        // Port of PatchForm.MakeMaskAddress: mask the LDR-pointer bytes inside a code
        // block so the address-dependent words don't break a GREP pattern match.
        static bool[] MakeMaskAddress(byte[] original, uint base_address)
        {
            bool[] isSkip = new bool[original.Length];

            base_address = U.toOffset(base_address);
            if (!U.isSafetyOffset(base_address))
            {
                return isSkip;
            }

            List<DisassemblerTrumb.LDRPointer> ldr = DisassemblerTrumb.MakeLDRMap(original, 0);

            uint base_pointer = U.toPointer(base_address);
            for (int i = 0; i < ldr.Count; i++)
            {
                if (ldr[i].ldr_data >= base_pointer
                    && ldr[i].ldr_data <= base_pointer + original.Length)
                {
                    isSkip[ldr[i].ldr_data_address + 0] = true;
                    isSkip[ldr[i].ldr_data_address + 1] = true;
                    isSkip[ldr[i].ldr_data_address + 2] = true;
                    isSkip[ldr[i].ldr_data_address + 3] = true;
                }
            }
            return isSkip;
        }

        // Port of PatchForm.ReadMod(string[], byte[], out bool[]) — the instant-EA path
        // has no per-key address-move args, so chaddr is 0 and the mask is built off
        // base 0 (matching ReadMod with an empty sp[]).
        static byte[] ReadMod(byte[] b, out bool[] isSkip)
        {
            uint chaddr = 0; // U.atoi0x(U.at(sp, 2)) with an empty sp == 0
            isSkip = MakeMaskAddress(b, chaddr);
            return b;
        }

        //lynによってインポートされるelfのマスクパターンを作ります。
        // Port of PatchForm.MakeLynMaskPattern.
        static bool[] MakeLynMaskPattern(byte[] bin)
        {
            bool[] isSkip = new bool[bin.Length];
            for (int i = 0; i + 3 < bin.Length; i += 4)
            {
                if (bin[i] == 0 && bin[i + 1] == 0 && bin[i + 2] == 0 && bin[i + 3] == 0)
                {
                    isSkip[i + 0] = true;
                    isSkip[i + 1] = true;
                    isSkip[i + 2] = true;
                    isSkip[i + 3] = true;
                }
            }
            return isSkip;
        }

        // Port of PatchForm.MakeFullMask.
        static bool[] MakeFullMask(uint length)
        {
            bool[] r = new bool[length];
            for (int i = 0; i < length; i++)
            {
                r[i] = true;
            }
            return r;
        }

        // Port of PatchForm.EraseORG: a later concrete mapping at the same address
        // supersedes a provisional ORG mapping there.
        static void EraseORG(List<BinMapping> binMappings, BinMapping b)
        {
            for (int i = 0; i < binMappings.Count; i++)
            {
                BinMapping a = binMappings[i];
                if (a.addr != b.addr)
                {
                    continue;
                }
                if (a.key == "ORG")
                {
                    binMappings.RemoveAt(i);
                    i--;
                    continue;
                }
            }
        }

        // Safe byte read from a byte[] (port of U.at(byte[], addr) — 0 when OOB).
        static uint AtByte(byte[] data, uint addr)
        {
            if (data == null || addr >= data.Length)
            {
                return 0;
            }
            return data[addr];
        }

        // The display name for an untraceable block: the file/label name, or a
        // localized "(inline)" placeholder for an unnamed (inline) block.
        static string BlockName(string name)
        {
            return string.IsNullOrEmpty(name) ? R._("(inline)") : name;
        }
    }
}
