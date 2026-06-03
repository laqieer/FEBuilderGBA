// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the cross-platform BULK-ATOMIC SkillSystems skill-config import
    /// seam (SLICE 2 of #923 / #885), with the plan's 3 HIGH + M/L fixes:
    ///
    ///   * Round-trip (bulk-export -> bulk-import -> re-export reproduces the TSV
    ///     + per-skill anims). applyRecycle:false (M2) so converted IDs don't
    ///     break the comparison.
    ///   * H1+H3 — FORCED-FAILURE MID-BULK -> ROM BYTE-IDENTICAL INCL LENGTH: a
    ///     3-skill bulk where skill 0's import GROWS rom.Data and skill 1's import
    ///     is fault-injected. The length-aware restore down-resizes back to the
    ///     snapshot length AND every byte matches; ZERO undo records pushed. Plus
    ///     a success case that pushes exactly ONE undo record.
    ///   * H2 — a skill import that RETURNS a non-empty error (NOT a throw) — a
    ///     stateful provider that passes the bulk validate pass then returns null
    ///     in the inner import's own validate -> ImportSkillAnimation RETURNS the
    ///     "cannot load" string -> bulk restores byte-identical.
    ///   * M1 — textID==0 row -> slot NOT written (preserved). applyRecycle:true
    ///     converts per the lookup.
    ///   * Guards — rom-identity, NOT_FOUND location, bad pointer location, bad
    ///     TSV, >16-colour PNG, missing FE8U template -> NO mutation.
    ///
    /// [Collection("SharedState")] because the tests mutate CoreState.ROM /
    /// CoreState.ImageService / CoreState.Undo / CoreState.BaseDirectory.
    /// </summary>
    [Collection("SharedState")]
    public class SkillConfigSkillSystemBulkImportCoreTests : IDisposable
    {
        // Synthetic ROM layout (all offsets are ROM offsets, not GBA pointers).
        // 32MB so that FindFreeSpace's search-start (Data.Length/2 == 0x01000000)
        // == the FE8 extends boundary (extends_address 0x09000000 -> offset
        // 0x01000000). Fresh anime allocations therefore land in the EXTENDED
        // area, which the export seam requires to re-render them.
        const uint ROM_SIZE = 0x02000000u;
        const uint TEXT_BASE = 0x1000u;           // text table base (u16 per skill).
        const uint ANIME_BASE = 0x2000u;          // anime table base (u32 per skill).
        const uint TEXT_PTR_LOC = 0x300u;         // u32 pointer LOCATION -> TEXT_BASE.
        const uint ANIME_PTR_LOC = 0x304u;        // u32 pointer LOCATION -> ANIME_BASE.

        readonly ROM _prevRom;
        readonly IImageService _prevSvc;
        readonly Undo _prevUndo;
        readonly string _prevBase;
        // #925 thread 2: track EVERY temp dir created by MakeTempDir so Dispose
        // deletes them all (the old single-field version leaked all but the first).
        readonly List<string> _tempDirs = new List<string>();

        public SkillConfigSkillSystemBulkImportCoreTests()
        {
            _prevRom = CoreState.ROM;
            _prevSvc = CoreState.ImageService;
            _prevUndo = CoreState.Undo;
            _prevBase = CoreState.BaseDirectory;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.ImageService = _prevSvc;
            CoreState.Undo = _prevUndo;
            CoreState.BaseDirectory = _prevBase;
            foreach (string d in _tempDirs)
            {
                if (d != null && Directory.Exists(d))
                {
                    try { Directory.Delete(d, true); } catch { /* swallow */ }
                }
            }
        }

        // ===============================================================
        // Round-trip (M2: applyRecycle:false)
        // ===============================================================

        [Fact]
        public void BulkImport_RoundTrip_ReproducesTsvAndAnims()
        {
            ROM rom = SetupRom();
            string dir = MakeTempDir();

            // Two skills. Skill 0 has an anime (will be imported into extended
            // area); skill 1 has textID only, animePtr 0.
            WriteAnimeDir(dir, 0, new[]
            {
                "S001A",
                "3 g000.png",
                "5 g001.png",
                "9 g000.png",
            });

            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                // textID  animePtr  (animePtr non-zero/extended for skill 0 so the
                // bulk attempts the per-skill anime import for it).
                U.ToHexString(0x123u) + "\t" + U.ToHexString(0x09000000u),
                U.ToHexString(0x456u) + "\t" + U.ToHexString(0x0u),
            });

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);
            Assert.Equal("", err);

            // textIDs landed verbatim (recycle off).
            Assert.Equal(0x123u, (uint)rom.u16(TEXT_BASE + 0));
            Assert.Equal(0x456u, (uint)rom.u16(TEXT_BASE + 2));

            // Skill 0's anime slot was repointed into the extended area.
            uint anime0 = rom.p32(ANIME_BASE + 0);
            Assert.True(anime0 >= U.toOffset(rom.RomInfo.extends_address),
                $"anime0 0x{anime0:X} should be in extended area");
            // Skill 1's anime slot was explicitly cleared to 0.
            Assert.Equal(0u, rom.p32(ANIME_BASE + 4));

            // Re-export and assert the TSV + skill-0 frames reproduce.
            string outDir = MakeTempDir();
            string outTsv = Path.Combine(outDir, "out.SkillConfig.tsv");
            var captured = new Dictionary<uint, SkillConfigBulkAnimeEntry>();
            string expErr = SkillConfigSkillSystemBulkExportCore.ExportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, outTsv,
                entry => captured[entry.Index] = entry);
            Assert.Equal("", expErr);

            string[] outLines = File.ReadAllLines(outTsv);
            Assert.True(outLines.Length >= 2);
            // Row 0: textID 0x123, animePtr == the extended anime0 (the export
            // seam writes rom.p32(slot), i.e. the OFFSET, matching what the import
            // re-derefs).
            string[] r0 = outLines[0].Split('\t');
            Assert.Equal(0x123u, U.atoh(r0[0]));
            Assert.Equal(anime0, U.atoh(r0[1]));
            // Row 1: textID 0x456, animePtr 0.
            string[] r1 = outLines[1].Split('\t');
            Assert.Equal(0x456u, U.atoh(r1[0]));
            Assert.Equal(0u, U.atoh(r1[1]));

            // Skill 0's re-exported animation reproduces the (id,wait) sequence.
            Assert.True(captured.ContainsKey(0));
            var res = captured[0].Result;
            Assert.Equal("", res.Error);
            Assert.Equal(0x1Au, res.SoundId);
            Assert.Equal(3, res.Frames.Count);
            Assert.Equal(0u, res.Frames[0].Id); Assert.Equal(3u, res.Frames[0].Wait);
            Assert.Equal(1u, res.Frames[1].Id); Assert.Equal(5u, res.Frames[1].Wait);
            Assert.Equal(0u, res.Frames[2].Id); Assert.Equal(9u, res.Frames[2].Wait);
        }

        // ===============================================================
        // #925 thread 1 — per-skill anime dir scoping in BOTH passes.
        // ===============================================================

        [Fact]
        public void BulkImport_TwoDistinctAnimeDirs_SameNamedPngs_ScopedPerSkill()
        {
            ROM rom = SetupRom();
            string dir = MakeTempDir();

            // TWO skills, each with its OWN anime{i}/ dir but the SAME PNG name
            // ("g000.png") in each — with DISTINCT content per dir. If the import
            // failed to scope PNGs per-skill it would load skill 1's frames from
            // skill 0's dir (or a shared/last dir) -> cross-dir leakage.
            WriteAnimeDir(dir, 0, new[] { "S0001", "1 g000.png" });
            WriteAnimeDir(dir, 1, new[] { "S0002", "1 g000.png" });

            // Materialize a DISTINCT marker file per skill dir so the provider can
            // read the actual scoped path and report which dir it loaded from.
            string dir0 = Path.Combine(dir, "anime" + U.ToHexString(0u));
            string dir1 = Path.Combine(dir, "anime" + U.ToHexString(1u));
            File.WriteAllText(Path.Combine(dir0, "g000.png"), "SKILL0");
            File.WriteAllText(Path.Combine(dir1, "g000.png"), "SKILL1");

            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x100u) + "\t" + U.ToHexString(0x09000000u),
                U.ToHexString(0x200u) + "\t" + U.ToHexString(0x09000000u),
            });

            // Recording provider: capture every scoped path it is asked to load,
            // and return a frame whose distinguishing pixel encodes WHICH marker
            // file (skill 0 vs skill 1) actually lives at that path. This proves
            // the path reached the CORRECT per-skill dir end-to-end.
            var requested = new List<string>();
            SkillSystemsAnimeImportCore.ImageProvider recording = scopedPath =>
            {
                requested.Add(scopedPath);
                string marker = File.Exists(scopedPath) ? File.ReadAllText(scopedPath) : "";
                var idx = new byte[8 * 8];
                idx[0] = (byte)(marker == "SKILL0" ? 3 : marker == "SKILL1" ? 4 : 0);
                return (idx, 8, 8, MakePalette());
            };

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), recording, applyRecycle: false);
            Assert.Equal("", err);

            // Every requested path for skill 0 lives under anime0/ and resolves to
            // the SKILL0 marker; likewise skill 1 under anime1/ -> SKILL1.
            var skill0Paths = requested.FindAll(p => p.Contains(
                Path.DirectorySeparatorChar + "anime" + U.ToHexString(0u) + Path.DirectorySeparatorChar));
            var skill1Paths = requested.FindAll(p => p.Contains(
                Path.DirectorySeparatorChar + "anime" + U.ToHexString(1u) + Path.DirectorySeparatorChar));

            Assert.NotEmpty(skill0Paths);
            Assert.NotEmpty(skill1Paths);
            // No path may be scoped to the WRONG dir or an unscoped basedir-only
            // location (the cross-dir leakage the bug would have caused).
            foreach (string p in requested)
            {
                Assert.True(File.Exists(p), $"scoped path should exist: {p}");
                string content = File.ReadAllText(p);
                Assert.True(content == "SKILL0" || content == "SKILL1",
                    $"path resolved to neither per-skill marker: {p} -> '{content}'");
            }
            foreach (string p in skill0Paths) Assert.Equal("SKILL0", File.ReadAllText(p));
            foreach (string p in skill1Paths) Assert.Equal("SKILL1", File.ReadAllText(p));
        }

        // ===============================================================
        // H1 + H3 — forced-failure mid-bulk after a resize -> byte-identical.
        // ===============================================================

        [Fact]
        public void BulkImport_ForcedFaultMidBulk_AfterResize_LeavesRomByteIdenticalInclLength()
        {
            ROM rom = SetupBusyRom();
            string dir = MakeTempDir();

            // 3 skills, ALL with anime so the loop reaches skill 1. Skill 0's
            // import GROWS rom.Data (the busy 0x55 fill forces a tail resize).
            // The faultInjector
            // fires on EVERY per-skill import's first frame; to make skill 0
            // COMMIT (grow) and skill 1 FAULT, we count invocations and only
            // throw on the SECOND skill's first frame.
            for (uint i = 0; i < 3; i++)
            {
                WriteAnimeDir(dir, i, new[] { "S0001", "1 g000.png", "2 g001.png" });
            }
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x111u) + "\t" + U.ToHexString(0x09000000u),
                U.ToHexString(0x222u) + "\t" + U.ToHexString(0x09000000u),
                U.ToHexString(0x333u) + "\t" + U.ToHexString(0x09000000u),
            });

            byte[] before = (byte[])rom.Data.Clone();
            int snapLength = rom.Data.Length;

            // faultInjector fires once per ImportSkillAnimation (at its i==0). The
            // bulk threads the SAME delegate into each skill, so this counter
            // distinguishes skill 0 (commit + grow) from skill 1 (fault).
            int skillImportCount = 0;
            Action fault = () =>
            {
                skillImportCount++;
                if (skillImportCount == 2) // second skill -> fault mid-write.
                    throw new InvalidOperationException("injected mid-bulk fault");
            };

            int undoBefore = UndoBufferCount();

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false, faultInjector: fault);

            Assert.NotEqual("", err);
            // The fault must have fired AFTER skill 0 grew the ROM (so the test
            // genuinely exercises an actual resize, not a no-resize path).
            Assert.True(skillImportCount >= 2, "the fault should fire on skill 1's import");

            // H1: length restored to the pre-bulk snapshot length...
            Assert.Equal(snapLength, rom.Data.Length);
            // ...and every byte identical (skill 0's committed writes rolled back).
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != rom.Data[i])
                    Assert.Fail($"ROM byte {i} changed: {before[i]:X2} -> {rom.Data[i]:X2}");
            }

            // H3: ZERO net undo records pushed on a fault.
            Assert.Equal(undoBefore, UndoBufferCount());
        }

        [Fact]
        public void BulkImport_Success_PushesExactlyOneUndoRecord()
        {
            ROM rom = SetupRom();
            string dir = MakeTempDir();
            WriteAnimeDir(dir, 0, new[] { "S0001", "1 g000.png" });
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x111u) + "\t" + U.ToHexString(0x09000000u),
                U.ToHexString(0x222u) + "\t" + U.ToHexString(0x0u),
            });

            int undoBefore = UndoBufferCount();
            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);
            Assert.Equal("", err);

            // Exactly ONE undo record for the whole bulk.
            Assert.Equal(undoBefore + 1, UndoBufferCount());
        }

        // ===============================================================
        // H2 — inner import RETURNS a non-empty error (not a throw).
        // ===============================================================

        [Fact]
        public void BulkImport_InnerImportReturnsError_RestoresByteIdentical()
        {
            ROM rom = SetupRom();
            string dir = MakeTempDir();
            WriteAnimeDir(dir, 0, new[] { "S0001", "1 g000.png", "2 g001.png" });
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x111u) + "\t" + U.ToHexString(0x09000000u),
            });

            byte[] before = (byte[])rom.Data.Clone();
            int snapLength = rom.Data.Length;

            // Stateful provider: succeed for the bulk's validate pass (so M3
            // validation passes), then return null on a LATER call (the inner
            // ImportSkillAnimation re-runs its OWN validate -> imageProvider) so
            // the inner import RETURNS a non-empty "cannot load" string rather
            // than throwing. Two unique PNGs are validated by the bulk (2 calls),
            // so we start failing from the 3rd call onward.
            int call = 0;
            SkillSystemsAnimeImportCore.ImageProvider stateful = name =>
            {
                call++;
                if (call <= 2) return MakeFrame(); // bulk validate (g000,g001).
                return null;                        // inner import validate -> error.
            };

            int undoBefore = UndoBufferCount();
            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), stateful, applyRecycle: false);

            Assert.NotEqual("", err);
            // Byte-identical restore (proves return-value fault detection).
            Assert.Equal(snapLength, rom.Data.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], rom.Data[i]);
            Assert.Equal(undoBefore, UndoBufferCount());
        }

        // ===============================================================
        // M1 — textID==0 preserved; recycle conversion.
        // ===============================================================

        [Fact]
        public void BulkImport_TextIdZero_SlotNotWritten()
        {
            ROM rom = SetupRom();
            // Pre-seed a sentinel into slot 0 so we can assert it is preserved.
            rom.write_u16(TEXT_BASE + 0, 0xBEEF);

            string dir = MakeTempDir();
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                // textID 0 -> slot must NOT be written (M1).
                U.ToHexString(0x0u) + "\t" + U.ToHexString(0x0u),
            });

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);
            Assert.Equal("", err);

            // The sentinel survives — a textID-0 row preserves the existing slot.
            Assert.Equal(0xBEEFu, (uint)rom.u16(TEXT_BASE + 0));
        }

        [Fact]
        public void BulkImport_ApplyRecycle_ConvertsTextId()
        {
            ROM rom = SetupRom();
            string dir = MakeTempDir();
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            // Skill index 0x90 with textID 0xEB1 -> recycled to 0xF72 (Vengeance).
            // We need that row at index 0x90, so pad the TSV up to 0x91 rows.
            var lines = new List<string>();
            for (uint i = 0; i <= 0x90; i++)
            {
                if (i == 0x90)
                    lines.Add(U.ToHexString(0xEB1u) + "\t" + U.ToHexString(0x0u));
                else
                    lines.Add(U.ToHexString(0x0u) + "\t" + U.ToHexString(0x0u));
            }
            File.WriteAllLines(tsv, lines);

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: true);
            Assert.Equal("", err);

            // Slot 0x90 holds the RECYCLED textID 0xF72, not 0xEB1.
            Assert.Equal(0xF72u, (uint)rom.u16(TEXT_BASE + 2 * 0x90));
        }

        // Pure-lookup sanity (the recycle table is verbatim from WF).
        [Fact]
        public void SkillTextIDRecycle_KnownMappings()
        {
            Assert.Equal(0xF72u, SkillConfigSkillTextIDRecycle.Convert(0x90, 0xEB1));
            Assert.Equal(0xF48u, SkillConfigSkillTextIDRecycle.Convert(0xF9, 0xF42));
            Assert.Equal(0xE9Eu, SkillConfigSkillTextIDRecycle.Convert(0x7F, 0xF65));
            // No rule -> identity.
            Assert.Equal(0x1234u, SkillConfigSkillTextIDRecycle.Convert(0x01, 0x1234));
            // Right skill, wrong textID -> identity.
            Assert.Equal(0xAAAu, SkillConfigSkillTextIDRecycle.Convert(0x90, 0xAAA));
        }

        // ===============================================================
        // FE8U template path.
        // ===============================================================

        [Fact]
        public void BulkImport_FE8U_WithTemplate_RoundTrips()
        {
            // Needs the config/patch2 submodule (the FE8U .dmp templates).
            string repoRoot = FindRepoRoot();
            string templatePath = Path.Combine(repoRoot, "config", "patch2", "FE8U", "skill",
                "skillanimtemplate_2016_11_04.dmp");
            Assert.True(File.Exists(templatePath),
                "config/patch2 submodule not checked out — run: git submodule update --init config/patch2");

            ROM rom = SetupRom(fe8u: true);
            CoreState.BaseDirectory = repoRoot;

            string dir = MakeTempDir();
            WriteAnimeDir(dir, 0, new[] { "S0042", "2 g000.png" });
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x100u) + "\t" + U.ToHexString(0x09000000u),
            });

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);
            Assert.Equal("", err);

            uint anime0 = rom.p32(ANIME_BASE + 0);
            byte[] tmpl = File.ReadAllBytes(templatePath);
            // The written anime region's PREFIX == the attack .dmp verbatim.
            for (int i = 0; i < tmpl.Length; i++)
                Assert.Equal(tmpl[i], rom.Data[anime0 + (uint)i]);
        }

        [Fact]
        public void BulkImport_FE8U_MissingTemplate_CleanError_NoMutation()
        {
            ROM rom = SetupRom(fe8u: true);
            // BaseDirectory with NO config/patch2/FE8U/skill .dmp -> validate fails.
            CoreState.BaseDirectory = Path.Combine(Path.GetTempPath(),
                "fe8u_bulk_missing_" + Guid.NewGuid().ToString("N"));

            string dir = MakeTempDir();
            WriteAnimeDir(dir, 0, new[] { "S0001", "1 g000.png" });
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x100u) + "\t" + U.ToHexString(0x09000000u),
            });

            byte[] before = (byte[])rom.Data.Clone();
            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);

            Assert.NotEqual("", err);
            Assert.Contains("template", err, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before.Length, rom.Data.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], rom.Data[i]);
        }

        // ===============================================================
        // Guards — all no-mutation.
        // ===============================================================

        [Fact]
        public void BulkImport_ForeignRom_Refused_NoMutation()
        {
            ROM active = SetupRom();
            ROM foreign = MakeRom("BE8J01", ROM_SIZE, fillBusy: false);
            byte[] before = (byte[])foreign.Data.Clone();

            string dir = MakeTempDir();
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[] { U.ToHexString(0x1u) + "\t" + U.ToHexString(0x0u) });

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                foreign, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);

            Assert.NotEqual("", err);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], foreign.Data[i]);
        }

        [Fact]
        public void BulkImport_NotFoundLocation_Refused_NoMutation()
        {
            ROM rom = SetupRom();
            byte[] before = (byte[])rom.Data.Clone();
            string dir = MakeTempDir();
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[] { U.ToHexString(0x1u) + "\t" + U.ToHexString(0x0u) });

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, U.NOT_FOUND, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);

            Assert.NotEqual("", err);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], rom.Data[i]);
        }

        [Fact]
        public void BulkImport_BadPointerLocation_Refused_NoMutation()
        {
            ROM rom = SetupRom();
            byte[] before = (byte[])rom.Data.Clone();
            string dir = MakeTempDir();
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[] { U.ToHexString(0x1u) + "\t" + U.ToHexString(0x0u) });

            // A pointer location past the end of the ROM -> isSafetyOffset fails.
            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, ROM_SIZE + 0x100u, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);

            Assert.NotEqual("", err);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], rom.Data[i]);
        }

        [Fact]
        public void BulkImport_Over16ColorPng_Rejected_NoMutation()
        {
            ROM rom = SetupRom();
            string dir = MakeTempDir();
            WriteAnimeDir(dir, 0, new[] { "S0001", "1 g000.png" });
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                U.ToHexString(0x100u) + "\t" + U.ToHexString(0x09000000u),
            });

            byte[] before = (byte[])rom.Data.Clone();
            // Provider returns a pixel with index 16 (>15 -> 4bpp violation).
            SkillSystemsAnimeImportCore.ImageProvider bad = name =>
            {
                var idx = new byte[8 * 8];
                idx[0] = 16;
                return (idx, 8, 8, MakePalette());
            };

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), bad, applyRecycle: false);

            Assert.NotEqual("", err);
            Assert.Contains("16 colours", err);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], rom.Data[i]);
        }

        [Fact]
        public void BulkImport_MalformedTsvRow_Skipped()
        {
            ROM rom = SetupRom();
            rom.write_u16(TEXT_BASE + 0, 0xCAFE); // sentinel for the bad row.

            string dir = MakeTempDir();
            string tsv = Path.Combine(dir, "skills.SkillConfig.tsv");
            File.WriteAllLines(tsv, new[]
            {
                "onlyonefield",                                       // < 2 fields -> skipped (L1).
                U.ToHexString(0x222u) + "\t" + U.ToHexString(0x0u),   // valid row 1.
            });

            string err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                rom, TEXT_PTR_LOC, ANIME_PTR_LOC, tsv,
                DirResolver(dir), Provider(dir), applyRecycle: false);
            Assert.Equal("", err);

            // Row 0 was malformed -> slot 0 preserved.
            Assert.Equal(0xCAFEu, (uint)rom.u16(TEXT_BASE + 0));
            // Row 1 applied.
            Assert.Equal(0x222u, (uint)rom.u16(TEXT_BASE + 2));
        }

        // ===============================================================
        // Helpers
        // ===============================================================

        ROM SetupRom(bool fe8u = false)
        {
            ROM rom = MakeRom(fe8u ? "BE8E01" : "BE8J01", ROM_SIZE, fillBusy: false);
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();
            CoreState.Undo = new Undo();
            return rom;
        }

        // A 16MB ROM whose alloc-search region (everything except the low tables
        // + pointer-locations) is filled with a NON-free 0x55 so FindFreeSpace
        // fails in both halves and a fresh anime allocation must RESIZE + append
        // at the tail — exercising an ACTUAL rom.Data growth (needed for the H1
        // length-aware restore test).
        ROM SetupBusyRom()
        {
            ROM rom = MakeRom("BE8J01", 0x01000000u, fillBusy: true);
            CoreState.ROM = rom;
            CoreState.ImageService = new StubImageService();
            CoreState.Undo = new Undo();
            return rom;
        }

        // A synthetic ROM. When fillBusy is false the body is 0x00 (free), and on
        // a 32MB ROM FindFreeSpace's search-start (0x01000000) == the extends
        // boundary so allocations land in the EXTENDED area. When fillBusy is true
        // the body is 0x55 (busy) except the low tables, forcing a resize-grow.
        // The text/anime pointer LOCATIONS hold GBA pointers to the table bases.
        static ROM MakeRom(string signature, uint size, bool fillBusy)
        {
            var data = new byte[size];
            if (fillBusy)
            {
                for (uint a = 0; a < size; a++) data[a] = 0x55;
            }
            var rom = new ROM();
            rom.LoadLow("synthetic_skillbulk.gba", data, signature);
            // Re-clear the low table + pointer-location region to 0x00 so the
            // tables read clean and the pointer locations are writable.
            if (fillBusy)
            {
                for (uint a = 0; a < 0x4000u; a++) rom.write_u8(a, 0x00);
            }
            rom.write_p32(TEXT_PTR_LOC, U.toPointer(TEXT_BASE));
            rom.write_p32(ANIME_PTR_LOC, U.toPointer(ANIME_BASE));
            return rom;
        }

        static Func<uint, string> DirResolver(string basedir)
            => i => Path.Combine(basedir, "anime" + U.ToHexString(i));

        // A provider that resolves frame PNGs against the per-skill dir is not
        // needed for synthetic frames — the bytes are deterministic regardless of
        // the name. We just return a fixed 8x8 frame.
        static SkillSystemsAnimeImportCore.ImageProvider Provider(string basedir)
            => name => MakeFrame();

        static (byte[] indexedPixels, int width, int height, byte[] gbaPalette)? MakeFrame()
        {
            var idx = new byte[8 * 8];
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

        static void WriteAnimeDir(string basedir, uint index, string[] scriptLines)
        {
            string dir = Path.Combine(basedir, "anime" + U.ToHexString(index));
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "anime.txt"), scriptLines);
        }

        string MakeTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(),
                "skillbulk_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            // #925 thread 2: track EVERY created temp dir so Dispose deletes them
            // all (some tests call MakeTempDir twice — e.g. the round-trip out dir).
            _tempDirs.Add(d);
            return d;
        }

        static int UndoBufferCount()
            => CoreState.Undo != null ? CoreState.Undo.UndoBuffer.Count : 0;

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
    }
}
