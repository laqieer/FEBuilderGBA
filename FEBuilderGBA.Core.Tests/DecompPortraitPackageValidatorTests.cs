using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the decomp-mode PORTRAIT PACKAGE validator (#1350) —
    /// <see cref="DecompAssetValidatorCore.ValidateAssetPackage"/> + the new
    /// <see cref="IndexedPngInfo.PaletteRgb"/> field.
    ///
    /// READ-ONLY: the package validator never reads CoreState.ROM (it takes a directory
    /// path only) — proven by a source assertion plus the fact that every test runs without
    /// a loaded ROM. Pure file I/O over temp dirs; no CoreState mutation, no SharedState.
    /// </summary>
    public class DecompPortraitPackageValidatorTests
    {
        // ---------------------------------------------------------------- helpers

        /// <summary>A distinct, deterministic GBA palette (BGR555 LE) of <paramref name="colors"/> entries.</summary>
        static byte[] BuildGbaPalette(int colors)
        {
            var pal = new byte[colors * 2];
            for (int i = 0; i < colors; i++)
            {
                // Vary R/G/B across entries so PaletteRgb is non-trivial and a perturbation
                // of one entry is unambiguously detectable.
                int r5 = i & 0x1F;
                int g5 = (i * 2) & 0x1F;
                int b5 = (i * 3) & 0x1F;
                ushort c = (ushort)((r5) | (g5 << 5) | (b5 << 10));
                pal[i * 2 + 0] = (byte)(c & 0xFF);
                pal[i * 2 + 1] = (byte)(c >> 8);
            }
            return pal;
        }

        /// <summary>Build a 128x112 (or arbitrary w x h) indexed sheet PNG with the given palette.</summary>
        static byte[] BuildSheetPng(int w, int h, int colors)
        {
            var indices = new byte[w * h];
            for (int i = 0; i < indices.Length; i++) indices[i] = (byte)(i % colors);
            byte[] png = IndexedPngWriter.Write(indices, w, h, BuildGbaPalette(colors), colors, transparentIndex: 0);
            Assert.NotNull(png);
            return png;
        }

        /// <summary>
        /// Build a JASC .pal text whose R/G/B triples EXACTLY match the PNG's emitted PLTE.
        /// We read the PLTE back from the written PNG so the "matching" sidecar is robust to
        /// the writer's BGR555→RGB conversion (no need to replicate it here).
        /// </summary>
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

        /// <summary>Create a fresh temp directory and return its path.</summary>
        static string FreshDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        // PNG hand-build helpers (for the non-indexed / garbage cases).
        static readonly byte[] Sig = { 137, 80, 78, 71, 13, 10, 26, 10 };

        static void WriteU32BE(MemoryStream ms, uint v)
        {
            ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v);
        }

        static void WriteChunk(MemoryStream ms, string type, byte[] data)
        {
            WriteU32BE(ms, (uint)data.Length);
            byte[] t = Encoding.ASCII.GetBytes(type);
            ms.Write(t, 0, 4);
            ms.Write(data, 0, data.Length);
            WriteU32BE(ms, 0); // CRC not validated by the reader
        }

        static byte[] BuildIhdr(int w, int h, byte colorType)
        {
            var d = new byte[13];
            d[0] = (byte)(w >> 24); d[1] = (byte)(w >> 16); d[2] = (byte)(w >> 8); d[3] = (byte)w;
            d[4] = (byte)(h >> 24); d[5] = (byte)(h >> 16); d[6] = (byte)(h >> 8); d[7] = (byte)h;
            d[8] = 8; d[9] = colorType;
            return d;
        }

        /// <summary>A color-type-2 (truecolor) PNG with IDAT+IEND — parses but is NON_INDEXED.</summary>
        static byte[] BuildNonIndexedPng(int w, int h)
        {
            using var ms = new MemoryStream();
            ms.Write(Sig, 0, Sig.Length);
            WriteChunk(ms, "IHDR", BuildIhdr(w, h, 2)); // colorType 2 = truecolor (no PLTE)
            WriteChunk(ms, "IDAT", new byte[] { 0x78, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 });
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        // ---------------------------------------------------------------- ParseKind

        [Theory]
        [InlineData("portrait-package")]
        [InlineData("portraitpackage")]
        [InlineData("PORTRAIT-PACKAGE")]
        public void ParseKind_PortraitPackage(string s)
        {
            Assert.Equal(AssetKind.PortraitPackage, DecompAssetValidatorCore.ParseKind(s));
        }

        // ---------------------------------------------------------------- IndexedPngReader.PaletteRgb

        [Fact]
        public void IndexedPngReader_PaletteRgb_RoundTrips()
        {
            int colors = 16;
            byte[] png = BuildSheetPng(16, 16, colors);
            IndexedPngInfo info = IndexedPngReader.Read(png);
            Assert.True(info.Ok, info.Error);
            Assert.Equal(colors, info.PaletteColorCount);
            Assert.Equal(colors * 3, info.PaletteRgb.Length);

            // PaletteRgb must equal the GBA→RGB conversion the writer applied (BGR555→8bit).
            byte[] gba = BuildGbaPalette(colors);
            for (int i = 0; i < colors; i++)
            {
                ushort c = (ushort)(gba[i * 2] | (gba[i * 2 + 1] << 8));
                byte r = (byte)((c & 0x1F) << 3);
                byte g = (byte)(((c >> 5) & 0x1F) << 3);
                byte b = (byte)(((c >> 10) & 0x1F) << 3);
                Assert.Equal(r, info.PaletteRgb[i * 3 + 0]);
                Assert.Equal(g, info.PaletteRgb[i * 3 + 1]);
                Assert.Equal(b, info.PaletteRgb[i * 3 + 2]);
            }
        }

        // ---------------------------------------------------------------- happy path

        [Fact]
        public void Package_Valid128x112_WithMatchingSidecar_Ok()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);
                File.WriteAllText(Path.Combine(dir, "sheet.pal"), BuildMatchingJasc(png));

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.True(r.Ok, string.Join("; ", r.Errors.ConvertAll(e => e.Code + ":" + e.Message)));
            }
            finally { Cleanup(dir); }
        }

        // ---------------------------------------------------------------- sheet presence

        [Fact]
        public void Package_MissingSheet_Error()
        {
            string dir = FreshDir();
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "MISSING_SHEET");
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_TwoPngs_MultipleSheets()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "a.png"), png);
                File.WriteAllBytes(Path.Combine(dir, "b.png"), png);

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "MULTIPLE_SHEETS");
            }
            finally { Cleanup(dir); }
        }

        // ---------------------------------------------------------------- main-only / incomplete

        [Fact]
        public void Package_MainOnly96x80_NoAllowFlag_IncompleteError()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(96, 80, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir, allowMainOnly: false);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "INCOMPLETE_PACKAGE");
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_MainOnly96x80_AllowFlag_WarnsButOk()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(96, 80, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir, allowMainOnly: true);
                Assert.DoesNotContain(r.Errors, e => e.Code == "INCOMPLETE_PACKAGE");
                Assert.Contains(r.Warnings, w => w.Code == "INCOMPLETE_PACKAGE");
                Assert.True(r.Ok, string.Join("; ", r.Errors.ConvertAll(e => e.Code)));
            }
            finally { Cleanup(dir); }
        }

        // ---------------------------------------------------------------- slot bounds

        [Fact]
        public void Package_TooNarrow120x112_SheetTooSmall()
        {
            // 120x112: the x=96 mini/eye/mouth column (32px wide) needs 128 → exceeds width.
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(120, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "SHEET_TOO_SMALL");
                // Not the canonical 96x80 main-only case either.
                Assert.DoesNotContain(r.Errors, e => e.Code == "INCOMPLETE_PACKAGE");
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_NonCanonicalButLargeEnough_SheetBadDimsWarn()
        {
            // 256x256: non-canonical but every slot (max extent 128x112) fits → WARN only.
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(256, 256, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.Contains(r.Warnings, w => w.Code == "SHEET_BAD_DIMS");
                Assert.DoesNotContain(r.Errors, e => e.Code == "SHEET_TOO_SMALL");
            }
            finally { Cleanup(dir); }
        }

        // ---------------------------------------------------------------- structural / palette

        [Fact]
        public void Package_NonIndexedSheet_NonIndexed()
        {
            string dir = FreshDir();
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), BuildNonIndexedPng(128, 112));

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "NON_INDEXED" || e.Code == "BAD_PNG");
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_SheetPaletteGt16_PortraitPaletteGt16Warn()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 32); // 32-color palette
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);
                File.WriteAllText(Path.Combine(dir, "sheet.pal"), BuildMatchingJasc(png));

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.Contains(r.Warnings, w => w.Code == "PORTRAIT_PALETTE_GT16");
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_MissingSidecar_MissingPaletteWarn_StillValidatesSheet()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);
                // no .pal sidecar

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.Contains(r.Warnings, w => w.Code == "MISSING_PALETTE");
                Assert.True(r.Ok, string.Join("; ", r.Errors.ConvertAll(e => e.Code)));
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_SidecarCountMismatch_PaletteCountMismatch()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);
                // Sidecar has only 8 colors (valid JASC) but the PLTE has 16.
                File.WriteAllText(Path.Combine(dir, "sheet.pal"),
                    "JASC-PAL\r\n0100\r\n8\r\n0 0 0\r\n8 8 8\r\n16 16 16\r\n24 24 24\r\n32 32 32\r\n40 40 40\r\n48 48 48\r\n56 56 56\r\n");

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "PALETTE_COUNT_MISMATCH");
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_SidecarColorMismatch_PaletteColorMismatch()
        {
            // THE key consistency test: same count, one differing RGB triple.
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                // Build a matching sidecar, then perturb entry 5's red channel.
                IndexedPngInfo info = IndexedPngReader.Read(png);
                int count = info.PaletteRgb.Length / 3;
                var sb = new StringBuilder();
                sb.Append("JASC-PAL\r\n0100\r\n").Append(count).Append("\r\n");
                for (int i = 0; i < count; i++)
                {
                    int rr = info.PaletteRgb[i * 3 + 0];
                    int gg = info.PaletteRgb[i * 3 + 1];
                    int bb = info.PaletteRgb[i * 3 + 2];
                    if (i == 5) rr = (rr == 255) ? 0 : rr + 1; // perturb exactly one channel
                    sb.Append(rr).Append(' ').Append(gg).Append(' ').Append(bb).Append("\r\n");
                }
                File.WriteAllText(Path.Combine(dir, "sheet.pal"), sb.ToString());

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "PALETTE_COLOR_MISMATCH");
            }
            finally { Cleanup(dir); }
        }

        // The sidecar is matched by the SHEET name (sheet.png -> sheet.pal). A .pal with an
        // UNRELATED name must NOT be used for the consistency comparison (Copilot PR #1353
        // review): it would otherwise emit a false PALETTE_COLOR_MISMATCH. The sheet's own
        // sidecar is absent → MISSING_PALETTE, and the unrelated one is flagged EXTRA_PALETTE.
        [Fact]
        public void Package_UnrelatedSidecarName_NotUsed_NoFalseMismatch()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                // A structurally valid JASC that DIFFERS from the sheet PLTE, but named so it
                // does NOT match the sheet (other.pal, not sheet.pal).
                IndexedPngInfo info = IndexedPngReader.Read(png);
                int count = info.PaletteRgb.Length / 3;
                var sb = new StringBuilder();
                sb.Append("JASC-PAL\r\n0100\r\n").Append(count).Append("\r\n");
                for (int i = 0; i < count; i++)
                    sb.Append(255).Append(' ').Append(0).Append(' ').Append(0).Append("\r\n"); // all red, definitely != PLTE
                File.WriteAllText(Path.Combine(dir, "other.pal"), sb.ToString());

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                // The unrelated palette was NOT compared → no false mismatch.
                Assert.DoesNotContain(r.Errors, e => e.Code == "PALETTE_COLOR_MISMATCH");
                Assert.DoesNotContain(r.Errors, e => e.Code == "PALETTE_COUNT_MISMATCH");
                // The sheet's own sidecar is missing, and the unrelated one is flagged.
                Assert.Contains(r.Warnings, w => w.Code == "MISSING_PALETTE");
                Assert.Contains(r.Warnings, w => w.Code == "EXTRA_PALETTE");
            }
            finally { Cleanup(dir); }
        }

        // A structurally INVALID sidecar (bad RGB triple) must be caught by ValidatePalette
        // (BAD_PALETTE_*) WITHOUT a misleading PALETTE_COUNT/COLOR_MISMATCH layered on top —
        // TryParseJascColors now returns false for a malformed/short palette so the
        // consistency comparison is skipped (Copilot PR #1353 review).
        [Fact]
        public void Package_MalformedSidecar_NoConsistencyMismatch_OnlyStructuralError()
        {
            string dir = FreshDir();
            try
            {
                byte[] png = BuildSheetPng(128, 112, 16);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);
                // Header declares 16 colors but a triple is malformed (300 is out of 0..255).
                File.WriteAllText(Path.Combine(dir, "sheet.pal"),
                    "JASC-PAL\r\n0100\r\n16\r\n0 0 0\r\n300 0 0\r\n");

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                // ValidatePalette reports the structural fault.
                Assert.Contains(r.Errors, e => e.Code == "BAD_PALETTE_COLOR" || e.Code == "BAD_PALETTE_COUNT");
                // No misleading consistency diagnostics on a structurally invalid palette.
                Assert.DoesNotContain(r.Errors, e => e.Code == "PALETTE_COUNT_MISMATCH");
                Assert.DoesNotContain(r.Errors, e => e.Code == "PALETTE_COLOR_MISMATCH");
            }
            finally { Cleanup(dir); }
        }

        // ---------------------------------------------------------------- fault safety

        [Fact]
        public void Package_GarbagePng_NoThrow_Error()
        {
            string dir = FreshDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "sheet.png"), "this is not a png");

                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
                Assert.False(r.Ok);
                Assert.NotEmpty(r.Errors);
            }
            finally { Cleanup(dir); }
        }

        [Fact]
        public void Package_DirNotFound_DirNotFound()
        {
            string dir = Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N"));
            AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.PortraitPackage, dir);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Code == "DIR_NOT_FOUND");
        }

        [Fact]
        public void Package_WrongKind_UnknownKind()
        {
            string dir = FreshDir();
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAssetPackage(AssetKind.Graphics, dir);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "UNKNOWN_KIND");
            }
            finally { Cleanup(dir); }
        }

        // ---------------------------------------------------------------- source assertion

        [SkippableFact]
        public void PackageValidator_DoesNotReference_CoreStateRom_SourceAssertion()
        {
            // The package validation path (in DecompAssetValidatorCore.cs) must contain no
            // real CODE reference to CoreState.ROM. Strip comment lines so XML docs that
            // mention CoreState.ROM (to explain it is never read) do not trip the guard.
            string root = FindRepoRoot();
            Skip.If(root == null, "repo root not found");
            string src = Path.Combine(root, "FEBuilderGBA.Core", "DecompAssetValidatorCore.cs");
            Skip.If(!File.Exists(src), "validator source not found");

            foreach (string line in File.ReadAllLines(src))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                    continue;
                Assert.DoesNotContain("CoreState.ROM", line);
            }
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
