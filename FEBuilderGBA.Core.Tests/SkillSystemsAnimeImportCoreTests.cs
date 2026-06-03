// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the cross-platform SkillSystems anime IMPORT seam (SLICE 1 of
    /// #913). FE8J path only; FE8U returns a clean not-supported error with ZERO
    /// mutation. All tests are fully synthetic — no real ROM file needed:
    ///
    ///   * Parse: D / S{hex} / {wait} {png} handling + 0x3d1 sound-id default.
    ///   * Round-trip: import a script + synthetic PNGs into a synthetic FE8J
    ///     ROM, then EXPORT the written slot and assert the same frame count +
    ///     (id,wait) sequence + dedup-by-filename id stability.
    ///   * Structural: frames-table ends with the 4-byte 0xFFFF,0xFFFF; each
    ///     palette region is 0x20 RAW (not LZ77); sound_id round-trips incl the
    ///     0x3d1 default; mainData word order; sound_id written RAW not pointer.
    ///   * Corruption guard: a forced fault BETWEEN a mid-write WriteAmbient and
    ///     the final repoint leaves rom.Data BYTE-IDENTICAL to the pre-snapshot.
    ///   * FE8U deferral: returns the not-supported error + ZERO bytes mutated.
    ///   * Guards: foreign ROM refused; bad script rejected; >16-colour PNG
    ///     rejected — all with no mutation.
    ///
    /// [Collection("SharedState")] because the tests mutate CoreState.ROM /
    /// CoreState.ImageService / CoreState.Undo.
    /// </summary>
    [Collection("SharedState")]
    public class SkillSystemsAnimeImportCoreTests
    {
        // ---------------------------------------------------------------
        // ParseScript
        // ---------------------------------------------------------------

        [Fact]
        public void ParseScript_DefaultSoundId_Is0x3d1_WhenNoSLine()
        {
            var s = SkillSystemsAnimeImportCore.ParseScript(new[] { "3 g000.png" });
            Assert.Equal("", s.Error);
            Assert.Equal(0x3d1u, s.SoundId);
            Assert.False(s.IsDefender);
            Assert.Single(s.Frames);
            Assert.Equal(3u, s.Frames[0].Wait);
            Assert.Equal("g000.png", s.Frames[0].PngName);
        }

        [Fact]
        public void ParseScript_SLine_ParsedAsHex_DLine_SetsDefender()
        {
            var s = SkillSystemsAnimeImportCore.ParseScript(new[]
            {
                "D #defender",
                "S1A2B",
                "5 a.png",
                "7 b.png",
            });
            Assert.Equal("", s.Error);
            Assert.True(s.IsDefender);
            Assert.Equal(0x1A2Bu, s.SoundId);
            Assert.Equal(2, s.Frames.Count);
            Assert.Equal("a.png", s.Frames[0].PngName);
            Assert.Equal("b.png", s.Frames[1].PngName);
        }

        [Fact]
        public void ParseScript_NoFrames_ReturnsError()
        {
            var s = SkillSystemsAnimeImportCore.ParseScript(new[] { "S0001", "# comment" });
            Assert.NotEqual("", s.Error);
            Assert.Empty(s.Frames);
        }

        // ---------------------------------------------------------------
        // Round-trip: import then export the written slot
        // ---------------------------------------------------------------

        [Fact]
        public void Import_FE8J_RoundTrip_ExportMatchesScript()
        {
            WithFE8J((rom, slot) =>
            {
                // Three script frames; two reference the SAME PNG (dedup by
                // filename → ids 0,1,0). The (id,wait) sequence must survive a
                // re-export.
                string[] script =
                {
                    "S001A",
                    "3 g000.png",
                    "5 g001.png",
                    "9 g000.png",
                };

                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider);
                Assert.Equal("", err);

                // The slot was repointed to the mainData (anime config) block.
                // Export takes the anime ADDRESS (the config block), not the slot.
                uint animeAddr = rom.p32(slot);
                var exported = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeAddr);
                Assert.Equal("", exported.Error);
                Assert.Equal(0x1Au, exported.SoundId);
                Assert.Equal(3, exported.Frames.Count);

                // (id, wait) sequence: 0/3, 1/5, 0/9 (dedup-by-filename → id 0 reused).
                Assert.Equal(0u, exported.Frames[0].Id);
                Assert.Equal(3u, exported.Frames[0].Wait);
                Assert.Equal(1u, exported.Frames[1].Id);
                Assert.Equal(5u, exported.Frames[1].Wait);
                Assert.Equal(0u, exported.Frames[2].Id);
                Assert.Equal(9u, exported.Frames[2].Wait);
            });
        }

        [Fact]
        public void Import_FE8J_DefaultSoundId_RoundTrips()
        {
            WithFE8J((rom, slot) =>
            {
                string[] script = { "1 g000.png" }; // no S line → 0x3d1
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider);
                Assert.Equal("", err);

                uint animeAddr = rom.p32(slot);
                var exported = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeAddr);
                Assert.Equal("", exported.Error);
                Assert.Equal(0x3d1u, exported.SoundId);
            });
        }

        // ---------------------------------------------------------------
        // Structural asserts on the written bytes
        // ---------------------------------------------------------------

        [Fact]
        public void Import_FE8J_StructuralLayout_IsWFCorrect()
        {
            WithFE8J((rom, slot) =>
            {
                string[] script =
                {
                    "S0042",
                    "2 g000.png",
                    "4 g001.png",
                };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider);
                Assert.Equal("", err);

                // mainData = [frames, tsalist, imagelist, pallist, sound_id].
                uint cfg = rom.p32(slot);
                uint framesOff   = rom.p32(cfg + 0);
                uint tsalistOff  = rom.p32(cfg + 4);
                uint imagelistOff = rom.p32(cfg + 8);
                uint pallistOff  = rom.p32(cfg + 12);
                uint soundId     = rom.u32(cfg + 16); // RAW u32, NOT a pointer

                // sound_id is stored RAW: 0x0042, NOT 0x08000042.
                Assert.Equal(0x0042u, soundId);

                // CRITICAL 3: frames table ends with a 4-byte 0xFFFF,0xFFFF.
                // 2 frames → 2*4 bytes payload, then 4-byte terminator.
                Assert.Equal(0xFFFFu, (uint)rom.u16(framesOff + 8));
                Assert.Equal(0xFFFFu, (uint)rom.u16(framesOff + 10));
                // And the frame payload before it: id 0 wait 2, id 1 wait 4.
                Assert.Equal(0u, (uint)rom.u16(framesOff + 0));
                Assert.Equal(2u, (uint)rom.u16(framesOff + 2));
                Assert.Equal(1u, (uint)rom.u16(framesOff + 4));
                Assert.Equal(4u, (uint)rom.u16(framesOff + 6));

                // Two unique frames → each parallel list holds 2 pointers.
                uint pal0 = rom.p32(pallistOff + 0);
                uint pal1 = rom.p32(pallistOff + 4);
                uint img0 = rom.p32(imagelistOff + 0);
                uint tsa0 = rom.p32(tsalistOff + 0);
                Assert.True(U_isSafetyOffset(rom, pal0));
                Assert.True(U_isSafetyOffset(rom, pal1));

                // CRITICAL 1: palette region is 0x20 RAW bytes — NOT an LZ77
                // stream. An LZ77 stream begins with 0x10; assert the first byte
                // is NOT a plausible LZ77 header AND the 0x20-byte region decodes
                // as the raw palette we provided (0,1,2,... low bytes).
                Assert.NotEqual(0x10, rom.Data[pal0]);
                for (int i = 0; i < 0x20; i++)
                    Assert.Equal((byte)i, rom.Data[pal0 + (uint)i]);

                // The image + tsa regions, by contrast, ARE LZ77 (header 0x10).
                Assert.Equal(0x10, rom.Data[img0]);
                Assert.Equal(0x10, rom.Data[tsa0]);
            });
        }

        // ---------------------------------------------------------------
        // CRITICAL corruption guard — forced fault mid-write
        // ---------------------------------------------------------------

        [Fact]
        public void Import_FE8J_ForcedFaultMidWrite_LeavesRomByteIdentical()
        {
            WithFE8J((rom, slot) =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                uint slotBefore = rom.u32(slot);

                string[] script =
                {
                    "S0001",
                    "1 g000.png",
                    "2 g001.png",
                };

                // Inject a fault AFTER the first frame's regions are written but
                // BEFORE the slot is repointed (the helper fires faultInjector
                // once, at i==0). The snapshot-restore must roll everything back.
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider,
                    faultInjector: () => throw new InvalidOperationException("injected fault"));

                Assert.NotEqual("", err);

                // The slot did NOT flip and NO freespace bytes leaked.
                Assert.Equal(slotBefore, rom.u32(slot));
                Assert.Equal(before.Length, rom.Data.Length);
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] != rom.Data[i])
                    {
                        Assert.Fail($"ROM byte {i} changed: {before[i]:X2} -> {rom.Data[i]:X2}");
                    }
                }
            });
        }

        // ---------------------------------------------------------------
        // FE8U deferral — zero mutation
        // ---------------------------------------------------------------

        [Fact]
        public void Import_FE8U_NotSupported_ZeroMutation()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            var prevUndo = CoreState.Undo;
            try
            {
                ROM rom = MakeFE8URom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                CoreState.Undo = new Undo();

                byte[] before = (byte[])rom.Data.Clone();
                uint slot = 0x300u;

                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, new[] { "1 g000.png" }, slot, FakeProvider);

                Assert.Contains("FE8U", err);
                // ZERO bytes mutated.
                Assert.Equal(before.Length, rom.Data.Length);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], rom.Data[i]);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
            }
        }

        // ---------------------------------------------------------------
        // Guards
        // ---------------------------------------------------------------

        [Fact]
        public void Import_ForeignRom_Refused_NoMutation()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            var prevUndo = CoreState.Undo;
            try
            {
                ROM active = MakeFE8JRom();
                ROM foreign = MakeFE8JRom();
                CoreState.ROM = active;
                CoreState.ImageService = new StubImageService();
                CoreState.Undo = new Undo();

                byte[] before = (byte[])foreign.Data.Clone();
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    foreign, new[] { "1 g000.png" }, 0x300u, FakeProvider);

                Assert.NotEqual("", err);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], foreign.Data[i]);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
            }
        }

        [Fact]
        public void Import_BadScript_Rejected_NoMutation()
        {
            WithFE8J((rom, slot) =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, new[] { "S0001", "# no frames" }, slot, FakeProvider);
                Assert.NotEqual("", err);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], rom.Data[i]);
            });
        }

        [Fact]
        public void Import_Over16ColorPng_Rejected_NoMutation()
        {
            WithFE8J((rom, slot) =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                // Provider returns a pixel with index 16 (>15 → 4bpp violation).
                SkillSystemsAnimeImportCore.ImageProvider bad = name =>
                {
                    var idx = new byte[8 * 8];
                    idx[0] = 16; // out of the 4bpp range
                    return (idx, 8, 8, MakePalette());
                };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, new[] { "1 g000.png" }, slot, bad);
                Assert.NotEqual("", err);
                Assert.Contains("16 colours", err);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], rom.Data[i]);
            });
        }

        [Fact]
        public void Import_MissingPng_Rejected_NoMutation()
        {
            WithFE8J((rom, slot) =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                SkillSystemsAnimeImportCore.ImageProvider missing = name => null;
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, new[] { "1 g000.png" }, slot, missing);
                Assert.NotEqual("", err);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], rom.Data[i]);
            });
        }

        // ===============================================================
        // Helpers
        // ===============================================================

        // A deterministic provider: 8x8 indexed image (will be padded to 240×160
        // by the Core), with a 0x20 palette whose bytes are 0,1,2,...,0x1F.
        static (byte[] indexedPixels, int width, int height, byte[] gbaPalette)? FakeProvider(string name)
        {
            var idx = new byte[8 * 8];
            // a couple of low indices so EncodeTSA has >1 colour
            idx[0] = 1;
            idx[63] = 2;
            return (idx, 8, 8, MakePalette());
        }

        static byte[] MakePalette()
        {
            var pal = new byte[0x20];
            for (int i = 0; i < 0x20; i++) pal[i] = (byte)i;
            return pal;
        }

        // Run an action with a synthetic FE8J ROM wired into CoreState, a free
        // anime pointer slot at 0x300 (initialized to 0), and the stub image
        // service. Restores CoreState afterwards.
        static void WithFE8J(Action<ROM, uint> body)
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            var prevUndo = CoreState.Undo;
            try
            {
                ROM rom = MakeFE8JRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                CoreState.Undo = new Undo();

                uint slot = 0x300u; // 4-byte pointer slot, currently 0
                body(rom, slot);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
            }
        }

        static ROM MakeFE8JRom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic_fe8j.gba", new byte[0x1000000], "BE8J01");
            return rom;
        }

        static ROM MakeFE8URom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic_fe8u.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        // isSafetyOffset is internal in Core; wrap the same check locally.
        static bool U_isSafetyOffset(ROM rom, uint addr)
            => addr >= 0x200u && addr < (uint)rom.Data.Length;
    }
}
