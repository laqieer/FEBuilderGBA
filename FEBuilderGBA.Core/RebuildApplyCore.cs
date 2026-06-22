using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform ROM-rebuild <c>Apply</c> (slice 1 of #1261): reads a <c>.rebuild</c>
    /// manifest plus its sidecar files and reconstructs a defragmented ROM from a vanilla
    /// base. Ported nearly verbatim from the WinForms <c>ToolROMRebuildApply</c>; the two
    /// WinForms touches are replaced with injected parameters:
    /// <list type="bullet">
    ///   <item><c>Program.ROM.RomInfo.extends_address</c> -&gt; the <c>extendsAddress</c> parameter.</item>
    ///   <item><c>MoveToFreeSapceForm.IsSkillReserve</c> -&gt; the optional <c>isReserved</c> callback.</item>
    /// </list>
    /// <para>
    /// Slice 1 uses <b>append-only allocation</b>: relocated data always grows the ROM tail.
    /// The free-area recycler (<c>ToolROMRebuildFreeArea</c>) is a later slice, so the
    /// <c>useFreeArea</c> / free-area-min-size parameters of the WinForms shell are omitted
    /// here. The flat 32MB reserve buffer + <c>ApplyVanillaROM</c> truncate are preserved so
    /// the eventual Avalonia background-thread writer can keep the same fault-safe shape
    /// (explicit undo + resize-before-write).
    /// </para>
    /// </summary>
    public static class RebuildApplyCore
    {
        //LZ77のID部の位置.
        const int LZ77UNIQ_SHIFT = 20;

        enum PointerType
        {
            NONE
            , ASM           //+0x1
            , ANTI_HUFFMAN  //+0x80 00 00 00
        }

        //戦闘アニメのフレームはポインタがあるのに可変長のLZ77なのでそれを何とかして解決するために利用する.
        //基本的に、無圧縮で格納して、最後にROM末尾に配置する.
        sealed class LZ77Struct
        {
            public byte[] Bin;
            public uint OrignalAddr;
            public uint OrignalDataSize;
            public string DebugInfo;

            public LZ77Struct(byte[] bin, uint orignalAddr, uint orignalDataSize, string debugInfo)
            {
                this.Bin = bin;
                this.OrignalAddr = orignalAddr;
                this.OrignalDataSize = orignalDataSize;
                this.DebugInfo = debugInfo;
            }
        }

        //まだ発見していない不明なポインタ
        sealed class MissingPointer
        {
            public uint FindPointer; //探しているポインタ
            public uint WroteAddr;   //このデータがある位置
            public PointerType Type;
            public bool IsLZ77;      //LZ77の仮想アドレス
            public string DebugTargetfilename;

            public MissingPointer(uint findPointer, uint wroteAddr, PointerType type, bool isLZ77, string debugTargetfilename)
            {
                this.FindPointer = findPointer;
                this.WroteAddr = wroteAddr;
                this.Type = type;
                this.IsLZ77 = isLZ77;
                this.DebugTargetfilename = debugTargetfilename;
            }
        }

        /// <summary>Result of an <see cref="Apply(ROM,string,uint,Func{uint,bool},IProgress{string})"/> run.</summary>
        public sealed class ApplyResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            /// <summary>The rebuilt ROM bytes (truncated to <c>WriteOffset</c>).</summary>
            public byte[] Rebuilt { get; set; }
            /// <summary>The self-check log (mirrors the WinForms <c>.log.txt</c> contents).</summary>
            public string Log { get; set; }
        }

        /// <summary>
        /// Apply a <c>.rebuild</c> manifest to a vanilla ROM and return the rebuilt bytes.
        /// </summary>
        /// <param name="vanilla">The unmodified base ROM (defines the non-rebuild region).</param>
        /// <param name="manifestPath">Path to the <c>.rebuild</c> manifest; sidecar files are resolved relative to its directory.</param>
        /// <param name="extendsAddress">GBA pointer of the extends ROM area (was <c>Program.ROM.RomInfo.extends_address</c>); reserved for later free-area slices.</param>
        /// <param name="isReserved">Optional: returns true (and may advance the ref addr) when an offset falls in a reserved/SkillSystems region during share-search. Default = never reserved.</param>
        /// <param name="progress">Optional progress reporter.</param>
        public static ApplyResult Apply(ROM vanilla, string manifestPath, uint extendsAddress,
            Func<uint, bool> isReserved = null, IProgress<string> progress = null)
        {
            if (vanilla == null || vanilla.Data == null)
            {
                return new ApplyResult { Success = false, Message = "Error: vanilla ROM is null." };
            }
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                return new ApplyResult { Success = false, Message = "Error: manifest not found: " + manifestPath };
            }

            var session = new Session(vanilla, isReserved, progress);
            return session.Run(manifestPath);
        }

        /// <summary>
        /// Per-run mutable state. Kept as an instance class (mirroring the WinForms field
        /// layout) so the ported method bodies stay byte-for-byte recognizable, while the
        /// public surface stays a pure static <see cref="Apply"/>.
        /// </summary>
        sealed class Session
        {
            //書き込んでいるデータ. 当初は無改造ROMからスタートしデータをどんどん書き込んでいきます.
            //ただし、リサイズによる無駄を避けるために、32MBを確保します.
            readonly byte[] WriteROMData32MB;
            //現在書き込んでいるROMサイズを指します.
            uint WriteOffset;

            //どのアドレスをどこに再マップしたかのテーブル
            readonly Dictionary<uint, uint> AddressMap = new Dictionary<uint, uint>();
            readonly List<MissingPointer> MissingPointerList = new List<MissingPointer>();
            readonly List<LZ77Struct> LZ77StructList = new List<LZ77Struct>();
            readonly StringBuilder ApplyLog = new StringBuilder();

            uint RebuildAddress;
            uint UseShareSameData = 0;

            readonly ROM Vanilla;
            readonly Func<uint, bool> IsReserved;
            readonly IProgress<string> Progress;

            public Session(ROM vanilla, Func<uint, bool> isReserved, IProgress<string> progress)
            {
                this.Vanilla = vanilla;
                this.IsReserved = isReserved;
                this.Progress = progress;
                this.WriteROMData32MB = new byte[32 * 1024 * 1024]; //32MB memory reserve
            }

            //ROMリサイズ
            void write_resize_data(uint addr)
            {
                this.WriteOffset = U.Padding4(addr);
            }

            bool isVanilaExtrendsROMArea(uint addr)
            {
                return addr > this.RebuildAddress;
            }

            // ---- base-region pointer resolution (#1344 — the WF @DEF/ResolvUnkLength Make
            // phase, ported to the Apply consumer) -----------------------------------------
            //
            // A pointer TOKEN inside a relocated MIX/IFR/LZ77 block can target an address that
            // lies in the NON-rebuild base region [0, RebuildAddress). That base region is
            // NEVER relocated — Apply seeds the output from the vanilla base and only moves
            // data at/after RebuildAddress (Alloc appends to the tail). So such a target stays
            // at its ORIGINAL address: it resolves to itself (identity), it is NOT Missing!.
            //
            // The WinForms ToolROMRebuildMake emits a `@DEF <addr>` manifest entry for every
            // such base-region struct (ToolROMRebuildMake.cs:291/1014 + the AppendPointer/
            // AppendLDR/ProcssPointer discovery phases), which ToolROMRebuildApply.DEF then
            // identity-maps (ResolvedPointer(labelPointer, labelPointer)). Those producer-side
            // discovery phases are deeply WinForms-coupled (MoveToFreeSapceForm.SearchPointer,
            // AsmMapFileAsmCache, the BL/LDR re-scan) and are NOT ported here. Instead the Core
            // pipeline reproduces the SAME end result on the Apply side: the base region is
            // identity by construction, so any unresolved token whose target is in
            // [0, RebuildAddress) is registered as identity — byte-for-byte what a `@DEF` for
            // that target would have produced. This is exactly the rule WF's own
            // BrokenData(addr) uses for non-extends addresses: ResolvedPointer(labelPointer,
            // labelPointer) (ToolROMRebuildApply.cs:524). The high-address Extends/Relocate
            // cases are unaffected (their forward-refs all land in the rebuild region, which is
            // resolved the normal way via the relocated entries' ResolvedPointer calls).
            //
            // isBaseRegionPointer takes a GBA pointer (the token value) and reports whether its
            // ROM offset is strictly below RebuildAddress — i.e. it is never relocated.
            bool isBaseRegionPointer(uint gbaPointer)
            {
                uint offset = U.toOffset(gbaPointer);
                return offset < this.RebuildAddress;
            }

            //append-only allocation (slice 1 — no free-area recycler).
            uint Alloc(uint size, uint current_addr)
            {
                uint writeaddr = this.WriteOffset;
                writeaddr = U.Padding4(writeaddr);
                write_resize_data(writeaddr + size);
                return writeaddr;
            }

            public ApplyResult Run(string filename)
            {
                this.AddressMap.Clear();
                this.MissingPointerList.Clear();
                this.LZ77StructList.Clear();
                U.write_range(this.WriteROMData32MB, 0, this.Vanilla.Data);
                this.WriteOffset = (uint)this.Vanilla.Data.Length;
                // UseShareSameData defaults to 0 (no share) for slice 1 — the WinForms
                // useShareSameData option is a UI toggle; the share-search code is kept
                // (and exercised when callers raise it) but the public API leaves it off.
                this.UseShareSameData = 0;

                //ディレクトリ成分が無いマニフェスト名 ("foo.rebuild") では GetDirectoryName が
                //null を返すので、カレントディレクトリ ("") として扱う (Path.Combine が投げない).
                string dir = Path.GetDirectoryName(filename) ?? "";

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(filename);
                }
                catch (Exception ex)
                {
                    return new ApplyResult { Success = false, Message = "Error: cannot read manifest. " + ex.Message };
                }

                this.RebuildAddress = GetRebuildAddress(lines);
                if (this.RebuildAddress > this.WriteOffset)
                {
                    write_resize_data(this.RebuildAddress);
                    this.RebuildAddress = this.WriteOffset;
                }

                int nextDoEvents = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string srcline = line;
                    line = U.ClipComment(line);
                    string[] sp = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (sp.Length < 2)
                    {
                        continue;
                    }
                    if (sp[0] == "@_CRC32"
                        || sp[0] == "@_REBUILDADDRESS"
                        || sp[0] == "@_ASSERT")
                    {
                        continue;
                    }

                    uint vanilla_addr;
                    uint blocksize;
                    uint count;
                    string targetfilename;
                    uint addr = ParseLine(sp, out vanilla_addr, out targetfilename, out blocksize, out count);

                    if (sp[0] == "@DEF")
                    {
                        DEF(addr, srcline);
                        continue;
                    }
                    else if (sp[0] == "@BROKENDATA")
                    {
                        BrokenData(addr, srcline);
                        continue;
                    }
                    else if (sp[0] == "@00")
                    {
                        Fixed00(addr, blocksize, srcline);
                        continue;
                    }

                    if (i > nextDoEvents)
                    {
                        this.Progress?.Report(i + ":" + line);
                        nextDoEvents = i + 0xff;
                    }

                    targetfilename = Path.Combine(dir, targetfilename);
                    if (!File.Exists(targetfilename))
                    {
                        return new ApplyResult
                        {
                            Success = false,
                            Message = "Error: missing sidecar file (" + targetfilename + ") at line " + (i + 1)
                        };
                    }

                    if (sp[0] == "@IFR")
                    {
                        IFR(addr, vanilla_addr, targetfilename, blocksize, count, srcline);
                    }
                    else if (sp[0] == "@BIN")
                    {
                        Bin(addr, targetfilename, srcline);
                    }
                    else if (sp[0] == "@MIX")
                    {
                        Mix(addr, targetfilename, srcline);
                    }
                    else if (sp[0] == "@MIXLZ77")
                    {
                        MixLZ77(addr, targetfilename, blocksize, srcline);
                    }
                    else
                    {
                        continue;
                    }
                }

                this.Progress?.Report("WriteBackMixLZ77");
                //後回しにしていたLZ77の解決.
                WriteBackMixLZ77();

                this.Progress?.Report("CheckSelf");
                string log;
                bool ok = CheckSelf(out log);

                this.Progress?.Report("ApplyVanillaROM");
                byte[] rebuilt = BuildRebuilt();

                return new ApplyResult
                {
                    Success = ok,
                    Message = ok ? "Rebuild applied." : "Rebuild applied with unresolved pointers (see Log).",
                    Rebuilt = rebuilt,
                    Log = log,
                };
            }

            //WinFormsの ApplyVanillaROM 相当. flat 32MB buffer を WriteOffset で切り詰める.
            byte[] BuildRebuilt()
            {
                return U.getBinaryData(this.WriteROMData32MB, 0, this.WriteOffset);
            }

            bool CheckSelf(out string log)
            {
                StringBuilder sb = new StringBuilder();
                ShowMissingPointers(sb);

                bool isOK = true;
                if (sb.Length > 0)
                {
                    sb.Insert(0, "== ERROR! ==\r\n");
                    isOK = false;
                }
                sb.AppendLine("== MAPPING ==");
                sb.Append(this.ApplyLog);

                log = sb.ToString();
                return isOK;
            }

            void ShowMissingPointers(StringBuilder sb)
            {
                for (int i = 0; i < this.MissingPointerList.Count; i++)
                {
                    MissingPointer m = this.MissingPointerList[i];
                    if (m.WroteAddr < this.RebuildAddress)
                    {
                        continue;
                    }
                    sb.Append("Missing!");
                    sb.Append(" P:");
                    sb.Append(U.To0xHexString(m.FindPointer));
                    sb.Append(" Pos:");
                    sb.Append(U.To0xHexString(m.WroteAddr));
                    sb.Append(" ");
                    sb.Append(m.Type);
                    sb.Append(m.IsLZ77 ? " (LZ77)" : "");
                    sb.Append(" //");
                    sb.Append(m.DebugTargetfilename);
                    sb.AppendLine();
                }
            }

            //ポインタが解決された
            void ResolvedPointer(uint labelPointer, uint addrPointer, string debugInfo)
            {
                this.AddressMap[labelPointer] = addrPointer;
                WriteApplyLog(labelPointer, addrPointer, debugInfo);

                //不明なポインタが解決された場合、書き戻す.
                for (int i = 0; i < this.MissingPointerList.Count; i++)
                {
                    MissingPointer m = this.MissingPointerList[i];

                    if (m.FindPointer == labelPointer)
                    {
                        uint p = addrPointer;
                        if (m.Type == PointerType.ANTI_HUFFMAN)
                        {
                            p += 0x80000000;
                        }

                        if (m.IsLZ77)
                        {
                            uint lz77uniq = (uint)(m.WroteAddr >> LZ77UNIQ_SHIFT);
                            uint pos = (uint)(m.WroteAddr & ((1 << LZ77UNIQ_SHIFT) - 1));
                            LZ77Struct lz77struct = this.LZ77StructList[(int)lz77uniq];
                            U.write_u32(lz77struct.Bin, pos, p);
                        }
                        else
                        {
                            U.write_u32(this.WriteROMData32MB, m.WroteAddr, p);
                        }
                        //解決されたのでリストから消す.
                        this.MissingPointerList.RemoveAt(i);
                        i--;
                    }
                    else if (m.Type == PointerType.ASM)
                    {
                        uint l;
                        uint p;
                        if (U.IsValueOdd(m.FindPointer))
                        {
                            if (U.IsValueOdd(labelPointer))
                            {
                                continue;
                            }
                            l = labelPointer + 1;

                            if (U.IsValueOdd(labelPointer))
                            {
                                p = addrPointer;
                            }
                            else
                            {
                                p = addrPointer + 1;
                            }
                        }
                        else
                        {
                            if (!U.IsValueOdd(labelPointer))
                            {
                                continue;
                            }
                            l = labelPointer - 1;

                            if (U.IsValueOdd(labelPointer))
                            {
                                p = addrPointer - 1;
                            }
                            else
                            {
                                p = addrPointer;
                            }
                        }

                        if (m.FindPointer != l)
                        {
                            continue;
                        }

                        if (m.IsLZ77)
                        {
                            uint lz77uniq = (uint)(m.WroteAddr >> LZ77UNIQ_SHIFT);
                            uint pos = (uint)(m.WroteAddr & ((1 << LZ77UNIQ_SHIFT) - 1));
                            LZ77Struct lz77struct = this.LZ77StructList[(int)lz77uniq];
                            U.write_u32(lz77struct.Bin, pos, p);
                        }
                        else
                        {
                            U.write_u32(this.WriteROMData32MB, m.WroteAddr, p);
                        }
                        //解決されたのでリストから消す.
                        this.MissingPointerList.RemoveAt(i);
                        i--;
                    }
                }
            }

            void DEF(uint addr, string debugInfo)
            {
                uint labelPointer = U.toPointer(addr);
                ResolvedPointer(labelPointer, labelPointer, debugInfo);
            }

            void BrokenData(uint addr, string debugInfo)
            {
                if (!isVanilaExtrendsROMArea(addr))
                {//非拡張データなので無視する.
                    uint labelPointer = U.toPointer(addr);
                    ResolvedPointer(labelPointer, labelPointer, debugInfo);
                    return;
                }

                //壊れている魔法アニメなどの画像データ. とりあえず4バイト null を入れる.
                Bin(addr, new byte[] { 0, 0, 0, 0 }, debugInfo);
            }

            void Fixed00(uint addr, uint length, string debugInfo)
            {
                U.write_fill(this.WriteROMData32MB, addr, length, 0);
            }

            void WriteApplyLog(uint labelPointer, uint addrPointer, string debugInfo)
            {
                if (labelPointer != addrPointer)
                {
                    ApplyLog.Append(U.ToHexString8(addrPointer));
                    ApplyLog.Append(" <=re= ");
                }
                ApplyLog.Append(U.ToHexString8(labelPointer));
                ApplyLog.Append(' ');
                ApplyLog.Append(debugInfo);
                ApplyLog.AppendLine();
            }

            uint SearchShareArea(byte[] bin)
            {
                //実はこのデータが既にROMにあったりしますか?
                uint foundAddr = U.Grep(this.WriteROMData32MB, bin, 0x100, this.WriteOffset, 4);
                if (foundAddr == U.NOT_FOUND)
                {
                    return U.NOT_FOUND;
                }

                //SkillSystemsなどの予約領域にヒットした場合は、その先から再検索する.
                //U.Grep の start は inclusive なので、必ず foundAddr+4 から再開しないと
                //同じ予約済みヒットを永久に返してしまう (4 = ARMアライメント).
                while (this.IsReserved != null && this.IsReserved(foundAddr))
                {
                    uint nextStart = foundAddr + 4;
                    if (nextStart >= this.WriteOffset)
                    {
                        return U.NOT_FOUND;
                    }
                    foundAddr = U.Grep(this.WriteROMData32MB, bin, nextStart, this.WriteOffset, 4);
                    if (foundAddr == U.NOT_FOUND)
                    {
                        return U.NOT_FOUND;
                    }
                }
                //既にROMにあるので共有させましょう
                return foundAddr;
            }

            void Bin(uint addr, string targetfilename, string debugInfo)
            {
                byte[] bin = File.ReadAllBytes(targetfilename);
                Bin(addr, bin, debugInfo);
            }
            void Bin(uint addr, byte[] bin, string debugInfo)
            {
                uint writeaddr;
                if (!isVanilaExtrendsROMArea(addr + (uint)bin.Length))
                {//非拡張領域
                    writeaddr = addr;
                }
                else
                {
                    if (IsShareData(bin.Length))
                    {
                        //実はこのデータが既にROMにあったりしますか?
                        uint foundAddr = SearchShareArea(bin);
                        if (foundAddr != U.NOT_FOUND)
                        {//既にROMにあるので共有させましょう
                            writeaddr = foundAddr;
                            ResolvedPointer(U.toPointer(addr), U.toPointer(writeaddr), debugInfo + "//SHARE!");
                            return;
                        }
                    }
                    //リポイントが必須
                    writeaddr = Alloc((uint)bin.Length, addr);
                }

                U.write_range(this.WriteROMData32MB, writeaddr, bin);
                ResolvedPointer(U.toPointer(addr), U.toPointer(writeaddr), debugInfo);
            }

            bool IsShareData(int length)
            {
                if (this.UseShareSameData == 1)
                {//32以下なら共有しない
                    if (length <= 32)
                    {
                        return false;
                    }
                    return true;
                }
                if (this.UseShareSameData == 2)
                {//8以下なら共有しない
                    if (length <= 8)
                    {
                        return false;
                    }
                    return true;
                }
                //共有しない
                return false;
            }

            List<byte> WriteBytes(int startIndex //spの中で書き込みを開始するデータ位置 1 or 0
                , string[] sp                    //ファイルから読みこんでパースしたデータ
                , uint writeTopBaseAddr          //トップデータの位置 (lz77の場合 0)
                , uint currentBaseAddr           //書き込む予定の位置 (lz77の場合 ユニークな値)
                , string debugInfo               //デバッグ用
            )
            {
                bool isLZ77 = (writeTopBaseAddr == 0);

                List<byte> bin = new List<byte>();
                for (int i = startIndex; i < sp.Length; i++)
                {
                    string s = sp[i];
                    if (s.Length <= 0)
                    {
                    }
                    else if (s[0] == '@')
                    {//ポインタ
                        uint p = U.toPointer(U.atoh(s.Substring(1)));

                        uint new_pointer;
                        if (this.AddressMap.TryGetValue(p, out new_pointer))
                        {
                            U.append_u32(bin, new_pointer);
                        }
                        else if (isBaseRegionPointer(p))
                        {//#1344 base-region target: never relocated -> identity (= a WF @DEF).
                            ResolvedPointer(p, p, debugInfo + " //BASE_DEF");
                            U.append_u32(bin, p);
                        }
                        else
                        {//アドレスのデータがないよ! 不明なポインタとしてリストに登録. 後で判明したら書き戻す
                            this.MissingPointerList.Add(new MissingPointer(p
                                , currentBaseAddr + (uint)bin.Count
                                , PointerType.NONE, isLZ77, debugInfo));
                            //とりあえずそのポインタの値で書き込む.
                            U.append_u32(bin, p);
                        }
                    }
                    else if (s[0] == '`')
                    {//anti-huffmanのポインタ
                        uint p = U.toPointer(U.atoh(s.Substring(1)));
                        uint new_pointer;
                        if (this.AddressMap.TryGetValue(p, out new_pointer))
                        {
                            U.append_u32(bin, new_pointer + 0x80000000);
                        }
                        else if (isBaseRegionPointer(p))
                        {//#1344 base-region target: never relocated -> identity (= a WF @DEF).
                            ResolvedPointer(p, p, debugInfo + " //BASE_DEF");
                            U.append_u32(bin, p + 0x80000000);
                        }
                        else
                        {//アドレスのデータがないよ! 不明なポインタとしてリストに登録. 後で判明したら書き戻す
                            this.MissingPointerList.Add(new MissingPointer(p
                                , currentBaseAddr + (uint)bin.Count
                                , PointerType.ANTI_HUFFMAN, isLZ77, debugInfo));
                            U.append_u32(bin, p + 0x80000000);
                        }
                    }
                    else if (s[0] == '&')
                    {//ASM
                        uint p = U.toPointer(U.atoh(s.Substring(1)));

                        uint new_pointer;
                        if (this.AddressMap.TryGetValue(p, out new_pointer))
                        {
                            U.append_u32(bin, new_pointer);
                        }
                        else if (
                            U.IsValueOdd(p)
                            && this.AddressMap.TryGetValue(p - 1, out new_pointer))
                        {
                            U.append_u32(bin, new_pointer + 1);
                        }
                        else if (isBaseRegionPointer(p))
                        {//#1344 base-region ASM target: never relocated -> identity (= a WF @DEF).
                            //p already carries its thumb (odd) bit; the identity value is p itself.
                            ResolvedPointer(p, p, debugInfo + " //BASE_DEF");
                            U.append_u32(bin, p);
                        }
                        else
                        {//アドレスのデータがないよ! 不明なポインタとしてリストに登録. 後で判明したら書き戻す
                            this.MissingPointerList.Add(new MissingPointer(p
                                , currentBaseAddr + (uint)bin.Count
                                , PointerType.ASM, isLZ77, debugInfo));
                            U.append_u32(bin, p);
                        }
                    }
                    else if (s[0] == '+')
                    {//自己参照
                        uint plus = U.atoh(s.Substring(1));
                        uint new_pointer = U.toPointer(writeTopBaseAddr + plus);
                        U.append_u32(bin, new_pointer);
                    }
                    else
                    {
                        U.append_u8(bin, U.atoh(s));
                    }
                }
                return bin;
            }

            void Mix(uint addr, string targetfilename, string debugInfo)
            {
                string lines = File.ReadAllText(targetfilename);
                string[] sp = lines.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                //まずサイズを求めないといけない.
                uint datasize = CalcWriteDataSize(sp);

                //領域の確保
                uint writeaddr;
                if (!isVanilaExtrendsROMArea(addr + datasize))
                {//非拡張領域. そのまま書き換えられる
                    writeaddr = addr;
                }
                else
                {//リポイントが必要
                    writeaddr = Alloc(datasize, addr);
                }
                List<byte> bin = WriteBytes(0, sp, writeaddr, writeaddr, debugInfo);

                //データの書き込み
                U.write_range(this.WriteROMData32MB, writeaddr, bin.ToArray());
                ResolvedPointer(U.toPointer(addr), U.toPointer(writeaddr), debugInfo);
            }

            uint CalcWriteDataSize(string[] sp)
            {
                uint datasize = 0;
                for (int i = 0; i < sp.Length; i++)
                {
                    if (sp[i][0] == '@' //ポインタ
                     || sp[i][0] == '&' //ASMコード ポインタ+1
                     || sp[i][0] == '+' //自己参照
                     || sp[i][0] == '`' //anti-huffman
                        )
                    {
                        datasize += 4;
                    }
                    else
                    {
                        datasize++;
                    }
                }
                return datasize;
            }

            void MixLZ77(uint addr, string targetfilename, uint datasize, string debugInfo)
            {
                string lines = File.ReadAllText(targetfilename);
                string[] sp = lines.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                int oldMissingSize = this.MissingPointerList.Count;
                uint lz77uniq = (uint)(this.LZ77StructList.Count << LZ77UNIQ_SHIFT);
                List<byte> bin = WriteBytes(0, sp, 0, lz77uniq, debugInfo);

                int newMissingSize = this.MissingPointerList.Count;
                if (oldMissingSize == newMissingSize)
                {//不明なポインタは存在しない. よって、書き込めるはず
                    WriteLZ77Bin(bin.ToArray(), addr, datasize, debugInfo);
                }
                else
                {//不明なポインタがあるので、末尾に回す
                    this.LZ77StructList.Add(new LZ77Struct(bin.ToArray(), addr, datasize, debugInfo));
                }
            }

            void WriteLZ77Bin(byte[] bin, uint addr, uint datasize, string debugInfo)
            {
                byte[] newbin = LZ77.compress(bin);

                //領域の確保
                uint writeaddr;
                if (!isVanilaExtrendsROMArea(addr + datasize)
                    && datasize >= newbin.Length)
                {//非拡張領域. そのまま書き換えられる
                    writeaddr = addr;
                }
                else
                {//リポイントが必要
                    writeaddr = Alloc((uint)newbin.Length, addr);
                }

                //データの書き込み
                U.write_range(this.WriteROMData32MB, writeaddr, newbin);
                ResolvedPointer(U.toPointer(addr), U.toPointer(writeaddr), debugInfo);
            }

            void WriteBackMixLZ77()
            {
                for (int i = 0; i < this.LZ77StructList.Count; i++)
                {
                    LZ77Struct lz77Struct = this.LZ77StructList[i];
                    WriteLZ77Bin(lz77Struct.Bin, lz77Struct.OrignalAddr, lz77Struct.OrignalDataSize, lz77Struct.DebugInfo);
                }
            }

            void IFR(uint addr, uint vanilla_addr, string targetfilename, uint blocksize, uint count, string debugInfo)
            {
                uint writeaddr;
                if (!isVanilaExtrendsROMArea(addr + blocksize * count))
                {//非拡張領域. そのまま書き換えられる
                    writeaddr = addr;
                }
                else
                {//リポイントが必要
                    writeaddr = Alloc(blocksize * count, addr);

                    //データのコピー
                    byte[] bin = U.getBinaryData(this.WriteROMData32MB, vanilla_addr, blocksize * count);
                    U.write_range(this.WriteROMData32MB, writeaddr, bin);
                }

                string[] lines = File.ReadAllLines(targetfilename);
                for (int n = 0; n < lines.Length; n++)
                {
                    string line = lines[n];
                    string[] sp = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (sp.Length < 1 || sp[0].Length < 1 || sp[0][0] != '=')
                    {
                        continue;
                    }
                    uint dataIndex = U.atoh(sp[0].Substring(1));

                    uint pos = writeaddr + (blocksize * dataIndex);
                    List<byte> bin = WriteBytes(1, sp, writeaddr, pos, debugInfo);

                    U.write_range(this.WriteROMData32MB, pos, bin.ToArray());
                }
                ResolvedPointer(U.toPointer(addr), U.toPointer(writeaddr), debugInfo);
            }
        }

        static uint ParseLine(string[] sp
           , out uint out_vanilla_addr
           , out string out_filename
           , out uint out_blocksize
           , out uint out_count)
        {
            out_vanilla_addr = 0;
            out_filename = "";
            out_blocksize = 0;
            out_count = 0;

            for (int i = 2; i < sp.Length; i++)
            {
                if (sp[i][0] == '=')
                {//無改造ROMのアドレス
                    out_vanilla_addr = U.atoh(sp[i].Substring(1));
                    out_vanilla_addr = U.toOffset(out_vanilla_addr);
                }
                else if (sp[i][0] == '*')
                {//個数
                    out_count = U.atoh(sp[i].Substring(1));
                }
                else if (sp[i][0] == ':')
                {//ブロックサイズ
                    out_blocksize = U.atoh(sp[i].Substring(1));
                }
                else
                {//不明なので多分ファイルだと思う.
                    out_filename = string.Join(" ", sp, i, sp.Length - i);
                    break;
                }
            }

            uint addr = U.atoh(sp[1]);
            return U.toOffset(addr);
        }

        public static uint GetCRC32(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string[] sp = lines[i].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length >= 1 && sp[0] == "@_CRC32" && sp.Length >= 2)
                {
                    return U.atoh(sp[1]);
                }
            }
            return U.NOT_FOUND;
        }
        public static uint GetRebuildAddress(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string[] sp = lines[i].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length >= 1 && sp[0] == "@_REBUILDADDRESS" && sp.Length >= 2)
                {
                    return U.atoh(sp[1]);
                }
            }
            return U.NOT_FOUND;
        }
    }
}
