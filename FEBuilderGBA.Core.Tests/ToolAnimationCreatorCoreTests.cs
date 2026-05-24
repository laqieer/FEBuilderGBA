// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ToolAnimationCreatorCore (#500) — covers AnimationTypeEnum,
// MapActionFrame record, MakeUniqId, ParseMapActionScript (file path),
// FormatMapActionScript (writeback), ReadFromRom + WriteToRom (direct-ROM path).
//
// Marked [Collection("SharedState")] for the ROM-mutating tests because they
// instantiate a private ROM instance — strictly speaking the file-side tests
// don't share state, but xUnit serializes the whole class which keeps things
// simple and matches the existing BattleAnimeExportCoreTests style.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ToolAnimationCreatorCoreTests
    {
        // ----------------------------------------------------------------
        // MakeUniqId — bit-packing must match WinForms ToolAnimationCreatorForm
        // ----------------------------------------------------------------

        [Fact]
        public void MakeUniqId_PacksTypeInHighByte()
        {
            // WinForms: (((uint)type) << 24) + id
            uint uniq = ToolAnimationCreatorCore.MakeUniqId(
                AnimationTypeEnum.MapActionAnimation, 0x42u);

            uint expected = ((uint)AnimationTypeEnum.MapActionAnimation << 24) | 0x42u;
            Assert.Equal(expected, uniq);
        }

        [Fact]
        public void MakeUniqId_BattleAnime_ZeroType()
        {
            uint uniq = ToolAnimationCreatorCore.MakeUniqId(
                AnimationTypeEnum.BattleAnime, 5u);
            // BattleAnime = 0, so the result is just the id.
            Assert.Equal(5u, uniq);
        }

        [Fact]
        public void MakeUniqId_TypeValuesAreStable()
        {
            // The numeric values of the enum members are baked into the
            // makeup id, so reordering would silently change tab uniqueness.
            // Pin them down here so any future enum reorder fails fast.
            Assert.Equal(0, (int)AnimationTypeEnum.BattleAnime);
            Assert.Equal(1, (int)AnimationTypeEnum.MagicAnime_FEEDitor);
            Assert.Equal(2, (int)AnimationTypeEnum.MagicAnime_CSACreator);
            Assert.Equal(3, (int)AnimationTypeEnum.Skill);
            Assert.Equal(4, (int)AnimationTypeEnum.TSAAnime);
            Assert.Equal(5, (int)AnimationTypeEnum.ROMAnime);
            Assert.Equal(6, (int)AnimationTypeEnum.MapActionAnimation);
        }

        // ----------------------------------------------------------------
        // ParseMapActionScript — file-side input
        // ----------------------------------------------------------------

        [Fact]
        public void ParseMapActionScript_MissingFile_Throws()
        {
            // Missing file is NOT silent success — Copilot CLI plan-review pt 5.
            string bogus = Path.Combine(Path.GetTempPath(),
                "this-file-does-not-exist-" + Guid.NewGuid() + ".txt");
            Assert.Throws<FileNotFoundException>(() =>
                ToolAnimationCreatorCore.ParseMapActionScript(bogus, out _));
        }

        [Fact]
        public void ParseMapActionScript_EmptyFile_ReturnsEmptyListAndNullName()
        {
            string path = WriteTempScript("");
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out string? name);
                Assert.Empty(frames);
                Assert.Null(name);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ParseMapActionScript_WithNameHeader_ParsesName()
        {
            string path = WriteTempScript(
                "//NAME=Hello\n" +
                "1\ttest.png\n"
            );
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out string? name);
                Assert.Equal("Hello", name);
                Assert.Single(frames);
                Assert.Equal(1u, frames[0].Wait);
                Assert.Equal("test.png", frames[0].ImageName);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ParseMapActionScript_TabSeparated_ParsesWaitAndImage()
        {
            string path = WriteTempScript("4\tframe_a.png\n5\tframe_b.png\n");
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out _);
                Assert.Equal(2, frames.Count);
                Assert.Equal(4u, frames[0].Wait);
                Assert.Equal("frame_a.png", frames[0].ImageName);
                Assert.Equal(5u, frames[1].Wait);
                Assert.Equal("frame_b.png", frames[1].ImageName);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ParseMapActionScript_SpaceSeparated_ParsesWaitAndImage()
        {
            // WF importer accepts tab OR space separators — Copilot CLI plan-review pt 5.
            string path = WriteTempScript("4 frame_a.png\n5 frame_b.png\n");
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out _);
                Assert.Equal(2, frames.Count);
                Assert.Equal(4u, frames[0].Wait);
                Assert.Equal("frame_a.png", frames[0].ImageName);
                Assert.Equal(5u, frames[1].Wait);
                Assert.Equal("frame_b.png", frames[1].ImageName);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ParseMapActionScript_BlankAndCommentLines_AreSkipped()
        {
            string path = WriteTempScript(
                "// header comment\n" +
                "\n" +
                "3\ta.png\n" +
                "// inline-comment\n" +
                "  \n" +
                "4\tb.png\n"
            );
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out _);
                Assert.Equal(2, frames.Count);
                Assert.Equal(3u, frames[0].Wait);
                Assert.Equal(4u, frames[1].Wait);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ParseMapActionScript_LineWithSound_ParsesDecimalAndHex()
        {
            string path = WriteTempScript(
                "1\ta.png\t42\n" +
                "2\tb.png\t0x1A\n"
            );
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out _);
                Assert.Equal(2, frames.Count);
                Assert.Equal(42u, frames[0].Sound);
                Assert.Equal(0x1Au, frames[1].Sound);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ParseMapActionScript_LineWithBadFormat_IsSkipped()
        {
            // No image field → skip (cannot edit / save back meaningfully).
            // No wait field but text → skip (NaN).
            string path = WriteTempScript(
                "1\tgood.png\n" +
                "notanumber\timg.png\n" +
                "garbage_no_separators\n" +
                "2\tgood2.png\n"
            );
            try
            {
                var frames = ToolAnimationCreatorCore.ParseMapActionScript(path, out _);
                Assert.Equal(2, frames.Count);
                Assert.Equal("good.png", frames[0].ImageName);
                Assert.Equal("good2.png", frames[1].ImageName);
            }
            finally { File.Delete(path); }
        }

        // ----------------------------------------------------------------
        // FormatMapActionScript — round-trips the parser
        // ----------------------------------------------------------------

        [Fact]
        public void FormatMapActionScript_RoundTripsFrames()
        {
            var input = new System.Collections.Generic.List<MapActionFrame>
            {
                new MapActionFrame(Wait: 4, ImagePointer: 0, PalettePointer: 0, Sound: 0, ImageName: "a.png"),
                new MapActionFrame(Wait: 5, ImagePointer: 0, PalettePointer: 0, Sound: 0x42, ImageName: "b.png"),
            };
            string text = ToolAnimationCreatorCore.FormatMapActionScript("My Anim", input);

            // The text must round-trip through the parser.
            string path = WriteTempScript(text);
            try
            {
                var parsed = ToolAnimationCreatorCore.ParseMapActionScript(path, out string? name);
                Assert.Equal("My Anim", name);
                Assert.Equal(2, parsed.Count);
                Assert.Equal(4u, parsed[0].Wait);
                Assert.Equal("a.png", parsed[0].ImageName);
                Assert.Equal(0u, parsed[0].Sound);
                Assert.Equal(5u, parsed[1].Wait);
                Assert.Equal("b.png", parsed[1].ImageName);
                Assert.Equal(0x42u, parsed[1].Sound);
            }
            finally { File.Delete(path); }
        }

        // ----------------------------------------------------------------
        // ReadFromRom — direct-from-ROM path used by Map Action entry point
        // ----------------------------------------------------------------

        [Fact]
        public void ReadFromRom_NullRom_ReturnsEmpty()
        {
            var result = ToolAnimationCreatorCore.ReadFromRom(null, 0x1000u);
            Assert.Empty(result);
        }

        [Fact]
        public void ReadFromRom_InvalidOffset_ReturnsEmpty()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x1000]);
            // 0x800000 is way past the synthetic ROM end, so the safety
            // check rejects it.
            var result = ToolAnimationCreatorCore.ReadFromRom(rom, 0x800000u);
            Assert.Empty(result);
        }

        [Fact]
        public void ReadFromRom_StopsOnZeroTerminator()
        {
            // 12 bytes per row. Layout: wait(1) | 00(1) | sound(2) | img(4) | pal(4)
            // Two valid rows, then a zero terminator (term1==0 && term2==0).
            // Place rows at 0x210 (above the 0x200 safety threshold).
            byte[] data = new byte[0x1000];
            uint baseAddr = 0x210;
            // Row 0: wait=3, sound=0x42, img=0x08001234, pal=0x080012AB
            data[baseAddr + 0x0] = 3;              // wait
            data[baseAddr + 0x1] = 0;              // padding
            data[baseAddr + 0x2] = 0x42; data[baseAddr + 0x3] = 0x00; // sound = 0x42
            data[baseAddr + 0x4] = 0x34; data[baseAddr + 0x5] = 0x12; data[baseAddr + 0x6] = 0x00; data[baseAddr + 0x7] = 0x08; // img
            data[baseAddr + 0x8] = 0xAB; data[baseAddr + 0x9] = 0x12; data[baseAddr + 0xA] = 0x00; data[baseAddr + 0xB] = 0x08; // pal

            // Row 1: wait=4, no sound, img=0x08002000, pal=0x08002030
            data[baseAddr + 0xC] = 4;
            data[baseAddr + 0x10] = 0x00; data[baseAddr + 0x11] = 0x20; data[baseAddr + 0x12] = 0x00; data[baseAddr + 0x13] = 0x08;
            data[baseAddr + 0x14] = 0x30; data[baseAddr + 0x15] = 0x20; data[baseAddr + 0x16] = 0x00; data[baseAddr + 0x17] = 0x08;

            // Terminator at baseAddr+0x18: 8 bytes of zero
            // (data is zero-initialised, so no action needed)

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var frames = ToolAnimationCreatorCore.ReadFromRom(rom, baseAddr);
            Assert.Equal(2, frames.Count);
            Assert.Equal(3u, frames[0].Wait);
            Assert.Equal(0x42u, frames[0].Sound);
            Assert.Equal(0x00001234u, frames[0].ImagePointer);
            Assert.Equal(0x000012ABu, frames[0].PalettePointer);
            Assert.Equal(4u, frames[1].Wait);
            Assert.Equal(0u, frames[1].Sound);
            Assert.Equal(0x00002000u, frames[1].ImagePointer);
            Assert.Equal(0x00002030u, frames[1].PalettePointer);
        }

        [Fact]
        public void ReadFromRom_RespectsLookaheadLimit()
        {
            // Without an explicit terminator, the WF code limits the scan to
            // 1MB. Pass a tiny explicit cap and confirm we stop at the cap.
            byte[] data = new byte[0x1000];
            uint baseAddr = 0x210;
            // Row 0: non-zero img so term1 != 0 (won't trigger early
            // terminator). Layout matches `WriteToRom` so the round-trip
            // assumptions hold.
            data[baseAddr + 0x0] = 1; // wait
            data[baseAddr + 0x4] = 0x00; data[baseAddr + 0x5] = 0x10; data[baseAddr + 0x6] = 0x00; data[baseAddr + 0x7] = 0x08; // img
            // Row 1: another non-zero row.
            data[baseAddr + 0xC] = 1; // wait
            data[baseAddr + 0x10] = 0x00; data[baseAddr + 0x11] = 0x20; data[baseAddr + 0x12] = 0x00; data[baseAddr + 0x13] = 0x08;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var frames = ToolAnimationCreatorCore.ReadFromRom(rom, baseAddr, frameLimit: 1u);
            // Confirm the explicit limit stops the loop at exactly 1.
            Assert.Single(frames);
        }

        // ----------------------------------------------------------------
        // WriteToRom — round-trips through ReadFromRom
        // ----------------------------------------------------------------

        [Fact]
        public void WriteToRom_WritesAllFieldsInPlace()
        {
            byte[] data = new byte[0x1000];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            var input = new System.Collections.Generic.List<MapActionFrame>
            {
                new MapActionFrame(Wait: 7, ImagePointer: 0x00100000, PalettePointer: 0x00200000, Sound: 0x55, ImageName: null),
                new MapActionFrame(Wait: 8, ImagePointer: 0x00300000, PalettePointer: 0x00400000, Sound: 0,    ImageName: null),
            };

            // Use 0x400 (above 0x200 safety threshold).
            ToolAnimationCreatorCore.WriteToRom(rom, 0x400u, input, undoData: null);

            // Read back via ReadFromRom — confirm round-trip.
            // The terminator must follow our 2 rows because the buffer was
            // zero-initialised.
            var roundTrip = ToolAnimationCreatorCore.ReadFromRom(rom, 0x400u);
            Assert.Equal(2, roundTrip.Count);
            Assert.Equal(7u, roundTrip[0].Wait);
            Assert.Equal(0x55u, roundTrip[0].Sound);
            Assert.Equal(0x00100000u, roundTrip[0].ImagePointer);
            Assert.Equal(0x00200000u, roundTrip[0].PalettePointer);
            Assert.Equal(8u, roundTrip[1].Wait);
            Assert.Equal(0u, roundTrip[1].Sound);
            Assert.Equal(0x00300000u, roundTrip[1].ImagePointer);
            Assert.Equal(0x00400000u, roundTrip[1].PalettePointer);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        static string WriteTempScript(string content)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "anim-test-" + Guid.NewGuid() + ".txt");
            File.WriteAllText(path, content);
            return path;
        }
    }
}
