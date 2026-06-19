using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Thrown when a patch cannot be loaded or applied: malformed text, an
    /// unsupported keyword/macro-address, an unsafe write target, or a ROM that
    /// cannot be grown to fit the patch.
    /// Carries a localized message (via <see cref="R.Error"/>) and never raises a
    /// dialog — callers decide how to surface it.
    /// </summary>
    public class PatchInstallException : Exception
    {
        public PatchInstallException(string message) : base(message) { }
    }

    /// <summary>
    /// Cross-platform patch load + apply engine extracted from WinForms PatchForm.
    /// SLICE 1 of issue #1248: handles the CustomBuild auto-install path only.
    ///
    /// A CustomBuild patch (produced by <see cref="DiffToolCore.MakeDiff"/>) contains
    /// ONLY literal-offset BIN/BINF byte-diffs — no macros ($), GREP, FREEAREA,
    /// pointer kinds, COPY/SLIDE/JUMP/TEXT/etc. This engine therefore implements just
    /// that literal-offset load+apply path so it is fully provable headless with no
    /// ROM/SkillSystems environment.
    ///
    /// Any keyword or address form outside the CustomBuild subset is rejected LOUDLY
    /// (<see cref="PatchInstallException"/>) so a non-CustomBuild patch fails fast
    /// rather than silently mis-applying. The full UpdatePatch merge engine and the
    /// MargeAndUpdate orchestration are separate later slices.
    ///
    /// Mirrors PatchForm.LoadPatch / CleanupKey (text parse) and
    /// writeBIN/BinWriteFile/makeBinModInnerReadFile/WriteBB (the BIN/BINF write path).
    /// </summary>
    public static class PatchInstallCore
    {
        /// <summary>
        /// Parsed patch: file path, display name, plus the raw key/value Param map.
        /// Mirrors PatchForm.PatchSt.
        /// </summary>
        public class PatchSt
        {
            public string PatchFileName;
            public string Name;
            public string SearchData; // search index text
            public DateTime Date;

            public Dictionary<string, string> Param;
        }

        /// <summary>
        /// Read a PATCH_*.txt file and parse it into a <see cref="PatchSt"/>.
        /// Mirrors PatchForm.LoadPatch(string,bool) with isScanOnly=false.
        /// Returns null when the text has no TYPE= line (not a patch).
        /// </summary>
        public static PatchSt LoadPatch(string fullfilename)
        {
            string[] lines = File.ReadAllLines(fullfilename);
            return LoadPatch(lines, fullfilename);
        }

        /// <summary>
        /// Parse already-read patch text into a <see cref="PatchSt"/>.
        /// Mirrors PatchForm.LoadPatch(string[],string,bool) with isScanOnly=false.
        /// Language-suffixed keys (NAME.ja / NAME.en) are resolved against
        /// <see cref="CoreState.Language"/>, exactly like the WinForms path.
        /// </summary>
        public static PatchSt LoadPatch(string[] lines, string fullfilename)
        {
            string lang = CoreState.Language;
            bool canSecondLanguageEnglish = U.CanSecondLanguageEnglish(lang);

            PatchSt p = new PatchSt();
            p.PatchFileName = fullfilename;
            p.Param = new Dictionary<string, string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.IsComment(line) || U.OtherLangLine(line))
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

            string type = U.at(p.Param, "TYPE");
            if (type == "")
            {
                return null;
            }

            // PATCH_ prefix is 6 chars; mirror PatchForm's Substring(6).
            string stem = Path.GetFileNameWithoutExtension(fullfilename) ?? "";
            string search_filename = stem.Length >= 6 ? stem.Substring(6) : stem;

            string name = U.at(p.Param, "NAME");
            if (name == "")
            {
                name = search_filename;
            }
            p.Name = name;

            p.SearchData = name + "\t" + search_filename + "\t"
                + U.at(p.Param, "INFO") + "\t" + U.at(p.Param, "AUTHOR") + "\t"
                + U.at(p.Param, "TAG") + "\t" + U.at(p.Param, "HINT");
            // WF uses U.GetFileDateLastWriteTime (a WinForms-only helper); inline its
            // body here. File.Exists guards parse-from-text callers with no real file.
            p.Date = File.Exists(fullfilename) ? File.GetLastWriteTime(fullfilename) : DateTime.MinValue;

            return p;
        }

        /// <summary>
        /// Resolve a language-suffixed key (e.g. "NAME.ja") against the active
        /// language. Returns the bare key when it matches the language (or the
        /// English fallback when permitted and the first language did not already
        /// set it), "" when it is for a different language, and the key unchanged
        /// when it carries no ".xx" suffix. Mirrors PatchForm.CleanupKey.
        /// </summary>
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

        /// <summary>
        /// Apply a CustomBuild patch to <paramref name="rom"/>, recording the
        /// pre-write bytes into <paramref name="undo"/> so the install can be rolled
        /// back. Iterates the Param map and, for each BIN/BINF key, reads the sidecar
        /// from the patch directory, resolves the LITERAL offset, runs the
        /// zero-address guard, grows the ROM if the write extends past its end, then
        /// writes the bytes.
        ///
        /// IMPORTANT: BIN/BINF here means a LITERAL-offset write only. Any other
        /// keyword, or a macro address ($...), throws <see cref="PatchInstallException"/>
        /// so a non-CustomBuild patch fails loudly rather than silently mis-applying.
        ///
        /// For undo to be meaningful the supplied ROM must be the one Undo snapshots
        /// from — i.e. CoreState.ROM — because the WinForms Undo machinery reads the
        /// pre-write bytes from CoreState.ROM at write time and rolls back into it.
        ///
        /// <paramref name="vanillaRom"/> is the unmodified ROM bytes used by the
        /// optional UNINSTALL keyword to copy the original bytes back; it is null for
        /// a plain install and only needed when the patch carries UNINSTALL lines.
        /// </summary>
        public static void ApplyPatch(PatchSt patch, ROM rom, Undo.UndoData undo, byte[] vanillaRom = null)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));

            string basedir = Path.GetDirectoryName(patch.PatchFileName) ?? "";

            foreach (var pair in patch.Param)
            {
                string[] sp = pair.Key.Split(':');
                string keyword = sp[0];
                string value = pair.Value;

                switch (keyword)
                {
                    // Non-write metadata keys — skip (mirrors WF, which only acts on
                    // keyword-matched lines and ignores the rest).
                    case "TYPE":
                    case "NAME":
                    case "INFO":
                    case "AUTHOR":
                    case "TAG":
                    case "HINT":
                    case "DATE":
                    case "URL":
                    case "PATCHED_IF":
                        continue;

                    case "BIN":
                    case "BINF":
                        ApplyBinFile(sp, Path.Combine(basedir, value), rom, undo);
                        break;

                    case "UNINSTALL":
                        // Optional clean extension: copy bytes back from a supplied
                        // vanilla ROM. The "value" is a hex length (matching WF, which
                        // takes the length from Path.GetFileName of the sidecar arg).
                        ApplyUninstall(sp, value, rom, undo, vanillaRom);
                        break;

                    default:
                        throw new PatchInstallException(R.Error(
                            "This keyword is not supported by the CustomBuild patch installer: {0}",
                            keyword));
                }
            }
        }

        /// <summary>
        /// Apply a single BIN/BINF literal-offset write: read the sidecar, resolve
        /// the literal offset, and write. Mirrors PatchForm.BinWriteFile +
        /// makeBinModInnerReadFile (literal branch only).
        /// </summary>
        static void ApplyBinFile(string[] sp, string filename, ROM rom, Undo.UndoData undo)
        {
            // sp[1] is the destination address; a CustomBuild BINF emits exactly two
            // tokens ("BINF" and "0x...."). A third token (sp[2]) would request a
            // ChangeAddress relocation, which is out of the CustomBuild subset.
            // Reject ANY third token unconditionally (loud fail) — a numeric check
            // would let a non-hex relocation token like "$TEXTID" parse to 0 and slip
            // through, silently ignoring the relocation it requested.
            if (sp.Length > 2)
            {
                throw new PatchInstallException(R.Error(
                    "Address relocation is not supported by the CustomBuild patch installer: {0}",
                    sp[0] + ":" + U.at(sp, 1)));
            }

            string addrstring = U.at(sp, 1);
            uint addr = ResolveLiteralOffset(addrstring);

            byte[] b = File.ReadAllBytes(filename);
            WriteBB(addr, b, rom, undo);
        }

        /// <summary>
        /// Optional UNINSTALL keyword: copy <c>length</c> bytes from the supplied
        /// vanilla ROM back into the working ROM at the literal destination address,
        /// undoing a prior install. Mirrors PatchForm.BinUninstall, but the vanilla
        /// ROM is supplied explicitly (no FindOrignalROM lookup / no caching).
        /// The length is taken from <paramref name="lengthHex"/> (the patch value),
        /// matching WF which reads it from the sidecar file name.
        /// </summary>
        static void ApplyUninstall(string[] sp, string lengthHex, ROM rom, Undo.UndoData undo, byte[] vanilla)
        {
            if (vanilla == null)
            {
                throw new PatchInstallException(R.Error(
                    "An unmodified ROM is required to uninstall, but none was supplied."));
            }

            // Bound-check directly against the vanilla ROM (U.isSafetyOffset reads
            // CoreState.ROM, whose length may differ from the vanilla source).
            uint addr = U.atoi0x(U.at(sp, 1));
            if (addr < 0x00000200 || addr >= 0x02000000 || addr >= vanilla.Length)
            {
                throw new PatchInstallException(R.Error(
                    "The uninstall destination address is invalid dest:{0}", U.at(sp, 1)));
            }

            uint length = U.atoi0x(lengthHex);
            byte[] s = U.getBinaryData(vanilla, addr, length);
            WriteBB(addr, s, rom, undo);
        }

        /// <summary>
        /// Resolve a CustomBuild address string to a ROM offset. Only the literal form
        /// is accepted (mirrors PatchForm.convertBinAddressString's first branch:
        /// U.toOffset(U.atoi0x(...))). A leading '$' (macro/pointer) is rejected as
        /// outside the CustomBuild subset.
        /// </summary>
        static uint ResolveLiteralOffset(string addrstring)
        {
            if (addrstring == "")
            {
                throw new PatchInstallException(R.Error("The patch is missing a destination address."));
            }
            if (addrstring[0] == '$')
            {
                throw new PatchInstallException(R.Error(
                    "Macro/pointer addresses are not supported by the CustomBuild patch installer: {0}",
                    addrstring));
            }
            return U.toOffset(U.atoi0x(addrstring));
        }

        /// <summary>
        /// Write <paramref name="b"/> at <paramref name="addr"/> into the ROM with
        /// the zero-address guard and auto-resize. Mirrors PatchForm.WriteBB.
        /// The resize happens BEFORE the write so the undo snapshot (taken inside
        /// write_range) captures the freshly-grown, zero-filled region as the
        /// pre-write state — matching the WinForms ordering exactly.
        /// </summary>
        static void WriteBB(uint addr, byte[] b, ROM rom, Undo.UndoData undo)
        {
            // WF U.CheckZeroAddressWrite: reject NOT_FOUND or addr <= 0x100.
            if (addr == U.NOT_FOUND || addr <= 0x100)
            {
                throw new PatchInstallException(R.Error(
                    "This address is dangerous to write to: {0}", U.To0xHexString(addr)));
            }

            if (addr + b.Length > rom.Data.Length)
            {
                bool isResizeSuccess = rom.write_resize_data((uint)(addr + b.Length));
                if (isResizeSuccess == false)
                {
                    throw new PatchInstallException(R.Error(
                        "Cannot allocate a region larger than 32MB(0x02000000). requested size:{0}",
                        U.To0xHexString((uint)(addr + b.Length))));
                }
            }

            rom.write_range(addr, b, undo);
        }
    }
}
