// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the cross-platform SkillSystems anime IMPORT seam (SLICE 1 #916
    /// FE8J + SLICE 2 #917 FE8U program-code re-emit). The FE8J tests are fully
    /// synthetic; the FE8U tests need the config/patch2 submodule checked out
    /// (they read the real skillanimtemplate*.dmp via CoreState.BaseDirectory):
    ///
    ///   * Parse: D / S{hex} / {wait} {png} handling + 0x3d1 sound-id default.
    ///   * Round-trip: import a script + synthetic PNGs into a synthetic FE8J/8U
    ///     ROM, then EXPORT the written slot and assert the same frame count +
    ///     (id,wait) sequence + dedup-by-filename id stability + sound id.
    ///   * Structural: frames-table ends with the 4-byte 0xFFFF,0xFFFF; each
    ///     palette region is 0x20 RAW (not LZ77); sound_id round-trips incl the
    ///     0x3d1 default; mainData word order; sound_id written RAW not pointer.
    ///   * FE8U prefix (#917): the written anime region's PREFIX == the exact
    ///     attack/defender .dmp bytes; the export SkipCode resolves the direction.
    ///   * Corruption guard: a forced fault BETWEEN a mid-write WriteAmbient and
    ///     the final repoint leaves rom.Data BYTE-IDENTICAL to the pre-snapshot
    ///     (FE8J AND FE8U — the template prepend opens no partial-write window).
    ///   * GUARD A (#917): a missing selected .dmp → clean no-mutation error.
    ///   * Guards: foreign ROM refused; bad script rejected; >16-colour PNG
    ///     rejected — all with no mutation.
    ///
    /// [Collection("SharedState")] because the tests mutate CoreState.ROM /
    /// CoreState.ImageService / CoreState.Undo / CoreState.BaseDirectory.
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
        // SLICE 2 (#917): FE8U program-template re-emit
        // ---------------------------------------------------------------

        [Fact]
        public void Import_FE8U_Attack_RoundTrip_PrefixIsAttackTemplate()
        {
            byte[] attackDmp = ReadDmp("skillanimtemplate_2016_11_04.dmp");
            WithFE8U((rom, slot) =>
            {
                // No D line → attack template. Two unique frames + a reused one.
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

                // The slot points at the program-template START (the anime addr).
                uint animeAddr = rom.p32(slot);

                // GUARD C: the written region's PREFIX == the attack .dmp VERBATIM.
                for (int i = 0; i < attackDmp.Length; i++)
                    Assert.Equal(attackDmp[i], rom.Data[(uint)animeAddr + (uint)i]);

                // Export resolves the config via SkipCode (which skips the prefix)
                // and reproduces frames/(id,wait)/sound, attack direction.
                var exported = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeAddr);
                Assert.Equal("", exported.Error);
                Assert.False(exported.IsDefender);
                Assert.Equal(0x1Au, exported.SoundId);
                Assert.Equal(3, exported.Frames.Count);
                Assert.Equal(0u, exported.Frames[0].Id);
                Assert.Equal(3u, exported.Frames[0].Wait);
                Assert.Equal(1u, exported.Frames[1].Id);
                Assert.Equal(5u, exported.Frames[1].Wait);
                Assert.Equal(0u, exported.Frames[2].Id);
                Assert.Equal(9u, exported.Frames[2].Wait);
            });
        }

        [Fact]
        public void Import_FE8U_Defender_RoundTrip_PrefixIsDefenderTemplate()
        {
            byte[] defenderDmp = ReadDmp("skillanimtemplate_defender_2017_01_24.dmp");
            WithFE8U((rom, slot) =>
            {
                // Leading D line → defender template.
                string[] script =
                {
                    "D #is defender",
                    "S0042",
                    "2 g000.png",
                };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider);
                Assert.Equal("", err);

                uint animeAddr = rom.p32(slot);

                // GUARD C: PREFIX == the DEFENDER .dmp bytes VERBATIM.
                for (int i = 0; i < defenderDmp.Length; i++)
                    Assert.Equal(defenderDmp[i], rom.Data[(uint)animeAddr + (uint)i]);

                // Export SkipCode resolves AnimeType.D (defender) on re-read.
                var exported = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeAddr);
                Assert.Equal("", exported.Error);
                Assert.True(exported.IsDefender);
                Assert.Equal(0x42u, exported.SoundId);
                Assert.Single(exported.Frames);
                Assert.Equal(0u, exported.Frames[0].Id);
                Assert.Equal(2u, exported.Frames[0].Wait);
            });
        }

        [Fact]
        public void Import_FE8U_AttackPrefixDiffersFromDefenderPrefix()
        {
            // Sanity: the two templates are distinct, so the direction-specific
            // prefix asserts above are meaningful (not a constant the test would
            // pass for either template).
            byte[] attack = ReadDmp("skillanimtemplate_2016_11_04.dmp");
            byte[] defender = ReadDmp("skillanimtemplate_defender_2017_01_24.dmp");
            Assert.False(attack.Length == defender.Length &&
                AreEqual(attack, defender));
        }

        [Fact]
        public void Import_FE8U_StructuralLayout_ConfigAfterTemplate()
        {
            byte[] attackDmp = ReadDmp("skillanimtemplate_2016_11_04.dmp");
            WithFE8U((rom, slot) =>
            {
                string[] script = { "S0042", "2 g000.png", "4 g001.png" };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider);
                Assert.Equal("", err);

                uint animeAddr = rom.p32(slot);
                // The 5-word config block begins immediately AFTER the template.
                uint cfg = animeAddr + (uint)attackDmp.Length;

                uint framesOff   = rom.p32(cfg + 0);
                uint pallistOff  = rom.p32(cfg + 12);
                uint soundId     = rom.u32(cfg + 16);

                // sound_id stored RAW: 0x0042, NOT 0x08000042.
                Assert.Equal(0x0042u, soundId);
                // frames terminator after 2 frames (2*4 bytes) is 0xFFFF,0xFFFF.
                Assert.Equal(0xFFFFu, (uint)rom.u16(framesOff + 8));
                Assert.Equal(0xFFFFu, (uint)rom.u16(framesOff + 10));
                // Palette region is RAW 0x20 (not LZ77 header 0x10).
                uint pal0 = rom.p32(pallistOff + 0);
                Assert.NotEqual(0x10, rom.Data[pal0]);
                for (int i = 0; i < 0x20; i++)
                    Assert.Equal((byte)i, rom.Data[pal0 + (uint)i]);
            });
        }

        [Fact]
        public void Import_FE8U_ForcedFaultMidWrite_LeavesRomByteIdentical()
        {
            WithFE8U((rom, slot) =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                uint slotBefore = rom.u32(slot);

                string[] script = { "S0001", "1 g000.png", "2 g001.png" };

                // Fault fires after the first frame's regions are written but
                // before the slot is repointed; the template prepend happens later
                // (it is part of mainData, the LAST op), so it opens NO extra
                // partial-write window. Snapshot-restore must roll everything back.
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, script, slot, FakeProvider,
                    faultInjector: () => throw new InvalidOperationException("injected fault"));

                Assert.NotEqual("", err);
                Assert.Equal(slotBefore, rom.u32(slot));
                Assert.Equal(before.Length, rom.Data.Length);
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] != rom.Data[i])
                        Assert.Fail($"ROM byte {i} changed: {before[i]:X2} -> {rom.Data[i]:X2}");
                }
            });
        }

        [Fact]
        public void Import_FE8U_MissingTemplate_CleanError_NoMutation()
        {
            // GUARD A: point BaseDirectory at a dir with NO config/patch2/FE8U/skill
            // .dmp → the pre-mutation template read fails → clean no-mutation error.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            var prevUndo = CoreState.Undo;
            var prevBase = CoreState.BaseDirectory;
            try
            {
                ROM rom = MakeFE8URom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                CoreState.Undo = new Undo();
                CoreState.BaseDirectory = Path.Combine(Path.GetTempPath(),
                    "fe8u_missing_dmp_" + Guid.NewGuid().ToString("N"));

                byte[] before = (byte[])rom.Data.Clone();
                uint slot = 0x300u;

                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, new[] { "1 g000.png" }, slot, FakeProvider);

                Assert.NotEqual("", err);
                Assert.Contains("template", err, StringComparison.OrdinalIgnoreCase);
                // ZERO bytes mutated — the failure is BEFORE the snapshot/mutate.
                Assert.Equal(before.Length, rom.Data.Length);
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], rom.Data[i]);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
                CoreState.BaseDirectory = prevBase;
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

        // ---------------------------------------------------------------
        // #914 — old-region recycle on re-import
        // ---------------------------------------------------------------

        // A zero/garbage slot enumerates EMPTY: this is the no-op path that keeps
        // every pre-existing zero-slot test regression-safe.
        [Fact]
        public void EnumerateOldAnimeRegions_ZeroSlot_ReturnsEmpty()
        {
            WithFE8J((rom, slot) =>
            {
                // The slot at 0x300 points at 0 in a fresh synthetic ROM.
                Assert.Equal(0u, rom.p32(slot));
                var pool = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot));
                Assert.Empty(pool);
                // Garbage (mid-ROM but no valid anime config there) also empties.
                var pool2 = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, 0xABCDEFu);
                // Either pre-walk guard rejects it (empty) or it bails partial; in
                // a zero ROM the config pointers are 0 (unsafe) so it returns empty.
                Assert.Empty(pool2);
            });
        }

        // Seed a REAL old anime (import A) so the recycle pool is NON-EMPTY before
        // the re-import (closes the false-green hole #5), then prove the recycle
        // actually reclaims via the DIFFERENTIAL len(on) < len(off).
        [Fact]
        public void Import_FE8J_SecondImport_NonEmptyPool_AndRecyclesNoLeak()
        {
            string[] scriptA =
            {
                "S001A",
                "3 g000.png",
                "5 g001.png",
                "9 g000.png",
            };

            // Non-empty-pool assertion: after seeding A, the slot's anime region
            // enumerates a populated recycle pool (so the recycle path can never
            // silently no-op and pass trivially).
            WithFE8J((rom, slot) =>
            {
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, scriptA, slot, FakeProvider);
                Assert.Equal("", err);
                var pool = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot));
                Assert.True(pool.Count > 0, "recycle pool must be NON-EMPTY after seeding a real old anime");
            });

            // DIFFERENTIAL: run the SAME A -> A re-import sequence twice, once with
            // recycle ON and once OFF, on two independently-constrained ROMs. The
            // recycle-ON ROM reuses the freed old-A region (no resize); the
            // recycle-OFF ROM must fresh-allocate into a ROM with no free space
            // left, forcing a resize. So len(on) < len(off).
            long lenOn = RunConstrainedReimport(scriptA, recycleOldRegion: true);
            long lenOff = RunConstrainedReimport(scriptA, recycleOldRegion: false);
            Assert.True(lenOn < lenOff,
                $"recycle should reclaim: len(on)={lenOn} must be < len(off)={lenOff}");
        }

        // After a recycle re-import of a DIFFERENT script B, a re-export must still
        // reproduce B exactly (logical correctness independent of placement).
        [Fact]
        public void Import_FE8J_SecondImport_RoundTripStillCorrect()
        {
            WithFE8J((rom, slot) =>
            {
                // Seed A first.
                string[] scriptA = { "S0011", "2 g000.png", "4 g001.png" };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptA, slot, FakeProvider));
                Assert.True(SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot)).Count > 0);

                // Recycle re-import of a DIFFERENT script B (3 frames, dedup 0,1,0).
                string[] scriptB =
                {
                    "S002B",
                    "6 b000.png",
                    "7 b001.png",
                    "8 b000.png",
                };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, scriptB, slot, FakeProvider, recycleOldRegion: true);
                Assert.Equal("", err);

                // Re-export reproduces B.
                uint animeAddr = rom.p32(slot);
                var exported = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeAddr);
                Assert.Equal("", exported.Error);
                Assert.Equal(0x2Bu, exported.SoundId);
                Assert.Equal(3, exported.Frames.Count);
                Assert.Equal(0u, exported.Frames[0].Id); Assert.Equal(6u, exported.Frames[0].Wait);
                Assert.Equal(1u, exported.Frames[1].Id); Assert.Equal(7u, exported.Frames[1].Wait);
                Assert.Equal(0u, exported.Frames[2].Id); Assert.Equal(8u, exported.Frames[2].Wait);
            });
        }

        // #885 across recycle: seed a real anime A, assert the pool is non-empty,
        // then a recycle:true import of B with a forced fault must leave rom.Data
        // BYTE-IDENTICAL (length AND every byte) to the pre-B snapshot. Proves the
        // in-place restore reverts the recycle-read + new-allocate + fault.
        [Fact]
        public void Import_FE8J_ForcedFault_WithPopulatedRecycle_ByteIdentical()
        {
            WithFE8J((rom, slot) =>
            {
                // Seed A.
                string[] scriptA = { "S0001", "1 g000.png", "2 g001.png" };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptA, slot, FakeProvider));
                Assert.True(SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot)).Count > 0,
                    "pool must be populated so the fault test exercises the recycle path");

                byte[] before = (byte[])rom.Data.Clone();
                uint slotBefore = rom.u32(slot);

                string[] scriptB = { "S0099", "3 b000.png", "4 b001.png", "5 b002.png" };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, scriptB, slot, FakeProvider,
                    faultInjector: () => throw new InvalidOperationException("injected fault"),
                    recycleOldRegion: true);

                Assert.NotEqual("", err);
                Assert.Equal(slotBefore, rom.u32(slot));
                Assert.Equal(before.Length, rom.Data.Length);
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] != rom.Data[i])
                        Assert.Fail($"ROM byte {i} changed: {before[i]:X2} -> {rom.Data[i]:X2}");
                }
            });
        }

        // BULK envelope (#923/#885): drive ImportSkillAnimation(manageSnapshot:false,
        // recycleOldRegion:true) under a CALLER-owned snapshot + ROM.BeginUndoScope,
        // seed a real anime, force a fault, and assert the caller's single outer
        // in-place (length-aware) restore leaves the ROM byte-identical.
        [Fact]
        public void Import_Bulk_ForcedFault_WithPopulatedRecycle_ByteIdentical()
        {
            WithFE8J((rom, slot) =>
            {
                // Seed A under a normal (managed) single import.
                string[] scriptA = { "S0001", "1 g000.png", "2 g001.png" };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptA, slot, FakeProvider));
                Assert.True(SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot)).Count > 0,
                    "pool must be populated so the bulk fault exercises the recycle path");

                // Caller-owned snapshot + ambient undo scope (the #923 envelope).
                byte[] snap = (byte[])rom.Data.Clone();
                var bulkUndo = CoreState.Undo != null
                    ? CoreState.Undo.NewUndoData("BulkForcedFaultTest")
                    : new Undo.UndoData();

                string fault = null;
                try
                {
                    using (ROM.BeginUndoScope(bulkUndo))
                    {
                        // recycleOldRegion:true here exercises enumeration-read +
                        // recycle-writes under the caller's scope; manageSnapshot:false
                        // means THIS method does NOT clone/restore — the caller does.
                        string[] scriptB = { "S0099", "3 b000.png", "4 b001.png", "5 b002.png" };
                        string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                            rom, scriptB, slot, FakeProvider,
                            faultInjector: () => throw new InvalidOperationException("injected bulk fault"),
                            manageSnapshot: false,
                            recycleOldRegion: true);
                        if (!string.IsNullOrEmpty(err)) fault = err;
                    }
                }
                catch (Exception ex)
                {
                    fault = "bulk threw: " + ex.Message;
                }

                Assert.NotNull(fault); // a fault DID occur

                // The caller's single outer, length-aware in-place restore.
                if (rom.Data.Length != snap.Length)
                    rom.write_resize_data((uint)snap.Length);
                Array.Copy(snap, rom.Data, snap.Length);

                Assert.Equal(snap.Length, rom.Data.Length);
                for (int i = 0; i < snap.Length; i++)
                {
                    if (snap[i] != rom.Data[i])
                        Assert.Fail($"ROM byte {i} changed after bulk restore: {snap[i]:X2} -> {rom.Data[i]:X2}");
                }
            });
        }

        // FE8U variant: recycle works ACROSS the per-skill program-template prefix.
        // The pool must be non-empty (SkipCode skips the prefix to find the config),
        // and the re-import round-trips.
        [Fact]
        public void Import_FE8U_SecondImport_RecyclesAcrossTemplatePrefix()
        {
            WithFE8U((rom, slot) =>
            {
                // Seed A (attack template — no D line).
                string[] scriptA =
                {
                    "S001A",
                    "3 g000.png",
                    "5 g001.png",
                    "9 g000.png",
                };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptA, slot, FakeProvider));

                // The pool is NON-EMPTY across the template prefix (SkipCode skips it).
                var pool = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot));
                Assert.True(pool.Count > 0, "FE8U recycle pool must be non-empty across the template prefix");

                // Recycle re-import of a DIFFERENT script B round-trips correctly.
                string[] scriptB = { "S0042", "2 b000.png", "4 b001.png" };
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, scriptB, slot, FakeProvider, recycleOldRegion: true);
                Assert.Equal("", err);

                uint animeAddr = rom.p32(slot);
                var exported = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animeAddr);
                Assert.Equal("", exported.Error);
                Assert.False(exported.IsDefender);
                Assert.Equal(0x42u, exported.SoundId);
                Assert.Equal(2, exported.Frames.Count);
                Assert.Equal(0u, exported.Frames[0].Id); Assert.Equal(2u, exported.Frames[0].Wait);
                Assert.Equal(1u, exported.Frames[1].Id); Assert.Equal(4u, exported.Frames[1].Wait);
            });
        }

        // ---------------------------------------------------------------
        // #929 — cross-slot shared-region refcount + exclude-aware enumeration
        // ---------------------------------------------------------------

        // B1: a SINGLE slot whose anime REUSES a frame id (so EnumerateOldAnime-
        // Regions emits the same Address.Addr more than once) must count as a
        // SINGLE owner — refcount == 1 for every one of its regions, NOT >1.
        // Otherwise the bulk would wrongly treat a deduped frame as "shared" and
        // recycle would become a near no-op.
        [Fact]
        public void BuildSkillAnimeRegionRefcount_RepeatedFrameInOneSlot_CountsAsSingleOwner()
        {
            WithFE8J((rom, slot) =>
            {
                // 3 frames, two referencing the SAME PNG -> ids 0,1,0. Frame id 0
                // is reused, so its per-frame regions appear TWICE in the raw
                // enumeration for this one slot.
                string[] script = { "S001A", "3 g000.png", "5 g001.png", "9 g000.png" };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, script, slot, FakeProvider));

                // The raw enumeration DOES contain a duplicate Addr (proves the
                // dedup is load-bearing, not vacuously satisfied).
                var regions = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot));
                var distinct = new HashSet<uint>();
                int dupes = 0;
                foreach (var r in regions) { if (!distinct.Add(r.Addr)) dupes++; }
                Assert.True(dupes > 0, "the reused-id slot must emit at least one duplicate region Addr");

                // One slot at index 0; the rest of the table is empty. EVERY region
                // is owned by exactly ONE slot -> count == 1 (never 2 for the
                // reused frame's regions).
                uint animeBase = slot; // single-slot table starts at the slot addr.
                var refcount = SkillSystemsAnimeImportCore.BuildSkillAnimeRegionRefcount(rom, animeBase, 1);
                Assert.NotEmpty(refcount);
                foreach (var kv in refcount)
                    Assert.Equal(1, kv.Value);
            });
        }

        // Two slots sharing a region (slot 1's palette-list entry redirected to
        // slot 0's palette region) -> that region's refcount == 2; every other
        // region (unshared) == 1.
        [Fact]
        public void BuildSkillAnimeRegionRefcount_TwoSlotsSharingRegion_CountsTwo()
        {
            WithTwoSlotTable((rom, animeBase, slot0, slot1, sharedPalRegion) =>
            {
                var refcount = SkillSystemsAnimeImportCore.BuildSkillAnimeRegionRefcount(rom, animeBase, 2);

                // The shared palette region is owned by BOTH slots -> count 2.
                Assert.True(refcount.TryGetValue(sharedPalRegion, out int sharedCount),
                    "the shared palette region must appear in the refcount");
                Assert.Equal(2, sharedCount);

                // At least one OTHER region exists and is unshared (count 1); no
                // region exceeds 2.
                int unsharedSeen = 0;
                foreach (var kv in refcount)
                {
                    Assert.True(kv.Value <= 2, $"no region may exceed 2 owners (addr 0x{kv.Key:X} = {kv.Value})");
                    if (kv.Key != sharedPalRegion)
                    {
                        Assert.Equal(1, kv.Value);
                        unsharedSeen++;
                    }
                }
                Assert.True(unsharedSeen > 0, "there must be at least one unshared (count==1) region");
            });
        }

        // #932 review: BuildSkillAnimeRegionRefcount must normalize animeBase via
        // U.toOffset so a caller may pass a GBA pointer (0x08xxxxxx) base, not just
        // a ROM offset. Without normalization, slot = animeBase + 4*i would carry
        // the 0x08000000 base, the isSafetyOffset(slot+3) guard would reject EVERY
        // slot, and the refcount would come back EMPTY -> the shared-region
        // exclusion safety silently disabled. Same two-slots-sharing-a-region
        // scenario as BuildSkillAnimeRegionRefcount_TwoSlotsSharingRegion_CountsTwo,
        // but the base is passed as a GBA pointer; the result must be IDENTICAL.
        [Fact]
        public void BuildSkillAnimeRegionRefcount_AcceptsGbaPointerBase_NormalizesToOffset()
        {
            WithTwoSlotTable((rom, animeBase, slot0, slot1, sharedPalRegion) =>
            {
                // Offset-base reference result (the existing, proven-correct path).
                var byOffset = SkillSystemsAnimeImportCore.BuildSkillAnimeRegionRefcount(rom, animeBase, 2);

                // Same call but the base is a GBA pointer (0x08xxxxxx). U.toPointer
                // is the canonical offset->pointer conversion; assert it really set
                // the 0x08000000 base so the test proves the normalization, not a
                // no-op.
                uint gbaBase = U.toPointer(animeBase);
                Assert.Equal(animeBase | 0x08000000u, gbaBase);
                var byPointer = SkillSystemsAnimeImportCore.BuildSkillAnimeRegionRefcount(rom, gbaBase, 2);

                // The safety is NOT silently disabled: the shared region is still
                // counted with exactly 2 owners.
                Assert.True(byPointer.TryGetValue(sharedPalRegion, out int sharedCount),
                    "the shared palette region must appear in the pointer-base refcount");
                Assert.Equal(2, sharedCount);

                // The pointer-base result is IDENTICAL to the offset-base result
                // (same keys, same per-region owner counts) -> toOffset normalized
                // the GBA pointer back to the offset the offset-base path used.
                Assert.NotEmpty(byPointer);
                Assert.Equal(byOffset.Count, byPointer.Count);
                foreach (var kv in byOffset)
                {
                    Assert.True(byPointer.TryGetValue(kv.Key, out int c),
                        $"offset-base addr 0x{kv.Key:X} missing from pointer-base refcount");
                    Assert.Equal(kv.Value, c);
                }
            });
        }

        // The exclude-aware overload must DROP every excluded address — covering
        // BOTH a per-frame region AND a config/list block — while KEEPING the
        // non-excluded regions.
        [Fact]
        public void EnumerateOldAnimeRegions_WithExclude_SkipsSharedPerFrameAndConfigBlocks()
        {
            WithFE8J((rom, slot) =>
            {
                string[] script = { "S0042", "2 g000.png", "4 g001.png" };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, script, slot, FakeProvider));

                uint cfg = rom.p32(slot);
                uint framesOff   = rom.p32(cfg + 0);   // a CONFIG/LIST block addr.
                uint pallistOff  = rom.p32(cfg + 12);
                uint pal0Region  = rom.p32(pallistOff + 0); // a PER-FRAME region addr.

                // Full pool (no exclude) contains both addresses.
                var full = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot), null);
                Assert.Contains(full, a => a.Addr == U.toOffset(framesOff));
                Assert.Contains(full, a => a.Addr == U.toOffset(pal0Region));

                // Exclude one per-frame region (pal0) AND one config/list block
                // (the frames table) -> both absent; everything else present.
                var exclude = new HashSet<uint> { U.toOffset(pal0Region), U.toOffset(framesOff) };
                var filtered = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot), exclude);

                Assert.DoesNotContain(filtered, a => a.Addr == U.toOffset(pal0Region));
                Assert.DoesNotContain(filtered, a => a.Addr == U.toOffset(framesOff));
                // The filtered pool is strictly smaller but still non-empty (other
                // regions — tsalist/imagelist/palettelist/pointer + other frames —
                // survive).
                Assert.True(filtered.Count > 0, "non-excluded regions must survive");
                Assert.True(filtered.Count < full.Count, "excluding regions must shrink the pool");

                // A SECOND pal region (id 1) that was NOT excluded is still present.
                uint pal1Region = rom.p32(pallistOff + 4);
                Assert.Contains(filtered, a => a.Addr == U.toOffset(pal1Region));
            });
        }

        // ===============================================================
        // Helpers
        // ===============================================================

        // Build a 2-slot FE8J anime table where slot 1's anime shares slot 0's
        // palette region (id-0 palette). Returns (rom, animeBase, slot0, slot1,
        // sharedPalRegionAddr). The shared region is created by REDIRECTING slot
        // 1's palette-list[0] entry to slot 0's palette region — the canonical
        // "different pointer slots referencing the same bytes" sharing case.
        static void WithTwoSlotTable(Action<ROM, uint, uint, uint, uint> body)
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

                // A contiguous 2-entry anime table at 0x400 (each slot a u32).
                uint animeBase = 0x400u;
                uint slot0 = animeBase + 0;
                uint slot1 = animeBase + 4;

                // Import two DISTINCT animes into the two slots.
                string[] scriptA = { "S001A", "3 a000.png", "5 a001.png" };
                string[] scriptB = { "S002B", "2 b000.png", "4 b001.png" };
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptA, slot0, FakeProvider));
                Assert.Equal("", SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptB, slot1, FakeProvider));

                // Slot 0's palette region (id 0).
                uint cfg0 = rom.p32(slot0);
                uint pallist0Off = rom.p32(cfg0 + 12);
                uint slot0PalRegion = U.toOffset(rom.p32(pallist0Off + 0));

                // Redirect slot 1's palette-list[0] entry to slot 0's palette
                // region -> the two slots now SHARE that one palette region.
                uint cfg1 = rom.p32(slot1);
                uint pallist1Off = rom.p32(cfg1 + 12);
                rom.write_p32(pallist1Off + 0, U.toPointer(slot0PalRegion));

                // Sanity: slot 1 really does reference slot 0's palette now.
                Assert.Equal(slot0PalRegion, U.toOffset(rom.p32(pallist1Off + 0)));

                body(rom, animeBase, slot0, slot1, slot0PalRegion);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
            }
        }

        // Differential helper: import script A into a fresh FE8J ROM, then CONSTRAIN
        // the ROM so the ONLY recyclable/free space is the old-A region itself, then
        // re-import A with the given recycle flag and return the final rom.Data
        // length. With recycle ON the freed old-A region is reused (no resize); with
        // recycle OFF the import must fresh-allocate into a ROM with no free space,
        // forcing a resize -> a larger length.
        static long RunConstrainedReimport(string[] scriptA, bool recycleOldRegion)
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
                uint slot = 0x300u;

                // Seed A.
                string err = SkillSystemsAnimeImportCore.ImportSkillAnimation(rom, scriptA, slot, FakeProvider);
                Assert.Equal("", err);

                // Snapshot the post-A ROM, then enumerate A's recyclable regions.
                byte[] afterA = (byte[])rom.Data.Clone();
                var pool = SkillSystemsAnimeImportCore.EnumerateOldAnimeRegions(rom, rom.p32(slot));
                Assert.True(pool.Count > 0);

                // Fill the ROM body (from 0x100, past the GBA header) with a
                // non-free byte (0x55: neither 0x00 nor 0xFF, so FindFreeSpace skips
                // it), wiping ALL free space. Then restore EXACTLY A's regions + the
                // slot from afterA so (a) the enumeration during the re-import
                // re-derives the same pool and (b) the recycle-ON path has those
                // regions to reuse, while the recycle-OFF path finds NO free space
                // anywhere and must resize (so its final length is strictly larger).
                for (int i = 0x100; i < rom.Data.Length; i++)
                    rom.Data[i] = 0x55;
                RestoreRange(rom, afterA, slot, 4);
                foreach (var a in pool)
                    RestoreRange(rom, afterA, a.Addr, (int)a.Length);

                // Re-import A with the recycle flag under test (single-import path).
                string err2 = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, scriptA, slot, FakeProvider, recycleOldRegion: recycleOldRegion);
                Assert.Equal("", err2);

                // Return the final length. recycle ON reuses (most of) the freed
                // old-A region so it grows by at most a little padding slack;
                // recycle OFF must fresh-allocate the WHOLE footprint into a ROM
                // with no free space, growing by far more. The caller asserts the
                // differential len(on) < len(off) (the robust, padding-tolerant
                // assertion the #914 review endorsed over brittle strict equality).
                return rom.Data.Length;
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
            }
        }

        // Copy [addr, addr+len) from src into rom.Data (bounds-clamped).
        static void RestoreRange(ROM rom, byte[] src, uint addr, int len)
        {
            if (len <= 0) return;
            // ROM size always fits int, so int indexing is safe here.
            int end = (int)Math.Min((long)addr + len, rom.Data.Length);
            for (int i = (int)addr; i < end; i++)
                rom.Data[i] = src[i];
        }

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

        // Run an action with a synthetic FE8U ROM wired into CoreState and
        // BaseDirectory pointed at the repo root (so the FE8U program template
        // .dmp reads resolve to config/patch2/FE8U/skill/). Slot at 0x300.
        static void WithFE8U(Action<ROM, uint> body)
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            var prevUndo = CoreState.Undo;
            var prevBase = CoreState.BaseDirectory;
            try
            {
                ROM rom = MakeFE8URom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                CoreState.Undo = new Undo();
                CoreState.BaseDirectory = FindRepoRoot();

                uint slot = 0x300u;
                body(rom, slot);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevSvc;
                CoreState.Undo = prevUndo;
                CoreState.BaseDirectory = prevBase;
            }
        }

        // Read a real FE8U skill program template .dmp from the checked-out
        // config/patch2 submodule. Throws an explicit message if the submodule
        // is missing so CI surfaces the dependency clearly.
        static byte[] ReadDmp(string name)
        {
            string path = Path.Combine(FindRepoRoot(), "config", "patch2", "FE8U", "skill", name);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    "config/patch2 submodule not checked out — needed for the FE8U "
                    + "skill-anime import tests. Run: git submodule update --init config/patch2");
            }
            return File.ReadAllBytes(path);
        }

        static bool AreEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static string FindRepoRoot()
        {
            string asm = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(asm);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate FEBuilderGBA.sln from " + asm);
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
