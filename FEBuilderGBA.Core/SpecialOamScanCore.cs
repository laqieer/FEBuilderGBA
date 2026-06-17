using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY, GUI-free cross-platform port of WinForms <c>OAMSPForm</c>
    /// (#1179): a discovery + hex-inspection tool for "special OAM"
    /// sprite-assembly entries found by scanning the ROM's ARM-Thumb LDR
    /// literal-pool loads.
    ///
    /// <para>The OAMSP table is a pointer ARRAY whose each pointer references an
    /// OAM12 block (a run of 12-byte OAM-command records). WF renders NO sprite
    /// image for these entries — the OAMSP designer text says the image address
    /// is unknown — so this port mirrors WF exactly: discover + label the
    /// entries and produce a hex-dump detail string. No sprite renderer is
    /// invented.</para>
    ///
    /// <para>Every ROM read is bounds-guarded; on a null ROM / null map / any
    /// fault the scan returns an empty list and the dump helpers return an empty
    /// string. The expensive full-ROM <c>MakeLDRMap</c> is supplied by the
    /// caller (e.g. <c>PointerToolAutoSearchCore.BuildLdrMap(rom.Data)</c>) so it
    /// can be cached per-ROM.</para>
    /// </summary>
    public static class SpecialOamScanCore
    {
        /// <summary>One discovered OAMSP entry: a pointer-array block plus the
        /// OAM12 sub-blocks it references.</summary>
        public sealed class OamSpEntry
        {
            public uint Addr;
            public uint Length;
            public string Name = "";
            public List<OamSp12Block> Oam12 = new List<OamSp12Block>();
        }

        /// <summary>An OAM12 sub-block referenced by an OAMSP pointer array
        /// (a run of 12-byte OAM-command records).</summary>
        public sealed class OamSp12Block
        {
            public uint Addr;
            public uint Length;
        }

        /// <summary>
        /// Discover all special-OAM entries in <paramref name="rom"/> from the
        /// supplied LDR literal-pool map. Verbatim port of WF
        /// <c>OAMSPForm.MakeAllDataLength</c>: a first pass over the LDR map
        /// (entries whose target is a special-OAM pointer array,
        /// <c>length &gt;= 4*3</c>) then a second pass over the <c>oam_name_</c>
        /// resource (named entries, <c>length &gt;= 4</c>). Never throws — returns
        /// an empty list on a null ROM / null map / any fault.
        /// </summary>
        public static List<OamSpEntry> ScanSpecialOam(ROM rom, List<DisassemblerTrumb.LDRPointer> ldrMap)
        {
            var result = new List<OamSpEntry>();
            if (rom == null || rom.Data == null || rom.RomInfo == null || ldrMap == null)
            {
                return result;
            }

            try
            {
                // Load the oam_name_ label resource defensively: a missing config
                // dir (e.g. CoreState.BaseDirectory unset in a headless test) must
                // NOT abort the whole scan — entries still discover with their
                // hex-fallback names. WF always has the config dir, so this only
                // hardens the headless path.
                Dictionary<uint, string> oamName;
                try
                {
                    oamName = U.LoadDicResource(U.ConfigDataFilename("oam_name_", rom));
                }
                catch
                {
                    oamName = null;
                }
                if (oamName == null) oamName = new Dictionary<uint, string>();

                uint borderline = rom.RomInfo.compress_image_borderline_address;
                var alreadyMatch = new Dictionary<uint, bool>();
                var alreadyMatch12 = new Dictionary<uint, bool>();

                // ----- Pass 1: LDR-map discoveries (require length >= 4*3) -----
                for (int i = 0; i < ldrMap.Count; i++)
                {
                    var ldr = ldrMap[i];
                    if (ldr == null) continue;

                    uint addr = ldr.ldr_data;
                    if (!U.isSafetyPointer(addr)) continue;
                    addr = U.toOffset(addr);
                    if (addr < borderline) continue;
                    if (alreadyMatch.ContainsKey(addr)) continue; // already known

                    string name = U.at(oamName, ldr.ldr_data_address);
                    if (name == "") name = U.at(oamName, ldr.ldr_address);
                    if (name == "") name = U.ToHexString8(ldr.ldr_data);
                    name = "OAMSP " + name;

                    var oam12Local = new List<OamSp12Block>();
                    uint length = CalcLengthAndCheck(rom, addr, name, oam12Local, alreadyMatch12);
                    if (length == U.NOT_FOUND || length < 4 * 3)
                    {
                        alreadyMatch[addr] = false; // record the failure
                        continue;
                    }

                    result.Add(new OamSpEntry { Addr = addr, Length = length, Name = name, Oam12 = oam12Local });
                    alreadyMatch[addr] = true;
                }

                // ----- Pass 2: oam_name_ resource entries (require length >= 4) -----
                foreach (var pair in oamName)
                {
                    uint addr = U.toOffset(pair.Key);
                    if (alreadyMatch.ContainsKey(addr)) continue; // already known

                    string name = "OAMSP_ " + pair.Value;
                    var oam12Local = new List<OamSp12Block>();
                    uint length = CalcLengthAndCheck(rom, addr, name, oam12Local, alreadyMatch12);
                    if (length == U.NOT_FOUND || length < 4)
                    {
                        alreadyMatch[addr] = false; // record the failure
                        continue;
                    }

                    result.Add(new OamSpEntry { Addr = addr, Length = length, Name = name, Oam12 = oam12Local });
                    alreadyMatch[addr] = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("SpecialOamScanCore.ScanSpecialOam failed: " + ex.ToString());
                return new List<OamSpEntry>();
            }

            return result;
        }

        /// <summary>
        /// Port of WF <c>OAMSPForm.CalcLengthAndCheck</c>: walk the pointer array
        /// at <paramref name="addr"/>, validating each word as an OAM term or a
        /// safe pointer to an OAM12 block (added to <paramref name="oam12Out"/>).
        /// Returns the byte length of the pointer array, or <c>U.NOT_FOUND</c>
        /// when an illegal command is hit. Bounds-guarded; never throws.
        /// </summary>
        static uint CalcLengthAndCheck(ROM rom, uint addr, string name, List<OamSp12Block> oam12Out, Dictionary<uint, bool> alreadyMatch)
        {
            uint start = addr;
            if ((ulong)addr + 4 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint length = (uint)(rom.Data.Length - 4);

            while (addr < length)
            {
                uint p = rom.u32(addr);
                if ((p & 0x88FFFF00u) == 0x80000000u)
                {//OAM term 0x8X0000XX
                    addr += 4;
                    break;
                }
                if ((p & 0x70000000u) > 0)
                {//OAM term — leading 7 nibble present in some entries
                    p = (p & 0x0FFFFFFFu);
                }
                if (!U.isSafetyPointer(p))
                {
                    //unintelligible command -> not an OAM array
                    return U.NOT_FOUND;
                }

                uint oam12Addr = U.toOffset(p);
                //odd addresses: normalise to plain (matches WF intuition)
                oam12Addr = DisassemblerTrumb.ProgramAddrToPlain(oam12Addr);
                if (!alreadyMatch.ContainsKey(oam12Addr))
                {//not yet known
                    uint oam12length = CalcLengthAndCheckOAM12(rom, oam12Addr);
                    if (oam12length == U.NOT_FOUND)
                    {
                        return U.NOT_FOUND;
                    }
                    oam12Out.Add(new OamSp12Block { Addr = oam12Addr, Length = oam12length });
                    alreadyMatch[oam12Addr] = true;
                }

                addr += 4;
            }
            return addr - start;
        }

        /// <summary>
        /// Port of WF <c>OAMSPForm.CalcLengthAndCheckOAM12</c>: walk a run of
        /// 12-byte OAM-command records from <paramref name="addr"/> until a
        /// terminator. Returns the run length, or <c>U.NOT_FOUND</c> on an OAM
        /// rule violation. Bounds-guarded; never throws.
        /// </summary>
        static uint CalcLengthAndCheckOAM12(ROM rom, uint addr)
        {
            uint start = addr;
            if ((ulong)addr + 12 > (ulong)rom.Data.Length) return U.NOT_FOUND;
            uint length = (uint)(rom.Data.Length - 12);

            while (addr < length)
            {
                byte[] oam = rom.getBinaryData(addr, 12);
                if (oam == null || oam.Length < 12) break;

                addr += 12;
                if (oam[0] == 0 && oam[1] == 0xFF && oam[2] == 0xFF && oam[3] == 0xFF)
                {//FEditor serialize terminator
                    break;
                }
                if (oam[0] == 0 && oam[1] == 0 && oam[2] == 0 && oam[3] == 0
                    && oam[4] == 0 && oam[5] == 0 && oam[6] == 0 && oam[7] == 0)
                {
                    break;
                }
                if (oam[2] == 0xFF && oam[3] == 0xFF)
                {//bytes 2,3 == FF FF -> separate routine
                    continue;
                }
                if (oam[0] == 0xFF && oam[1] == 0xFF)
                {//FF FF xx xx -> special command
                    continue;
                }
                if (oam[0] == 1)
                {//terminator
                    break;
                }
                if (oam[0] == 0)
                {//data
                    continue;
                }

                //OAM rule violation
                return U.NOT_FOUND;
            }
            return addr - start;
        }

        /// <summary>
        /// Build the hex-dump detail string for an entry — port of WF
        /// <c>OAMSPForm.AddressList_SelectedIndexChanged</c>'s <c>X_DATA</c>
        /// text: the entry's pointer-array words, then each OAM12 sub-block's
        /// 12-byte records. Never throws — returns an empty string on a null /
        /// invalid entry or any fault (keeps the selection path read-only and
        /// crash-free, #1179 review point 3).
        /// </summary>
        public static string BuildDetailDump(ROM rom, OamSpEntry entry)
        {
            if (rom == null || rom.Data == null || entry == null) return "";

            try
            {
                var sb = new StringBuilder();

                uint addr = entry.Addr;
                uint length = entry.Length;
                for (uint i = 0; i < length; i += 4)
                {
                    if ((ulong)addr + i + 4 > (ulong)rom.Data.Length) break;
                    uint p = rom.u32(addr + i);
                    sb.Append(U.ToHexString8(p));
                    sb.Append(" ");
                }
                sb.AppendLine();
                sb.AppendLine();

                if (entry.Oam12 != null)
                {
                    for (int n = 0; n < entry.Oam12.Count; n++)
                    {
                        var block = entry.Oam12[n];
                        if (block == null) continue;
                        uint baddr = block.Addr;
                        uint blen = block.Length;
                        sb.AppendLine(U.ToHexString8(baddr) + ":");
                        if (blen == U.NOT_FOUND)
                        {
                            sb.AppendLine("-unknown-");
                            for (uint i = 0; i < 12 * 100; i += 12, baddr += 12)
                            {
                                if ((ulong)baddr + 12 > (ulong)rom.Data.Length) break;
                                sb.Append(U.HexDump(rom.getBinaryData(baddr, 12)));
                            }
                        }
                        else
                        {
                            for (uint i = 0; i < blen; i += 12, baddr += 12)
                            {
                                if ((ulong)baddr + 12 > (ulong)rom.Data.Length) break;
                                sb.Append(U.HexDump(rom.getBinaryData(baddr, 12)));
                            }
                        }
                        sb.AppendLine();
                        sb.AppendLine();
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log.Error("SpecialOamScanCore.BuildDetailDump failed: " + ex.ToString());
                return "";
            }
        }
    }
}
