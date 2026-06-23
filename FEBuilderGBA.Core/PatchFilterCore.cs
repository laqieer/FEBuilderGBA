// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side Patch Manager filter helper (#1376).
//
// The Avalonia Patch Manager seeds its filter box with synthetic tokens from the
// editors' [HardCoding] links — "HARDCODING_{UNIT|CLASS|ITEM}=NN" — and the
// installed-only token "!". WinForms PatchForm.MakeFiltedPatchs (PatchForm.cs:143)
// special-cases both BEFORE its substring search; the Avalonia ApplyFilter did
// substring-only matching, so those tokens matched nothing (issue #1376).
//
// This helper ports ONLY the token-recognition + per-patch predicate parts of
// MakeFiltedPatchs, REUSING the already-ported gate logic in PatchHardCodeScanner
// (LoadPatch / isCanonicalSkip / CheckIF / EaBinInstallStatus / IsHardCodingPatch):
//   - TryParseHardCodingToken : mirrors the WF token parse
//       target_typename = cut(filter,"hardcoding_","=").ToUpper();
//       target_value     = atoh(skip(filter,"="));
//   - IsHardCodingTokenMatch  : LoadPatch(file) -> PatchHardCodeScanner.IsHardCodingPatch
//       (= WF isFilterHardCoding, PatchForm.cs:9569).
//   - IsInstalledForFilter    : LoadPatch(file) -> EaBinInstallStatus == Installed
//       (= WF IsInstalled, PatchForm.cs:199 — the "!" token).
//
// READ-ONLY: never mutates the ROM. Never throws. The ROM is always passed
// explicitly so it works headless where CoreState.ROM may be a different instance.
//
// LANGUAGE: the scanner's LoadPatch expects the WinForms-style language value
// (ja/en/zh) for its CleanupKey, NOT PatchMetadataCore.GetLanguageSuffix() (which
// returns "" for Japanese and would flip CanSecondLanguageEnglish on). Callers
// pass CoreState.Language-style values; ScanLang() normalizes null -> "en",
// matching PatchHardCodeScanner.ScanHardCodes' own internal choice.
using System;

namespace FEBuilderGBA
{
    public static class PatchFilterCore
    {
        /// <summary>
        /// The WinForms-style language value the patch scanner's CleanupKey expects
        /// (ja/en/zh). Mirrors PatchHardCodeScanner.ScanHardCodes' "CoreState.Language ?? en".
        /// </summary>
        public static string ScanLang(string lang)
        {
            return string.IsNullOrEmpty(lang) ? "en" : lang;
        }

        /// <summary>
        /// Recognize the Patch Manager <c>hardcoding_{type}=NN</c> filter token and
        /// extract its target type name (upper-cased: <c>UNIT</c>/<c>CLASS</c>/<c>ITEM</c>)
        /// and 8-bit hex value. Mirrors WinForms MakeFiltedPatchs (PatchForm.cs:167-170):
        /// the filter is lower-cased, then if it begins with <c>hardcoding_</c>,
        /// <c>typeName = cut(filter,"hardcoding_","=").ToUpper()</c> and
        /// <c>value = atoh(skip(filter,"="))</c>.
        /// <para>
        /// The seeded token is plain ASCII (e.g. <c>HARDCODING_UNIT=01</c>), so only a
        /// simple lower-case is needed — the WF migemo/narrow-font transforms in
        /// <c>U.CleanupFindString</c> are irrelevant to this synthetic token.
        /// </para>
        /// Returns true ONLY for a FULLY-FORMED token — it begins with <c>hardcoding_</c>,
        /// has an <c>=</c> separator, a RECOGNIZED type (<c>UNIT</c>/<c>CLASS</c>/<c>ITEM</c>),
        /// and a value whose first character is a hex digit. Otherwise returns false (with
        /// empty/zero outs) so the caller falls through to the normal substring path. This
        /// keeps a partially-typed or malformed <c>hardcoding_</c> input (mid-keystroke
        /// <c>hardcoding_</c> / <c>hardcoding_unit</c>, an empty value <c>hardcoding_unit=</c>,
        /// a bad type <c>hardcoding_xyz=01</c>, or non-hex <c>hardcoding_unit=zz</c>) from
        /// triggering the per-patch LoadPatch/ROM scan and from forcing an empty result.
        /// A complete <c>hardcoding_unit=01</c> parses unchanged. Never throws.
        /// </summary>
        public static bool TryParseHardCodingToken(string filter, out string typeNameUpper, out uint value)
        {
            typeNameUpper = "";
            value = 0;
            if (string.IsNullOrEmpty(filter)) return false;

            string f = filter.Trim().ToLowerInvariant();
            if (f.IndexOf("hardcoding_", StringComparison.Ordinal) != 0) return false;

            // Require a '=' separator before taking the hardcoding branch.
            if (f.IndexOf('=', StringComparison.Ordinal) < 0) return false;

            // U.cut / U.skip are the same primitives WF uses.
            string typeLower = U.cut(f, "hardcoding_", "=");
            string type = typeLower.ToUpperInvariant();
            // Only the three real ADDRESS_TYPE values are seeded by the editors.
            if (type != "UNIT" && type != "CLASS" && type != "ITEM") return false;

            // Require a non-empty value whose first char is a hex digit (so "zz"/"" -> fall
            // back to substring, not a forced-empty hardcoding result). atoh truncates at
            // the first non-hex char, matching WF's parse for the kept hex prefix.
            string valueStr = U.skip(f, "=");
            if (valueStr.Length == 0 || !U.ishex(valueStr[0])) return false;

            typeNameUpper = type;
            value = U.atoh(valueStr);
            return true;
        }

        /// <summary>True iff the filter, trimmed and lower-cased, is exactly the WF
        /// installed-only token <c>!</c>.</summary>
        public static bool IsInstalledOnlyToken(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return false;
            return filter.Trim() == "!";
        }

        /// <summary>
        /// Does the patch file at <paramref name="patchFilePath"/> hard-code
        /// <paramref name="value"/> for ADDRESS_TYPE <paramref name="typeNameUpper"/>?
        /// Loads the patch via <see cref="PatchHardCodeScanner.LoadPatch"/> (lang
        /// normalized by <see cref="ScanLang"/>) and applies the WF
        /// <c>isFilterHardCoding</c> predicate (<see cref="PatchHardCodeScanner.IsHardCodingPatch"/>).
        /// Returns false on any null/unloadable input. Read-only; never throws.
        /// </summary>
        public static bool IsHardCodingTokenMatch(ROM rom, string patchFilePath, string lang,
            uint value, string typeNameUpper)
        {
            if (rom == null || string.IsNullOrEmpty(patchFilePath)) return false;
            var patch = PatchHardCodeScanner.LoadPatch(rom, patchFilePath, ScanLang(lang));
            if (patch == null) return false;
            return PatchHardCodeScanner.IsHardCodingPatch(rom, patch, value, typeNameUpper);
        }

        /// <summary>
        /// Is the patch file at <paramref name="patchFilePath"/> installed in
        /// <paramref name="rom"/>? Mirrors WinForms <c>PatchForm.IsInstalled</c>
        /// (PatchForm.cs:199) for the Patch Manager <c>!</c> token, via the faithful
        /// Core port <see cref="PatchHardCodeScanner.EaBinInstallStatus"/> — a patch is
        /// "installed" iff some <c>PATCHED_IF</c>/<c>PATCHED_IFNOT</c> install marker
        /// matches the ROM (byte-faithful <c>$GREP</c>/<c>$FGREP</c> included).
        /// <para>
        /// CONSERVATIVE: only a definitive <c>Installed</c> verdict keeps the patch;
        /// <c>Unknown</c> (an un-ported macro signature) is treated as not-installed for
        /// display so the filtered list never shows a patch we cannot prove installed.
        /// </para>
        /// Read-only; never throws.
        /// </summary>
        public static bool IsInstalledForFilter(ROM rom, string patchFilePath, string lang)
        {
            if (rom == null || string.IsNullOrEmpty(patchFilePath)) return false;
            var patch = PatchHardCodeScanner.LoadPatch(rom, patchFilePath, ScanLang(lang));
            if (patch == null) return false;
            return PatchHardCodeScanner.EaBinInstallStatus(rom, patch)
                == PatchHardCodeScanner.InstallStatusEnum.Installed;
        }
    }
}
