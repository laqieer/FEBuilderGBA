// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for PatchFilterCore (#1376) — the Patch Manager filter token helper.
//
// PatchFilterCore ports the token-recognition + per-patch predicate parts of
// WinForms PatchForm.MakeFiltedPatchs:
//   - "HARDCODING_{UNIT|CLASS|ITEM}=NN" -> patches that hard-code that id
//     (= isFilterHardCoding, reusing PatchHardCodeScanner.IsHardCodingPatch).
//   - "!" -> installed-only (= IsInstalled, via EaBinInstallStatus).
//   - everything else -> the caller's substring path (TryParse returns false).
//
// Tests seed temporary PATCH_*.txt files under a temp config/patch2/{ver} dir and
// LoadPatch them through PatchFilterCore, so the real parse + gate + address-read
// path is exercised without depending on the config/patch2 submodule checkout.
using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchFilterCoreTests
    {
        // ---- helpers (mirror PatchHardCodeScannerTests) --------------------

        static ROM MakeFe8uRom(uint idAddr, byte idValue)
        {
            var data = new byte[0x1000000];
            data[idAddr] = idValue;
            var rom = new ROM();
            bool ok = rom.LoadLow("filter-fe8u.gba", data, "BE8E01");
            Assert.True(ok);
            Assert.Equal("FE8U", rom.RomInfo.VersionToFilename);
            return rom;
        }

        static string MakeTempVerDir(out string root, string ver = "FE8U")
        {
            root = Path.Combine(Path.GetTempPath(), "fe_pf_" + Guid.NewGuid().ToString("N"));
            string verDir = Path.Combine(root, "config", "patch2", ver);
            Directory.CreateDirectory(verDir);
            return verDir;
        }

        static string WritePatch(string verDir, string fileNameNoExt, params string[] lines)
        {
            string path = Path.Combine(verDir, "PATCH_" + fileNameNoExt + ".txt");
            File.WriteAllLines(path, lines);
            return path;
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        // ---- TryParseHardCodingToken --------------------------------------

        [Theory]
        [InlineData("HARDCODING_UNIT=01", "UNIT", 0x01u)]
        [InlineData("HARDCODING_CLASS=0C", "CLASS", 0x0Cu)]
        [InlineData("HARDCODING_ITEM=21", "ITEM", 0x21u)]
        [InlineData("hardcoding_unit=ff", "UNIT", 0xFFu)]
        [InlineData("  HARDCODING_UNIT=02  ", "UNIT", 0x02u)] // trimmed
        public void TryParse_ValidToken_ExtractsTypeAndHexValue(string filter, string expType, uint expVal)
        {
            bool ok = PatchFilterCore.TryParseHardCodingToken(filter, out string type, out uint val);
            Assert.True(ok);
            Assert.Equal(expType, type);
            Assert.Equal(expVal, val);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("!")]
        [InlineData("Eirika")]
        [InlineData("skill system")]
        [InlineData("hardcodingunit=01")] // missing underscore
        public void TryParse_NonToken_ReturnsFalse(string filter)
        {
            bool ok = PatchFilterCore.TryParseHardCodingToken(filter, out _, out _);
            Assert.False(ok);
        }

        // Incomplete or malformed tokens (mid-keystroke / typos) must NOT take the
        // hardcoding branch — they fall through to substring filtering, so no per-patch
        // ROM scan and no forced-empty result. Regression for Copilot PR #1384 review.
        [Theory]
        [InlineData("hardcoding_")]        // no '=' yet, no type
        [InlineData("hardcoding_unit")]    // no '=' yet
        [InlineData("HARDCODING_UNIT")]    // no '=' yet (upper)
        [InlineData("hardcoding_=01")]     // '=' present but empty type name
        [InlineData("hardcoding_unit=")]   // recognized type but no value
        [InlineData("hardcoding_xyz=01")]  // unrecognized type
        [InlineData("hardcoding_unit=zz")] // non-hex value
        [InlineData("hardcoding_weapon=05")] // unrecognized type
        public void TryParse_IncompleteOrMalformedToken_ReturnsFalse(string filter)
        {
            bool ok = PatchFilterCore.TryParseHardCodingToken(filter, out _, out _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParse_NullFilter_ReturnsFalse_NoThrow()
        {
            bool ok = PatchFilterCore.TryParseHardCodingToken(null, out _, out _);
            Assert.False(ok);
        }

        // ---- IsInstalledOnlyToken -----------------------------------------

        [Theory]
        [InlineData("!", true)]
        [InlineData(" ! ", true)]
        [InlineData("", false)]
        [InlineData("!!", false)]
        [InlineData("a!", false)]
        public void IsInstalledOnlyToken(string filter, bool expected)
        {
            Assert.Equal(expected, PatchFilterCore.IsInstalledOnlyToken(filter));
        }

        // ---- ScanLang -----------------------------------------------------

        [Theory]
        [InlineData("", "en")]
        [InlineData("ja", "ja")]
        [InlineData("zh", "zh")]
        [InlineData("en", "en")]
        public void ScanLang_NormalizesEmptyToEn_PreservesJa(string input, string expected)
        {
            Assert.Equal(expected, PatchFilterCore.ScanLang(input));
        }

        [Fact]
        public void ScanLang_Null_ReturnsEn()
        {
            Assert.Equal("en", PatchFilterCore.ScanLang(null));
        }

        // ---- IsHardCodingTokenMatch (UNIT/CLASS/ITEM) ---------------------

        [Fact]
        public void HardCodingMatch_Unit_MatchesRightPatch()
        {
            var rom = MakeFe8uRom(0x2000, 0x01);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "EirikaUnit",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT");
                Assert.True(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x01, "UNIT"));
                // wrong id -> no match
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x02, "UNIT"));
                // wrong type -> no match
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x01, "CLASS"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void HardCodingMatch_Class_MatchesRightPatch()
        {
            var rom = MakeFe8uRom(0x2400, 0x0C);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "MyClass",
                    "TYPE=ADDR", "ADDRESS=0x2400", "ADDRESS_TYPE=CLASS");
                Assert.True(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x0C, "CLASS"));
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x0C, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void HardCodingMatch_Item_MatchesRightPatch()
        {
            var rom = MakeFe8uRom(0x2800, 0x21);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "MyItem",
                    "TYPE=ADDR", "ADDRESS=0x2800", "ADDRESS_TYPE=ITEM");
                Assert.True(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x21, "ITEM"));
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x21, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        // ---- gate parity: value 0, CANONICAL_SKIP, IF "E" -----------------

        [Fact]
        public void HardCodingMatch_ValueZero_NeverMatches()
        {
            var rom = MakeFe8uRom(0x2000, 0x00);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "ZeroUnit",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT");
                // value arg 0 -> false (WF value<=0); also id resolves to 0.
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x00, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void HardCodingMatch_CanonicalSkip_Excluded()
        {
            var rom = MakeFe8uRom(0x2000, 0x07);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "Canon",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT", "CANONICAL_SKIP=true");
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x07, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void HardCodingMatch_IfMismatch_Excluded()
        {
            var rom = MakeFe8uRom(0x2000, 0x07);
            rom.Data[0x4000] = 0x00; // need 0xAB -> mismatch -> CheckIF "E"
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "IfMiss",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT", "IF:0x4000=0xAB 0xCD");
                Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x07, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void HardCodingMatch_IfMatch_Included()
        {
            var rom = MakeFe8uRom(0x2000, 0x07);
            rom.Data[0x4000] = 0xAB;
            rom.Data[0x4001] = 0xCD;
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "IfPos",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT", "IF:0x4000=0xAB 0xCD");
                Assert.True(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", 0x07, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        // ---- end-to-end token parse + match -------------------------------

        [Fact]
        public void ParseThenMatch_EndToEnd_Unit()
        {
            var rom = MakeFe8uRom(0x2000, 0x01);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "EndToEnd",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT");
                Assert.True(PatchFilterCore.TryParseHardCodingToken("HARDCODING_UNIT=01",
                    out string type, out uint val));
                Assert.True(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "en", val, type));
            }
            finally { TryDelete(root); }
        }

        // ---- "!" installed-only via EaBinInstallStatus --------------------

        [Fact]
        public void InstalledForFilter_PatchedIfMatch_IsInstalled()
        {
            // PATCHED_IF whose bytes MATCH the ROM -> EaBinInstallStatus Installed.
            var rom = MakeFe8uRom(0x2000, 0x07);
            rom.Data[0x5000] = 0xDE;
            rom.Data[0x5001] = 0xAD;
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "InstalledPatch",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT",
                    "PATCHED_IF:0x5000=0xDE 0xAD");
                Assert.True(PatchFilterCore.IsInstalledForFilter(rom, file, "en"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void InstalledForFilter_PatchedIfMismatch_NotInstalled()
        {
            var rom = MakeFe8uRom(0x2000, 0x07);
            rom.Data[0x5000] = 0x00; // need 0xDE -> mismatch -> NotInstalled
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "NotInstalledPatch",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT",
                    "PATCHED_IF:0x5000=0xDE 0xAD");
                Assert.False(PatchFilterCore.IsInstalledForFilter(rom, file, "en"));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void InstalledForFilter_NoMarker_NotInstalled()
        {
            // No PATCHED_IF marker -> EaBinInstallStatus NotInstalled (mirrors WF blind spot).
            var rom = MakeFe8uRom(0x2000, 0x07);
            string verDir = MakeTempVerDir(out string root);
            try
            {
                string file = WritePatch(verDir, "Plain",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT");
                Assert.False(PatchFilterCore.IsInstalledForFilter(rom, file, "en"));
            }
            finally { TryDelete(root); }
        }

        // ---- language: .ja vs .en key resolution (no drift) ---------------

        // Regression for the Copilot plan review: the filter helper must pass a
        // WinForms-style lang (ja/en/zh) to LoadPatch — NOT GetLanguageSuffix()
        // which returns "" for Japanese (flipping CanSecondLanguageEnglish on).
        // Here a patch supplies ADDRESS_TYPE only via a ".ja"-suffixed key; under
        // lang "ja" the .ja key resolves to ADDRESS_TYPE=UNIT and the patch matches.
        [Fact]
        public void Language_JaKeyResolved_UnderJaLang()
        {
            var rom = MakeFe8uRom(0x2000, 0x07);
            string verDir = MakeTempVerDir(out string root, "FE8U");
            try
            {
                // ADDRESS_TYPE provided ONLY as a localized .ja key.
                string file = WritePatch(verDir, "JaKey",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE.ja=UNIT");
                Assert.True(PatchFilterCore.IsHardCodingTokenMatch(rom, file, "ja", 0x07, "UNIT"));
            }
            finally { TryDelete(root); }
        }

        // ---- null/guard safety --------------------------------------------

        [Fact]
        public void NullRom_NoThrow_ReturnsFalse()
        {
            Assert.False(PatchFilterCore.IsHardCodingTokenMatch(null, "x.txt", "en", 1, "UNIT"));
            Assert.False(PatchFilterCore.IsInstalledForFilter(null, "x.txt", "en"));
        }

        [Fact]
        public void MissingPatchFile_NoThrow_ReturnsFalse()
        {
            var rom = MakeFe8uRom(0x2000, 0x07);
            string missing = Path.Combine(Path.GetTempPath(), "no_such_patch_" + Guid.NewGuid().ToString("N") + ".txt");
            Assert.False(PatchFilterCore.IsHardCodingTokenMatch(rom, missing, "en", 0x07, "UNIT"));
            Assert.False(PatchFilterCore.IsInstalledForFilter(rom, missing, "en"));
        }
    }
}
