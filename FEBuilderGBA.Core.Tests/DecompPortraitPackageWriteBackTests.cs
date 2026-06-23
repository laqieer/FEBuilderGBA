using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the decomp-mode portrait PACKAGE source-tree WRITE-BACK + ROUND-TRIP
    /// helpers (#1374): <see cref="DecompAssetExportCore.ImportPortraitPackage"/>,
    /// <see cref="DecompAssetExportCore.RoundTripPortraitPackageAgainstBaseline"/>, and
    /// <see cref="DecompAssetExportCore.ResolvePortraitPackage"/>.
    ///
    /// All paths are ROM-FREE: they take directory paths only and never read/mutate
    /// CoreState.ROM. A SHA-256 invariant test proves a no-ROM import leaves any loaded ROM
    /// byte image untouched. Pure file I/O over temp dirs; no CoreState mutation, no SharedState.
    /// </summary>
    [Collection("SharedState")] // the ROM-invariant test mutates CoreState.ROM — serialize to avoid parallel races
    public class DecompPortraitPackageWriteBackTests
    {
        // ---------------------------------------------------------------- helpers (mirror the validator tests)

        static byte[] BuildGbaPalette(int colors)
        {
            var pal = new byte[colors * 2];
            for (int i = 0; i < colors; i++)
            {
                int r5 = i & 0x1F;
                int g5 = (i * 2) & 0x1F;
                int b5 = (i * 3) & 0x1F;
                ushort c = (ushort)((r5) | (g5 << 5) | (b5 << 10));
                pal[i * 2 + 0] = (byte)(c & 0xFF);
                pal[i * 2 + 1] = (byte)(c >> 8);
            }
            return pal;
        }

        static byte[] BuildSheetPng(int w, int h, int colors)
        {
            var indices = new byte[w * h];
            for (int i = 0; i < indices.Length; i++) indices[i] = (byte)(i % colors);
            byte[] png = IndexedPngWriter.Write(indices, w, h, BuildGbaPalette(colors), colors, transparentIndex: 0);
            Assert.NotNull(png);
            return png;
        }

        static string BuildMatchingJasc(byte[] pngBytes)
        {
            IndexedPngInfo info = IndexedPngReader.Read(pngBytes);
            Assert.True(info.Ok, info.Error);
            int count = info.PaletteRgb.Length / 3;
            var sb = new StringBuilder();
            sb.Append("JASC-PAL\r\n0100\r\n").Append(count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            for (int i = 0; i < count; i++)
                sb.Append(info.PaletteRgb[i * 3 + 0]).Append(' ')
                  .Append(info.PaletteRgb[i * 3 + 1]).Append(' ')
                  .Append(info.PaletteRgb[i * 3 + 2]).Append("\r\n");
            return sb.ToString();
        }

        static string FreshDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pkgwb_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        /// <summary>Write a full valid 128x112 portrait package (sheet + matching sidecar) into a fresh dir.</summary>
        static string MakeValidPackage(string sheetName = "portrait", int colors = 16)
        {
            string dir = FreshDir();
            byte[] png = BuildSheetPng(128, 112, colors);
            File.WriteAllBytes(Path.Combine(dir, sheetName + ".png"), png);
            File.WriteAllText(Path.Combine(dir, sheetName + ".pal"), BuildMatchingJasc(png));
            return dir;
        }

        // ---------------------------------------------------------------- ImportPortraitPackage

        [Fact]
        public void Import_ValidPackage_WritesSheetAndSidecar_ToEmptyOwner()
        {
            string src = MakeValidPackage();
            string dest = FreshDir(); // empty → clean new owner
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, allowMainOnly: false, overwriteOwner: false);
                Assert.True(r.Ok, r.Message);
                Assert.Equal(2, r.WrittenPaths.Count); // sheet + sidecar
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")));
                Assert.True(File.Exists(Path.Combine(dest, "portrait.pal")));

                // Identity copy: dest sheet bytes == source sheet bytes.
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(src, "portrait.png")),
                    File.ReadAllBytes(Path.Combine(dest, "portrait.png")));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_ValidPackage_WritesToMissingDestDir()
        {
            string src = MakeValidPackage();
            string dest = Path.Combine(Path.GetTempPath(), "pkgwb_missing_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.False(Directory.Exists(dest));
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, false);
                Assert.True(r.Ok, r.Message);
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_PackageWithoutSidecar_WritesSheetOnly()
        {
            string src = FreshDir();
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), BuildSheetPng(128, 112, 16));
            string dest = FreshDir();
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, false);
                Assert.True(r.Ok, r.Message);
                Assert.Single(r.WrittenPaths);
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")));
                Assert.False(File.Exists(Path.Combine(dest, "portrait.pal")));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_ExistingOwner_WithoutOverwrite_Refused()
        {
            string src = MakeValidPackage();
            string dest = MakeValidPackage("existing"); // already a single-package owner
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, overwriteOwner: false);
                Assert.False(r.Ok);
                Assert.Equal(DecompAssetStatus.PathRejected, r.Status);
                Assert.Contains("OWNER_EXISTS", r.Message);
                // Refuse-before-write: the source sheet must NOT have been added.
                Assert.False(File.Exists(Path.Combine(dest, "portrait.png")));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_ExistingOwner_WithOverwrite_Writes()
        {
            string src = MakeValidPackage();
            string dest = MakeValidPackage("portrait", colors: 8); // existing owner, different bytes
            try
            {
                byte[] before = File.ReadAllBytes(Path.Combine(dest, "portrait.png"));
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, overwriteOwner: true);
                Assert.True(r.Ok, r.Message);
                byte[] after = File.ReadAllBytes(Path.Combine(dest, "portrait.png"));
                Assert.NotEqual(before, after);
                Assert.Equal(File.ReadAllBytes(Path.Combine(src, "portrait.png")), after);
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_Overwrite_DifferentSheetName_LeavesSingleOwner_NoStalePng()
        {
            // Source sheet "portrait.png"; existing owner "old.png". --overwrite must REPLACE the
            // old owner (delete old.png) so the dir holds exactly ONE png afterwards (#1379 fix).
            string src = MakeValidPackage("portrait", colors: 16);
            string dest = MakeValidPackage("old", colors: 8); // owner with a DIFFERENT sheet name
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, overwriteOwner: true);
                Assert.True(r.Ok, r.Message);

                string[] pngs = Directory.GetFiles(dest, "*.png");
                Assert.Single(pngs); // NOT two — old.png was removed
                Assert.Equal("portrait.png", Path.GetFileName(pngs[0]));
                Assert.False(File.Exists(Path.Combine(dest, "old.png")));
                Assert.False(File.Exists(Path.Combine(dest, "old.pal"))); // old sidecar removed too
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")));
                Assert.True(File.Exists(Path.Combine(dest, "portrait.pal")));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_Overwrite_SourceWithoutSidecar_RemovesStaleOwnerSidecar()
        {
            // Source has NO sidecar; existing owner has one with the SAME sheet name. After
            // --overwrite the stale sidecar must be gone (source is the single source of truth).
            string src = FreshDir();
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), BuildSheetPng(128, 112, 16));
            string dest = MakeValidPackage("portrait", colors: 8); // owner WITH a sidecar
            try
            {
                Assert.True(File.Exists(Path.Combine(dest, "portrait.pal")));
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, overwriteOwner: true);
                Assert.True(r.Ok, r.Message);
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")));
                Assert.False(File.Exists(Path.Combine(dest, "portrait.pal"))); // stale sidecar gone
                Assert.Single(Directory.GetFiles(dest, "*.png"));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_AmbiguousDest_MultiplePngs_Refused()
        {
            string src = MakeValidPackage();
            string dest = FreshDir();
            File.WriteAllBytes(Path.Combine(dest, "a.png"), BuildSheetPng(128, 112, 16));
            File.WriteAllBytes(Path.Combine(dest, "b.png"), BuildSheetPng(128, 112, 16));
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, overwriteOwner: true);
                Assert.False(r.Ok);
                Assert.Equal(DecompAssetStatus.PathRejected, r.Status);
                Assert.Contains("AMBIGUOUS_OWNER", r.Message);
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_BadGeometry_NonCanonicalTooSmall_Refused()
        {
            // 64x64 sheet — mini/eye/mouth slots are out of bounds → validator errors (SHEET_TOO_SMALL).
            string src = FreshDir();
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), BuildSheetPng(64, 64, 16));
            string dest = FreshDir();
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, false);
                Assert.False(r.Ok);
                Assert.Equal(DecompAssetStatus.NotData, r.Status);
                Assert.Contains("validation failed", r.Message);
                Assert.False(File.Exists(Path.Combine(dest, "portrait.png")));
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_PaletteMismatch_Refused()
        {
            // Sheet + a SIDECAR whose colors differ from the PLTE → PALETTE_COLOR_MISMATCH (error).
            string src = FreshDir();
            byte[] png = BuildSheetPng(128, 112, 16);
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), png);
            // Tamper one JASC triple so it no longer matches the PLTE.
            string jasc = BuildMatchingJasc(png);
            string[] lines = jasc.Replace("\r\n", "\n").Split('\n');
            // line index 3 is the first color triple (after JASC-PAL / 0100 / count).
            lines[3] = "255 0 0";
            File.WriteAllText(Path.Combine(src, "portrait.pal"), string.Join("\r\n", lines));
            string dest = FreshDir();
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, false);
                Assert.False(r.Ok);
                Assert.Equal(DecompAssetStatus.NotData, r.Status);
                Assert.Contains("validation failed", r.Message);
            }
            finally { Cleanup(src); Cleanup(dest); }
        }

        [Fact]
        public void Import_MainOnly_RequiresAllowFlag()
        {
            // 96x80 main-mug-only sheet → INCOMPLETE_PACKAGE error unless allowMainOnly.
            string src = FreshDir();
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), BuildSheetPng(96, 80, 16));
            string dest1 = FreshDir();
            string dest2 = FreshDir();
            try
            {
                DecompAssetResult refused = DecompAssetExportCore.ImportPortraitPackage(src, dest1, allowMainOnly: false, overwriteOwner: false);
                Assert.False(refused.Ok);
                Assert.Equal(DecompAssetStatus.NotData, refused.Status);

                DecompAssetResult ok = DecompAssetExportCore.ImportPortraitPackage(src, dest2, allowMainOnly: true, overwriteOwner: false);
                Assert.True(ok.Ok, ok.Message);
            }
            finally { Cleanup(src); Cleanup(dest1); Cleanup(dest2); }
        }

        [Fact]
        public void Import_MissingSourceDir_BadArgsOrNotData()
        {
            string dest = FreshDir();
            try
            {
                DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(
                    Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N")), dest, false, false);
                Assert.False(r.Ok);
            }
            finally { Cleanup(dest); }
        }

        [Fact]
        public void Import_NullArgs_BadArgs_NeverThrows()
        {
            Assert.Equal(DecompAssetStatus.BadArgs, DecompAssetExportCore.ImportPortraitPackage(null, "x", false, false).Status);
            Assert.Equal(DecompAssetStatus.BadArgs, DecompAssetExportCore.ImportPortraitPackage("x", null, false, false).Status);
        }

        // ---------------------------------------------------------------- RoundTrip against baseline (the oracle)

        [Fact]
        public void RoundTrip_IdenticalPackages_Ok()
        {
            string src = MakeValidPackage();
            string baseline = FreshDir();
            // Copy src → baseline so they are byte-identical.
            DecompAssetExportCore.ImportPortraitPackage(src, baseline, false, false);
            try
            {
                DecompAssetResult r = DecompAssetExportCore.RoundTripPortraitPackageAgainstBaseline(src, baseline, false);
                Assert.True(r.Ok, r.Message);
            }
            finally { Cleanup(src); Cleanup(baseline); }
        }

        [Fact]
        public void RoundTrip_TamperedSheet_StillValid_Mismatches()
        {
            // A validation-VALID but byte-different source must MISMATCH the baseline (real oracle).
            string baseline = MakeValidPackage();
            string src = MakeValidPackage("portrait", colors: 16);
            // Re-write the src sheet with a different valid 128x112 sheet (different pixel data).
            byte[] tampered = BuildSheetPng(128, 112, 12);
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), tampered);
            File.WriteAllText(Path.Combine(src, "portrait.pal"), BuildMatchingJasc(tampered));
            try
            {
                // Sanity: the tampered source still VALIDATES (so only the baseline compare can fail it).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, src, false);
                Assert.True(v.Ok, "tampered source should still pass structural validation");

                DecompAssetResult r = DecompAssetExportCore.RoundTripPortraitPackageAgainstBaseline(src, baseline, false);
                Assert.False(r.Ok);
                Assert.Equal(DecompAssetStatus.NotData, r.Status);
                Assert.Contains("mismatch", r.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally { Cleanup(src); Cleanup(baseline); }
        }

        [Fact]
        public void RoundTrip_SidecarPresenceMismatch_Fails()
        {
            string baseline = MakeValidPackage(); // has sidecar
            string src = FreshDir();
            // Same sheet bytes as baseline but NO sidecar.
            File.Copy(Path.Combine(baseline, "portrait.png"), Path.Combine(src, "portrait.png"));
            try
            {
                DecompAssetResult r = DecompAssetExportCore.RoundTripPortraitPackageAgainstBaseline(src, baseline, false);
                Assert.False(r.Ok);
                Assert.Contains("sidecar", r.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally { Cleanup(src); Cleanup(baseline); }
        }

        [Fact]
        public void RoundTrip_MissingExpectBaseline_BadArgs()
        {
            string src = MakeValidPackage();
            try
            {
                Assert.Equal(DecompAssetStatus.BadArgs,
                    DecompAssetExportCore.RoundTripPortraitPackageAgainstBaseline(src, null, false).Status);
            }
            finally { Cleanup(src); }
        }

        // ---------------------------------------------------------------- ResolvePortraitPackage

        [Fact]
        public void Resolve_SinglePngWithSidecar()
        {
            string dir = MakeValidPackage();
            try
            {
                DecompAssetExportCore.PortraitPackageFiles f = DecompAssetExportCore.ResolvePortraitPackage(dir);
                Assert.NotNull(f);
                Assert.Equal(1, f.PngCount);
                Assert.NotNull(f.SheetPath);
                Assert.NotNull(f.SidecarPath);
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Resolve_MultiplePngs_AmbiguousNullSheet()
        {
            string dir = FreshDir();
            File.WriteAllBytes(Path.Combine(dir, "a.png"), BuildSheetPng(128, 112, 16));
            File.WriteAllBytes(Path.Combine(dir, "b.png"), BuildSheetPng(128, 112, 16));
            try
            {
                DecompAssetExportCore.PortraitPackageFiles f = DecompAssetExportCore.ResolvePortraitPackage(dir);
                Assert.NotNull(f);
                Assert.Equal(2, f.PngCount);
                Assert.Null(f.SheetPath);
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Resolve_NullOrMissingDir_ReturnsNull()
        {
            Assert.Null(DecompAssetExportCore.ResolvePortraitPackage(null));
            Assert.Null(DecompAssetExportCore.ResolvePortraitPackage(
                Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N"))));
        }

        // ---------------------------------------------------------------- ROM invariant (never mutates CoreState.ROM)

        [Fact]
        public void Import_DoesNotMutate_LoadedRom_Sha256Invariant()
        {
            // Load a tiny synthetic ROM into CoreState.ROM, import a package, assert the ROM
            // byte image is byte-identical before/after (the path is ROM-FREE by design).
            ROM saved = CoreState.ROM;
            try
            {
                var data = new byte[0x1000000];
                for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 7);
                var rom = new ROM();
                rom.LoadLow("portrait-pkg-invariant-fe8u.gba", data, "BE8E01");
                CoreState.ROM = rom;

                string before = Sha256(rom.Data);
                int beforeLen = rom.Data.Length;

                string src = MakeValidPackage();
                string dest = FreshDir();
                try
                {
                    DecompAssetResult r = DecompAssetExportCore.ImportPortraitPackage(src, dest, false, false);
                    Assert.True(r.Ok, r.Message);

                    Assert.Same(rom, CoreState.ROM);          // identity unchanged
                    Assert.Equal(beforeLen, rom.Data.Length); // length unchanged
                    Assert.Equal(before, Sha256(rom.Data));   // bytes unchanged
                }
                finally { Cleanup(src); Cleanup(dest); }
            }
            finally { CoreState.ROM = saved; }
        }

        static string Sha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data));
        }
    }
}
