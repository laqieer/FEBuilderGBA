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
        /// Returns false (and empty/zero outs) when the trimmed/lower-cased filter does
        /// NOT start with <c>hardcoding_</c>, so the caller falls through to the substring
        /// path. Never throws.
        /// </summary>
        public static bool TryParseHardCodingToken(string filter, out string typeNameUpper, out uint value)
        {
            typeNameUpper = "";
            value = 0;
            if (string.IsNullOrEmpty(filter)) return false;

            string f = filter.Trim().ToLowerInvariant();
            if (f.IndexOf("hardcoding_", StringComparison.Ordinal) != 0) return false;

            // U.cut / U.skip are the same primitives WF uses.
            typeNameUpper = U.cut(f, "hardcoding_", "=").ToUpperInvariant();
            value = U.atoh(U.skip(f, "="));
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
