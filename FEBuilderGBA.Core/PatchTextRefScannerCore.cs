// SPDX-License-Identifier: GPL-3.0-or-later
// #1027 — Installed-patch text/song reference scanner for the Text Editor
// free-area union. Cross-platform, strictly READ-ONLY.
//
// Faithful (subset) port of WinForms PatchForm.MakeVarsIDArray (PatchForm.cs:6895):
//   for every INSTALLED ADDR / STRUCT patch in config/patch2/{version}/, fold its
//   TEXT/SONG/EVENT references into the union:
//     - ADDR  + ADDRESS_TYPE TEXT  -> u16 text id at the literal ADDRESS
//     - ADDR  + ADDRESS_TYPE SONG  -> u8  song id at the literal ADDRESS
//     - STRUCT                     -> for each of DATACOUNT entries, for each
//                                     {prefix}{n}:TYPE index:
//                                       TEXT  (len 2) -> u16, TEXT (len 1) -> u8
//                                       SONG  (len 2) -> u16, SONG (len 1) -> u8
//                                       EVENT (len>1) -> follow event pointer, scan
//
// WF gating ported (PatchForm.MakeVarsIDArray + IsMakePatchStructDataListTarget):
//   - skip CANONICAL_SKIP patches
//   - STRUCT: include unless CheckIF == "E"
//   - ADDR : include only when CheckIF == "I" (installed) — NOT "E"/not-installed
//
// DOCUMENTED RESIDUAL CARVE-OUTS (why this is "Ref #1027" not "Closes" for the patch
// source): WinForms PatchForm.convertBinAddressString / CheckIF resolve four ADDRESS
// macro forms that are NOT ported here because they need the full WinForms grep /
// freespace subsystem:
//   - $GREP / $XGREP   (byte-signature search for ADDRESS / POINTER / DATACOUNT)
//   - $FGREP           (file-content search)
//   - $FREEAREA        (install-time relocation target — never a data-scan address)
//   - GREP-based install IF detection (PatchMetadataCore.CheckPatchInstalled returns
//     Unknown for $GREP/$FGREP conditions)
// A patch whose ADDRESS / POINTER / DATACOUNT is grep-resolved, or whose install state
// is only detectable via a grep IF, is SKIPPED (its text ids are not added). Patches
// using literal / $0x-pointer addresses + literal DATACOUNT are fully covered. The
// SkillSystems STRUCT text table is covered separately + faithfully by
// SkillSystemTextScanner (free-area union calls both).

using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Scans installed ADDR / STRUCT patch metadata for TEXT / SONG / EVENT
    /// references and folds them into the Text Editor free-area "used text id"
    /// union. See file header for the faithful subset + documented carve-outs.
    /// </summary>
    public static class PatchTextRefScannerCore
    {
        /// <summary>
        /// Add every installed-patch TEXT id (and event-followed text id) into
        /// <paramref name="ids"/>. SONG ids are reported separately via
        /// <paramref name="songIds"/> (the free-area union masks SONG ids too —
        /// mirroring WF UseValsID.ConvertMaps which keys by raw id regardless of
        /// TargetType). Pass null for <paramref name="songIds"/> to ignore songs.
        /// </summary>
        /// <param name="rom">Loaded ROM (must be the active CoreState.ROM for the
        /// EVENT-follow path to run).</param>
        /// <param name="ids">Destination TEXT-id set.</param>
        /// <param name="songIds">Optional destination SONG-id set.</param>
        public static void CollectUsedRefs(ROM rom, HashSet<uint> ids, HashSet<uint> songIds)
        {
            if (rom?.RomInfo == null || rom.Data == null || ids == null) return;

            string patchDir = ResolvePatchDirectory(rom.RomInfo.VersionToFilename);
            if (string.IsNullOrEmpty(patchDir) || !Directory.Exists(patchDir)) return;

            string lang = PatchMetadataCore.GetLanguageSuffix();

            // EventScript scan prerequisites (for STRUCT EVENT indexes).
            bool eventScanReady =
                CoreState.EventScript != null
                && CoreState.ROM != null
                && ReferenceEquals(CoreState.ROM, rom)
                && CoreState.CommentCache != null;
            var tracelist = new List<uint>();

            string[] dirs;
            try { dirs = Directory.GetDirectories(patchDir); }
            catch { return; }

            foreach (string dir in dirs)
            {
                string[] patchFiles;
                try { patchFiles = Directory.GetFiles(dir, "PATCH_*.txt"); }
                catch { continue; }
                if (patchFiles.Length == 0) continue;

                ScanOnePatch(rom, patchFiles[0], lang, eventScanReady, tracelist, ids, songIds);
            }
        }

        static void ScanOnePatch(ROM rom, string patchFile, string lang, bool eventScanReady,
            List<uint> tracelist, HashSet<uint> ids, HashSet<uint> songIds)
        {
            List<PatchMetadataCore.PatchParam> prms;
            try { prms = PatchMetadataCore.ParsePatchParams(patchFile); }
            catch { return; }
            if (prms.Count == 0) return;

            // CANONICAL_SKIP guard (WF PatchForm.isCanonicalSkip).
            string canonicalSkip = GetParam(prms, "CANONICAL_SKIP", "0");
            if (StringBool(canonicalSkip)) return;

            string type = GetParam(prms, "TYPE", "");
            if (type != "ADDR" && type != "STRUCT") return; // WF dispatches only these

            // CheckIF tri-state (port of CheckIFFast):
            //   "E" -> prerequisite missing / dangerous addr -> skip ALL types
            //   "I" -> installed
            //   "" -> not installed
            string checkIF = CheckIF(rom, prms, lang);

            // WF IsMakePatchStructDataListTarget(type, checkIF, isInstallOnly:true, isStructOnly:false):
            //   STRUCT: include unless "E".
            //   non-STRUCT (ADDR): include only when "I".
            if (type == "STRUCT")
            {
                if (checkIF == "E") return;
                ScanStruct(rom, prms, eventScanReady, tracelist, ids, songIds);
            }
            else // ADDR
            {
                if (checkIF != "I") return;
                ScanAddr(rom, prms, ids, songIds);
            }
        }

        // ---- ADDR (PatchForm.MakeVarsIDArrayForAddr) ----------------------
        static void ScanAddr(ROM rom, List<PatchMetadataCore.PatchParam> prms,
            HashSet<uint> ids, HashSet<uint> songIds)
        {
            string addressType = GetParam(prms, "ADDRESS_TYPE", "");
            if (addressType != "TEXT" && addressType != "SONG") return;

            string addrStr = GetParam(prms, "ADDRESS", "");
            uint addr;
            if (!TryResolveAddress(rom, addrStr, out addr)) return;
            if (!U.isSafetyOffset(addr, rom)) return;

            if (addressType == "TEXT")
            {
                if (addr + 2 > (uint)rom.Data.Length) return;
                AddTextId(ids, rom.u16(addr));
            }
            else // SONG
            {
                if (addr + 1 > (uint)rom.Data.Length) return;
                AddSongId(songIds, rom.u8(addr));
            }
        }

        // ---- STRUCT (PatchForm.MakeVarsIDArrayForStruct) ------------------
        static void ScanStruct(ROM rom, List<PatchMetadataCore.PatchParam> prms,
            bool eventScanReady, List<uint> tracelist, HashSet<uint> ids, HashSet<uint> songIds)
        {
            // Resolve struct base — POINTER (deref) takes priority over ADDRESS.
            uint structAddr;
            string pointerStr = GetParam(prms, "POINTER", "");
            if (pointerStr != "")
            {
                uint structPointer;
                if (!TryResolveAddress(rom, pointerStr, out structPointer)) return;
                if (!U.isSafetyOffset(structPointer, rom)) return;
                if (structPointer + 4 > (uint)rom.Data.Length) return;
                structAddr = rom.p32(structPointer);
                if (!U.isSafetyOffset(structAddr, rom)) return;
            }
            else
            {
                string addressStr = GetParam(prms, "ADDRESS", "");
                if (addressStr == "") return;
                if (!TryResolveAddress(rom, addressStr, out structAddr)) return;
                if (!U.isSafetyOffset(structAddr, rom)) return;
            }

            uint datasize = U.atoi0x(GetParam(prms, "DATASIZE", ""));
            if (datasize <= 0) return;

            string datacountStr = GetParam(prms, "DATACOUNT", "");
            uint datacount;
            if (datacountStr.Length > 0 && datacountStr[0] == '$')
            {
                // CARVE-OUT: $GREP-resolved DATACOUNT — would need the WinForms grep
                // subsystem. Skip (documented residual gap).
                return;
            }
            else
            {
                datacount = U.atoi0x(datacountStr);
            }
            if (datacount <= 0) return;
            if (datacount >= 0xFFFF) return; // WF Debug.Assert guard

            // Build the per-index list (port of MakeTextIndexes): {prefix}{n}:TYPE
            // where prefix maps to a byte length via GetTypeLength.
            var indexes = MakeTextIndexes(prms);
            if (indexes.Count == 0) return;

            uint addr = structAddr;
            for (uint i = 0; i < datacount; i++, addr += datasize)
            {
                foreach (var idx in indexes)
                {
                    uint p = addr + idx.Offset;
                    if (idx.Length == 1)
                    {
                        if (idx.Type == "TEXT")
                        {
                            if (p + 1 > (uint)rom.Data.Length) continue;
                            AddTextId(ids, rom.u8(p));
                        }
                        else if (idx.Type == "SONG")
                        {
                            if (p + 1 > (uint)rom.Data.Length) continue;
                            AddSongId(songIds, rom.u8(p));
                        }
                    }
                    else
                    {
                        if (idx.Type == "EVENT")
                        {
                            if (!eventScanReady) continue;
                            if (p + 4 > (uint)rom.Data.Length) continue;
                            uint eventAddr = rom.p32(p);
                            if (!U.isSafetyOffset(eventAddr, rom)) continue;
                            EventScriptReferenceScanner.ScanScriptForTextIds(
                                rom, CoreState.EventScript, eventAddr, tracelist, ids);
                        }
                        else if (idx.Type == "TEXT")
                        {
                            if (p + 2 > (uint)rom.Data.Length) continue;
                            AddTextId(ids, rom.u16(p));
                        }
                        else if (idx.Type == "SONG")
                        {
                            if (p + 2 > (uint)rom.Data.Length) continue;
                            AddSongId(songIds, rom.u16(p));
                        }
                        // MULTICG/BGICON -> BG ids only; not text/song -> ignored
                        // for the text/song free-area union (WF appends them as BG).
                    }
                }
            }
        }

        struct TextIndex { public uint Offset; public uint Length; public string Type; }

        // Port of PatchForm.MakeTextIndexes: every "{prefix}{n}:TYPE" param where
        // TYPE in {TEXT,EVENT,SONG,FLAG} and {n} is numeric. Length = GetTypeLength(prefix).
        static List<TextIndex> MakeTextIndexes(List<PatchMetadataCore.PatchParam> prms)
        {
            var list = new List<TextIndex>();
            foreach (var prm in prms)
            {
                string[] sp = prm.KeyParts;
                if (sp.Length < 1) continue;
                string key = sp[0];
                string type = sp.Length > 1 ? sp[1] : "";
                if (type != "TEXT" && type != "EVENT" && type != "SONG" && type != "FLAG") continue;
                if (key.Length < 2) continue;
                if (!IsNum(key[1])) continue;
                int datanum = (int)AtoiPrefix(key.Substring(1));
                list.Add(new TextIndex
                {
                    Offset = (uint)datanum,
                    Length = GetTypeLength(key[0]),
                    Type = type,
                });
            }
            return list;
        }

        // VERBATIM port of WinForms InputFormRef.GetTypeLength (InputFormRef.cs:5033):
        //   B/b/l/h -> 1, W -> 2, D/P -> 4, everything else -> 0.
        // Note: in MakeVarsIDArrayForStruct only `length == 1` takes the u8 path; any
        // other length (0, 2, 4) falls into the u16 / EVENT-pointer branch. So a
        // `T{n}`/`S{n}` prefix (not in the table -> 0) correctly reads a u16 text/song
        // id, and `B{n}` (1) reads a u8 — matching WF byte-for-byte.
        static uint GetTypeLength(char c)
        {
            switch (c)
            {
                case 'B': return 1;
                case 'b': return 1;
                case 'W': return 2;
                case 'D': return 4;
                case 'P': return 4;
                case 'l': return 1;
                case 'h': return 1;
            }
            return 0;
        }

        // ---- shared address resolution (literal + $0x pointer only) -------
        // Returns false for $GREP / $XGREP / $FGREP / $FREEAREA (carved out).
        static bool TryResolveAddress(ROM rom, string addrstring, out uint addr)
        {
            addr = 0;
            if (string.IsNullOrEmpty(addrstring)) return false;

            if (addrstring[0] != '$')
            {
                addr = U.toOffset(U.atoi0x(addrstring));
                return true;
            }

            string value = addrstring.Substring(1);
            if (value.Length == 0) return false;

            if (IsNum(value[0]))
            {
                // $0x123 -> [0x123] pointer deref
                uint pa = U.toOffset(U.atoi0x(value));
                if (!U.isSafetyOffset(pa, rom)) return false;
                if (pa + 4 > (uint)rom.Data.Length) return false;
                addr = rom.p32(pa);
                return true;
            }

            // $FREEAREA / $GREP / $XGREP / $FGREP — carved out.
            return false;
        }

        // ---- CheckIF tri-state (port of PatchForm.CheckIF, literal-addr subset) -
        // Faithful port of WinForms PatchForm.CheckIF (literal-address subset).
        // Returns "E" (prerequisite missing / dangerous / conflict), "I" (installed),
        // or "" (undetermined). Mirrors WF exactly: per IF-family key, compute
        // `notFound` (the ROM byte-pattern at the address does NOT match the need),
        // then dispatch:
        //   IF / PATCHED_IFNOT (isnot=false): notFound -> "E" (required pattern absent).
        //   IFNOT / CONFLICT_IF (isnot=true): match    -> "E" (conflict present).
        //   PATCHED_IF (isnot=true):          match    -> "I" (installed).
        // A required (!isnot) condition whose address is unsafe/out-of-range, or whose
        // value has < 2 space-separated tokens, also yields "E" (WF returns "E"). The
        // $GREP/$XGREP/$FGREP/$FREEAREA macro forms are carved out: an UNresolvable
        // required (!isnot) condition is "E"; an isnot condition is skipped.
        static string CheckIF(ROM rom, List<PatchMetadataCore.PatchParam> prms, string lang)
        {
            foreach (var prm in prms)
            {
                string[] sp = prm.KeyParts;
                if (sp.Length < 1) continue;
                string key = sp[0];
                string addrstring = sp.Length > 1 ? sp[1] : "";
                string value = prm.Value;

                bool isnot;
                if (key == "IF" || key == "PATCHED_IFNOT")
                {
                    isnot = false;
                }
                else if (key == "IFNOT" || key == "PATCHED_IF" || key == "CONFLICT_IF")
                {
                    isnot = true;
                }
                else
                {
                    continue;
                }

                // Carve-out: a $GREP/$XGREP/$FGREP/$FREEAREA macro address can't be
                // resolved here. WF treats an unresolvable REQUIRED (!isnot) address
                // as "E"; an isnot condition with an unresolvable address is skipped.
                if (addrstring.Length > 0 && addrstring[0] == '$'
                    && !(addrstring.Length > 1 && IsNum(addrstring[1])))
                {
                    if (!isnot) return "E";
                    continue;
                }

                uint address;
                if (!TryResolveAddress(rom, addrstring, out address) || !U.isSafetyOffset(address, rom))
                {
                    // WF: unsafe address on a required IF -> "E"; isnot -> continue.
                    if (!isnot) return "E";
                    continue;
                }

                // WF requires >= 2 space-separated value tokens; otherwise "E".
                string[] args = value.Split(' ');
                if (args.Length <= 1)
                {
                    if (!isnot) return "E";
                    continue;
                }

                // notFound = the ROM bytes at `address` do NOT match `need` (WF logic).
                bool notFound = false;
                if (address + (uint)args.Length > (uint)rom.Data.Length)
                {
                    notFound = true; // out of range can never match the need bytes
                }
                else
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        uint data = rom.u8(address + (uint)i);
                        uint need = U.atoi0x(args[i]);
                        if (data != need) { notFound = true; break; }
                    }
                }

                if (!isnot)
                {
                    // IF / PATCHED_IFNOT: a non-match (notFound) is a prerequisite
                    // failure -> "E". A match continues.
                    if (notFound) return "E";
                }
                else
                {
                    // IFNOT / PATCHED_IF / CONFLICT_IF: a MATCH (notFound==false) is
                    // decisive.
                    if (!notFound)
                    {
                        if (key == "PATCHED_IF") return "I"; // installed
                        return "E";                          // IFNOT/CONFLICT_IF conflict
                    }
                }
            }

            // No decisive IF condition fired.
            return "";
        }

        // ---- patch dir resolution (mirrors PatchManagerViewModel.ResolvePatchDirectory) -
        static string ResolvePatchDirectory(string version)
        {
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

            string path = Path.Combine(baseDir, "config", "patch2", version);
            if (Directory.Exists(path)) return path;

            try
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), "config", "patch2", version);
                if (Directory.Exists(path)) return path;
            }
            catch { }

            string dir = baseDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                {
                    path = Path.Combine(dir, "config", "patch2", version);
                    if (Directory.Exists(path)) return path;
                    break;
                }
                string parent = Path.GetDirectoryName(dir) ?? "";
                if (parent == dir) break;
                dir = parent;
            }

            return Path.Combine(baseDir, "config", "patch2", version);
        }

        // ---- small helpers ------------------------------------------------
        static string GetParam(List<PatchMetadataCore.PatchParam> prms, string keyword, string def)
        {
            foreach (var prm in prms)
            {
                if (string.Equals(prm.Keyword, keyword, StringComparison.Ordinal))
                    return prm.Value;
            }
            return def;
        }

        static void AddTextId(HashSet<uint> ids, uint id)
        {
            if (ids == null) return;
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }

        static void AddSongId(HashSet<uint> songIds, uint id)
        {
            if (songIds == null) return;
            if (id == 0 || id >= 0x7FFF) return;
            songIds.Add(id);
        }

        static bool IsNum(char c) => c >= '0' && c <= '9';

        static bool StringBool(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            v = v.Trim();
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
        }

        // Parse the leading decimal run of a "{n}..." key suffix (mirrors U.atoi
        // on key.Substring(1) — WF's MakeTextIndexes uses U.atoi which reads the
        // leading decimal digits).
        static uint AtoiPrefix(string s)
        {
            uint v = 0;
            foreach (char c in s)
            {
                if (c < '0' || c > '9') break;
                v = v * 10 + (uint)(c - '0');
            }
            return v;
        }
    }
}
