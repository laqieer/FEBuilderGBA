using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform ROM-rebuild <c>Make</c> EMITTER (slice 1 of #1261): given the
    /// modified ROM bytes, a vanilla base ROM, a rebuild start address and a
    /// <b>caller-supplied</b> <see cref="Address"/> struct list, it writes a
    /// <c>.rebuild</c> manifest plus the per-struct sidecar files
    /// (<c>rebuild_ifr/</c>, <c>rebuild_mix/</c>, <c>rebuild_bin/</c>) that
    /// <see cref="RebuildApplyCore"/> consumes.
    /// <para>
    /// The keystone producer (<c>U.MakeAllStructPointersList</c> + ~120 WinForms Form
    /// statics) is NOT ported here — the struct list is a <b>parameter</b>, so the WinForms
    /// shell keeps producing it for now (a later slice ports the producer). Consequently the
    /// producer-only steps of the WinForms <c>ToolROMRebuildMake.Make</c> (LDR/BL re-scan,
    /// hardcoded-pointer search, OptimizeList, ProcssPointer) are intentionally omitted: the
    /// supplied list is taken as authoritative and emitted as-is.
    /// </para>
    /// <para>WinForms touches replaced by injected parameters/callbacks:</para>
    /// <list type="bullet">
    ///   <item><c>Program.ROM.Data</c> -&gt; the <c>modified</c> parameter.</item>
    ///   <item><c>this.Vanilla</c> -&gt; the <c>vanilla</c> parameter.</item>
    ///   <item><c>Program.AsmMapFileAsmCache.GetName</c> -&gt; the optional <c>nameResolver</c> callback (default returns "").</item>
    ///   <item><c>Program.ROM.RomInfo.version</c> -&gt; the <c>romVersion</c> parameter (default 8 = FE8U).</item>
    ///   <item><c>Program.AIScript</c>/<c>Program.ProcsScript</c>/<c>EventScriptWithPatchDic</c> -&gt; optional script dictionaries; when a struct needs one that was not supplied the generic <c>WildCard</c> emitter is used instead of crashing.</item>
    /// </list>
    /// </summary>
    public static class RebuildMakeCore
    {
        enum PointerType
        {
            NONE
            , POINTER
            , ASM
            , DATA
        }

        enum ASMC_Delect
        {
            NONE
            , AUTO
            , TEXTPOINTERS
            , ASM
        }

        sealed class RefCmd
        {
            //コマンドのデータ
            public string Cmd = "";
            //このコマンドが定義するポインタ値
            public Address UseAddress;
        }

        /// <summary>
        /// Emit a <c>.rebuild</c> manifest (and its sidecar files) for <paramref name="modified"/>.
        /// </summary>
        /// <param name="modified">The modified ROM bytes to defragment.</param>
        /// <param name="vanilla">The unmodified base ROM.</param>
        /// <param name="rebuildAddress">Offset at/after which data is rebuilt (everything below is left in place).</param>
        /// <param name="structList">The caller-supplied list of known data/pointer locations (the parameter boundary — was <c>U.MakeAllStructPointersList</c>).</param>
        /// <param name="manifestPath">Output path of the <c>.rebuild</c> file; sidecar folders are created beside it.</param>
        /// <param name="nameResolver">Optional symbol-name resolver (was <c>Program.AsmMapFileAsmCache.GetName</c>); default returns "".</param>
        /// <param name="romVersion">FE game version for EventCond layout (6/7/8); default 8.</param>
        /// <param name="eventScriptWithPatch">Optional EVENTSCRIPT dictionary (with patch). Null -&gt; generic emit.</param>
        /// <param name="eventScriptWithoutPatch">Optional EVENTSCRIPT dictionary (without patch). Null -&gt; generic emit.</param>
        /// <param name="aiScript">Optional AISCRIPT dictionary. Null -&gt; generic emit.</param>
        /// <param name="procsScript">Optional PROCS dictionary. Null -&gt; generic emit.</param>
        /// <param name="progress">Optional progress reporter.</param>
        public static void Make(byte[] modified, ROM vanilla, uint rebuildAddress, List<Address> structList,
            string manifestPath,
            Func<uint, string> nameResolver = null,
            int romVersion = 8,
            EventScript eventScriptWithPatch = null,
            EventScript eventScriptWithoutPatch = null,
            EventScript aiScript = null,
            EventScript procsScript = null,
            IProgress<string> progress = null)
        {
            if (modified == null) throw new ArgumentNullException(nameof(modified));
            if (vanilla == null) throw new ArgumentNullException(nameof(vanilla));
            if (structList == null) throw new ArgumentNullException(nameof(structList));
            if (string.IsNullOrEmpty(manifestPath)) throw new ArgumentNullException(nameof(manifestPath));

            var emitter = new Emitter(modified, vanilla, rebuildAddress, structList,
                nameResolver ?? (_ => ""), romVersion,
                eventScriptWithPatch, eventScriptWithoutPatch, aiScript, procsScript, progress);
            emitter.Run(manifestPath);
        }

        /// <summary>
        /// Validate that a generated rebuild projection is complete before its caller reports
        /// success. The manifest must be a readable regular file with both required headers, and
        /// every referenced sidecar must be a readable regular file beneath the manifest directory.
        /// </summary>
        public static void ValidateProjectionOutput(string manifestPath)
            => ValidateProjectionOutput(manifestPath, File.GetAttributes);

        internal static void ValidateProjectionOutput(
            string manifestPath,
            Func<string, FileAttributes> getAttributes)
        {
            if (string.IsNullOrEmpty(manifestPath))
                throw new ArgumentNullException(nameof(manifestPath));
            if (getAttributes == null)
                throw new ArgumentNullException(nameof(getAttributes));

            string fullManifestPath = Path.GetFullPath(manifestPath);
            string baseDir = Path.GetDirectoryName(fullManifestPath) ?? Directory.GetCurrentDirectory();
            string[] lines = ReadAllLinesFromRegularFile(
                fullManifestPath, "rebuild manifest", getAttributes);
            bool hasCrc32 = false;
            bool hasRebuildAddress = false;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = U.ClipComment(lines[lineIndex]);
                string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                if (tokens[0] == "@_CRC32")
                {
                    hasCrc32 |= tokens.Length >= 2;
                    continue;
                }
                if (tokens[0] == "@_REBUILDADDRESS")
                {
                    hasRebuildAddress |= tokens.Length >= 2;
                    continue;
                }

                if (tokens[0] != "@IFR"
                    && tokens[0] != "@BIN"
                    && tokens[0] != "@MIX"
                    && tokens[0] != "@MIXLZ77")
                {
                    continue;
                }

                int filenameIndex = 2;
                while (filenameIndex < tokens.Length
                    && tokens[filenameIndex].Length > 0
                    && (tokens[filenameIndex][0] == '='
                        || tokens[filenameIndex][0] == '*'
                        || tokens[filenameIndex][0] == ':'))
                {
                    filenameIndex++;
                }
                if (filenameIndex >= tokens.Length)
                {
                    throw new InvalidDataException(
                        "Rebuild projection has no sidecar path at line " + (lineIndex + 1) + ".");
                }

                string relativePath = string.Join(" ", tokens, filenameIndex, tokens.Length - filenameIndex);
                if (Path.IsPathRooted(relativePath))
                {
                    throw new InvalidDataException(
                        "Rebuild projection sidecar path must be relative at line " + (lineIndex + 1) + ".");
                }

                string sidecarPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));
                string relativeToBase = Path.GetRelativePath(baseDir, sidecarPath);
                if (Path.IsPathRooted(relativeToBase)
                    || relativeToBase == ".."
                    || relativeToBase.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || relativeToBase.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Rebuild projection sidecar escapes its scratch directory at line "
                        + (lineIndex + 1) + ".");
                }

                try
                {
                    ValidateNoReparsePointDirectories(
                        baseDir, sidecarPath, getAttributes, lineIndex + 1);
                }
                catch (FileNotFoundException)
                {
                    throw new InvalidDataException(
                        "Missing rebuild sidecar at line " + (lineIndex + 1) + ": " + sidecarPath);
                }
                catch (DirectoryNotFoundException)
                {
                    throw new InvalidDataException(
                        "Missing rebuild sidecar at line " + (lineIndex + 1) + ": " + sidecarPath);
                }
                ValidateReadableRegularFile(
                    sidecarPath,
                    "rebuild sidecar at line " + (lineIndex + 1),
                    getAttributes);
            }

            if (!hasCrc32 || !hasRebuildAddress)
            {
                throw new InvalidDataException(
                    "Rebuild projection manifest is incomplete: required @_CRC32 and "
                    + "@_REBUILDADDRESS headers were not both written.");
            }
        }

        static void ValidateNoReparsePointDirectories(
            string baseDir,
            string sidecarPath,
            Func<string, FileAttributes> getAttributes,
            int lineNumber)
        {
            string current = Path.GetDirectoryName(sidecarPath);
            while (!string.IsNullOrEmpty(current))
            {
                if ((getAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Rebuild projection sidecar has a symlink/junction directory at line "
                        + lineNumber + ".");
                }
                if (BuildfilePathSafety.PathsEqual(current, baseDir))
                    return;
                current = Path.GetDirectoryName(current);
            }

            throw new InvalidDataException(
                "Rebuild projection sidecar is not beneath its scratch directory at line "
                + lineNumber + ".");
        }

        static void ValidateReadableRegularFile(
            string path,
            string description,
            Func<string, FileAttributes> getAttributes)
        {
            ValidateRegularFileMetadata(path, description, getAttributes);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stream.CopyTo(Stream.Null);
            }
        }

        static string[] ReadAllLinesFromRegularFile(
            string path,
            string description,
            Func<string, FileAttributes> getAttributes)
        {
            ValidateRegularFileMetadata(path, description, getAttributes);
            return File.ReadAllLines(path);
        }

        static void ValidateRegularFileMetadata(
            string path,
            string description,
            Func<string, FileAttributes> getAttributes)
        {
            if (!File.Exists(path))
                throw new InvalidDataException("Missing " + description + ": " + path);

            FileAttributes attributes = getAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException(description + " is not a regular file: " + path);
            if (!ProjectionFileSystemSafety.TryValidateRegularFile(
                path, out string fileTypeError))
            {
                throw new InvalidDataException(
                    description + " is not a regular file: " + path
                    + " (" + fileTypeError + ").");
            }
        }

        /// <summary>
        /// Per-run emitter state. Instance class so the ported WinForms method bodies stay
        /// recognizable while the public surface stays a pure static <see cref="Make"/>.
        /// </summary>
        sealed class Emitter
        {
            readonly byte[] Modified;
            readonly ROM Vanilla;
            readonly uint RebuildAddress;
            readonly List<Address> StructList;
            readonly Func<uint, string> NameResolver;
            readonly int RomVersion;
            readonly EventScript EventScriptWithPatchDic;
            readonly EventScript EventScriptWithoutPatchDic;
            readonly EventScript AIScript;
            readonly EventScript ProcsScript;
            readonly IProgress<string> Progress;

            string BaseDir;
            Dictionary<uint, PointerType> PointerMark;

            public Emitter(byte[] modified, ROM vanilla, uint rebuildAddress, List<Address> structList,
                Func<uint, string> nameResolver, int romVersion,
                EventScript eswp, EventScript eswop, EventScript ai, EventScript procs, IProgress<string> progress)
            {
                this.Modified = modified;
                this.Vanilla = vanilla;
                this.RebuildAddress = rebuildAddress;
                this.StructList = structList;
                this.NameResolver = nameResolver;
                this.RomVersion = romVersion;
                this.EventScriptWithPatchDic = eswp;
                this.EventScriptWithoutPatchDic = eswop;
                this.AIScript = ai;
                this.ProcsScript = procs;
                this.Progress = progress;
            }

            bool isRebuildAddress(uint addr)
            {
                return addr >= this.RebuildAddress;
            }

            // ---- ROM compare helpers (read from Modified / Vanilla, not Program.ROM) ----
            bool romcmp(uint addr, uint vanilla_addr, uint blockSize)
            {
                if (isRebuildAddress(addr))
                {
                    return false;
                }
                byte[] s = U.getBinaryData(this.Modified, addr, blockSize);
                byte[] v = this.Vanilla.getBinaryData(vanilla_addr, blockSize);
                return (U.memcmp(s, v) == 0);
            }
            bool romcmpPointer(uint addr, uint vanilla_addr, uint blockSize)
            {
                if (isRebuildAddress(addr))
                {
                    return false;
                }
                if (!U.isSafetyOffset(addr) || !U.isSafetyOffset(vanilla_addr, this.Vanilla))
                {
                    return false;
                }

                uint sp = U.u32(this.Modified, addr);
                uint vp = this.Vanilla.p32(vanilla_addr);

                if (isRebuildAddress(U.toOffset(sp)))
                {
                    return false;
                }
                if (!U.isSafetyOffset(U.toOffset(sp)) || !U.isSafetyOffset(vp, this.Vanilla))
                {
                    return false;
                }

                byte[] s = U.getBinaryData(this.Modified, U.toOffset(sp), blockSize);
                byte[] v = this.Vanilla.getBinaryData(vp, blockSize);
                return (U.memcmp(s, v) == 0);
            }

            public void Run(string manifestPath)
            {
                //GetDirectoryName は manifestPath にディレクトリ成分が無い (例 "foo.rebuild")
                //場合 null を返し、続く Path.Combine が ArgumentNullException を投げる.
                //ディレクトリ無し = カレントディレクトリ ("") として扱う.
                this.BaseDir = Path.GetDirectoryName(manifestPath) ?? "";
                Directory.CreateDirectory(Path.Combine(this.BaseDir, "rebuild_ifr"));
                Directory.CreateDirectory(Path.Combine(this.BaseDir, "rebuild_mix"));
                Directory.CreateDirectory(Path.Combine(this.BaseDir, "rebuild_bin"));

                this.Progress?.Report("データを準備中...");
                //ポインタを高速に探索するためにルックアップテーブルを作成する.
                MakePointerMark();

                //処理済みアドレスマーク
                bool[] processedAddress = new bool[32 * 1024 * 1024];
                List<RefCmd> refCmdList = new List<RefCmd>(65535);

                int nextDoEvents = 0;
                for (int i = 0; i < StructList.Count; i++)
                {
                    Address a = StructList[i];

                    if (a.Addr >= processedAddress.Length || processedAddress[a.Addr])
                    {
                        continue;
                    }
                    processedAddress[a.Addr] = true;

                    if (i > nextDoEvents)
                    {
                        this.Progress?.Report(a.Info);
                        nextDoEvents = i + 0xff;
                    }

                    RefCmd refCmd;
                    if (a.BlockSize > 0)
                    {//ifrデータ
                        if (a.DataType == Address.DataTypeEnum.TEXTPOINTERS)
                        {//TEXT POINTERSは、Anti-Huffmanの可能性を考慮しないといけない.
                            refCmd = IFR(a, ASMC_Delect.TEXTPOINTERS);
                        }
                        else if (a.DataType == Address.DataTypeEnum.InputFormRef)
                        {//データポインタ
                            refCmd = IFR(a, ASMC_Delect.NONE);
                        }
                        else if (a.DataType == Address.DataTypeEnum.InputFormRef_ASM)
                        {//ASMポインタ
                            refCmd = IFR(a, ASMC_Delect.AUTO);
                        }
                        else
                        {//データとASMポインタの混在
                            refCmd = IFR(a, ASMC_Delect.AUTO);
                        }
                    }
                    else if (Address.IsPointerableType(a.DataType))
                    {//ポインタが存在するデータ
                        refCmd = Mix(a);
                    }
                    else
                    {//固定データ ポインタはこの中には発生しない
                        refCmd = BIN(a);
                    }

                    if (refCmd.Cmd == "")
                    {
                        Def(refCmd, a);
                    }

                    refCmd.UseAddress = a;
                    refCmdList.Add(refCmd);
                }

                this.Progress?.Report("未知のハックを探索中...");
                FindUnknownHack2(refCmdList, processedAddress);

                StringBuilder rebuildData = new StringBuilder();
                rebuildData.Append(RefSortSimple(refCmdList));

                File.WriteAllText(manifestPath, rebuildData.ToString());
            }

            void MakePointerMark()
            {
                this.PointerMark = new Dictionary<uint, PointerType>(65535);
                for (int i = 0; i < StructList.Count; i++)
                {
                    Address address = StructList[i];
                    if (address.Pointer == U.NOT_FOUND || address.Pointer == 0)
                    {
                    }
                    else
                    {
                        this.PointerMark[U.toPointer(address.Pointer)] = PointerType.POINTER;
                    }

                    if (Address.IsASMOnly(address.DataType))
                    {
                        this.PointerMark[U.toPointer(address.Addr)] = PointerType.ASM;
                    }
                    else
                    {
                        this.PointerMark[U.toPointer(address.Addr)] = PointerType.DATA;
                    }
                }
            }

            void Def(RefCmd refCmd, Address a)
            {
                refCmd.Cmd = "@DEF " + U.ToHexString8(a.Addr) + "\t//" + a.Info;
            }

            static int CompareRefCmd(RefCmd a, RefCmd b)
            {
                return (int)((int)a.UseAddress.Addr - (int)b.UseAddress.Addr);
            }

            string RefSortSimple(List<RefCmd> refCmdList)
            {
                this.Progress?.Report("Make List....");
                StringBuilder sb = new StringBuilder();
                U.CRC32 crc32 = new U.CRC32();
                sb.Append("@_CRC32 ");
                sb.Append(U.ToHexString(crc32.Calc(this.Vanilla.Data)));
                sb.AppendLine(" //vanilla ROM CRC32");

                sb.Append("@_REBUILDADDRESS ");
                sb.Append(U.ToHexString(this.RebuildAddress));
                sb.AppendLine(" //rebuild start address");

                refCmdList.Sort(CompareRefCmd);
                for (int i = 0; i < refCmdList.Count; i++)
                {
                    RefCmd refCmd = refCmdList[i];
                    sb.AppendLine(refCmd.Cmd);
                }

                this.Progress?.Report("Save File!");
                return sb.ToString();
            }

            // ---- IFR -----------------------------------------------------------
            RefCmd IFR(Address address, ASMC_Delect asmdelect)
            {
                StringBuilder sb = new StringBuilder();

                uint vanila_addr;
                if (address.Pointer == 0 || address.Pointer == U.NOT_FOUND)
                {//ポインタがないので代わりに現在のアドレスで比較してみる.
                    vanila_addr = address.Addr;
                }
                else if (U.isSafetyOffset(address.Pointer, this.Vanilla))
                {
                    vanila_addr = this.Vanilla.p32(address.Pointer);
                    if (!U.isSafetyOffset(vanila_addr, this.Vanilla))
                    {//ポインタ先が不明なのでアドレスで補う
                        vanila_addr = address.Addr;
                    }
                }
                else
                {//ポインタがないので、現在のアドレスで比較するしかない.
                    vanila_addr = address.Addr;
                }

                string basename = MakeName(address);
                string filename = Path.Combine("rebuild_ifr", basename + ".txt");
                StringBuilder infsb = new StringBuilder();

                RefCmd refCmd = new RefCmd();

                uint addr = address.Addr;
                uint startaddr = addr;
                uint end = addr + address.Length;                   //終端データまで含めたデータ末尾の位置
                uint endMinusOneBlock = end - address.BlockSize;    //終端データを含めないデータ末尾の位置

                byte[] bin = this.Modified;
                for (uint no = 0; addr < end; addr += address.BlockSize, no++)
                {
                    infsb.Append("=");
                    infsb.Append(U.ToHexString8(no));

                    bool isTermData = (endMinusOneBlock == addr); //終端データ
                    //1件だけのデータなので、終端データの保護はありません
                    if (address.DataType == Address.DataTypeEnum.InputFormRef_1)
                    {
                        isTermData = false;
                    }

                    uint inner_end = Math.Min(addr + address.BlockSize, (uint)bin.Length);
                    for (uint a = addr; a < inner_end; a++)
                    {
                        infsb.Append(' ');
                        if (a % 4 == 0 //ARMなので4バイトアライメント
                            && isTermData == false //終端データのポインタは無効のデータがあるので無視していい
                            && U.GetIndexOf(address.PointerIndexes, a - addr) >= 0 //ポインタがあるカラムかどうか
                            )
                        {
                            bool r = IsRegistAddr(refCmd, infsb, bin, startaddr, endMinusOneBlock, a, asmdelect);
                            if (r)
                            {
                                a += 3;
                                continue;
                            }
                        }
                        infsb.Append(U.ToHexString(bin[a]));
                    }
                    infsb.AppendLine();
                }

                if (infsb.Length == 0)
                {//記録するデータがない
                    return refCmd;
                }

                string fullfilename = Path.Combine(this.BaseDir, filename);
                File.WriteAllText(fullfilename, infsb.ToString());

                sb.Append("@IFR ");
                sb.Append(U.ToHexString8(address.Addr)); //addr
                sb.Append(" :");
                sb.Append(U.ToHexString(address.BlockSize)); //blocksize
                sb.Append(" *");
                sb.Append(U.ToHexString(address.Length / address.BlockSize)); //count
                if (vanila_addr == address.Addr)
                {//ポインタがないか、アドレスを変更していない.
                }
                else
                {
                    sb.Append(" =");
                    sb.Append(U.ToHexString8(vanila_addr)); //無改造ROMのアドレス
                }
                sb.Append(" ");
                sb.Append(filename);

                refCmd.Cmd = sb.ToString();
                return refCmd;
            }

            void Apeend4Bytes(StringBuilder sb, byte[] romdata, uint addr)
            {
                sb.Append(U.ToHexString(romdata[addr + 0]));
                sb.Append(' ');
                sb.Append(U.ToHexString(romdata[addr + 1]));
                sb.Append(' ');
                sb.Append(U.ToHexString(romdata[addr + 2]));
                sb.Append(' ');
                sb.Append(U.ToHexString(romdata[addr + 3]));
            }

            // ---- IsRegistAddr (the @ / ` / & / + token decision) ---------------
            bool IsRegistAddrTextPointers(RefCmd refCmd, StringBuilder sb, byte[] romdata, uint startaddr, uint endaddr, uint addr)
            {
                uint srcp = U.u32(romdata, addr);
                bool isAntiHuffman;

                if (srcp >= 0x80000000 && srcp < 0x8A000000)
                {//Anti-Huffman
                    isAntiHuffman = true;
                    srcp -= 0x80000000;
                }
                else
                {//通常のポインタ
                    isAntiHuffman = false;
                }

                if (!U.isSafetyPointer(srcp))
                {//正しいポインタではない.
                    return false;
                }

                if (PointerMark.ContainsKey(srcp))
                {
                    if (isAntiHuffman)
                    {
                        sb.Append('`');
                    }
                    else
                    {
                        sb.Append('@');
                    }
                    sb.Append(U.ToHexString(srcp));
                    return true;
                }

                return false;
            }

            bool IsRegistAddr(RefCmd refCmd, StringBuilder sb, byte[] romdata, uint startaddr, uint endaddr, uint addr, ASMC_Delect asmcdelect)
            {
                if (asmcdelect == ASMC_Delect.TEXTPOINTERS)
                {
                    return IsRegistAddrTextPointers(refCmd, sb, romdata, startaddr, endaddr, addr);
                }

                uint srcp = U.u32(romdata, addr);
                if (!U.isSafetyPointer(srcp))
                {
                    return false;
                }
                uint srcoffset = U.toOffset(srcp);

                if (PointerMark.ContainsKey(srcp))
                {
                    if (srcoffset == startaddr
                     && IsEnableSelfPointer(asmcdelect)
                        )
                    {//自己参照
                        sb.Append('+');
                        sb.Append(U.ToHexString(srcoffset - startaddr));
                    }
                    else if (asmcdelect == ASMC_Delect.ASM
                        && U.IsValueOdd(srcp)
                        && PointerMark[srcp] == PointerType.ASM)
                    {//ASMポインタ
                        sb.Append('&');
                        sb.Append(U.ToHexString(srcp - 1));
                    }
                    else
                    {//ポインタ
                        sb.Append('@');
                        sb.Append(U.ToHexString(srcp));
                    }

                    return true;
                }
                else if (srcoffset >= startaddr && srcoffset < endaddr
                     && IsEnableSelfPointer(asmcdelect)
                    )
                {//自己参照
                    sb.Append('+');
                    sb.Append(U.ToHexString(srcoffset - startaddr));
                    return true;
                }
                else if (U.IsValueOdd(srcp)
                            && PointerMark.ContainsKey(srcp - 1)
                            && PointerMark[srcp - 1] == PointerType.ASM
                            && IsEnableASMPointer(asmcdelect)
                    )
                {//ASM参照
                    sb.Append('&');
                    sb.Append(U.ToHexString(srcp));
                    return true;
                }
                else
                {//謎のアドレス
                    return false;
                }
            }
            bool IsEnableASMPointer(ASMC_Delect asmcdelect)
            {
                return asmcdelect == ASMC_Delect.AUTO || asmcdelect == ASMC_Delect.ASM;
            }
            bool IsEnableSelfPointer(ASMC_Delect asmcdelect)
            {
                return asmcdelect != ASMC_Delect.ASM;
            }

            // ---- generic / specialised MIX inner emitters ----------------------
            bool WildCard(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length, ASMC_Delect asmc_delect)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);
                bool isPointer = false;

                for (; addr < end; addr++)
                {
                    infsb.Append(' ');
                    if (addr % 4 == 0)
                    {
                        bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr, asmc_delect);
                        if (r)
                        {
                            addr += 3;
                            isPointer = true;
                            continue;
                        }
                    }
                    infsb.Append(U.ToHexString(romdata[addr]));
                }

                return isPointer;
            }
            void MixRec(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length, uint[] pointerIndexes)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                for (; addr < end; addr++)
                {
                    infsb.Append(' ');
                    if (addr % 4 == 0
                        && U.GetIndexOf(pointerIndexes, addr - startaddr) >= 0)
                    {
                        bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr, ASMC_Delect.AUTO);
                        if (r)
                        {
                            addr += 3;
                            continue;
                        }
                    }
                    infsb.Append(U.ToHexString(romdata[addr]));
                }
            }

            //楽譜専用
            void SongScore(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                uint position = addr;
                uint percussion = 0;
                while (true)
                {
                    if (position >= end)
                    {
                        break;
                    }
                    uint b = romdata[position];
                    position++;

                    infsb.Append(' ');
                    infsb.Append(U.ToHexString(b));

                    if (b == 0xB1)
                    {
                        break;
                    }
                    else if (b == 0xB2 || b == 0xB3)
                    {
                        //repointer (4-byte payload). 切り詰められたデータで end を跨がないよう確認.
                        if (position + 4 > end)
                        {
                            break;
                        }
                        infsb.Append(' ');
                        bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, position, ASMC_Delect.NONE);
                        if (!r)
                        {
                            Apeend4Bytes(infsb, romdata, position);
                        }
                        position += 4;
                    }
                    else if (b == 0xBD || b == 0xBB || b == 0xBC || b == 0xBE || b == 0xBF || b == 0xC0 || b == 0xC1)
                    {
                        // These commands take a data byte that must not be processed.
                        if (position >= end)
                        {
                            break;
                        }
                        infsb.Append(' ');
                        infsb.Append(U.ToHexString(romdata[position]));
                        position++;
                    }
                    else if (b == 0xb9)
                    {//MEMACC 4バイト命令. 最初の1バイトはコピー済みなので、残りの3バイトコピーする.
                        if (position + 3 > end)
                        {
                            break;
                        }
                        infsb.Append(' ');
                        infsb.Append(U.ToHexString(romdata[position]));
                        position++;
                        infsb.Append(' ');
                        infsb.Append(U.ToHexString(romdata[position]));
                        position++;
                        infsb.Append(' ');
                        infsb.Append(U.ToHexString(romdata[position]));
                        position++;
                    }
                    else if (percussion != 0 && b < 0x80)
                    {
                        while (position < end && romdata[position] < 0x80)
                        {// Volume marker
                            infsb.Append(' ');
                            infsb.Append(U.ToHexString(romdata[position]));
                            position++;
                        }
                    }
                }
            }

            //Font
            void Font(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                //先頭だけポインタ
                {
                    infsb.Append(' ');
                    bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr, ASMC_Delect.NONE);
                    if (!r)
                    {
                        Apeend4Bytes(infsb, romdata, addr);
                    }
                    addr += 4;
                }

                //以下データ
                for (; addr < end; addr++)
                {
                    infsb.Append(' ');
                    infsb.Append(U.ToHexString(romdata[addr]));
                }
            }

            //ASM
            void ASM(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                List<DisassemblerTrumb.LDRPointer> ldrmap = DisassemblerTrumb.MakeLDRMap(romdata, addr, addr + length, true);
                Dictionary<uint, bool> ldrmapFast = new Dictionary<uint, bool>();
                for (int i = 0; i < ldrmap.Count; i++)
                {
                    DisassemblerTrumb.LDRPointer ldr = ldrmap[i];
                    ldrmapFast[ldr.ldr_data_address] = true;
                }

                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                for (; addr < end; addr++)
                {
                    infsb.Append(' ');
                    if (addr % 4 == 0
                        && ldrmapFast.ContainsKey(addr))
                    {
                        bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr, ASMC_Delect.ASM);
                        if (r)
                        {
                            addr += 3;
                            continue;
                        }
                    }
                    infsb.Append(U.ToHexString(romdata[addr]));
                }
            }

            void NoPointer(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint end = Math.Min(addr + length, (uint)romdata.Length);
                for (; addr < end; addr++)
                {
                    infsb.Append(' ');
                    infsb.Append(U.ToHexString(romdata[addr]));
                }
            }

            bool IsEventCondASM(uint type)
            {
                if (this.RomVersion == 6)
                {
                    return type == 0xD;
                }
                return type == 0xE || type == 0x4;
            }

            void EventCond12_16(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                bool isFE7 = this.RomVersion == 7;

                while (addr < end)
                {
                    if (addr + 12 > end)
                    {
                        break;
                    }

                    uint type = romdata[addr];
                    if (type == 0)
                    {//終端
                        break;
                    }

                    infsb.Append(' ');
                    Apeend4Bytes(infsb, romdata, addr + 0);

                    infsb.Append(' ');
                    bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr + 4, ASMC_Delect.NONE);
                    if (!r)
                    {
                        Apeend4Bytes(infsb, romdata, addr + 4);
                    }

                    infsb.Append(' ');
                    bool r2;
                    if (IsEventCondASM(type))
                    {
                        r2 = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr + 8, ASMC_Delect.ASM);
                    }
                    else
                    {
                        r2 = false;
                    }
                    if (!r2)
                    {
                        Apeend4Bytes(infsb, romdata, addr + 8);
                    }

                    if (isFE7 && type == 0x02)
                    {
                        infsb.Append(' ');
                        Apeend4Bytes(infsb, romdata, addr + 12);
                        addr += 16;
                    }
                    else
                    {
                        addr += 12;
                    }
                }
                //端数データがあれば記録する
                NoPointer(refCmd, infsb, romdata, addr, end - addr);
            }

            void EventCond12(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                while (addr < end)
                {
                    if (addr + 12 > end)
                    {//端数を何とか格納する
                        break;
                    }

                    uint type = romdata[addr];
                    if (type == 0)
                    {//終端
                        break;
                    }
                    infsb.Append(' ');
                    Apeend4Bytes(infsb, romdata, addr + 0);

                    infsb.Append(' ');
                    bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr + 4, ASMC_Delect.NONE);
                    if (!r)
                    {
                        Apeend4Bytes(infsb, romdata, addr + 4);
                    }

                    infsb.Append(' ');
                    bool r2;
                    if (IsEventCondASM(type))
                    {
                        r2 = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr + 8, ASMC_Delect.ASM);
                    }
                    else
                    {
                        r2 = false;
                    }
                    if (!r2)
                    {
                        Apeend4Bytes(infsb, romdata, addr + 8);
                    }

                    addr += 12;
                }
                //端数データがあれば記録する
                NoPointer(refCmd, infsb, romdata, addr, end - addr);
            }

            void EventCond16(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                while (addr < end)
                {
                    if (addr + 12 > end)
                    {//端数を何とか格納する
                        break;
                    }

                    uint type = romdata[addr];
                    if (type == 0)
                    {//終端
                        break;
                    }

                    infsb.Append(' ');
                    Apeend4Bytes(infsb, romdata, addr + 0);

                    infsb.Append(' ');
                    bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr + 4, ASMC_Delect.NONE);
                    if (!r)
                    {
                        Apeend4Bytes(infsb, romdata, addr + 4);
                    }

                    infsb.Append(' ');
                    Apeend4Bytes(infsb, romdata, addr + 8);

                    infsb.Append(' ');
                    bool r3;
                    if (IsEventCondASM(type))
                    {
                        r3 = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr + 12, ASMC_Delect.ASM);
                    }
                    else
                    {
                        r3 = false;
                    }
                    if (!r3)
                    {
                        Apeend4Bytes(infsb, romdata, addr + 12);
                    }

                    addr += 16;
                }
                //端数データがあれば記録する
                NoPointer(refCmd, infsb, romdata, addr, end - addr);
            }

            void EventScript1(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length)
            {
                uint end = Math.Min(addr + length, (uint)romdata.Length);
                while (addr < end)
                {
                    List<uint> pointerIndexes = new List<uint>();
                    EventScript.OneCode code = this.EventScriptWithPatchDic.DisAseemble(romdata, addr);
                    for (int i = 0; i < code.Script.Args.Length; i++)
                    {
                        EventScript.Arg arg = code.Script.Args[i];
                        EventScript.ArgType type = arg.Type;
                        if (FEBuilderGBA.EventScript.IsPointerArgs(type))
                        {
                            pointerIndexes.Add((uint)arg.Position);
                        }
                    }

                    uint inneraddr = addr;
                    uint innerend = addr + (uint)code.Script.Size;
                    uint inneroffset = 0;
                    while (inneraddr < innerend)
                    {
                        EventScript.OneCode innercode = this.EventScriptWithoutPatchDic.DisAseemble(romdata, inneraddr);
                        for (int i = 0; i < innercode.Script.Args.Length; i++)
                        {
                            EventScript.Arg arg = innercode.Script.Args[i];
                            EventScript.ArgType type = arg.Type;
                            if (FEBuilderGBA.EventScript.IsPointerArgs(type))
                            {
                                pointerIndexes.Add((uint)arg.Position + inneroffset);
                            }
                        }
                        inneroffset += (uint)innercode.Script.Size;
                        inneraddr += (uint)innercode.Script.Size;
                    }

                    MixRec(refCmd, infsb, romdata, addr, (uint)code.Script.Size, pointerIndexes.ToArray());

                    addr += (uint)code.Script.Size;
                }
            }

            void EventScript2(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length, EventScript scriptDic)
            {
                uint end = Math.Min(addr + length, (uint)romdata.Length);
                while (addr < end)
                {
                    List<uint> pointerIndexes = new List<uint>();
                    EventScript.OneCode code = scriptDic.DisAseemble(romdata, addr);
                    for (int i = 0; i < code.Script.Args.Length; i++)
                    {
                        EventScript.Arg arg = code.Script.Args[i];
                        EventScript.ArgType type = arg.Type;
                        if (FEBuilderGBA.EventScript.IsPointerArgs(type))
                        {
                            pointerIndexes.Add((uint)arg.Position);
                        }
                    }

                    MixRec(refCmd, infsb, romdata, addr, (uint)code.Script.Size, pointerIndexes.ToArray());

                    addr += (uint)code.Script.Size;
                }
            }

            //戦闘アニメ or 魔法アニメなどの 0x85フレームがあるもの専用
            void BattleAnimation(RefCmd refCmd, StringBuilder infsb, byte[] romdata, uint addr, uint length, uint pointerCount)
            {
                uint startaddr = addr;
                uint end = Math.Min(addr + length, (uint)romdata.Length);

                for (; addr < end; addr += 4)
                {
                    if (!U.isSafetyZArray(addr + 4 + (4 * pointerCount), romdata))
                    {
                        break;
                    }

                    infsb.Append(' ');
                    Apeend4Bytes(infsb, romdata, addr + 0);

                    if (romdata[addr + 3] != 0x86)
                    {
                        continue;
                    }
                    for (int n = 0; n < pointerCount; n++)
                    {
                        addr += 4;

                        infsb.Append(' ');
                        bool r = IsRegistAddr(refCmd, infsb, romdata, startaddr, end, addr, ASMC_Delect.NONE);
                        if (!r)
                        {
                            Apeend4Bytes(infsb, romdata, addr);
                        }
                    }
                }

                //端数データがあれば記録する.
                NoPointer(refCmd, infsb, romdata, addr, end - addr);
            }

            // ---- MIX dispatcher ------------------------------------------------
            RefCmd Mix(Address address)
            {
                RefCmd refCmd = new RefCmd();

                if (address.Length <= 0)
                {//サイズが0の場合、推測します.
                    if (address.DataType == Address.DataTypeEnum.MAGICFRAME_CSA
                        || address.DataType == Address.DataTypeEnum.MAGICFRAME_FEITORADV)
                    {//間違ったフレームデータ
                        return BrokenData(address);
                    }
                    else
                    {
                        return refCmd;
                    }
                }

                uint vanila_addr;
                if (address.Pointer == 0 || address.Pointer == U.NOT_FOUND)
                {//ポインタがないので代わりに現在のアドレスで比較してみる.
                    if (romcmp(address.Addr, address.Addr, address.Length))
                    {//無改造ROMと同一なので記録する必要なし.
                        return refCmd;
                    }
                    vanila_addr = address.Addr;
                }
                else if (U.isSafetyOffset(address.Pointer, this.Vanilla))
                {
                    if (romcmpPointer(address.Pointer, address.Pointer, address.Length))
                    {//ポインタ上では、記録する必要なし
                        if (romcmp(address.Addr, address.Addr, address.Length))
                        {//無改造ROMと同一なので記録する必要なし.
                            return refCmd;
                        }
                    }

                    vanila_addr = this.Vanilla.p32(address.Pointer);
                    if (!U.isSafetyOffset(vanila_addr, this.Vanilla))
                    {//バニラのアドレスが不明
                        vanila_addr = address.Addr;
                    }
                }
                else
                {//ポインタはあるがバニラにはないということは拡張領域にあるデータだと思われる.
                    if (romcmp(address.Addr, address.Addr, address.Length))
                    {//無改造ROMと同一なので記録する必要なし.
                        return refCmd;
                    }
                    vanila_addr = address.Addr;
                }

                string basename = MakeName(address);
                string filename = Path.Combine("rebuild_mix", basename + ".txt");
                StringBuilder infsb = new StringBuilder();

                StringBuilder sb = new StringBuilder();

                if (address.DataType == Address.DataTypeEnum.BATTLEFRAME)
                {//LZ77で圧縮が必要
                    byte[] bin = LZ77.decompress(this.Modified, address.Addr);
                    BattleAnimation(refCmd, infsb, bin, 0, (uint)bin.Length, 2);
                    sb.Append("@MIXLZ77 ");
                }
                else if (address.DataType == Address.DataTypeEnum.MAGICFRAME_FEITORADV)
                {//魔法フレーム1
                    BattleAnimation(refCmd, infsb, this.Modified, address.Addr, address.Length, 6);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.MAGICFRAME_CSA)
                {//魔法フレーム2
                    BattleAnimation(refCmd, infsb, this.Modified, address.Addr, address.Length, 7);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.EVENTCOND_ALWAYS)
                {//常時条件
                    EventCond12(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.EVENTCOND_OBJECT)
                {//オブジェクト
                    EventCond12(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.EVENTCOND_TALK)
                {//会話
                    if (this.RomVersion == 6)
                    {
                        EventCond12(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    }
                    else
                    {
                        EventCond16(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    }
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.EVENTCOND_TURN)
                {//ターン
                    if (this.RomVersion == 7)
                    {
                        EventCond12_16(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    }
                    else
                    {
                        EventCond12(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    }
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.SONGSCORE)
                {//音楽データ
                    SongScore(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.EVENTSCRIPT
                         && this.EventScriptWithPatchDic != null && this.EventScriptWithoutPatchDic != null)
                {
                    EventScript1(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.AISCRIPT && this.AIScript != null)
                {
                    EventScript2(refCmd, infsb, this.Modified, address.Addr, address.Length, this.AIScript);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.PROCS && this.ProcsScript != null)
                {
                    EventScript2(refCmd, infsb, this.Modified, address.Addr, address.Length, this.ProcsScript);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.SONGTRACK)
                {
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.NONE);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.ASM
                    || address.DataType == Address.DataTypeEnum.PATCH_ASM
                    || address.DataType == Address.DataTypeEnum.BL_ASM
                    )
                {
                    ASM(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.FONT)
                {//フォント
                    Font(refCmd, infsb, this.Modified, address.Addr, address.Length);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.POINTER)
                {//ポインタ
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.NONE);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.POINTER_ASM)
                {//ポインタ
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.ASM);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.NEW_TARGET_SELECTION_STRUCT)
                {//ポインタ
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.ASM);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.POINTER_ARRAY)
                {//ポインタ
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.NONE);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.SplitMenu5)
                {//分岐メニュー5
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.AUTO);
                    sb.Append("@MIX ");
                }
                else if (address.DataType == Address.DataTypeEnum.SplitMenu9)
                {//分岐メニュー9
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.AUTO);
                    sb.Append("@MIX ");
                }
                else
                {
                    WildCard(refCmd, infsb, this.Modified, address.Addr, address.Length, ASMC_Delect.AUTO);
                    sb.Append("@MIX ");
                }

                if (infsb.Length <= 0)
                {
                    return refCmd;
                }

                //MIXデータを書き込む.
                infsb.Remove(0, 1);
                string fullfilename = Path.Combine(this.BaseDir, filename);
                File.WriteAllText(fullfilename, infsb.ToString());

                sb.Append(U.ToHexString8(address.Addr)); //addr

                if (address.DataType == Address.DataTypeEnum.BATTLEFRAME)
                {//LZ77で圧縮する場合、データのサイズが可変なので、上書きできるか調べるにはサイズデータが必要です.
                    sb.Append(" :");
                    sb.Append(U.ToHexString(address.Length)); //blocksize
                }

                sb.Append(" ");
                sb.Append(filename);

                refCmd.Cmd = sb.ToString();
                return refCmd;
            }

            RefCmd BIN(Address address)
            {
                RefCmd refCmd = new RefCmd();
                if (address.Length <= 0)
                {
                    return BrokenData(address);
                }

                uint vanila_addr;
                if (address.Pointer == 0 || address.Pointer == U.NOT_FOUND)
                {//ポインタがないので代わりに現在のアドレスで比較してみる.
                    if (romcmp(address.Addr, address.Addr, address.Length))
                    {//無改造ROMと同一なので記録する必要なし.
                        return refCmd;
                    }
                    vanila_addr = address.Addr;
                }
                else if (U.isSafetyOffset(address.Pointer, this.Vanilla))
                {
                    if (romcmpPointer(address.Pointer, address.Pointer, address.Length))
                    {//無改造ROMと同一なので記録する必要なし.
                        if (romcmp(address.Addr, address.Addr, address.Length))
                        {//無改造ROMと同一なので記録する必要なし.
                            return refCmd;
                        }
                    }
                    vanila_addr = this.Vanilla.p32(address.Pointer);
                    if (!U.isSafetyOffset(vanila_addr, this.Vanilla))
                    {//バニラのアドレスが不明
                        vanila_addr = address.Addr;
                    }
                }
                else
                {//ポインタはあるがバニラにはないということは拡張領域にあるデータだと思われる.
                    if (romcmp(address.Addr, address.Addr, address.Length))
                    {//無改造ROMと同一なので記録する必要なし.
                        return refCmd;
                    }
                    vanila_addr = address.Addr;
                }
                StringBuilder sb = new StringBuilder();

                string basename = MakeName(address);
                string filename = Path.Combine("rebuild_bin", basename + ".bin");

                sb.Append("@BIN ");
                sb.Append(U.ToHexString8(address.Addr));
                sb.Append(" ");
                sb.Append(filename);

                string fullfilename = Path.Combine(this.BaseDir, filename);
                byte[] bin = U.getBinaryData(this.Modified, address.Addr, address.Length);
                File.WriteAllBytes(fullfilename, bin);

                refCmd.Cmd = sb.ToString();
                return refCmd;
            }

            //壊れたデータ
            RefCmd BrokenData(Address address)
            {
                RefCmd refCmd = new RefCmd();
                StringBuilder sb = new StringBuilder();

                sb.Append("@BROKENDATA ");
                sb.Append(U.ToHexString8(address.Addr));
                sb.Append("//");
                sb.Append(address.Info);

                refCmd.Cmd = sb.ToString();
                return refCmd;
            }

            // ---- FindUnknownHack2 (byte-diff fallback for unmarked regions) ----
            bool IsNULLData(byte[] bin)
            {
                for (int i = 0; i < bin.Length; i++)
                {
                    if (bin[i] != 0)
                    {
                        return false;
                    }
                }
                return true;
            }

            RefCmd UnkBin(uint addr, uint length)
            {
                Address address = new Address(addr, length, U.NOT_FOUND, "UnkHack", Address.DataTypeEnum.BIN);

                byte[] bin = U.getBinaryData(this.Modified, address.Addr, length);

                StringBuilder sb = new StringBuilder();
                RefCmd refCmd = new RefCmd();
                if (IsNULLData(bin))
                {
                    sb.Append("@00 ");
                    sb.Append(U.ToHexString8(address.Addr));
                    sb.Append(" :");
                    sb.Append(U.ToHexString(address.Length)); //blocksize
                }
                else
                {
                    string basename = MakeName(address);
                    string filename = Path.Combine("rebuild_bin", basename + ".bin");

                    sb.Append("@BIN ");
                    sb.Append(U.ToHexString8(address.Addr));
                    sb.Append(" ");
                    sb.Append(filename);

                    string fullfilename = Path.Combine(this.BaseDir, filename);
                    File.WriteAllBytes(fullfilename, bin);
                }

                refCmd.Cmd = sb.ToString();
                refCmd.UseAddress = address;
                return refCmd;
            }

            void FindUnknownHack2(List<RefCmd> refCmdList, bool[] processedAddress)
            {
                for (int i = 0; i < refCmdList.Count; i++)
                {
                    RefCmd rc = refCmdList[i];
                    Address a = rc.UseAddress;

                    uint addr = a.Addr;
                    uint end = addr + a.Length;
                    end = Math.Min(end, (uint)processedAddress.Length);

                    if (rc.Cmd.IndexOf("@DEF") == 0)
                    {
                        for (; addr < end; addr++)
                        {
                            processedAddress[addr] = false;
                        }
                        continue;
                    }

                    for (; addr < end; addr++)
                    {
                        processedAddress[addr] = true;
                    }
                }

                uint vallinaLength = (uint)this.Vanilla.Data.Length;
                uint length = Math.Min(vallinaLength, this.RebuildAddress);
                uint start = U.NOT_FOUND;
                for (uint i = 0x0; i < length; i++)
                {
                    if (processedAddress[i])
                    {
                        if (start != U.NOT_FOUND)
                        {
                            RefCmd refCmd = UnkBin(start, i - start);
                            refCmdList.Add(refCmd);
                            start = U.NOT_FOUND;
                        }

                        continue;
                    }

                    uint c = i < (uint)this.Modified.Length ? this.Modified[i] : (uint)0;
                    uint v = this.Vanilla.u8(i);
                    if (c != v)
                    {
                        if (start == U.NOT_FOUND)
                        {
                            start = i;
                        }
                    }
                    else
                    {
                        if (start != U.NOT_FOUND)
                        {
                            RefCmd refCmd = UnkBin(start, i - start);
                            refCmdList.Add(refCmd);
                            start = U.NOT_FOUND;
                        }
                    }
                }

                if (this.RebuildAddress > vallinaLength)
                {//無改造ROMより大きい領域で、コピーできる領域.
                    length = this.RebuildAddress - vallinaLength;
                    RefCmd refCmd = UnkBin(vallinaLength, length);
                    refCmdList.Add(refCmd);
                }
            }

            string MakeName(Address address)
            {
                string basename;
                if (address.Info != "")
                {
                    basename = U.escape_filename(address.Info) + "_";
                    basename = basename.Replace(' ', '_');
                    basename = basename.Replace("_____", "_");
                    basename = basename.Replace("____", "_");
                    basename = basename.Replace("___", "_");
                    basename = basename.Replace("__", "_");
                }
                else
                {
                    basename = "";
                }

                if (basename.Length >= 25)
                {
                    basename = basename.Substring(0, 25);
                }

                if (address.Pointer == 0 || address.Pointer == U.NOT_FOUND)
                {
                    return basename + "." + U.ToHexString8(address.Addr);
                }
                else
                {
                    return basename + ".P" + U.ToHexString8(address.Pointer);
                }
            }
        }
    }
}
