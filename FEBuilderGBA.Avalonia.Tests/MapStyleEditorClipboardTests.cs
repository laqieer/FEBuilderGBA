// SPDX-License-Identifier: GPL-3.0-or-later
// Round-trip + normalization tests for MapStyleEditorView's #672 Slice A
// Palette Export / Import / Clipboard handlers.
//
// The clipboard staging path is fronted by static helpers exposed via
// `internal static` (InternalsVisibleTo to FEBuilderGBA.Avalonia.Tests)
// so we can validate the pack/unpack + normalization round-trip without
// driving the file picker / clipboard / VM through a headless harness.

using System;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MapStyleEditorClipboardTests
    {
        /// <summary>
        /// Pack 16 RGB triplets, export to JASC-PAL, import back, then
        /// unpack: every channel must match the source. Proves the
        /// BGR555 LE wire format used by the Export button round-trips
        /// through PaletteFormatConverter.
        /// </summary>
        [AvaloniaFact]
        public void PaletteExport_RoundTrip_BytesMatchBgr555()
        {
            var view = new MapStyleEditorView();
            var vm = (MapStyleEditorViewModel)view.DataViewModel!;

            // Seed VM with deterministic per-row pattern.
            for (int i = 1; i <= 16; i++)
            {
                vm.SetColorR(i, (ushort)((i - 1) & 0x1F));
                vm.SetColorG(i, (ushort)((i + 5) & 0x1F));
                vm.SetColorB(i, (ushort)((i + 11) & 0x1F));
            }

            byte[] bytes = view.PackPaletteToBytes();
            Assert.Equal(32, bytes.Length);

            // Export to JASC-PAL then import back to GBA bytes.
            byte[] exported = PaletteFormatConverter.ExportToFormat(bytes, PaletteFormat.JascPal);
            byte[] reimported = PaletteFormatConverter.ImportFromFormat(exported, PaletteFormat.JascPal);

            byte[] normalized = MapStyleEditorView.NormalizeImportedPalette(reimported)!;
            Assert.NotNull(normalized);
            Assert.Equal(32, normalized.Length);

            // Compare every halfword. The 5-5-5 channels are bit-truncated
            // so the round-trip is lossless on the masked low bits.
            for (int i = 0; i < 16; i++)
            {
                ushort original = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
                ushort returned = (ushort)(normalized[i * 2] | (normalized[i * 2 + 1] << 8));
                Assert.Equal(original, returned);
            }
        }

        /// <summary>
        /// v2 Copilot review item 1: inputs shorter than 32 bytes must be
        /// rejected outright (no truncation, no partial-fill). Returns
        /// null and lets the caller surface a user-facing error.
        /// </summary>
        [Fact]
        public void PaletteImport_RejectsTooShort()
        {
            Assert.Null(MapStyleEditorView.NormalizeImportedPalette(null));
            Assert.Null(MapStyleEditorView.NormalizeImportedPalette(Array.Empty<byte>()));
            Assert.Null(MapStyleEditorView.NormalizeImportedPalette(new byte[1]));
            Assert.Null(MapStyleEditorView.NormalizeImportedPalette(new byte[31]));
        }

        /// <summary>
        /// v2 Copilot review item 1: inputs larger than 32 bytes (e.g.
        /// ACT files = 768 bytes for 256 colors after PaletteFormatConverter
        /// import) must be truncated to the FIRST 32 bytes (16 colors).
        /// </summary>
        [Fact]
        public void PaletteImport_NormalizesToFirst32Bytes()
        {
            // 768 bytes = 256-color ACT import (palette bytes after RgbToGba).
            byte[] large = new byte[768];
            for (int i = 0; i < 32; i++) large[i] = (byte)(0x80 + i); // distinctive prefix
            for (int i = 32; i < 768; i++) large[i] = 0xCC;            // detection sentinel

            byte[]? normalized = MapStyleEditorView.NormalizeImportedPalette(large);

            Assert.NotNull(normalized);
            Assert.Equal(32, normalized!.Length);
            for (int i = 0; i < 32; i++)
            {
                Assert.Equal((byte)(0x80 + i), normalized[i]);
            }
            // Must NOT include any byte from the truncated tail.
            Assert.DoesNotContain((byte)0xCC, normalized);
        }

        /// <summary>
        /// Exact 32-byte input must pass through unchanged (the common
        /// 16-color JASC-PAL / GBA raw case). A second call against the
        /// returned buffer is still valid (idempotent).
        /// </summary>
        [Fact]
        public void PaletteImport_Exact32_PassesThrough()
        {
            byte[] exact = new byte[32];
            for (int i = 0; i < 32; i++) exact[i] = (byte)(i * 7);

            byte[]? once = MapStyleEditorView.NormalizeImportedPalette(exact);
            Assert.NotNull(once);
            Assert.Equal(32, once!.Length);
            for (int i = 0; i < 32; i++) Assert.Equal((byte)(i * 7), once[i]);

            byte[]? twice = MapStyleEditorView.NormalizeImportedPalette(once);
            Assert.NotNull(twice);
            Assert.Equal(32, twice!.Length);
        }
    }
}
