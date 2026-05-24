// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for UnitPaletteWriteCore (#397).
//
// Validates the cross-platform LZ77 palette write-back used by both the
// Avalonia ImageUnitPaletteView and (eventually) any other caller that needs
// to overwrite a unit-palette LZ77 blob. Required by Copilot CLI v3 plan
// round-2 feedback (PR #397):
//   - In-place writes when newCompressed.Length <= oldCompressed.Length.
//   - Reallocation + P12 slot patching when newCompressed.Length is larger.
//   - Invalid-input early-out (null arrays, wrong lengths, out-of-range channels).
//   - Non-pointer P12 slot early-out.
//   - RGB555 round-trip equality through LZ77 compress/decompress.
//   - Undo coverage on reallocation path.
//
// Cross-platform: no WinForms, no Avalonia, no System.Drawing. Runs under
// FEBuilderGBA.Core.Tests/.
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UnitPaletteWriteCoreTests
    {
        // ----- Helpers -----

        /// <summary>Create a 16-color RGB555 buffer where every color is unique.</summary>
        static (uint[] r, uint[] g, uint[] b) MakeUniquePalette()
        {
            var r = new uint[16];
            var g = new uint[16];
            var b = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                r[i] = (uint)(i % 32);
                g[i] = (uint)((i * 2) % 32);
                b[i] = (uint)((i * 3) % 32);
            }
            return (r, g, b);
        }

        /// <summary>Create a 16-color RGB555 buffer with all colors equal (highly compressible).</summary>
        static (uint[] r, uint[] g, uint[] b) MakeUniformPalette(uint rr = 5, uint gg = 10, uint bb = 15)
        {
            var r = new uint[16];
            var g = new uint[16];
            var b = new uint[16];
            for (int i = 0; i < 16; i++) { r[i] = rr; g[i] = gg; b[i] = bb; }
            return (r, g, b);
        }

        /// <summary>Pack 16 RGB555 colors to 32 bytes in GBA palette order.</summary>
        static byte[] PackRgb555(uint[] r, uint[] g, uint[] b)
        {
            var raw = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(((b[i] & 0x1F) << 10) | ((g[i] & 0x1F) << 5) | (r[i] & 0x1F));
                raw[i * 2] = (byte)c;
                raw[i * 2 + 1] = (byte)(c >> 8);
            }
            return raw;
        }

        /// <summary>Build an in-memory ROM with a single unit-palette row at offset 0x40,
        /// whose P12 slot (offset 0x4C) points to an LZ77-compressed initial palette at <paramref name="initialPaletteOffset"/>.</summary>
        static (ROM rom, uint rowAddr, uint rowP12Slot) BuildRomWithPaletteAt(
            byte[] initialCompressedPalette, uint initialPaletteOffset = 0x200)
        {
            byte[] data = new byte[0x10000];
            // Place the compressed palette
            System.Array.Copy(initialCompressedPalette, 0, data, (int)initialPaletteOffset, initialCompressedPalette.Length);
            // Row layout: 12 bytes name "PalName\0\0\0\0\0" then u32 P12 pointer.
            uint rowAddr = 0x40;
            data[rowAddr + 0] = (byte)'P';
            data[rowAddr + 1] = (byte)'a';
            data[rowAddr + 2] = (byte)'l';
            data[rowAddr + 3] = (byte)'N';
            data[rowAddr + 4] = (byte)'a';
            data[rowAddr + 5] = (byte)'m';
            data[rowAddr + 6] = (byte)'e';
            uint gbaPtr = U.toPointer(initialPaletteOffset);
            data[rowAddr + 12] = (byte)(gbaPtr & 0xFF);
            data[rowAddr + 13] = (byte)((gbaPtr >> 8) & 0xFF);
            data[rowAddr + 14] = (byte)((gbaPtr >> 16) & 0xFF);
            data[rowAddr + 15] = (byte)((gbaPtr >> 24) & 0xFF);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            return (rom, rowAddr, rowAddr + 12);
        }

        // ===== Invalid input early-outs =====

        [Fact]
        public void WritePalette_NullArrays_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, null!, new uint[16], new uint[16], null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], null!, new uint[16], null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], new uint[16], null!, null));
        }

        [Fact]
        public void WritePalette_WrongLength_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[15], new uint[16], new uint[16], null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], new uint[17], new uint[16], null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], new uint[16], new uint[8], null));
        }

        [Fact]
        public void WritePalette_ChannelOutOfRange_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            var r = new uint[16];
            r[5] = 32; // OUT OF 0-31 range
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, new uint[16], new uint[16], null));
            var g = new uint[16];
            g[7] = 0xFFFFFFFF;
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], g, new uint[16], null));
        }

        [Fact]
        public void WritePalette_NonPointer_ReturnsNotFound()
        {
            // ROM whose P12 slot reads as 0 (not a GBA pointer)
            byte[] data = new byte[0x10000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            // P12 slot at 0x4C reads 0x00000000 by default
            var (r, g, b) = MakeUniquePalette();
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, 0x4Cu, r, g, b, null));
        }

        [Fact]
        public void WritePalette_NonLZ77Source_ReturnsNotFound()
        {
            // Build a ROM where P12 points to a region that is NOT a valid LZ77 stream
            byte[] data = new byte[0x10000];
            // Write some junk at 0x200 (not LZ77; first byte != 0x10)
            for (int i = 0; i < 32; i++) data[0x200 + i] = 0xAB;
            uint gbaPtr = U.toPointer(0x200);
            data[0x4C + 0] = (byte)(gbaPtr & 0xFF);
            data[0x4C + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            data[0x4C + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            data[0x4C + 3] = (byte)((gbaPtr >> 24) & 0xFF);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            var (r, g, b) = MakeUniquePalette();
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, 0x4Cu, r, g, b, null));
        }

        // ===== In-place write paths =====

        [Fact]
        public void WritePalette_InPlace_WhenSameSize()
        {
            // Initial palette is the same as the new one — guaranteed same compressed length.
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);
            int origLen = rom.Data.Length;

            // Write the SAME unique palette — compressed bytes are identical, so in-place fits exactly.
            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rInit, gInit, bInit, null);

            Assert.Equal(origP12, result); // pointer unchanged
            Assert.Equal(origP12, rom.u32(p12Slot)); // P12 slot still has the original pointer
            Assert.Equal(origLen, rom.Data.Length); // ROM size unchanged
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(origP12));
            byte[] expected = PackRgb555(rInit, gInit, bInit);
            Assert.Equal(expected, decompressed);
        }

        [Fact]
        public void WritePalette_InPlace_WhenSmaller_ZeroFillsRemainder()
        {
            // Build a ROM whose initial palette is LESS compressible (larger compressed bytes),
            // then overwrite with a more compressible uniform palette.
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            uint initialPaletteOffset = 0x200;
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed, initialPaletteOffset);
            uint origP12 = rom.u32(p12Slot);
            uint oldCompressedLen = LZ77.getCompressedSize(rom.Data, U.toOffset(origP12));

            // New palette is a uniform (very compressible) — should fit in-place strictly smaller.
            var (rNew, gNew, bNew) = MakeUniformPalette();
            byte[] newCompressedExpected = LZ77.compress(PackRgb555(rNew, gNew, bNew));
            Assert.True(newCompressedExpected.Length < oldCompressedLen,
                "Test invariant: uniform palette must compress smaller than unique palette.");
            int origRomLen = rom.Data.Length;

            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, null);

            Assert.Equal(origP12, result);
            Assert.Equal(origP12, rom.u32(p12Slot));
            Assert.Equal(origRomLen, rom.Data.Length);
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(origP12));
            byte[] expected = PackRgb555(rNew, gNew, bNew);
            Assert.Equal(expected, decompressed);
            // The bytes BEYOND the new compressed length, but within the old compressed length,
            // should be zero-filled.
            uint trailingStart = initialPaletteOffset + (uint)newCompressedExpected.Length;
            uint trailingEnd = initialPaletteOffset + oldCompressedLen;
            for (uint a = trailingStart; a < trailingEnd; a++)
            {
                Assert.Equal((byte)0, rom.Data[a]);
            }
        }

        // ===== Reallocating write =====

        [Fact]
        public void WritePalette_Reallocates_WhenLarger_AndPatchesP12()
        {
            // Initial: tiny uniform palette (very compressible, short compressed bytes).
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed, 0x200);
            uint origP12 = rom.u32(p12Slot);
            uint origOffset = U.toOffset(origP12);
            int origRomLen = rom.Data.Length;
            uint oldCompressedLen = LZ77.getCompressedSize(rom.Data, origOffset);

            // New: unique palette (less compressible, longer compressed bytes).
            var (rNew, gNew, bNew) = MakeUniquePalette();
            byte[] newCompressedExpected = LZ77.compress(PackRgb555(rNew, gNew, bNew));
            Assert.True(newCompressedExpected.Length > oldCompressedLen,
                "Test invariant: unique palette must compress larger than the uniform initial.");

            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, null);

            // The returned pointer must NOT be the original.
            Assert.NotEqual(origP12, result);
            // P12 slot must be updated to the new pointer (the round-2 explicit ask).
            uint newP12 = rom.u32(p12Slot);
            Assert.Equal(result, newP12);
            Assert.NotEqual(origP12, newP12);
            // ROM grew.
            Assert.True(rom.Data.Length > origRomLen, $"ROM should grow: was {origRomLen}, now {rom.Data.Length}");
            // The new pointer must point at the appended bytes; decompress must equal the new palette.
            uint newOffset = U.toOffset(newP12);
            byte[] decompressed = LZ77.decompress(rom.Data, newOffset);
            byte[] expected = PackRgb555(rNew, gNew, bNew);
            Assert.Equal(expected, decompressed);
        }

        // ===== Round-trip =====

        [Fact]
        public void WritePalette_RoundTrip_RGB555()
        {
            // Initial: uniform.
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            // Write a carefully chosen distinct palette and verify each (R, G, B) survives.
            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint newP12 = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, null);
            Assert.NotEqual(U.NOT_FOUND, newP12);
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(newP12));
            Assert.Equal(32, decompressed.Length);
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[i * 2] | (decompressed[i * 2 + 1] << 8));
                uint r = (uint)(c & 0x1F);
                uint g = (uint)((c >> 5) & 0x1F);
                uint b = (uint)((c >> 10) & 0x1F);
                Assert.Equal(rNew[i], r);
                Assert.Equal(gNew[i], g);
                Assert.Equal(bNew[i], b);
            }
        }

        // ===== Undo coverage =====

        [Fact]
        public void WritePalette_UndoCovers_InPlace()
        {
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            var undo = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "WritePalette InPlace",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            var (rNew, gNew, bNew) = MakeUniformPalette();
            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, undo);
            Assert.NotEqual(U.NOT_FOUND, result);
            // At least one undo entry must have been recorded for the write_range + zero-fill.
            Assert.True(undo.list.Count > 0);
        }

        [Fact]
        public void WritePalette_UndoCovers_Reallocation()
        {
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);

            var undo = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "WritePalette Realloc",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, undo);
            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.NotEqual(origP12, result);
            // For reallocation we record at least the P12 patch + the write_range to the new area.
            // The number of entries is implementation-detail-sensitive, but it must be > 0
            // AND there must be an entry covering the P12 slot (so undo can revert the P12 change).
            Assert.True(undo.list.Count > 0);
            bool sawP12 = false;
            foreach (var pos in undo.list)
            {
                if (pos.addr == p12Slot)
                {
                    sawP12 = true;
                    Assert.Equal(4, pos.data.Length);
                    break;
                }
            }
            Assert.True(sawP12, "Undo must include an entry covering the P12 slot after reallocation.");
        }

        // ===== Encoding helper =====

        [Fact]
        public void PackPalette_EncodesRgb555InGbaOrder()
        {
            // Standalone helper test: PackRgb555 above is the inverse of decompress.
            // The helper Core class exposes the same encoding for callers who only
            // want the raw bytes (no LZ77 step). This is exercised by encoding
            // a known palette and decoding the resulting byte stream manually.
            uint[] r = { 31, 0, 0 };
            uint[] g = { 0, 31, 0 };
            uint[] b = { 0, 0, 31 };
            // Pad to 16 colors.
            var rFull = new uint[16];
            var gFull = new uint[16];
            var bFull = new uint[16];
            System.Array.Copy(r, rFull, 3);
            System.Array.Copy(g, gFull, 3);
            System.Array.Copy(b, bFull, 3);

            byte[] raw = UnitPaletteWriteCore.PackRgb555(rFull, gFull, bFull);
            Assert.Equal(32, raw.Length);
            // c[0] = pure red (0x1F)
            Assert.Equal(0x1F, raw[0]);
            Assert.Equal(0x00, raw[1]);
            // c[1] = pure green (0x1F << 5 = 0x3E0)
            ushort c1 = (ushort)(raw[2] | (raw[3] << 8));
            Assert.Equal(0x03E0, c1);
            // c[2] = pure blue (0x1F << 10 = 0x7C00)
            ushort c2 = (ushort)(raw[4] | (raw[5] << 8));
            Assert.Equal(0x7C00, c2);
        }
    }
}
