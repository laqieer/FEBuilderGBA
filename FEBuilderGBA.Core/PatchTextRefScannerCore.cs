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
// GREP/XGREP/FGREP/P32/TEXTID macro resolution is handled by
// PatchMacroAddressResolverCore (faithful port of PatchForm.convertBinAddressString).
// Remaining carve-outs (return U.NOT_FOUND, never throw):
//   $FREEAREA               — write-time allocator, meaningless read-only
//   $EndWeaponDebuffTable3/4/5 — weapon-debuff-only PatchUtil/Form-bound macros
// The SkillSystems STRUCT text table is covered separately + faithfully by
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

            // Base directory of the patch file (for $FGREP file-content lookups)
            string basedir = Path.GetDirectoryName(patchFile) ?? "";

            // CheckIF tri-state (port of CheckIFFast):
            //   "E" -> prerequisite missing / dangerous addr -> skip ALL types
            //   "I" -> installed
            //   "" -> not installed
            string checkIF = CheckIF(rom, prms, lang, basedir);

            // WF IsMakePatchStructDataListTarget(type, checkIF, isInstallOnly:true, isStructOnly:false):
            //   STRUCT: include unless "E".
            //   non-STRUCT (ADDR): include only when "I".
            if (type == "STRUCT")
            {
                if (checkIF == "E") return;
                ScanStruct(rom, prms, basedir, eventScanReady, tracelist, ids, songIds);
            }
            else // ADDR
            {
                if (checkIF != "I") return;
                ScanAddr(rom, prms, basedir, ids, songIds);
            }
        }

        // ---- ADDR (PatchForm.MakeVarsIDArrayForAddr) ----------------------
        static void ScanAddr(ROM rom, List<PatchMetadataCore.PatchParam> prms,
            string basedir, HashSet<uint> ids, HashSet<uint> songIds)
        {
            string addressType = GetParam(prms, "ADDRESS_TYPE", "");
            if (addressType != "TEXT" && addressType != "SONG") return;

            string addrStr = GetParam(prms, "ADDRESS", "");
            uint addr = ResolveAddress(rom, addrStr, basedir);
            if (addr == U.NOT_FOUND) return;
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
            string basedir, bool eventScanReady, List<uint> tracelist,
            HashSet<uint> ids, HashSet<uint> songIds)
        {
            // Resolve struct base — POINTER (deref) takes priority over ADDRESS.
            uint structAddr;
            string pointerStr = GetParam(prms, "POINTER", "");
            if (pointerStr != "")
            {
                uint structPointer = ResolveAddress(rom, pointerStr, basedir);
                if (structPointer == U.NOT_FOUND) return;
                if (!U.isSafetyOffset(structPointer, rom)) return;
                if (structPointer + 4 > (uint)rom.Data.Length) return;
                structAddr = rom.p32(structPointer);
                if (!U.isSafetyOffset(structAddr, rom)) return;
            }
            else
            {
                string addressStr = GetParam(prms, "ADDRESS", "");
                if (addressStr == "") return;
                structAddr = ResolveAddress(rom, addressStr, basedir);
                if (structAddr == U.NOT_FOUND) return;
                if (!U.isSafetyOffset(structAddr, rom)) return;
            }

            uint datasize = U.atoi0x(GetParam(prms, "DATASIZE", ""));
            if (datasize <= 0) return;

            string datacountStr = GetParam(prms, "DATACOUNT", "");
            uint datacount;
            if (datacountStr.Length > 0 && datacountStr[0] == '$')
            {
                // $GREP-resolved DATACOUNT — resolve via PatchMacroAddressResolverCore,
                // passing structAddr as startOffset (faithful port of WF convertBinAddressString
                // call in MakeVarsIDArrayForStruct: start_offset = struct_address).
                uint resolved = PatchMacroAddressResolverCore.Resolve(rom, datacountStr, basedir, structAddr);
                if (resolved == U.NOT_FOUND) return;
                // WF: if resolved >= struct_address, treat as end address and compute count
                if (resolved >= structAddr)
                    datacount = (uint)Math.Ceiling((resolved - structAddr) / (double)datasize);
                else
                    return;
                if (datacount >= 0xFFFF) return; // WF Debug.Assert guard
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

        // ---- shared address resolution via PatchMacroAddressResolverCore -------
        // Delegates to PatchMacroAddressResolverCore.Resolve for all macro forms
        // ($GREP, $XGREP, $FGREP, $P32, $TEXTID, etc.) as well as plain literals
        // and $0x pointer-deref. Returns U.NOT_FOUND on any failure.
        static uint ResolveAddress(ROM rom, string addrstring, string basedir, uint startOffset = 0x100)
        {
            return PatchMacroAddressResolverCore.Resolve(rom, addrstring, basedir, startOffset);
        }

        // ---- CheckIF tri-state (port of PatchForm.CheckIF) ----------------
        // Faithful port of WinForms PatchForm.CheckIF, now with full macro-address
        // resolution via PatchMacroAddressResolverCore (previously literal/$0x only).
        // Returns "E" (prerequisite missing / dangerous / conflict), "I" (installed),
        // or "" (undetermined). Mirrors WF exactly: per IF-family key, compute
        // `notFound` (the ROM byte-pattern at the address does NOT match the need),
        // then dispatch:
        //   IF / PATCHED_IFNOT (isnot=false): notFound -> "E" (required pattern absent).
        //   IFNOT / CONFLICT_IF (isnot=true): match    -> "E" (conflict present).
        //   PATCHED_IF (isnot=true):          match    -> "I" (installed).
        // A required (!isnot) condition whose address can't be resolved or is unsafe,
        // or whose value has < 2 space-separated tokens, yields "E".
        // $FREEAREA / $EndWeaponDebuffTable3/4/5 (PatchMacroAddressResolverCore carve-outs)
        // return U.NOT_FOUND; required condition -> "E", isnot condition -> skipped.
        static string CheckIF(ROM rom, List<PatchMetadataCore.PatchParam> prms, string lang, string basedir)
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

                uint address = ResolveAddress(rom, addrstring, basedir);
                if (address == U.NOT_FOUND || !U.isSafetyOffset(address, rom))
                {
                    // WF: unsafe/unresolvable address on a required IF -> "E"; isnot -> skip.
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
