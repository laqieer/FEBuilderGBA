// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the READ-ONLY cross-platform SkillSystems bulk-export seam
    /// (SLICE 1 of #920), <see cref="SkillConfigSkillSystemBulkExportCore"/>.
    ///
    /// Synthetic-ROM strategy (no real .gba file needed): a FE8J ROM big enough
    /// to host an extended-area anime (ROM length > extends_address offset
    /// 0x01000000). We plant:
    ///   * a text-pointer LOCATION holding a GBA pointer to the text base,
    ///   * an anime-pointer LOCATION holding a GBA pointer to the anime base,
    ///   * a 3-row text table (u16 textIDs),
    ///   * a 3-row anime table (u32 pointers): row 0 = 0 (no anime), row 1 =
    ///     a NON-extended pointer (skipped by writeAnime), row 2 = a fully
    ///     built EXTENDED-area anime (invokes writeAnime).
    ///
    /// Asserts: TSV has `count` rows with the right textID/animePtr hex; the
    /// extended row invokes the writeAnime delegate with a non-empty frame list
    /// + the `anime{i:hex}` dir name; READ-ONLY (rom.Data byte-identical); and
    /// the guard paths (NOT_FOUND location, foreign ROM, unsafe base) return a
    /// clean error with no mutation.
    ///
    /// [Collection("SharedState")] because the tests mutate CoreState.ROM /
    /// CoreState.ImageService.
    /// </summary>
    [Collection("SharedState")]
    public sealed class SkillConfigSkillSystemBulkExportCoreTests : IDisposable
    {
        readonly ROM _prevRom;
        readonly IImageService _prevSvc;

        public SkillConfigSkillSystemBulkExportCoreTests()
        {
            _prevRom = CoreState.ROM;
            _prevSvc = CoreState.ImageService;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.ImageService = _prevSvc;
        }

        // ROM length: 0x1100000 (> extends offset 0x01000000) so we can plant an
        // anime at 0x01000000+ that counts as extended.
        const int ROM_LEN = 0x1100000;
        const uint TEXT_LOC = 0x200;   // pointer LOCATION holding text base ptr
        const uint ANIME_LOC = 0x210;  // pointer LOCATION holding anime base ptr
        const uint TEXT_BASE = 0x800;  // u16 text table
        const uint ANIME_BASE = 0x900; // u32 anime table
        const uint ROW_COUNT = 3;

        // The three textIDs and the three anime pointers (offsets) we plant.
        static readonly uint[] TEXT_IDS = { 0x0101, 0x0202, 0x0303 };
        const uint NON_EXTENDED_ANIME = 0x00400000; // < extends offset, not exported
        const uint EXTENDED_ANIME = 0x01000000;     // == extends offset, exported

        [Fact]
        public void ExportAll_WritesTsvRows_AndDelegatesExtendedAnime()
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();

            // Plant the extended-area anime (a full FE8J config the merged
            // export seam can walk) at EXTENDED_ANIME.
            BuildSyntheticAnime(rom.Data, EXTENDED_ANIME,
                waits: new uint[] { 4, 6 }, soundId: 0x2B);

            byte[] before = (byte[])rom.Data.Clone();

            string tsvPath = Path.Combine(Path.GetTempPath(),
                "skillconfig_bulk_" + Guid.NewGuid().ToString("N") + ".SkillConfig.tsv");

            var delegated = new List<SkillConfigBulkAnimeEntry>();
            try
            {
                string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                    rom, TEXT_LOC, ANIME_LOC, tsvPath, e => delegated.Add(e));

                Assert.Equal("", err);

                // READ-ONLY: not a single byte of the ROM changed.
                Assert.Equal(before, rom.Data);

                // TSV: WF DataCount is the `i < 255` predicate (cap 255), so the
                // count equals MAX_COUNT on this oversized ROM. The first three
                // rows hold our planted values; rows 3..254 are zero-filled.
                string[] lines = File.ReadAllLines(tsvPath);
                Assert.Equal(SkillConfigSkillSystemBulkExportCore.MAX_COUNT, lines.Length);

                uint[] plantedPtrs = { 0u, NON_EXTENDED_ANIME, EXTENDED_ANIME };
                for (int i = 0; i < (int)ROW_COUNT; i++)
                {
                    string[] cols = lines[i].Split('\t');
                    Assert.Equal(2, cols.Length);
                    Assert.Equal(U.ToHexString(TEXT_IDS[i]), cols[0]);
                    Assert.Equal(U.ToHexString(plantedPtrs[i]), cols[1]);
                }
                // A zero-filled row past our table reads textID=0, animePtr=0.
                Assert.Equal(U.ToHexString(0u) + "\t" + U.ToHexString(0u), lines[10]);

                // Only the extended-area row (index 2) was delegated.
                Assert.Single(delegated);
                var entry = delegated[0];
                Assert.Equal(2u, entry.Index);
                Assert.Equal("anime" + U.ToHexString(2u), entry.AnimeDirName);
                Assert.Equal("", entry.Result.Error);
                Assert.Equal(2, entry.Result.Frames.Count);
                Assert.Equal(0x2Bu, entry.Result.SoundId);
            }
            finally
            {
                TryDelete(tsvPath);
            }
        }

        [Fact]
        public void ExportAll_NullWriteAnime_StillWritesTsv_NoThrow()
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();
            BuildSyntheticAnime(rom.Data, EXTENDED_ANIME, new uint[] { 1 }, 0);

            byte[] before = (byte[])rom.Data.Clone();
            string tsvPath = Path.Combine(Path.GetTempPath(),
                "skillconfig_bulk_" + Guid.NewGuid().ToString("N") + ".SkillConfig.tsv");
            try
            {
                string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                    rom, TEXT_LOC, ANIME_LOC, tsvPath, null);
                Assert.Equal("", err);
                Assert.Equal(before, rom.Data); // READ-ONLY
                Assert.Equal(SkillConfigSkillSystemBulkExportCore.MAX_COUNT, File.ReadAllLines(tsvPath).Length);
            }
            finally { TryDelete(tsvPath); }
        }

        [Fact]
        public void ExportAll_NotFoundLocation_ReturnsCleanError_NoMutation()
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();
            byte[] before = (byte[])rom.Data.Clone();

            string tsvPath = Path.Combine(Path.GetTempPath(),
                "skillconfig_bulk_" + Guid.NewGuid().ToString("N") + ".SkillConfig.tsv");

            string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                rom, U.NOT_FOUND, ANIME_LOC, tsvPath, _ => { });

            Assert.NotEqual("", err);
            Assert.Contains("patch", err, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, rom.Data);
            Assert.False(File.Exists(tsvPath)); // never wrote the TSV
        }

        [Fact]
        public void ExportAll_ForeignRom_Refused()
        {
            ROM active = MakeRom();
            ROM foreign = MakeRom();
            CoreState.ROM = active;
            CoreState.ImageService = new StubImageService();

            string tsvPath = Path.Combine(Path.GetTempPath(),
                "skillconfig_bulk_" + Guid.NewGuid().ToString("N") + ".SkillConfig.tsv");

            string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                foreign, TEXT_LOC, ANIME_LOC, tsvPath, _ => { });

            Assert.NotEqual("", err);
            Assert.False(File.Exists(tsvPath));
        }

        [Theory]
        [InlineData(0u)]                 // zero location
        [InlineData(0xFFFFFFFEu)]        // huge location (past EOF, != NOT_FOUND)
        public void ExportAll_UnsafePointerLocation_ReturnsCleanError_NoMutation(uint badLoc)
        {
            // #922 review thread 1: an invalid (or zero) pointer LOCATION must be
            // caught BEFORE the p32 deref, returning a clean error with no TSV.
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();
            byte[] before = (byte[])rom.Data.Clone();

            string tsvPath = Path.Combine(Path.GetTempPath(),
                "skillconfig_bulk_" + Guid.NewGuid().ToString("N") + ".SkillConfig.tsv");

            // Bad TEXT location (anime location still valid).
            string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                rom, badLoc, ANIME_LOC, tsvPath, _ => { });
            Assert.NotEqual("", err);
            Assert.Contains("location", err, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, rom.Data);
            Assert.False(File.Exists(tsvPath));

            // Bad ANIME location (text location still valid) — same guard.
            string err2 = SkillConfigSkillSystemBulkExportCore.ExportAll(
                rom, TEXT_LOC, badLoc, tsvPath, _ => { });
            Assert.NotEqual("", err2);
            Assert.Contains("location", err2, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, rom.Data);
            Assert.False(File.Exists(tsvPath));
        }

        [Fact]
        public void ExportAll_UnsafeTextBase_ReturnsError()
        {
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();

            // TEXT_LOC currently points to TEXT_BASE; overwrite it with a tiny
            // (below-safety-floor) base so isSafetyOffset(textBase) fails.
            WriteU32(rom.Data, TEXT_LOC, ToPtr(0x10));

            string tsvPath = Path.Combine(Path.GetTempPath(),
                "skillconfig_bulk_" + Guid.NewGuid().ToString("N") + ".SkillConfig.tsv");

            string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                rom, TEXT_LOC, ANIME_LOC, tsvPath, _ => { });

            Assert.NotEqual("", err);
            Assert.False(File.Exists(tsvPath));
        }

        // ===============================================================
        // Helpers
        // ===============================================================

        static ROM MakeRom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic_fe8j_bulk.gba", new byte[ROM_LEN], "BE8J01");

            // pointer LOCATIONS → bases.
            WriteU32(rom.Data, TEXT_LOC, ToPtr(TEXT_BASE));
            WriteU32(rom.Data, ANIME_LOC, ToPtr(ANIME_BASE));

            // text table (3 u16 ids) — non-zero so getBlockDataCount keeps going;
            // the count cap is the i<255 predicate. We plant a 0xFFFF? No — WF
            // count is purely the i<255 predicate, so the table can be any bytes.
            for (uint i = 0; i < ROW_COUNT; i++)
                WriteU16(rom.Data, TEXT_BASE + 2 * i, (int)TEXT_IDS[i]);

            // anime table (3 u32 pointers): 0, non-extended, extended.
            WriteU32(rom.Data, ANIME_BASE + 0, 0u);
            WriteU32(rom.Data, ANIME_BASE + 4, ToPtr(NON_EXTENDED_ANIME));
            WriteU32(rom.Data, ANIME_BASE + 8, ToPtr(EXTENDED_ANIME));

            return rom;
        }

        // Build a complete FE8J anime structure at `config` and return it.
        // Mirrors SkillSystemsAnimeExportCoreTests.BuildSyntheticAnime but takes
        // an explicit config offset so we can place it in the extended area.
        static void BuildSyntheticAnime(byte[] data, uint config, uint[] waits, uint soundId)
        {
            uint frames = config + 0x100;
            uint graphiclist = config + 0x200;
            uint tsalist = config + 0x300;
            uint palettelist = config + 0x400;
            uint objLz = config + 0x1000;
            uint tsaLz = config + 0x2000;
            uint palOff = config + 0x3000;

            WriteU32(data, config + 0, ToPtr(frames));
            WriteU32(data, config + 4, ToPtr(tsalist));
            WriteU32(data, config + 8, ToPtr(graphiclist));
            WriteU32(data, config + 12, ToPtr(palettelist));
            WriteU32(data, config + 16, soundId);

            WriteU32(data, graphiclist + 0, ToPtr(objLz));
            WriteU32(data, tsalist + 0, ToPtr(tsaLz));
            WriteU32(data, palettelist + 0, ToPtr(palOff));

            uint fp = frames;
            for (int i = 0; i < waits.Length; i++)
            {
                WriteU16(data, fp + 0, 0);
                WriteU16(data, fp + 2, (ushort)waits[i]);
                fp += 4;
            }
            WriteU16(data, fp + 0, 0xFFFF);
            WriteU16(data, fp + 2, 0xFFFF);

            PlantZeroLZ77(data, objLz, 0x800);
            PlantZeroLZ77(data, tsaLz, 120);
            PlantPalette(data, palOff);
        }

        static uint ToPtr(uint off) => off + 0x08000000u;

        static void PlantZeroLZ77(byte[] data, uint offset, int uncompressedSize)
        {
            data[offset + 0] = 0x10;
            data[offset + 1] = (byte)(uncompressedSize & 0xFF);
            data[offset + 2] = (byte)((uncompressedSize >> 8) & 0xFF);
            data[offset + 3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int remaining = uncompressedSize;
            int pos = (int)offset + 4;
            while (remaining > 0 && pos + 1 < data.Length)
            {
                int count = remaining < 8 ? remaining : 8;
                data[pos++] = 0x00;
                for (int k = 0; k < count && pos < data.Length; k++, remaining--)
                    data[pos++] = 0x00;
            }
        }

        static void PlantPalette(byte[] data, uint offset)
        {
            for (int i = 0; i < 16; i++)
            {
                ushort color = (i == 0) ? (ushort)0 : (ushort)0x7FFF;
                data[offset + i * 2 + 0] = (byte)(color & 0xFF);
                data[offset + i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }
        }

        static void WriteU16(byte[] data, uint offset, int value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void WriteU32(byte[] data, uint offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }
}
