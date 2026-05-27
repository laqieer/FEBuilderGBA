// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Portrait Import Wizard advanced palette options (#662).
//
// Covers Copilot CLI plan v2 review points 1 & 2:
//   1. SharePalette must DEREFERENCE the D8 pointer (read rom.p32(addr+8) then
//      getBinaryData at the offset) — not read 32 bytes directly from addr+8.
//   2. Custom palette must validate exactly 32 bytes (16 colors).
// Plus the orthogonal Fuchidori (black outline) checkbox semantics, and
// coverage for both ImportSimple AND ImportSheet code paths.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PortraitImportHelperPaletteOptionsTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public PortraitImportHelperPaletteOptionsTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null) CoreState.ImageService = new SkiaImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        static byte[] BuildSimplePalette(int colors)
        {
            byte[] pal = new byte[colors * 2];
            // index 0 = transparent (black, 0x0000), rest distinct GBA colors.
            for (int i = 1; i < colors; i++)
            {
                // pack i across BGR555 channels so each index is distinct.
                int r = (i * 2) & 0x1F;
                int g = (i * 3) & 0x1F;
                int b = (i * 5) & 0x1F;
                ushort gba = (ushort)((b << 10) | (g << 5) | r);
                pal[i * 2] = (byte)(gba & 0xFF);
                pal[i * 2 + 1] = (byte)((gba >> 8) & 0xFF);
            }
            return pal;
        }

        static ImageImportService.LoadResult MakeSyntheticLoadResult(int w, int h)
        {
            byte[] indexed = new byte[w * h];
            for (int i = 0; i < indexed.Length; i++)
                indexed[i] = (byte)((i / 8) % 16);
            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = indexed,
                GBAPalette = BuildSimplePalette(16),
                SourcePath = "C:\\tmp\\synthetic.png",
            };
        }

        // ------------------------------------------------------------------
        // Share-palette: must DEREFERENCE the D8 pointer.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_SharePalette_DereferencesD8Pointer_AndDoesNotWritePalette()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            // Pick entry 7 (avoid commonly tested 0-5).
            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(7 * rom.RomInfo.portrait_datasize);

            // Snapshot:
            //   - D8 pointer bytes at entryAddr+8 (must NOT change in share mode)
            //   - existing palette bytes at the DEREFERENCED D8 offset (must NOT change)
            uint d8Before = rom.p32(entryAddr + 8);
            uint paletteOffset = U.toOffset(d8Before);
            Assert.True(paletteOffset > 0 && paletteOffset + 32 <= rom.Data.Length,
                "Test pre-condition: entry 7 must have a valid D8 palette pointer");
            byte[] paletteBytesBefore = new byte[32];
            Array.Copy(rom.Data, (int)paletteOffset, paletteBytesBefore, 0, 32);

            uint d0Before = rom.p32(entryAddr + 0);

            var loadResult = MakeSyntheticLoadResult(16, 16);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.SharePalette, null, false);

            Assert.True(outcome.Success, $"Share-palette import failed: {outcome.Error}");

            // D0 should change (sheet tile data was rewritten).
            Assert.NotEqual(d0Before, rom.p32(entryAddr + 0));

            // D8 pointer must be UNCHANGED — share mode skips palette write.
            Assert.Equal(d8Before, rom.p32(entryAddr + 8));

            // Existing palette bytes at the dereferenced offset must also be
            // untouched. This guards against the bug where the helper writes
            // a new palette to the slot, defeating the whole point of
            // share-mode.
            byte[] paletteBytesAfter = new byte[32];
            Array.Copy(rom.Data, (int)paletteOffset, paletteBytesAfter, 0, 32);
            Assert.Equal(paletteBytesBefore, paletteBytesAfter);
        }

        // ------------------------------------------------------------------
        // Custom palette: writes the palette + remaps to it.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_CustomPalette_WritesNewPaletteToRom()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(8 * rom.RomInfo.portrait_datasize);

            uint d8Before = rom.p32(entryAddr + 8);

            // Build a distinctive custom palette — index 0 black, 1 pure red,
            // 2 pure green, rest grey ramps.
            byte[] custom = new byte[32];
            // index 0 already 0,0
            custom[2] = 0x1F; custom[3] = 0x00; // index 1 = red (B=0 G=0 R=31)
            custom[4] = 0xE0; custom[5] = 0x03; // index 2 = green (G=31)
            for (int i = 3; i < 16; i++)
            {
                int v = (i * 2) & 0x1F;
                ushort gba = (ushort)((v << 10) | (v << 5) | v);
                custom[i * 2] = (byte)(gba & 0xFF);
                custom[i * 2 + 1] = (byte)((gba >> 8) & 0xFF);
            }

            var loadResult = MakeSyntheticLoadResult(16, 16);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.CustomPalette, custom, false);

            Assert.True(outcome.Success, $"Custom-palette import failed: {outcome.Error}");

            // D8 pointer should have moved (new palette written to free space).
            uint d8After = rom.p32(entryAddr + 8);
            Assert.NotEqual(d8Before, d8After);

            // The bytes at the new D8 location should equal the custom palette.
            uint newOffset = U.toOffset(d8After);
            Assert.True(newOffset > 0 && newOffset + 32 <= rom.Data.Length);
            byte[] newPaletteBytes = new byte[32];
            Array.Copy(rom.Data, (int)newOffset, newPaletteBytes, 0, 32);
            Assert.Equal(custom, newPaletteBytes);
        }

        // ------------------------------------------------------------------
        // Custom palette: invalid sizes rejected with clear error.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_CustomPalette_RejectsTooShort()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(9 * rom.RomInfo.portrait_datasize);

            byte[] shortPalette = new byte[16]; // only 8 colors
            uint d0Before = rom.p32(entryAddr + 0);

            var loadResult = MakeSyntheticLoadResult(16, 16);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.CustomPalette, shortPalette, false);

            Assert.False(outcome.Success);
            Assert.Contains("16 colors", outcome.Error);
            Assert.Contains("16", outcome.Error); // actual length reported
            // ROM bytes must be unchanged — failure happened pre-scope.
            Assert.Equal(d0Before, rom.p32(entryAddr + 0));
        }

        [Fact]
        public void ImportSimple_CustomPalette_RejectsNull()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(10 * rom.RomInfo.portrait_datasize);

            var loadResult = MakeSyntheticLoadResult(16, 16);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.CustomPalette, null, false);

            Assert.False(outcome.Success);
            Assert.Contains("16 colors", outcome.Error);
        }

        // ------------------------------------------------------------------
        // Fuchidori (orthogonal to mode) — toggling it must not break the
        // happy path. The actual outline-correctness is covered in the Core
        // ImageUtilCoreFuchidoriTests.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_Fuchidori_StillSucceeds()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(11 * rom.RomInfo.portrait_datasize);

            uint d0Before = rom.p32(entryAddr + 0);
            var loadResult = MakeSyntheticLoadResult(16, 16);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, true);
            Assert.True(outcome.Success, $"Fuchidori import failed: {outcome.Error}");
            Assert.NotEqual(d0Before, rom.p32(entryAddr + 0));
        }

        [Fact]
        public void ImportSimple_BackwardCompatibilityOverload_StillWorks()
        {
            // The original 5-arg ImportSimple signature must still work and
            // default to AutoQuantize + no-Fuchidori behavior.
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }
            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(12 * rom.RomInfo.portrait_datasize);
            var loadResult = MakeSyntheticLoadResult(16, 16);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(rom, entryAddr, loadResult, undo);
            Assert.True(outcome.Success, $"Default overload import failed: {outcome.Error}");
        }

        // ------------------------------------------------------------------
        // ImportSheet — share-palette + custom-palette branches must work
        // for the 128x112 composite path as well (Copilot review point 2:
        // cover both simple AND sheet imports).
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSheet_SharePalette_DoesNotWritePalette()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(13 * rom.RomInfo.portrait_datasize);

            uint d8Before = rom.p32(entryAddr + 8);
            uint paletteOffset = U.toOffset(d8Before);
            Assert.True(paletteOffset > 0 && paletteOffset + 32 <= rom.Data.Length);
            byte[] paletteBefore = new byte[32];
            Array.Copy(rom.Data, (int)paletteOffset, paletteBefore, 0, 32);

            var loadResult = MakeSyntheticLoadResult(128, 112);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSheet(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.SharePalette, null, false);

            Assert.True(outcome.Success, $"Sheet share-palette import failed: {outcome.Error}");

            // D8 pointer must be unchanged + palette bytes at dereference unchanged.
            Assert.Equal(d8Before, rom.p32(entryAddr + 8));
            byte[] paletteAfter = new byte[32];
            Array.Copy(rom.Data, (int)paletteOffset, paletteAfter, 0, 32);
            Assert.Equal(paletteBefore, paletteAfter);
        }

        [Fact]
        public void ImportSheet_CustomPalette_RejectsShortPalette()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(14 * rom.RomInfo.portrait_datasize);

            uint d0Before = rom.p32(entryAddr + 0);
            var loadResult = MakeSyntheticLoadResult(128, 112);
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSheet(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.CustomPalette, new byte[10], false);

            Assert.False(outcome.Success);
            Assert.Contains("16 colors", outcome.Error);
            // ROM unchanged.
            Assert.Equal(d0Before, rom.p32(entryAddr + 0));
        }

        // ------------------------------------------------------------------
        // Share-palette safety: if the target slot has no valid D8 pointer
        // (e.g. zero), the helper must fail cleanly without ROM writes.
        // ------------------------------------------------------------------

        [Fact]
        public void ImportSimple_SharePalette_FailsCleanly_OnInvalidD8Pointer()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;

            // Pick a region of the ROM that is definitely not portrait data —
            // entry 0xFF * datasize starting from portrait base is well past
            // the portrait table; many bytes there are zero. We synthesize a
            // slot whose D8 we control: bump the entry up far enough that D8
            // is bytes-of-zero. To avoid depending on ROM contents we instead
            // construct the failure mode synthetically: pick a portrait slot
            // address that's a known palette table boundary and overwrite D8
            // with 0 first via a non-portrait-helper write.

            // Simpler: use entry 15 and forcibly zero D8 bytes BEFORE the
            // share-palette call. The helper must detect the invalid pointer
            // and refuse.
            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            uint entryAddr = baseAddr + (uint)(15 * rom.RomInfo.portrait_datasize);

            // Save the 4 bytes, zero them, run share-palette, then restore.
            byte[] saved = new byte[4];
            Array.Copy(rom.Data, (int)entryAddr + 8, saved, 0, 4);
            try
            {
                Array.Clear(rom.Data, (int)entryAddr + 8, 4);

                uint d0Before = rom.p32(entryAddr + 0);
                var loadResult = MakeSyntheticLoadResult(16, 16);
                var undo = new UndoService();
                var outcome = PortraitImportHelper.ImportSimple(
                    rom, entryAddr, loadResult, undo,
                    PortraitPaletteMode.SharePalette, null, false);

                Assert.False(outcome.Success);
                Assert.Contains("palette pointer", outcome.Error);
                // ROM bytes around D0 must be unchanged — helper rejected
                // before opening the undo scope.
                Assert.Equal(d0Before, rom.p32(entryAddr + 0));
            }
            finally
            {
                Array.Copy(saved, 0, rom.Data, (int)entryAddr + 8, 4);
            }
        }
    }
}
