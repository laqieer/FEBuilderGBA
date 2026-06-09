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
//   - Undo coverage via the ambient scope.
//   - Multi-slot semantics (single-slot overwrite + override-all),
//     mirroring WF PaletteFormRef.MakePaletteUIToROM (Copilot CLI PR #585
//     round 1 ask).
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

        /// <summary>Build a multi-slot palette: 5 concatenated 32-byte slots
        /// (Ally / Enemy / NPC / Gray / Independent) where slot N has every
        /// color = (N+1, N+2, N+3) so they are distinguishable post-write.</summary>
        static byte[] BuildMultiSlotRaw(int slotCount = 5)
        {
            byte[] raw = new byte[slotCount * 32];
            for (int s = 0; s < slotCount; s++)
            {
                for (int i = 0; i < 16; i++)
                {
                    uint r = (uint)((s + 1) & 0x1F);
                    uint g = (uint)((s + 2) & 0x1F);
                    uint b = (uint)((s + 3) & 0x1F);
                    ushort c = (ushort)(((b & 0x1F) << 10) | ((g & 0x1F) << 5) | (r & 0x1F));
                    raw[s * 32 + i * 2] = (byte)c;
                    raw[s * 32 + i * 2 + 1] = (byte)(c >> 8);
                }
            }
            return raw;
        }

        // ===== Invalid input early-outs =====

        [Fact]
        public void WritePalette_NullArrays_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, null!, new uint[16], new uint[16], 0, false, undo: null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], null!, new uint[16], 0, false, undo: null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], new uint[16], null!, 0, false, undo: null));
        }

        [Fact]
        public void WritePalette_WrongLength_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[15], new uint[16], new uint[16], 0, false, undo: null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], new uint[17], new uint[16], 0, false, undo: null));
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], new uint[16], new uint[8], 0, false, undo: null));
        }

        [Fact]
        public void WritePalette_ChannelOutOfRange_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            var r = new uint[16];
            r[5] = 32; // OUT OF 0-31 range
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, new uint[16], new uint[16], 0, false, undo: null));
            var g = new uint[16];
            g[7] = 0xFFFFFFFF;
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, new uint[16], g, new uint[16], 0, false, undo: null));
        }

        [Fact]
        public void WritePalette_NegativeIndex_ReturnsNotFound()
        {
            var compressed = LZ77.compress(PackRgb555(MakeUniquePalette().r, MakeUniquePalette().g, MakeUniquePalette().b));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(compressed);
            var (r, g, b) = MakeUniformPalette();
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, g, b, -1, false, undo: null));
        }

        [Fact]
        public void WritePalette_NonPointer_ReturnsNotFound()
        {
            byte[] data = new byte[0x10000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            var (r, g, b) = MakeUniquePalette();
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, 0x4Cu, r, g, b, 0, false, undo: null));
        }

        [Fact]
        public void WritePalette_NonLZ77Source_ReturnsNotFound()
        {
            byte[] data = new byte[0x10000];
            for (int i = 0; i < 32; i++) data[0x200 + i] = 0xAB; // junk
            uint gbaPtr = U.toPointer(0x200);
            data[0x4C + 0] = (byte)(gbaPtr & 0xFF);
            data[0x4C + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            data[0x4C + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            data[0x4C + 3] = (byte)((gbaPtr >> 24) & 0xFF);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            var (r, g, b) = MakeUniquePalette();
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.WritePalette(rom, 0x4Cu, r, g, b, 0, false, undo: null));
        }

        // ===== Single-slot in-place writes (no other slots present) =====

        [Fact]
        public void WritePalette_InPlace_WhenSameSize()
        {
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);
            int origLen = rom.Data.Length;

            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rInit, gInit, bInit, 0, false, undo: null);

            Assert.Equal(origP12, result);
            Assert.Equal(origP12, rom.u32(p12Slot));
            Assert.Equal(origLen, rom.Data.Length);
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(origP12));
            Assert.Equal(PackRgb555(rInit, gInit, bInit), decompressed);
        }

        [Fact]
        public void WritePalette_InPlace_WhenSmaller_ZeroFillsRemainder()
        {
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            uint initialPaletteOffset = 0x200;
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed, initialPaletteOffset);
            uint origP12 = rom.u32(p12Slot);
            uint oldCompressedLen = LZ77.getCompressedSize(rom.Data, U.toOffset(origP12));

            var (rNew, gNew, bNew) = MakeUniformPalette();
            byte[] newCompressedExpected = LZ77.compress(PackRgb555(rNew, gNew, bNew));
            Assert.True(newCompressedExpected.Length < oldCompressedLen,
                "Test invariant: uniform palette must compress smaller than unique palette.");
            int origRomLen = rom.Data.Length;

            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, 0, false, undo: null);

            Assert.Equal(origP12, result);
            Assert.Equal(origP12, rom.u32(p12Slot));
            Assert.Equal(origRomLen, rom.Data.Length);
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(origP12));
            Assert.Equal(PackRgb555(rNew, gNew, bNew), decompressed);
            // Trailing bytes are zero-filled by the helper.
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
            // Initial: tiny uniform (very compressible).
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed, 0x200);
            uint origP12 = rom.u32(p12Slot);
            int origRomLen = rom.Data.Length;
            uint oldCompressedLen = LZ77.getCompressedSize(rom.Data, U.toOffset(origP12));

            // New: unique palette (less compressible).
            var (rNew, gNew, bNew) = MakeUniquePalette();
            byte[] newCompressedExpected = LZ77.compress(PackRgb555(rNew, gNew, bNew));
            Assert.True(newCompressedExpected.Length > oldCompressedLen,
                "Test invariant: unique palette must compress larger than the uniform initial.");

            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, 0, false, undo: null);

            Assert.NotEqual(origP12, result);
            // Round-2 explicit ask: P12 slot updated to the new pointer.
            uint newP12 = rom.u32(p12Slot);
            Assert.Equal(result, newP12);
            Assert.NotEqual(origP12, newP12);
            // ROM grew.
            Assert.True(rom.Data.Length > origRomLen, $"ROM should grow: was {origRomLen}, now {rom.Data.Length}");
            uint newOffset = U.toOffset(newP12);
            byte[] decompressed = LZ77.decompress(rom.Data, newOffset);
            Assert.Equal(PackRgb555(rNew, gNew, bNew), decompressed);
        }

        // ===== Round-trip =====

        [Fact]
        public void WritePalette_RoundTrip_RGB555()
        {
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint newP12 = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, 0, false, undo: null);
            Assert.NotEqual(U.NOT_FOUND, newP12);
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(newP12));
            Assert.Equal(32, decompressed.Length);
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[i * 2] | (decompressed[i * 2 + 1] << 8));
                Assert.Equal(rNew[i], (uint)(c & 0x1F));
                Assert.Equal(gNew[i], (uint)((c >> 5) & 0x1F));
                Assert.Equal(bNew[i], (uint)((c >> 10) & 0x1F));
            }
        }

        // ===== Multi-slot semantics =====

        [Fact]
        public void WritePalette_Single_Slot_Preserves_Other_Slots()
        {
            // 5-slot initial buffer. Write slot 0 (Ally) with a new color.
            // Slots 1..4 must survive untouched.
            byte[] rawInit = BuildMultiSlotRaw(5);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);

            // New ally palette = pure white
            uint[] r = new uint[16], g = new uint[16], b = new uint[16];
            for (int i = 0; i < 16; i++) { r[i] = 31; g[i] = 31; b[i] = 31; }

            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, g, b, 0, false, undo: null);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            // Result buffer must still have 5 slots.
            Assert.Equal(5 * 32, decompressed.Length);
            // Slot 0 = pure white (R=G=B=31 -> 0x7FFF).
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[i * 2] | (decompressed[i * 2 + 1] << 8));
                Assert.Equal(0x7FFF, c);
            }
            // Slots 1..4 unchanged.
            for (int s = 1; s < 5; s++)
            {
                for (int i = 0; i < 16; i++)
                {
                    byte expectLo = rawInit[s * 32 + i * 2];
                    byte expectHi = rawInit[s * 32 + i * 2 + 1];
                    Assert.Equal(expectLo, decompressed[s * 32 + i * 2]);
                    Assert.Equal(expectHi, decompressed[s * 32 + i * 2 + 1]);
                }
            }
        }

        [Fact]
        public void WritePalette_Single_Slot_Index_3_Updates_Only_That_Slot()
        {
            byte[] rawInit = BuildMultiSlotRaw(5);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            // New Gray palette = pure red
            uint[] r = new uint[16], g = new uint[16], b = new uint[16];
            for (int i = 0; i < 16; i++) { r[i] = 31; g[i] = 0; b[i] = 0; }
            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, g, b, 3, false, undo: null);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            // Slot 3 = pure red (0x001F).
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[3 * 32 + i * 2] | (decompressed[3 * 32 + i * 2 + 1] << 8));
                Assert.Equal(0x001F, c);
            }
            // Slots 0,1,2,4 unchanged.
            foreach (int s in new[] { 0, 1, 2, 4 })
            {
                for (int i = 0; i < 32; i++)
                {
                    Assert.Equal(rawInit[s * 32 + i], decompressed[s * 32 + i]);
                }
            }
        }

        [Fact]
        public void WritePalette_OverrideAll_Replaces_Every_Slot()
        {
            byte[] rawInit = BuildMultiSlotRaw(5);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            // Pure blue for all slots.
            uint[] r = new uint[16], g = new uint[16], b = new uint[16];
            for (int i = 0; i < 16; i++) { r[i] = 0; g[i] = 0; b[i] = 31; }
            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, g, b, 0, true, undo: null);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            Assert.Equal(5 * 32, decompressed.Length);
            // Every slot is now pure blue (0x7C00).
            for (int s = 0; s < 5; s++)
            {
                for (int i = 0; i < 16; i++)
                {
                    ushort c = (ushort)(decompressed[s * 32 + i * 2] | (decompressed[s * 32 + i * 2 + 1] << 8));
                    Assert.Equal(0x7C00, c);
                }
            }
        }

        [Fact]
        public void WritePalette_Single_Slot_Index_Beyond_End_Grows_Buffer()
        {
            // Initial: only 1 slot. Write to slot index 2 (out of range)
            // — buffer must grow to 3 slots, leaving slots 0..1 padded with zeros
            // (the System.Array.Resize zero-fills).
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] rawInit = PackRgb555(rInit, gInit, bInit);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            uint[] r = new uint[16], g = new uint[16], b = new uint[16];
            for (int i = 0; i < 16; i++) { r[i] = 1; g[i] = 2; b[i] = 3; }
            uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, r, g, b, 2, false, undo: null);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            Assert.Equal(3 * 32, decompressed.Length);
            // Slot 0: original uniform palette (untouched).
            for (int i = 0; i < 32; i++)
            {
                Assert.Equal(rawInit[i], decompressed[i]);
            }
            // Slot 1: zero-filled by the resize.
            for (int i = 0; i < 32; i++)
            {
                Assert.Equal((byte)0, decompressed[32 + i]);
            }
            // Slot 2: the new palette.
            ushort expected = (ushort)((3 << 10) | (2 << 5) | 1);
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[2 * 32 + i * 2] | (decompressed[2 * 32 + i * 2 + 1] << 8));
                Assert.Equal(expected, c);
            }
        }

        // ===== Undo coverage via the ambient scope =====

        [Fact]
        public void WritePalette_UndoCovers_InPlace_ViaAmbientScope()
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
            using (ROM.BeginUndoScope(undo))
            {
                var (rNew, gNew, bNew) = MakeUniformPalette();
                uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, 0, false, undo: undo);
                Assert.NotEqual(U.NOT_FOUND, result);
            }
            // Ambient scope recorded at least one entry per write_*: write_range
            // + (smaller-case) write_fill. Each entry must appear EXACTLY ONCE
            // (no double-recording).
            Assert.True(undo.list.Count > 0);
            // No duplicate-address entries with identical (addr, data.Length).
            // The exact count depends on whether the size is identical or
            // smaller; both cases produce >= 1 entry.
        }

        [Fact]
        public void WritePalette_UndoCovers_Reallocation_ViaAmbientScope()
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
            using (ROM.BeginUndoScope(undo))
            {
                var (rNew, gNew, bNew) = MakeUniquePalette();
                uint result = UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, 0, false, undo: undo);
                Assert.NotEqual(U.NOT_FOUND, result);
                Assert.NotEqual(origP12, result);
            }
            // Ambient scope captured entries for write_range(appendOffset, compressed)
            // and write_p32(rowP12SlotOffset, newPointer).
            Assert.True(undo.list.Count > 0);
            // The P12 slot entry MUST appear so undo can revert the pointer.
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

        [Fact]
        public void WritePalette_Undo_NotDoubleRecorded()
        {
            // Regression: when the caller has opened an ambient undo scope AND
            // passes the same UndoData to WritePalette, the helper must NOT
            // double-record. Verified by counting how many entries cover any
            // single address inside the compressed write region.
            var (rInit, gInit, bInit) = MakeUniformPalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            var undo = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "regression",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            using (ROM.BeginUndoScope(undo))
            {
                var (rNew, gNew, bNew) = MakeUniformPalette();
                UnitPaletteWriteCore.WritePalette(rom, p12Slot, rNew, gNew, bNew, 0, false, undo: undo);
            }

            // Each address should appear AT MOST ONCE in the undo list
            // (allowing for write_range covering multiple bytes; we count
            // overlapping ranges instead).
            // The total byte coverage should equal the sum of distinct write
            // calls, NOT double that.
            var seen = new HashSet<uint>();
            int doubles = 0;
            foreach (var pos in undo.list)
            {
                for (uint a = pos.addr; a < pos.addr + pos.data.Length; a++)
                {
                    if (!seen.Add(a)) doubles++;
                }
            }
            Assert.Equal(0, doubles);
        }

        // ===== Encoding helper =====

        [Fact]
        public void PackPalette_EncodesRgb555InGbaOrder()
        {
            uint[] r = { 31, 0, 0 };
            uint[] g = { 0, 31, 0 };
            uint[] b = { 0, 0, 31 };
            var rFull = new uint[16];
            var gFull = new uint[16];
            var bFull = new uint[16];
            System.Array.Copy(r, rFull, 3);
            System.Array.Copy(g, gFull, 3);
            System.Array.Copy(b, bFull, 3);

            byte[] raw = UnitPaletteWriteCore.PackRgb555(rFull, gFull, bFull);
            Assert.Equal(32, raw.Length);
            Assert.Equal(0x1F, raw[0]);
            Assert.Equal(0x00, raw[1]);
            ushort c1 = (ushort)(raw[2] | (raw[3] << 8));
            Assert.Equal(0x03E0, c1);
            ushort c2 = (ushort)(raw[4] | (raw[5] << 8));
            Assert.Equal(0x7C00, c2);
        }

        // ===== #1067: New Palette Allocation (AllocNewPalette) =====

        /// <summary>Decode a 16-color RGB555 bank from the decompressed buffer at
        /// the given slot index into per-channel arrays.</summary>
        static (uint[] r, uint[] g, uint[] b) DecodeBank(byte[] decompressed, int slot)
        {
            var r = new uint[16];
            var g = new uint[16];
            var b = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[slot * 32 + i * 2] | (decompressed[slot * 32 + i * 2 + 1] << 8));
                r[i] = (uint)(c & 0x1F);
                g[i] = (uint)((c >> 5) & 0x1F);
                b[i] = (uint)((c >> 10) & 0x1F);
            }
            return (r, g, b);
        }

        // (1) AllocNewPalette repoints P12 to a NEW offset even when the
        // recompressed bytes would have fit in-place (forced append).
        [Fact]
        public void AllocNewPalette_ForcesAppend_RepointsP12_EvenWhenFitsInPlace()
        {
            // Two-bank initial stream so multi-bank is exercised.
            byte[] rawInit = BuildMultiSlotRaw(2);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);
            int origRomLen = rom.Data.Length;

            // Edit bank 0 to a UNIFORM palette so the recompressed bytes would
            // compress SMALLER than (or equal to) the original — i.e. the
            // default WritePalette path would have taken the in-place branch.
            var (rNew, gNew, bNew) = MakeUniformPalette();

            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 0, false);

            Assert.NotEqual(U.NOT_FOUND, result);
            // P12 was repointed to a brand-new offset (NOT the original).
            Assert.NotEqual(origP12, result);
            Assert.Equal(result, rom.u32(p12Slot));
            Assert.NotEqual(origP12, rom.u32(p12Slot));
            // ROM grew (appended at the end).
            Assert.True(rom.Data.Length > origRomLen, $"ROM should grow: was {origRomLen}, now {rom.Data.Length}");
            Assert.True(U.toOffset(result) >= (uint)origRomLen, "new block should live in the appended region");
        }

        // (2) The new stream decodes back to the edited colors for the selected bank.
        [Fact]
        public void AllocNewPalette_NewStream_DecodesEditedBank()
        {
            byte[] rawInit = BuildMultiSlotRaw(2);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            // Distinctive edited palette for bank 1.
            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 1, false);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            Assert.Equal(2 * 32, decompressed.Length);
            var (rGot, gGot, bGot) = DecodeBank(decompressed, 1);
            Assert.Equal(rNew, rGot);
            Assert.Equal(gNew, gGot);
            Assert.Equal(bNew, bGot);
        }

        // (3) The untouched bank(s) are preserved verbatim in the new stream.
        [Fact]
        public void AllocNewPalette_PreservesUntouchedBanks()
        {
            byte[] rawInit = BuildMultiSlotRaw(2);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            // Edit bank 0 only -> bank 1 must survive byte-identical to rawInit.
            uint[] r = new uint[16], g = new uint[16], b = new uint[16];
            for (int i = 0; i < 16; i++) { r[i] = 31; g[i] = 31; b[i] = 31; } // pure white
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, r, g, b, 0, false);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            Assert.Equal(2 * 32, decompressed.Length);
            // Bank 0 = pure white.
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(decompressed[i * 2] | (decompressed[i * 2 + 1] << 8));
                Assert.Equal(0x7FFF, c);
            }
            // Bank 1 = untouched (verbatim from rawInit).
            for (int i = 0; i < 32; i++)
            {
                Assert.Equal(rawInit[32 + i], decompressed[32 + i]);
            }
        }

        // (4) The OLD compressed block bytes are unchanged after alloc (no recycle).
        [Fact]
        public void AllocNewPalette_LeavesOldBlockUntouched()
        {
            byte[] rawInit = BuildMultiSlotRaw(2);
            byte[] initialCompressed = LZ77.compress(rawInit);
            uint initialPaletteOffset = 0x200;
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed, initialPaletteOffset);
            uint origP12 = rom.u32(p12Slot);
            uint oldOffset = U.toOffset(origP12);
            uint oldLen = LZ77.getCompressedSize(rom.Data, oldOffset);

            // Snapshot the old compressed block.
            byte[] oldBlock = new byte[oldLen];
            System.Array.Copy(rom.Data, oldOffset, oldBlock, 0, (int)oldLen);

            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 0, false);
            Assert.NotEqual(U.NOT_FOUND, result);
            // The new block lives elsewhere.
            Assert.NotEqual(oldOffset, U.toOffset(result));

            // The OLD block bytes are byte-identical (shared-palette safety).
            for (uint i = 0; i < oldLen; i++)
            {
                Assert.Equal(oldBlock[i], rom.Data[oldOffset + i]);
            }
        }

        // (5) Invalid/zero P12 -> deterministic fresh SINGLE 32-byte bank.
        [Fact]
        public void AllocNewPalette_ZeroP12_BuildsFreshSingleBank()
        {
            // Build a ROM whose row P12 slot is ZERO (no pointer at all).
            byte[] data = new byte[0x10000];
            uint rowAddr = 0x40;
            data[rowAddr + 0] = (byte)'P';
            data[rowAddr + 1] = (byte)'a';
            data[rowAddr + 2] = (byte)'l';
            // P12 (offset +12) left as 0.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            uint p12Slot = rowAddr + 12;
            int origRomLen = rom.Data.Length;

            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 0, false);

            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.Equal(result, rom.u32(p12Slot));
            Assert.True(rom.Data.Length > origRomLen);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(result));
            // Exactly ONE 32-byte bank (deterministic single slot).
            Assert.Equal(32, decompressed.Length);
            var (rGot, gGot, bGot) = DecodeBank(decompressed, 0);
            Assert.Equal(rNew, rGot);
            Assert.Equal(gNew, gGot);
            Assert.Equal(bNew, bGot);
        }

        [Fact]
        public void AllocNewPalette_NonLZ77P12_BuildsFreshSingleBank()
        {
            // P12 points at junk (non-LZ77). The fresh-single-bank path applies.
            byte[] data = new byte[0x10000];
            for (int i = 0; i < 32; i++) data[0x200 + i] = 0xAB; // junk
            uint rowAddr = 0x40;
            uint gbaPtr = U.toPointer(0x200);
            data[rowAddr + 12] = (byte)(gbaPtr & 0xFF);
            data[rowAddr + 13] = (byte)((gbaPtr >> 8) & 0xFF);
            data[rowAddr + 14] = (byte)((gbaPtr >> 16) & 0xFF);
            data[rowAddr + 15] = (byte)((gbaPtr >> 24) & 0xFF);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            uint p12Slot = rowAddr + 12;

            var (rNew, gNew, bNew) = MakeUniformPalette();
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 0, false);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            Assert.Equal(32, decompressed.Length);
            var (rGot, gGot, bGot) = DecodeBank(decompressed, 0);
            Assert.Equal(rNew, rGot);
            Assert.Equal(gNew, gGot);
            Assert.Equal(bNew, bGot);
            // The junk source bytes are left untouched.
            for (int i = 0; i < 32; i++) Assert.Equal(0xAB, rom.Data[0x200 + i]);
        }

        // (5a) Copilot review: zero P12 + paletteIndex=2 (non-override) -> fresh
        // buffer is >=3 banks, ONLY bank 2 carries the edited colors, banks 0/1
        // are all-zero/black.
        [Fact]
        public void AllocNewPalette_ZeroP12_HonorsPaletteIndex_NonOverride()
        {
            byte[] data = new byte[0x10000];
            uint rowAddr = 0x40;
            data[rowAddr + 0] = (byte)'P';
            // P12 (offset +12) left as 0.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            uint p12Slot = rowAddr + 12;

            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 2, false);
            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.Equal(result, rom.u32(p12Slot));

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(result));
            // (paletteIndex + 1) = 3 banks.
            Assert.Equal(3 * 32, decompressed.Length);
            // Banks 0 and 1 are all-zero/black (untouched).
            for (int i = 0; i < 2 * 32; i++) Assert.Equal((byte)0, decompressed[i]);
            // Bank 2 = the edited colors.
            var (rGot, gGot, bGot) = DecodeBank(decompressed, 2);
            Assert.Equal(rNew, rGot);
            Assert.Equal(gNew, gGot);
            Assert.Equal(bNew, bGot);
        }

        // (5b) Copilot review: zero P12 + paletteIndex=2 + isOverrideAll=true ->
        // ALL 3 banks decode to the edited colors.
        [Fact]
        public void AllocNewPalette_ZeroP12_OverrideAll_FillsEveryBank()
        {
            byte[] data = new byte[0x10000];
            uint rowAddr = 0x40;
            data[rowAddr + 0] = (byte)'P';
            // P12 (offset +12) left as 0.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom;
            uint p12Slot = rowAddr + 12;

            var (rNew, gNew, bNew) = MakeUniquePalette();
            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 2, true);
            Assert.NotEqual(U.NOT_FOUND, result);

            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(rom.u32(p12Slot)));
            Assert.Equal(3 * 32, decompressed.Length);
            // EVERY bank decodes to the edited colors.
            for (int s = 0; s < 3; s++)
            {
                var (rGot, gGot, bGot) = DecodeBank(decompressed, s);
                Assert.Equal(rNew, rGot);
                Assert.Equal(gNew, gGot);
                Assert.Equal(bNew, bGot);
            }
        }

        // (6) Outer Undo.Rollback restores byte-identical (length + P12 + bytes).
        [Fact]
        public void AllocNewPalette_OuterUndo_RestoresByteIdentical()
        {
            byte[] rawInit = BuildMultiSlotRaw(2);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);

            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.Undo = new Undo();
                uint origP12 = rom.u32(p12Slot);
                byte[] before = (byte[])rom.Data.Clone();

                var (rNew, gNew, bNew) = MakeUniquePalette();
                Undo.UndoData ud = CoreState.Undo.NewUndoData("AllocNewPalette outer-undo");
                using (ROM.BeginUndoScope(ud))
                {
                    uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, rNew, gNew, bNew, 0, false);
                    Assert.NotEqual(U.NOT_FOUND, result);
                }
                if (ud.list.Count > 0)
                {
                    CoreState.Undo.Push(ud);
                    CoreState.Undo.RunUndo();
                }

                // Byte-identical: length, P12, and every byte.
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(origP12, rom.u32(p12Slot));
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] != rom.Data[i])
                        Assert.Fail($"Byte mismatch at 0x{i:X06}: before=0x{before[i]:X02}, after-undo=0x{rom.Data[i]:X02}");
                }
            }
            finally { CoreState.Undo = prevUndo; }
        }

        // (7) Regression: WritePalette(forceNewAlloc:false) keeps in-place behavior
        // byte-identical to the pre-change path (P12 unchanged, in-place write).
        [Fact]
        public void WritePalette_ForceNewAllocFalse_StillInPlace_WhenFits()
        {
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);
            int origLen = rom.Data.Length;

            // Same-size rewrite -> in-place when forceNewAlloc is false (default).
            uint result = UnitPaletteWriteCore.WritePalette(
                rom, p12Slot, rInit, gInit, bInit, 0, false, forceNewAlloc: false, undo: null);

            Assert.Equal(origP12, result);
            Assert.Equal(origP12, rom.u32(p12Slot)); // P12 unchanged
            Assert.Equal(origLen, rom.Data.Length);   // no growth (in-place)
            byte[] decompressed = LZ77.decompress(rom.Data, U.toOffset(origP12));
            Assert.Equal(PackRgb555(rInit, gInit, bInit), decompressed);
        }

        [Fact]
        public void WritePalette_ForceNewAllocTrue_AppendsEvenWhenFits()
        {
            // Direct WritePalette force-append (the seam AllocNewPalette uses).
            var (rInit, gInit, bInit) = MakeUniquePalette();
            byte[] initialCompressed = LZ77.compress(PackRgb555(rInit, gInit, bInit));
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);
            int origLen = rom.Data.Length;

            uint result = UnitPaletteWriteCore.WritePalette(
                rom, p12Slot, rInit, gInit, bInit, 0, false, forceNewAlloc: true, undo: null);

            Assert.NotEqual(origP12, result);
            Assert.Equal(result, rom.u32(p12Slot));
            Assert.True(rom.Data.Length > origLen);
        }

        // (8) Fault restore: a bad input that passes the rom-null guard but fails
        // validation inside the write must return NOT_FOUND with the ROM
        // byte-identical (no partial mutation). We inject an out-of-range channel,
        // which AppendFreshBanks / WritePalette both reject AFTER the
        // snapshot is taken — proving the no-mutation-on-fault contract.
        [Fact]
        public void AllocNewPalette_BadInput_ReturnsNotFound_NoMutation()
        {
            byte[] rawInit = BuildMultiSlotRaw(2);
            byte[] initialCompressed = LZ77.compress(rawInit);
            var (rom, _, p12Slot) = BuildRomWithPaletteAt(initialCompressed);
            uint origP12 = rom.u32(p12Slot);
            byte[] before = (byte[])rom.Data.Clone();

            var (r, g, b) = MakeUniquePalette();
            r[3] = 0xFFFFFFFF; // out of 0-31 range -> rejected before any write

            uint result = UnitPaletteWriteCore.AllocNewPalette(rom, p12Slot, r, g, b, 0, false);

            Assert.Equal(U.NOT_FOUND, result);
            // ROM byte-identical: length, P12, and every byte.
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.Equal(origP12, rom.u32(p12Slot));
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[i], rom.Data[i]);
            }
        }

        [Fact]
        public void AllocNewPalette_NullRom_ReturnsNotFound()
        {
            var (r, g, b) = MakeUniquePalette();
            Assert.Equal(U.NOT_FOUND, UnitPaletteWriteCore.AllocNewPalette(null!, 0x4Cu, r, g, b, 0, false));
        }
    }
}
