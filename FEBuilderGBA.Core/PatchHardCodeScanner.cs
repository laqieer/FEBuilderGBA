// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side patch-scan hardcode detector (#1035).
//
// Ports ONLY the patch-scan part of WinForms PatchForm.MakeHardCodeWarning
// (FEBuilderGBA/PatchForm.cs ~:9594):
//   - ScanPatchs(path)          (~:107)  — enumerate PATCH_*.txt recursively.
//   - LoadPatch                 (~:247)  — parse into a Param dict (language
//                                          resolved CleanupKey).
//   - isCanonicalSkip(patch)    (~:6889) — CANONICAL_SKIP=true short-circuit.
//   - CheckIFFast/CheckIF       (~:4385) — IF/IFNOT/PATCHED_IF/PATCHED_IFNOT/
//                                          CONFLICT_IF tri-state gate. A patch is
//                                          skipped only when the gate returns "E".
//   - GetAddrPatchAddressToValue8Fast (~:9560) — read the ADDRESS-param 8-bit id.
//
// This is NOT the full ASM/MAP symbol pipeline (GetAsmMapFile / SearchNear),
// which is tracked separately by #1026. Hardcode warnings sourced from
// disassembled ASM references are out of scope here.
//
// Every ROM read is bounds/EOF guarded; the scanner never throws. No CoreState
// reads for the ROM/safety paths — the ROM is always passed explicitly so it
// works in headless tests where CoreState.ROM may be a different instance.
using System;
using System.Collections.Generic;
using System.IO;
// Unify the scanner's patch record onto the public WinForms-mirror type so the
// #1261 producer can consume ScanPatchs/LoadPatch results and iterate patch.Param
// without a private nested type. The scanner only sets PatchFileName + Param and
// reads only those two fields, so the existing ScanHardCodes behaviour is unchanged.
using PatchSt = FEBuilderGBA.PatchInstallCore.PatchSt;

namespace FEBuilderGBA
{
    public static class PatchHardCodeScanner
    {

        /// <summary>
        /// Scan all PATCH_*.txt files for the loaded ROM version and populate the
        /// three hardcode lookup arrays (each must be length 256, keyed by id).
        /// Ports WinForms PatchForm.MakeHardCodeWarning faithfully:
        ///   skip if isCanonicalSkip, skip if CheckIF == "E", then for
        ///   ADDRESS_TYPE in {UNIT,CLASS,ITEM} read the 8-bit id and (if != 0 and
        ///   in range) set the matching array slot.
        /// Read-only: never mutates the ROM. Never throws.
        /// </summary>
        public static void ScanHardCodes(ROM rom, bool[] unit, bool[] cls, bool[] item)
        {
            if (rom == null || rom.Data == null) return;
            if (unit == null || cls == null || item == null) return;

            string version = SafeVersionFolder(rom);
            if (version == "") return;

            string patchDir = ResolvePatchDirectory(version);
            string lang = CoreState.Language ?? "en";

            List<PatchSt> patchs = ScanPatchs(rom, patchDir, lang);
            foreach (PatchSt patch in patchs)
            {
                if (isCanonicalSkip(patch))
                {
                    continue;
                }
                string checkIF = CheckIF(rom, patch);
                if (checkIF == "E")
                {
                    continue;
                }

                string addressType = U.at(patch.Param, "ADDRESS_TYPE");
                if (addressType == "UNIT")
                {
                    uint id = GetAddrPatchAddressToValue8Fast(rom, patch);
                    if (id == 0) continue;
                    if (id < unit.Length) unit[id] = true;
                }
                else if (addressType == "CLASS")
                {
                    uint id = GetAddrPatchAddressToValue8Fast(rom, patch);
                    if (id == 0) continue;
                    if (id < cls.Length) cls[id] = true;
                }
                else if (addressType == "ITEM")
                {
                    uint id = GetAddrPatchAddressToValue8Fast(rom, patch);
                    if (id == 0) continue;
                    if (id < item.Length) item[id] = true;
                }
            }
        }

        /// <summary>
        /// Resolve the config/patch2/{version} directory: CoreState.BaseDirectory,
        /// then the current dir, then walk up to a repo root (.git) for dev runs.
        /// Mirrors the Avalonia PatchManagerViewModel.ResolvePatchDirectory walk so
        /// headless tests find the submodule checkout.
        /// </summary>
        public static string ResolvePatchDirectory(string version)
        {
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(CoreState.BaseDirectory)) roots.Add(CoreState.BaseDirectory);
            roots.Add(AppContext.BaseDirectory);
            try { roots.Add(Directory.GetCurrentDirectory()); } catch { }

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string path = Path.Combine(root, "config", "patch2", version);
                if (Directory.Exists(path)) return path;
            }

            // Development: walk up from each root to a repo root and look there.
            foreach (string root in roots)
            {
                string dir = root;
                for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
                {
                    try
                    {
                        if (Directory.Exists(Path.Combine(dir, ".git")) ||
                            File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                        {
                            string path = Path.Combine(dir, "config", "patch2", version);
                            if (Directory.Exists(path)) return path;
                            break;
                        }
                    }
                    catch { }
                    string parent = Path.GetDirectoryName(dir) ?? "";
                    if (parent == dir) break;
                    dir = parent;
                }
            }

            string fallbackRoot = !string.IsNullOrEmpty(CoreState.BaseDirectory)
                ? CoreState.BaseDirectory : AppContext.BaseDirectory;
            return Path.Combine(fallbackRoot ?? "", "config", "patch2", version);
        }

        static string SafeVersionFolder(ROM rom)
        {
            try
            {
                if (rom.RomInfo == null) return "";
                if (rom.RomInfo.version == 0) return ""; // WF ScanPatchs returns empty for version 0.
                return rom.RomInfo.VersionToFilename ?? "";
            }
            catch { return ""; }
        }

        // ---- ScanPatchs (~:107) -------------------------------------------------
        // PUBLIC for the #1261 ROM-rebuild producer (s2pf-1): the producer's
        // MakePatchStructDataListCore orchestrator calls this with the rom + the
        // ResolvePatchDirectory(version) path + language. Body unchanged.
        public static List<PatchSt> ScanPatchs(ROM rom, string path, string lang)
        {
            var patchs = new List<PatchSt>();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return patchs;

            string[] files;
            try
            {
                files = Directory.GetFiles(path, "PATCH_*.txt", SearchOption.AllDirectories);
            }
            catch
            {
                return patchs;
            }

            foreach (string fullfilename in files)
            {
                PatchSt patch = LoadPatch(rom, fullfilename, lang);
                if (patch == null) continue;
                patchs.Add(patch);
            }
            return patchs;
        }

        // ---- LoadPatch (~:247) --------------------------------------------------
        // PUBLIC for the #1261 producer (s2pf-1). Body unchanged.
        public static PatchSt LoadPatch(ROM rom, string fullfilename, string lang)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(fullfilename);
            }
            catch
            {
                return null;
            }

            bool canSecondLanguageEnglish = U.CanSecondLanguageEnglish(lang);

            // PatchInstallCore.PatchSt does not default-init Param (the former private
            // nested record did), so initialize it explicitly to preserve behaviour.
            var p = new PatchSt { PatchFileName = fullfilename, Param = new Dictionary<string, string>() };

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                // Use the rom-aware OtherLangLine overload (U.cs:851) so the
                // {J}/{U} language-line filter reads the EXPLICIT rom passed to
                // ScanHardCodes — NOT the ambient CoreState.ROM (which may be a
                // different instance in headless tests). Honors this file's
                // "no CoreState ROM reads" header guarantee.
                if (U.IsComment(line) || U.OtherLangLine(line, rom))
                {
                    continue;
                }
                line = U.ClipComment(line);
                line = line.Trim();

                int sep = line.IndexOf('=');
                if (sep < 0)
                {
                    continue;
                }
                string key = line.Substring(0, sep);
                string value = line.Substring(sep + 1);

                key = CleanupKey(key, lang, canSecondLanguageEnglish, p);
                if (key == "")
                {
                    continue;
                }

                p.Param[key] = value;
            }

            // WF returns null when there is no TYPE (not a real patch).
            string type = U.at(p.Param, "TYPE");
            if (type == "")
            {
                return null;
            }
            return p;
        }

        // ---- CleanupKey (~:211) -------------------------------------------------
        static string CleanupKey(string key, string lang, bool canSecondLanguageEnglish, PatchSt patch)
        {
            if (key.Length < 3)
            {
                return key;
            }
            if (key[key.Length - 3] != '.')
            {
                return key;
            }
            string k = key.Substring(key.Length - 2);
            if (k == lang)
            {
                return key.Substring(0, key.Length - 3);
            }

            if (canSecondLanguageEnglish)
            {
                if (k == "en")
                {
                    string ret_key = key.Substring(0, key.Length - 3);
                    if (!patch.Param.ContainsKey(ret_key))
                    {
                        return ret_key;
                    }
                }
            }

            return "";
        }

        // ---- isCanonicalSkip (~:6889) ------------------------------------------
        // PUBLIC for the #1261 producer (s2pf-1). Body unchanged.
        public static bool isCanonicalSkip(PatchSt patch)
        {
            string v = U.at(patch.Param, "CANONICAL_SKIP", "0");
            return U.stringbool(v);
        }

        // ---- CheckIF (~:4395) ---------------------------------------------------
        // Faithful port of the tri-state gate. Returns "E" (excluded / prereq
        // missing), "I" (installed), or "" (passes). MakeHardCodeWarning only
        // skips on "E". The WinForms CacheCheckIF side-table is irrelevant to the
        // gate result, so it is intentionally omitted here.
        // PUBLIC for the #1261 producer (s2pf-1): the orchestrator gates patches with
        // this (the WF MakePatchStructDataList path uses CheckIFFast, whose only
        // difference is the CacheCheckIF side-table — irrelevant to the gate result,
        // already documented as intentionally omitted above). Body unchanged.
        public static string CheckIF(ROM rom, PatchSt patch)
        {
            foreach (var pair in patch.Param)
            {
                string[] sp = pair.Key.Split(':');
                string key = sp[0];
                string addrstring = U.at(sp, 1);
                string value = pair.Value;

                bool isnot = false;
                if (key != "IF" && key != "PATCHED_IFNOT")
                {
                    if (key != "IFNOT" && key != "PATCHED_IF" && key != "CONFLICT_IF")
                    {
                        continue;
                    }
                    isnot = true;
                }

                string basedir = Path.GetDirectoryName(patch.PatchFileName) ?? "";
                uint address = convertBinAddressString(rom, addrstring, basedir);
                if (!U.isSafetyOffset(address, rom))
                {
                    if (!isnot)
                    {
                        return "E";
                    }
                    continue;
                }

                string[] args = value.Split(' ');
                if (args.Length <= 1)
                {
                    return "E";
                }

                uint[] data = new uint[args.Length];
                bool readOk = true;
                for (int i = 0; i < args.Length; i++)
                {
                    uint a = address + (uint)i;
                    if (!U.isSafetyOffset(a, rom)) { readOk = false; break; }
                    data[i] = rom.u8(a);
                }
                if (!readOk)
                {
                    // Reading the comparison window ran off the ROM. For a positive
                    // condition this means the prereq cannot be satisfied (= "E");
                    // for an inverted one it is treated as "not matching" (continue).
                    if (!isnot) return "E";
                    continue;
                }

                uint[] need = new uint[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    need[i] = U.atoi0x(args[i]);
                }

                bool notFound = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (data[i] != need[i])
                    {
                        notFound = true;
                        break;
                    }
                }

                if (isnot)
                {
                    if (notFound == false)
                    {
                        if (key == "PATCHED_IF")
                        {
                            return "I";
                        }
                        return "E";
                    }
                }
                else
                {
                    if (notFound == true)
                    {
                        if (key == "PATCHED_IF")
                        {
                            return "I";
                        }
                        return "E";
                    }
                }
            }

            return "";
        }

        // ---- convertBinAddressString (~:3000) ----------------------------------
        // Ports the address-resolution forms used by CheckIF (appnedSize == 0,
        // start_offset == 0x100). Plain addresses + $pointer + the GREP family +
        // P32. Macro forms that need WinForms-only helpers ($FREEAREA, TEXTID,
        // EndWeaponDebuffTable*, XGREP) resolve to NOT_FOUND, which the safety
        // check converts to the "E" skip for a positive condition — matching the
        // WinForms outcome that those patches are not applicable.
        static uint convertBinAddressString(ROM rom, string addrstring, string basedir)
        {
            const uint start_offset = 0x100;

            if (string.IsNullOrEmpty(addrstring))
            {
                return U.NOT_FOUND;
            }

            if (addrstring[0] != '$')
            {
                // Plain numeric address.
                return U.toOffset(U.atoi0x(addrstring));
            }

            string value = addrstring.Substring(1);
            if (value.Length == 0) return U.NOT_FOUND;

            if (U.isnum(value[0]))
            {
                // $0x123 -> pointer dereference.
                uint addr = U.toOffset(U.atoi0x(value));
                if (!U.isSafetyOffset(addr, rom))
                {
                    return U.NOT_FOUND;
                }
                return rom.p32(addr);
            }

            if (value == "FREEAREA")
            {
                // CheckIF passes appnedSize == 0 -> WF returns 0 (unsafe offset).
                return 0;
            }

            // GREP / FGREP family (XGREP needs a mask builder not ported here).
            var m = RegexCache.Match(value, @"^(F|X)?GREP([0-9]+)(ENDA|END)?\+?([0-9]+)? ");
            if (m.Groups.Count >= 5 && m.Success)
            {
                if (m.Groups[1].Value == "X")
                {
                    // XGREP unsupported here -> NOT_FOUND.
                    return U.NOT_FOUND;
                }

                uint align = U.atoi(m.Groups[2].Value);
                uint skip = U.atoi(m.Groups[4].Value);
                byte[] need = MakeGrepData(value, basedir, m.Groups[1].Value == "F");
                if (need == null || need.Length == 0) return U.NOT_FOUND;

                if (m.Groups[3].Value == "ENDA")
                {
                    return U.GrepEnd(rom.Data, need, start_offset, 0, align, skip, false);
                }
                else if (m.Groups[3].Value == "END")
                {
                    return U.GrepEnd(rom.Data, need, start_offset, 0, align, skip, true);
                }
                else
                {
                    return U.Grep(rom.Data, need, start_offset, 0, align);
                }
            }

            if (value.IndexOf("GREP_ENABLE_POINTER ") == 0)
            {
                return U.GrepEnablePointer(rom.Data, start_offset, 0);
            }

            if (value.IndexOf("P32 ") == 0)
            {
                return ReadPointer(rom, value, 0);
            }
            if (value.IndexOf("P32+4 ") == 0)
            {
                return ReadPointer(rom, value, 4);
            }

            // TEXTID / EndWeaponDebuffTable* depend on WinForms-only helpers.
            return U.NOT_FOUND;
        }

        static byte[] MakeGrepData(string value, string basedir, bool isFile)
        {
            string[] sp = value.Split(' ');
            var grepdata = new List<byte>();
            for (int i = 1; i < sp.Length; i++)
            {
                if (sp[i].Length == 0) continue;
                if (sp[i][0] == '$')
                {
                    // Macro tokens inside GREP (e.g. file inclusions) are not
                    // ported; treat as not-found so the gate excludes safely.
                    return null;
                }
                grepdata.Add((byte)U.atoi0x(sp[i]));
            }
            // The FGREP file-inclusion form is not ported; only inline byte
            // patterns are honored. (isFile/basedir reserved for parity.)
            _ = isFile; _ = basedir;
            return grepdata.ToArray();
        }

        static uint ReadPointer(ROM rom, string value, uint plus)
        {
            string[] sp = value.Split(' ');
            if (sp.Length < 2) return U.NOT_FOUND;
            uint p = U.atoi0x(sp[1]);
            if (!U.isSafetyOffset(p, rom)) return U.NOT_FOUND;

            uint a = p + plus;
            if (!U.isSafetyOffset(a + 3, rom)) return U.NOT_FOUND;
            uint pp = rom.u32(a);
            if (!U.isSafetyPointer(pp, rom)) return U.NOT_FOUND;
            if (U.IsValueOdd(pp)) pp--;
            return U.toOffset(pp);
        }

        // ---- GetAddrPatchAddressToValue8Fast (~:9560) --------------------------
        // WinForms GetAddrPatchAddressFast reads the ADDRESS param with U.atoi0x,
        // which parses the FIRST token of a possibly multi-token ADDRESS (e.g.
        // "0x037B86 0x33270" -> 0x037B86). The real UNIT/CLASS/ITEM patches use
        // plain ROM offsets, where toOffset is a no-op. Per the #1035 brief we
        // additionally toOffset() the result so a pointer-style 0x08...... ADDRESS
        // resolves to its ROM offset (WinForms would reject it as out of range
        // and yield id 0); for every real patch the behavior is identical.
        static uint GetAddrPatchAddressToValue8Fast(ROM rom, PatchSt patch)
        {
            string address_string = U.at(patch.Param, "ADDRESS");
            uint addr = U.toOffset(U.atoi0x(address_string));
            if (!U.isSafetyOffset(addr, rom))
            {
                return 0;
            }
            return rom.u8(addr);
        }
    }
}
