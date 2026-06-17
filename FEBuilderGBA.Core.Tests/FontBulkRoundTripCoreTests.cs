// SPDX-License-Identifier: GPL-3.0-or-later
// #1165 Core tests for FontBulkExportCore / FontBulkImportCore — the
// `.fontall.txt` bulk export + atomic bulk import (ports of WF FontForm
// ExportALL / ImportAll). Uses an in-memory PNG-write/read pair of delegates so
// the round-trip runs without the filesystem.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FontBulkRoundTripCoreTests
    {
        const uint ROM_LEN = 0x01000000;
        const uint GLYPH_OFF = 0x00700000;
        const uint MOJI_A = 0x41;

        sealed class ImageServiceScope : IDisposable
        {
            readonly IImageService _prev;
            public ImageServiceScope()
            {
                _prev = CoreState.ImageService;
                CoreState.ImageService = new StubImageService();
            }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");

            uint topaddress = rom.RomInfo.font_serif_address;
            uint bucket = topaddress + (MOJI_A << 2);
            U.write_u32(rom.Data, GLYPH_OFF + 0, 0);
            U.write_u8(rom.Data, GLYPH_OFF + 4, MOJI_A);
            U.write_u8(rom.Data, GLYPH_OFF + 5, 9);
            for (uint i = 0; i < 64; i++) rom.Data[GLYPH_OFF + 8 + i] = 0x1B; // some pattern
            U.write_u32(rom.Data, bucket, U.toPointer(GLYPH_OFF));
            return rom;
        }

        [Fact]
        public void ExportAll_BuildsManifestForPlantedSerifGlyph()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                var savedNames = new List<string>();
                string manifest = FontBulkExportCore.ExportAll(rom, userFontOnly: false, (img, pngName) =>
                {
                    Assert.NotNull(img);
                    savedNames.Add(pngName);
                    return true;
                });

                Assert.Contains("\ttext\t9\t", manifest);          // serif row with width 9
                Assert.Contains("text_" + U.ToHexString(MOJI_A) + ".png", manifest);
                Assert.Contains("text_" + U.ToHexString(MOJI_A) + ".png", savedNames);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportAll_RoundTripsAndIsAtomicOnFailure()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // 16x16 indexed pixels (all index 1).
                byte[] idx = new byte[16 * 16];
                for (int i = 0; i < idx.Length; i++) idx[i] = 1;
                byte[] expected = FontGlyphRenderCore.PackGlyphBytes(idx);

                // Manifest with one good row for 'A'.
                string manifest = "//char\ttype\tWidth\tFilename\nA\ttext\t9\ttext_41.png\n";

                string err = FontBulkImportCore.ImportAll(rom, manifest, (pngName, type) =>
                    new FontGlyphPixels { Indexed = idx, Width = 16, Height = 16 });
                Assert.Equal("", err);

                byte[] written = rom.getBinaryData(GLYPH_OFF + 8, 64);
                Assert.Equal(expected, written);
                // The manifest's stored width (9) is preserved on import — NOT
                // overwritten with a pixel-derived value (Copilot #89).
                Assert.Equal(9u, rom.u8(GLYPH_OFF + 5));

                // Now a manifest whose loader fails on the 2nd row must restore the
                // ROM byte-identical (atomic — the 1st row's write is rolled back too).
                ROM rom2 = MakeRom();
                CoreState.ROM = rom2;
                byte[] snap = (byte[])rom2.Data.Clone();
                string manifest2 = "A\ttext\t9\ttext_41.png\nB\ttext\t9\ttext_42.png\n";
                int call = 0;
                string err2 = FontBulkImportCore.ImportAll(rom2, manifest2, (pngName, type) =>
                {
                    call++;
                    if (call == 2) return null; // simulate a load failure on row 2
                    return new FontGlyphPixels { Indexed = idx, Width = 16, Height = 16 };
                });
                Assert.NotEqual("", err2);
                Assert.Equal(snap, rom2.Data); // byte-identical restore
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Theory]
        [InlineData("text_41.png", true, 0x41u)]
        [InlineData("item_A8.png", true, 0xA8u)]
        [InlineData("noUnderscore.png", false, 0u)]
        [InlineData("bad_ZZ.png", false, 0u)]
        public void TryParseMojiFromFilename_Cases(string name, bool ok, uint expected)
        {
            bool got = FontBulkImportCore.TryParseMojiFromFilename(name, out uint moji);
            Assert.Equal(ok, got);
            if (ok) Assert.Equal(expected, moji);
        }

        [Fact]
        public void ImportAll_MalformedDataRow_AbortsAndRestores()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();
                byte[] idx = new byte[16 * 16];

                // Unknown type 'bogus' on an otherwise well-formed row must ABORT
                // (no silent skip), restoring the ROM byte-identical.
                string manifest = "A\tbogus\t9\ttext_41.png\n";
                string err = FontBulkImportCore.ImportAll(rom, manifest, (n, t) =>
                    new FontGlyphPixels { Indexed = idx, Width = 16, Height = 16 });
                Assert.NotEqual("", err);
                Assert.Equal(snap, rom.Data);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportAll_InvalidWidth_AbortsAndRestores()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();
                byte[] idx = new byte[16 * 16];

                // Non-numeric width column must ABORT (no silent derive-from-pixels).
                string manifest = "A\ttext\tNaN\ttext_41.png\n";
                string err = FontBulkImportCore.ImportAll(rom, manifest, (n, t) =>
                    new FontGlyphPixels { Indexed = idx, Width = 16, Height = 16 });
                Assert.NotEqual("", err);
                Assert.Equal(snap, rom.Data);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportAll_TooFewColumns_AbortsAndRestores()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();
                byte[] idx = new byte[16 * 16];

                string manifest = "A\ttext\t9\n"; // missing the filename column
                string err = FontBulkImportCore.ImportAll(rom, manifest, (n, t) =>
                    new FontGlyphPixels { Indexed = idx, Width = 16, Height = 16 });
                Assert.NotEqual("", err);
                Assert.Equal(snap, rom.Data);
            }
            finally { CoreState.ROM = prevRom; }
        }
    }
}
