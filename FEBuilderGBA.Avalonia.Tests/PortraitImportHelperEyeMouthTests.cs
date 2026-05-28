// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for #663 Slice A — PortraitImportHelper eye/mouth block coords.
//
// Verifies the four nullable byte? parameters at the END of ImportSimple /
// ImportSheet correctly persist to entry bytes B20-B23 on FE7/FE8 ROMs and
// are silently skipped on FE6 (whose 16-byte entry layout reuses those bytes
// for unrelated fields). The 0xFF case is exercised explicitly because the
// API treats 0xFF as a valid byte value (NOT a sentinel) — Copilot CLI v2
// review point 2.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PortraitImportHelperEyeMouthTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public PortraitImportHelperEyeMouthTests(RomFixture fixture, ITestOutputHelper output)
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
            for (int i = 1; i < colors; i++)
            {
                int r = (i * 2) & 0x1F;
                int g = (i * 3) & 0x1F;
                int b = (i * 5) & 0x1F;
                ushort gba = (ushort)((b << 10) | (g << 5) | r);
                pal[i * 2] = (byte)(gba & 0xFF);
                pal[i * 2 + 1] = (byte)((gba >> 8) & 0xFF);
            }
            return pal;
        }

        static ImageImportService.LoadResult MakeSimpleLoadResult()
        {
            const int w = 16, h = 16;
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

        static ImageImportService.LoadResult MakeSheetLoadResult()
        {
            const int w = 128, h = 112;
            byte[] indexed = new byte[w * h];
            for (int i = 0; i < indexed.Length; i++)
                indexed[i] = (byte)((i / 32) % 16);
            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = w,
                Height = h,
                IndexedPixels = indexed,
                GBAPalette = BuildSimplePalette(16),
                SourcePath = "C:\\tmp\\synthetic_sheet.png",
            };
        }

        // Pick a portrait entry far enough into the table that the
        // pre-existing palette test bank (entries 0-7) is undisturbed.
        const int TestEntryIndex = 9;

        static uint GetEntryAddr(ROM rom, int index)
        {
            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            return baseAddr + (uint)(index * rom.RomInfo.portrait_datasize);
        }

        // ------------------------------------------------------------------
        // Test 1: FE8U, all 4 NUDs supplied → B20-B23 contain the supplied
        // values after Import (Copilot CLI v3 Slice A primary acceptance).
        // ------------------------------------------------------------------
        [Fact]
        public void ImportSimple_FE8U_WritesB20B23()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            uint entryAddr = GetEntryAddr(rom, TestEntryIndex);

            var loadResult = MakeSimpleLoadResult();
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, false,
                "Import w/ B20-B23 (FE8U test)",
                mouthBlockX: 10, mouthBlockY: 20, eyeBlockX: 30, eyeBlockY: 40);

            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");
            Assert.Equal((uint)10, rom.u8(entryAddr + 20));
            Assert.Equal((uint)20, rom.u8(entryAddr + 21));
            Assert.Equal((uint)30, rom.u8(entryAddr + 22));
            Assert.Equal((uint)40, rom.u8(entryAddr + 23));
        }

        // ------------------------------------------------------------------
        // Test 2: FE6, all 4 NUDs supplied → B20-B23 UNCHANGED (the FE6
        // 16-byte entry layout uses those bytes for unrelated fields, so
        // the helper must NOT clobber them).
        // ------------------------------------------------------------------
        [Fact]
        public void ImportSimple_FE6_SkipsB20B23()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE6")
            {
                _output.WriteLine($"SKIP: needs FE6 ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            uint entryAddr = GetEntryAddr(rom, TestEntryIndex);

            // Snapshot bytes that *would* be B20-B23 on an FE7/FE8 entry.
            // On FE6 these are part of the NEXT entry — make sure we don't
            // touch them.
            byte before20 = (byte)rom.u8(entryAddr + 20);
            byte before21 = (byte)rom.u8(entryAddr + 21);
            byte before22 = (byte)rom.u8(entryAddr + 22);
            byte before23 = (byte)rom.u8(entryAddr + 23);

            var loadResult = MakeSimpleLoadResult();
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, false,
                "Import w/ B20-B23 (FE6 skip test)",
                mouthBlockX: 10, mouthBlockY: 20, eyeBlockX: 30, eyeBlockY: 40);

            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");
            Assert.Equal((uint)before20, rom.u8(entryAddr + 20));
            Assert.Equal((uint)before21, rom.u8(entryAddr + 21));
            Assert.Equal((uint)before22, rom.u8(entryAddr + 22));
            Assert.Equal((uint)before23, rom.u8(entryAddr + 23));
        }

        // ------------------------------------------------------------------
        // Test 3: FE7/FE8, all 4 params left as null → B20-B23 UNCHANGED.
        // This proves backward compatibility for existing callers (Pick
        // PNG/BMP, FE-Repo, drag-drop, batch import) that don't supply the
        // new params.
        // ------------------------------------------------------------------
        [Fact]
        public void ImportSimple_NullParams_DoesNotOverwrite()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            if (_fixture.Version == "FE6")
            {
                _output.WriteLine("SKIP: this test needs FE7 or FE8");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            uint entryAddr = GetEntryAddr(rom, TestEntryIndex);

            byte before20 = (byte)rom.u8(entryAddr + 20);
            byte before21 = (byte)rom.u8(entryAddr + 21);
            byte before22 = (byte)rom.u8(entryAddr + 22);
            byte before23 = (byte)rom.u8(entryAddr + 23);

            var loadResult = MakeSimpleLoadResult();
            var undo = new UndoService();
            // Call the mode-aware overload without supplying the new params
            // (all default to null).
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, false,
                "Import w/ null block coords (backward-compat test)");

            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");
            Assert.Equal((uint)before20, rom.u8(entryAddr + 20));
            Assert.Equal((uint)before21, rom.u8(entryAddr + 21));
            Assert.Equal((uint)before22, rom.u8(entryAddr + 22));
            Assert.Equal((uint)before23, rom.u8(entryAddr + 23));
        }

        // ------------------------------------------------------------------
        // Test 4: FE7/FE8, 0xFF passed → B20-B23 == 0xFF after Import.
        // 0xFF is NOT a sentinel; it must round-trip like any other byte
        // value (Copilot CLI v2 review point 2).
        // ------------------------------------------------------------------
        [Fact]
        public void ImportSimple_FF_WritesFF()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            if (_fixture.Version == "FE6")
            {
                _output.WriteLine("SKIP: this test needs FE7 or FE8");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            uint entryAddr = GetEntryAddr(rom, TestEntryIndex);

            var loadResult = MakeSimpleLoadResult();
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, false,
                "Import w/ 0xFF block coords",
                mouthBlockX: 0xFF, mouthBlockY: 0xFF,
                eyeBlockX:   0xFF, eyeBlockY:   0xFF);

            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");
            Assert.Equal((uint)0xFF, rom.u8(entryAddr + 20));
            Assert.Equal((uint)0xFF, rom.u8(entryAddr + 21));
            Assert.Equal((uint)0xFF, rom.u8(entryAddr + 22));
            Assert.Equal((uint)0xFF, rom.u8(entryAddr + 23));
        }

        // ------------------------------------------------------------------
        // Test 5: ImportSheet (128x112 composite path), FE7/FE8 → B20-B23
        // contain the supplied values. Mirrors the ImportSimple test but
        // covers the second helper entrypoint.
        // ------------------------------------------------------------------
        [Fact]
        public void ImportSheet_FE8U_WritesB20B23()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            // Use a distinct entry so this test does not collide with the
            // ImportSimple test if they run in the same process.
            uint entryAddr = GetEntryAddr(rom, TestEntryIndex + 1);

            var loadResult = MakeSheetLoadResult();
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSheet(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, false,
                "Import Sheet w/ B20-B23 (FE8U test)",
                mouthBlockX: 11, mouthBlockY: 22, eyeBlockX: 33, eyeBlockY: 44);

            Assert.True(outcome.Success, $"Sheet import failed: {outcome.Error}");
            Assert.Equal((uint)11, rom.u8(entryAddr + 20));
            Assert.Equal((uint)22, rom.u8(entryAddr + 21));
            Assert.Equal((uint)33, rom.u8(entryAddr + 22));
            Assert.Equal((uint)44, rom.u8(entryAddr + 23));
        }

        // ------------------------------------------------------------------
        // #657: ImportSimple must zero D4 (mini face pointer) and D12 (mouth
        // frames pointer) on FE7/8 so the Portrait Image Editor doesn't
        // render those sub-views from STALE old portrait tile blocks under
        // the new palette — that was the "total mess" the user reported in
        // the reopened #657 screenshot.
        // ------------------------------------------------------------------
        [Fact]
        public void ImportSimple_FE8U_ZerosD4AndD12_Issue657()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            var rom = _fixture.ROM;
            uint entryAddr = GetEntryAddr(rom, TestEntryIndex);

            // Seed D4 and D12 with non-zero sentinel pointers so the test
            // can prove the import overwrote them.
            rom.write_p32(entryAddr + 4, 0x08AAAAAAu);
            rom.write_p32(entryAddr + 12, 0x08BBBBBBu);
            Assert.Equal((uint)0x08AAAAAA, rom.u32(entryAddr + 4));
            Assert.Equal((uint)0x08BBBBBB, rom.u32(entryAddr + 12));

            var loadResult = MakeSimpleLoadResult();
            var undo = new UndoService();
            var outcome = PortraitImportHelper.ImportSimple(
                rom, entryAddr, loadResult, undo,
                PortraitPaletteMode.AutoQuantize, null, false,
                "Import w/ D4+D12 invalidate (FE8U #657 test)",
                mouthBlockX: null, mouthBlockY: null, eyeBlockX: null, eyeBlockY: null);

            Assert.True(outcome.Success, $"Import failed: {outcome.Error}");
            // D4 and D12 should now read as 0 — the editor will render the
            // mini face and mouth-frame sub-views as blank instead of the
            // garbled old data the user saw.
            Assert.Equal((uint)0, rom.u32(entryAddr + 4));
            Assert.Equal((uint)0, rom.u32(entryAddr + 12));
        }
    }
}
